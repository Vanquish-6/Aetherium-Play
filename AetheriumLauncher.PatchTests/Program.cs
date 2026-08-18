using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AcLegacyLauncher;

static void Equal<T>(T expected, T actual, string label)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"{label}: expected {expected}, got {actual}");
    }
}

static void True(bool value, string label)
{
    if (!value)
    {
        throw new InvalidOperationException($"{label}: expected true");
    }
}

static void BytesEqual(byte[] expected, byte[] actual, string label)
{
    if (!actual.AsSpan().SequenceEqual(expected))
    {
        throw new InvalidOperationException(
            $"{label}: expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(actual)}");
    }
}

static int DecodeInternalNearBranch(byte[] code, int instructionOffset)
{
    int length;
    int displacementOffset;
    if (code[instructionOffset] == 0xE9)
    {
        length = 5;
        displacementOffset = instructionOffset + 1;
    }
    else if (code[instructionOffset] == 0x0F &&
             code[instructionOffset + 1] is >= 0x80 and <= 0x8F)
    {
        length = 6;
        displacementOffset = instructionOffset + 2;
    }
    else
    {
        throw new InvalidOperationException(
            $"No near branch at wrapper offset {instructionOffset}.");
    }

    var displacement = BinaryPrimitives.ReadInt32LittleEndian(
        code.AsSpan(displacementOffset, sizeof(int)));
    return instructionOffset + length + displacement;
}

var useTimeSignature = NativeClientDddAcceleration.SupportedSignatureForTest();
Equal(32, useTimeSignature.Length, "UseTime signature length");
BytesEqual(
    Convert.FromHexString(
        "558BEC83E4F883EC08535556578BF98B470485C0BEF8B77E007537A178086600"),
    useTimeSignature,
    "UseTime signature");

var downloadStatusSignature =
    NativeClientDddAcceleration.DownloadStatusSignatureForTest();
Equal(50, downloadStatusSignature.Length, "download-status signature length");
BytesEqual(
    Convert.FromHexString(
        "8B41108B54240489028B41148B54240889028B411C8B54240C89028B41208B542410" +
        "89028B511833C083FA030F94C0C21000"),
    downloadStatusSignature,
    "download-status signature");

var originalUseTime = NativeClientDddAcceleration.OriginalUseTimePrologueForTest();
var originalDownloadStatus =
    NativeClientDddAcceleration.OriginalDownloadStatusTailForTest();
BytesEqual(
    Convert.FromHexString("558BEC83E4F8"),
    originalUseTime,
    "UseTime rollback bytes");
BytesEqual(
    Convert.FromHexString("8B511833C083FA030F94C0C21000"),
    originalDownloadStatus,
    "download-status rollback bytes");

var originalSpellRegion = NativeClientDddAcceleration.OriginalSpellRegionPrologueForTest();
BytesEqual(
    Convert.FromHexString("83EC18568BF1"),
    originalSpellRegion,
    "SpellRegion rollback bytes");

BytesEqual(
    Convert.FromHexString("83EC0C5355568BF1"),
    NativeClientDddAcceleration.OriginalAllegPanelPrologueForTest(),
    "AllegPanel rollback bytes");

var originalVersion = NativeClientDddAcceleration.OriginalVersionForTest();
var capabilityVersion = NativeClientDddAcceleration.CapabilityVersionForTest();
Equal(12, originalVersion.Length, "original version literal length");
Equal(12, capabilityVersion.Length, "capability version literal length");
BytesEqual("2005.02.001\0"u8.ToArray(), originalVersion, "original version literal");
BytesEqual("2005.02.A09\0"u8.ToArray(), capabilityVersion, "capability version literal");

var publicProfile = NativeClientDddAcceleration.PublicProfileForTest();
Equal(NativeClientDddAcceleration.PublicProfileId, publicProfile.Id, "public profile id");
Equal(CommunityClientBootstrap.ExpectedSize, publicProfile.ExpectedSize, "public profile size");
Equal(
    CommunityClientBootstrap.ExpectedSha256,
    publicProfile.ExpectedSha256,
    "public profile SHA-256");
Equal(NativeClientDddAcceleration.UseTimeRva, publicProfile.UseTimeRva, "public UseTime RVA");
Equal(
    NativeClientDddAcceleration.DownloadStatusRva,
    publicProfile.DownloadStatusRva,
    "public download-status RVA");
Equal(
    NativeClientDddAcceleration.VersionLiteralRva,
    publicProfile.VersionLiteralRva,
    "public version-literal RVA");
BytesEqual(useTimeSignature, publicProfile.ExpectedUseTimeSignature, "public profile UseTime signature");
Equal(
    publicProfile.Id,
    NativeClientDddAcceleration.IdentifySupportedProfileForTest(
        publicProfile.ExpectedSize,
        publicProfile.ExpectedSha256.ToLowerInvariant()).Id,
    "case-insensitive public profile selection");

var adminProfile = NativeClientDddAcceleration.AdminProfileForTest();
Equal(NativeClientDddAcceleration.AdminProfileId, adminProfile.Id, "admin profile id");
Equal(NativeClientDddAcceleration.AdminExpectedSize, adminProfile.ExpectedSize, "admin profile size");
Equal(
    NativeClientDddAcceleration.AdminExpectedSha256,
    adminProfile.ExpectedSha256,
    "admin profile SHA-256");
Equal(
    NativeClientDddAcceleration.AdminUseTimeRva,
    adminProfile.UseTimeRva,
    "admin UseTime RVA");
Equal(
    NativeClientDddAcceleration.AdminDownloadStatusRva,
    adminProfile.DownloadStatusRva,
    "admin download-status RVA");
Equal(
    NativeClientDddAcceleration.AdminVersionLiteralRva,
    adminProfile.VersionLiteralRva,
    "admin version-literal RVA");
BytesEqual(
    Convert.FromHexString(
        "558BEC83E4F883EC08535556578BF98B470485C0BE787E93007537A1D0C27A00"),
    adminProfile.ExpectedUseTimeSignature,
    "admin profile UseTime signature");
BytesEqual(
    downloadStatusSignature,
    adminProfile.ExpectedDownloadStatusSignature,
    "admin profile download-status signature");
BytesEqual(originalVersion, adminProfile.OriginalVersionLiteral, "admin original version literal");
Equal(
    adminProfile.Id,
    NativeClientDddAcceleration.IdentifySupportedProfileForTest(
        adminProfile.ExpectedSize,
        adminProfile.ExpectedSha256).Id,
    "exact admin profile selection");
var unknownProfileRejected = false;
try
{
    _ = NativeClientDddAcceleration.IdentifySupportedProfileForTest(
        adminProfile.ExpectedSize,
        new string('0', 64));
}
catch (InvalidDataException)
{
    unknownProfileRejected = true;
}

True(unknownProfileRejected, "unknown same-size admin image rejection");

var processNameMatch = new RunningProgramIdentity(
    101,
    "cheatengine-x86_64",
    "Cheat Engine 7.5",
    "cheatengine-x86_64.exe",
    null,
    null,
    null,
    null);
True(ClientAntiTamper.IsKnownCheatEngineIdentity(processNameMatch),
    "official Cheat Engine process name");

var renamedMetadataMatch = new RunningProgramIdentity(
    102,
    "calculator",
    "Cheat Engine 7.5",
    "calculator.exe",
    "cheatengine-x86_64.exe",
    "Cheat Engine",
    "Cheat Engine",
    "Cheat Engine 7.5");
True(ClientAntiTamper.IsKnownCheatEngineIdentity(renamedMetadataMatch),
    "renamed Cheat Engine version metadata");

var safeBrowser = new RunningProgramIdentity(
    103,
    "chrome",
    "Cheat Engine download - Google Chrome",
    "chrome.exe",
    "chrome.exe",
    "Google LLC",
    "Google Chrome",
    "Google Chrome");
True(!ClientAntiTamper.IsKnownCheatEngineIdentity(safeBrowser),
    "ordinary browser identity");

var safeSimilarName = new RunningProgramIdentity(
    104,
    "cheat-sheet",
    "Cheat Engine Notes - Notepad",
    "cheat-sheet.exe",
    "cheat-sheet.exe",
    "Example Company",
    "Study helper",
    "Study helper");
True(!ClientAntiTamper.IsKnownCheatEngineIdentity(safeSimilarName),
    "non-Cheat-Engine similar process name");

var renamedWindowMatch = new RunningProgramIdentity(
    105,
    "calculator",
    "Cheat Engine 7.5",
    "calculator.exe",
    "calculator.exe",
    null,
    null,
    null);
True(ClientAntiTamper.IsKnownCheatEngineIdentity(renamedWindowMatch),
    "renamed Cheat Engine exact top-level title");
Equal(
    processNameMatch,
    ClientAntiTamper.FindKnownMemoryEditor([safeBrowser, processNameMatch])!,
    "first prohibited running program");

// Test-only accessors must not expose mutable runtime guards or rollback bytes.
useTimeSignature[0] = 0;
downloadStatusSignature[0] = 0;
originalUseTime[0] = 0;
originalDownloadStatus[0] = 0;
originalSpellRegion[0] = 0;
originalVersion[0] = 0;
capabilityVersion[0] = 0;
Equal((byte)0x55, NativeClientDddAcceleration.SupportedSignatureForTest()[0],
    "UseTime signature clone");
Equal((byte)0x8B, NativeClientDddAcceleration.DownloadStatusSignatureForTest()[0],
    "download-status signature clone");
Equal((byte)0x55, NativeClientDddAcceleration.OriginalUseTimePrologueForTest()[0],
    "UseTime rollback clone");
Equal((byte)0x8B, NativeClientDddAcceleration.OriginalDownloadStatusTailForTest()[0],
    "download-status rollback clone");
Equal((byte)0x83, NativeClientDddAcceleration.OriginalSpellRegionPrologueForTest()[0],
    "SpellRegion rollback clone");
Equal((byte)0x83, NativeClientDddAcceleration.OriginalAllegPanelPrologueForTest()[0],
    "AllegPanel rollback clone");
Equal((byte)0x56, NativeClientDddAcceleration.OriginalSetTextPrologueForTest()[0],
    "SetText rollback clone");
Equal((byte)'2', NativeClientDddAcceleration.OriginalVersionForTest()[0],
    "original-version clone");
Equal((byte)'2', NativeClientDddAcceleration.CapabilityVersionForTest()[0],
    "capability-version clone");

var remoteBase = new IntPtr(0x0100_0000);
var remoteCode = NativeClientDddAcceleration.BuildRemoteCode(remoteBase);
Equal(NativeClientDddAcceleration.RemoteCodeSize, remoteCode.Length, "remote code size");
var wrapperLength =
    NativeClientDddAcceleration.UseTimeWrapperLengthForTest(remoteBase);
Equal(115, wrapperLength, "UseTime wrapper length");
True(wrapperLength <= NativeClientDddAcceleration.UseTimeTrampolineOffset,
    "UseTime wrapper/trampoline non-overlap");

var expectedUseTimeWrapper = Convert.FromHexString(
    "5356578BF1BB080000008B460485C00F84330000008338000F842A0000008B7E0885FF" +
    "0F840A000000837F60200F833C0000008B7E0C85FF0F840A000000837F60200F832700" +
    "00008BCEE8310000004B0F84190000008B460485C00F840E0000008338000F84050000" +
    "00E99BFFFFFF5F5E5BC3");
BytesEqual(expectedUseTimeWrapper, remoteCode[..wrapperLength],
    "high-water queue-drain wrapper");

// Every internal branch must land on the intended instruction boundary.
var expectedBranches = new (int Instruction, int Target)[]
{
    (15, 72),
    (24, 72),
    (35, 51),
    (45, 111),
    (56, 72),
    (66, 111),
    (80, 111),
    (91, 111),
    (100, 111),
    (106, 10),
};
foreach (var (instruction, target) in expectedBranches)
{
    Equal(target, DecodeInternalNearBranch(remoteCode, instruction),
        $"wrapper branch at {instruction}");
}

var originalCall =
    NativeClientDddAcceleration.UseTimeOriginalCallOffsetForTest(remoteBase);
Equal(74, originalCall, "wrapper original-call offset");
Equal((byte)0xE8, remoteCode[originalCall], "wrapper CALL opcode");
Equal(
    IntPtr.Add(remoteBase, NativeClientDddAcceleration.UseTimeTrampolineOffset),
    NativeClientDddAcceleration.DecodeRelativeTarget(remoteCode, originalCall, remoteBase),
    "wrapper call target");

var trampoline = NativeClientDddAcceleration.UseTimeTrampolineOffset;
BytesEqual(
    Convert.FromHexString("558BEC83E4F8"),
    remoteCode[trampoline..(trampoline + 6)],
    "trampoline prologue");
Equal((byte)0xE9, remoteCode[trampoline + 6], "trampoline jump opcode");
Equal(
    new IntPtr(unchecked((int)(NativeClientDddAcceleration.PreferredImageBase +
                               NativeClientDddAcceleration.UseTimeRva + 6))),
    NativeClientDddAcceleration.DecodeRelativeTarget(remoteCode, trampoline + 6, remoteBase),
    "trampoline continuation");

var adminRemoteCode = NativeClientDddAcceleration.BuildRemoteCode(remoteBase, adminProfile);
Equal(
    new IntPtr(unchecked((int)(adminProfile.PreferredImageBase + adminProfile.UseTimeRva + 6))),
    NativeClientDddAcceleration.DecodeRelativeTarget(
        adminRemoteCode,
        trampoline + 6,
        remoteBase),
    "admin trampoline continuation");

var statusWrapper = NativeClientDddAcceleration.DownloadStatusDrainWrapperForTest();
BytesEqual(
    Convert.FromHexString(
        "33C08379180375278B510485D27420833A00751B8B510885D27406837A6000750E" +
        "8B510C85D27406837A6000750140C21000"),
    statusWrapper,
    "download-status drain wrapper source");
BytesEqual(
    statusWrapper,
    remoteCode[
        NativeClientDddAcceleration.DownloadStatusWrapperOffset..
        (NativeClientDddAcceleration.DownloadStatusWrapperOffset + statusWrapper.Length)],
    "download-status drain wrapper placement");
statusWrapper[0] = 0;
Equal((byte)0x33,
    NativeClientDddAcceleration.DownloadStatusDrainWrapperForTest()[0],
    "download-status wrapper clone");

var spellSignature = NativeClientDddAcceleration.SpellRegionSignatureForTest();
Equal(32, spellSignature.Length, "SpellRegion signature length");
BytesEqual(
    Convert.FromHexString(
        "83EC18568BF18B8EC4000000E8FFA4F6FF8B86D400000085C00F84BD000000B9"),
    spellSignature,
    "public SpellRegion::Update signature");
BytesEqual(
    Convert.FromHexString("83EC18568BF1"),
    NativeClientDddAcceleration.OriginalSpellRegionPrologueForTest(),
    "SpellRegion rollback bytes");
Equal(
    NativeClientDddAcceleration.SpellRegionUpdateRva,
    publicProfile.SpellRegion.UpdateRva,
    "public SpellRegion RVA");
Equal(
    NativeClientDddAcceleration.SpellRegionUpdateFileOffset,
    publicProfile.SpellRegion.UpdateFileOffset,
    "public SpellRegion file offset");
Equal(
    NativeClientDddAcceleration.SpellRegionUpdateSize,
    publicProfile.SpellRegion.UpdateSize,
    "public SpellRegion size");
Equal(NativeClientDddAcceleration.SpellDurationRva, publicProfile.SpellRegion.SpellDurationRva,
    "public SpellDuration RVA");
Equal(NativeClientDddAcceleration.TextRegionGetTextRva, publicProfile.SpellRegion.GetTextRva,
    "public GetText RVA");
Equal(NativeClientDddAcceleration.TextRegionSetTextRva, publicProfile.SpellRegion.SetTextRva,
    "public SetText RVA");
BytesEqual(spellSignature, publicProfile.SpellRegion.ExpectedSignature,
    "public profile SpellRegion signature");
spellSignature[0] = 0;
Equal((byte)0x83, NativeClientDddAcceleration.SpellRegionSignatureForTest()[0],
    "SpellRegion signature clone");
BytesEqual(
    Convert.FromHexString(
        "83EC18568BF18B8EC4000000E8AF59F6FF8B86D400000085C00F84BD000000B9"),
    adminProfile.SpellRegion.ExpectedSignature,
    "admin profile SpellRegion signature");
Equal(
    NativeClientDddAcceleration.AdminSpellRegionUpdateRva,
    adminProfile.SpellRegion.UpdateRva,
    "admin SpellRegion RVA");
Equal(
    NativeClientDddAcceleration.SpellRegionUpdateSize,
    adminProfile.SpellRegion.UpdateSize,
    "admin SpellRegion size");

var spellWrapper = NativeClientDddAcceleration.SpellRegionWrapperForTest(remoteBase);
var spellWrapperLength =
    NativeClientDddAcceleration.SpellRegionWrapperLengthForTest(remoteBase);
Equal(spellWrapperLength, spellWrapper.Length, "SpellRegion wrapper length accessor");
True(
    NativeClientDddAcceleration.SpellRegionWrapperOffset + spellWrapperLength <=
    NativeClientDddAcceleration.SetTextWrapperOffset,
    "SpellRegion wrapper/SetText containment");
BytesEqual(
    spellWrapper,
    remoteCode[
        NativeClientDddAcceleration.SpellRegionWrapperOffset..
        (NativeClientDddAcceleration.SpellRegionWrapperOffset + spellWrapperLength)],
    "SpellRegion wrapper placement");
spellWrapper[0] = 0;
Equal((byte)0x55,
    NativeClientDddAcceleration.SpellRegionWrapperForTest(remoteBase)[0],
    "SpellRegion wrapper clone");

var spellWrapperAddress =
    IntPtr.Add(remoteBase, NativeClientDddAcceleration.SpellRegionWrapperOffset);
var placedSpellWrapper = remoteCode[
    NativeClientDddAcceleration.SpellRegionWrapperOffset..
    (NativeClientDddAcceleration.SpellRegionWrapperOffset + spellWrapperLength)];
var spellRelCalls = new List<IntPtr>();
var spellAbsCalls = new List<IntPtr>();
for (var i = 0; i + 5 < placedSpellWrapper.Length; i++)
{
    if (placedSpellWrapper[i] == 0xE8)
    {
        spellRelCalls.Add(
            NativeClientDddAcceleration.DecodeRelativeTarget(
                placedSpellWrapper,
                i,
                spellWrapperAddress));
        i += 4;
        continue;
    }

    if (placedSpellWrapper[i] == 0xFF && placedSpellWrapper[i + 1] == 0x15)
    {
        spellAbsCalls.Add(new IntPtr(unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            placedSpellWrapper.AsSpan(i + 2, sizeof(int))))));
        i += 5;
    }
}

Equal(5, spellRelCalls.Count, "SpellRegion wrapper relative-call count");
True(
    spellRelCalls.Contains(new IntPtr(unchecked((int)(
        NativeClientDddAcceleration.PreferredImageBase +
        NativeClientDddAcceleration.SpellDurationRva)))),
    "SpellRegion wrapper calls SpellDuration");
True(
    spellRelCalls.Contains(new IntPtr(unchecked((int)(
        NativeClientDddAcceleration.PreferredImageBase +
        NativeClientDddAcceleration.Ftol2Rva)))),
    "SpellRegion wrapper calls _ftol2");
Equal(
    2,
    spellRelCalls.Count(target => target == new IntPtr(unchecked((int)(
        NativeClientDddAcceleration.PreferredImageBase +
        NativeClientDddAcceleration.Ftol2Rva)))),
    "SpellRegion wrapper _ftol2 pair");
True(
    spellRelCalls.Contains(new IntPtr(unchecked((int)(
        NativeClientDddAcceleration.PreferredImageBase +
        NativeClientDddAcceleration.TextRegionGetTextRva)))),
    "SpellRegion wrapper calls GetText");
True(
    spellRelCalls.Contains(new IntPtr(unchecked((int)(
        NativeClientDddAcceleration.PreferredImageBase +
        NativeClientDddAcceleration.TextRegionSetTextRva)))),
    "SpellRegion wrapper calls SetText");
Equal(1, spellAbsCalls.Count, "SpellRegion wrapper sprintf IAT count");
Equal(
    new IntPtr(unchecked((int)(
        NativeClientDddAcceleration.PreferredImageBase +
        NativeClientDddAcceleration.SprintfIatRva))),
    spellAbsCalls[0],
    "SpellRegion wrapper sprintf IAT");

var adminSpellWrapper = NativeClientDddAcceleration.BuildRemoteCode(remoteBase, adminProfile);
var adminPlacedSpell = adminSpellWrapper[
    NativeClientDddAcceleration.SpellRegionWrapperOffset..
    (NativeClientDddAcceleration.SpellRegionWrapperOffset + spellWrapperLength)];
var adminSpellRelCalls = new List<IntPtr>();
for (var i = 0; i + 5 < adminPlacedSpell.Length; i++)
{
    if (adminPlacedSpell[i] == 0xE8)
    {
        adminSpellRelCalls.Add(
            NativeClientDddAcceleration.DecodeRelativeTarget(
                adminPlacedSpell,
                i,
                spellWrapperAddress));
        i += 4;
    }
}

True(
    adminSpellRelCalls.Contains(new IntPtr(unchecked((int)(
        adminProfile.PreferredImageBase + adminProfile.SpellRegion.GetTextRva)))),
    "admin SpellRegion wrapper calls GetText");
True(
    adminSpellRelCalls.Contains(new IntPtr(unchecked((int)(
        adminProfile.PreferredImageBase + adminProfile.SpellRegion.SetTextRva)))),
    "admin SpellRegion wrapper calls SetText");
True(
    !spellRelCalls.Contains(new IntPtr(unchecked((int)(
        adminProfile.PreferredImageBase + adminProfile.SpellRegion.GetTextRva)))),
    "public SpellRegion wrapper does not use admin GetText");

var trampolineEnd = trampoline + 6 + 5;
True(trampolineEnd <= NativeClientDddAcceleration.DownloadStatusWrapperOffset,
    "trampoline/status-wrapper non-overlap");
True(
    NativeClientDddAcceleration.DownloadStatusWrapperOffset +
        NativeClientDddAcceleration.DownloadStatusDrainWrapperForTest().Length <=
    NativeClientDddAcceleration.SpellRegionWrapperOffset,
    "status-wrapper/SpellRegion non-overlap");
True(
    NativeClientDddAcceleration.SpellRegionWrapperOffset + spellWrapperLength <=
    NativeClientDddAcceleration.SetTextWrapperOffset,
    "SpellRegion wrapper/SetText non-overlap");
True(remoteCode[wrapperLength..trampoline].All(value => value == 0xCC),
    "wrapper/trampoline padding");
True(remoteCode[trampolineEnd..NativeClientDddAcceleration.DownloadStatusWrapperOffset]
        .All(value => value == 0xCC),
    "trampoline/status-wrapper padding");
var statusEnd = NativeClientDddAcceleration.DownloadStatusWrapperOffset +
    NativeClientDddAcceleration.DownloadStatusDrainWrapperForTest().Length;
True(remoteCode[statusEnd..NativeClientDddAcceleration.SpellRegionWrapperOffset]
        .All(value => value == 0xCC),
    "status-wrapper/SpellRegion padding");
True(remoteCode[
        (NativeClientDddAcceleration.SpellRegionWrapperOffset + spellWrapperLength)..
        NativeClientDddAcceleration.SetTextWrapperOffset]
        .All(value => value == 0xCC),
    "SpellRegion/SetText padding");

var useTimeAddress = new IntPtr(unchecked((int)(
    NativeClientDddAcceleration.PreferredImageBase + NativeClientDddAcceleration.UseTimeRva)));
var entryPatch = NativeClientDddAcceleration.BuildEntryPatch(useTimeAddress, remoteBase);
BytesEqual(Convert.FromHexString("E9FB25BF0090"), entryPatch, "UseTime entry detour");
Equal(remoteBase,
    NativeClientDddAcceleration.DecodeRelativeTarget(entryPatch, 0, useTimeAddress),
    "UseTime entry-detour target");

var downloadStatusTailAddress = new IntPtr(unchecked((int)(
    NativeClientDddAcceleration.PreferredImageBase +
    NativeClientDddAcceleration.DownloadStatusTailRva)));
var statusWrapperAddress =
    IntPtr.Add(remoteBase, NativeClientDddAcceleration.DownloadStatusWrapperOffset);
var statusPatch = NativeClientDddAcceleration.BuildDownloadStatusTailPatch(
    downloadStatusTailAddress,
    statusWrapperAddress);
BytesEqual(
    Convert.FromHexString("E9C730BF00909090909090909090"),
    statusPatch,
    "download-status tail detour");
Equal(statusWrapperAddress,
    NativeClientDddAcceleration.DecodeRelativeTarget(
        statusPatch,
        0,
        downloadStatusTailAddress),
    "download-status tail-detour target");

var spellRegionAddress = new IntPtr(unchecked((int)(
    NativeClientDddAcceleration.PreferredImageBase +
    NativeClientDddAcceleration.SpellRegionUpdateRva)));
var spellRegionPatch = NativeClientDddAcceleration.BuildEntryPatch(
    spellRegionAddress,
    spellWrapperAddress);
Equal(6, spellRegionPatch.Length, "SpellRegion entry detour length");
Equal((byte)0xE9, spellRegionPatch[0], "SpellRegion entry JMP");
Equal((byte)0x90, spellRegionPatch[5], "SpellRegion entry NOP");
Equal(spellWrapperAddress,
    NativeClientDddAcceleration.DecodeRelativeTarget(
        spellRegionPatch,
        0,
        spellRegionAddress),
    "SpellRegion entry-detour target");

// Check the largest normal x86 user-mode allocation neighborhood too; every
// absolute rel32 edge must still decode to the intended target.
var highRemoteBase = new IntPtr(0x7000_0000);
var highRemoteCode = NativeClientDddAcceleration.BuildRemoteCode(highRemoteBase);
var highOriginalCall =
    NativeClientDddAcceleration.UseTimeOriginalCallOffsetForTest(highRemoteBase);
Equal(
    IntPtr.Add(highRemoteBase, NativeClientDddAcceleration.UseTimeTrampolineOffset),
    NativeClientDddAcceleration.DecodeRelativeTarget(
        highRemoteCode,
        highOriginalCall,
        highRemoteBase),
    "high-address wrapper call");
Equal(
    new IntPtr(unchecked((int)(NativeClientDddAcceleration.PreferredImageBase +
                               NativeClientDddAcceleration.UseTimeRva + 6))),
    NativeClientDddAcceleration.DecodeRelativeTarget(
        highRemoteCode,
        NativeClientDddAcceleration.UseTimeTrampolineOffset + 6,
        highRemoteBase),
    "high-address trampoline continuation");
var highStatusWrapper =
    IntPtr.Add(highRemoteBase, NativeClientDddAcceleration.DownloadStatusWrapperOffset);
var highStatusPatch = NativeClientDddAcceleration.BuildDownloadStatusTailPatch(
    downloadStatusTailAddress,
    highStatusWrapper);
Equal(highStatusWrapper,
    NativeClientDddAcceleration.DecodeRelativeTarget(
        highStatusPatch,
        0,
        downloadStatusTailAddress),
    "high-address download-status detour");
var highSpellWrapper =
    IntPtr.Add(highRemoteBase, NativeClientDddAcceleration.SpellRegionWrapperOffset);
var highSpellPatch = NativeClientDddAcceleration.BuildEntryPatch(
    spellRegionAddress,
    highSpellWrapper);
Equal(highSpellWrapper,
    NativeClientDddAcceleration.DecodeRelativeTarget(
        highSpellPatch,
        0,
        spellRegionAddress),
    "high-address SpellRegion detour");
var highSpellPlaced = highRemoteCode[
    NativeClientDddAcceleration.SpellRegionWrapperOffset..
    (NativeClientDddAcceleration.SpellRegionWrapperOffset + spellWrapperLength)];
True(
    Enumerable.Range(0, highSpellPlaced.Length - 4).Any(index =>
        highSpellPlaced[index] == 0xE8 &&
        NativeClientDddAcceleration.DecodeRelativeTarget(
            highSpellPlaced,
            index,
            highSpellWrapper) ==
        new IntPtr(unchecked((int)(
        NativeClientDddAcceleration.PreferredImageBase +
        NativeClientDddAcceleration.TextRegionGetTextRva)))),
    "high-address SpellRegion GetText call");

var setTextSignature = NativeClientDddAcceleration.SetTextSignatureForTest();
Equal(32, setTextSignature.Length, "SetText signature length");
BytesEqual(
    Convert.FromHexString(
        "568BF1E8F8E1FFFF8B4C24148B5424108B06518B4C2410528B54241051528BCE"),
    setTextSignature,
    "public TextRegion::SetText signature");
BytesEqual(
    Convert.FromHexString("568BF1E8F8E1FFFF"),
    NativeClientDddAcceleration.OriginalSetTextPrologueForTest(),
    "public SetText stolen prologue");
setTextSignature[0] = 0;
Equal((byte)0x56, NativeClientDddAcceleration.SetTextSignatureForTest()[0],
    "SetText signature clone");
BytesEqual(
    Convert.FromHexString(
        "568BF1E8D8E1FFFF8B4C24148B5424108B06518B4C2410528B54241051528BCE"),
    adminProfile.XpLabel.ExpectedSetTextSignature,
    "admin TextRegion::SetText signature");
Equal(
    NativeClientDddAcceleration.TextRegionSetTextRva,
    publicProfile.XpLabel.SetTextRva,
    "public SetText RVA");
Equal(
    NativeClientDddAcceleration.AdminTextRegionSetTextRva,
    adminProfile.XpLabel.SetTextRva,
    "admin SetText RVA");
Equal(
    NativeClientDddAcceleration.AllegPanelSetXpChangeRva,
    publicProfile.XpLabel.AllegPanelSetXpChangeRva,
    "public AllegPanel::SetXPChange RVA");
Equal(
    NativeClientDddAcceleration.AdminAllegPanelSetXpChangeRva,
    adminProfile.XpLabel.AllegPanelSetXpChangeRva,
    "admin AllegPanel::SetXPChange RVA");
BytesEqual(
    Convert.FromHexString(
        "83EC0C5355568BF18B0657FF50348B2D1CD368008B4C24248B5424208BF833C0"),
    publicProfile.XpLabel.ExpectedAllegPanelSignature,
    "public AllegPanel::SetXPChange signature");
BytesEqual(
    Convert.FromHexString(
        "83EC0C5355568BF18B0657FF50348B2DAC927D008B4C24248B5424208BF833C0"),
    adminProfile.XpLabel.ExpectedAllegPanelSignature,
    "admin AllegPanel::SetXPChange signature");

var setTextWrapper = NativeClientDddAcceleration.SetTextWrapperForTest(remoteBase);
True(
    NativeClientDddAcceleration.SetTextWrapperOffset + setTextWrapper.Length <=
    NativeClientDddAcceleration.SetTextTrampolineOffset,
    "SetText wrapper/trampoline non-overlap");
BytesEqual(
    setTextWrapper,
    remoteCode[
        NativeClientDddAcceleration.SetTextWrapperOffset..
        (NativeClientDddAcceleration.SetTextWrapperOffset + setTextWrapper.Length)],
    "SetText wrapper placement");
setTextWrapper[0] = 0;
Equal((byte)0x56,
    NativeClientDddAcceleration.SetTextWrapperForTest(remoteBase)[0],
    "SetText wrapper clone");

var setTextWrapperAddress =
    IntPtr.Add(remoteBase, NativeClientDddAcceleration.SetTextWrapperOffset);
var setTextTrampolineAddress =
    IntPtr.Add(remoteBase, NativeClientDddAcceleration.SetTextTrampolineOffset);
var placedSetTextWrapper = remoteCode[
    NativeClientDddAcceleration.SetTextWrapperOffset..
    (NativeClientDddAcceleration.SetTextWrapperOffset + setTextWrapper.Length)];
var setTextCallsGetText = false;
var setTextJumpsTrampoline = false;
for (var i = 0; i + 5 <= placedSetTextWrapper.Length; i++)
{
    if (placedSetTextWrapper[i] == 0xE8)
    {
        var target = NativeClientDddAcceleration.DecodeRelativeTarget(
            placedSetTextWrapper,
            i,
            setTextWrapperAddress);
        if (target == new IntPtr(unchecked((int)(
            NativeClientDddAcceleration.PreferredImageBase +
            NativeClientDddAcceleration.TextRegionGetTextRva))))
        {
            setTextCallsGetText = true;
        }

        i += 4;
        continue;
    }

    if (placedSetTextWrapper[i] == 0xE9)
    {
        var target = NativeClientDddAcceleration.DecodeRelativeTarget(
            placedSetTextWrapper,
            i,
            setTextWrapperAddress);
        if (target == setTextTrampolineAddress)
        {
            setTextJumpsTrampoline = true;
        }

        i += 4;
    }
}

True(setTextCallsGetText, "SetText wrapper calls GetText");
True(setTextJumpsTrampoline, "SetText wrapper falls through to trampoline");
True(
    placedSetTextWrapper.AsSpan().IndexOf(
        new byte[] { 0x5B, 0x8B, 0xCE, 0x5E, 0xE9 }) >= 0,
    "SetText fallthrough restores ecx before the trampoline");

var setTextAddress = new IntPtr(unchecked((int)(
    NativeClientDddAcceleration.PreferredImageBase +
    NativeClientDddAcceleration.TextRegionSetTextRva)));
var setTextPatch = NativeClientDddAcceleration.BuildStolenDetourPatch(
    setTextAddress,
    setTextWrapperAddress);
Equal(8, setTextPatch.Length, "SetText entry detour length");
Equal((byte)0xE9, setTextPatch[0], "SetText entry JMP");
Equal((byte)0x90, setTextPatch[5], "SetText entry NOP");
Equal((byte)0x90, setTextPatch[7], "SetText entry trailing NOP");
Equal(setTextWrapperAddress,
    NativeClientDddAcceleration.DecodeRelativeTarget(setTextPatch, 0, setTextAddress),
    "SetText entry-detour target");

var placedSetTextTrampoline = remoteCode[
    NativeClientDddAcceleration.SetTextTrampolineOffset..
    (NativeClientDddAcceleration.SetTextTrampolineOffset + 13)];
BytesEqual(
    Convert.FromHexString("568BF1"),
    placedSetTextTrampoline[..3],
    "SetText trampoline stolen prologue");
Equal((byte)0xE8, placedSetTextTrampoline[3], "SetText trampoline ClearAllText CALL");
Equal(
    NativeClientDddAcceleration.SetTextClearAllTextForTest(),
    NativeClientDddAcceleration.DecodeRelativeTarget(
        placedSetTextTrampoline,
        3,
        setTextTrampolineAddress),
    "SetText trampoline calls ClearAllText");
Equal((byte)0xE9, placedSetTextTrampoline[8], "SetText trampoline continuation JMP");
Equal(
    IntPtr.Add(setTextAddress, NativeClientDddAcceleration.StolenDetourLength),
    NativeClientDddAcceleration.DecodeRelativeTarget(
        placedSetTextTrampoline,
        8,
        setTextTrampolineAddress),
    "SetText trampoline continues at SetText+8");

var allegPanelWrapper = NativeClientDddAcceleration.AllegPanelWrapperForTest(remoteBase);
True(
    NativeClientDddAcceleration.AllegPanelWrapperOffset + allegPanelWrapper.Length <=
    NativeClientDddAcceleration.AllegPanelTrampolineOffset,
    "AllegPanel wrapper/trampoline non-overlap");
BytesEqual(
    allegPanelWrapper,
    remoteCode[
        NativeClientDddAcceleration.AllegPanelWrapperOffset..
        (NativeClientDddAcceleration.AllegPanelWrapperOffset + allegPanelWrapper.Length)],
    "AllegPanel wrapper placement");

var allegPanelAddress = new IntPtr(unchecked((int)(
    NativeClientDddAcceleration.PreferredImageBase +
    NativeClientDddAcceleration.AllegPanelSetXpChangeRva)));
var allegPanelWrapperAddress =
    IntPtr.Add(remoteBase, NativeClientDddAcceleration.AllegPanelWrapperOffset);
var allegPanelTrampolineAddress =
    IntPtr.Add(remoteBase, NativeClientDddAcceleration.AllegPanelTrampolineOffset);
var allegPanelPatch = NativeClientDddAcceleration.BuildStolenDetourPatch(
    allegPanelAddress,
    allegPanelWrapperAddress);
Equal(allegPanelWrapperAddress,
    NativeClientDddAcceleration.DecodeRelativeTarget(
        allegPanelPatch,
        0,
        allegPanelAddress),
    "AllegPanel entry-detour target");
Equal(
    IntPtr.Add(allegPanelAddress, NativeClientDddAcceleration.StolenDetourLength),
    NativeClientDddAcceleration.DecodeRelativeTarget(
        remoteCode,
        NativeClientDddAcceleration.AllegPanelTrampolineOffset +
            NativeClientDddAcceleration.OriginalAllegPanelPrologueForTest().Length,
        remoteBase),
    "AllegPanel trampoline continuation");

var highSetTextWrapper =
    IntPtr.Add(highRemoteBase, NativeClientDddAcceleration.SetTextWrapperOffset);
var highSetTextPatch = NativeClientDddAcceleration.BuildStolenDetourPatch(
    setTextAddress,
    highSetTextWrapper);
Equal(highSetTextWrapper,
    NativeClientDddAcceleration.DecodeRelativeTarget(
        highSetTextPatch,
        0,
        setTextAddress),
    "high-address SetText detour");
var highSetTextPlaced = highRemoteCode[
    NativeClientDddAcceleration.SetTextWrapperOffset..
    (NativeClientDddAcceleration.SetTextWrapperOffset + setTextWrapper.Length)];
True(
    Enumerable.Range(0, highSetTextPlaced.Length - 4).Any(index =>
        highSetTextPlaced[index] == 0xE8 &&
        NativeClientDddAcceleration.DecodeRelativeTarget(
            highSetTextPlaced,
            index,
            highSetTextWrapper) ==
        new IntPtr(unchecked((int)(
            NativeClientDddAcceleration.PreferredImageBase +
            NativeClientDddAcceleration.TextRegionGetTextRva)))),
    "high-address SetText GetText call");

var emptyStartupOptions = LauncherStartupOptions.Parse([]);
True(emptyStartupOptions.GameInstallDirectory is null, "default UI install selection");
var startupTestDirectory = Path.Combine(
    Path.GetTempPath(),
    $"aetherium-startup-options-{Guid.NewGuid():N}");
var startupLinkDirectory = startupTestDirectory + "-link";
Directory.CreateDirectory(startupTestDirectory);
try
{
    foreach (var fileName in new[] { "client.exe", "portal.dat", "cell.dat" })
    {
        File.WriteAllBytes(Path.Combine(startupTestDirectory, fileName), [0x01]);
    }

    var resolvedClientPath = string.Empty;
    var explicitStartupOptions = LauncherStartupOptions.Parse(
        ["--GAME-INSTALL", startupTestDirectory],
        clientPath =>
        {
            resolvedClientPath = clientPath;
            return publicProfile;
        });
    Equal(
        Path.GetFullPath(startupTestDirectory),
        explicitStartupOptions.GameInstallDirectory!,
        "explicit UI install selection");
    Equal(
        Path.Combine(Path.GetFullPath(startupTestDirectory), "client.exe"),
        resolvedClientPath,
        "explicit UI exact-profile validator path");
    True(ReferenceEquals(publicProfile, explicitStartupOptions.ClientProfile),
        "explicit UI resolved profile retention");

    var unsupportedClientRejected = false;
    try
    {
        _ = LauncherStartupOptions.Parse(["--game-install", startupTestDirectory]);
    }
    catch (InvalidDataException)
    {
        unsupportedClientRejected = true;
    }

    True(unsupportedClientRejected, "unsupported explicit UI client rejection");

    File.WriteAllBytes(Path.Combine(startupTestDirectory, "cell.dat"), []);
    var emptyDatRejected = false;
    try
    {
        _ = LauncherStartupOptions.Parse(
            ["--game-install", startupTestDirectory],
            _ => publicProfile);
    }
    catch (InvalidDataException)
    {
        emptyDatRejected = true;
    }

    True(emptyDatRejected, "empty explicit UI DAT rejection");
    File.WriteAllBytes(Path.Combine(startupTestDirectory, "cell.dat"), [0x01]);

    var legacyWorkspace = Path.Combine(startupTestDirectory, "multiclient");
    Directory.CreateDirectory(legacyWorkspace);
    var legacyMarker = Path.Combine(legacyWorkspace, "must-remain.bin");
    File.WriteAllBytes(legacyMarker, [0xA5]);
    var failedStartRejectedBeforeCleanup = false;
    try
    {
        _ = ClientLauncher.Start(
            new LaunchConfig
            {
                InstallPath = startupTestDirectory,
                TicketKey = "profile-validation-test",
            },
            prepareGraphics: false);
    }
    catch (InvalidDataException)
    {
        failedStartRejectedBeforeCleanup = true;
    }

    True(failedStartRejectedBeforeCleanup, "unsupported launch rejected before side effects");
    True(File.Exists(legacyMarker), "unsupported launch preserved legacy multiclient marker");
    True(
        !ClientLauncher.ShouldRemoveLegacyMulticlient(
            new LaunchConfig { PreserveLegacyMulticlient = true }),
        "startup override preserves legacy multiclient");
    True(
        ClientLauncher.ShouldRemoveLegacyMulticlient(new LaunchConfig()),
        "ordinary launch retains legacy multiclient migration");

    foreach (var disallowedPath in new[]
             {
                 @"\\server\share\Aetherium",
                 @"\\?\C:\Aetherium",
                 @"\??\C:\Aetherium",
             })
    {
        var disallowedRootRejected = false;
        try
        {
            _ = LauncherStartupOptions.Parse(
                ["--game-install", disallowedPath],
                _ => publicProfile);
        }
        catch (ArgumentException)
        {
            disallowedRootRejected = true;
        }

        True(disallowedRootRejected, $"UNC/device UI path rejection: {disallowedPath}");
    }

    foreach (var label in new[] { "install root", "required DAT" })
    {
        var reparseAttributesRejected = false;
        try
        {
            LauncherStartupOptions.EnsureNotReparsePointForTest(
                FileAttributes.ReparsePoint | FileAttributes.Archive,
                label);
        }
        catch (InvalidDataException)
        {
            reparseAttributesRejected = true;
        }

        True(reparseAttributesRejected, $"deterministic {label} reparse rejection");
    }

    var cellPath = Path.Combine(startupTestDirectory, "cell.dat");
    var cellTargetPath = Path.Combine(startupTestDirectory, "cell-target.dat");
    try
    {
        File.WriteAllBytes(cellTargetPath, [0x01]);
        File.Delete(cellPath);
        File.CreateSymbolicLink(cellPath, cellTargetPath);
        var reparseFileRejected = false;
        try
        {
            _ = LauncherStartupOptions.Parse(
                ["--game-install", startupTestDirectory],
                _ => publicProfile);
        }
        catch (InvalidDataException)
        {
            reparseFileRejected = true;
        }

        True(reparseFileRejected, "reparse DAT rejection");
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or
                               PlatformNotSupportedException)
    {
        Console.WriteLine($"SKIP: file reparse test unavailable: {ex.Message}");
    }
    finally
    {
        if (File.Exists(cellPath))
        {
            File.Delete(cellPath);
        }

        if (File.Exists(cellTargetPath))
        {
            File.Delete(cellTargetPath);
        }

        File.WriteAllBytes(cellPath, [0x01]);
    }

    try
    {
        Directory.CreateSymbolicLink(startupLinkDirectory, startupTestDirectory);
        var reparseRootRejected = false;
        try
        {
            _ = LauncherStartupOptions.Parse(
                ["--game-install", startupLinkDirectory],
                _ => publicProfile);
        }
        catch (InvalidDataException)
        {
            reparseRootRejected = true;
        }

        True(reparseRootRejected, "reparse install-root rejection");
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or
                               PlatformNotSupportedException)
    {
        Console.WriteLine($"SKIP: directory reparse test unavailable: {ex.Message}");
    }
}
finally
{
    if (Directory.Exists(startupLinkDirectory))
    {
        Directory.Delete(startupLinkDirectory);
    }

    Directory.Delete(startupTestDirectory, recursive: true);
}

var unknownUiArgumentRejected = false;
try
{
    _ = LauncherStartupOptions.Parse(["--unknown"]);
}
catch (ArgumentException)
{
    unknownUiArgumentRejected = true;
}

True(unknownUiArgumentRejected, "unknown UI argument rejection");

Console.WriteLine(
    "PASS: A09 public/admin profiles, UI install override, known memory-editor identity matching, " +
    "exact client signatures, SpellRegion duration-text hitch bypass, " +
    "XP label hitch bypass, " +
    "rollback bytes, bounded high-water wrapper, worker-drain completion wrapper, " +
    "layouts, branches, and detours verified.");

if (args is ["--verify-game-install", var gameInstallDirectory])
{
    var options = LauncherStartupOptions.Parse(
        ["--game-install", gameInstallDirectory]);
    Console.WriteLine(
        $"PASS: verified physical game install {options.GameInstallDirectory} as " +
        $"{options.ClientProfile?.Id}.");
}

if (args is ["--scan-active"])
{
    var timings = new List<long>();
    for (var pass = 0; pass < 3; pass++)
    {
        var started = Stopwatch.StartNew();
        ClientAntiTamper.EnsureNoKnownMemoryEditorRunning();
        started.Stop();
        timings.Add(started.ElapsedMilliseconds);
    }

    Console.WriteLine(
        $"PASS: active-program identity scans completed locally in {string.Join(", ", timings)} ms.");
}

if (args is ["--trigger-live-integrity-canary", var canaryProcessIdText])
{
    True(
        int.TryParse(canaryProcessIdText, out var canaryProcessId) && canaryProcessId > 0,
        "live integrity canary process id");
    using var canaryProcess = Process.GetProcessById(canaryProcessId);
    RemoteMemoryTest.TriggerLiveIntegrityCanary(canaryProcess);

    True(
        canaryProcess.WaitForExit(ClientAntiTamper.ScanIntervalMilliseconds + 8_000),
        "launcher monitor terminated the live DDD canary client");
    Console.WriteLine(
        $"PASS: live A09 integrity canary ended hash-verified client PID {canaryProcessId}.");
}

if (args is ["--prepare-release-client", var releaseClientDirectory])
{
    var fullDirectory = Path.GetFullPath(releaseClientDirectory);
    Directory.CreateDirectory(fullDirectory);
    foreach (var datName in new[] { "cell.dat", "portal.dat" })
    {
        var datPath = Path.Combine(fullDirectory, datName);
        if (!File.Exists(datPath))
        {
            File.WriteAllBytes(datPath, []);
        }
    }

    CommunityClientBootstrap.InstallFromCommunityAsync(fullDirectory)
        .GetAwaiter()
        .GetResult();
    CommunityClientBootstrap.VerifyInstalledClientAsync(fullDirectory)
        .GetAwaiter()
        .GetResult();
    Console.WriteLine(
        $"PASS: downloaded and hash-verified release integration client at {fullDirectory}.");
}

if (args is [var integrationMode, var clientPath] &&
    integrationMode is "--integration" or "--suspended-integration")
{
    var resumeClient = integrationMode == "--integration";
    var fullClientPath = Path.GetFullPath(clientPath);
    var workingDirectory = Path.GetDirectoryName(fullClientPath)
        ?? throw new InvalidOperationException("The integration client path has no directory.");
    var process = NativeProcess.StartSuspendedClient(
        fullClientPath,
        workingDirectory,
        "-a ddd-hook-integration-test -h 127.0.0.1 -p 9 -nd",
        out var processHandle,
        out var threadHandle);
    ClientAntiTamperContainment? integrationContainment = null;
    ClientAntiTamperRuntimeGuard? integrationGuard = null;
    using var violationSeen = new ManualResetEventSlim(initialState: false);
    string? observedViolation = null;
    try
    {
        integrationContainment = ClientAntiTamper.CreateRuntimeContainment(processHandle);
        var installation = NativeClientDddAcceleration.Apply(fullClientPath, processHandle);
        var detail = installation.Detail;
        True(detail.Contains(NativeClientDddAcceleration.CapabilityVersion,
                StringComparison.Ordinal),
            "runtime patch detail capability marker");
        True(detail.Contains("async writer guard active", StringComparison.Ordinal),
            "runtime patch detail writer guard");
        Equal(7, installation.Regions.Count, "monitored runtime patch region count");
        NativeClientDddAcceleration.VerifyInstalled(processHandle, installation);

        foreach (var region in installation.Regions)
        {
            var originalByte = region.ExpectedBytes[0];
            RemoteMemoryTest.WriteByte(
                processHandle,
                region.Address,
                (byte)(originalByte ^ 0xFF));
            var mutationDetected = false;
            try
            {
                NativeClientDddAcceleration.VerifyInstalled(processHandle, installation);
            }
            catch (InvalidDataException)
            {
                mutationDetected = true;
            }
            finally
            {
                RemoteMemoryTest.WriteByte(
                    processHandle,
                    region.Address,
                    originalByte);
            }

            True(mutationDetected, $"integrity mismatch detected for {region.Label}");
            NativeClientDddAcceleration.VerifyInstalled(processHandle, installation);
        }

        var duplicateRejected = false;
        try
        {
            _ = NativeClientDddAcceleration.Apply(fullClientPath, processHandle);
        }
        catch (InvalidDataException)
        {
            duplicateRejected = true;
        }

        True(duplicateRejected, "already-modified process signature rejection");

        integrationGuard = ClientAntiTamper.StartRuntimeMonitor(
            process,
            installation,
            integrationContainment,
            displayWarnings: false,
            violationObserver: reason =>
            {
                observedViolation = reason;
                violationSeen.Set();
            });
        if (resumeClient)
        {
            NativeProcess.ResumeAndClose(ref processHandle, ref threadHandle);
        }

        integrationGuard.VerifyNow();

        var liveCanaryRegion = installation.Regions.Single(region =>
            region.Label.Contains("UseTime", StringComparison.Ordinal));
        RemoteMemoryTest.TriggerLiveIntegrityCanary(process);
        True(
            violationSeen.Wait(ClientAntiTamper.ScanIntervalMilliseconds + 8_000),
            "resident monitor reported the live-canary detour mutation");
        True(
            observedViolation?.Contains(liveCanaryRegion.Label, StringComparison.Ordinal) == true,
            "resident monitor identified the live-canary detour region");
        True(
            process.WaitForExit(ClientAntiTamper.ScanIntervalMilliseconds + 8_000),
            $"resident monitor terminated a {(resumeClient ? "resumed" : "suspended")} " +
            "client after the live-canary detour mutation");

        Console.WriteLine(
            $"PASS: installed the transactional runtime patch, detected real mutations in all " +
            $"seven regions, monitored PID {process.Id} while " +
            $"{(resumeClient ? "resumed" : "suspended")}, and ended it after runtime tamper: " +
            detail);
    }
    finally
    {
        integrationGuard?.Dispose();
        integrationContainment?.Dispose();

        if (processHandle != IntPtr.Zero)
        {
            if (!process.HasExited && !NativeProcess.TerminateProcess(processHandle, 0))
            {
                throw new InvalidOperationException(
                    "TerminateProcess failed for the suspended integration client.");
            }

            if (!process.WaitForExit(5_000))
            {
                throw new InvalidOperationException(
                    "The suspended integration client did not terminate within five seconds.");
            }
        }

        if (threadHandle != IntPtr.Zero)
        {
            NativeProcess.CloseHandle(threadHandle);
        }

        if (processHandle != IntPtr.Zero)
        {
            NativeProcess.CloseHandle(processHandle);
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: false);
                process.WaitForExit(5_000);
            }
        }
        catch
        {
            // The assertion above retains the primary integration failure. The
            // kill-on-close job is the remaining fail-closed cleanup path.
        }

        process.Dispose();
    }
}

if (args is [
        "--job-kill-child",
        var childClientPath,
        var childSignalPath,
        var childAcknowledgementPath
    ])
{
    var fullClientPath = Path.GetFullPath(childClientPath);
    var workingDirectory = Path.GetDirectoryName(fullClientPath)
        ?? throw new InvalidOperationException("The job test client path has no directory.");
    Process? childClientProcess = null;
    var childProcessHandle = IntPtr.Zero;
    var childThreadHandle = IntPtr.Zero;
    ClientAntiTamperContainment? childContainment = null;
    try
    {
        childClientProcess = NativeProcess.StartSuspendedClient(
            fullClientPath,
            workingDirectory,
            "-a job-kill-integration-test -h 127.0.0.1 -p 9 -nd",
            out childProcessHandle,
            out childThreadHandle);
        childContainment = ClientAntiTamper.CreateRuntimeContainment(childProcessHandle);
        var installation = NativeClientDddAcceleration.Apply(
            fullClientPath,
            childProcessHandle);
        _ = ClientAntiTamper.StartRuntimeMonitor(
            childClientProcess,
            installation,
            childContainment,
            displayWarnings: false);
        var childSignalTemporaryPath = childSignalPath + ".tmp";
        File.WriteAllText(
            childSignalTemporaryPath,
            $"{childClientProcess.Id}|" +
            $"{childClientProcess.StartTime.ToUniversalTime().Ticks}|{fullClientPath}");
        File.Move(childSignalTemporaryPath, childSignalPath);

        var acknowledgementDeadline = Stopwatch.StartNew();
        while (!File.Exists(childAcknowledgementPath) &&
               acknowledgementDeadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(25);
        }

        // Simulate Task Manager ending the launcher: bypass managed finally blocks
        // so only JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE can end the suspended A09 client.
        RemoteMemoryTest.TerminateCurrentProcessImmediately();
        throw new InvalidOperationException("TerminateProcess unexpectedly returned.");
    }
    finally
    {
        childContainment?.Dispose();
        if (childProcessHandle != IntPtr.Zero)
        {
            NativeProcess.TerminateProcess(childProcessHandle, 0);
            NativeProcess.CloseHandle(childProcessHandle);
        }

        if (childThreadHandle != IntPtr.Zero)
        {
            NativeProcess.CloseHandle(childThreadHandle);
        }

        try
        {
            if (childClientProcess is not null && !childClientProcess.HasExited)
            {
                childClientProcess.Kill(entireProcessTree: false);
                childClientProcess.WaitForExit(5_000);
            }
        }
        catch
        {
            // Best-effort cleanup for a failed child-test setup.
        }

        childClientProcess?.Dispose();
    }
}

if (args is ["--job-kill-integration", var jobClientPath])
{
    var outerSignalPath = Path.Combine(
        Path.GetTempPath(),
        $"aetherium-job-kill-{Guid.NewGuid():N}.txt");
    var acknowledgementPath = outerSignalPath + ".ack";
    Process? child = null;
    Process? containedClient = null;
    try
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not resolve the patch-test executable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (Path.GetFileNameWithoutExtension(executablePath)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }

        startInfo.ArgumentList.Add("--job-kill-child");
        startInfo.ArgumentList.Add(Path.GetFullPath(jobClientPath));
        startInfo.ArgumentList.Add(outerSignalPath);
        startInfo.ArgumentList.Add(acknowledgementPath);

        child = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the launcher-kill child test.");
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(outerSignalPath) &&
               !child.HasExited &&
               deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(50);
        }

        if (!File.Exists(outerSignalPath))
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: false);
                child.WaitForExit(5_000);
            }

            var output = child.StandardOutput.ReadToEnd();
            var error = child.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                "The launcher-kill child did not publish its client PID. " +
                $"stdout={output} stderr={error}");
        }

        var signalParts = File.ReadAllText(outerSignalPath).Split('|', 3);
        Equal(3, signalParts.Length, "launcher-kill child identity field count");
        var clientProcessId = int.Parse(signalParts[0]);
        var clientStartTicks = long.Parse(signalParts[1]);
        var expectedClientPath = Path.GetFullPath(signalParts[2]);
        containedClient = Process.GetProcessById(clientProcessId);
        _ = containedClient.Handle;
        Equal(
            clientStartTicks,
            containedClient.StartTime.ToUniversalTime().Ticks,
            "launcher-kill client start identity");
        Equal(
            expectedClientPath,
            Path.GetFullPath(RemoteMemoryTest.QueryImagePath(containedClient.Handle)),
            "launcher-kill client image identity");

        File.WriteAllText(acknowledgementPath, "parent holds validated client handle");
        True(child.WaitForExit(10_000), "forced launcher child exit");
        var clientExited = containedClient.WaitForExit(10_000);
        if (!clientExited)
        {
            // This is the already-validated process handle, never a recycled PID.
            containedClient.Kill(entireProcessTree: false);
            containedClient.WaitForExit(5_000);
        }

        True(clientExited, "kill-on-launcher-exit job ended the admitted client");
        Console.WriteLine(
            $"PASS: forced launcher termination also ended contained client PID {clientProcessId}.");
    }
    finally
    {
        try
        {
            if (child is not null && !child.HasExited)
            {
                child.Kill(entireProcessTree: false);
                child.WaitForExit(5_000);
            }
        }
        catch
        {
            // Best-effort negative-test cleanup.
        }

        try
        {
            if (containedClient is not null && !containedClient.HasExited)
            {
                containedClient.Kill(entireProcessTree: false);
                containedClient.WaitForExit(5_000);
            }
        }
        catch
        {
            // Best-effort cleanup through the validated process handle only.
        }

        containedClient?.Dispose();
        child?.Dispose();
        if (File.Exists(outerSignalPath))
        {
            File.Delete(outerSignalPath);
        }

        if (File.Exists(acknowledgementPath))
        {
            File.Delete(acknowledgementPath);
        }

        var temporarySignalPath = outerSignalPath + ".tmp";
        if (File.Exists(temporarySignalPath))
        {
            File.Delete(temporarySignalPath);
        }
    }
}

internal static class RemoteMemoryTest
{
    private const uint PageExecuteReadWrite = 0x40;

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
    private static extern bool FlushInstructionCache(
        IntPtr process,
        IntPtr address,
        nuint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        IntPtr process,
        uint flags,
        StringBuilder executableName,
        ref int size);

    internal static void WriteByte(IntPtr process, IntPtr address, byte value)
    {
        if (!VirtualProtectEx(
                process,
                address,
                1,
                PageExecuteReadWrite,
                out var oldProtect))
        {
            throw Failure("VirtualProtectEx failed before test mutation");
        }

        try
        {
            var bytes = new[] { value };
            if (!WriteProcessMemory(process, address, bytes, 1, out var written) || written != 1)
            {
                throw Failure("WriteProcessMemory failed for test mutation");
            }

            if (!FlushInstructionCache(process, address, 1))
            {
                throw Failure("FlushInstructionCache failed for test mutation");
            }
        }
        finally
        {
            if (!VirtualProtectEx(process, address, 1, oldProtect, out _))
            {
                throw Failure("VirtualProtectEx failed after test mutation");
            }
        }
    }

    internal static void TriggerLiveIntegrityCanary(Process process)
    {
        var processHandle = process.Handle;
        var imagePath = Path.GetFullPath(QueryImagePath(processHandle));
        var image = new FileInfo(imagePath);
        if (!image.Name.Equals("client.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Live integrity canary refused non-client executable {image.Name}.");
        }

        var profile = NativeClientDddAcceleration.ResolveSupportedClientProfile(imagePath);

        var capabilityAddress = new IntPtr(unchecked((int)(
            profile.PreferredImageBase + profile.VersionLiteralRva)));
        var expectedCapability = NativeClientDddAcceleration.CapabilityVersionForTest();
        if (!ReadBytes(processHandle, capabilityAddress, expectedCapability.Length)
                .SequenceEqual(expectedCapability))
        {
            throw new InvalidDataException(
                "Live integrity canary refused a client without the exact A09 marker.");
        }

        var useTimeAddress = new IntPtr(unchecked((int)(
            profile.PreferredImageBase + profile.UseTimeRva)));
        var useTimePatch = ReadBytes(processHandle, useTimeAddress, 6);
        if (useTimePatch[0] != 0xE9 || useTimePatch[5] != 0x90)
        {
            throw new InvalidDataException(
                "Live integrity canary refused a client without the expected A09 UseTime detour.");
        }

        WriteByte(processHandle, useTimeAddress, 0x90);
    }

    internal static byte[] ReadBytes(IntPtr process, IntPtr address, int length)
    {
        var bytes = new byte[length];
        if (!ReadProcessMemory(process, address, bytes, (nuint)length, out var read) ||
            read != (nuint)length)
        {
            throw Failure("ReadProcessMemory failed for canary verification");
        }

        return bytes;
    }

    internal static void TerminateCurrentProcessImmediately()
    {
        if (!NativeProcess.TerminateProcess(GetCurrentProcess(), 0))
        {
            throw Failure("TerminateProcess failed for launcher-kill simulation");
        }
    }

    internal static string QueryImagePath(IntPtr process)
    {
        var capacity = 1024;
        var path = new StringBuilder(capacity);
        if (!QueryFullProcessImageNameW(process, 0, path, ref capacity))
        {
            throw Failure("QueryFullProcessImageNameW failed for contained client identity");
        }

        return path.ToString();
    }

    private static Win32Exception Failure(string message) =>
        new(Marshal.GetLastWin32Error(), message);
}
