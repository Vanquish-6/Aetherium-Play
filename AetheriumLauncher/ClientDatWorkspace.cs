namespace AcLegacyLauncher;

internal sealed class ClientDatWorkspaceInfo
{
    internal required string WorkingDirectory { get; init; }

    internal required string ClientExePath { get; init; }
}

/// <summary>
/// Gives each live account slot its own writable portal.dat/cell.dat pair.
///
/// The install-root DATs are the launcher-owned seed only. A client never runs
/// against them directly. While the machine-wide seed gate is held, a slot is
/// refreshed from that seed, performs any server DDD repair in its private
/// workspace, and then promotes the fully-drained result back to the seed.
/// This prevents two native DAT allocators from ever mutating one file pair.
/// </summary>
internal static class ClientDatWorkspace
{
    internal const string RootFolderName = "multiclient";
    private const string SeedStageRootName = ".aetherium-dat-seed-stage";
    private const string SeedPendingMarkerName = ".aetherium-dat-seed.pending";

    private static readonly string[] DatFileNames =
    [
        "portal.dat",
        "cell.dat",
    ];

    private static readonly HashSet<string> SkipRuntimeNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "AetheriumLauncher.exe",
            "AetheriumLauncher.dll",
            "AetheriumLauncher.pdb",
            "AetheriumLauncher.deps.json",
            "AetheriumLauncher.runtimeconfig.json",
            "AcLegacyLauncher.exe",
            "AcLegacyLauncher.dll",
            "AcLegacyLauncher.pdb",
            "AcLegacyLauncher.deps.json",
            "AcLegacyLauncher.runtimeconfig.json",
            ".aetherium-shared-dat.lock",
        };

    internal static string GetWorkspaceDirectory(string installDirectory, string slotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        var safeSlot = new string(
            slotId
                .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                .ToArray());
        if (string.IsNullOrWhiteSpace(safeSlot))
        {
            throw new ArgumentException("Account slot ID is not valid.", nameof(slotId));
        }

        return Path.Combine(
            Path.GetFullPath(installDirectory),
            RootFolderName,
            "slot-" + safeSlot);
    }

    internal static ClientDatWorkspaceInfo PrepareFromSeed(
        string installDirectory,
        string slotId,
        Action<string>? report = null)
    {
        var install = Path.GetFullPath(installDirectory);
        EnsureDirectoryIsNotReparsePoint(install, "game install");

        var root = Path.Combine(install, RootFolderName);
        Directory.CreateDirectory(root);
        EnsureDirectoryIsNotReparsePoint(root, "private DAT workspace root");

        RecoverPendingSeedPromotion(install, report);

        var workingDirectory = GetWorkspaceDirectory(install, slotId);
        Directory.CreateDirectory(workingDirectory);
        EnsureDirectoryIsNotReparsePoint(workingDirectory, "private DAT workspace");

        CopyRuntimeFiles(install, workingDirectory, report);
        foreach (var datName in DatFileNames)
        {
            CopyFileAtomically(
                Path.Combine(install, datName),
                Path.Combine(workingDirectory, datName),
                $"seed {datName}",
                report);
        }

        var clientPath = Path.Combine(workingDirectory, "client.exe");
        if (!File.Exists(clientPath))
        {
            throw new FileNotFoundException(
                "The private DAT workspace is missing client.exe.",
                clientPath);
        }

        return new ClientDatWorkspaceInfo
        {
            WorkingDirectory = workingDirectory,
            ClientExePath = clientPath,
        };
    }

    internal static void PromoteToSeed(
        string workingDirectory,
        string installDirectory,
        Action<string>? report = null)
    {
        var source = Path.GetFullPath(workingDirectory);
        var install = Path.GetFullPath(installDirectory);
        EnsureDirectoryIsNotReparsePoint(source, "private DAT workspace");
        EnsureDirectoryIsNotReparsePoint(install, "game install");
        RecoverPendingSeedPromotion(install, report);

        var stageRoot = Path.Combine(install, SeedStageRootName);
        Directory.CreateDirectory(stageRoot);
        EnsureDirectoryIsNotReparsePoint(stageRoot, "DAT seed staging root");

        var transactionId = Guid.NewGuid().ToString("N");
        var stageDirectory = Path.Combine(stageRoot, transactionId);
        Directory.CreateDirectory(stageDirectory);
        EnsureDirectoryIsNotReparsePoint(stageDirectory, "DAT seed staging transaction");

        foreach (var datName in DatFileNames)
        {
            CopyFileAtomically(
                Path.Combine(source, datName),
                Path.Combine(stageDirectory, datName),
                $"stage {datName}",
                report);
        }

        // The marker is written only after both staged files are durable. If
        // power is lost after this point, the next seed owner replays both files
        // from the complete stage before any client can clone the seed.
        WritePendingMarkerAtomically(install, transactionId);
        ApplyStagedSeedPromotion(install, transactionId, report);
    }

    internal static void RecoverPendingSeedPromotion(
        string installDirectory,
        Action<string>? report = null)
    {
        var install = Path.GetFullPath(installDirectory);
        var markerPath = Path.Combine(install, SeedPendingMarkerName);
        if (!File.Exists(markerPath))
        {
            return;
        }

        EnsureFileIsNotReparsePoint(markerPath, "DAT seed pending marker");
        var transactionId = File.ReadAllText(markerPath).Trim();
        if (transactionId.Length != 32 ||
            transactionId.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException(
                "The DAT seed pending marker is invalid; refusing to launch from an uncertain seed.");
        }

        report?.Invoke(
            "Recovering an interrupted DAT seed promotion before another client starts.");
        ApplyStagedSeedPromotion(install, transactionId, report);
    }

    private static void ApplyStagedSeedPromotion(
        string installDirectory,
        string transactionId,
        Action<string>? report)
    {
        var stageDirectory = Path.Combine(
            installDirectory,
            SeedStageRootName,
            transactionId);
        EnsureDirectoryIsNotReparsePoint(
            stageDirectory,
            "DAT seed staging transaction");

        // Staged sources remain intact until both destination replacements are
        // complete. Replaying this pair is therefore idempotent after a crash
        // between the two atomic destination replacements.
        foreach (var datName in DatFileNames)
        {
            CopyFileAtomically(
                Path.Combine(stageDirectory, datName),
                Path.Combine(installDirectory, datName),
                $"promote {datName}",
                report);
        }

        var markerPath = Path.Combine(installDirectory, SeedPendingMarkerName);
        File.Delete(markerPath);
        try
        {
            Directory.Delete(stageDirectory, recursive: true);
        }
        catch
        {
            // An orphaned completed stage is never authoritative without the
            // marker and can be cleaned by a later installer/maintenance pass.
        }
    }

    private static void WritePendingMarkerAtomically(
        string installDirectory,
        string transactionId)
    {
        var markerPath = Path.Combine(installDirectory, SeedPendingMarkerName);
        var temporaryPath = markerPath +
            $".aetherium-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(transactionId);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, markerPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Marker temp files are never read as authoritative state.
            }
        }
    }

    private static void CopyRuntimeFiles(
        string installDirectory,
        string workingDirectory,
        Action<string>? report)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(installDirectory))
        {
            var name = Path.GetFileName(sourcePath);
            if (SkipRuntimeNames.Contains(name) ||
                DatFileNames.Any(dat => name.Equals(dat, StringComparison.OrdinalIgnoreCase)) ||
                name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destinationPath = Path.Combine(workingDirectory, name);
            // Always refresh from the verified install root. Timestamps are not
            // an integrity signal and a same-size stale/tampered runtime file
            // must never survive merely because it has a newer mtime.
            report?.Invoke($"Refreshing private-slot runtime file {name}.");
            CopyFileAtomically(sourcePath, destinationPath, $"runtime {name}", report: null);
        }
    }

    private static void CopyFileAtomically(
        string sourcePath,
        string destinationPath,
        string label,
        Action<string>? report)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Missing {label} source.", sourcePath);
        }

        EnsureFileIsNotReparsePoint(sourcePath, label);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var temporaryPath = destinationPath +
            $".aetherium-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp";
        try
        {
            report?.Invoke(
                $"{label}: {Path.GetFileName(sourcePath)} -> {Path.GetDirectoryName(destinationPath)}");

            using (var source = new FileStream(
                       sourcePath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete,
                       bufferSize: 1024 * 1024,
                       FileOptions.SequentialScan))
            using (var destination = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1024 * 1024,
                       FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                source.CopyTo(destination, 1024 * 1024);
                destination.Flush(flushToDisk: true);
            }

            if (File.Exists(destinationPath))
            {
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // A stale temp file is harmless; the next launch never treats
                // it as a DAT seed or runtime file.
            }
        }
    }

    private static void EnsureDirectoryIsNotReparsePoint(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Aetherium Play refuses a reparse-point {label}: {path}");
        }
    }

    private static void EnsureFileIsNotReparsePoint(string path, string label)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Aetherium Play refuses a reparse-point {label}: {path}");
        }
    }
}
