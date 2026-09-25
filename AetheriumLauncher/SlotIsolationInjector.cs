using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AcLegacyLauncher;

internal static class SlotIsolationInjector
{
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr process,
        IntPtr address,
        nuint size,
        uint allocationType,
        uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(
        IntPtr process,
        IntPtr address,
        nuint size,
        uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        byte[] buffer,
        nuint size,
        out nuint bytesWritten);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr process,
        IntPtr threadAttributes,
        nuint stackSize,
        IntPtr startAddress,
        IntPtr parameter,
        uint creationFlags,
        out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    internal static string DllPath => Path.Combine(AppContext.BaseDirectory, "SlotIsolation.dll");

    internal static void EnsureDllPresent()
    {
        if (Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "Account slot isolation requires the x86 Aetherium Launcher build.");
        }

        if (!File.Exists(DllPath))
        {
            throw new FileNotFoundException(
                "SlotIsolation.dll is missing, so the client would share retail settings.",
                DllPath);
        }
    }

    internal static void Inject(IntPtr processHandle)
    {
        EnsureDllPresent();
        var injectPath = Path.GetFullPath(DllPath);
        var localModule = LoadLibraryW(injectPath);
        if (localModule == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not load SlotIsolation.dll in the launcher.");
        }

        var localInstall = GetProcAddress(localModule, "SlotIsolation_Install");
        if (localInstall == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "SlotIsolation.dll does not export SlotIsolation_Install.");
        }

        var installOffset = localInstall.ToInt64() - localModule.ToInt64();
        var pathBytes = Encoding.Unicode.GetBytes(injectPath + '\0');
        var remotePath = VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            (nuint)pathBytes.Length,
            MemCommit | MemReserve,
            PageReadWrite);
        if (remotePath == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "VirtualAllocEx failed while preparing account slot isolation.");
        }

        IntPtr loadThread = IntPtr.Zero;
        IntPtr installThread = IntPtr.Zero;
        try
        {
            if (!WriteProcessMemory(
                    processHandle,
                    remotePath,
                    pathBytes,
                    (nuint)pathBytes.Length,
                    out var bytesWritten)
                || bytesWritten != (nuint)pathBytes.Length)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "WriteProcessMemory failed while preparing account slot isolation.");
            }

            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibraryW = kernel32 == IntPtr.Zero
                ? IntPtr.Zero
                : GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibraryW == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not resolve LoadLibraryW for account slot isolation.");
            }

            loadThread = CreateRemoteThread(
                processHandle,
                IntPtr.Zero,
                0,
                loadLibraryW,
                remotePath,
                0,
                out _);
            if (loadThread == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "CreateRemoteThread failed while loading account slot isolation.");
            }

            if (WaitForSingleObject(loadThread, 15_000) != 0 ||
                !GetExitCodeThread(loadThread, out var remoteModule) ||
                remoteModule == 0)
            {
                throw new InvalidOperationException(
                    "SlotIsolation.dll did not load into client.exe.");
            }

            var remoteInstall = new IntPtr((long)remoteModule + installOffset);
            installThread = CreateRemoteThread(
                processHandle,
                IntPtr.Zero,
                0,
                remoteInstall,
                IntPtr.Zero,
                0,
                out _);
            if (installThread == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "CreateRemoteThread failed while installing account slot isolation.");
            }

            if (WaitForSingleObject(installThread, 15_000) != 0 ||
                !GetExitCodeThread(installThread, out var installed) ||
                installed != 1)
            {
                throw new InvalidOperationException(
                    "Account slot isolation did not install, so the client was not started.");
            }
        }
        finally
        {
            if (installThread != IntPtr.Zero)
            {
                CloseHandle(installThread);
            }

            if (loadThread != IntPtr.Zero)
            {
                CloseHandle(loadThread);
            }

            VirtualFreeEx(processHandle, remotePath, 0, MemRelease);
        }
    }
}
