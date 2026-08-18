using System.Diagnostics;

namespace AcLegacyLauncher;

public sealed class ClientLaunchResult
{
    public required string InstallDirectory { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string Arguments { get; init; }

    public required Process? Process { get; init; }

    public bool SeededSafeGraphics { get; init; }

    public string ResolvedDDrawPath { get; init; } = string.Empty;

    public string LaunchDetail { get; init; } = string.Empty;

    internal ClientAntiTamperRuntimeGuard? AntiTamperGuard { get; init; }
}

public static class ClientLauncher
{
    public const string LegacyMulticlientFolderName = "multiclient";

    public static ClientLaunchResult Start(
        LaunchConfig config,
        string? dgVoodooToolsDirectory = null,
        bool prepareGraphics = true,
        Action<string>? report = null)
    {
        var (installDirectory, clientPath, expectedProfile) = ResolveValidatedLaunchTarget(config);

        // This local-only check happens before profile cleanup, graphics setup,
        // or process creation so a refused launch leaves the game untouched.
        ClientAntiTamper.EnsureNoKnownMemoryEditorRunning();

        RemoveLegacyProfileStore(report);
        if (ShouldRemoveLegacyMulticlient(config))
        {
            RemoveLegacyMulticlientFolder(installDirectory, report);
        }

        var seededSafeGraphics = false;
        string? graphicsDetail = null;
        var workingDirectory = installDirectory;

        if (prepareGraphics)
        {
            GraphicsBootstrap.EnsureDirectDrawWrapper(
                installDirectory,
                dgVoodooToolsDirectory ?? GetRepositoryToolsDirectory());

            if (config.SeedSafeGraphics)
            {
                GraphicsBootstrap.SeedSafeGraphicsSettings();
                GraphicsBootstrap.SeedUserPreferencesDisplay(fullScreen: true);
                seededSafeGraphics = true;
            }

            GraphicsBootstrap.ApplySoloCaptureMouseSettings(workingDirectory);
            graphicsDetail = "Solo display: CaptureMouse=true; Alt+Enter enabled.";
        }

        var argumentParts = BuildArgumentParts(config);
        var arguments = BuildArgumentString(argumentParts);

        // Suspend → optional vintage Decal inject → resume.
        // Stock client single-instance behavior is left alone (no multiclient patch).
        var process = NativeProcess.StartSuspendedClient(
            clientPath,
            workingDirectory,
            arguments,
            out var processHandle,
            out var threadHandle);

        NativeClientDddAccelerationInstallation? dddAcceleration = null;
        ClientAntiTamperContainment? containment = null;
        ClientAntiTamperRuntimeGuard? antiTamper = null;
        var dddAccelerationDetail = string.Empty;
        var antiTamperDetail = string.Empty;
        var vintageDecalDetail = string.Empty;
        try
        {
            // Contain the suspended stock client before any A09 marker, hook, or
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

            // Optional DLL injection must leave all guarded A09 regions
            // intact. Any collision is refused while the client is suspended.
            NativeClientDddAcceleration.VerifyInstalled(
                processHandle,
                dddAcceleration);

            // Close the race between the initial scan and process setup. A hit
            // here follows the existing fail-closed path and terminates only the
            // still-suspended client launched above.
            ClientAntiTamper.EnsureNoKnownMemoryEditorRunning();

            // Start the independent launcher-resident guard while the primary
            // client thread is still suspended, so no admitted A09 client ever
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
        }
        catch
        {
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
            AntiTamperGuard = antiTamper,
            SeededSafeGraphics = seededSafeGraphics,
            ResolvedDDrawPath = GraphicsBootstrap.DescribeResolvedDDrawDll(workingDirectory),
            LaunchDetail = string.Join(
                " ",
                new[]
                {
                    graphicsDetail,
                    dddAccelerationDetail,
                    antiTamperDetail,
                    vintageDecalDetail,
                }
                    .Where(detail => !string.IsNullOrWhiteSpace(detail))),
        };
    }

    internal static NativeClientDddAccelerationProfile ValidateForLaunch(LaunchConfig config) =>
        ResolveValidatedLaunchTarget(config).Profile;

    internal static bool ShouldRemoveLegacyMulticlient(LaunchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return !config.PreserveLegacyMulticlient;
    }

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

    /// <summary>
    /// Older dual-client builds copied portal/cell DATs under multiclient\{account}.
    /// Those private copies are the main reason players keep redoing DDD updates.
    /// </summary>
    public static void RemoveLegacyMulticlientFolder(string installDirectory, Action<string>? report = null)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return;
        }

        var multiclientRoot = Path.Combine(installDirectory, LegacyMulticlientFolderName);
        if (!Directory.Exists(multiclientRoot))
        {
            return;
        }

        try
        {
            report?.Invoke($"Removing legacy multiclient folder: {multiclientRoot}");
            Directory.Delete(multiclientRoot, recursive: true);
            report?.Invoke("Removed legacy multiclient DAT workspaces.");
        }
        catch (Exception ex)
        {
            report?.Invoke(
                $"Could not remove {multiclientRoot} (close any old client windows and retry): {ex.Message}");
        }
    }
}
