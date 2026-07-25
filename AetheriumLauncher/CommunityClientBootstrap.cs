using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CG.Web.MegaApiClient;

namespace AcLegacyLauncher;

internal static class CommunityClientBootstrap
{
    public const string SourceUrl =
        "https://mega.nz/folder/L1MniCKJ#1dQCCFPc2ddcFILa_JGeZw/folder/T00V3ISI";

    // MegaApiClient 1.10.5 does not understand MEGA's nested-folder suffix.
    // Passing SourceUrl makes it decode "key/folder/id" as the share key,
    // producing unreadable attributes and invalid file checksums. Open the
    // root share with the real key, then scope its returned node tree by ID.
    private const string ApiRootUrl =
        "https://mega.nz/folder/L1MniCKJ#1dQCCFPc2ddcFILa_JGeZw";

    private const string SelectedFolderId = "T00V3ISI";

    public const string ExpectedSha256 =
        "52DDFDD1BD3AF839A90898C9A2A3BA8983E1811A1F1E45A588B649C5615DD26B";

    public const long ExpectedSize = 2_682_016;

    private const string ClientFileName = "client.exe";
    private const string ProvenanceFileName = "COMMUNITY_CLIENT_INSTALL.json";
    private const string DownloaderPackageVersion = "1.10.5";
    private const long MaximumZipSize = 300_000_000;
    private const int MaximumZipCandidates = 4;

    public static async Task InstallFromCommunityAsync(
        string installDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        var targetPath = ValidateInstallDirectory(installDirectory);

        if (await HasExpectedHashAsync(targetPath, cancellationToken))
        {
            WriteProvenance(installDirectory, "already-present", null);
            return;
        }

        var mega = new MegaApiClient();
        try
        {
            await mega.LoginAnonymousAsync();

            var nodes = (await mega.GetNodesFromLinkAsync(new Uri(ApiRootUrl))).ToArray();
            var selectedScopeIds = GetSelectedScopeIds(nodes);
            var allPublicFiles = nodes
                .Where(node => node.Type == NodeType.File)
                .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .ToArray();
            var selectedPublicFiles = allPublicFiles
                .Where(node =>
                    node.ParentId is not null &&
                    selectedScopeIds.Contains(node.ParentId))
                .ToArray();
            var publicFiles = selectedPublicFiles.Length == 0
                ? allPublicFiles
                : selectedPublicFiles;
            var rawCandidates = publicFiles
                .Where(node => node.Size == ExpectedSize)
                .OrderByDescending(node =>
                    string.Equals(node.Name, ClientFileName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(node => LooksLikeTargetVersion(node.Name))
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .ToArray();

            var attempts = new List<string>();
            foreach (var candidate in rawCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var temporaryPath = CreateTemporaryPath(installDirectory);

                try
                {
                    await mega.DownloadFileAsync(
                        candidate,
                        temporaryPath,
                        progress: progress!,
                        cancellationToken);

                    var actualHash = await GetSha256Async(temporaryPath, cancellationToken);
                    if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        attempts.Add(
                            $"raw {DescribeNode(candidate)} -> SHA-256 {actualHash}");
                        continue;
                    }

                    ReplaceClient(temporaryPath, targetPath);
                    await EnsureExpectedClientAsync(targetPath, cancellationToken);
                    WriteProvenance(installDirectory, "community-mega-download", candidate.Id);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    attempts.Add(
                        $"raw {DescribeNode(candidate)} -> download failed: {ex.Message}");
                }
                finally
                {
                    TryDelete(temporaryPath);
                }
            }

            var zipCandidates = publicFiles
                .Where(node =>
                    node.Size > 0 &&
                    node.Size <= MaximumZipSize &&
                    string.Equals(
                        Path.GetExtension(node.Name ?? string.Empty),
                        ".zip",
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(node => LooksLikeTargetVersion(node.Name))
                .ThenBy(node => node.Size)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumZipCandidates)
                .ToArray();

            foreach (var archiveNode in zipCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var archivePath = CreateTemporaryPath(installDirectory);

                try
                {
                    await mega.DownloadFileAsync(
                        archiveNode,
                        archivePath,
                        progress: progress!,
                        cancellationToken);

                    var matchedEntry = await TryInstallFromZipAsync(
                        archivePath,
                        installDirectory,
                        targetPath,
                        attempts,
                        cancellationToken);
                    if (matchedEntry is null)
                    {
                        continue;
                    }

                    await EnsureExpectedClientAsync(targetPath, cancellationToken);
                    WriteProvenance(
                        installDirectory,
                        "community-mega-zip-download",
                        archiveNode.Id,
                        $"{archiveNode.Name}::{matchedEntry}");
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    attempts.Add(
                        $"ZIP {DescribeNode(archiveNode)} -> download/read failed: {ex.Message}");
                }
                finally
                {
                    TryDelete(archivePath);
                }
            }

            var exposedFiles = publicFiles.Length == 0
                ? "(no files returned)"
                : string.Join(
                    "; ",
                    publicFiles.Take(50).Select(DescribeNode));
            var attemptSummary = attempts.Count == 0
                ? "(no size-matched executable or supported ZIP candidate was exposed)"
                : string.Join("; ", attempts.Take(25));

            throw new InvalidDataException(
                $"The public MEGA link did not yield the required DM v1.0.69 client. " +
                $"Required size: {ExpectedSize:N0}; SHA-256: {ExpectedSha256}. " +
                $"Selected folder ID: {SelectedFolderId}; selected files: " +
                $"{selectedPublicFiles.Length}; all share files: {allPublicFiles.Length}. " +
                $"Attempts: {attemptSummary}. Files exposed by the selected scope: {exposedFiles}");
        }
        finally
        {
            if (mega.IsLoggedIn)
            {
                try
                {
                    await mega.LogoutAsync();
                }
                catch
                {
                    // The verified client is already local; a logout failure must not undo it.
                }
            }
        }
    }

    public static async Task InstallFromVerifiedFileAsync(
        string sourcePath,
        string installDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A source client path is required.", nameof(sourcePath));
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("The source client file does not exist.", fullSourcePath);
        }

        var targetPath = ValidateInstallDirectory(installDirectory);
        var temporaryPath = CreateTemporaryPath(installDirectory);

        try
        {
            File.Copy(fullSourcePath, temporaryPath, overwrite: false);
            await EnsureExpectedClientAsync(temporaryPath, cancellationToken);
            ReplaceClient(temporaryPath, targetPath);
            await EnsureExpectedClientAsync(targetPath, cancellationToken);
            WriteProvenance(installDirectory, "verified-local-file", null);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static async Task InstallFromVerifiedZipAsync(
        string sourceArchivePath,
        string installDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceArchivePath))
        {
            throw new ArgumentException("A source ZIP path is required.", nameof(sourceArchivePath));
        }

        var fullSourcePath = Path.GetFullPath(sourceArchivePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("The source ZIP does not exist.", fullSourcePath);
        }

        var targetPath = ValidateInstallDirectory(installDirectory);
        var attempts = new List<string>();
        var matchedEntry = await TryInstallFromZipAsync(
            fullSourcePath,
            installDirectory,
            targetPath,
            attempts,
            cancellationToken);
        if (matchedEntry is null)
        {
            throw new InvalidDataException(
                $"The ZIP did not contain the required DM client. {string.Join("; ", attempts)}");
        }

        await EnsureExpectedClientAsync(targetPath, cancellationToken);
        WriteProvenance(
            installDirectory,
            "verified-local-zip",
            null,
            matchedEntry);
    }

    public static async Task VerifyInstalledClientAsync(
        string installDirectory,
        CancellationToken cancellationToken = default)
    {
        var targetPath = ValidateInstallDirectory(installDirectory);
        await EnsureExpectedClientAsync(targetPath, cancellationToken);
    }

    private static string ValidateInstallDirectory(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new ArgumentException("An Asheron's Call install directory is required.");
        }

        var fullInstallDirectory = Path.GetFullPath(installDirectory);
        if (!Directory.Exists(fullInstallDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The Asheron's Call install directory does not exist: {fullInstallDirectory}");
        }

        var missingDats = new[] { "cell.dat", "portal.dat" }
            .Where(fileName => !File.Exists(Path.Combine(fullInstallDirectory, fileName)))
            .ToArray();
        if (missingDats.Length != 0)
        {
            throw new InvalidDataException(
                $"The selected folder is not a complete client install. Missing: " +
                string.Join(", ", missingDats));
        }

        return Path.Combine(fullInstallDirectory, ClientFileName);
    }

    private static async Task EnsureExpectedClientAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing {ClientFileName}.", path);
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length != ExpectedSize)
        {
            throw new InvalidDataException(
                $"{ClientFileName} is {fileInfo.Length:N0} bytes; expected {ExpectedSize:N0} bytes.");
        }

        var actualHash = await GetSha256Async(path, cancellationToken);
        if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{ClientFileName} SHA-256 is {actualHash}; expected {ExpectedSha256}.");
        }
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != ExpectedSize)
        {
            return false;
        }

        var actualHash = await GetSha256Async(path, cancellationToken);
        return string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task<string?> TryInstallFromZipAsync(
        string archivePath,
        string installDirectory,
        string targetPath,
        List<string> attempts,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var candidates = archive.Entries
            .Where(entry =>
                entry.Length == ExpectedSize &&
                !string.IsNullOrEmpty(entry.Name))
            .OrderByDescending(entry =>
                string.Equals(entry.Name, ClientFileName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(entry => LooksLikeTargetVersion(entry.FullName))
            .ThenBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            attempts.Add(
                $"ZIP {Path.GetFileName(archivePath)} contained no {ExpectedSize:N0}-byte file");
            return null;
        }

        foreach (var entry in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var temporaryPath = CreateTemporaryPath(installDirectory);

            try
            {
                await using (var source = entry.Open())
                await using (var destination = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 128,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }

                var actualHash = await GetSha256Async(temporaryPath, cancellationToken);
                if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    attempts.Add(
                        $"ZIP entry {SanitizeForLog(entry.FullName)} -> SHA-256 {actualHash}");
                    continue;
                }

                ReplaceClient(temporaryPath, targetPath);
                return entry.FullName;
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        return null;
    }

    private static bool LooksLikeTargetVersion(string? value)
    {
        return value?.Contains("1.0.69", StringComparison.OrdinalIgnoreCase) == true ||
               value?.Contains("2005-01-24", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static HashSet<string> GetSelectedScopeIds(IEnumerable<INode> nodes)
    {
        var scopeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            SelectedFolderId,
        };

        var directories = nodes
            .Where(node => node.Type == NodeType.Directory)
            .ToArray();
        bool added;
        do
        {
            added = false;
            foreach (var directory in directories)
            {
                if (directory.ParentId is not null &&
                    scopeIds.Contains(directory.ParentId) &&
                    scopeIds.Add(directory.Id))
                {
                    added = true;
                }
            }
        }
        while (added);

        return scopeIds;
    }

    private static string DescribeNode(INode node)
    {
        return $"\"{SanitizeForLog(node.Name ?? "(unnamed)")}\" ({node.Size:N0} bytes)";
    }

    private static string SanitizeForLog(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string CreateTemporaryPath(string installDirectory)
    {
        return Path.Combine(
            Path.GetFullPath(installDirectory),
            $".aclegacy-client-{Guid.NewGuid():N}.tmp");
    }

    private static void ReplaceClient(string verifiedTemporaryPath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            var attributes = File.GetAttributes(targetPath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(targetPath, attributes & ~FileAttributes.ReadOnly);
            }
        }

        // The temporary file is deliberately in the target directory so this is a
        // same-volume replacement: the old client remains intact until this move.
        File.Move(verifiedTemporaryPath, targetPath, overwrite: true);
    }

    private static void WriteProvenance(
        string installDirectory,
        string installMode,
        string? selectedNodeId,
        string? selectedSourceItem = null)
    {
        var provenance = new
        {
            Client = "Dark Majesty client.exe v1.0.69.0",
            Source = SourceUrl,
            ExpectedSize,
            Sha256 = ExpectedSha256,
            InstalledAtUtc = DateTimeOffset.UtcNow,
            InstallMode = installMode,
            SelectedPublicNodeId = selectedNodeId,
            SelectedSourceItem = selectedSourceItem,
            Downloader = $"MegaApiClient {DownloaderPackageVersion}",
            DownloaderSource = "https://github.com/gpailler/MegaApiClient",
            DatUpdate = "portal.dat and cell.dat are updated separately by DDD from the game server.",
        };

        var path = Path.Combine(Path.GetFullPath(installDirectory), ProvenanceFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(provenance, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; never hide the original download/verification error.
        }
    }
}
