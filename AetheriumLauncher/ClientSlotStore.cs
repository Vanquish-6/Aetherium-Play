using Microsoft.Win32;

namespace AcLegacyLauncher;

internal sealed class SlotLaunchEnvironment
{
    public required string SlotId { get; init; }

    public required string SlotRoot { get; init; }

    public required string SlotDocuments { get; init; }

    public required string RetailDocuments { get; init; }

    public required string SlotMaps { get; init; }

    public required string RegistrySubKey { get; init; }

    public IReadOnlyDictionary<string, string> ToProcessEnvironment()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ClientSlotStore.SlotIdVariable] = SlotId,
            [ClientSlotStore.SlotDocsVariable] = SlotDocuments,
            [ClientSlotStore.RetailDocsVariable] = RetailDocuments,
            [ClientSlotStore.SlotMapsVariable] = SlotMaps,
            [ClientSlotStore.RegSubKeyVariable] = RegistrySubKey,
        };
    }
}

internal static class ClientSlotStore
{
    internal const string SlotIdVariable = "AETHERIUM_SLOT_ID";
    internal const string SlotDocsVariable = "AETHERIUM_SLOT_DOCS";
    internal const string RetailDocsVariable = "AETHERIUM_RETAIL_DOCS";
    internal const string SlotMapsVariable = "AETHERIUM_SLOT_MAPS";
    internal const string RegSubKeyVariable = "AETHERIUM_REG_SUBKEY";
    internal const string SeededValueName = "AetheriumSlotSeeded";

    private const string PreferencesFolderName = "Asheron's Call";

    internal static SlotLaunchEnvironment Prepare(
        string slotId,
        string installDirectory,
        bool safeGraphics)
    {
        return Prepare(slotId, installDirectory, safeGraphics, paths: null);
    }

    internal static SlotLaunchEnvironment Prepare(
        string slotId,
        string installDirectory,
        bool safeGraphics,
        SlotStorePaths? paths)
    {
        var environment = CreateEnvironment(slotId, paths);
        SeedFiles(environment, installDirectory);
        if (paths?.SkipRegistry != true)
        {
            if (paths?.SlotGraphics is not null)
            {
                SeedGraphicsKey(paths.SlotGraphics, paths.MachineGraphics, safeGraphics);
            }
            else
            {
                using var slot = Registry.CurrentUser.CreateSubKey(environment.RegistrySubKey, writable: true)
                    ?? throw new InvalidOperationException(
                        "Could not create the private graphics key for this account slot.");
                RegistryKey? machine = null;
                try
                {
                    using var machineBase = RegistryKey.OpenBaseKey(
                        RegistryHive.LocalMachine,
                        RegistryView.Registry32);
                    machine = machineBase.OpenSubKey(
                        @"SOFTWARE\Microsoft\Microsoft Games\Asheron's Call\1.00",
                        writable: false);
                    SeedGraphicsKey(slot, machine, safeGraphics);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException
                    or System.Security.SecurityException
                    or PlatformNotSupportedException)
                {
                    SeedGraphicsKey(slot, null, safeGraphics);
                }
                finally
                {
                    machine?.Dispose();
                }
            }
        }

        File.WriteAllText(Path.Combine(environment.SlotRoot, "seeded.flag"), "1");
        return environment;
    }

    internal static SlotLaunchEnvironment CreateEnvironment(string slotId, SlotStorePaths? paths = null)
    {
        if (slotId is not "1" and not "2")
        {
            throw new InvalidOperationException("Account slot must be 1 or 2.");
        }

        var slotRoot = paths?.SlotRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AetheriumPlay",
            "slots",
            slotId);
        var retailDocuments = paths?.RetailDocuments ??
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Directory.CreateDirectory(slotRoot);
        var slotDocuments = Path.Combine(slotRoot, "Documents");
        var slotMaps = Path.Combine(slotRoot, "game", "ui", "inputmaps");
        Directory.CreateDirectory(slotDocuments);
        Directory.CreateDirectory(Path.Combine(slotDocuments, PreferencesFolderName));
        Directory.CreateDirectory(slotMaps);
        return new SlotLaunchEnvironment
        {
            SlotId = slotId,
            SlotRoot = slotRoot,
            SlotDocuments = slotDocuments,
            RetailDocuments = retailDocuments,
            SlotMaps = slotMaps,
            RegistrySubKey = $@"Software\AetheriumPlay\slots\{slotId}",
        };
    }

    internal static void SeedFiles(SlotLaunchEnvironment environment, string installDirectory)
    {
        var preferencesDirectory = Path.Combine(environment.SlotDocuments, PreferencesFolderName);
        Directory.CreateDirectory(preferencesDirectory);
        var markerPath = Path.Combine(environment.SlotRoot, "seeded.flag");
        if (!File.Exists(markerPath))
        {
            var retailPreferences = Path.Combine(environment.RetailDocuments, PreferencesFolderName);
            CopyMissingFiles(retailPreferences, preferencesDirectory, "UserPreferences.ini");
            CopyMissingFiles(retailPreferences, preferencesDirectory, "*.keymap");
            CopyMissingFiles(
                Path.Combine(installDirectory, "game", "ui", "inputmaps"),
                environment.SlotMaps,
                "*.*");
        }

        var preferencesPath = Path.Combine(preferencesDirectory, "UserPreferences.ini");
        if (!File.Exists(preferencesPath))
        {
            File.WriteAllLines(preferencesPath,
            [
                "[Display]",
                "RefreshRate=Auto",
                "Resolution=800x600",
                "FullScreen=True",
                "SyncToRefresh=False",
            ]);
        }
    }

    internal static void SeedGraphicsKey(RegistryKey slotKey, RegistryKey? machineKey, bool safeDefaults)
    {
        ArgumentNullException.ThrowIfNull(slotKey);
        var seeded = slotKey.GetValue(SeededValueName) is int value && value != 0;
        if (!seeded && machineKey is not null)
        {
            foreach (var name in machineKey.GetValueNames())
            {
                if (name.Equals(SeededValueName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var data = machineKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (data is null)
                {
                    continue;
                }

                slotKey.SetValue(name, data, machineKey.GetValueKind(name));
            }
        }

        GraphicsBootstrap.RepairPrivateSlotGraphics(slotKey, safeDefaults);
        slotKey.SetValue(SeededValueName, 1, RegistryValueKind.DWord);
    }

    private static void CopyMissingFiles(string? sourceDirectory, string destinationDirectory, string searchPattern)
    {
        Directory.CreateDirectory(destinationDirectory);
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var source in Directory.EnumerateFiles(sourceDirectory, searchPattern))
        {
            var destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
            if (!File.Exists(destination))
            {
                File.Copy(source, destination, overwrite: false);
            }
        }
    }
}

internal sealed class SlotStorePaths
{
    public string? SlotRoot { get; init; }

    public string? RetailDocuments { get; init; }

    public RegistryKey? MachineGraphics { get; init; }

    public RegistryKey? SlotGraphics { get; init; }

    public bool SkipRegistry { get; init; }
}
