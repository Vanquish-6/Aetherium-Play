using System.Security.Cryptography;
using CG.Web.MegaApiClient;

namespace AcLegacyLauncher;

internal static class CommunityGameInstallerBootstrap
{
    public const string SourceUrl =
        "https://mega.nz/folder/L1MniCKJ#1dQCCFPc2ddcFILa_JGeZw/folder/mlk0DQqR";

    private const string ApiRootUrl =
        "https://mega.nz/folder/L1MniCKJ#1dQCCFPc2ddcFILa_JGeZw";

    private const string SelectedFolderId = "mlk0DQqR";

    public const string ArchiveFileName = "2004-09-01 (278717kb).7z";
    public const long ExpectedArchiveSize = 242_270_845;
    public const string ExpectedArchiveSha256 =
        "50A6B74A706989B1E8B06936419BB87D05A1E4D9D1D6417C10CA1FA0F60E89EF";

    public const long ExpectedInstallerSize = 285_405_384;
    public const string ExpectedInstallerSha256 =
        "046EA8FFFA7CFD5828355F72FEBADDB52B536510202DD73D79A19BFA340B92BC";

    public static async Task<string> ProbeSourceAsync(
        CancellationToken cancellationToken = default)
    {
        var mega = new MegaApiClient();
        try
        {
            await mega.LoginAnonymousAsync();
            var node = await FindArchiveNodeAsync(mega, cancellationToken);
            return $"{node.Name} ({node.Size:N0} bytes, node {node.Id})";
        }
        finally
        {
            await TryLogoutAsync(mega);
        }
    }

    public static async Task DownloadArchiveAsync(
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("An archive destination path is required.");
        }

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidOperationException("The archive destination has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);

        if (File.Exists(fullDestinationPath))
        {
            await VerifyArchiveAsync(fullDestinationPath, cancellationToken);
            progress?.Report(100);
            return;
        }

        var temporaryPath = fullDestinationPath + $".{Guid.NewGuid():N}.download";
        var mega = new MegaApiClient();
        try
        {
            await mega.LoginAnonymousAsync();
            var node = await FindArchiveNodeAsync(mega, cancellationToken);
            await mega.DownloadFileAsync(
                node,
                temporaryPath,
                progress!,
                cancellationToken);
            await VerifyArchiveAsync(temporaryPath, cancellationToken);
            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
            progress?.Report(100);
        }
        finally
        {
            TryDelete(temporaryPath);
            await TryLogoutAsync(mega);
        }
    }

    public static Task VerifyArchiveAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return VerifyFileAsync(
            path,
            "Dark Majesty community archive",
            ExpectedArchiveSize,
            ExpectedArchiveSha256,
            cancellationToken);
    }

    public static Task VerifyInstallerAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        return VerifyFileAsync(
            path,
            "ac1install.exe",
            ExpectedInstallerSize,
            ExpectedInstallerSha256,
            cancellationToken);
    }

    private static async Task<INode> FindArchiveNodeAsync(
        MegaApiClient mega,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nodes = (await mega.GetNodesFromLinkAsync(new Uri(ApiRootUrl))).ToArray();
        var scopeIds = GetSelectedScopeIds(nodes);
        var allFiles = nodes.Where(node => node.Type == NodeType.File).ToArray();
        var selectedFiles = allFiles
            .Where(node => node.ParentId is not null && scopeIds.Contains(node.ParentId))
            .ToArray();
        var files = selectedFiles.Length == 0 ? allFiles : selectedFiles;

        var candidate = files
            .Where(node => node.Size == ExpectedArchiveSize)
            .OrderByDescending(node =>
                string.Equals(node.Name, ArchiveFileName, StringComparison.OrdinalIgnoreCase))
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (candidate is not null)
        {
            return candidate;
        }

        var exposed = files.Length == 0
            ? "(no files returned)"
            : string.Join(
                "; ",
                files.Take(50).Select(node => $"\"{Sanitize(node.Name)}\" ({node.Size:N0} bytes)"));
        throw new InvalidDataException(
            $"The selected community folder did not expose the required Dark Majesty archive. " +
            $"Expected {ExpectedArchiveSize:N0} bytes and SHA-256 {ExpectedArchiveSha256}. " +
            $"Folder ID: {SelectedFolderId}. Files exposed: {exposed}");
    }

    private static HashSet<string> GetSelectedScopeIds(IEnumerable<INode> nodes)
    {
        var scopeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            SelectedFolderId,
        };
        var directories = nodes.Where(node => node.Type == NodeType.Directory).ToArray();

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

    private static async Task VerifyFileAsync(
        string path,
        string description,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Missing {description}.", fullPath);
        }

        var actualSize = new FileInfo(fullPath).Length;
        if (actualSize != expectedSize)
        {
            throw new InvalidDataException(
                $"{description} is {actualSize:N0} bytes; expected {expectedSize:N0} bytes.");
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{description} SHA-256 is {actualHash}; expected {expectedSha256}.");
        }
    }

    private static async Task TryLogoutAsync(MegaApiClient mega)
    {
        if (!mega.IsLoggedIn)
        {
            return;
        }

        try
        {
            await mega.LogoutAsync();
        }
        catch
        {
            // Download and hash results remain authoritative if logout fails.
        }
    }

    private static string Sanitize(string? value)
    {
        return (value ?? "(unnamed)").Replace('\r', ' ').Replace('\n', ' ');
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
            // Best-effort cleanup must not hide the real download error.
        }
    }
}
