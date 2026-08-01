using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AcLegacyLauncher;

internal sealed class NativeClientDddAccelerationInstallation
{
    internal NativeClientDddAccelerationInstallation(
        string detail,
        IReadOnlyList<NativeClientDddAcceleration.RemotePatchRegion> regions)
    {
        Detail = detail;
        Regions = regions;
    }

    internal string Detail { get; }

    internal IReadOnlyList<NativeClientDddAcceleration.RemotePatchRegion> Regions { get; }
}

/// <summary>
/// Applies the Aetherium CLIDAT queue-drain hook to the one public DM client
/// build whose machine code has been verified. The patch is process-local; it
/// never modifies client.exe on disk.
///
/// The verified retail layout exposes each Asynch_Cache pending-request count
/// at +0x60. Inbound consumption pauses when either writer reaches the
/// conservative high-water policy below, including before the first consume of
/// a frame. The completion hook waits for an empty inbound queue and both
/// workers to return from their save/iteration requests. This is worker-drained
/// completion, not a claim of power-loss durability through FlushFileBuffers.
/// </summary>
internal static class NativeClientDddAcceleration
{
    internal const int MaxMessagesPerFrame = 8;
    // Conservative policy, not a native-client capacity claim. At 32 pending
    // requests the wrapper pauses inbound consumption until that writer catches up.
    internal const int AsyncPendingHighWater = 32;
    internal const uint PreferredImageBase = 0x0040_0000;
    internal const uint UseTimeRva = 0x0000_DA00;
    internal const uint DownloadStatusRva = 0x0000_CFB0;
    internal const uint DownloadStatusTailRva = DownloadStatusRva + 0x24;
    internal const uint VersionLiteralRva = 0x001F_112C;
    internal const int UseTimeTrampolineOffset = 0x80;
    internal const int DownloadStatusWrapperOffset = 0xA0;
    internal const int RemoteCodeSize = 0x100;
    internal const string CapabilityVersion = "2005.02.A09";

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint PageExecuteRead = 0x20;
    private const uint PageExecuteReadWrite = 0x40;

    private static readonly byte[] ExpectedUseTimeSignature =
    [
        0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC,
        0x08, 0x53, 0x55, 0x56, 0x57, 0x8B, 0xF9, 0x8B,
        0x47, 0x04, 0x85, 0xC0, 0xBE, 0xF8, 0xB7, 0x7E,
        0x00, 0x75, 0x37, 0xA1, 0x78, 0x08, 0x66, 0x00,
    ];

    private static readonly byte[] OriginalUseTimePrologue =
    [
        0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8,
    ];

    private static readonly byte[] ExpectedDownloadStatusSignature =
    [
        0x8B, 0x41, 0x10, 0x8B, 0x54, 0x24, 0x04, 0x89,
        0x02, 0x8B, 0x41, 0x14, 0x8B, 0x54, 0x24, 0x08,
        0x89, 0x02, 0x8B, 0x41, 0x1C, 0x8B, 0x54, 0x24,
        0x0C, 0x89, 0x02, 0x8B, 0x41, 0x20, 0x8B, 0x54,
        0x24, 0x10, 0x89, 0x02, 0x8B, 0x51, 0x18, 0x33,
        0xC0, 0x83, 0xFA, 0x03, 0x0F, 0x94, 0xC0, 0xC2,
        0x10, 0x00,
    ];

    private static readonly byte[] OriginalDownloadStatusTail =
    [
        0x8B, 0x51, 0x18, 0x33, 0xC0, 0x83, 0xFA,
        0x03, 0x0F, 0x94, 0xC0, 0xC2, 0x10, 0x00,
    ];

    private static readonly byte[] DownloadStatusDrainWrapper =
    [
        0x33, 0xC0,                         // xor eax, eax
        0x83, 0x79, 0x18, 0x03,             // cmp dword ptr [ecx+18h], 3
        0x75, 0x27,                         // jne false
        0x8B, 0x51, 0x04,                   // mov edx, [ecx+4] (queue header)
        0x85, 0xD2,                         // test edx, edx
        0x74, 0x20,                         // jz false
        0x83, 0x3A, 0x00,                   // cmp dword ptr [edx], 0 (queue head)
        0x75, 0x1B,                         // jne false
        0x8B, 0x51, 0x08,                   // mov edx, [ecx+8] (portal cache)
        0x85, 0xD2,                         // test edx, edx
        0x74, 0x06,                         // jz cell
        0x83, 0x7A, 0x60, 0x00,             // cmp dword ptr [edx+60h], 0
        0x75, 0x0E,                         // jne false
        0x8B, 0x51, 0x0C,                   // cell: mov edx, [ecx+0Ch]
        0x85, 0xD2,                         // test edx, edx
        0x74, 0x06,                         // jz true
        0x83, 0x7A, 0x60, 0x00,             // cmp dword ptr [edx+60h], 0
        0x75, 0x01,                         // jne false
        0x40,                               // true: inc eax
        0xC2, 0x10, 0x00,                   // false: ret 10h
    ];

    private static readonly byte[] OriginalVersionLiteral =
        Encoding.ASCII.GetBytes("2005.02.001\0");

    private static readonly byte[] CapabilityVersionLiteral =
        Encoding.ASCII.GetBytes(CapabilityVersion + "\0");

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
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
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
        IntPtr baseAddress,
        nuint size);

    internal static NativeClientDddAccelerationInstallation Apply(
        string clientPath,
        IntPtr processHandle)
    {
        if (Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "Accelerated DAT repair requires the x86 Aetherium Launcher build.");
        }

        VerifySupportedClientFile(clientPath);

        var useTimeAddress = Address(PreferredImageBase + UseTimeRva);
        var downloadStatusAddress = Address(PreferredImageBase + DownloadStatusRva);
        var downloadStatusTailAddress = Address(PreferredImageBase + DownloadStatusTailRva);
        var versionAddress = Address(PreferredImageBase + VersionLiteralRva);
        VerifyRemoteBytes(
            processHandle,
            useTimeAddress,
            ExpectedUseTimeSignature,
            "CLCache::UseTime");
        VerifyRemoteBytes(
            processHandle,
            downloadStatusAddress,
            ExpectedDownloadStatusSignature,
            "CLCache::Get_Download_Status");
        VerifyRemoteBytes(
            processHandle,
            versionAddress,
            OriginalVersionLiteral,
            "client version literal");

        var remoteCode = VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            RemoteCodeSize,
            MemCommit | MemReserve,
            PageReadWrite);
        if (remoteCode == IntPtr.Zero)
        {
            throw Win32Failure("VirtualAllocEx failed while preparing accelerated DAT repair");
        }

        var committed = false;
        var useTimePatchAttempted = false;
        var downloadStatusPatchAttempted = false;
        var versionPatchAttempted = false;
        try
        {
            var code = BuildRemoteCode(remoteCode);
            WriteExact(processHandle, remoteCode, code, "accelerated DAT repair code");
            ProtectExact(
                processHandle,
                remoteCode,
                (nuint)code.Length,
                PageExecuteRead,
                "accelerated DAT repair code");
            FlushExact(
                processHandle,
                remoteCode,
                (nuint)code.Length,
                "accelerated DAT repair code");
            VerifyRemoteBytes(
                processHandle,
                remoteCode,
                code,
                "installed accelerated DAT repair code");

            var entryPatch = BuildEntryPatch(useTimeAddress, remoteCode);
            useTimePatchAttempted = true;
            WriteProtectedExact(
                processHandle,
                useTimeAddress,
                entryPatch,
                "CLCache::UseTime detour");
            FlushExact(
                processHandle,
                useTimeAddress,
                (nuint)entryPatch.Length,
                "CLCache::UseTime detour");

            var downloadStatusPatch = BuildDownloadStatusTailPatch(
                downloadStatusTailAddress,
                Add(remoteCode, DownloadStatusWrapperOffset));
            downloadStatusPatchAttempted = true;
            WriteProtectedExact(
                processHandle,
                downloadStatusTailAddress,
                downloadStatusPatch,
                "CLCache::Get_Download_Status drain detour");
            FlushExact(
                processHandle,
                downloadStatusTailAddress,
                (nuint)downloadStatusPatch.Length,
                "CLCache::Get_Download_Status drain detour");

            // The server echoes this exact same-length version in PacketTwo. The
            // client also compares PacketTwo against this literal, so the marker
            // advertises the fully-installed hooks without changing the legacy
            // wire layout. Keep this write last so a partial install is never
            // advertised to the accelerated server path.
            versionPatchAttempted = true;
            WriteProtectedExact(
                processHandle,
                versionAddress,
                CapabilityVersionLiteral,
                "accelerated DAT repair capability marker");

            var installation = new NativeClientDddAccelerationInstallation(
                $"Accelerated DAT repair enabled ({CapabilityVersion}; up to " +
                $"{MaxMessagesPerFrame} queued records per frame; async writer guard active).",
                [
                    new RemotePatchRegion(
                        remoteCode,
                        code,
                        "installed accelerated DAT repair code"),
                    new RemotePatchRegion(
                        useTimeAddress,
                        entryPatch,
                        "installed CLCache::UseTime detour"),
                    new RemotePatchRegion(
                        downloadStatusTailAddress,
                        downloadStatusPatch,
                        "installed CLCache::Get_Download_Status drain detour"),
                    new RemotePatchRegion(
                        versionAddress,
                        CapabilityVersionLiteral,
                        "installed client version marker"),
                ]);
            VerifyInstalled(processHandle, installation);

            committed = true;
            return installation;
        }
        catch (Exception patchError)
        {
            var rollbackErrors = new List<Exception>();
            if (versionPatchAttempted)
            {
                TryRollback(
                    processHandle,
                    versionAddress,
                    OriginalVersionLiteral,
                    "client version marker",
                    rollbackErrors);
            }

            if (downloadStatusPatchAttempted)
            {
                TryRollback(
                    processHandle,
                    downloadStatusTailAddress,
                    OriginalDownloadStatusTail,
                    "CLCache::Get_Download_Status drain detour",
                    rollbackErrors);
                TryFlushRollback(
                    processHandle,
                    downloadStatusTailAddress,
                    OriginalDownloadStatusTail.Length,
                    "CLCache::Get_Download_Status rollback",
                    rollbackErrors);
            }

            if (useTimePatchAttempted)
            {
                TryRollback(
                    processHandle,
                    useTimeAddress,
                    OriginalUseTimePrologue,
                    "CLCache::UseTime detour",
                    rollbackErrors);
                TryFlushRollback(
                    processHandle,
                    useTimeAddress,
                    OriginalUseTimePrologue.Length,
                    "CLCache::UseTime rollback",
                    rollbackErrors);
            }

            if (rollbackErrors.Count != 0)
            {
                rollbackErrors.Insert(0, patchError);
                throw new AggregateException(
                    "Accelerated DAT repair failed and could not be fully rolled back. " +
                    "The suspended client must not be resumed.",
                    rollbackErrors);
            }

            throw new InvalidOperationException(
                "Accelerated DAT repair could not be installed. The suspended client was left " +
                "unmodified and will not be resumed.",
                patchError);
        }
        finally
        {
            if (!committed)
            {
                VirtualFreeEx(processHandle, remoteCode, 0, MemRelease);
            }
        }
    }

    internal static void VerifyInstalled(
        IntPtr processHandle,
        NativeClientDddAccelerationInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        foreach (var region in installation.Regions)
        {
            VerifyRemoteBytes(
                processHandle,
                region.Address,
                region.ExpectedBytes,
                region.Label);
        }
    }

    internal sealed class RemotePatchRegion
    {
        internal RemotePatchRegion(IntPtr address, byte[] expectedBytes, string label)
        {
            Address = address;
            ExpectedBytes = (byte[])expectedBytes.Clone();
            Label = label;
        }

        internal IntPtr Address { get; }

        internal byte[] ExpectedBytes { get; }

        internal string Label { get; }
    }

    internal static byte[] BuildRemoteCode(IntPtr remoteCodeAddress)
    {
        var code = Enumerable.Repeat((byte)0xCC, RemoteCodeSize).ToArray();
        var trampolineAddress = Add(remoteCodeAddress, UseTimeTrampolineOffset);
        var wrapper = BuildUseTimeWrapper(
            remoteCodeAddress,
            trampolineAddress,
            out _);
        if (wrapper.Length > UseTimeTrampolineOffset)
        {
            throw new InvalidOperationException(
                "The CLCache::UseTime wrapper overlaps its trampoline.");
        }

        wrapper.CopyTo(code, 0);

        OriginalUseTimePrologue.CopyTo(code, UseTimeTrampolineOffset);
        code[UseTimeTrampolineOffset + OriginalUseTimePrologue.Length] = 0xE9;
        var trampolineJumpAddress = Add(
            remoteCodeAddress,
            UseTimeTrampolineOffset + OriginalUseTimePrologue.Length);
        var originalContinuation = Address(PreferredImageBase + UseTimeRva + 6);
        WriteRelativeDisplacement(
            code,
            UseTimeTrampolineOffset + OriginalUseTimePrologue.Length + 1,
            trampolineJumpAddress,
            originalContinuation);

        var trampolineEnd = UseTimeTrampolineOffset + OriginalUseTimePrologue.Length + 5;
        if (trampolineEnd > DownloadStatusWrapperOffset)
        {
            throw new InvalidOperationException(
                "The CLCache::UseTime trampoline overlaps the download-status wrapper.");
        }

        var statusEnd = DownloadStatusWrapperOffset + DownloadStatusDrainWrapper.Length;
        if (statusEnd > code.Length)
        {
            throw new InvalidOperationException(
                "The download-status wrapper exceeds the remote-code allocation.");
        }

        DownloadStatusDrainWrapper.CopyTo(code, DownloadStatusWrapperOffset);
        return code;
    }

    private static byte[] BuildUseTimeWrapper(
        IntPtr wrapperAddress,
        IntPtr trampolineAddress,
        out int originalCallOffset)
    {
        var code = new X86CodeBuilder();
        code.Emit(0x53);                                      // push ebx
        code.Emit(0x56);                                      // push esi
        code.Emit(0x57);                                      // push edi
        code.Emit(0x8B, 0xF1);                                // mov esi, ecx
        code.Emit(0xBB, (byte)MaxMessagesPerFrame, 0, 0, 0);  // mov ebx, 8

        code.Mark("check_inbound");
        code.Emit(0x8B, 0x46, 0x04);                          // mov eax, [esi+4]
        code.Emit(0x85, 0xC0);                                // test eax, eax
        code.JumpIf(0x84, "call_original");                  // jz call_original
        code.Emit(0x83, 0x38, 0x00);                          // cmp dword ptr [eax], 0
        code.JumpIf(0x84, "call_original");                  // jz call_original

        // With inbound work present, refuse even the first consume while a
        // writer is at the high-water mark. Null cache pointers mean that cache
        // has not been constructed and therefore has no pending requests.
        code.Emit(0x8B, 0x7E, 0x08);                          // mov edi, [esi+8]
        code.Emit(0x85, 0xFF);                                // test edi, edi
        code.JumpIf(0x84, "check_cell");                     // jz check_cell
        code.Emit(0x83, 0x7F, 0x60, (byte)AsyncPendingHighWater);
        code.JumpIf(0x83, "done");                           // jae done

        code.Mark("check_cell");
        code.Emit(0x8B, 0x7E, 0x0C);                          // mov edi, [esi+0Ch]
        code.Emit(0x85, 0xFF);                                // test edi, edi
        code.JumpIf(0x84, "call_original");                  // jz call_original
        code.Emit(0x83, 0x7F, 0x60, (byte)AsyncPendingHighWater);
        code.JumpIf(0x83, "done");                           // jae done

        code.Mark("call_original");
        code.Emit(0x8B, 0xCE);                                // mov ecx, esi
        originalCallOffset = code.Position;
        code.Emit(0xE8, 0, 0, 0, 0);                         // call trampoline
        code.Emit(0x4B);                                      // dec ebx
        code.JumpIf(0x84, "done");                           // jz done

        // Do not turn an empty queue into repeated maintenance calls. If more
        // work remains, loop back through both pending-count checks.
        code.Emit(0x8B, 0x46, 0x04);                          // mov eax, [esi+4]
        code.Emit(0x85, 0xC0);                                // test eax, eax
        code.JumpIf(0x84, "done");                           // jz done
        code.Emit(0x83, 0x38, 0x00);                          // cmp dword ptr [eax], 0
        code.JumpIf(0x84, "done");                           // jz done
        code.Jump("check_inbound");

        code.Mark("done");
        code.Emit(0x5F);                                      // pop edi
        code.Emit(0x5E);                                      // pop esi
        code.Emit(0x5B);                                      // pop ebx
        code.Emit(0xC3);                                      // ret

        var wrapper = code.Build();
        WriteRelativeDisplacement(
            wrapper,
            originalCallOffset + 1,
            Add(wrapperAddress, originalCallOffset),
            trampolineAddress);
        return wrapper;
    }

    internal static byte[] BuildEntryPatch(IntPtr useTimeAddress, IntPtr wrapperAddress)
    {
        var patch = new byte[OriginalUseTimePrologue.Length];
        patch[0] = 0xE9;
        WriteRelativeDisplacement(patch, 1, useTimeAddress, wrapperAddress);
        patch[5] = 0x90;
        return patch;
    }

    internal static byte[] BuildDownloadStatusTailPatch(
        IntPtr downloadStatusTailAddress,
        IntPtr wrapperAddress)
    {
        var patch = Enumerable.Repeat((byte)0x90, OriginalDownloadStatusTail.Length).ToArray();
        patch[0] = 0xE9;
        WriteRelativeDisplacement(patch, 1, downloadStatusTailAddress, wrapperAddress);
        return patch;
    }

    internal static IntPtr DecodeRelativeTarget(
        byte[] code,
        int instructionOffset,
        IntPtr codeBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (instructionOffset < 0 || instructionOffset + 5 > code.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(instructionOffset));
        }

        if (code[instructionOffset] is not (0xE8 or 0xE9))
        {
            throw new ArgumentException("Instruction is not an x86 near CALL/JMP.", nameof(code));
        }

        var displacement = BinaryPrimitives.ReadInt32LittleEndian(
            code.AsSpan(instructionOffset + 1, sizeof(int)));
        return Add(codeBaseAddress, instructionOffset + 5L + displacement);
    }

    internal static byte[] SupportedSignatureForTest() =>
        (byte[])ExpectedUseTimeSignature.Clone();

    internal static byte[] DownloadStatusSignatureForTest() =>
        (byte[])ExpectedDownloadStatusSignature.Clone();

    internal static byte[] OriginalUseTimePrologueForTest() =>
        (byte[])OriginalUseTimePrologue.Clone();

    internal static byte[] OriginalDownloadStatusTailForTest() =>
        (byte[])OriginalDownloadStatusTail.Clone();

    internal static byte[] DownloadStatusDrainWrapperForTest() =>
        (byte[])DownloadStatusDrainWrapper.Clone();

    internal static int UseTimeWrapperLengthForTest(IntPtr remoteCodeAddress)
    {
        var wrapper = BuildUseTimeWrapper(
            remoteCodeAddress,
            Add(remoteCodeAddress, UseTimeTrampolineOffset),
            out _);
        return wrapper.Length;
    }

    internal static int UseTimeOriginalCallOffsetForTest(IntPtr remoteCodeAddress)
    {
        _ = BuildUseTimeWrapper(
            remoteCodeAddress,
            Add(remoteCodeAddress, UseTimeTrampolineOffset),
            out var callOffset);
        return callOffset;
    }

    internal static byte[] OriginalVersionForTest() =>
        (byte[])OriginalVersionLiteral.Clone();

    internal static byte[] CapabilityVersionForTest() =>
        (byte[])CapabilityVersionLiteral.Clone();

    private static void VerifySupportedClientFile(string clientPath)
    {
        var file = new FileInfo(clientPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("Missing client.exe.", clientPath);
        }

        if (file.Length != CommunityClientBootstrap.ExpectedSize)
        {
            throw new InvalidDataException(
                $"client.exe is {file.Length:N0} bytes; accelerated DAT repair requires the " +
                $"verified {CommunityClientBootstrap.ExpectedSize:N0}-byte DM client.");
        }

        using var stream = new FileStream(
            clientPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(
                actualHash,
                CommunityClientBootstrap.ExpectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"client.exe SHA-256 is {actualHash}; accelerated DAT repair requires the " +
                $"verified public client {CommunityClientBootstrap.ExpectedSha256}.");
        }
    }

    private static void VerifyRemoteBytes(
        IntPtr processHandle,
        IntPtr address,
        byte[] expected,
        string label)
    {
        var actual = ReadExact(processHandle, address, expected.Length, label);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidDataException(
                $"The client {label} bytes did not match the expected verified A09 state.");
        }
    }

    private static byte[] ReadExact(
        IntPtr processHandle,
        IntPtr address,
        int length,
        string label)
    {
        var buffer = new byte[length];
        if (!ReadProcessMemory(
                processHandle,
                address,
                buffer,
                (nuint)buffer.Length,
                out var bytesRead)
            || bytesRead != (nuint)buffer.Length)
        {
            throw Win32Failure($"ReadProcessMemory failed for {label}");
        }

        return buffer;
    }

    private static void WriteExact(
        IntPtr processHandle,
        IntPtr address,
        byte[] bytes,
        string label)
    {
        if (!WriteProcessMemory(
                processHandle,
                address,
                bytes,
                (nuint)bytes.Length,
                out var bytesWritten)
            || bytesWritten != (nuint)bytes.Length)
        {
            throw Win32Failure($"WriteProcessMemory failed for {label}");
        }
    }

    private static void ProtectExact(
        IntPtr processHandle,
        IntPtr address,
        nuint length,
        uint protection,
        string label)
    {
        if (!VirtualProtectEx(processHandle, address, length, protection, out _))
        {
            throw Win32Failure($"VirtualProtectEx failed for {label}");
        }
    }

    private static void WriteProtectedExact(
        IntPtr processHandle,
        IntPtr address,
        byte[] bytes,
        string label)
    {
        if (!VirtualProtectEx(
                processHandle,
                address,
                (nuint)bytes.Length,
                PageExecuteReadWrite,
                out var originalProtection))
        {
            throw Win32Failure($"VirtualProtectEx failed while opening {label}");
        }

        Exception? writeError = null;
        try
        {
            WriteExact(processHandle, address, bytes, label);
        }
        catch (Exception error)
        {
            writeError = error;
        }

        if (!VirtualProtectEx(
                processHandle,
                address,
                (nuint)bytes.Length,
                originalProtection,
                out _))
        {
            var protectionError = Win32Failure(
                $"VirtualProtectEx failed while restoring {label}");
            if (writeError is not null)
            {
                throw new AggregateException(writeError, protectionError);
            }

            throw protectionError;
        }

        if (writeError is not null)
        {
            ExceptionDispatchInfo.Capture(writeError).Throw();
        }
    }

    private static void FlushExact(
        IntPtr processHandle,
        IntPtr address,
        nuint length,
        string label)
    {
        if (!FlushInstructionCache(processHandle, address, length))
        {
            throw Win32Failure($"FlushInstructionCache failed for {label}");
        }
    }

    private static void TryRollback(
        IntPtr processHandle,
        IntPtr address,
        byte[] originalBytes,
        string label,
        List<Exception> errors)
    {
        try
        {
            WriteProtectedExact(processHandle, address, originalBytes, $"{label} rollback");
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private static void TryFlushRollback(
        IntPtr processHandle,
        IntPtr address,
        int length,
        string label,
        List<Exception> errors)
    {
        try
        {
            FlushExact(
                processHandle,
                address,
                (nuint)length,
                label);
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private static void WriteRelativeDisplacement(
        byte[] destination,
        int displacementOffset,
        IntPtr instructionAddress,
        IntPtr targetAddress)
    {
        var nextInstruction = instructionAddress.ToInt64() + 5;
        var displacement = targetAddress.ToInt64() - nextInstruction;
        if (displacement is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidOperationException("The x86 hook target is outside rel32 range.");
        }

        BinaryPrimitives.WriteInt32LittleEndian(
            destination.AsSpan(displacementOffset, sizeof(int)),
            (int)displacement);
    }

    private static IntPtr Address(uint value) => new(unchecked((int)value));

    private static IntPtr Add(IntPtr address, long offset) =>
        new(checked(address.ToInt64() + offset));

    private static Win32Exception Win32Failure(string message) =>
        new(Marshal.GetLastWin32Error(), message);

    private sealed class X86CodeBuilder
    {
        private readonly List<byte> _bytes = [];
        private readonly Dictionary<string, int> _labels =
            new(StringComparer.Ordinal);
        private readonly List<(int DisplacementOffset, string Label)> _fixups = [];

        internal int Position => _bytes.Count;

        internal void Emit(params byte[] bytes) => _bytes.AddRange(bytes);

        internal void Mark(string label)
        {
            if (!_labels.TryAdd(label, Position))
            {
                throw new InvalidOperationException($"Duplicate x86 label: {label}");
            }
        }

        internal void JumpIf(byte conditionOpcode, string label)
        {
            Emit(0x0F, conditionOpcode);
            AddFixup(label);
        }

        internal void Jump(string label)
        {
            Emit(0xE9);
            AddFixup(label);
        }

        internal byte[] Build()
        {
            var result = _bytes.ToArray();
            foreach (var (displacementOffset, label) in _fixups)
            {
                if (!_labels.TryGetValue(label, out var targetOffset))
                {
                    throw new InvalidOperationException($"Undefined x86 label: {label}");
                }

                var displacement = targetOffset - (displacementOffset + sizeof(int));
                BinaryPrimitives.WriteInt32LittleEndian(
                    result.AsSpan(displacementOffset, sizeof(int)),
                    displacement);
            }

            return result;
        }

        private void AddFixup(string label)
        {
            var displacementOffset = Position;
            Emit(0, 0, 0, 0);
            _fixups.Add((displacementOffset, label));
        }
    }
}
