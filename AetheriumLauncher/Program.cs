using System.Text.Json;

namespace AcLegacyLauncher;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 &&
            string.Equals(args[0], "--install-community-client", StringComparison.OrdinalIgnoreCase))
        {
            return RunCommunityClientCommand(args, CommunityClientCommand.Download);
        }

        if (args.Length == 2 &&
            string.Equals(
                args[0],
                "--install-community-client-with-progress",
                StringComparison.OrdinalIgnoreCase))
        {
            return RunProgressOperation(
                "Downloading and verifying the Dark Majesty client",
                (progress, cancellationToken) =>
                    CommunityClientBootstrap.InstallFromCommunityAsync(
                        args[1],
                        cancellationToken,
                        progress));
        }

        if (args.Length > 0 &&
            string.Equals(args[0], "--install-community-client-from-file", StringComparison.OrdinalIgnoreCase))
        {
            return RunCommunityClientCommand(args, CommunityClientCommand.VerifiedFile);
        }

        if (args.Length > 0 &&
            string.Equals(args[0], "--install-community-client-from-zip", StringComparison.OrdinalIgnoreCase))
        {
            return RunCommunityClientCommand(args, CommunityClientCommand.VerifiedZip);
        }

        if (args.Length > 0 &&
            string.Equals(args[0], "--verify-community-client", StringComparison.OrdinalIgnoreCase))
        {
            return RunCommunityClientCommand(args, CommunityClientCommand.Verify);
        }

        if (args.Length == 2 &&
            string.Equals(
                args[0],
                "--download-community-game-archive",
                StringComparison.OrdinalIgnoreCase))
        {
            return RunProgressOperation(
                "Downloading and verifying the original Dark Majesty installer",
                (progress, cancellationToken) =>
                    CommunityGameInstallerBootstrap.DownloadArchiveAsync(
                        args[1],
                        progress,
                        cancellationToken));
        }

        if (args.Length == 2 &&
            string.Equals(
                args[0],
                "--prepare-community-game-installer",
                StringComparison.OrdinalIgnoreCase))
        {
            return RunProgressOperation(
                "Preparing the original Dark Majesty installer",
                (progress, cancellationToken) =>
                    CommunityGameInstallerBootstrap.PrepareInstallerAsync(
                        args[1],
                        progress,
                        cancellationToken));
        }

        if (args.Length == 2 &&
            string.Equals(
                args[0],
                "--verify-community-game-archive",
                StringComparison.OrdinalIgnoreCase))
        {
            return RunGameInstallerCommand(
                () => CommunityGameInstallerBootstrap.VerifyArchiveAsync(args[1]));
        }

        if (args.Length == 2 &&
            string.Equals(
                args[0],
                "--verify-community-game-installer",
                StringComparison.OrdinalIgnoreCase))
        {
            return RunGameInstallerCommand(
                () => CommunityGameInstallerBootstrap.VerifyInstallerAsync(args[1]));
        }

        if (args.Length == 1 &&
            string.Equals(
                args[0],
                "--probe-community-game-source",
                StringComparison.OrdinalIgnoreCase))
        {
            return RunGameInstallerCommand(async () =>
            {
                var result = await CommunityGameInstallerBootstrap.ProbeSourceAsync();
                WriteAetheriumPlayLog($"SOURCE PROBE: {result}");
            });
        }

        if (args.Length is 2 or 3 &&
            string.Equals(
                args[0],
                "--configure-aetherium-install",
                StringComparison.OrdinalIgnoreCase))
        {
            return RunGameInstallerCommand(() =>
            {
                AetheriumInstallationConfiguration.Configure(
                    args[1],
                    args.Length == 3 ? args[2] : null);
                return Task.CompletedTask;
            });
        }

        if (args.Any(arg => string.Equals(arg, "--smoke-launch", StringComparison.OrdinalIgnoreCase)))
        {
            return RunSmokeLaunch();
        }

        LauncherStartupOptions startupOptions;
        try
        {
            startupOptions = LauncherStartupOptions.Parse(args);
        }
        catch (Exception ex)
        {
            ApplicationConfiguration.Initialize();
            MessageBox.Show(
                ex.Message,
                "Aetherium Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1(startupOptions.GameInstallDirectory));
        return 0;
    }

    private static int RunProgressOperation(
        string operationName,
        Func<IProgress<double>, CancellationToken, Task> operation)
    {
        ApplicationConfiguration.Initialize();
        using var form = new DownloadProgressForm(operationName, operation);
        Application.Run(form);
        var failureDetail = form.Failure is null
            ? string.Empty
            : $"{Environment.NewLine}{form.Failure}";
        WriteAetheriumPlayLog(
            $"{(form.ExitCode == 0 ? "SUCCESS" : "FAILURE")}: {operationName}; " +
            $"exit {form.ExitCode}{failureDetail}");
        return form.ExitCode;
    }

    private static int RunGameInstallerCommand(Func<Task> operation)
    {
        try
        {
            operation().GetAwaiter().GetResult();
            WriteAetheriumPlayLog("SUCCESS: game-installer bootstrap command");
            return 0;
        }
        catch (Exception ex)
        {
            WriteAetheriumPlayLog(
                $"FAILURE: game-installer bootstrap command{Environment.NewLine}{ex}");
            return 1;
        }
    }

    private static int RunCommunityClientCommand(
        string[] args,
        CommunityClientCommand command)
    {
        var showFailureMessage = command != CommunityClientCommand.Verify;

        try
        {
            switch (command)
            {
                case CommunityClientCommand.Download when args.Length == 2:
                    CommunityClientBootstrap.InstallFromCommunityAsync(args[1])
                        .GetAwaiter()
                        .GetResult();
                    break;

                case CommunityClientCommand.VerifiedFile when args.Length == 3:
                    CommunityClientBootstrap.InstallFromVerifiedFileAsync(args[1], args[2])
                        .GetAwaiter()
                        .GetResult();
                    break;

                case CommunityClientCommand.VerifiedZip when args.Length == 3:
                    CommunityClientBootstrap.InstallFromVerifiedZipAsync(args[1], args[2])
                        .GetAwaiter()
                        .GetResult();
                    break;

                case CommunityClientCommand.Verify when args.Length == 2:
                    CommunityClientBootstrap.VerifyInstalledClientAsync(args[1])
                        .GetAwaiter()
                        .GetResult();
                    break;

                default:
                    throw new ArgumentException("Invalid community-client command arguments.");
            }

            WriteCommunityClientLog($"SUCCESS: {command}");
            return 0;
        }
        catch (Exception ex)
        {
            WriteCommunityClientLog($"FAILURE: {command}{Environment.NewLine}{ex}");

            if (showFailureMessage)
            {
                MessageBox.Show(
                    "The community DM client could not be downloaded and verified, so " +
                    "the existing client.exe was not replaced." + Environment.NewLine +
                    Environment.NewLine +
                    $"Required SHA-256: {CommunityClientBootstrap.ExpectedSha256}" +
                    Environment.NewLine +
                    $"Source: {CommunityClientBootstrap.SourceUrl}" +
                    Environment.NewLine +
                    Environment.NewLine +
                    $"Details were written to: {CommunityClientLogPath}",
                    "Aetherium Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return 1;
        }
    }

    private static string CommunityClientLogPath =>
        Path.Combine(Path.GetTempPath(), "AetheriumLauncher-community-client.log");

    private static string AetheriumPlayLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AetheriumPlay",
            "Logs",
            "setup.log");

    private static void WriteCommunityClientLog(string text)
    {
        try
        {
            File.AppendAllText(
                CommunityClientLogPath,
                $"[{DateTimeOffset.UtcNow:O}] {text}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never hide the actionable download/verification result.
        }
    }

    private static void WriteAetheriumPlayLog(string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AetheriumPlayLogPath)!);
            File.AppendAllText(
                AetheriumPlayLogPath,
                $"[{DateTimeOffset.UtcNow:O}] {text}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never mask a verified bootstrap result.
        }
    }

    private static int RunSmokeLaunch()
    {
        var installDirectory = LaunchConfig.DefaultInstallPath;
        var configPath = Path.Combine(installDirectory, "launcher.json");
        var packageDirectory = Path.Combine(installDirectory, "Decal-2.6.1.1-DM");
        var resultPath = Path.Combine(packageDirectory, "smoke-launch.json");

        try
        {
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Missing launcher.json for smoke launch.", configPath);
            }

            var config = JsonSerializer.Deserialize<LaunchConfig>(File.ReadAllText(configPath))
                ?? throw new InvalidOperationException("launcher.json did not contain a launch configuration.");
            config.InstallPath = installDirectory;

            var result = ClientLauncher.Start(
                config,
                ClientLauncher.GetRepositoryToolsDirectory(),
                prepareGraphics: false);
            var process = result.Process
                ?? throw new InvalidOperationException("The client process was not returned by the launcher.");

            WriteSmokeResult(resultPath, new
            {
                Success = true,
                ProcessId = process.Id,
                StartedAt = DateTimeOffset.Now,
                result.WorkingDirectory,
                result.LaunchDetail,
            });
            process.WaitForExit();
            result.AntiTamperGuard?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(packageDirectory);
            WriteSmokeResult(resultPath, new
            {
                Success = false,
                FailedAt = DateTimeOffset.Now,
                Error = ex.ToString(),
            });
            return 1;
        }
    }

    private static void WriteSmokeResult(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private enum CommunityClientCommand
    {
        Download,
        VerifiedFile,
        VerifiedZip,
        Verify,
    }
}
