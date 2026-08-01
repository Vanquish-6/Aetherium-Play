using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace AcLegacyLauncher;

internal sealed record RunningProgramIdentity(
    int ProcessId,
    string ProcessName,
    string? MainWindowTitle,
    string? ImageName,
    string? OriginalFilename,
    string? CompanyName,
    string? FileDescription,
    string? ProductName);

internal sealed class ClientAntiTamperContainment : IDisposable
{
    private Action? closeJob;
    private int shutdownState;

    internal ClientAntiTamperContainment(Action closeJob)
    {
        this.closeJob = closeJob;
    }

    internal bool IsIntentionalStopRequested => Volatile.Read(ref shutdownState) == 1;

    internal void StopIntentionally()
    {
        Interlocked.CompareExchange(ref shutdownState, 1, 0);
        Dispose();
    }

    internal bool TryBeginViolation() =>
        Interlocked.CompareExchange(ref shutdownState, 2, 0) == 0;

    public void Dispose() => Interlocked.Exchange(ref closeJob, null)?.Invoke();
}

internal sealed class ClientAntiTamperRuntimeGuard : IDisposable
{
    private readonly Process clientProcess;
    private readonly NativeClientDddAccelerationInstallation installation;
    private readonly ClientAntiTamperContainment containment;
    private readonly Action<string>? violationObserver;

    internal ClientAntiTamperRuntimeGuard(
        Process clientProcess,
        NativeClientDddAccelerationInstallation installation,
        ClientAntiTamperContainment containment,
        Action<string>? violationObserver)
    {
        this.clientProcess = clientProcess;
        this.installation = installation;
        this.containment = containment;
        this.violationObserver = violationObserver;
    }

    internal string Detail =>
        "A09 anti-tamper active (running-program identity, client-hook integrity, " +
        "and client kill-on-launcher-exit containment; local only).";

    internal void VerifyNow() =>
        ClientAntiTamper.EnforceRuntimeState(
            clientProcess,
            installation,
            containment,
            displayWarnings: false,
            violationObserver: violationObserver);

    public void Dispose() => containment.StopIntentionally();
}

/// <summary>
/// Transparent, user-mode deterrence for common Cheat Engine builds. The guard
/// examines identity metadata for active programs only. It does not enumerate
/// directories, inspect arbitrary modules, upload a process list, or stop any
/// process other than the client instance launched by Aetherium Play.
/// </summary>
internal static class ClientAntiTamper
{
    internal const int ScanIntervalMilliseconds = 2_000;

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int InitialImagePathCapacity = 1024;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitSilentBreakawayOk = 0x0000_1000;
    private const uint JobObjectLimitKillOnJobClose = 0x0000_2000;
    private const int MaxVersionIdentityCacheEntries = 2_048;
    private static readonly ConcurrentDictionary<string, ActiveImageVersionIdentity>
        VersionIdentityCache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr process,
        uint flags,
        StringBuilder executableName,
        ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(
        IntPtr jobAttributes,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        IntPtr job,
        IntPtr process);

    internal static void EnsureNoKnownMemoryEditorRunning()
    {
        var match = FindKnownMemoryEditor(CaptureRunningPrograms());
        if (match is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Cheat Engine appears to be running ({match.ProcessName}, PID {match.ProcessId}). " +
            "Aetherium Play did not close it. Close it yourself and try Play again. " +
            "This check examines identity metadata for active programs only; it does not " +
            "scan directories or send a process list.");
    }

    internal static RunningProgramIdentity? FindKnownMemoryEditor(
        IEnumerable<RunningProgramIdentity> runningPrograms)
    {
        ArgumentNullException.ThrowIfNull(runningPrograms);
        return runningPrograms.FirstOrDefault(IsKnownCheatEngineIdentity);
    }

    internal static bool IsKnownCheatEngineIdentity(RunningProgramIdentity program)
    {
        ArgumentNullException.ThrowIfNull(program);

        if (IsKnownExecutableMarker(program.ProcessName) ||
            IsKnownExecutableMarker(program.ImageName) ||
            IsKnownExecutableMarker(program.OriginalFilename))
        {
            return true;
        }

        return IsKnownCheatEngineWindowTitle(program.MainWindowTitle) ||
               IsKnownVersionMarker(program.CompanyName) ||
               IsKnownVersionMarker(program.FileDescription) ||
               IsKnownVersionMarker(program.ProductName);
    }

    internal static ClientAntiTamperRuntimeGuard StartRuntimeMonitor(
        Process clientProcess,
        NativeClientDddAccelerationInstallation installation,
        ClientAntiTamperContainment containment,
        bool displayWarnings = true,
        Action<string>? violationObserver = null)
    {
        ArgumentNullException.ThrowIfNull(clientProcess);
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(containment);

        try
        {
            // Verify synchronously before the primary client thread is resumed.
            // The job handle is already live, so forcibly ending the launcher
            // cannot strand an admitted client without its monitor.
            VerifyRuntimeState(clientProcess, installation);

            var monitor = new Thread(
                () => MonitorClient(
                    clientProcess,
                    installation,
                    containment,
                    displayWarnings,
                    violationObserver))
            {
                IsBackground = true,
                Name = $"Aetherium client integrity monitor {clientProcess.Id}",
            };
            monitor.SetApartmentState(ApartmentState.STA);
            monitor.Start();

            return new ClientAntiTamperRuntimeGuard(
                clientProcess,
                installation,
                containment,
                violationObserver);
        }
        catch
        {
            // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE also guarantees cleanup if
            // monitor startup itself fails.
            containment.Dispose();
            throw;
        }
    }

    internal static ClientAntiTamperContainment CreateRuntimeContainment(
        IntPtr suspendedProcessHandle)
    {
        if (suspendedProcessHandle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "A suspended client process handle is required.",
                nameof(suspendedProcessHandle));
        }

        var clientJob = CreateKillOnCloseClientJob(suspendedProcessHandle);
        return new ClientAntiTamperContainment(clientJob.Dispose);
    }

    internal static void VerifyRuntimeState(
        Process clientProcess,
        NativeClientDddAccelerationInstallation installation)
    {
        EnsureNoKnownMemoryEditorRunning();
        NativeClientDddAcceleration.VerifyInstalled(clientProcess.Handle, installation);
    }

    internal static void EnforceRuntimeState(
        Process clientProcess,
        NativeClientDddAccelerationInstallation installation,
        ClientAntiTamperContainment containment,
        bool displayWarnings,
        Action<string>? violationObserver)
    {
        try
        {
            VerifyRuntimeState(clientProcess, installation);
        }
        catch (Exception error)
        {
            if (!containment.IsIntentionalStopRequested &&
                !HasExited(clientProcess) &&
                containment.TryBeginViolation())
            {
                EndClientForViolation(
                    clientProcess,
                    containment,
                    error.Message,
                    displayWarnings,
                    violationObserver);
            }

            throw;
        }
    }

    private static void MonitorClient(
        Process clientProcess,
        NativeClientDddAccelerationInstallation installation,
        ClientAntiTamperContainment containment,
        bool displayWarnings,
        Action<string>? violationObserver)
    {
        try
        {
            while (true)
            {
                if (containment.IsIntentionalStopRequested || HasExited(clientProcess))
                {
                    return;
                }

                try
                {
                    VerifyRuntimeState(clientProcess, installation);
                }
                catch (Exception error)
                {
                    if (containment.IsIntentionalStopRequested || HasExited(clientProcess))
                    {
                        return;
                    }

                    if (!containment.TryBeginViolation())
                    {
                        return;
                    }

                    EndClientForViolation(
                        clientProcess,
                        containment,
                        error.Message,
                        displayWarnings,
                        violationObserver);
                    return;
                }

                if (clientProcess.WaitForExit(ScanIntervalMilliseconds))
                {
                    return;
                }
            }
        }
        catch (Exception error)
        {
            if (!containment.IsIntentionalStopRequested &&
                !HasExited(clientProcess) &&
                containment.TryBeginViolation())
            {
                EndClientForViolation(
                    clientProcess,
                    containment,
                    $"The A09 integrity monitor failed: {error.Message}",
                    displayWarnings,
                    violationObserver);
            }
        }
        finally
        {
            containment.Dispose();
        }
    }

    private static IReadOnlyList<RunningProgramIdentity> CaptureRunningPrograms()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                "Aetherium Play could not inspect active program identities.",
                error);
        }

        var identities = new List<RunningProgramIdentity>(processes.Length);
        foreach (var process in processes)
        {
            using (process)
            {
                string processName;
                try
                {
                    processName = process.ProcessName;
                }
                catch
                {
                    // A protected process or exit race that prevents even the
                    // process name from being read is not evidence of a match.
                    continue;
                }

                var imagePath = TryGetActiveImagePath(process.Id);
                var version = GetActiveImageVersionIdentity(imagePath, process);
                identities.Add(new RunningProgramIdentity(
                    process.Id,
                    processName,
                    TryGetMainWindowTitle(process),
                    imagePath is null ? null : Path.GetFileName(imagePath),
                    version.OriginalFilename,
                    version.CompanyName,
                    version.FileDescription,
                    version.ProductName));
            }
        }

        return identities;
    }

    private static string? TryGetActiveImagePath(int processId)
    {
        var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);
        if (processHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var capacity = InitialImagePathCapacity;
            var path = new StringBuilder(capacity);
            return QueryFullProcessImageNameW(
                processHandle,
                flags: 0,
                path,
                ref capacity)
                ? path.ToString()
                : null;
        }
        finally
        {
            NativeProcess.CloseHandle(processHandle);
        }
    }

    private static string? TryGetMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            // Preserve the process-name/path evidence when a process exits or
            // denies the independent window-title query.
            return null;
        }
    }

    private static ActiveImageVersionIdentity GetActiveImageVersionIdentity(
        string? imagePath,
        Process process)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return ActiveImageVersionIdentity.Empty;
        }

        var cacheKey = TryBuildVersionCacheKey(imagePath, process);
        if (cacheKey is not null &&
            VersionIdentityCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var identity = ReadActiveImageVersionIdentity(imagePath);
        if (identity is null)
        {
            // A transient exit/access race is retried on a later scan instead
            // of permanently caching an empty identity for this image path.
            return ActiveImageVersionIdentity.Empty;
        }

        if (cacheKey is not null)
        {
            if (VersionIdentityCache.Count >= MaxVersionIdentityCacheEntries)
            {
                VersionIdentityCache.Clear();
            }

            VersionIdentityCache.TryAdd(cacheKey, identity);
        }

        return identity;
    }

    private static string? TryBuildVersionCacheKey(string imagePath, Process process)
    {
        try
        {
            // Start time separates different processes that reuse the same path;
            // file length/write time also invalidate the cache if that image is
            // replaced during a long launcher session.
            var file = new FileInfo(imagePath);
            return $"{imagePath}\0{process.Id}\0" +
                   $"{process.StartTime.ToUniversalTime().Ticks}\0" +
                   $"{file.Length}\0{file.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            // Without a stable active-file identity, read the version resource
            // again instead of risking a stale allow or stale refusal.
            return null;
        }
    }

    private static bool IsKnownExecutableMarker(string? value)
    {
        var marker = NormalizeIdentity(value);
        return marker == "cheatengine" ||
               marker.StartsWith("cheatenginex8664", StringComparison.Ordinal) ||
               marker.StartsWith("cheatenginei386", StringComparison.Ordinal) ||
               marker.StartsWith("ceserver", StringComparison.Ordinal) ||
               marker.StartsWith("cedebug", StringComparison.Ordinal);
    }

    private static bool IsKnownVersionMarker(string? value)
    {
        var marker = NormalizeIdentity(value);
        return marker.StartsWith("cheatengine", StringComparison.Ordinal);
    }

    private static bool IsKnownCheatEngineWindowTitle(string? value)
    {
        var title = value?.Trim() ?? string.Empty;
        if (title.Equals("Cheat Engine", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string Prefix = "Cheat Engine ";
        if (!title.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var version = title[Prefix.Length..];
        return version.Length != 0 &&
               version.Any(char.IsDigit) &&
               version.All(character => char.IsDigit(character) || character == '.');
    }

    private static ActiveImageVersionIdentity? ReadActiveImageVersionIdentity(string imagePath)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(imagePath);
            return new ActiveImageVersionIdentity(
                version.OriginalFilename,
                version.CompanyName,
                version.FileDescription,
                version.ProductName);
        }
        catch
        {
            // A process may exit or deny access between the active snapshot and
            // its version-resource query. The active executable path is cached
            // only in this process and is never written to the violation log.
            return null;
        }
    }

    private static string NormalizeIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileNameWithoutExtension(value.Trim());
        return new string(fileName
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            // Unknown is not confirmed exit. The monitor/job must stay alive
            // until Windows confirms that this specific client has ended.
            return false;
        }
    }

    private static void EndClientForViolation(
        Process clientProcess,
        ClientAntiTamperContainment containment,
        string reason,
        bool displayWarnings,
        Action<string>? violationObserver)
    {
        // End the contained client before any filesystem, observer, or UI work.
        // Closing the assigned job is the primary fail-closed action. It is also
        // what Windows does if AetheriumLauncher.exe is forcibly terminated.
        string? containmentFailure = null;
        try
        {
            containment.Dispose();
        }
        catch (Exception error)
        {
            containmentFailure = error.Message;
        }

        var ended = TryEndClient(clientProcess, out var endFailure);
        var processId = clientProcess.Id;
        if (!string.IsNullOrWhiteSpace(containmentFailure))
        {
            endFailure = string.IsNullOrWhiteSpace(endFailure)
                ? $"job close failed: {containmentFailure}"
                : $"job close failed: {containmentFailure}; {endFailure}";
        }

        WriteViolationLog(processId, reason);
        try
        {
            violationObserver?.Invoke(reason);
        }
        catch (Exception observerError)
        {
            WriteViolationLog(
                processId,
                $"The anti-tamper test observer failed: {observerError.Message}");
        }

        if (ended)
        {
            if (displayWarnings)
            {
                ShowWarning(
                    "Aetherium Play ended its client.exe because the A09 anti-tamper " +
                    "check failed. No other program was stopped.\n\n" + reason);
            }
            return;
        }

        var failureNotice =
            "The A09 anti-tamper check failed, but Windows did not confirm that " +
            "client.exe ended. Aetherium Play will remain active and keep trying; " +
            "close client.exe manually if it is still visible.\n\n" +
            reason + "\n\nTermination detail: " + endFailure;
        WriteViolationLog(processId, failureNotice);
        if (displayWarnings)
        {
            ShowWarningAsync(failureNotice);
        }

        // Never return to an unmonitored, already-admitted client. Normally the
        // kill-on-close job ends it immediately; direct termination is retried
        // indefinitely if Windows has not yet confirmed exit.
        while (!TryEndClient(clientProcess, out _))
        {
            Thread.Sleep(1_000);
        }

        WriteViolationLog(
            processId,
            "Windows confirmed client.exe ended after anti-tamper termination retries.");
    }

    private static bool TryEndClient(Process clientProcess, out string failure)
    {
        failure = string.Empty;
        if (HasExited(clientProcess))
        {
            return true;
        }

        try
        {
            clientProcess.Kill(entireProcessTree: false);
        }
        catch (Exception error)
        {
            failure = error.Message;
            try
            {
                if (!NativeProcess.TerminateProcess(clientProcess.Handle, 1))
                {
                    failure += $"; TerminateProcess failed (Win32 {Marshal.GetLastWin32Error()})";
                }
            }
            catch (Exception nativeError)
            {
                failure += $"; TerminateProcess could not run: {nativeError.Message}";
            }
        }

        try
        {
            if (clientProcess.WaitForExit(5_000) || HasExited(clientProcess))
            {
                return true;
            }

            failure = string.IsNullOrWhiteSpace(failure)
                ? "client.exe did not exit within five seconds"
                : failure + "; client.exe did not exit within five seconds";
        }
        catch (Exception error)
        {
            failure = string.IsNullOrWhiteSpace(failure)
                ? error.Message
                : failure + "; exit confirmation failed: " + error.Message;
        }

        return false;
    }

    private static void ShowWarningAsync(string message)
    {
        var warningThread = new Thread(() => ShowWarning(message))
        {
            IsBackground = true,
            Name = "Aetherium anti-tamper warning",
        };
        warningThread.SetApartmentState(ApartmentState.STA);
        warningThread.Start();
    }

    private static void ShowWarning(string message)
    {
        try
        {
            MessageBox.Show(
                message,
                "Aetherium Play - Anti-Tamper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch
        {
            // The local log remains available if Windows cannot display the notice.
        }
    }

    private static KillOnCloseJobHandle CreateKillOnCloseClientJob(IntPtr processHandle)
    {
        var rawJob = CreateJobObjectW(IntPtr.Zero, null);
        if (rawJob == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Aetherium Play could not create the client containment job");
        }

        var job = new KillOnCloseJobHandle(rawJob);
        try
        {
            var limits = new JobObjectExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags =
                JobObjectLimitKillOnJobClose | JobObjectLimitSilentBreakawayOk;
            if (!SetInformationJobObject(
                    rawJob,
                    JobObjectExtendedLimitInformationClass,
                    ref limits,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Aetherium Play could not configure client containment");
            }

            if (!AssignProcessToJobObject(rawJob, processHandle))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "A09 could not attach client.exe to kill-on-close containment. " +
                    "An incompatible outer Windows job or unsupported Windows version may " +
                    "prevent containment; launch from the normal desktop. The client will " +
                    "not start without this protection");
            }

            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    private static void WriteViolationLog(int processId, string reason)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AetheriumPlay");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "anti-tamper.log"),
                $"{DateTimeOffset.UtcNow:O} client_pid={processId} {reason}{Environment.NewLine}");
        }
        catch
        {
            // Logging must not mask the original anti-tamper result.
        }
    }

    private sealed record ActiveImageVersionIdentity(
        string? OriginalFilename,
        string? CompanyName,
        string? FileDescription,
        string? ProductName)
    {
        internal static ActiveImageVersionIdentity Empty { get; } = new(null, null, null, null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        internal JobObjectBasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryUsed;
        internal UIntPtr PeakJobMemoryUsed;
    }

    private sealed class KillOnCloseJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal KillOnCloseJobHandle(IntPtr jobHandle)
            : base(ownsHandle: true)
        {
            SetHandle(jobHandle);
        }

        protected override bool ReleaseHandle() => NativeProcess.CloseHandle(handle);
    }
}
