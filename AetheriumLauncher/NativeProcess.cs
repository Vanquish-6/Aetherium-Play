using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AcLegacyLauncher;

internal static class NativeProcess
{
    public const uint CreateSuspended = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    public static Process StartSuspendedClient(
        string clientPath,
        string workingDirectory,
        string arguments,
        out IntPtr processHandle,
        out IntPtr threadHandle)
    {
        var commandLine = $"\"{clientPath}\" {arguments}".TrimEnd();
        var startupInfo = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcessW(
                clientPath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CreateSuspended,
                IntPtr.Zero,
                workingDirectory,
                ref startupInfo,
                out var processInfo))
        {
            throw new InvalidOperationException(
                $"CreateProcess failed for client.exe (Win32 {Marshal.GetLastWin32Error()}).");
        }

        processHandle = processInfo.hProcess;
        threadHandle = processInfo.hThread;

        try
        {
            return Process.GetProcessById(processInfo.dwProcessId);
        }
        catch
        {
            CloseHandle(threadHandle);
            CloseHandle(processHandle);
            throw;
        }
    }

    public static void ResumeAndClose(IntPtr processHandle, IntPtr threadHandle)
    {
        try
        {
            if (ResumeThread(threadHandle) == unchecked((uint)-1))
            {
                var error = Marshal.GetLastWin32Error();
                TerminateProcess(processHandle, 1);
                throw new InvalidOperationException($"ResumeThread failed (Win32 {error}).");
            }
        }
        finally
        {
            if (threadHandle != IntPtr.Zero)
            {
                CloseHandle(threadHandle);
            }

            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }
        }
    }
}
