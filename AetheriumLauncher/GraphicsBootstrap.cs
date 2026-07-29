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
        // EOR and DM share Documents\Asheron's Call\UserPreferences.ini and *.keymap.
        // Never rewrite an existing file — only seed a minimal Display section if missing.
        var preferencesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Asheron's Call");
        var preferencesPath = Path.Combine(preferencesDirectory, "UserPreferences.ini");
        if (File.Exists(preferencesPath))
        {
            return;
        }

        var fullScreenValue = fullScreen ? "True" : "False";
        Directory.CreateDirectory(preferencesDirectory);
        File.WriteAllLines(preferencesPath, new[]
        {
            "[Display]",
            "RefreshRate=Auto",
            "Resolution=800x600",
            $"FullScreen={fullScreenValue}",
            "SyncToRefresh=False",
        });
    }

    /// <summary>
    /// Solo play: restore dgVoodoo's normal captured-mouse behavior.
    /// Older builds forced dual-client CaptureMouse=false flags into client folders.
    /// </summary>
    internal static void ApplySoloCaptureMouseSettings(string workingDirectory)
    {
        ApplyDgVoodooFlags(
            workingDirectory,
            captureMouse: true,
            fullScreenMode: null,
            freeMouse: false,
            centerAppWindow: null,
            appControlledScreenMode: false,
            disableAltEnterToToggleScreenMode: false);
    }

    /// <summary>
    /// Legacy helper — prefer <see cref="ApplySoloCaptureMouseSettings"/>.
    /// </summary>
    internal static void ApplyInputFriendlyDgVoodooConfig(string workingDirectory)
    {
        ApplySoloCaptureMouseSettings(workingDirectory);
    }

    private static void ApplyDgVoodooFlags(
        string workingDirectory,
        bool? captureMouse,
        bool? fullScreenMode,
        bool? freeMouse,
        bool? centerAppWindow,
        bool? appControlledScreenMode,
        bool? disableAltEnterToToggleScreenMode)
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
            else if (appControlledScreenMode is not null &&
                     line.StartsWith("AppControlledScreenMode", StringComparison.OrdinalIgnoreCase))
            {
                updated = SetConfigFlag(
                    line,
                    "AppControlledScreenMode",
                    appControlledScreenMode.Value);
            }
            else if (disableAltEnterToToggleScreenMode is not null &&
                     line.StartsWith(
                         "DisableAltEnterToToggleScreenMode",
                         StringComparison.OrdinalIgnoreCase))
            {
                updated = SetConfigFlag(
                    line,
                    "DisableAltEnterToToggleScreenMode",
                    disableAltEnterToToggleScreenMode.Value);
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
