using System.Runtime.InteropServices;

namespace AcLegacyLauncher;

/// <summary>
/// Per-client working folders under {install}\multiclient\{id}\ so dual DM clients
/// don't fight over exclusive portal.dat / cell.dat locks on one install.
/// Binaries are hard-linked (same volume); the two DAT files are real copies.
/// </summary>
public static class ClientInstanceWorkspace
{
    public const string RootFolderName = "multiclient";

    private static readonly string[] ExclusiveDatFiles =
    [
        "portal.dat",
        "cell.dat",
    ];

    private static readonly HashSet<string> SkipFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AetheriumLauncher.exe",
        "AetheriumLauncher.dll",
        "AetheriumLauncher.pdb",
        "AetheriumLauncher.deps.json",
        "AetheriumLauncher.runtimeconfig.json",
        // Keep the old names excluded so upgraded installs cannot copy stale
        // launcher binaries into a per-client workspace.
        "AcLegacyLauncher.exe",
        "AcLegacyLauncher.dll",
        "AcLegacyLauncher.pdb",
        "AcLegacyLauncher.deps.json",
        "AcLegacyLauncher.runtimeconfig.json",
    };

    public sealed class PrepareResult
    {
        public required string WorkingDirectory { get; init; }

        public required string ClientExePath { get; init; }

        public string Detail { get; init; } = string.Empty;

        public bool CopiedDats { get; init; }
    }

    public static string SanitizeInstanceId(string? raw)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? "client" : raw.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        value = value.Replace(' ', '_');
        if (value.Length > 48)
        {
            value = value[..48];
        }

        return string.IsNullOrWhiteSpace(value) ? "client" : value;
    }

    /// <summary>
    /// True when a prior dual/profile launch already seeded private DATs for this account.
    /// </summary>
    public static bool PrivateWorkspaceExists(string installDirectory, string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return false;
        }

        var workingDirectory = Path.Combine(
            installDirectory,
            RootFolderName,
            SanitizeInstanceId(instanceId));
        return ExclusiveDatFiles.All(datName =>
            File.Exists(Path.Combine(workingDirectory, datName)));
    }

    public static bool IsClientProcessRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("client").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static PrepareResult Ensure(
        string installDirectory,
        string instanceId,
        Action<string>? report = null)
    {
        if (!Directory.Exists(installDirectory))
        {
            throw new DirectoryNotFoundException(installDirectory);
        }

        var safeId = SanitizeInstanceId(instanceId);
        var workingDirectory = Path.Combine(installDirectory, RootFolderName, safeId);
        Directory.CreateDirectory(workingDirectory);

        report?.Invoke($"Preparing multiclient folder: {workingDirectory}");

        LinkRuntimeFiles(installDirectory, workingDirectory, report);
        var copiedDats = SyncAuthoritativeDats(installDirectory, report, workingDirectory);

        var clientExe = Path.Combine(workingDirectory, "client.exe");
        if (!File.Exists(clientExe))
        {
            throw new FileNotFoundException(
                $"Failed to prepare client.exe in multiclient folder {workingDirectory}",
                clientExe);
        }

        var detail = copiedDats
            ? $"Private portal.dat/cell.dat synced under multiclient\\{safeId} from the newest install copy."
            : $"Using authoritative multiclient\\{safeId} DAT workspace.";

        return new PrepareResult
        {
            WorkingDirectory = workingDirectory,
            ClientExePath = clientExe,
            Detail = detail,
            CopiedDats = copiedDats,
        };
    }

    /// <summary>
    /// After DDD updates any portal.dat/cell.dat (main or a multiclient workspace),
    /// that newest pair becomes authoritative: copy it to the primary install and to
    /// every other multiclient workspace that is behind. Prevents re-download loops
    /// when one account is current and another (or main) is still on the old revision.
    /// </summary>
    public static bool SyncAuthoritativeDats(
        string installDirectory,
        Action<string>? report = null,
        string? alsoEnsureDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return false;
        }

        var copiedAny = false;
        foreach (var datName in ExclusiveDatFiles)
        {
            var locations = ListDatLocations(installDirectory, datName, alsoEnsureDirectory);
            var existing = locations
                .Where(File.Exists)
                .Select(path => new FileInfo(path))
                .ToArray();
            if (existing.Length == 0)
            {
                if (alsoEnsureDirectory is null)
                {
                    continue;
                }

                throw new FileNotFoundException(
                    $"Install is missing {datName} (required for dual client).",
                    Path.Combine(installDirectory, datName));
            }

            // Newest write time wins; larger size breaks ties (DDD grows these files).
            var newest = existing
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ThenByDescending(info => info.Length)
                .First();

            foreach (var destinationPath in locations.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (destinationPath.Equals(newest.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(destinationPath))
                {
                    var destInfo = new FileInfo(destinationPath);
                    if (destInfo.LastWriteTimeUtc > newest.LastWriteTimeUtc ||
                        (destInfo.LastWriteTimeUtc == newest.LastWriteTimeUtc &&
                         destInfo.Length >= newest.Length))
                    {
                        continue;
                    }
                }

                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                try
                {
                    report?.Invoke(
                        $"Syncing authoritative {datName} → {DescribeDatLocation(installDirectory, destinationPath)}...");
                    File.Copy(newest.FullName, destinationPath, overwrite: true);
                    try
                    {
                        File.SetLastWriteTimeUtc(destinationPath, newest.LastWriteTimeUtc);
                    }
                    catch
                    {
                        // Non-fatal.
                    }

                    copiedAny = true;
                }
                catch (IOException ex)
                {
                    // Destination locked by a running client — leave it; next launch retries.
                    report?.Invoke(
                        $"Could not sync {datName} to {DescribeDatLocation(installDirectory, destinationPath)} " +
                        $"(in use): {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    report?.Invoke(
                        $"Could not sync {datName} to {DescribeDatLocation(installDirectory, destinationPath)}: {ex.Message}");
                }
            }
        }

        return copiedAny;
    }

    private static IReadOnlyList<string> ListDatLocations(
        string installDirectory,
        string datName,
        string? alsoEnsureDirectory)
    {
        var paths = new List<string>
        {
            Path.Combine(installDirectory, datName),
        };

        if (!string.IsNullOrWhiteSpace(alsoEnsureDirectory))
        {
            paths.Add(Path.Combine(alsoEnsureDirectory, datName));
        }

        var multiclientRoot = Path.Combine(installDirectory, RootFolderName);
        if (Directory.Exists(multiclientRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(multiclientRoot))
            {
                paths.Add(Path.Combine(directory, datName));
            }
        }

        return paths;
    }

    private static string DescribeDatLocation(string installDirectory, string datPath)
    {
        var fullInstall = Path.GetFullPath(installDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDat = Path.GetFullPath(datPath);
        if (fullDat.StartsWith(fullInstall + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return fullDat[(fullInstall.Length + 1)..];
        }

        return Path.GetFileName(datPath);
    }

    private static void LinkRuntimeFiles(string installDirectory, string workingDirectory, Action<string>? report)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(installDirectory))
        {
            var name = Path.GetFileName(sourcePath);
            if (SkipFileNames.Contains(name))
            {
                continue;
            }

            if (ExclusiveDatFiles.Any(dat => name.Equals(dat, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Keep the instance folder lean: exe/dll/ini/conf the client loads from cwd.
            var ext = Path.GetExtension(name);
            if (ext is not (".exe" or ".dll" or ".ini" or ".conf" or ".txt" or ".cfg"))
            {
                continue;
            }

            var destPath = Path.Combine(workingDirectory, name);

            // Each instance needs its own dgVoodoo.conf so CaptureMouse/fullscreen
            // can be tuned for dual-client without rewriting the primary install.
            if (name.Equals("DgVoodoo.conf", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("dgVoodoo.conf", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                continue;
            }

            EnsureLinkedOrCopied(sourcePath, destPath, report);
        }
    }

    private static void EnsureLinkedOrCopied(string sourcePath, string destPath, Action<string>? report)
    {
        var sourceInfo = new FileInfo(sourcePath);
        if (File.Exists(destPath))
        {
            var destInfo = new FileInfo(destPath);
            if (destInfo.Length == sourceInfo.Length &&
                destInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc)
            {
                return;
            }

            File.Delete(destPath);
        }

        if (Native.CreateHardLink(destPath, sourcePath, IntPtr.Zero))
        {
            return;
        }

        // Different volume or FS that rejects hardlinks — fall back to copy.
        report?.Invoke($"Hardlink failed for {Path.GetFileName(sourcePath)}; copying instead.");
        File.Copy(sourcePath, destPath, overwrite: true);
    }

    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateHardLink(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes);
    }
}
