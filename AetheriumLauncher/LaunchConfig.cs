namespace AcLegacyLauncher;

using System.Text.Json.Serialization;

public sealed class LaunchConfig
{
    public const string DefaultInstallPath = @"C:\asheronscalldm";

    public string InstallPath { get; set; } = DefaultInstallPath;

    public string TicketKey { get; set; } = string.Empty;

    public string Host { get; set; } = AetheriumInstallationConfiguration.DefaultHost;

    public int Port { get; set; } = AetheriumInstallationConfiguration.DefaultPort;

    public string VArg { get; set; } = string.Empty;

    public string ZArg { get; set; } = string.Empty;

    public bool UseNoDisplayMode { get; set; } = false;

    public bool SeedSafeGraphics { get; set; } = false;

    public string? Skin { get; set; }

    [JsonIgnore]
    public bool PreserveLegacyMulticlient { get; set; }
}
