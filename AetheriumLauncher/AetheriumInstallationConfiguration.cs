using System.Text.Json;

namespace AcLegacyLauncher;

internal static class AetheriumInstallationConfiguration
{
    public const string DefaultHost = "play.aetherium.ac";
    public const int WhiteServerPort = 9000;
    public const int RedServerPort = 9100;
    public const int DefaultPort = WhiteServerPort;
    private const string GamePathMarkerFileName = "game.install.path";

    public static void Configure(string gameInstallDirectory, string? skinName = null)
    {
        var fullGameDirectory = Path.GetFullPath(gameInstallDirectory);
        if (!File.Exists(Path.Combine(fullGameDirectory, "client.exe")) ||
            !File.Exists(Path.Combine(fullGameDirectory, "portal.dat")) ||
            !File.Exists(Path.Combine(fullGameDirectory, "cell.dat")))
        {
            throw new InvalidDataException(
                $"The selected game directory is incomplete: {fullGameDirectory}");
        }

        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, GamePathMarkerFileName),
            fullGameDirectory);

        var configPath = Path.Combine(fullGameDirectory, "launcher.json");
        LaunchConfig config;
        try
        {
            config = File.Exists(configPath)
                ? JsonSerializer.Deserialize<LaunchConfig>(File.ReadAllText(configPath))
                    ?? new LaunchConfig()
                : new LaunchConfig();
        }
        catch
        {
            config = new LaunchConfig();
        }

        config.InstallPath = fullGameDirectory;
        config.Host = DefaultHost;
        config.Port = DefaultPort;
        if (!string.IsNullOrWhiteSpace(skinName))
        {
            config.Skin = skinName.Trim();
        }

        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        if (WineRuntime.IsWine)
        {
            GraphicsBootstrap.SeedSafeGraphicsSettings();
            GraphicsBootstrap.SeedUserPreferencesDisplay(fullScreen: true);
        }
    }

    public static string? TryReadGameInstallDirectory()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, GamePathMarkerFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllText(path).Trim();
            return Directory.Exists(value) &&
                   File.Exists(Path.Combine(value, "client.exe"))
                ? Path.GetFullPath(value)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
