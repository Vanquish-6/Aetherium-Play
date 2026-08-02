namespace AcLegacyLauncher;

internal sealed class LauncherStartupOptions
{
    private LauncherStartupOptions(
        string? gameInstallDirectory,
        NativeClientDddAccelerationProfile? clientProfile)
    {
        GameInstallDirectory = gameInstallDirectory;
        ClientProfile = clientProfile;
    }

    internal string? GameInstallDirectory { get; }

    internal NativeClientDddAccelerationProfile? ClientProfile { get; }

    internal static LauncherStartupOptions Parse(IReadOnlyList<string> args) =>
        Parse(args, NativeClientDddAcceleration.ResolveSupportedClientProfile);

    internal static LauncherStartupOptions Parse(
        IReadOnlyList<string> args,
        Func<string, NativeClientDddAccelerationProfile> resolveClientProfile)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(resolveClientProfile);
        if (args.Count == 0)
        {
            return new LauncherStartupOptions(null, null);
        }

        if (args.Count != 2 ||
            !args[0].Equals("--game-install", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Usage: AetheriumLauncher.exe [--game-install <directory>]");
        }

        var requestedDirectory = args[1].Trim();
        if (string.IsNullOrWhiteSpace(requestedDirectory))
        {
            throw new ArgumentException("--game-install requires a game directory.");
        }

        if (!Path.IsPathFullyQualified(requestedDirectory) ||
            IsUncOrDevicePath(requestedDirectory))
        {
            throw new ArgumentException(
                "--game-install requires an absolute directory on a local drive; " +
                "UNC and device paths are not allowed.");
        }

        var fullDirectory = Path.GetFullPath(requestedDirectory);
        if (IsUncOrDevicePath(fullDirectory) || !HasLocalFixedDriveRoot(fullDirectory))
        {
            throw new ArgumentException(
                "--game-install must resolve to a local fixed drive.");
        }

        EnsurePhysicalDirectoryTree(fullDirectory);
        var clientPath = EnsurePhysicalFile(fullDirectory, "client.exe", requireNonEmpty: true);
        _ = EnsurePhysicalFile(fullDirectory, "portal.dat", requireNonEmpty: true);
        _ = EnsurePhysicalFile(fullDirectory, "cell.dat", requireNonEmpty: true);
        var clientProfile = resolveClientProfile(clientPath);

        return new LauncherStartupOptions(fullDirectory, clientProfile);
    }

    private static bool IsUncOrDevicePath(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal) ||
        path.StartsWith("\\??\\", StringComparison.Ordinal);

    private static bool HasLocalFixedDriveRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root is not { Length: 3 } ||
            !char.IsAsciiLetter(root[0]) ||
            root[1] != ':' ||
            root[2] != Path.DirectorySeparatorChar)
        {
            return false;
        }

        try
        {
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsurePhysicalDirectoryTree(string fullDirectory)
    {
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The --game-install directory does not exist: {fullDirectory}");
        }

        for (var directory = new DirectoryInfo(fullDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            EnsureNotReparsePoint(
                directory.Attributes,
                $"The --game-install path crosses a reparse-point directory: {directory.FullName}");
        }
    }

    private static string EnsurePhysicalFile(
        string fullDirectory,
        string fileName,
        bool requireNonEmpty)
    {
        var path = Path.Combine(fullDirectory, fileName);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new InvalidDataException(
                $"The --game-install directory is incomplete: {fullDirectory}. " +
                $"Missing: {fileName}");
        }

        EnsureNotReparsePoint(
            file.Attributes,
            $"The --game-install file must not be a reparse point: {path}");

        if (requireNonEmpty && file.Length == 0)
        {
            throw new InvalidDataException(
                $"The --game-install file is empty: {path}");
        }

        return path;
    }

    internal static void EnsureNotReparsePointForTest(FileAttributes attributes, string label) =>
        EnsureNotReparsePoint(attributes, label);

    private static void EnsureNotReparsePoint(FileAttributes attributes, string message)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(message);
        }
    }
}
