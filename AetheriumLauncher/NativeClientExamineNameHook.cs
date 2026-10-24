using System.Buffers.Binary;

namespace AcLegacyLauncher;

internal static partial class NativeClientDddAcceleration
{
    internal const int ExamineNameStolenLength = 5;
    internal const int ExamineNameCaveCodeLength = 25;
    internal const int DachshundCreatureType = 102;

    private const int ExamineNameCaseIndex = 76;
    private const int ExamineNameEpilogueDelta = 12;
    private const int ExamineNameJumpOpcodeOffset = 14;
    private const int ExamineNameTableOffset = 17;
    private const int ExamineNameSiteLength = 21;
    private const int ExamineNameCasePrefixLength = 7;
    private const int MaxExamineNameScanLength = 8 * 1024 * 1024;
    private const uint ImageSectionExecute = 0x2000_0000;

    private static readonly byte[] ExamineNameSignature =
    [
        0x8B, 0x44, 0x24, 0x04, 0x48, 0x83, 0xF8, 0x4C, 0x0F, 0x87,
    ];

    private static readonly byte[] ExamineNameText = "Dachshund\0"u8.ToArray();

    internal readonly record struct ExamineNameSite(uint SwitchAddress, uint EpilogueAddress);

    private readonly record struct PeCodeSection(
        uint VirtualAddress,
        int VirtualSize,
        int RawPointer,
        int RawSize);

    private sealed class ExamineNameHookInstallation
    {
        internal IntPtr Cave;
        internal IntPtr SwitchAddress;
        internal bool PatchAttempted;
        internal string Detail { get; set; } = string.Empty;
        internal List<RemotePatchRegion> Regions { get; } = [];
    }

    /// <summary>
    /// Creature-type names in the ID panel come from a switch on property 2,
    /// not from portal.dat. Types 1 through 77 use the existing jump table.
    /// Type 102 is past that table and would otherwise display as Unknown.
    /// The detour is registered with the other patch regions. The runtime
    /// integrity check compares those registered bytes; it does not hash the
    /// examine function separately.
    /// </summary>
    private static void InstallExamineNameHook(
        IntPtr processHandle,
        ExamineNameHookInstallation install)
    {
        if (!TryReadExamineNameSite(processHandle, out var site, out var detail))
        {
            install.Detail = detail;
            LogExamineNameHook(detail);
            return;
        }

        var caveSize = ExamineNameCaveCodeLength + ExamineNameText.Length;
        var cave = VirtualAllocEx(
            processHandle,
            IntPtr.Zero,
            (nuint)caveSize,
            MemCommit | MemReserve,
            PageReadWrite);
        if (cave == IntPtr.Zero)
        {
            throw Win32Failure("VirtualAllocEx failed while preparing the examine-name hook");
        }

        install.Cave = cave;
        var switchAddress = Address(site.SwitchAddress);
        var image = BuildExamineNameCave(
            cave,
            switchAddress,
            Address(site.EpilogueAddress));
        WriteExact(processHandle, cave, image, "examine-name cave");
        ProtectExact(
            processHandle,
            cave,
            (nuint)image.Length,
            PageExecuteRead,
            "examine-name cave");
        FlushExact(processHandle, cave, (nuint)image.Length, "examine-name cave");
        VerifyRemoteBytes(processHandle, cave, image, "installed examine-name cave");

        var patch = BuildExamineNameDetour(switchAddress, cave);
        install.SwitchAddress = switchAddress;
        install.PatchAttempted = true;
        WriteProtectedExact(processHandle, switchAddress, patch, "examine-name detour");
        FlushExact(
            processHandle,
            switchAddress,
            (nuint)patch.Length,
            "examine-name detour");
        VerifyRemoteBytes(processHandle, switchAddress, patch, "installed examine-name detour");

        install.Regions.Add(new RemotePatchRegion(
            switchAddress,
            patch,
            "installed examine-name detour"));
        install.Regions.Add(new RemotePatchRegion(
            cave,
            image,
            "installed examine-name cave"));
        install.Detail =
            "Examine-name hook active (creature type 102 displays as Dachshund).";
        LogExamineNameHook(
            $"{install.Detail} switch=0x{site.SwitchAddress:X8} epilogue=0x{site.EpilogueAddress:X8}");
    }

    internal static byte[] BuildExamineNameCave(
        IntPtr caveAddress,
        IntPtr switchAddress,
        IntPtr epilogueAddress)
    {
        var image = new byte[ExamineNameCaveCodeLength + ExamineNameText.Length];
        image[0] = 0x8B;
        image[1] = 0x44;
        image[2] = 0x24;
        image[3] = 0x04;
        image[4] = 0x83;
        image[5] = 0xF8;
        image[6] = DachshundCreatureType;
        image[7] = 0x74;
        const int dogOffset = 15;
        const int jeNextOffset = 9;
        var jeDisplacement = dogOffset - jeNextOffset;
        if (jeDisplacement is < sbyte.MinValue or > sbyte.MaxValue)
        {
            throw new InvalidOperationException("The examine-name type check is out of short-jump range.");
        }

        image[8] = (byte)jeDisplacement;
        image[9] = 0x48;
        image[10] = 0xE9;
        WriteRelativeDisplacement(
            image,
            11,
            Add(caveAddress, 10),
            Add(switchAddress, ExamineNameStolenLength));
        image[15] = 0xB9;
        var nameAddress = Add(caveAddress, ExamineNameCaveCodeLength);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(16, sizeof(int)),
            unchecked((int)nameAddress.ToInt64()));
        image[20] = 0xE9;
        WriteRelativeDisplacement(
            image,
            21,
            Add(caveAddress, 20),
            epilogueAddress);
        ExamineNameText.CopyTo(image, ExamineNameCaveCodeLength);
        return image;
    }

    internal static byte[] BuildExamineNameDetour(IntPtr switchAddress, IntPtr caveAddress)
    {
        var patch = new byte[ExamineNameStolenLength];
        patch[0] = 0xE9;
        WriteRelativeDisplacement(patch, 1, switchAddress, caveAddress);
        return patch;
    }

    internal static bool TryLocateExamineNameSiteForTest(
        IReadOnlyList<(uint Address, byte[] Bytes)> ranges,
        Func<uint, int, byte[]?> read,
        out ExamineNameSite site,
        out string detail) =>
        TryLocateExamineNameSite(ranges, read, out site, out detail);

    internal static bool TryLocateExamineNameSiteInFileForTest(
        byte[] file,
        out ExamineNameSite site,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!TryParsePeExecutableSections(file, out var imageBase, out var sections, out var parseDetail))
        {
            site = default;
            detail = "Examine-name hook skipped: " + parseDetail;
            return false;
        }

        var ranges = new List<(uint Address, byte[] Bytes)>();
        foreach (var section in sections)
        {
            var mapped = Math.Min(section.VirtualSize, section.RawSize);
            if (mapped <= 0)
            {
                continue;
            }

            if (mapped > MaxExamineNameScanLength)
            {
                site = default;
                detail = "Examine-name hook skipped: executable section is outside the scan limit.";
                return false;
            }

            if (section.RawPointer > file.Length - mapped)
            {
                site = default;
                detail = "Examine-name hook skipped: executable section is outside the file.";
                return false;
            }

            var start = imageBase + section.VirtualAddress;
            if (start < imageBase)
            {
                site = default;
                detail = "Examine-name hook skipped: executable section address wrapped.";
                return false;
            }

            var bytes = new byte[mapped];
            file.AsSpan(section.RawPointer, mapped).CopyTo(bytes);
            ranges.Add((start, bytes));
        }

        return TryLocateExamineNameSite(
            ranges,
            (address, length) => ReadFileVirtual(file, imageBase, sections, address, length),
            out site,
            out detail);
    }

    private static bool TryReadExamineNameSite(
        IntPtr processHandle,
        out ExamineNameSite site,
        out string detail)
    {
        var header = ReadExact(
            processHandle,
            Address(PreferredImageBase),
            0x1000,
            "examine-name image headers");
        if (!TryParsePeExecutableSections(header, out _, out var sections, out var parseDetail))
        {
            site = default;
            detail = "Examine-name hook skipped: " + parseDetail;
            return false;
        }

        var ranges = new List<(uint Address, byte[] Bytes)>();
        foreach (var section in sections)
        {
            if (section.VirtualSize <= 0)
            {
                continue;
            }

            if (section.VirtualSize > MaxExamineNameScanLength)
            {
                site = default;
                detail = "Examine-name hook skipped: executable section is outside the scan limit.";
                return false;
            }

            var start = PreferredImageBase + section.VirtualAddress;
            if (start < PreferredImageBase)
            {
                site = default;
                detail = "Examine-name hook skipped: executable section address wrapped.";
                return false;
            }

            ranges.Add((
                start,
                ReadExact(
                    processHandle,
                    Address(start),
                    section.VirtualSize,
                    "examine-name executable section")));
        }

        return TryLocateExamineNameSite(
            ranges,
            (address, length) => ReadExact(
                processHandle,
                Address(address),
                length,
                "examine-name jump table"),
            out site,
            out detail);
    }

    private static bool TryLocateExamineNameSite(
        IReadOnlyList<(uint Address, byte[] Bytes)> ranges,
        Func<uint, int, byte[]?> read,
        out ExamineNameSite site,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(read);
        var hits = new List<(uint SwitchAddress, uint TableAddress)>();
        foreach (var (rangeAddress, bytes) in ranges)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            for (var index = 0; index + ExamineNameSiteLength <= bytes.Length; index++)
            {
                if (!bytes.AsSpan(index, ExamineNameSignature.Length)
                        .SequenceEqual(ExamineNameSignature))
                {
                    continue;
                }

                if (bytes[index + ExamineNameJumpOpcodeOffset] != 0xFF ||
                    bytes[index + ExamineNameJumpOpcodeOffset + 1] != 0x24 ||
                    bytes[index + ExamineNameJumpOpcodeOffset + 2] != 0x85)
                {
                    continue;
                }

                var switchAddress = rangeAddress + (uint)index;
                if (switchAddress < rangeAddress)
                {
                    continue;
                }

                hits.Add((
                    switchAddress,
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(index + ExamineNameTableOffset, sizeof(uint)))));
            }
        }

        if (hits.Count != 1)
        {
            site = default;
            detail = hits.Count == 0
                ? "Examine-name hook skipped: signature was not found."
                : $"Examine-name hook skipped: signature matched {hits.Count} times.";
            return false;
        }

        var tableLength = (ExamineNameCaseIndex + 1) * sizeof(uint);
        var table = read(hits[0].TableAddress, tableLength);
        if (table is null || table.Length != tableLength)
        {
            site = default;
            detail = "Examine-name hook skipped: jump table was not readable.";
            return false;
        }

        var caseAddress = BinaryPrimitives.ReadUInt32LittleEndian(
            table.AsSpan(ExamineNameCaseIndex * sizeof(uint), sizeof(uint)));
        if (caseAddress > uint.MaxValue - ExamineNameEpilogueDelta)
        {
            site = default;
            detail = "Examine-name hook skipped: type 77 case address is not usable.";
            return false;
        }

        var caseBytes = read(caseAddress, ExamineNameCasePrefixLength);
        if (caseBytes is null ||
            caseBytes.Length != ExamineNameCasePrefixLength ||
            caseBytes[0] != 0xB9 ||
            caseBytes[5] != 0xEB ||
            caseBytes[6] != 0x05)
        {
            site = default;
            detail = "Examine-name hook skipped: type 77 case is not mov ecx, imm32 / jmp short +5.";
            return false;
        }

        site = new ExamineNameSite(hits[0].SwitchAddress, caseAddress + ExamineNameEpilogueDelta);
        detail = string.Empty;
        return true;
    }

    private static byte[] ExamineNameOriginalPrologue() =>
        ExamineNameSignature[..ExamineNameStolenLength];

    private static bool TryParsePeExecutableSections(
        ReadOnlySpan<byte> image,
        out uint imageBase,
        out List<PeCodeSection> sections,
        out string detail)
    {
        imageBase = 0;
        sections = [];
        detail = "client image headers are not a PE32 module.";
        if (image.Length < 0x40 || image[0] != (byte)'M' || image[1] != (byte)'Z')
        {
            return false;
        }

        var lfanew = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(0x3C, sizeof(int)));
        if (lfanew <= 0 || lfanew > image.Length - 24)
        {
            return false;
        }

        if (image[lfanew] != (byte)'P' ||
            image[lfanew + 1] != (byte)'E' ||
            image[lfanew + 2] != 0 ||
            image[lfanew + 3] != 0)
        {
            return false;
        }

        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(image.Slice(lfanew + 6, sizeof(ushort)));
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(image.Slice(lfanew + 20, sizeof(ushort)));
        if (sectionCount == 0 || sectionCount > 96 || optionalSize < 32)
        {
            return false;
        }

        var optional = lfanew + 24;
        if (optional > image.Length - optionalSize)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(image.Slice(optional, sizeof(ushort))) != 0x10B)
        {
            return false;
        }

        imageBase = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(optional + 28, sizeof(uint)));
        var sectionTable = optional + optionalSize;
        var sectionBytes = sectionCount * 40;
        if (sectionTable > image.Length - sectionBytes)
        {
            return false;
        }

        for (var index = 0; index < sectionCount; index++)
        {
            var entry = sectionTable + (index * 40);
            var virtualSize = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(entry + 8, sizeof(int)));
            var virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(entry + 12, sizeof(uint)));
            var rawSize = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(entry + 16, sizeof(int)));
            var rawPointer = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(entry + 20, sizeof(int)));
            var characteristics = BinaryPrimitives.ReadUInt32LittleEndian(image.Slice(entry + 36, sizeof(uint)));
            if ((characteristics & ImageSectionExecute) == 0)
            {
                continue;
            }

            if (virtualSize < 0 || rawSize < 0 || rawPointer < 0)
            {
                sections = [];
                return false;
            }

            sections.Add(new PeCodeSection(virtualAddress, virtualSize, rawPointer, rawSize));
        }

        if (sections.Count == 0)
        {
            detail = "client image has no executable section.";
            return false;
        }

        detail = string.Empty;
        return true;
    }

    private static byte[]? ReadFileVirtual(
        byte[] file,
        uint imageBase,
        IReadOnlyList<PeCodeSection> sections,
        uint address,
        int length)
    {
        if (length <= 0 || address < imageBase)
        {
            return null;
        }

        var rva = address - imageBase;
        foreach (var section in sections)
        {
            var mapped = Math.Min(section.VirtualSize, section.RawSize);
            if (mapped < length || rva < section.VirtualAddress)
            {
                continue;
            }

            var offset = rva - section.VirtualAddress;
            if ((ulong)offset + (ulong)length > (ulong)mapped)
            {
                continue;
            }

            var fileOffset = section.RawPointer + (int)offset;
            if (fileOffset < 0 || fileOffset > file.Length - length)
            {
                return null;
            }

            var bytes = new byte[length];
            file.AsSpan(fileOffset, length).CopyTo(bytes);
            return bytes;
        }

        return null;
    }

    private static void LogExamineNameHook(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AetheriumPlay");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "examine-name.log"),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // The installation detail still reports the same outcome.
        }
    }
}
