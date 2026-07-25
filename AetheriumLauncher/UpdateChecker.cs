using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AcLegacyLauncher;

/// <summary>
/// Checks GitHub Releases for a newer AetheriumPlaySetup.exe and offers to install it.
/// Configure <see cref="GitHubOwner"/> / <see cref="GitHubRepo"/> to match the public repo.
/// </summary>
internal static class UpdateChecker
{
    // Change these if the public GitHub repo moves.
    public const string GitHubOwner = "Vanquish-6";
    public const string GitHubRepo = "Aetherium-Play";

    private const string SetupAssetName = "AetheriumPlaySetup.exe";
    private const string UserAgent = "AetheriumLauncher-UpdateChecker";

    private static readonly HttpClient Http = CreateClient();
    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AcLegacyLauncher");
    private static readonly string SkipFilePath = Path.Combine(StateDirectory, "skipped-update.txt");

    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    public static async Task CheckForUpdatesAsync(
        IWin32Window? owner,
        bool interactive,
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            report?.Invoke("Checking for launcher updates...");
            var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(true);
            if (release is null || release.TagVersion is null)
            {
                if (interactive)
                {
                    MessageBox.Show(
                        owner,
                        "Could not read the latest GitHub release. Check that the repo is public and has a release.",
                        "Aetherium Launcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            if (release.TagVersion <= CurrentVersion)
            {
                report?.Invoke($"Launcher is up to date (v{FormatVersion(CurrentVersion)}).");
                if (interactive)
                {
                    MessageBox.Show(
                        owner,
                        $"You are on the latest version (v{FormatVersion(CurrentVersion)}).",
                        "Aetherium Launcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            if (!interactive && IsSkipped(release.TagVersion))
            {
                report?.Invoke($"Update v{FormatVersion(release.TagVersion)} available (skipped for now).");
                return;
            }

            var asset = release.FindSetupAsset();
            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            {
                if (interactive)
                {
                    MessageBox.Show(
                        owner,
                        $"Update v{FormatVersion(release.TagVersion)} exists, but no {SetupAssetName} asset was attached.",
                        "Aetherium Launcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return;
            }

            var notes = string.IsNullOrWhiteSpace(release.Body)
                ? "A newer Aetherium Play setup is available."
                : release.Body.Trim();
            if (notes.Length > 600)
            {
                notes = notes[..600] + "...";
            }

            var result = MessageBox.Show(
                owner,
                $"Update available: v{FormatVersion(CurrentVersion)} → v{FormatVersion(release.TagVersion)}\n\n" +
                $"{notes}\n\n" +
                "Download and run the new installer now?",
                "Aetherium Play Update",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Information);

            if (result == DialogResult.Cancel || result == DialogResult.No)
            {
                RememberSkip(release.TagVersion);
                report?.Invoke($"Update v{FormatVersion(release.TagVersion)} deferred.");
                return;
            }

            report?.Invoke($"Downloading v{FormatVersion(release.TagVersion)}...");
            var setupPath = await DownloadSetupAsync(asset.BrowserDownloadUrl, cancellationToken)
                .ConfigureAwait(true);

            report?.Invoke("Starting installer...");
            Process.Start(new ProcessStartInfo
            {
                FileName = setupPath,
                UseShellExecute = true,
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore.
        }
        catch (Exception ex)
        {
            report?.Invoke($"Update check failed: {ex.Message}");
            if (interactive)
            {
                MessageBox.Show(
                    owner,
                    $"Update check failed:\n{ex.Message}",
                    "Aetherium Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
        using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(
                stream,
                UpdateJsonContext.Default.GitHubRelease,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<string> DownloadSetupAsync(string url, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AetheriumPlayUpdates");
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, SetupAssetName);

        using var response = await Http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var output = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            82_192,
            useAsync: true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return targetPath;
    }

    private static Version ReadCurrentVersion()
    {
        var informational = typeof(UpdateChecker).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var cleaned = informational.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
            if (Version.TryParse(NormalizeVersion(cleaned), out var parsed))
            {
                return parsed;
            }
        }

        return typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static string NormalizeVersion(string value)
    {
        var match = Regex.Match(value.Trim(), @"\d+(?:\.\d+){0,3}");
        return match.Success ? match.Value : value.Trim().TrimStart('v', 'V');
    }

    private static string FormatVersion(Version version)
    {
        if (version.Revision > 0)
        {
            return version.ToString(4);
        }

        if (version.Build > 0)
        {
            return version.ToString(3);
        }

        return version.ToString(2);
    }

    private static bool IsSkipped(Version version)
    {
        try
        {
            if (!File.Exists(SkipFilePath))
            {
                return false;
            }

            var text = File.ReadAllText(SkipFilePath).Trim();
            return Version.TryParse(NormalizeVersion(text), out var skipped) && skipped == version;
        }
        catch
        {
            return false;
        }
    }

    private static void RememberSkip(Version version)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            File.WriteAllText(SkipFilePath, FormatVersion(version));
        }
        catch
        {
            // Best-effort only.
        }
    }

    internal sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset>? Assets { get; set; }

        public Version? TagVersion
        {
            get
            {
                if (string.IsNullOrWhiteSpace(TagName))
                {
                    return null;
                }

                return Version.TryParse(NormalizeVersion(TagName), out var version) ? version : null;
            }
        }

        public GitHubReleaseAsset? FindSetupAsset()
        {
            return Assets?.FirstOrDefault(asset =>
                string.Equals(asset.Name, SetupAssetName, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}

[JsonSerializable(typeof(UpdateChecker.GitHubRelease))]
[JsonSerializable(typeof(UpdateChecker.GitHubReleaseAsset))]
[JsonSerializable(typeof(List<UpdateChecker.GitHubReleaseAsset>))]
internal partial class UpdateJsonContext : JsonSerializerContext
{
}
