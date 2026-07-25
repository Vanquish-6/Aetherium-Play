using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AcLegacyLauncher;

internal static class VintageDecalInjector
{
    private const string PackageDirectoryName = "Decal-2.6.1.1-DM";
    private const string EnableMarkerName = "enabled.flag";

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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

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

    internal static string? FindEnabledPackage(string installDirectory)
    {
        var packageDirectory = Path.Combine(installDirectory, PackageDirectoryName);
        var markerPath = Path.Combine(packageDirectory, EnableMarkerName);
        var injectPath = Path.Combine(packageDirectory, "Inject.dll");

        return File.Exists(markerPath) && File.Exists(injectPath)
            ? packageDirectory
            : null;
    }

    internal static void Inject(
        string packageDirectory,
        Process process,
        IntPtr processHandle,
        IntPtr threadHandle)
    {
        if (Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "Vintage Decal injection requires the x86 Aetherium Launcher build.");
        }

        var injectPath = Path.GetFullPath(Path.Combine(packageDirectory, "Inject.dll"));
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
                "VirtualAllocEx failed while preparing vintage Decal injection.");
        }

        IntPtr remoteThread = IntPtr.Zero;
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
                    "WriteProcessMemory failed while preparing vintage Decal injection.");
            }

            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibraryW = kernel32 == IntPtr.Zero
                ? IntPtr.Zero
                : GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibraryW == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not resolve LoadLibraryW for vintage Decal injection.");
            }

            remoteThread = CreateRemoteThread(
                processHandle,
                IntPtr.Zero,
                0,
                loadLibraryW,
                remotePath,
                0,
                out _);
            if (remoteThread == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "CreateRemoteThread failed while injecting vintage Decal.");
            }

            var waitResult = WaitForSingleObject(remoteThread, 15_000);
            if (waitResult != 0)
            {
                throw new TimeoutException(
                    $"Vintage Decal LoadLibraryW did not finish (wait result 0x{waitResult:X8}).");
            }

            if (!GetExitCodeThread(remoteThread, out var moduleHandle) || moduleHandle == 0)
            {
                throw new InvalidOperationException(
                    $"LoadLibraryW failed to load {injectPath} into client.exe (PID {process.Id}).");
            }
        }
        finally
        {
            if (remoteThread != IntPtr.Zero)
            {
                CloseHandle(remoteThread);
            }

            VirtualFreeEx(processHandle, remotePath, 0, MemRelease);
        }
    }
}
