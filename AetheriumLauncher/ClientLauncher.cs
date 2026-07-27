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
}

public static class ClientLauncher
{
    public static ClientLaunchResult Start(
        LaunchConfig config,
        string? dgVoodooToolsDirectory = null,
        bool prepareGraphics = true,
        Action<string>? report = null)
    {
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

        var seededSafeGraphics = false;
        string? graphicsDetail = null;
        var workingDirectory = installDirectory;

        if (prepareGraphics)
        {
            GraphicsBootstrap.EnsureDirectDrawWrapper(
                installDirectory,
                dgVoodooToolsDirectory ?? GetRepositoryToolsDirectory());

            var otherClientRunning = GetRunningClientDirectories().Count > 0;
            if (otherClientRunning)
            {
                GraphicsBootstrap.ApplyMulticlientWindowedSettings(
                    workingDirectory,
                    additionalClientDirectories: new[] { installDirectory });
                graphicsDetail =
                    "Another client is already running: windowed + CaptureMouse=false.";
            }
            else
            {
                if (config.SeedSafeGraphics)
                {
                    GraphicsBootstrap.SeedSafeGraphicsSettings();
                    GraphicsBootstrap.SeedUserPreferencesDisplay(fullScreen: true);
                    seededSafeGraphics = true;
                }

                GraphicsBootstrap.ApplySoloCaptureMouseSettings(
                    workingDirectory,
                    additionalClientDirectories: new[] { installDirectory });
                graphicsDetail = "Solo display: CaptureMouse=true.";
            }
        }

        var argumentParts = BuildArgumentParts(config);
        var arguments = BuildArgumentString(argumentParts);

        // Suspend → patch Empyrean single-instance gate if stock → resume.
        // DM client has no ASLR (ImageBase 0x400000), so the file offset maps cleanly.
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
}
