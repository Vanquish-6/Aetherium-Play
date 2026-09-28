using System.Diagnostics;

namespace AcLegacyLauncher;

public sealed class ClientLaunchResult
{
    public required string InstallDirectory { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string Arguments { get; init; }

    public required Process? Process { get; init; }

    public string SlotId { get; init; } = "1";

    public bool SeededSafeGraphics { get; init; }

    public string ResolvedDDrawPath { get; init; } = string.Empty;

    public string LaunchDetail { get; init; } = string.Empty;

    internal ClientAntiTamperRuntimeGuard? AntiTamperGuard { get; init; }
}

public static class ClientLauncher
{
    public static ClientLaunchResult Start(
        LaunchConfig config,
        string? dgVoodooToolsDirectory = null,
        bool prepareGraphics = true,
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        var (installDirectory, clientPath, expectedProfile) = ResolveValidatedLaunchTarget(config);

        // This local-only check happens before profile cleanup, graphics setup,
        // or process creation so a refused launch leaves the game untouched.
        ClientAntiTamper.EnsureNoKnownMemoryEditorRunning();

        RemoveLegacyProfileStore(report);

        config.SyncSelectedSlotToLaunchFields();
        var safeGraphics = prepareGraphics && (WineRuntime.IsWine || config.SeedSafeGraphics);
        var slotEnvironment = ClientSlotStore.Prepare(
            config.SelectedSlotId,
            installDirectory,
            safeGraphics);
        SlotIsolationInjector.EnsureDllPresent();

        var seededSafeGraphics = safeGraphics;
        string? graphicsDetail = null;
        var workspaceDirectory =
            ClientDatWorkspace.GetWorkspaceDirectory(installDirectory, config.SelectedSlotId);

        // Every live client gets a private writable DAT pair. The slot lease is
        // held for the entire process lifetime, while the install seed lease
        // serializes seed -> slot -> verified DDD -> seed promotion.
        SharedDatLaunchLease? workspaceLease = null;
        SharedDatLaunchLease? datLaunchLease = null;
        ClientDatWorkspaceInfo datWorkspace;
        try
        {
            workspaceLease =
                SharedDatLaunchGate.AcquireWhenAvailable(workspaceDirectory, cancellationToken);
            datLaunchLease =
                SharedDatLaunchGate.AcquireWhenAvailable(installDirectory, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (prepareGraphics)
            {
                GraphicsBootstrap.EnsureDirectDrawWrapper(
                    installDirectory,
                    dgVoodooToolsDirectory ?? GetRepositoryToolsDirectory());
            }

            datWorkspace = ClientDatWorkspace.PrepareFromSeed(
                installDirectory,
                config.SelectedSlotId,
                report);
        }
        catch
        {
            datLaunchLease?.Dispose();
            workspaceLease?.Dispose();
            throw;
        }

        var workingDirectory = datWorkspace.WorkingDirectory;
        clientPath = datWorkspace.ClientExePath;

        if (prepareGraphics)
        {
            GraphicsBootstrap.EnsureDirectDrawWrapper(
                workingDirectory,
                dgVoodooToolsDirectory ?? GetRepositoryToolsDirectory());
            GraphicsBootstrap.RestoreDisplayModeBefore136(workingDirectory);
            if (config.AnotherClientRunning)
            {
                GraphicsBootstrap.ApplySecondClientMouseSettings(workingDirectory);
            }

            graphicsDetail = WineRuntime.IsWine
                ? "Wine DirectDraw (dgVoodoo skipped)."
                : config.AnotherClientRunning
                    ? "Second client: private DAT workspace; CaptureMouse=false."
                    : "Private DAT workspace; slot display settings are isolated.";
        }

        var argumentParts = BuildArgumentParts(config);
        var arguments = BuildArgumentString(argumentParts);

        // Suspend → slot isolation → optional vintage Decal inject → resume.
        // portal.dat/cell.dat are private to this slot; the install-root pair is
        // never opened by a live client and is only the serialized seed.
        Process process;
        IntPtr processHandle;
        IntPtr threadHandle;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            process = NativeProcess.StartSuspendedClient(
                clientPath,
                workingDirectory,
                arguments,
                out processHandle,
                out threadHandle,
                slotEnvironment.ToProcessEnvironment());
        }
        catch
        {
            datLaunchLease?.Dispose();
            workspaceLease?.Dispose();
            throw;
        }

        NativeClientDddAccelerationInstallation? dddAcceleration = null;
        ClientAntiTamperContainment? containment = null;
        ClientAntiTamperRuntimeGuard? antiTamper = null;
        var dddAccelerationDetail = string.Empty;
        var antiTamperDetail = string.Empty;
        var vintageDecalDetail = string.Empty;
        try
        {
            // Contain the suspended stock client before any A10 marker, hook, or
            // optional injection is written. If the launcher ends at any later
            // point, Windows cannot leave a patched orphan to be resumed.
            containment = ClientAntiTamper.CreateRuntimeContainment(processHandle);

            // Install the exact-client runtime hook before any injected DLL can
            // alter the image and before the primary thread executes client code.
            dddAcceleration = NativeClientDddAcceleration.Apply(
                clientPath,
                processHandle);
            if (!ReferenceEquals(dddAcceleration.Profile, expectedProfile))
            {
                throw new InvalidDataException(
                    "The verified client profile changed while the suspended process was starting.");
            }
            dddAccelerationDetail = dddAcceleration.Detail;
            report?.Invoke(dddAccelerationDetail);

            SlotIsolationInjector.Inject(processHandle);
            var isolationDetail = $"Slot {slotEnvironment.SlotId} settings are private.";
            report?.Invoke(isolationDetail);

            var vintageDecalPackage = VintageDecalInjector.FindEnabledPackage(installDirectory);
            if (vintageDecalPackage is not null)
            {
                VintageDecalInjector.Inject(
                    vintageDecalPackage,
                    process,
                    processHandle,
                    threadHandle);
                vintageDecalDetail = "Injected vintage Decal 2.6.1.1 before client resume.";
            }

            // Optional DLL injection must leave all guarded A10 regions
            // intact. Any collision is refused while the client is suspended.
            NativeClientDddAcceleration.VerifyInstalled(
                processHandle,
                dddAcceleration);

            // Close the race between the initial scan and process setup. A hit
            // here follows the existing fail-closed path and terminates only the
            // still-suspended client launched above.
            ClientAntiTamper.EnsureNoKnownMemoryEditorRunning();

            // Start the independent launcher-resident guard while the primary
            // client thread is still suspended, so no admitted A10 client ever
            // runs without an active integrity monitor.
            antiTamper = ClientAntiTamper.StartRuntimeMonitor(
                process,
                dddAcceleration,
                containment);
            antiTamperDetail = antiTamper.Detail;
            report?.Invoke(antiTamperDetail);

            NativeProcess.ResumeAndClose(ref processHandle, ref threadHandle);

            // Close the first-interval race with a synchronous post-resume scan;
            // the resident thread then continues scan-before-wait every two seconds.
            antiTamper.VerifyNow();

            // Keep the slot's private DAT mutex for the process lifetime.
            // The seed mutex is released only after A10 freezes CLCache at a
            // fully-drained boundary, the private DAT pair is atomically
            // promoted to the install seed, and the launcher acknowledges that
            // promotion back to the client.
            SharedDatLaunchGate.ReleaseWhenProcessExits(
                workspaceLease!,
                process,
                report);
            workspaceLease = null;

            SharedDatLaunchGate.ReleaseWhenClientDatSafe(
                datLaunchLease!,
                process,
                dddAcceleration,
                report,
                onDatSafe: () =>
                {
                    try
                    {
                        ClientDatWorkspace.PromoteToSeed(
                            workingDirectory,
                            installDirectory,
                            report);
                        NativeClientDddAcceleration.MarkDatPromotionComplete(
                            process.Handle,
                            dddAcceleration);
                    }
                    catch
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                            }
                        }
                        catch
                        {
                            // The DAT seed was not acknowledged; fail closed and
                            // let the mutex remain owned until process exit.
                        }

                        throw;
                    }
                });
            datLaunchLease = null;
        }
        catch
        {
            datLaunchLease?.Dispose();
            workspaceLease?.Dispose();
            if (antiTamper is not null)
            {
                antiTamper.Dispose();
            }
            else
            {
                containment?.Dispose();
            }

            if (threadHandle != IntPtr.Zero || processHandle != IntPtr.Zero)
            {
                try
                {
                    if (processHandle != IntPtr.Zero)
                    {
                        NativeProcess.TerminateProcess(processHandle, 1);
                    }
                }
                catch
                {
                    // Best-effort cleanup.
                }

                if (threadHandle != IntPtr.Zero)
                {
                    NativeProcess.CloseHandle(threadHandle);
                }

                if (processHandle != IntPtr.Zero)
                {
                    NativeProcess.CloseHandle(processHandle);
                }
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Best-effort cleanup.
            }

            throw;
        }

        return new ClientLaunchResult
        {
            InstallDirectory = installDirectory,
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            Process = process,
            SlotId = slotEnvironment.SlotId,
            AntiTamperGuard = antiTamper,
            SeededSafeGraphics = seededSafeGraphics,
            ResolvedDDrawPath = GraphicsBootstrap.DescribeResolvedDDrawDll(workingDirectory),
            LaunchDetail = string.Join(
                " ",
                new[]
                {
                    graphicsDetail,
                    $"Slot {slotEnvironment.SlotId} settings are private.",
                    dddAccelerationDetail,
                    antiTamperDetail,
                    vintageDecalDetail,
                    WineRuntime.LaunchDetail,
                }
                    .Where(detail => !string.IsNullOrWhiteSpace(detail))),
        };
    }

    internal static NativeClientDddAccelerationProfile ValidateForLaunch(LaunchConfig config) =>
        ResolveValidatedLaunchTarget(config).Profile;

    private static (
        string InstallDirectory,
        string ClientPath,
        NativeClientDddAccelerationProfile Profile) ResolveValidatedLaunchTarget(LaunchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var installDirectory = ResolveInstallDirectory(config.InstallPath)
            ?? throw new InvalidOperationException(
                "Install folder must point to a directory containing client.exe.");

        var clientPath = Path.Combine(installDirectory, "client.exe");
        if (!File.Exists(clientPath))
        {
            throw new FileNotFoundException($"Missing client.exe in {installDirectory}", clientPath);
        }

        if (string.IsNullOrWhiteSpace(config.TicketKey))
        {
            throw new InvalidOperationException("Account name is required.");
        }

        var profile = NativeClientDddAcceleration.ResolveSupportedClientProfile(clientPath);
        return (installDirectory, clientPath, profile);
    }

    public static IReadOnlyList<string> BuildArgumentParts(LaunchConfig config)
    {
        var argumentParts = new List<string>
        {
            "-a",
            config.TicketKey.Trim(),
        };

        if (!string.IsNullOrWhiteSpace(config.Host))
        {
            argumentParts.Add("-h");
            argumentParts.Add(config.Host.Trim());
        }

        argumentParts.Add("-p");
        argumentParts.Add(config.Port.ToString());

        if (!string.IsNullOrWhiteSpace(config.VArg))
        {
            argumentParts.Add("-v");
            argumentParts.Add(config.VArg.Trim());
        }

        if (!string.IsNullOrWhiteSpace(config.ZArg))
        {
            argumentParts.Add("-z");
            argumentParts.Add(config.ZArg.Trim());
        }

        if (config.UseNoDisplayMode)
        {
            argumentParts.Add("-nd");
        }

        return argumentParts;
    }

    public static string BuildArgumentString(IEnumerable<string> argumentParts)
    {
        return string.Join(
            " ",
            argumentParts.Select(part =>
                part.IndexOfAny([' ', '\t', '"']) >= 0
                    ? $"\"{part.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                    : part));
    }

    public static string? ResolveInstallDirectory(string? installPath)
    {
        var value = installPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (File.Exists(value))
        {
            var fileName = Path.GetFileName(value);
            if (!fileName.Equals("client.exe", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Path.GetDirectoryName(value);
        }

        if (!Directory.Exists(value))
        {
            return null;
        }

        return File.Exists(Path.Combine(value, "client.exe")) ? value : null;
    }

    public static string GetRepositoryToolsDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tools", "dgvoodoo");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "dgvoodoo");
    }

    /// <summary>
    /// Older builds stored account presets in LocalAppData; remove them so they
    /// cannot keep feeding alternate launch identities.
    /// </summary>
    public static void RemoveLegacyProfileStore(Action<string>? report = null)
    {
        var storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AcLegacyLauncher",
            "profiles.json");

        try
        {
            if (!File.Exists(storePath))
            {
                return;
            }

            File.Delete(storePath);
            report?.Invoke($"Removed legacy profile store: {storePath}");
        }
        catch (Exception ex)
        {
            report?.Invoke($"Could not remove legacy profile store ({storePath}): {ex.Message}");
        }
    }

}
