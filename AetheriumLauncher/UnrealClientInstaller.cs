using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcLegacyLauncher;

/// <summary>
/// One-time Unreal client download. The launcher fetches a small signed catalog from
/// aetherium.ac, then the zip it names, and only replaces the install when the hash matches.
/// Later launches reuse the files already on disk.
/// </summary>
internal static class UnrealClientInstaller
{
    internal const string CatalogUrl = "https://aetherium.ac/downloads/unreal/unreal-download.json";
    internal const long MaximumPackageBytes = 8L * 1024 * 1024 * 1024;
    private const string Product = "acdm-unreal-download";
    private static readonly HashSet<string> DownloadHosts =
        new(StringComparer.OrdinalIgnoreCase) { "aetherium.ac" };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AetheriumPlay", "Unreal", "Windows");

    internal static async Task EnsureReadyAsync(IWin32Window owner, CancellationToken cancellationToken)
    {
        var installed = UnrealClientLauncher.FindInstalledDirectory(null);
        UnrealDownloadCatalog catalog;
        try
        {
            catalog = await FetchCatalogAsync(cancellationToken);
        }
        catch (Exception) when (installed is not null && LocalReleaseOk(installed))
        {
            return;
        }
        catch (UnrealDownloadUnavailableException)
        {
            throw new InvalidOperationException(
                "The Unreal client is not installed, and the download is not published yet.");
        }

        if (IsCurrent(catalog))
            return;

        var existing = UnrealClientLauncher.FindInstalledDirectory(null);
        var prompt = existing is null
            ? $"Download the Unreal client ({FormatSize(catalog.PackageSize)})?\n\nIt is stored for this Windows user and is not downloaded again unless a newer signed release is published."
            : $"A newer Unreal client is available ({catalog.Version}, {FormatSize(catalog.PackageSize)}).\n\nDownload it now?";
        if (MessageBox.Show(owner, prompt, "Aetherium Play", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            throw new InvalidOperationException("The Unreal client was not downloaded.");

        using var form = new DownloadProgressForm(
            "Downloading the Unreal client",
            (progress, token) => InstallAsync(catalog, progress, token));
        form.ShowDialog(owner);
        if (form.ExitCode == 2)
            throw new OperationCanceledException();
        if (form.ExitCode != 0)
            throw form.Failure ?? new InvalidOperationException("The Unreal client download did not finish.");
        if (!IsCurrent(catalog))
            throw new InvalidOperationException("The Unreal client did not match the signed release after install.");
    }

    internal static async Task InstallFromCatalogAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var catalog = await FetchCatalogAsync(cancellationToken);
        if (IsCurrent(catalog))
        {
            progress.Report(100);
            return;
        }
        await InstallAsync(catalog, progress, cancellationToken);
    }

    internal static async Task<UnrealDownloadCatalog> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        using var http = CreateHttp();
        using var response = await http.GetAsync(CatalogUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            throw new UnrealDownloadUnavailableException();
        if (!response.IsSuccessStatusCode)
            throw new InvalidDataException($"The Unreal download catalog returned {(int)response.StatusCode}.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length is 0 or > UnrealReleaseVerifier.MaximumManifestBytes)
            throw new InvalidDataException("The Unreal download catalog is an invalid size.");
        return VerifyCatalog(bytes);
    }

    private static bool LocalReleaseOk(string directory)
    {
        try
        {
            var executable = UnrealClientLauncher.ResolveExecutable(directory);
            var projectDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", ".."));
            using (UnrealReleaseVerifier.OpenVerifiedExecutable(projectDirectory, executable)) { }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException)
        {
            return false;
        }
    }

    internal static bool IsCurrent(UnrealDownloadCatalog catalog)
    {
        var directory = UnrealClientLauncher.FindInstalledDirectory(null);
        if (directory is null)
            return false;
        try
        {
            var executable = UnrealClientLauncher.ResolveExecutable(directory);
            var projectDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", ".."));
            using var verified = UnrealReleaseVerifier.OpenVerifiedExecutable(projectDirectory, executable);
            var manifestPath = Path.Combine(projectDirectory, UnrealReleaseVerifier.ManifestFileName);
            var payload = UnrealReleaseVerifier.Verify(File.ReadAllBytes(manifestPath), UnrealReleaseVerifier.PublicKeys);
            return string.Equals(payload.Version, catalog.Version, StringComparison.Ordinal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException)
        {
            return false;
        }
    }

    private static async Task InstallAsync(
        UnrealDownloadCatalog catalog, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var destination = DefaultInstallDirectory;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempRoot = Path.Combine(Path.GetTempPath(), "AetheriumPlay", "unreal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var packagePath = Path.Combine(tempRoot, "client.zip");
        var staging = destination + ".incoming";
        try
        {
            await DownloadPackageAsync(catalog, packagePath, progress, cancellationToken);
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            ExtractZip(packagePath, staging);
            var executable = UnrealClientLauncher.ResolveExecutable(staging);
            var projectDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", ".."));
            using (UnrealReleaseVerifier.OpenVerifiedExecutable(projectDirectory, executable)) { }
            var payload = UnrealReleaseVerifier.Verify(
                File.ReadAllBytes(Path.Combine(projectDirectory, UnrealReleaseVerifier.ManifestFileName)),
                UnrealReleaseVerifier.PublicKeys);
            if (!string.Equals(payload.Version, catalog.Version, StringComparison.Ordinal))
                throw new InvalidDataException("The downloaded Unreal client version does not match the signed catalog.");

            if (Directory.Exists(destination))
            {
                var backup = destination + ".previous";
                if (Directory.Exists(backup))
                    Directory.Delete(backup, recursive: true);
                Directory.Move(destination, backup);
            }
            Directory.Move(staging, destination);
            staging = "";
            WriteMarker(destination);
            var previous = destination + ".previous";
            if (Directory.Exists(previous))
                Directory.Delete(previous, recursive: true);
            progress.Report(100);
        }
        finally
        {
            if (staging.Length > 0 && Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (IOException) { }
            }
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { }
        }
    }

    private static async Task DownloadPackageAsync(
        UnrealDownloadCatalog catalog, string packagePath, IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var http = CreateHttp();
        http.Timeout = TimeSpan.FromHours(6);
        using var response = await http.GetAsync(catalog.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new InvalidDataException("The Unreal client download must not redirect.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidDataException($"The Unreal client download returned {(int)response.StatusCode}.");
        var declared = response.Content.Headers.ContentLength;
        if (declared is not null && declared.Value != catalog.PackageSize)
            throw new InvalidDataException("The Unreal client download size does not match the signed catalog.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long written = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            written += read;
            if (written > catalog.PackageSize)
                throw new InvalidDataException("The Unreal client download is larger than the signed catalog.");
            sha.AppendData(buffer.AsSpan(0, read));
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress.Report(written * 100d / catalog.PackageSize);
        }
        if (written != catalog.PackageSize)
            throw new InvalidDataException("The Unreal client download ended before the signed size.");
        var digest = Convert.ToHexString(sha.GetHashAndReset());
        if (!digest.Equals(catalog.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Unreal client download failed its SHA-256 check.");
    }

    private static void ExtractZip(string packagePath, string destination)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var root = Path.GetFullPath(destination);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            var relative = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (relative.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
                throw new InvalidDataException("The Unreal client package contains an unsafe path.");
            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The Unreal client package escapes its install folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
    }

    private static void WriteMarker(string directory)
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "unreal.install.path"), directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static UnrealDownloadCatalog VerifyCatalog(byte[] envelopeBytes)
    {
        var envelope = JsonSerializer.Deserialize<UnrealReleaseEnvelope>(envelopeBytes, Json)
            ?? throw new InvalidDataException("The Unreal download catalog is empty.");
        if (envelope.SchemaVersion != 1 || string.IsNullOrWhiteSpace(envelope.KeyId) ||
            string.IsNullOrWhiteSpace(envelope.PayloadBase64) || string.IsNullOrWhiteSpace(envelope.SignatureBase64) ||
            !UnrealReleaseVerifier.PublicKeys.TryGetValue(envelope.KeyId, out var publicKey))
            throw new InvalidDataException("The Unreal download catalog is not signed by a trusted key.");
        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Convert.FromBase64String(envelope.PayloadBase64);
            signature = Convert.FromBase64String(envelope.SignatureBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("The Unreal download catalog signature is malformed.", ex);
        }
        using var key = RSA.Create();
        key.FromXmlString(publicKey);
        if (key.KeySize < 2048 || payloadBytes.Length == 0 || !key.VerifyData(
                payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("The Unreal download catalog signature did not verify.");
        var payload = JsonSerializer.Deserialize<UnrealDownloadPayload>(payloadBytes, Json)
            ?? throw new InvalidDataException("The signed Unreal download catalog is empty.");
        if (payload.SchemaVersion != 1 || payload.Product != Product ||
            string.IsNullOrWhiteSpace(payload.Version) || payload.Version.Length > 64 ||
            payload.PackageSize is <= 0 or > MaximumPackageBytes ||
            payload.PackageSha256 is not { Length: 64 } || !payload.PackageSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The signed Unreal download catalog is not valid.");
        ValidateHttpsHost(payload.PackageUrl);
        return new UnrealDownloadCatalog(payload.Version, payload.PackageUrl, payload.PackageSize, payload.PackageSha256);
    }

    private static void ValidateHttpsHost(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort || uri.IsLoopback ||
            !DownloadHosts.Contains(uri.IdnHost) || IPAddress.TryParse(uri.IdnHost, out _))
            throw new InvalidDataException("The Unreal client download address is not an approved HTTPS endpoint.");
    }

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AetheriumPlay-UnrealDownload/1");
        return client;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
        return $"{bytes / (1024d * 1024):0} MB";
    }
}

internal sealed record UnrealDownloadCatalog(string Version, string PackageUrl, long PackageSize, string PackageSha256);

internal sealed class UnrealDownloadUnavailableException : Exception
{
    internal UnrealDownloadUnavailableException() : base("The Unreal download catalog is not published.") { }
}

internal sealed class UnrealDownloadPayload
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }
    [JsonPropertyName("product")]
    public string Product { get; set; } = "";
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
    [JsonPropertyName("publishedUtc")]
    public string PublishedUtc { get; set; } = "";
    [JsonPropertyName("packageUrl")]
    public string PackageUrl { get; set; } = "";
    [JsonPropertyName("packageSize")]
    public long PackageSize { get; set; }
    [JsonPropertyName("packageSha256")]
    public string PackageSha256 { get; set; } = "";
}
