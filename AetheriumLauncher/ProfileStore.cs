using System.Text.Json;

namespace AcLegacyLauncher;

public static class ProfileStore
{
    private const string FileName = "profiles.json";

    private static readonly string StoreDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AcLegacyLauncher");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string StorePath => Path.Combine(StoreDirectory, FileName);

    public static ProfileStoreData Load()
    {
        if (!File.Exists(StorePath))
        {
            return new ProfileStoreData();
        }

        try
        {
            var data = JsonSerializer.Deserialize<ProfileStoreData>(File.ReadAllText(StorePath));
            if (data is null)
            {
                return new ProfileStoreData();
            }

            data.Profiles ??= new List<ClientProfile>();
            return data;
        }
        catch
        {
            return new ProfileStoreData();
        }
    }

    public static void Save(ProfileStoreData data)
    {
        Directory.CreateDirectory(StoreDirectory);
        data.Profiles ??= new List<ClientProfile>();
        File.WriteAllText(StorePath, JsonSerializer.Serialize(data, JsonOptions));
    }

    public static ClientProfile AddOrUpdate(ClientProfile profile)
    {
        var data = Load();
        var existing = data.Profiles.FirstOrDefault(p =>
            p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            data.Profiles.Add(profile);
        }
        else
        {
            existing.DisplayName = profile.DisplayName;
            existing.InstallPath = profile.InstallPath;
            existing.AccountName = profile.AccountName;
            existing.Password = profile.Password;
            existing.Host = profile.Host;
            existing.Port = profile.Port;
            existing.Zone = profile.Zone;
            existing.UseNoDisplayMode = profile.UseNoDisplayMode;
            existing.SeedSafeGraphics = profile.SeedSafeGraphics;
            profile = existing;
        }

        Save(data);
        return profile;
    }

    public static bool Remove(string profileId)
    {
        var data = Load();
        var removed = data.Profiles.RemoveAll(p =>
            p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            Save(data);
        }

        return removed;
    }

    /// <summary>
    /// True when saved profiles point at more than one distinct client install
    /// (e.g. main + admin folders).
    /// </summary>
    public static bool HasMultipleInstallRoots()
    {
        return GetDistinctInstallDirectories().Count > 1;
    }

    public static IReadOnlyList<string> GetDistinctInstallDirectories()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in Load().Profiles)
        {
            var resolved = ClientLauncher.ResolveInstallDirectory(profile.InstallPath);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                roots.Add(Path.GetFullPath(resolved));
            }
        }

        return roots.ToList();
    }

    public static int CountProfilesForInstall(string installDirectory)
    {
        var target = Path.GetFullPath(installDirectory);
        return Load().Profiles.Count(profile =>
        {
            var resolved = ClientLauncher.ResolveInstallDirectory(profile.InstallPath);
            return !string.IsNullOrWhiteSpace(resolved)
                && Path.GetFullPath(resolved).Equals(target, StringComparison.OrdinalIgnoreCase);
        });
    }
}
