using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AcLegacyLauncher;

public enum MulticlientGateStatus
{
    NotFound,
    AlreadyAllowsMulti,
    StockNeedsPatch,
    PatchedInMemory,
}

public sealed class MulticlientGateResult
{
    public MulticlientGateStatus Status { get; init; }

    public string Detail { get; init; } = string.Empty;

    public int FileOffset { get; init; } = -1;
}

/// <summary>
/// DM-era client.exe single-instance gate: CreateMutexA("Empyrean Client") then
/// cmp eax, ERROR_ALREADY_EXISTS (0xB7) / jnz fail. Classic multiclient flips jnz→jmp.
/// </summary>
public static class MulticlientGate
{
    private const string MutexName = "Empyrean Client";
    private static readonly byte[] CmpAlreadyExists = [0x3D, 0xB7, 0x00, 0x00, 0x00];

    public static MulticlientGateResult Inspect(string clientExePath)
    {
        var bytes = File.ReadAllBytes(clientExePath);
        if (!TryFindGateBranchOffset(bytes, out var branchOffset, out var branchOpcode))
        {
            return new MulticlientGateResult
            {
                Status = MulticlientGateStatus.NotFound,
                Detail = $"No '{MutexName}' single-instance gate found (admin/custom build?).",
            };
        }

        if (branchOpcode == 0xEB)
        {
            return new MulticlientGateResult
            {
                Status = MulticlientGateStatus.AlreadyAllowsMulti,
                Detail = $"On-disk multiclient patch present (jmp at 0x{branchOffset:X}).",
                FileOffset = branchOffset,
            };
        }

        return new MulticlientGateResult
        {
            Status = MulticlientGateStatus.StockNeedsPatch,
            Detail = $"Stock gate at 0x{branchOffset:X} (jnz) — will patch in memory at launch.",
            FileOffset = branchOffset,
        };
    }

    /// <summary>
    /// While the process is suspended at startup, rewrite jnz→jmp for the Empyrean gate if needed.
    /// Safe no-op when already patched or gate not found.
    /// </summary>
    public static MulticlientGateResult EnsureAllowMulti(string clientExePath, IntPtr processHandle)
    {
        var bytes = File.ReadAllBytes(clientExePath);
        if (!TryFindGateBranchOffset(bytes, out var branchOffset, out var branchOpcode))
        {
            return new MulticlientGateResult
            {
                Status = MulticlientGateStatus.NotFound,
                Detail = $"No '{MutexName}' gate — launching without multiclient patch.",
            };
        }

        if (branchOpcode == 0xEB)
        {
            return new MulticlientGateResult
            {
                Status = MulticlientGateStatus.AlreadyAllowsMulti,
                Detail = $"Multiclient OK (on-disk jmp at 0x{branchOffset:X}).",
                FileOffset = branchOffset,
            };
        }

        if (branchOpcode != 0x75)
        {
            return new MulticlientGateResult
            {
                Status = MulticlientGateStatus.NotFound,
                Detail = $"Unexpected branch opcode 0x{branchOpcode:X2} at 0x{branchOffset:X}.",
                FileOffset = branchOffset,
            };
        }

        var imageBase = ReadImageBase(bytes);
        var branchRva = FileOffsetToRva(bytes, branchOffset)
            ?? throw new InvalidOperationException($"Could not map multiclient gate offset 0x{branchOffset:X} to RVA.");
        var address = new IntPtr(unchecked((int)(imageBase + branchRva)));
        byte[] patch = [0xEB];
        if (!Native.WriteProcessMemory(processHandle, address, patch, patch.Length, out _))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to apply multiclient patch at 0x{address.ToInt64():X} (Win32 {error}).");
        }

        return new MulticlientGateResult
        {
            Status = MulticlientGateStatus.PatchedInMemory,
            Detail = $"Applied in-memory multiclient patch (jnz→jmp at 0x{branchOffset:X}).",
            FileOffset = branchOffset,
        };
    }

    internal static bool TryFindGateBranchOffset(byte[] image, out int branchOffset, out byte branchOpcode)
    {
        branchOffset = -1;
        branchOpcode = 0;

        var stringOffset = IndexOf(image, Encoding.ASCII.GetBytes(MutexName + "\0"));
        if (stringOffset < 0)
        {
            // Some builds omit the trailing null in a tight pack; try without.
            stringOffset = IndexOf(image, Encoding.ASCII.GetBytes(MutexName));
        }

        if (stringOffset < 0)
        {
            return false;
        }

        var imageBase = ReadImageBase(image);
        var stringRva = FileOffsetToRva(image, stringOffset);
        if (stringRva is null)
        {
            return false;
        }

        var stringVa = unchecked((int)(imageBase + stringRva.Value));
        var pushNeedle = new byte[]
        {
            0x68,
            (byte)stringVa,
            (byte)(stringVa >> 8),
            (byte)(stringVa >> 16),
            (byte)(stringVa >> 24),
        };

        var pushOffset = IndexOf(image, pushNeedle);
        if (pushOffset < 0)
        {
            return false;
        }

        // CreateMutexA call site is immediately after push name / push 1 / push attrs.
        var searchStart = pushOffset;
        var searchEnd = Math.Min(image.Length - CmpAlreadyExists.Length - 1, pushOffset + 96);
        for (var i = searchStart; i <= searchEnd; i++)
        {
            if (!MatchesAt(image, i, CmpAlreadyExists))
            {
                continue;
            }

            var opcode = image[i + CmpAlreadyExists.Length];
            if (opcode is 0x75 or 0xEB)
            {
                branchOffset = i + CmpAlreadyExists.Length;
                branchOpcode = opcode;
                return true;
            }
        }

        return false;
    }

    private static uint ReadImageBase(byte[] image)
    {
        if (image.Length < 0x40)
        {
            return 0x400000;
        }

        var peOffset = BitConverter.ToInt32(image, 0x3C);
        if (peOffset <= 0 || peOffset + 0x38 >= image.Length)
        {
            return 0x400000;
        }

        // PE32 OptionalHeader.ImageBase at optional+28 → file pe+24+28 = pe+52
        return BitConverter.ToUInt32(image, peOffset + 52);
    }

    private static uint? FileOffsetToRva(byte[] image, int fileOffset)
    {
        var peOffset = BitConverter.ToInt32(image, 0x3C);
        var numberOfSections = BitConverter.ToUInt16(image, peOffset + 6);
        var sizeOfOptionalHeader = BitConverter.ToUInt16(image, peOffset + 20);
        var sectionTable = peOffset + 24 + sizeOfOptionalHeader;

        for (var i = 0; i < numberOfSections; i++)
        {
            var off = sectionTable + i * 40;
            if (off + 40 > image.Length)
            {
                break;
            }

            var virtualAddress = BitConverter.ToUInt32(image, off + 12);
            var sizeOfRawData = BitConverter.ToUInt32(image, off + 16);
            var pointerToRawData = BitConverter.ToUInt32(image, off + 20);
            if (fileOffset >= pointerToRawData && fileOffset < pointerToRawData + sizeOfRawData)
            {
                return virtualAddress + (uint)(fileOffset - pointerToRawData);
            }
        }

        // Flat mapping fallback (common for this era PE when raw==rva).
        return (uint)fileOffset;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (MatchesAt(haystack, i, needle))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool MatchesAt(byte[] haystack, int offset, byte[] needle)
    {
        for (var i = 0; i < needle.Length; i++)
        {
            if (haystack[offset + i] != needle[i])
            {
                return false;
            }
        }

        return true;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            int nSize,
            out IntPtr lpNumberOfBytesWritten);
    }
}

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
