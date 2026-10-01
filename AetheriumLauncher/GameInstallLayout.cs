namespace AcLegacyLauncher;

internal static class GameInstallLayout
{
    public const string ClientFileName = "client.exe";

    public static readonly IReadOnlyList<string> RequiredDataFiles = ["portal.dat", "cell.dat"];

    // client.exe imports these by name. Windows resolves them from the game
    // folder first, then the 32-bit system folder, and the client refuses to
    // start with a loader error if any one is absent.
    public static readonly IReadOnlyList<string> RequiredClientLibraries =
        ["ACmvhlp.dll", "msvcp70.dll", "msvcr70.dll", "msvci70.dll"];

    private static readonly string[] DirectCandidates =
    [
        LaunchConfig.DefaultInstallPath,
        @"C:\Turbine\Asheron's Call",
        @"C:\Program Files (x86)\Turbine\Asheron's Call",
        @"C:\Program Files\Turbine\Asheron's Call",
        @"C:\Turbine Entertainment Software\Asheron's Call",
        @"C:\Program Files (x86)\Turbine Entertainment Software\Asheron's Call",
        @"C:\Program Files\Turbine Entertainment Software\Asheron's Call",
    ];

    // Different AC releases and locales spell "Asheron's Call" with different
    // apostrophes, so the publisher folders are scanned for any game subfolder.
    private static readonly string[] PublisherFolders =
    [
        @"C:\Turbine",
        @"C:\Turbine Entertainment Software",
        @"C:\Program Files\Turbine",
        @"C:\Program Files (x86)\Turbine",
        @"C:\Program Files\Turbine Entertainment Software",
        @"C:\Program Files (x86)\Turbine Entertainment Software",
    ];

    public static IReadOnlyList<string> FindMissingFiles(string directory, bool includeClient = true)
    {
        var missing = new List<string>();
        if (includeClient && !HasNonEmptyFile(directory, ClientFileName))
        {
            missing.Add(ClientFileName);
        }

        missing.AddRange(RequiredDataFiles.Where(name => !HasNonEmptyFile(directory, name)));
        missing.AddRange(RequiredClientLibraries.Where(name =>
            !HasNonEmptyFile(directory, name) && !HasNonEmptyFile(Environment.SystemDirectory, name)));
        return missing;
    }

    public static bool IsComplete(string directory) => FindMissingFiles(directory).Count == 0;

    public static void EnsureComplete(string directory, bool includeClient = true)
    {
        var missing = FindMissingFiles(directory, includeClient);
        if (missing.Count != 0)
        {
            throw new InvalidDataException(DescribeIncomplete(directory, missing));
        }
    }

    public static string DescribeIncomplete(string directory, IReadOnlyList<string> missing)
    {
        var message =
            "This folder is not a complete Asheron's Call: Dark Majesty install:" + Environment.NewLine +
            directory + Environment.NewLine + Environment.NewLine +
            "Missing: " + string.Join(", ", missing) + Environment.NewLine + Environment.NewLine;

        var complete = FindCompleteInstall();
        if (complete is not null &&
            !string.Equals(Path.GetFullPath(complete), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
        {
            return message +
                "A complete install was found at:" + Environment.NewLine +
                complete + Environment.NewLine + Environment.NewLine +
                "Click Browse next to Install and pick that folder.";
        }

        return message +
            "Click Browse next to Install and pick the folder the Dark Majesty installer put the " +
            "game in (client.exe, portal.dat, cell.dat and msvcp70.dll sit side by side there). " +
            "If you can't find one, run AetheriumPlaySetup.exe again and let it install the game.";
    }

    // Returns the first complete install, or the first folder that at least has
    // client.exe so Play can explain exactly which files are missing.
    public static string? FindDefaultInstallDirectory()
    {
        string? partial = null;
        foreach (var candidate in EnumerateCandidates())
        {
            if (IsComplete(candidate))
            {
                return candidate;
            }

            partial ??= candidate;
        }

        return partial;
    }

    public static string? FindCompleteInstall() =>
        EnumerateCandidates().FirstOrDefault(IsComplete);

    // Accepts the folder itself, or a game folder one or two levels below it
    // (players often pick "C:\Turbine" or "Program Files (x86)").
    public static string? FindCompleteInstallUnder(string selectedDirectory)
    {
        if (IsComplete(selectedDirectory))
        {
            return selectedDirectory;
        }

        foreach (var child in SafeEnumerateDirectories(selectedDirectory))
        {
            if (IsComplete(child))
            {
                return child;
            }
        }

        foreach (var child in SafeEnumerateDirectories(selectedDirectory))
        {
            foreach (var grandchild in SafeEnumerateDirectories(child))
            {
                if (IsComplete(grandchild))
                {
                    return grandchild;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var configured = AetheriumInstallationConfiguration.TryReadGameInstallDirectory();
        if (configured is not null)
        {
            yield return configured;
        }

        foreach (var candidate in DirectCandidates)
        {
            if (File.Exists(Path.Combine(candidate, ClientFileName)))
            {
                yield return candidate;
            }
        }

        foreach (var parent in PublisherFolders)
        {
            foreach (var subDirectory in SafeEnumerateDirectories(parent))
            {
                if (File.Exists(Path.Combine(subDirectory, ClientFileName)))
                {
                    yield return subDirectory;
                }
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateDirectories(directory).ToArray()
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static bool HasNonEmptyFile(string directory, string fileName)
    {
        try
        {
            var file = new FileInfo(Path.Combine(directory, fileName));
            return file.Exists && file.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
