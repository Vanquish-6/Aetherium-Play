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
        var copiedDats = EnsureStablePrivateDats(installDirectory, workingDirectory, report);

        var clientExe = Path.Combine(workingDirectory, "client.exe");
        if (!File.Exists(clientExe))
        {
            throw new FileNotFoundException(
                $"Failed to prepare client.exe in multiclient folder {workingDirectory}",
                clientExe);
        }

        var detail = copiedDats
            ? $"Created stable private DAT workspace multiclient\\{safeId}."
            : $"Preserved stable private DAT workspace multiclient\\{safeId}.";

        return new PrepareResult
        {
            WorkingDirectory = workingDirectory,
            ClientExePath = clientExe,
            Detail = detail,
            CopiedDats = copiedDats,
        };
    }

    /// <summary>
    /// Seeds an account's private DAT pair exactly once. DDD updates these files
    /// in place, so later launches must never replace them from the primary install
    /// or another account based on size or timestamps.
    /// </summary>
    private static bool EnsureStablePrivateDats(
        string installDirectory,
        string workingDirectory,
        Action<string>? report)
    {
        var privatePaths = ExclusiveDatFiles
            .Select(name => Path.Combine(workingDirectory, name))
            .ToArray();
        var privateExists = privatePaths.Select(File.Exists).ToArray();

        if (privateExists.All(exists => exists))
        {
            ReportDatPair("Preserving private DATs", privatePaths, report);
            return false;
        }

        if (privateExists.Any(exists => exists))
        {
            var missing = privatePaths
                .Where(path => !File.Exists(path))
                .Select(Path.GetFileName);
            throw new InvalidDataException(
                $"Private DAT workspace is incomplete ({string.Join(", ", missing)} missing): " +
                workingDirectory);
        }

        var sourcePaths = ExclusiveDatFiles
            .Select(name => Path.Combine(installDirectory, name))
            .ToArray();
        var missingSources = sourcePaths
            .Where(path => !File.Exists(path))
            .Select(Path.GetFileName)
            .ToArray();
        if (missingSources.Length != 0)
        {
            throw new FileNotFoundException(
                $"Primary install is missing required DAT files: {string.Join(", ", missingSources)}",
                installDirectory);
        }

        Directory.CreateDirectory(workingDirectory);
        var temporaryPaths = privatePaths
            .Select(path => path + $".seed-{Guid.NewGuid():N}.tmp")
            .ToArray();
        try
        {
            for (var index = 0; index < sourcePaths.Length; index++)
            {
                report?.Invoke(
                    $"Seeding private {Path.GetFileName(privatePaths[index])}: " +
                    privatePaths[index]);
                File.Copy(sourcePaths[index], temporaryPaths[index], overwrite: false);
            }

            for (var index = 0; index < privatePaths.Length; index++)
            {
                File.Move(temporaryPaths[index], privatePaths[index], overwrite: false);
            }
        }
        catch
        {
            // Neither final path existed when seeding began, so any final file
            // visible here is a launcher-owned partial copy and is safe to roll back.
            foreach (var path in temporaryPaths.Concat(privatePaths))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Best-effort cleanup of launcher-owned seed files.
                }
            }

            throw;
        }

        ReportDatPair("Seeded private DATs", privatePaths, report);
        return true;
    }

    private static void ReportDatPair(
        string prefix,
        IReadOnlyList<string> paths,
        Action<string>? report)
    {
        if (report is null)
        {
            return;
        }

        var descriptions = paths.Select(path =>
        {
            var file = new FileInfo(path);
            return $"{path} ({file.Length:N0} bytes, {file.LastWriteTimeUtc:O})";
        });
        report($"{prefix}: {string.Join("; ", descriptions)}");
    }

    /// <summary>
    /// After DDD updates any portal.dat/cell.dat (main or a multiclient workspace),
    /// the largest intact pair becomes authoritative and is copied to every other
    /// workspace that is behind. Authority is by file size (DDD grows these files),
    /// not mtime — a touched stale main copy must never overwrite a larger updated pair.
    /// </summary>
    [Obsolete("Cross-workspace DAT synchronization can erase DDD progress.", error: true)]
    private static bool DeprecatedSyncAuthoritativeDats(
        string installDirectory,
        Action<string>? report = null,
        string? alsoEnsureDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return false;
        }

        var workspaces = ListDatWorkspaces(installDirectory, alsoEnsureDirectory);
        if (workspaces.Count == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(alsoEnsureDirectory))
        {
            Directory.CreateDirectory(alsoEnsureDirectory);
        }

        DatWorkspace? authoritative = null;
        foreach (var workspace in workspaces)
        {
            if (!TryReadDatPair(workspace, out var pair))
            {
                continue;
            }

            if (authoritative is null || CompareDatPairs(pair, authoritative.Value.Pair) > 0)
            {
                authoritative = new DatWorkspace(workspace, pair);
            }
        }

        if (authoritative is null)
        {
            if (alsoEnsureDirectory is null)
            {
                return false;
            }

            throw new FileNotFoundException(
                "Install is missing portal.dat/cell.dat (required for dual client).",
                Path.Combine(installDirectory, ExclusiveDatFiles[0]));
        }

        report?.Invoke(
            $"Authoritative DATs: {DescribeWorkspace(installDirectory, authoritative.Value.Directory)} " +
            $"(portal {authoritative.Value.Pair.PortalLength:N0} bytes, " +
            $"cell {authoritative.Value.Pair.CellLength:N0} bytes).");

        var copiedAny = false;
        foreach (var workspace in workspaces.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (workspace.Equals(authoritative.Value.Directory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryReadDatPair(workspace, out var existing) &&
                CompareDatPairs(existing, authoritative.Value.Pair) >= 0)
            {
                continue;
            }

            if (CopyDatPair(authoritative.Value.Directory, workspace, installDirectory, report))
            {
                copiedAny = true;
            }
        }

        return copiedAny;
    }

    private static IReadOnlyList<string> ListDatWorkspaces(
        string installDirectory,
        string? alsoEnsureDirectory)
    {
        var directories = new List<string>
        {
            Path.GetFullPath(installDirectory),
        };

        if (!string.IsNullOrWhiteSpace(alsoEnsureDirectory))
        {
            directories.Add(Path.GetFullPath(alsoEnsureDirectory));
        }

        var multiclientRoot = Path.Combine(installDirectory, RootFolderName);
        if (Directory.Exists(multiclientRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(multiclientRoot))
            {
                directories.Add(Path.GetFullPath(directory));
            }
        }

        return directories;
    }

    private static bool TryReadDatPair(string directory, out DatPair pair)
    {
        var portalPath = Path.Combine(directory, "portal.dat");
        var cellPath = Path.Combine(directory, "cell.dat");
        if (!File.Exists(portalPath) || !File.Exists(cellPath))
        {
            pair = default;
            return false;
        }

        var portal = new FileInfo(portalPath);
        var cell = new FileInfo(cellPath);
        pair = new DatPair(portal.Length, cell.Length, portal.LastWriteTimeUtc, cell.LastWriteTimeUtc);
        return true;
    }

    /// <summary>
    /// Positive when left is ahead of right. Size wins (DDD grows DATs); mtime is tie-break only.
    /// </summary>
    private static int CompareDatPairs(DatPair left, DatPair right)
    {
        var portal = left.PortalLength.CompareTo(right.PortalLength);
        if (portal != 0)
        {
            return portal;
        }

        var cell = left.CellLength.CompareTo(right.CellLength);
        if (cell != 0)
        {
            return cell;
        }

        var stamp = left.NewestWriteUtc.CompareTo(right.NewestWriteUtc);
        return stamp;
    }

    private static bool CopyDatPair(
        string sourceDirectory,
        string destinationDirectory,
        string installDirectory,
        Action<string>? report)
    {
        Directory.CreateDirectory(destinationDirectory);
        var copiedAny = false;
        foreach (var datName in ExclusiveDatFiles)
        {
            var sourcePath = Path.Combine(sourceDirectory, datName);
            var destinationPath = Path.Combine(destinationDirectory, datName);
            try
            {
                report?.Invoke(
                    $"Syncing authoritative {datName} → {DescribeDatLocation(installDirectory, destinationPath)}...");
                File.Copy(sourcePath, destinationPath, overwrite: true);
                try
                {
                    File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
                }
                catch
                {
                    // Non-fatal.
                }

                copiedAny = true;
            }
            catch (IOException ex)
            {
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

        return copiedAny;
    }

    private static string DescribeWorkspace(string installDirectory, string workspaceDirectory)
    {
        var fullInstall = Path.GetFullPath(installDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullWorkspace = Path.GetFullPath(workspaceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (fullWorkspace.Equals(fullInstall, StringComparison.OrdinalIgnoreCase))
        {
            return "primary install";
        }

        if (fullWorkspace.StartsWith(fullInstall + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return fullWorkspace[(fullInstall.Length + 1)..];
        }

        return fullWorkspace;
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

            // Keep the instance folder lean: runtime files the client loads from cwd.
            // Default.map is required for keyboard movement and command bindings.
            var ext = Path.GetExtension(name);
            if (ext is not (".exe" or ".dll" or ".ini" or ".conf" or ".txt" or ".cfg" or ".map"))
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

            // Real copy for client.exe so GetModuleFileName cannot resolve back to the
            // primary install via hardlink and load/update the wrong DAT folder.
            if (name.Equals("client.exe", StringComparison.OrdinalIgnoreCase))
            {
                EnsureCopied(sourcePath, destPath, report);
                continue;
            }

            EnsureLinkedOrCopied(sourcePath, destPath, report);
        }
    }

    private static void EnsureCopied(string sourcePath, string destPath, Action<string>? report)
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
        }

        report?.Invoke($"Copying {Path.GetFileName(sourcePath)} into private workspace...");
        File.Copy(sourcePath, destPath, overwrite: true);
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

    private readonly record struct DatPair(
        long PortalLength,
        long CellLength,
        DateTime PortalWriteUtc,
        DateTime CellWriteUtc)
    {
        public DateTime NewestWriteUtc =>
            PortalWriteUtc >= CellWriteUtc ? PortalWriteUtc : CellWriteUtc;
    }

    private readonly record struct DatWorkspace(string Directory, DatPair Pair);

    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateHardLink(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes);
    }
}
