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
        var copiedDats = EnsureExclusiveDats(installDirectory, workingDirectory, report);

        var clientExe = Path.Combine(workingDirectory, "client.exe");
        if (!File.Exists(clientExe))
        {
            throw new FileNotFoundException(
                $"Failed to prepare client.exe in multiclient folder {workingDirectory}",
                clientExe);
        }

        var detail = copiedDats
            ? $"Private portal.dat/cell.dat ready under multiclient\\{safeId} (one-time copy)."
            : $"Using existing multiclient\\{safeId} workspace.";

        return new PrepareResult
        {
            WorkingDirectory = workingDirectory,
            ClientExePath = clientExe,
            Detail = detail,
            CopiedDats = copiedDats,
        };
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

    private static bool EnsureExclusiveDats(string installDirectory, string workingDirectory, Action<string>? report)
    {
        var copiedAny = false;
        foreach (var datName in ExclusiveDatFiles)
        {
            var sourcePath = Path.Combine(installDirectory, datName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    $"Install is missing {datName} (required for dual client).",
                    sourcePath);
            }

            var destPath = Path.Combine(workingDirectory, datName);
            if (File.Exists(destPath))
            {
                var sourceInfo = new FileInfo(sourcePath);
                var destInfo = new FileInfo(destPath);
                // Private portal.dat/cell.dat are updated in-place by DDD after login.
                // Only replace them when the *primary* install is strictly newer.
                // A size mismatch with a newer private file means DDD already advanced
                // this workspace (e.g. 6000 vs main 1680); overwriting caused re-DDD loops.
                if (sourceInfo.LastWriteTimeUtc <= destInfo.LastWriteTimeUtc)
                {
                    continue;
                }

                report?.Invoke($"Refreshing {datName} from primary install (primary is newer)...");
            }
            else
            {
                report?.Invoke($"Copying {datName} for private instance (may take a minute)...");
            }

            File.Copy(sourcePath, destPath, overwrite: true);
            try
            {
                File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(sourcePath));
            }
            catch
            {
                // Non-fatal.
            }

            copiedAny = true;
        }

        return copiedAny;
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
