namespace AcLegacyLauncher;

/// <summary>
/// One saved account/launch identity for multi-client use.
/// The main parchment form stays independent; profiles live in profiles.json.
/// </summary>
public sealed class ClientProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public string InstallPath { get; set; } = LaunchConfig.DefaultInstallPath;

    public string AccountName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Host { get; set; } = AetheriumInstallationConfiguration.DefaultHost;

    public int Port { get; set; } = AetheriumInstallationConfiguration.DefaultPort;

    public string Zone { get; set; } = string.Empty;

    public bool UseNoDisplayMode { get; set; }

    public bool SeedSafeGraphics { get; set; }

    public LaunchConfig ToLaunchConfig()
    {
        return new LaunchConfig
        {
            InstallPath = InstallPath,
            TicketKey = AccountName,
            Host = Host,
            Port = Port,
            VArg = Password,
            ZArg = Zone,
            UseNoDisplayMode = UseNoDisplayMode,
            SeedSafeGraphics = SeedSafeGraphics,
        };
    }

    public static ClientProfile FromLaunchConfig(LaunchConfig config, string? displayName = null)
    {
        var account = config.TicketKey.Trim();
        return new ClientProfile
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? (string.IsNullOrWhiteSpace(account) ? "Profile" : account)
                : displayName.Trim(),
            InstallPath = config.InstallPath,
            AccountName = account,
            Password = config.VArg,
            Host = config.Host,
            Port = config.Port,
            Zone = config.ZArg,
            UseNoDisplayMode = config.UseNoDisplayMode,
            SeedSafeGraphics = config.SeedSafeGraphics,
        };
    }
}

public sealed class ProfileStoreData
{
    public List<ClientProfile> Profiles { get; set; } = new();
}
