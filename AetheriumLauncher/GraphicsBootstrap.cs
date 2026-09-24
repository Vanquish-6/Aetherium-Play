using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace AcLegacyLauncher;

internal static class GraphicsBootstrap
{
    internal const string RepairGraphicsArgument = "--repair-graphics";

    private const string RegistrySubKey =
        @"Software\Classes\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00";

    // client.exe reads these from HKLM\SOFTWARE\Microsoft\Microsoft Games\Asheron's Call\1.00
    // (the 32-bit view). A saved DirectDrawDevice that no longer enumerates makes WinMain
    // exit before the game window stays up. A non-elevated write only updates the
    // VirtualStore overlay, so the real key has to be repaired too.
    private static readonly string[] UserOverlaySubKeys =
    [
        RegistrySubKey,
        @"Software\Classes\VirtualStore\MACHINE\SOFTWARE\Microsoft\Microsoft Games\Asheron's Call\1.00",
    ];

    private static readonly (RegistryView View, string SubKey)[] MachineKeys =
    [
        (RegistryView.Registry64, @"SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"),
        (RegistryView.Registry32, @"SOFTWARE\Microsoft\Microsoft Games\Asheron's Call\1.00"),
    ];

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
        if (WineRuntime.IsWine)
        {
            RemoveLocalDirectDrawOverrides(installDirectory);
        }

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
            if (WineRuntime.IsWine)
            {
                return $"Wine DirectDraw ({resolvedPath})";
            }

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
        using (var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the AC registry key."))
        {
            SeedGraphicsValues(key);
        }

        // The Windows installer writes HKLM. Wine prefixes have no Inno Setup
        // pass, so the 32-bit launcher writes the game's real key as well.
        if (WineRuntime.IsWine)
        {
            foreach (var subKey in new[]
            {
                @"SOFTWARE\Microsoft\Microsoft Games\Asheron's Call\1.00",
                @"SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00",
            })
            {
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(subKey, writable: true);
                    if (key is not null)
                    {
                        SeedGraphicsValues(key);
                    }
                }
                catch
                {
                    // HKCU VirtualStore remains if a prefix blocks HKLM.
                }
            }
        }
    }

    /// <summary>
    /// Drops a saved 3D accelerator and forces the fullscreen hardware mode the
    /// client can actually open. Runs on every Play and during setup.
    /// </summary>
    internal static void EnsureDisplayDeviceForLaunch()
    {
        RepairUserOverlay();
        if (MachineGraphicsAreSafe() ||
            (TryRepairMachineGraphics() && MachineGraphicsAreSafe()) ||
            WineRuntime.IsWine)
        {
            return;
        }

        RequestElevatedRepair();
        if (!MachineGraphicsAreSafe())
        {
            throw new InvalidOperationException(
                "The saved 3D device is still set, so the game would close immediately. Press Play again.");
        }
    }

    internal static int RepairMachineGraphicsFromElevatedProcess()
    {
        return TryRepairMachineGraphics() && MachineGraphicsAreSafe() ? 0 : 1;
    }

    internal static bool HasStaleDisplayDevice(RegistryKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ReadDword(key, "UseHardware") != 1 ||
               ReadDword(key, "DoubleBuffer") != 2 ||
               ReadDword(key, "FullScreen") != 1 ||
               HasValue(key, "DirectDrawDevice") ||
               HasValue(key, "DirectDrawGUID");
    }

    internal static void ClearStaleDisplayDevice(RegistryKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        key.SetValue("UseHardware", 1, RegistryValueKind.DWord);
        key.SetValue("DoubleBuffer", 2, RegistryValueKind.DWord);
        key.SetValue("FullScreen", 1, RegistryValueKind.DWord);
        DeleteValueIfPresent(key, "DirectDrawDevice");
        DeleteValueIfPresent(key, "DirectDrawGUID");
    }

    private static void SeedGraphicsValues(RegistryKey key)
    {
        // Fullscreen avoids the GDI 16-bit desktop check in windowed mode (modern Windows is 32-bit).
        ClearStaleDisplayDevice(key);
        key.SetValue("ZBuffer2", 0, RegistryValueKind.DWord);
        key.SetValue("ScreenWidth", 800, RegistryValueKind.DWord);
        key.SetValue("ScreenHeight", 600, RegistryValueKind.DWord);
    }

    private static void RepairUserOverlay()
    {
        foreach (var subKey in UserOverlaySubKeys)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
                if (key is not null)
                {
                    ClearStaleDisplayDevice(key);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // The real HKLM repair still runs. An unwritable overlay cannot
                // be the only copy the client sees when that key is absent.
            }
        }
    }

    private static bool MachineGraphicsAreSafe()
    {
        var sawKey = false;
        foreach (var (view, subKey) in MachineKeys)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(subKey, writable: false);
                if (key is null || HasStaleDisplayDevice(key))
                {
                    return false;
                }

                sawKey = true;
            }
            catch (PlatformNotSupportedException)
            {
                // 32-bit Windows has no separate 64-bit registry view.
            }
        }

        return sawKey;
    }

    private static bool TryRepairMachineGraphics()
    {
        var denied = false;
        foreach (var (view, subKey) in MachineKeys)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.CreateSubKey(subKey, writable: true);
                if (key is null)
                {
                    denied = true;
                    continue;
                }

                ClearStaleDisplayDevice(key);
                if (HasStaleDisplayDevice(key))
                {
                    denied = true;
                }
            }
            catch (PlatformNotSupportedException)
            {
                // 32-bit Windows has no separate 64-bit registry view.
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
            {
                denied = true;
            }
        }

        return !denied;
    }

    private static void RequestElevatedRepair()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException(
                "The game's saved 3D device has to be cleared before it can open. Start Aetherium Launcher again.");
        }

        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = RepairGraphicsArgument,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException(
                "The game's saved 3D device was left in place, so the client would close immediately. Approve the Windows prompt and press Play again.");
        }

        using (process)
        {
            if (process is null)
            {
                throw new InvalidOperationException(
                    "The saved 3D device could not be cleared, so the game would close immediately. Press Play again and approve the Windows prompt.");
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "The saved 3D device could not be cleared, so the game would close immediately. Press Play again and approve the Windows prompt.");
            }
        }
    }

    private static int? ReadDword(RegistryKey key, string name)
    {
        return key.GetValue(name) is int value ? value : null;
    }

    private static bool HasValue(RegistryKey key, string name)
    {
        return key.GetValueNames().Any(value =>
            value.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static void DeleteValueIfPresent(RegistryKey key, string name)
    {
        if (HasValue(key, name))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
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
        if (WineRuntime.IsWine)
        {
            RemoveLocalDirectDrawOverrides(installDirectory);
            return true;
        }

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
                // 2.87.x builds share DDraw.dll file version 4.7.1.3000, and a
                // later build can match the previous file size. Replace whenever
                // the bytes differ so an already-installed wrapper still updates.
                if (!File.Exists(destinationPath) || !FileContentsMatch(sourcePath, destinationPath))
                {
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                    copiedAny = true;
                }
            }

            // Seed dgVoodoo.conf only when the game folder has none. Never replace
            // an existing conf: PLAY used to recopy the stock template whenever
            // the file size changed, which stomped OutputAPI and other edits.
            var configSourcePath = Path.Combine(extractedDirectory, "dgVoodoo.conf");
            var configDestinationPath = Path.Combine(installDirectory, "DgVoodoo.conf");
            if (File.Exists(configSourcePath) && !File.Exists(configDestinationPath))
            {
                File.Copy(configSourcePath, configDestinationPath);
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

    private static bool FileContentsMatch(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        return SHA256.HashData(left).AsSpan().SequenceEqual(SHA256.HashData(right));
    }

    internal static void RemoveLocalDirectDrawOverrides(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return;
        }

        foreach (var fileName in new[] { "DDraw.dll", "D3DImm.dll" })
        {
            var path = Path.Combine(installDirectory, fileName);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Wine must not load a leftover Windows dgVoodoo ddraw.dll.
            }
        }
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
