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

    public string MulticlientDetail { get; init; } = string.Empty;

    public string InstanceDetail { get; init; } = string.Empty;
}

public static class ClientLauncher
{
    public static ClientLaunchResult Start(
        LaunchConfig config,
        string? dgVoodooToolsDirectory = null,
        bool prepareGraphics = true,
        string? instanceKey = null,
        bool forcePrivateDatInstance = false,
        Action<string>? report = null)
    {
        var installDirectory = ResolveInstallDirectory(config.InstallPath)
            ?? throw new InvalidOperationException(
                "Install folder must point to a directory containing client.exe.");

        var primaryClientPath = Path.Combine(installDirectory, "client.exe");
        if (!File.Exists(primaryClientPath))
        {
            throw new FileNotFoundException($"Missing client.exe in {installDirectory}", primaryClientPath);
        }

        if (string.IsNullOrWhiteSpace(config.TicketKey))
        {
            throw new InvalidOperationException("Account name is required.");
        }

        var seededSafeGraphics = false;
        string? graphicsDetail = null;

        if (prepareGraphics)
        {
            GraphicsBootstrap.EnsureDirectDrawWrapper(
                installDirectory,
                dgVoodooToolsDirectory ?? GetRepositoryToolsDirectory());
        }

        var runningClientDirectories = GetRunningClientDirectories();
        var otherClientRunning = runningClientDirectories.Count > 0;
        var sameInstallAlreadyRunning = runningClientDirectories.Any(directory =>
            IsSameInstallTree(directory, installDirectory));
        var multiInstallProfiles = ProfileStore.HasMultipleInstallRoots();

        // Prefer a stable per-account DAT workspace once it exists (or when multiple
        // profiles share this install). Solo Play used to bounce back to the primary
        // portal/cell after a dual launch, which looked like "DATs not saved to profile."
        var instanceId = string.IsNullOrWhiteSpace(instanceKey) ? config.TicketKey : instanceKey;
        var privateWorkspaceExists = ClientInstanceWorkspace.PrivateWorkspaceExists(
            installDirectory,
            instanceId);
        var multiProfilesOnInstall = ProfileStore.CountProfilesForInstall(installDirectory) > 1;

        // Private DAT copies when two clients would share one install's portal/cell,
        // when this account already has a private workspace, or when profiles require it.
        // Separate folders (main vs admin) already have their own dats — don't copy.
        var usePrivateInstance = forcePrivateDatInstance
            || sameInstallAlreadyRunning
            || privateWorkspaceExists
            || multiProfilesOnInstall;

        // Windowed + no CaptureMouse whenever dual is likely across one OR many folders.
        var useDualClientDisplay = usePrivateInstance
            || otherClientRunning
            || multiInstallProfiles
            || forcePrivateDatInstance
            || !string.IsNullOrWhiteSpace(instanceKey);

        string workingDirectory;
        string clientPath;
        var instanceDetail = "Using primary install folder.";

        if (usePrivateInstance)
        {
            var prepared = ClientInstanceWorkspace.Ensure(installDirectory, instanceId!, report);
            workingDirectory = prepared.WorkingDirectory;
            clientPath = prepared.ClientExePath;
            instanceDetail = prepared.Detail;

            if (prepareGraphics)
            {
                GraphicsBootstrap.EnsureDirectDrawWrapper(
                    workingDirectory,
                    dgVoodooToolsDirectory ?? GetRepositoryToolsDirectory());
            }
        }
        else
        {
            workingDirectory = installDirectory;
            clientPath = primaryClientPath;
        }

        if (prepareGraphics)
        {
            if (useDualClientDisplay)
            {
                var extraDirs = ProfileStore.GetDistinctInstallDirectories()
                    .Concat(runningClientDirectories)
                    .Append(installDirectory);
                GraphicsBootstrap.ApplyMulticlientWindowedSettings(workingDirectory, extraDirs);
                graphicsDetail =
                    "Dual-client display: windowed + CaptureMouse=false on all known client folders.";
            }
            else if (config.SeedSafeGraphics)
            {
                GraphicsBootstrap.SeedSafeGraphicsSettings();
                GraphicsBootstrap.SeedUserPreferencesDisplay(fullScreen: true);
                seededSafeGraphics = true;
                GraphicsBootstrap.ApplyInputFriendlyDgVoodooConfig(workingDirectory);
            }
            else
            {
                GraphicsBootstrap.ApplyInputFriendlyDgVoodooConfig(workingDirectory);
            }
        }

        var argumentParts = BuildArgumentParts(config);
        var arguments = BuildArgumentString(argumentParts);

        // Suspend → patch Empyrean single-instance gate if stock → resume.
        // DM client has no ASLR (ImageBase 0x400000), so the file offset maps cleanly.
        // Patch against the on-disk image we execute (hardlink shares bytes with primary).
        var process = NativeProcess.StartSuspendedClient(
            clientPath,
            workingDirectory,
            arguments,
            out var processHandle,
            out var threadHandle);

        MulticlientGateResult gate;
        var vintageDecalDetail = string.Empty;
        try
        {
            gate = MulticlientGate.EnsureAllowMulti(clientPath, processHandle);

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

            NativeProcess.ResumeAndClose(processHandle, threadHandle);
            processHandle = IntPtr.Zero;
            threadHandle = IntPtr.Zero;
        }
        catch
        {
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
            SeededSafeGraphics = seededSafeGraphics,
            ResolvedDDrawPath = GraphicsBootstrap.DescribeResolvedDDrawDll(workingDirectory),
            MulticlientDetail = string.Join(
                " ",
                new[] { gate.Detail, graphicsDetail, vintageDecalDetail }
                    .Where(detail => !string.IsNullOrWhiteSpace(detail))),
            InstanceDetail = instanceDetail,
        };
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

    public static IReadOnlyList<string> GetRunningClientDirectories()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var process in Process.GetProcessesByName("client"))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        directories.Add(Path.GetFullPath(directory));
                    }
                }
                catch
                {
                    // 32-bit client from 64-bit launcher can deny MainModule access.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // Ignore process enumeration failures.
        }

        return directories.ToList();
    }

    public static bool IsSameInstallTree(string clientDirectory, string installDirectory)
    {
        var client = Path.GetFullPath(clientDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var install = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (client.Equals(install, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var multiclientRoot = Path.Combine(install, ClientInstanceWorkspace.RootFolderName);
        return client.StartsWith(multiclientRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || client.Equals(multiclientRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Private DAT folders are only required when two launches share one install tree.
    /// </summary>
    public static bool RequiresPrivateDatInstance(string installDirectory, IEnumerable<ClientProfile> batchProfiles)
    {
        var target = Path.GetFullPath(installDirectory);
        var sharing = batchProfiles.Count(profile =>
        {
            var resolved = ResolveInstallDirectory(profile.InstallPath);
            return !string.IsNullOrWhiteSpace(resolved)
                && Path.GetFullPath(resolved).Equals(target, StringComparison.OrdinalIgnoreCase);
        });

        return sharing > 1 || IsSameInstallTreeRunning(installDirectory);
    }

    private static bool IsSameInstallTreeRunning(string installDirectory)
    {
        return GetRunningClientDirectories().Any(directory => IsSameInstallTree(directory, installDirectory));
    }
}
