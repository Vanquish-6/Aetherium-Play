using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AcLegacyLauncher;

internal static class GraphicsBootstrap
{
    private const string RegistrySubKey =
        @"Software\Classes\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00";

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hLibModule);

    // Loads "DDraw.dll" by its bare name - the same way client.exe would - and reports
    // which actual file Windows resolved it to. Because AetheriumLauncher.exe itself
    // lives in the same folder as client.exe, standard DLL search order (app's own
    // directory is checked first, before System32) resolves identically for both
    // processes, so this tells us definitively whether the wrapper is really taking
    // effect on this machine instead of just trusting that dropping the file in place
    // is enough.
    internal static string DescribeResolvedDDrawDll(string installDirectory)
    {
        var expectedPath = Path.Combine(installDirectory, "DDraw.dll");
        var handle = LoadLibrary("DDraw.dll");
        if (handle == IntPtr.Zero)
        {
            return $"could not load DDraw.dll at all (Win32 error {Marshal.GetLastWin32Error()})";
        }

        try
        {
            var buffer = new StringBuilder(1024);
            GetModuleFileName(handle, buffer, buffer.Capacity);
            var resolvedPath = buffer.ToString();
            var isOurWrapper = resolvedPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase);
            return isOurWrapper
                ? $"our dgVoodoo wrapper ({resolvedPath})"
                : $"WARNING - the real Windows one, not our wrapper ({resolvedPath})";
        }
        finally
        {
            FreeLibrary(handle);
        }
    }

    internal static void SeedSafeGraphicsSettings()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the AC registry key.");

        // Fullscreen avoids the GDI 16-bit desktop check in windowed mode (modern Windows is 32-bit).
        key.SetValue("UseHardware", 1, RegistryValueKind.DWord);
        key.SetValue("DoubleBuffer", 2, RegistryValueKind.DWord);
        key.SetValue("FullScreen", 1, RegistryValueKind.DWord);
        key.SetValue("ZBuffer2", 0, RegistryValueKind.DWord);
        key.SetValue("ScreenWidth", 800, RegistryValueKind.DWord);
        key.SetValue("ScreenHeight", 600, RegistryValueKind.DWord);

        foreach (var valueName in new[] { "DirectDrawDevice", "DirectDrawGUID" })
        {
            if (Array.IndexOf(key.GetValueNames(), valueName) >= 0)
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
    }

    internal static void SeedUserPreferencesDisplay(bool fullScreen = true)
    {
        var preferencesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Asheron's Call");
        var preferencesPath = Path.Combine(preferencesDirectory, "UserPreferences.ini");
        var fullScreenValue = fullScreen ? "True" : "False";

        if (!File.Exists(preferencesPath))
        {
            // This file is normally created by the game itself after it starts up - on a
            // brand-new install (exactly the case that hits the "set your desktop to
            // 16-bit" error) it doesn't exist yet, so this fix would otherwise silently
            // never apply before the very first, failing launch attempt. Seed a minimal
            // valid file up front instead of waiting for the game to create one.
            Directory.CreateDirectory(preferencesDirectory);
            File.WriteAllLines(preferencesPath, new[]
            {
                "[Display]",
                "RefreshRate=Auto",
                "Resolution=800x600",
                $"FullScreen={fullScreenValue}",
                "SyncToRefresh=False",
            });
            return;
        }

        var lines = File.ReadAllLines(preferencesPath);
        var inDisplaySection = false;
        var changed = false;
        var sawFullScreen = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inDisplaySection = trimmed.Equals("[Display]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inDisplaySection)
            {
                continue;
            }

            if (trimmed.StartsWith("Resolution=", StringComparison.OrdinalIgnoreCase))
            {
                if (!trimmed.Equals("Resolution=800x600", StringComparison.OrdinalIgnoreCase))
                {
                    lines[index] = "Resolution=800x600";
                    changed = true;
                }
            }
            else if (trimmed.StartsWith("FullScreen=", StringComparison.OrdinalIgnoreCase))
            {
                sawFullScreen = true;
                var desired = $"FullScreen={fullScreenValue}";
                if (!trimmed.Equals(desired, StringComparison.OrdinalIgnoreCase))
                {
                    lines[index] = desired;
                    changed = true;
                }
            }
        }

        if (!sawFullScreen)
        {
            // Append into [Display] if missing.
            var displayIndex = Array.FindIndex(
                lines,
                line => line.Trim().Equals("[Display]", StringComparison.OrdinalIgnoreCase));
            if (displayIndex >= 0)
            {
                var list = lines.ToList();
                list.Insert(displayIndex + 1, $"FullScreen={fullScreenValue}");
                lines = list.ToArray();
                changed = true;
            }
        }

        if (changed)
        {
            File.WriteAllLines(preferencesPath, lines);
        }
    }

    /// <summary>
    /// Dual-client friendly display/input: windowed AC + dgVoodoo without mouse capture.
    /// Two exclusive-fullscreen clients with CaptureMouse=true commonly eat keyboard focus.
    /// Applies to the launch folder and any other known AC client folders (main + admin, etc.).
    /// </summary>
    internal static void ApplyMulticlientWindowedSettings(
        string workingDirectory,
        IEnumerable<string>? additionalClientDirectories = null)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the AC registry key."))
        {
            key.SetValue("UseHardware", 1, RegistryValueKind.DWord);
            key.SetValue("DoubleBuffer", 2, RegistryValueKind.DWord);
            key.SetValue("FullScreen", 0, RegistryValueKind.DWord);
            key.SetValue("ZBuffer2", 0, RegistryValueKind.DWord);
            key.SetValue("ScreenWidth", 800, RegistryValueKind.DWord);
            key.SetValue("ScreenHeight", 600, RegistryValueKind.DWord);
        }

        SeedUserPreferencesDisplay(fullScreen: false);

        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            workingDirectory,
        };

        if (additionalClientDirectories is not null)
        {
            foreach (var directory in additionalClientDirectories)
            {
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    directories.Add(directory);
                }
            }
        }

        foreach (var directory in directories)
        {
            ApplyMulticlientDgVoodooConfig(directory);
        }
    }

    /// <summary>
    /// CaptureMouse alone can steal keyboard even in windowed mode with two clients.
    /// Safe to apply on every launch.
    /// </summary>
    internal static void ApplyInputFriendlyDgVoodooConfig(string workingDirectory)
    {
        ApplyDgVoodooFlags(
            workingDirectory,
            captureMouse: false,
            fullScreenMode: null,
            freeMouse: true,
            centerAppWindow: null);
    }

    internal static void ApplyMulticlientDgVoodooConfig(string workingDirectory)
    {
        ApplyDgVoodooFlags(
            workingDirectory,
            captureMouse: false,
            fullScreenMode: false,
            freeMouse: true,
            centerAppWindow: false);
    }

    private static void ApplyDgVoodooFlags(
        string workingDirectory,
        bool? captureMouse,
        bool? fullScreenMode,
        bool? freeMouse,
        bool? centerAppWindow)
    {
        var configPath = Path.Combine(workingDirectory, "DgVoodoo.conf");
        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(workingDirectory, "dgVoodoo.conf");
        }

        if (!File.Exists(configPath))
        {
            return;
        }

        var lines = File.ReadAllLines(configPath);
        var changed = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            string? updated = null;

            if (captureMouse is not null &&
                line.StartsWith("CaptureMouse", StringComparison.OrdinalIgnoreCase))
            {
                updated = SetConfigFlag(line, "CaptureMouse", captureMouse.Value);
            }
            else if (fullScreenMode is not null &&
                     line.StartsWith("FullScreenMode", StringComparison.OrdinalIgnoreCase))
            {
                updated = SetConfigFlag(line, "FullScreenMode", fullScreenMode.Value);
            }
            else if (freeMouse is not null &&
                     line.StartsWith("FreeMouse", StringComparison.OrdinalIgnoreCase))
            {
                updated = SetConfigFlag(line, "FreeMouse", freeMouse.Value);
            }
            else if (centerAppWindow is not null &&
                     line.StartsWith("CenterAppWindow", StringComparison.OrdinalIgnoreCase))
            {
                updated = SetConfigFlag(line, "CenterAppWindow", centerAppWindow.Value);
            }

            if (updated is null || updated == line)
            {
                continue;
            }

            lines[index] = updated;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        // Break any hardlink to another install before writing.
        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }

        File.WriteAllLines(configPath, lines);
    }

    internal static bool EnsureDirectDrawWrapper(string installDirectory, string repositoryToolsDirectory)
    {
        var extractedDirectory = Path.Combine(repositoryToolsDirectory, "extracted");
        var wrapperSourceDirectory = Path.Combine(extractedDirectory, "MS", "x86");
        var hasWrapperSource = Directory.Exists(wrapperSourceDirectory);
        var copiedAny = false;

        if (hasWrapperSource)
        {
            foreach (var fileName in new[] { "DDraw.dll", "D3DImm.dll" })
            {
                var sourcePath = Path.Combine(wrapperSourceDirectory, fileName);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                var destinationPath = Path.Combine(installDirectory, fileName);
                if (!File.Exists(destinationPath)
                    || new FileInfo(sourcePath).Length != new FileInfo(destinationPath).Length)
                {
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                    copiedAny = true;
                }
            }

            var configSourcePath = Path.Combine(extractedDirectory, "dgVoodoo.conf");
            var configDestinationPath = Path.Combine(installDirectory, "DgVoodoo.conf");
            if (File.Exists(configSourcePath)
                && (!File.Exists(configDestinationPath)
                    || new FileInfo(configSourcePath).Length != new FileInfo(configDestinationPath).Length))
            {
                File.Copy(configSourcePath, configDestinationPath, overwrite: true);
                copiedAny = true;
            }
        }

        var installConfigPath = Path.Combine(installDirectory, "DgVoodoo.conf");
        if (File.Exists(installConfigPath) && DisableWatermarks(installConfigPath))
        {
            copiedAny = true;
        }

        return copiedAny || File.Exists(Path.Combine(installDirectory, "DDraw.dll"));
    }

    internal static bool DisableWatermarks(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return false;
        }

        var lines = File.ReadAllLines(configPath);
        var changed = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var updated = lines[index] switch
            {
                var line when line.StartsWith("dgVoodooWatermark", StringComparison.OrdinalIgnoreCase)
                    => SetConfigFlag(line, "dgVoodooWatermark", false),
                var line when line.StartsWith("3DfxWatermark", StringComparison.OrdinalIgnoreCase)
                    => SetConfigFlag(line, "3DfxWatermark", false),
                var line when line.StartsWith("3DfxSplashScreen", StringComparison.OrdinalIgnoreCase)
                    => SetConfigFlag(line, "3DfxSplashScreen", false),
                _ => null,
            };

            if (updated is null || updated == lines[index])
            {
                continue;
            }

            lines[index] = updated;
            changed = true;
        }

        if (changed)
        {
            File.WriteAllLines(configPath, lines);
        }

        return changed;
    }

    private static string SetConfigFlag(string line, string key, bool value)
    {
        var equalsIndex = line.IndexOf('=');
        if (equalsIndex < 0)
        {
            return line;
        }

        var padding = line[..(equalsIndex + 1)] + " ";
        while (padding.Length < 36)
        {
            padding += " ";
        }

        return padding + (value ? "true" : "false");
    }
}
