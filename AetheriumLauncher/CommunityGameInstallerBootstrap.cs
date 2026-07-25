using System.Security.Cryptography;
using System.Diagnostics;
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
            try
            {
                await VerifyArchiveAsync(fullDestinationPath, cancellationToken);
                progress?.Report(100);
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                TryDelete(fullDestinationPath);
                if (File.Exists(fullDestinationPath))
                {
                    throw new IOException(
                        "The cached Dark Majesty archive is invalid and could not be replaced.",
                        ex);
                }
            }
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

    public static async Task PrepareInstallerAsync(
        string workDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workDirectory))
        {
            throw new ArgumentException("A bootstrap work directory is required.");
        }

        var root = Path.GetFullPath(workDirectory);
        var archivePath = Path.Combine(root, "dm-source.7z");
        var sourceDirectory = Path.Combine(root, "source");
        var legacyDirectory = Path.Combine(root, "legacy");

        Directory.CreateDirectory(root);
        await DownloadArchiveAsync(archivePath, progress, cancellationToken);
        await ExtractAndVerifyArchiveAsync(
            archivePath,
            sourceDirectory,
            cancellationToken);
        await ExtractAndVerifyLegacyPayloadAsync(
            Path.Combine(sourceDirectory, "ac1install.exe"),
            legacyDirectory,
            cancellationToken);
        progress?.Report(100);
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

    private static async Task ExtractAndVerifyArchiveAsync(
        string archivePath,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        var tarPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "tar.exe");
        if (!File.Exists(tarPath))
        {
            throw new FileNotFoundException(
                "Windows tar.exe is required to unpack the verified ACCPP archive.",
                tarPath);
        }

        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetDirectory(sourceDirectory);

            try
            {
                var result = await RunProcessAsync(
                    tarPath,
                    ["-xf", archivePath, "-C", sourceDirectory],
                    sourceDirectory,
                    cancellationToken);
                if (result.ExitCode != 0)
                {
                    throw new InvalidDataException(
                        $"Windows archive extraction failed with exit code " +
                        $"{result.ExitCode}: {result.Error}");
                }

                await VerifyInstallerAsync(
                    Path.Combine(sourceDirectory, "ac1install.exe"),
                    cancellationToken);
                return;
            }
            catch (Exception ex) when (
                ex is IOException or InvalidDataException)
            {
                lastFailure = ex;
            }
        }

        throw new InvalidDataException(
            "The verified ACCPP archive could not be extracted intact after two attempts.",
            lastFailure);
    }

    private static async Task ExtractAndVerifyLegacyPayloadAsync(
        string installerPath,
        string legacyDirectory,
        CancellationToken cancellationToken)
    {
        await VerifyInstallerAsync(installerPath, cancellationToken);

        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetDirectory(legacyDirectory);

            try
            {
                var result = await RunProcessAsync(
                    installerPath,
                    [$"/extract_all:{legacyDirectory}"],
                    Path.GetDirectoryName(installerPath)!,
                    cancellationToken);
                if (result.ExitCode != 0)
                {
                    throw new InvalidDataException(
                        $"The original installer extractor returned exit code " +
                        $"{result.ExitCode}: {result.Error}");
                }

                ValidateLegacyPayload(legacyDirectory);
                return;
            }
            catch (Exception ex) when (
                ex is IOException or InvalidDataException or System.ComponentModel.Win32Exception)
            {
                lastFailure = ex;
            }
        }

        throw new InvalidDataException(
            "The original installer could not produce an intact Disk1 payload after two attempts.",
            lastFailure);
    }

    private static void ValidateLegacyPayload(string legacyDirectory)
    {
        var disk1 = Path.Combine(legacyDirectory, "Disk1");
        var requiredFiles = new[]
        {
            "setup.exe",
            "setup.ini",
            "setup.inx",
            "data1.cab",
            "data1.hdr",
            "data2.cab",
        };
        var missing = requiredFiles
            .Where(name => !File.Exists(Path.Combine(disk1, name)))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"The original installer Disk1 payload is incomplete. Missing: " +
                string.Join(", ", missing));
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
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

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
