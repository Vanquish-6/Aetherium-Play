using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AcLegacyLauncher;

/// <summary>
/// Enables an explicitly selected Aeth-Tank binary for this launch. One startup branch already calls
/// APIManager::Init. The other branch is a 5-byte jump that skips it, which is
/// the branch this client takes, so ACPlugin is never loaded. The jump is
/// redirected at a stub that calls Init and then continues. The live call that
/// follows those 5 bytes is left unchanged. The file on disk is not touched.
/// Managed releases are verified before this method. The legacy local marker
/// is available only in development builds, never in the player release.
/// </summary>
internal static class AethTankPluginBootstrap
{
    private const uint PageExecuteReadWrite = 0x40;
    private const uint MemCommitReserve = 0x3000;
    private const uint ImageBase = 0x0040_0000;
    private const uint PatchRva = 0x1978;
    private const uint InitVa = 0x0049_5460;
    private const uint ContinuationVa = 0x0040_2190;

    private static readonly byte[] ExpectedSite =
    [
        0xE9, 0x13, 0x08, 0x00, 0x00, 0xE8, 0xDE, 0x3A, 0x09, 0x00,
    ];

    private static readonly uint[] CaveHints =
    [
        0x2000_0000, 0x3000_0000, 0x1000_0000, 0x0200_0000, 0x2500_0000,
    ];

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        nuint size,
        out nuint bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtectEx(
        IntPtr process,
        IntPtr address,
        nuint size,
        uint newProtect,
        out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushInstructionCache(
        IntPtr process,
        IntPtr address,
        nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr process,
        IntPtr address,
        nuint size,
        uint allocationType,
        uint protect);

    internal static string? FindDevelopmentPlugin()
    {
#if DEBUG || AETH_TANK_DEVELOPER_BUILD
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "aeth-tank");
        var plugin = Path.Combine(root, "build", "aeth-tank.dll");
        return File.Exists(Path.Combine(root, "private.enable")) && File.Exists(plugin) ? plugin : null;
#else
        return null;
#endif
    }

    internal static string? TryEnable(IntPtr processHandle, string profileId, string registrySubKey,
        string? plugin, string? dataDirectory = null)
    {
        RestorePreviousPlugin(registrySubKey);
        var publicClient = string.Equals(
            profileId, NativeClientDddAcceleration.PublicProfileId, StringComparison.Ordinal);
        var adminClient = string.Equals(
            profileId, NativeClientDddAcceleration.AdminProfileId, StringComparison.Ordinal);
        if (!publicClient && !adminClient)
        {
            return null;
        }

        if (plugin is null || !File.Exists(plugin))
        {
            return null;
        }

        // Slot isolation redirects the client's HKLM\...\1.00 open to this
        // per-slot HKCU key. ACPlugin has to live there or Init never loads it.
        using (var slotKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registrySubKey, writable: true))
        {
            if (slotKey is null) throw new InvalidOperationException("Could not open this slot's plugin settings.");
            var previousPlugin = slotKey.GetValue("ACPlugin") as string;
            if (previousPlugin is not null)
                slotKey.SetValue("AethTankPreviousPlugin", previousPlugin, Microsoft.Win32.RegistryValueKind.String);
            else slotKey.DeleteValue("AethTankPreviousPlugin", throwOnMissingValue: false);
            slotKey.SetValue("AethTankManagedPlugin", plugin, Microsoft.Win32.RegistryValueKind.String);
            slotKey.SetValue("ACPlugin", plugin, Microsoft.Win32.RegistryValueKind.String);
        }

        // The Aetherium admin client follows the native APIManager::Init path.
        // It needs the per-slot ACPlugin registration but not the retail client's
        // skipped-call redirect below.
        if (adminClient)
        {
            return "aeth-tank registered for this admin-client launch only. client.exe on disk is unchanged.";
        }

        var address = new IntPtr(ImageBase + PatchRva);
        var actual = new byte[ExpectedSite.Length];
        if (!ReadProcessMemory(processHandle, address, actual, (nuint)actual.Length, out var read)
            || read != (nuint)actual.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the plugin loader site.");
        }

        if (!actual.AsSpan().SequenceEqual(ExpectedSite))
        {
            throw new InvalidDataException(
                "The verified client plugin site did not match. aeth-tank was not enabled.");
        }

        var cave = AllocateCave(processHandle);
        var caveVa = unchecked((uint)cave.ToInt32());
        var stub = BuildStub(caveVa);
        if (!WriteProcessMemory(processHandle, cave, stub, (nuint)stub.Length, out var stubWritten)
            || stubWritten != (nuint)stub.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not write the plugin loader stub.");
        }

        var redirect = BuildRedirect(caveVa);
        if (!VirtualProtectEx(processHandle, address, (nuint)redirect.Length, PageExecuteReadWrite, out var previous)
            || previous == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the plugin loader site.");
        }

        try
        {
            if (!WriteProcessMemory(
                    processHandle,
                    address,
                    redirect,
                    (nuint)redirect.Length,
                    out var written)
                || written != (nuint)redirect.Length)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enable the plugin loader.");
            }
        }
        finally
        {
            var restore = previous == 0 ? 0x20u : previous;
            VirtualProtectEx(processHandle, address, (nuint)redirect.Length, restore, out _);
            FlushInstructionCache(processHandle, address, (nuint)redirect.Length);
            FlushInstructionCache(processHandle, cave, (nuint)stub.Length);
        }

        var check = new byte[ExpectedSite.Length];
        ReadProcessMemory(processHandle, address, check, (nuint)check.Length, out _);
        if (!check.AsSpan(0, redirect.Length).SequenceEqual(redirect)
            || !check.AsSpan(redirect.Length).SequenceEqual(ExpectedSite.AsSpan(redirect.Length)))
        {
            throw new InvalidDataException("The plugin loader redirect did not stick.");
        }

        try
        {
            var log = Path.Combine(dataDirectory ?? Path.GetDirectoryName(plugin)!, "aeth-tank.log");
            File.AppendAllText(log,
                $"enabled cave=0x{caveVa:X8} site={Convert.ToHexString(check)}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return "aeth-tank enabled for this launch only. client.exe on disk is unchanged.";
    }

    internal static void RestorePreviousPlugin(string registrySubKey)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registrySubKey, writable: true);
        if (key is null) return;
        var current = key.GetValue("ACPlugin") as string;
        var owned = key.GetValue("AethTankManagedPlugin") as string;
        var previous = key.GetValue("AethTankPreviousPlugin") as string;
        var ours = current is not null && (string.Equals(current, owned, StringComparison.OrdinalIgnoreCase) ||
            (owned is null && string.Equals(Path.GetFileName(current), "aeth-tank.dll", StringComparison.OrdinalIgnoreCase)));
        if (ours)
        {
            if (previous is not null) key.SetValue("ACPlugin", previous, Microsoft.Win32.RegistryValueKind.String);
            else key.DeleteValue("ACPlugin", throwOnMissingValue: false);
        }
        key.DeleteValue("AethTankManagedPlugin", throwOnMissingValue: false);
        key.DeleteValue("AethTankPreviousPlugin", throwOnMissingValue: false);
    }

    private static IntPtr AllocateCave(IntPtr processHandle)
    {
        foreach (var hint in CaveHints)
        {
            var cave = VirtualAllocEx(
                processHandle,
                new IntPtr(hint),
                0x1000,
                MemCommitReserve,
                PageExecuteReadWrite);
            if (cave != IntPtr.Zero && unchecked((uint)cave.ToInt32()) < 0x8000_0000)
            {
                return cave;
            }
        }

        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not place the plugin loader stub.");
    }

    private static byte[] BuildStub(uint caveVa)
    {
        var stub = new byte[10];
        stub[0] = 0xE8;
        BitConverter.GetBytes(unchecked((int)(InitVa - (caveVa + 5)))).CopyTo(stub, 1);
        stub[5] = 0xE9;
        BitConverter.GetBytes(unchecked((int)(ContinuationVa - (caveVa + 10)))).CopyTo(stub, 6);
        return stub;
    }

    private static byte[] BuildRedirect(uint caveVa)
    {
        var redirect = new byte[5];
        redirect[0] = 0xE9;
        BitConverter.GetBytes(unchecked((int)(caveVa - (ImageBase + PatchRva + 5)))).CopyTo(redirect, 1);
        return redirect;
    }
}
