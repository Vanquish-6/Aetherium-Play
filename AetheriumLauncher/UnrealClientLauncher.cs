using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace AcLegacyLauncher;

/** Unreal is a separate client: no native image patches, DirectDraw or DLL injection. */
public static class UnrealClientLauncher
{
    public static string? FindInstalledDirectory(string? preferred)
    {
        var candidates = new List<string?> { preferred };
        var marker = Path.Combine(AppContext.BaseDirectory, "unreal.install.path");
        try { if (File.Exists(marker)) candidates.Add(File.ReadAllText(marker).Trim()); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AetheriumPlay", "Unreal", "Windows"));
        candidates.Add(@"C:\ACDM-Unreal\Windows");
        candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Windows")));
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try { _ = ResolveExecutable(candidate); return Path.GetFullPath(candidate); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }

    public static string ResolveExecutable(string directory)
    {
        var root = Path.GetFullPath(directory);
        // Shipping builds name the game ACDM-Win64-Shipping.exe; Development builds use ACDM.exe.
        foreach (var folder in new[] { @"ACDM\Binaries\Win64", @"Windows\ACDM\Binaries\Win64", @"Binaries\Win64" })
        foreach (var name in new[] { "ACDM-Win64-Shipping.exe", "ACDM.exe" })
        {
            var candidate = Path.Combine(root, folder, name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Choose the installed Unreal client folder (containing ACDM.exe).");
    }

    public static void Validate(LaunchConfig config)
    {
        config.SyncSelectedSlotToLaunchFields();
        _ = ResolveExecutable(config.UnrealInstallPath);
        if (string.IsNullOrWhiteSpace(config.TicketKey) || string.IsNullOrEmpty(config.VArg))
            throw new InvalidOperationException("Account name and password are required.");
        if (config.Port is not 9000 and not 9100)
            throw new InvalidOperationException("Choose Aetherium White or Red.");
    }

    internal static ProcessStartInfo BuildStartInfo(LaunchConfig config, string address)
    {
        Validate(config);
        if (!IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            throw new InvalidOperationException("The selected realm has no IPv4 address.");
        var executable = ResolveExecutable(config.UnrealInstallPath);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-ACDMOnline");
        start.ArgumentList.Add($"-ACDMServer={address}");
        start.ArgumentList.Add($"-ACDMPort={config.Port}");
        // Set only the child's environment. Never place the password in argv,
        // launcher status strings, Unreal's command-line log, or a handoff file.
        start.Environment["ACDM_ACCOUNT"] = config.TicketKey;
        start.Environment["ACDM_PASSWORD"] = config.VArg;
        var userRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aetherium", "Unreal", $"Slot{config.SelectedSlotId}");
        start.ArgumentList.Add($"-UserDir={userRoot}");
        return start;
    }

    public static ClientLaunchResult Start(LaunchConfig config, CancellationToken cancellationToken)
    {
        Validate(config);
        cancellationToken.ThrowIfCancellationRequested();
        var address = Dns.GetHostAddresses(config.Host).FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new InvalidOperationException("Unable to resolve the selected realm.");
        var start = BuildStartInfo(config, address.ToString());
        var projectDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(start.FileName)!, "..", ".."));
        var userDirectory = start.ArgumentList.Single(value => value.StartsWith("-UserDir=", StringComparison.Ordinal))[9..];
        var dataDirectory = Path.Combine(userDirectory, config.Port == 9100 ? "Red" : "White", "Data");
        Directory.CreateDirectory(dataDirectory);
        using (SharedDatLaunchGate.AcquireWhenAvailable(dataDirectory, cancellationToken))
        {
            SeedMissingDats(config, projectDirectory, dataDirectory, cancellationToken);
        }
        start.ArgumentList.Add($"-ACDMPortalDat={Path.Combine(dataDirectory, "portal.dat")}");
        start.ArgumentList.Add($"-ACDMCellDat={Path.Combine(dataDirectory, "cell.dat")}");
        cancellationToken.ThrowIfCancellationRequested();
        Process process;
        using (UnrealReleaseVerifier.OpenVerifiedExecutable(projectDirectory, start.FileName))
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start the Unreal client.");
        }
        // A direct launch can offer a route back to this exact launcher.
        try
        {
            File.WriteAllText(Path.Combine(projectDirectory, "LauncherPath.txt"), Environment.ProcessPath!);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new ClientLaunchResult
        {
            InstallDirectory = config.UnrealInstallPath,
            WorkingDirectory = start.WorkingDirectory,
            Arguments = string.Join(" ", start.ArgumentList),
            Process = process,
            SlotId = config.SelectedSlotId,
            LaunchDetail = $"Unreal client: {(config.Port == 9100 ? "Red" : "White")}",
        };
    }

    private static readonly string[] DatFileNames = ["portal.dat", "cell.dat"];

    /** Seeds a slot's base DAT pair once. Later server patches land as CLIDAT sidecars beside it. */
    private static void SeedMissingDats(
        LaunchConfig config, string projectDirectory, string dataDirectory, CancellationToken cancellationToken)
    {
        if (DatFileNames.All(file => File.Exists(Path.Combine(dataDirectory, file))))
            return;

        var classicInstall = config.InstallPath;
        if (HasDatPair(classicInstall))
        {
            // A running classic client promotes patched DATs into this seed while holding the
            // install's mutex. Copying without it can capture a half-written allocator.
            using (SharedDatLaunchGate.AcquireWhenAvailable(classicInstall, cancellationToken))
            {
                ClientDatWorkspace.RecoverPendingSeedPromotion(classicInstall);
                CopyMissingDats(classicInstall, dataDirectory, cancellationToken);
            }
            return;
        }

        var packagedData = Path.Combine(projectDirectory, "Data");
        if (HasDatPair(packagedData))
        {
            CopyMissingDats(packagedData, dataDirectory, cancellationToken);
            return;
        }

        throw new FileNotFoundException(
            "The Unreal client needs portal.dat and cell.dat from your Dark Majesty game folder. " +
            "Set the Dark Majesty folder in the launcher, start Classic once so it finishes patching, then try again.");
    }

    private static bool HasDatPair(string? directory) =>
        !string.IsNullOrWhiteSpace(directory) &&
        DatFileNames.All(file =>
        {
            var path = Path.Combine(directory, file);
            return File.Exists(path) && new FileInfo(path).Length > 0;
        });

    private static void CopyMissingDats(string sourceDirectory, string dataDirectory, CancellationToken cancellationToken)
    {
        foreach (var file in DatFileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(dataDirectory, file);
            if (File.Exists(target)) continue;
            var pending = target + ".seed-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(Path.Combine(sourceDirectory, file), pending);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(pending, target);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }
}
