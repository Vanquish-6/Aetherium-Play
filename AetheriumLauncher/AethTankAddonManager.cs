using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace AcLegacyLauncher;

internal sealed record AethTankAddonStatus(
    bool ReleaseConfigured,
    bool ManagedStateExists,
    bool Installed,
    bool Enabled,
    string? CurrentVersion,
    string? PreviousVersion,
    bool PromptDismissed,
    bool DontAskAgain,
    bool GameRunning,
    string Message);

internal sealed class AethTankAddonState
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; }
    public string? CurrentVersion { get; set; }
    public string? PreviousVersion { get; set; }
    public bool PromptDismissed { get; set; }
    public bool DontAskAgain { get; set; }
}

/// <summary>
/// Installs only separately downloaded, signed runtime packages. This manager
/// contains no Aeth-Tank implementation or publishing credentials.
/// </summary>
internal sealed class AethTankAddonManager
{
    internal static AethTankAddonManager Default { get; } =
        new(AethTankReleasePolicy.CreateOptions());

    private const string EnvelopeFileName = "release-envelope.json";
    private readonly AethTankAddonOptions options;
    private readonly HttpClient http;
    private readonly Func<bool> gameRunning;
    private readonly Action<string, AethTankManifestPayload> verifyBinary;
    private string StatePath => Path.Combine(options.RootDirectory, "state.json");
    private string VersionsDirectory => Path.Combine(options.RootDirectory, "versions");
    internal string DataDirectory => Path.Combine(options.RootDirectory, "data");

    internal AethTankAddonManager(AethTankAddonOptions options)
        : this(options, CreateHttpClient(), IsAnyGameRunning, AethTankBinaryVerifier.Verify)
    {
    }

    // Dependencies are explicit for offline tests; production always uses the
    // compiled trust policy, real game check, and full Authenticode verifier.
    internal AethTankAddonManager(
        AethTankAddonOptions options,
        HttpClient http,
        Func<bool> gameRunning,
        Action<string, AethTankManifestPayload> verifyBinary)
    {
        this.options = options with
        {
            RootDirectory = Path.GetFullPath(options.RootDirectory),
            PublicKeys = new Dictionary<string, string>(options.PublicKeys, StringComparer.Ordinal),
            DownloadHosts = new HashSet<string>(options.DownloadHosts, StringComparer.OrdinalIgnoreCase),
        };
        this.http = http;
        this.gameRunning = gameRunning;
        this.verifyBinary = verifyBinary;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AetheriumPlay-AethTankAddons/1");
        return client;
    }

    private bool ReleaseConfigured
    {
        get
        {
            if (options.PublicKeys.Count == 0 || options.DownloadHosts.Count == 0) return false;
            try
            {
                // Feed hostname is the trusted compiled URI; package hostnames
                // are separately allowlisted because storage may use a CDN.
                var feed = new Uri(options.ManifestUrl, UriKind.Absolute);
                AethTankManifest.ValidateDownloadUri(options.ManifestUrl,
                    new HashSet<string>([feed.IdnHost], StringComparer.OrdinalIgnoreCase));
                return true;
            }
            catch (Exception ex) when (ex is UriFormatException or InvalidDataException)
            {
                return false;
            }
        }
    }

    internal AethTankAddonStatus GetStatus()
    {
        var managed = File.Exists(StatePath);
        try
        {
            EnsureSafeRoot(create: false);
            var state = ReadState();
            return new(
                ReleaseConfigured, managed, state.CurrentVersion is not null,
                state.CurrentVersion is not null && state.Enabled,
                state.CurrentVersion, state.PreviousVersion,
                state.PromptDismissed, state.DontAskAgain, gameRunning(),
                state.CurrentVersion is not null
                    ? $"Aeth-Tank {state.CurrentVersion} is {(state.Enabled ? "enabled for the next launch" : "disabled")}."
                    : ReleaseConfigured ? "Aeth-Tank is optional. Choose Install to add it."
                    : "Aeth-Tank release is not available yet.");
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            // An unreadable path or state is not evidence that this user never
            // made a managed choice. Suppress offers and developer fallback.
            return new(ReleaseConfigured, true, false, false, null, null,
                true, true, gameRunning(), $"Aeth-Tank is unavailable: {SafeError(ex)}");
        }
    }

    internal async Task<AethTankRelease?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!ReleaseConfigured) return null;
        var bytes = await DownloadManifestAsync(cancellationToken);
        var release = AethTankManifest.Verify(bytes, options);
        var state = ReadState();
        return state.CurrentVersion is null ||
            AethTankManifest.ParseVersion(release.Version) > AethTankManifest.ParseVersion(state.CurrentVersion)
            ? release : null;
    }

    internal async Task InstallAsync(
        AethTankRelease release,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        // Reverify exact signed bytes; a caller cannot mutate an earlier parsed
        // object to replace the URL, filenames, version, or accepted hashes.
        release = AethTankManifest.Verify(release.Envelope, options);
        if (!ReleaseConfigured)
            throw new InvalidOperationException("Aeth-Tank release is not available yet.");
        using var mutation = AcquireMutation();
        using var stateLock = AcquireStateLock();
        var state = ReadState();
        if (state.CurrentVersion is not null &&
            AethTankManifest.ParseVersion(release.Version) <= AethTankManifest.ParseVersion(state.CurrentVersion))
            throw new InvalidOperationException("Use Roll back to select an already installed older release.");
        EnsureDataDirectory();
        var stagingRoot = Path.Combine(options.RootDirectory, "staging");
        CreateSafeDirectory(stagingRoot);
        var staging = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        CreateSafeDirectory(staging);
        var archivePath = Path.Combine(staging, "download.zip");
        var payloadDirectory = Path.Combine(staging, "payload");
        CreateSafeDirectory(payloadDirectory);
        try
        {
            progress?.Report($"Downloading Aeth-Tank {release.Version}...");
            await DownloadPackageAsync(release, archivePath, cancellationToken);
            progress?.Report("Checking the signed Aeth-Tank package...");
            ExtractVerifiedPackage(archivePath, payloadDirectory, release.Payload);
            verifyBinary(Path.Combine(payloadDirectory, "aeth-tank.dll"), release.Payload);
            cancellationToken.ThrowIfCancellationRequested();
            if (gameRunning())
                throw new InvalidOperationException("Close all game clients before installing Aeth-Tank.");
            File.WriteAllBytes(Path.Combine(payloadDirectory, EnvelopeFileName), release.Envelope);
            CreateSafeDirectory(VersionsDirectory);
            var destination = VersionDirectory(release.Version);
            if (Directory.Exists(destination))
            {
                var existing = VerifyInstalled(release.Version);
                if (!existing.Payload.PackageSha256.Equals(
                        release.Payload.PackageSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An installed Aeth-Tank version cannot be replaced by different bytes.");
            }
            else
            {
                // The version directory becomes visible as one complete unit.
                Directory.Move(payloadDirectory, destination);
            }
            state.PreviousVersion = state.CurrentVersion;
            state.CurrentVersion = release.Version;
            state.Enabled = true;
            state.PromptDismissed = true;
            SaveState(state);
            progress?.Report($"Aeth-Tank {release.Version} is installed. It will load on the next game launch.");
        }
        finally
        {
            TryDeleteSafeTree(staging);
        }
    }

    internal void SetEnabled(bool enabled)
    {
        using var stateLock = AcquireStateLock();
        var state = ReadState();
        if (enabled)
        {
            if (state.CurrentVersion is null)
                throw new InvalidOperationException("Install Aeth-Tank before enabling it.");
            VerifyInstalled(state.CurrentVersion);
            EnsureDataDirectory();
        }
        state.Enabled = enabled;
        state.PromptDismissed = true;
        SaveState(state);
    }

    internal void RememberPrompt(bool dontAskAgain)
    {
        using var stateLock = AcquireStateLock();
        var state = ReadState();
        state.PromptDismissed = true;
        state.DontAskAgain = dontAskAgain || state.DontAskAgain;
        SaveState(state);
    }

    internal void Rollback()
    {
        using var mutation = AcquireMutation();
        using var stateLock = AcquireStateLock();
        var state = ReadState();
        if (state.PreviousVersion is null)
            throw new InvalidOperationException("No previous Aeth-Tank release is available.");
        VerifyInstalled(state.PreviousVersion);
        (state.CurrentVersion, state.PreviousVersion) = (state.PreviousVersion, state.CurrentVersion);
        SaveState(state);
    }

    internal void Uninstall()
    {
        using var mutation = AcquireMutation();
        using var stateLock = AcquireStateLock();
        AethTankAddonState state;
        try { state = ReadState(); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            // Uninstall is the recovery path for corrupt local settings. It
            // never trusts a path from that file and preserves the data root.
            state = new AethTankAddonState { DontAskAgain = true };
        }
        state.Enabled = false;
        state.CurrentVersion = state.PreviousVersion = null;
        state.PromptDismissed = true;
        SaveState(state);
        // User profiles remain under the stable data directory. Persisting the
        // disabled managed state also prevents an old developer marker fallback.
        if (Directory.Exists(VersionsDirectory))
        {
            EnsureSafeTree(VersionsDirectory);
            Directory.Delete(VersionsDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Acquire for every classic launch, even if the add-on is disabled.
    /// Keep this lease until the game exits so updating cannot race Play.
    /// Verification failures disable only the add-on for this launch.
    /// </summary>
    internal AethTankLaunchLease AcquireForLaunch(string clientProfileId, Action<string>? report = null)
    {
        FileStream stream;
        try
        {
            EnsureSafeRoot(create: true);
            stream = OpenLease(shared: true);
        }
        catch (Exception ex) when (ex is not AethTankAddonBusyException && IsExpectedFailure(ex))
        {
            // Optional add-on storage must not prevent the ordinary game from
            // starting. An actual installer lease collision still propagates.
            // Uncertain storage must never activate an unsigned dev fallback.
            report?.Invoke($"Aeth-Tank is unavailable for this launch: {SafeError(ex)}");
            return AethTankLaunchLease.Unavailable(DataDirectory);
        }
        var lease = new AethTankLaunchLease(
            stream, DataDirectory, File.Exists(StatePath));
        try
        {
            var state = ReadState();
            if (!state.Enabled || state.CurrentVersion is null) return lease;
            if (!AethTankManifest.SupportedClients.ContainsKey(clientProfileId))
            {
                report?.Invoke("Aeth-Tank is disabled for this launch: the selected client is not compatible.");
                return lease;
            }
            var pluginPath = Path.Combine(VersionDirectory(state.CurrentVersion), "aeth-tank.dll");
            // Pin the exact DLL against write/delete before hashing, parsing,
            // signature verification, and the subsequent native LoadLibrary.
            // Keep the handle for the game lifetime, including other slots.
            var binaryLock = new FileStream(pluginPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var release = VerifyInstalled(state.CurrentVersion);
                if (!AethTankManifest.SupportsClient(release.Payload, clientProfileId))
                {
                    report?.Invoke("Aeth-Tank is disabled for this launch: this release does not support the selected client.");
                    return lease;
                }
                EnsureDataDirectory();
                lease.PinBinary(binaryLock);
                binaryLock = null!;
                lease.PluginPath = pluginPath;
            }
            finally { binaryLock?.Dispose(); }
            report?.Invoke($"Verified Aeth-Tank {state.CurrentVersion} for this launch.");
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            try
            {
                lease.DisablePlugin(suppressDeveloperFallback: true);
                report?.Invoke($"Aeth-Tank was not enabled: {SafeError(ex)}");
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
        catch
        {
            lease.Dispose();
            throw;
        }
        return lease;
    }

    private AethTankRelease VerifyInstalled(string version)
    {
        var directory = VersionDirectory(version);
        EnsureSafeTree(directory);
        var envelopePath = Path.Combine(directory, EnvelopeFileName);
        var info = new FileInfo(envelopePath);
        if (!info.Exists || info.Length <= 0 || info.Length > AethTankManifest.MaximumEnvelopeBytes)
            throw new InvalidDataException("The installed Aeth-Tank release manifest is missing or invalid.");
        var release = AethTankManifest.Verify(File.ReadAllBytes(envelopePath), options);
        if (release.Version != version)
            throw new InvalidDataException("The installed Aeth-Tank directory does not match its signed version.");
        var actualNames = Directory.EnumerateFileSystemEntries(directory)
            .Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        var requiredNames = AethTankManifest.AllowedFiles.Append(EnvelopeFileName)
            .ToHashSet(StringComparer.Ordinal);
        if (!actualNames.SetEquals(requiredNames))
            throw new InvalidDataException("The installed Aeth-Tank directory contains unapproved files.");
        foreach (var file in release.Payload.Files)
            AethTankManifest.VerifyFile(Path.Combine(directory, file.Path), file);
        verifyBinary(Path.Combine(directory, "aeth-tank.dll"), release.Payload);
        return release;
    }

    private async Task<byte[]> DownloadManifestAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            new Uri(options.ManifestUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        // A missing file on the website falls through to the homepage. That page
        // starts with '<' and is not a signed release.
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Aeth-Tank release is not published yet.");
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBoundedAsync(
            response, AethTankManifest.MaximumEnvelopeBytes, cancellationToken).ConfigureAwait(false);
        if (LooksLikeHtml(bytes))
            throw new InvalidOperationException("Aeth-Tank release is not published yet.");
        return bytes;
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> bytes)
    {
        var start = 0;
        while (start < bytes.Length && bytes[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            start++;
        return start < bytes.Length && bytes[start] == (byte)'<';
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpResponseMessage response, int maximum, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is { } length && (length <= 0 || length > maximum))
            throw new InvalidDataException("The Aeth-Tank download has an invalid size.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        await CopyBoundedAsync(source, output, maximum, cancellationToken).ConfigureAwait(false);
        return output.ToArray();
    }

    private async Task DownloadPackageAsync(
        AethTankRelease release, string destination, CancellationToken cancellationToken)
    {
        var expected = release.Payload;
        var uri = AethTankManifest.ValidateDownloadUri(expected.PackageUrl, options.DownloadHosts);
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != expected.PackageSize)
            throw new InvalidDataException("The Aeth-Tank package length does not match its signed release.");
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var copied = await CopyBoundedAsync(source, output, expected.PackageSize, cancellationToken)
                .ConfigureAwait(false);
            if (copied != expected.PackageSize)
                throw new InvalidDataException("The Aeth-Tank package download was incomplete.");
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        AethTankManifest.VerifyFile(destination, new AethTankManifestFile
        {
            Path = "package", Size = expected.PackageSize, Sha256 = expected.PackageSha256,
        });
    }

    private static async Task<long> CopyBoundedAsync(
        Stream source, Stream output, long maximum, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) return copied;
            copied = checked(copied + read);
            if (copied > maximum)
                throw new InvalidDataException("The Aeth-Tank download exceeded its approved size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ExtractVerifiedPackage(
        string archivePath, string destination, AethTankManifestPayload release)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count != release.Files.Count)
            throw new InvalidDataException("The Aeth-Tank archive has unexpected files.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var expected = release.Files.SingleOrDefault(file => file.Path == entry.FullName);
            var unixKind = ((uint)entry.ExternalAttributes >> 16) & 0xf000;
            if (expected is null || !seen.Add(entry.FullName) || entry.Length != expected.Size ||
                unixKind is not (0 or 0x8000) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The Aeth-Tank archive contains an unapproved or linked entry.");
            var target = Path.Combine(destination, expected.Path);
            using (var input = entry.Open())
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = input.Read(buffer)) != 0)
                {
                    copied = checked(copied + read);
                    if (copied > expected.Size)
                        throw new InvalidDataException("An unpacked Aeth-Tank file exceeded its approved size.");
                    output.Write(buffer, 0, read);
                }
                if (copied != expected.Size)
                    throw new InvalidDataException("An unpacked Aeth-Tank file was incomplete.");
                output.Flush(flushToDisk: true);
            }
            AethTankManifest.VerifyFile(target, expected);
        }
    }

    private AethTankAddonState ReadState()
    {
        EnsureSafeRoot(create: false);
        var attributes = ReadAttributesIfPresent(StatePath);
        if (attributes is null) return new AethTankAddonState();
        if ((attributes & FileAttributes.Directory) != 0)
            throw new InvalidDataException("The Aeth-Tank settings path is not a file.");
        EnsureNoReparseComponents(StatePath);
        var info = new FileInfo(StatePath);
        if (info.Length <= 0 || info.Length > 4096)
            throw new InvalidDataException("The Aeth-Tank add-on settings are invalid.");
        var bytes = File.ReadAllBytes(StatePath);
        AethTankManifest.RejectDuplicateProperties(bytes);
        var state = JsonSerializer.Deserialize<AethTankAddonState>(bytes, AethTankManifest.Json);
        if (state is null || state.SchemaVersion != 1 ||
            state.Enabled && state.CurrentVersion is null ||
            state.CurrentVersion is not null && state.CurrentVersion == state.PreviousVersion)
            throw new InvalidDataException("The Aeth-Tank add-on settings are invalid.");
        if (state.CurrentVersion is not null) AethTankManifest.ParseVersion(state.CurrentVersion);
        if (state.PreviousVersion is not null) AethTankManifest.ParseVersion(state.PreviousVersion);
        return state;
    }

    private void SaveState(AethTankAddonState state)
    {
        EnsureSafeRoot(create: true);
        var temp = Path.Combine(options.RootDirectory, "state-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state, AethTankManifest.Json);
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temp, StatePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private string VersionDirectory(string version)
    {
        AethTankManifest.ParseVersion(version);
        var path = Path.Combine(VersionsDirectory, version);
        EnsureNoReparseComponents(path);
        return path;
    }

    private FileStream AcquireMutation()
    {
        EnsureSafeRoot(create: true);
        var stream = OpenLease(shared: false);
        try
        {
            if (gameRunning())
                throw new InvalidOperationException("Close all game clients before changing installed Aeth-Tank files.");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private FileStream OpenLease(bool shared)
    {
        var path = Path.Combine(options.RootDirectory, "launch.lock");
        EnsureNoReparseComponents(path);
        // Initialize without truncating. Read leases can then be shared by
        // multiple launcher instances and account slots.
        if (!File.Exists(path))
        {
            try { using var create = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite); }
            catch (IOException) when (File.Exists(path)) { }
        }
        try
        {
            return new FileStream(path, FileMode.Open,
                shared ? FileAccess.Read : FileAccess.ReadWrite,
                shared ? FileShare.Read : FileShare.None);
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        {
            throw new AethTankAddonBusyException(shared
                ? "Aeth-Tank installation is in progress. Wait for it to finish before launching the game."
                : "Close all game clients and finish other add-on actions before changing Aeth-Tank files.", ex);
        }
    }

    private FileStream AcquireStateLock()
    {
        EnsureSafeRoot(create: true);
        var path = Path.Combine(options.RootDirectory, "state.lock");
        EnsureNoReparseComponents(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex)
        {
            throw new InvalidOperationException("Another Aeth-Tank settings operation is in progress.", ex);
        }
    }

    private void EnsureDataDirectory()
    {
        if (DataDirectory.Length > 110)
            throw new InvalidOperationException("The Aeth-Tank settings location is too long for the classic client.");
        CreateSafeDirectory(DataDirectory);
        var probe = Path.Combine(DataDirectory, "write-" + Guid.NewGuid().ToString("N") + ".tmp");
        using var output = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1, FileOptions.DeleteOnClose);
        output.WriteByte(0);
        output.Flush(flushToDisk: true);
    }

    private void EnsureSafeRoot(bool create)
    {
        EnsureNoReparseComponents(options.RootDirectory);
        var attributes = ReadAttributesIfPresent(options.RootDirectory);
        if (attributes is not null && (attributes & FileAttributes.Directory) == 0)
            throw new InvalidDataException("The Aeth-Tank add-on location is not a directory.");
        if (create) CreateSafeDirectory(options.RootDirectory);
    }

    private static void CreateSafeDirectory(string path)
    {
        EnsureNoReparseComponents(path);
        Directory.CreateDirectory(path);
        EnsureNoReparseComponents(path);
    }

    private static void EnsureNoReparseComponents(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 ||
            !char.IsAsciiLetter(path[0]) || path[1] != ':' ||
            path[2] is not ('\\' or '/') || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Aeth-Tank files must use a local absolute drive path.");
        var fullPath = Path.GetFullPath(path);
        for (var current = fullPath; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            var attributes = ReadAttributesIfPresent(current);
            if (attributes is null) continue;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Aeth-Tank add-on paths cannot contain links or junctions.");
            if (current != fullPath && (attributes & FileAttributes.Directory) == 0)
                throw new InvalidDataException("An Aeth-Tank parent path is not a directory.");
        }
    }

    private static FileAttributes? ReadAttributesIfPresent(string path)
    {
        // File.Exists silently returns false for inaccessible paths. Only a
        // genuine missing path may be treated as an absent managed choice.
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static void EnsureSafeTree(string directory)
    {
        EnsureNoReparseComponents(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The installed Aeth-Tank release directory is missing.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            EnsureNoReparseComponents(entry);
            if (Directory.Exists(entry)) EnsureSafeTree(entry);
        }
    }

    private static void TryDeleteSafeTree(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            EnsureSafeTree(directory);
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            // Leave an unactivated staging folder for a later cleanup rather
            // than following an unexpected link or hiding the original error.
        }
    }

    private static bool IsAnyGameRunning()
    {
        try
        {
            foreach (var name in new[] { "client", "acclient" })
            {
                var processes = Process.GetProcessesByName(name);
                try { if (processes.Length != 0) return true; }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsExpectedFailure(Exception ex) =>
        ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Security.SecurityException or
            InvalidOperationException or CryptographicException or JsonException or ArgumentException or
            BadImageFormatException or OverflowException;

    private static string SafeError(Exception ex) => ex is JsonException
        ? "The saved add-on metadata is invalid. Use the Add-ons page to review it."
        : ex.Message;
}

internal sealed class AethTankAddonBusyException(string message, Exception inner)
    : InvalidOperationException(message, inner)
{
}

internal sealed class AethTankLaunchLease : IDisposable
{
    private FileStream? file;
    private FileStream? binary;
    private EventHandler? exitHandler;
    private Process? process;
    private readonly bool unavailable;
    internal string? PluginPath { get; set; }
    internal string DataDirectory { get; }
    internal bool ManagedStateExists { get; private set; }

    internal AethTankLaunchLease(FileStream file, string dataDirectory, bool managedStateExists)
    {
        this.file = file;
        DataDirectory = dataDirectory;
        ManagedStateExists = managedStateExists;
    }

    private AethTankLaunchLease(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        ManagedStateExists = true;
        unavailable = true;
    }

    internal static AethTankLaunchLease Unavailable(string dataDirectory) => new(dataDirectory);

    internal void PinBinary(FileStream stream)
    {
        if (file is null || binary is not null)
            throw new InvalidOperationException("The Aeth-Tank binary lease is not available.");
        binary = stream;
    }

    internal void DisablePlugin(bool suppressDeveloperFallback = false)
    {
        PluginPath = null;
        if (suppressDeveloperFallback) ManagedStateExists = true;
        Interlocked.Exchange(ref binary, null)?.Dispose();
    }

    internal void HoldUntilExit(Process launchedProcess)
    {
        ArgumentNullException.ThrowIfNull(launchedProcess);
        if (unavailable) return; // No add-on path or handle is handed to this game.
        if (file is null || process is not null)
            throw new InvalidOperationException("The Aeth-Tank launch lease is not available.");
        process = launchedProcess;
        exitHandler = (_, _) => Dispose();
        launchedProcess.Exited += exitHandler;
        try
        {
            launchedProcess.EnableRaisingEvents = true;
            if (launchedProcess.HasExited) Dispose();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        DisablePlugin();
        Interlocked.Exchange(ref file, null)?.Dispose();
        var observed = Interlocked.Exchange(ref process, null);
        var handler = Interlocked.Exchange(ref exitHandler, null);
        if (observed is not null && handler is not null)
        {
            try { observed.Exited -= handler; }
            catch (InvalidOperationException) { }
        }
    }
}
