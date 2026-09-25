namespace AcLegacyLauncher;

using System.Text.Json.Serialization;

public sealed class AccountSlot
{
    public string Id { get; set; } = "1";

    public string Account { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int Port { get; set; } = AetheriumInstallationConfiguration.DefaultPort;
}

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

    public List<AccountSlot> Slots { get; set; } = [];

    public string SelectedSlotId { get; set; } = "1";

    [JsonIgnore]
    public bool PreserveLegacyMulticlient { get; set; }

    [JsonIgnore]
    public bool AnotherClientRunning { get; set; }

    public void EnsureSlots()
    {
        var incoming = Slots ?? [];
        var migrateLegacyAccount = incoming.Count == 0;
        var slotOne = incoming.FirstOrDefault(slot => slot.Id == "1");
        var slotTwo = incoming.FirstOrDefault(slot => slot.Id == "2");
        if (slotOne is null)
        {
            slotOne = new AccountSlot
            {
                Id = "1",
                Account = migrateLegacyAccount ? TicketKey ?? string.Empty : string.Empty,
                Password = migrateLegacyAccount ? VArg ?? string.Empty : string.Empty,
                Port = migrateLegacyAccount && Port > 0
                    ? Port
                    : AetheriumInstallationConfiguration.DefaultPort,
            };
        }

        slotTwo ??= new AccountSlot
        {
            Id = "2",
            Port = AetheriumInstallationConfiguration.DefaultPort,
        };

        slotOne.Account ??= string.Empty;
        slotOne.Password ??= string.Empty;
        slotTwo.Account ??= string.Empty;
        slotTwo.Password ??= string.Empty;
        if (slotOne.Port <= 0)
        {
            slotOne.Port = AetheriumInstallationConfiguration.DefaultPort;
        }

        if (slotTwo.Port <= 0)
        {
            slotTwo.Port = AetheriumInstallationConfiguration.DefaultPort;
        }

        Slots = [slotOne, slotTwo];
        if (SelectedSlotId is not "1" and not "2")
        {
            SelectedSlotId = "1";
        }
    }

    public AccountSlot SelectedSlot
    {
        get
        {
            EnsureSlots();
            return Slots.First(slot => slot.Id == SelectedSlotId);
        }
    }

    public void SyncSelectedSlotToLaunchFields()
    {
        var slot = SelectedSlot;
        TicketKey = slot.Account.Trim();
        VArg = slot.Password;
        Port = slot.Port;
    }
}
