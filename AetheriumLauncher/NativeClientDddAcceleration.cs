using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AcLegacyLauncher;

internal sealed class NativeClientSpellRegionHook
{
    internal NativeClientSpellRegionHook(
        uint updateRva,
        int updateSize,
        int updateFileOffset,
        byte[] expectedSignature,
        uint spellDurationRva,
        uint playerModuleRva,
        uint timerCurTimeRva,
        uint ftol2Rva,
        uint getTextRva,
        uint setTextRva,
        uint sprintfIatRva)
    {
        UpdateRva = updateRva;
        UpdateSize = updateSize;
        UpdateFileOffset = updateFileOffset;
        ExpectedSignature = (byte[])expectedSignature.Clone();
        SpellDurationRva = spellDurationRva;
        PlayerModuleRva = playerModuleRva;
        TimerCurTimeRva = timerCurTimeRva;
        Ftol2Rva = ftol2Rva;
        GetTextRva = getTextRva;
        SetTextRva = setTextRva;
        SprintfIatRva = sprintfIatRva;
    }

    internal uint UpdateRva { get; }

    internal int UpdateSize { get; }

    internal int UpdateFileOffset { get; }

    internal byte[] ExpectedSignature { get; }

    internal uint SpellDurationRva { get; }

    internal uint PlayerModuleRva { get; }

    internal uint TimerCurTimeRva { get; }

    internal uint Ftol2Rva { get; }

    internal uint GetTextRva { get; }

    internal uint SetTextRva { get; }

    internal uint SprintfIatRva { get; }
}

internal sealed class NativeClientXpLabelHook
{
    internal NativeClientXpLabelHook(
        uint setIntSignedRva,
        byte[] expectedSetIntSignedSignature,
        uint setIntUnsignedRva,
        byte[] expectedSetIntUnsignedSignature,
        uint statRegionSetIntRva,
        byte[] expectedStatRegionSignature,
        uint infoBoxSetAvailableRva,
        byte[] expectedInfoBoxSignature,
        uint infoBoxSetParentTailRva,
        uint allegPanelSetXpChangeRva,
        byte[] expectedAllegPanelSignature)
    {
        SetIntSignedRva = setIntSignedRva;
        ExpectedSetIntSignedSignature = (byte[])expectedSetIntSignedSignature.Clone();
        SetIntUnsignedRva = setIntUnsignedRva;
        ExpectedSetIntUnsignedSignature = (byte[])expectedSetIntUnsignedSignature.Clone();
        StatRegionSetIntRva = statRegionSetIntRva;
        ExpectedStatRegionSignature = (byte[])expectedStatRegionSignature.Clone();
        InfoBoxSetAvailableRva = infoBoxSetAvailableRva;
        ExpectedInfoBoxSignature = (byte[])expectedInfoBoxSignature.Clone();
        InfoBoxSetParentTailRva = infoBoxSetParentTailRva;
        AllegPanelSetXpChangeRva = allegPanelSetXpChangeRva;
        ExpectedAllegPanelSignature = (byte[])expectedAllegPanelSignature.Clone();
    }

    internal uint SetIntSignedRva { get; }

    internal byte[] ExpectedSetIntSignedSignature { get; }

    internal byte[] OriginalSetIntSignedPrologue =>
        StolenPrologue(ExpectedSetIntSignedSignature, NativeClientDddAcceleration.StolenDetourLength);

    internal uint SetIntUnsignedRva { get; }

    internal byte[] ExpectedSetIntUnsignedSignature { get; }

    internal byte[] OriginalSetIntUnsignedPrologue =>
        StolenPrologue(ExpectedSetIntUnsignedSignature, NativeClientDddAcceleration.StolenDetourLength);

    internal uint StatRegionSetIntRva { get; }

    internal byte[] ExpectedStatRegionSignature { get; }

    internal byte[] OriginalStatRegionPrologue =>
        StolenPrologue(ExpectedStatRegionSignature, NativeClientDddAcceleration.StolenDetourLength);

    internal uint InfoBoxSetAvailableRva { get; }

    internal byte[] ExpectedInfoBoxSignature { get; }

    internal byte[] OriginalInfoBoxPrologue =>
        StolenPrologue(ExpectedInfoBoxSignature, NativeClientDddAcceleration.InfoBoxStolenLength);

    internal uint InfoBoxSetParentTailRva { get; }

    internal uint AllegPanelSetXpChangeRva { get; }

    internal byte[] ExpectedAllegPanelSignature { get; }

    private static byte[] StolenPrologue(byte[] signature, int length)
    {
        var prologue = new byte[length];
        signature.AsSpan(0, length).CopyTo(prologue);
        return prologue;
    }
}

internal sealed class NativeClientDddAccelerationProfile
{
    internal NativeClientDddAccelerationProfile(
        string id,
        long expectedSize,
        string expectedSha256,
        uint preferredImageBase,
        uint useTimeRva,
        byte[] expectedUseTimeSignature,
        uint downloadStatusRva,
        byte[] expectedDownloadStatusSignature,
        uint versionLiteralRva,
        byte[] originalVersionLiteral,
        NativeClientSpellRegionHook spellRegion,
        NativeClientXpLabelHook xpLabel)
    {
        Id = id;
        ExpectedSize = expectedSize;
        ExpectedSha256 = expectedSha256;
        PreferredImageBase = preferredImageBase;
        UseTimeRva = useTimeRva;
        ExpectedUseTimeSignature = (byte[])expectedUseTimeSignature.Clone();
        DownloadStatusRva = downloadStatusRva;
        ExpectedDownloadStatusSignature = (byte[])expectedDownloadStatusSignature.Clone();
        VersionLiteralRva = versionLiteralRva;
        OriginalVersionLiteral = (byte[])originalVersionLiteral.Clone();
        SpellRegion = spellRegion;
        XpLabel = xpLabel;
    }

    internal string Id { get; }

    internal long ExpectedSize { get; }

    internal string ExpectedSha256 { get; }

    internal uint PreferredImageBase { get; }

    internal uint UseTimeRva { get; }

    internal byte[] ExpectedUseTimeSignature { get; }

    internal uint DownloadStatusRva { get; }

    internal uint DownloadStatusTailRva => DownloadStatusRva + 0x24;

    internal byte[] ExpectedDownloadStatusSignature { get; }

    internal uint VersionLiteralRva { get; }

    internal byte[] OriginalVersionLiteral { get; }

    internal NativeClientSpellRegionHook SpellRegion { get; }

    internal NativeClientXpLabelHook XpLabel { get; }
}

internal sealed class NativeClientDddAccelerationInstallation
{
    internal NativeClientDddAccelerationInstallation(
        string detail,
        NativeClientDddAccelerationProfile profile,
        IReadOnlyList<NativeClientDddAcceleration.RemotePatchRegion> regions)
    {
        Detail = detail;
        Profile = profile;
        Regions = regions;
    }

    internal string Detail { get; }

    internal NativeClientDddAccelerationProfile Profile { get; }

    internal IReadOnlyList<NativeClientDddAcceleration.RemotePatchRegion> Regions { get; }
}

/// <summary>
/// Applies the Aetherium process-local A09 hooks to a verified public or
/// admin DM client. The patch never modifies client.exe on disk.
///
/// Two CLCache detours drain inbound DAT work faster and hold the patch UI
/// incomplete until both writers are idle. A third detour replaces
/// SpellRegion::Update so open buff/debuff duration labels skip ClearAllText
/// when the m:ss string is unchanged. Number-panel hitch skips hook the
/// integer writers that already have the value (TextRegion::SetInt signed
/// and unsigned, StatRegion::SetInt, InfoBox::SetAvailable, and
/// AllegPanel::SetXPChange). Global TextRegion::SetText stays stock: 1.0.26
/// detoured it and that prevented the client from opening.
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
    // Public 1.0.69 SpellRegion::Update: file offset == RVA because .text
    // PointerToRawData and VirtualAddress are both 0x1000. Size is the padded
    // distance to MouseDown; the live body returns at +0xE0.
    internal const uint SpellRegionUpdateRva = 0x000B_1480;
    internal const int SpellRegionUpdateFileOffset = 0x000B_1480;
    internal const int SpellRegionUpdateSize = 0xF0;
    internal const uint SpellDurationRva = 0x0018_A160;
    internal const uint PlayerModuleRva = 0x0028_D3B8;
    internal const uint TimerCurTimeRva = 0x0026_0878;
    internal const uint Ftol2Rva = 0x0018_DF30;
    internal const uint TextRegionGetTextRva = 0x0001_8650;
    internal const uint TextRegionSetTextRva = 0x0001_D790;
    internal const uint SprintfIatRva = 0x001E_9328;
    internal const string PublicProfileId = "public-retail-1.0.69";
    internal const string AdminProfileId = "aetherium-admin-1.0.69";
    internal const long AdminExpectedSize = 4_149_248;
    internal const string AdminExpectedSha256 =
        "0FF432B4B98F7510034B924F73D35CD1684DFA0AEB229F2DF31C57DB5936229F";
    internal const uint AdminUseTimeRva = 0x0001_02E0;
    internal const uint AdminDownloadStatusRva = 0x0000_F850;
    internal const uint AdminVersionLiteralRva = 0x0023_8E8C;
    internal const uint AdminSpellRegionUpdateRva = 0x000B_8A40;
    internal const int AdminSpellRegionUpdateFileOffset = 0x000B_8A40;
    internal const uint AdminSpellDurationRva = 0x001C_D260;
    internal const uint AdminPlayerModuleRva = 0x003D_9348;
    internal const uint AdminTimerCurTimeRva = 0x003A_C2D0;
    internal const uint AdminFtol2Rva = 0x001D_0F90;
    internal const uint AdminTextRegionGetTextRva = 0x0001_B0D0;
    internal const uint AdminTextRegionSetTextRva = 0x0002_0220;
    internal const uint AdminSprintfIatRva = 0x0023_03F4;
    internal const uint AllegPanelSetXpChangeRva = 0x000B_CA90;
    internal const uint AdminAllegPanelSetXpChangeRva = 0x000C_4250;
    internal const uint TextRegionSetIntSignedRva = 0x0001_D7C0;
    internal const uint TextRegionSetIntUnsignedRva = 0x0001_D810;
    internal const uint AdminTextRegionSetIntSignedRva = 0x0002_0250;
    internal const uint AdminTextRegionSetIntUnsignedRva = 0x0002_02A0;
    internal const uint StatRegionSetIntRva = 0x000C_F260;
    internal const uint AdminStatRegionSetIntRva = 0x000D_6A70;
    internal const uint InfoBoxSetAvailableRva = 0x000C_F7D0;
    internal const uint AdminInfoBoxSetAvailableRva = 0x000D_6FE0;
    internal const uint InfoBoxSetParentTailRva = 0x000C_F8D2;
    internal const uint AdminInfoBoxSetParentTailRva = 0x000D_70E2;
    internal const int UseTimeTrampolineOffset = 0x80;
    internal const int DownloadStatusWrapperOffset = 0xA0;
    internal const int SpellRegionWrapperOffset = 0x100;
    internal const int SetIntSignedWrapperOffset = 0x280;
    internal const int SetIntSignedTrampolineOffset = 0x400;
    internal const int SetIntUnsignedWrapperOffset = 0x410;
    internal const int SetIntUnsignedTrampolineOffset = 0x590;
    internal const int StatRegionWrapperOffset = 0x5A0;
    internal const int StatRegionTrampolineOffset = 0x5E0;
    internal const int InfoBoxWrapperOffset = 0x5F0;
    internal const int InfoBoxTrampolineOffset = 0x640;
    internal const int AllegPanelWrapperOffset = 0x650;
    internal const int AllegPanelTrampolineOffset = 0x6B0;
    internal const int RemoteCodeSize = 0x700;
    internal const int StolenDetourLength = 8;
    internal const int InfoBoxStolenLength = 5;
    internal const string CapabilityVersion = "2005.02.A09";
    internal const int TextRegionFontOffset = 0xC8;
    internal const int TextRegionLineCountOffset = 0x10C;
    internal const int TextRegionLineBuffOffset = 0x110;
    internal const int GlyphStringHeadOffset = 4;
    internal const int GlyphFontOffset = 0x14;
    internal const int GlyphTypeOffset = 4;
    internal const int GlyphCharOffset = 8;
    internal const int AllegPanelXpAvailOffset = 0xC8;
    internal const int AllegPanelLevelOffset = 0xCC;
    internal const int AllegPanelPatronIdOffset = 0xD4;
    internal const int StatRegionXpTotalWidgetOffset = 0xBC;
    internal const int StatRegionXpTotalOffset = 0xDC;
    internal const int InfoBoxAvailableOffset = 0xBC;
    internal const uint TotalExperienceQuality = 0x15;

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint PageExecuteRead = 0x20;
    private const uint PageExecuteReadWrite = 0x40;

    private static readonly byte[] PublicExpectedUseTimeSignature =
    [
        0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC,
        0x08, 0x53, 0x55, 0x56, 0x57, 0x8B, 0xF9, 0x8B,
        0x47, 0x04, 0x85, 0xC0, 0xBE, 0xF8, 0xB7, 0x7E,
        0x00, 0x75, 0x37, 0xA1, 0x78, 0x08, 0x66, 0x00,
    ];

    private static readonly byte[] AdminExpectedUseTimeSignature =
    [
        0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8, 0x83, 0xEC,
        0x08, 0x53, 0x55, 0x56, 0x57, 0x8B, 0xF9, 0x8B,
        0x47, 0x04, 0x85, 0xC0, 0xBE, 0x78, 0x7E, 0x93,
        0x00, 0x75, 0x37, 0xA1, 0xD0, 0xC2, 0x7A, 0x00,
    ];

    private static readonly byte[] PublicExpectedSpellRegionSignature =
    [
        0x83, 0xEC, 0x18, 0x56, 0x8B, 0xF1, 0x8B, 0x8E,
        0xC4, 0x00, 0x00, 0x00, 0xE8, 0xFF, 0xA4, 0xF6,
        0xFF, 0x8B, 0x86, 0xD4, 0x00, 0x00, 0x00, 0x85,
        0xC0, 0x0F, 0x84, 0xBD, 0x00, 0x00, 0x00, 0xB9,
    ];

    private static readonly byte[] AdminExpectedSpellRegionSignature =
    [
        0x83, 0xEC, 0x18, 0x56, 0x8B, 0xF1, 0x8B, 0x8E,
        0xC4, 0x00, 0x00, 0x00, 0xE8, 0xAF, 0x59, 0xF6,
        0xFF, 0x8B, 0x86, 0xD4, 0x00, 0x00, 0x00, 0x85,
        0xC0, 0x0F, 0x84, 0xBD, 0x00, 0x00, 0x00, 0xB9,
    ];

    private static readonly byte[] PublicExpectedSetIntSignedSignature =
    [
        0x8B, 0x44, 0x24, 0x04, 0x83, 0xEC, 0x14, 0x56,
        0x50, 0x8B, 0xF1, 0x8D, 0x4C, 0x24, 0x08, 0x68,
        0x40, 0xA9, 0x5E, 0x00, 0x51, 0xFF, 0x15, 0x28,
        0x93, 0x5E, 0x00, 0x83, 0xC4, 0x0C, 0x8B, 0xCE,
    ];

    private static readonly byte[] PublicExpectedSetIntUnsignedSignature =
    [
        0x8B, 0x44, 0x24, 0x04, 0x83, 0xEC, 0x14, 0x56,
        0x50, 0x8B, 0xF1, 0x8D, 0x4C, 0x24, 0x08, 0x68,
        0xEC, 0xC7, 0x5E, 0x00, 0x51, 0xFF, 0x15, 0x28,
        0x93, 0x5E, 0x00, 0x83, 0xC4, 0x0C, 0x8B, 0xCE,
    ];

    private static readonly byte[] AdminExpectedSetIntSignedSignature =
    [
        0x8B, 0x44, 0x24, 0x04, 0x83, 0xEC, 0x14, 0x56,
        0x50, 0x8B, 0xF1, 0x8D, 0x4C, 0x24, 0x08, 0x68,
        0xF0, 0x19, 0x63, 0x00, 0x51, 0xFF, 0x15, 0xF4,
        0x03, 0x63, 0x00, 0x83, 0xC4, 0x0C, 0x8B, 0xCE,
    ];

    private static readonly byte[] AdminExpectedSetIntUnsignedSignature =
    [
        0x8B, 0x44, 0x24, 0x04, 0x83, 0xEC, 0x14, 0x56,
        0x50, 0x8B, 0xF1, 0x8D, 0x4C, 0x24, 0x08, 0x68,
        0x40, 0x42, 0x63, 0x00, 0x51, 0xFF, 0x15, 0xF4,
        0x03, 0x63, 0x00, 0x83, 0xC4, 0x0C, 0x8B, 0xCE,
    ];

    private static readonly byte[] PublicExpectedStatRegionSignature =
    [
        0x51, 0xA1, 0x68, 0xCC, 0x7E, 0x00, 0x53, 0x55,
        0x56, 0x89, 0x44, 0x24, 0x0C, 0x57, 0x83, 0xC0,
        0x04, 0x50, 0x8B, 0xF1, 0xFF, 0x15, 0xE0, 0x91,
        0x5E, 0x00, 0x8B, 0x44, 0x24, 0x18, 0x83, 0xC0,
    ];

    private static readonly byte[] AdminExpectedStatRegionSignature =
    [
        0x51, 0xA1, 0x70, 0x93, 0x93, 0x00, 0x53, 0x55,
        0x56, 0x89, 0x44, 0x24, 0x0C, 0x57, 0x83, 0xC0,
        0x04, 0x50, 0x8B, 0xF1, 0xFF, 0x15, 0x24, 0x02,
        0x63, 0x00, 0x8B, 0x44, 0x24, 0x18, 0x83, 0xC0,
    ];

    private static readonly byte[] PublicExpectedInfoBoxSignature =
    [
        0x51, 0x8B, 0x44, 0x24, 0x08, 0x56, 0x8B, 0xF1,
        0x3B, 0x86, 0xB8, 0x00, 0x00, 0x00, 0x57, 0x89,
        0x86, 0xBC, 0x00, 0x00, 0x00, 0x72, 0x0E, 0x8B,
        0x0D, 0x14, 0xD3, 0x68, 0x00, 0x8B, 0x89, 0x30,
    ];

    private static readonly byte[] AdminExpectedInfoBoxSignature =
    [
        0x51, 0x8B, 0x44, 0x24, 0x08, 0x56, 0x8B, 0xF1,
        0x3B, 0x86, 0xB8, 0x00, 0x00, 0x00, 0x57, 0x89,
        0x86, 0xBC, 0x00, 0x00, 0x00, 0x72, 0x0E, 0x8B,
        0x0D, 0xA4, 0x92, 0x7D, 0x00, 0x8B, 0x89, 0x30,
    ];

    private static readonly byte[] PublicExpectedAllegPanelSignature =
    [
        0x83, 0xEC, 0x0C, 0x53, 0x55, 0x56, 0x8B, 0xF1,
        0x8B, 0x06, 0x57, 0xFF, 0x50, 0x34, 0x8B, 0x2D,
        0x1C, 0xD3, 0x68, 0x00, 0x8B, 0x4C, 0x24, 0x24,
        0x8B, 0x54, 0x24, 0x20, 0x8B, 0xF8, 0x33, 0xC0,
    ];

    private static readonly byte[] AdminExpectedAllegPanelSignature =
    [
        0x83, 0xEC, 0x0C, 0x53, 0x55, 0x56, 0x8B, 0xF1,
        0x8B, 0x06, 0x57, 0xFF, 0x50, 0x34, 0x8B, 0x2D,
        0xAC, 0x92, 0x7D, 0x00, 0x8B, 0x4C, 0x24, 0x24,
        0x8B, 0x54, 0x24, 0x20, 0x8B, 0xF8, 0x33, 0xC0,
    ];

    private static readonly byte[] OriginalUseTimePrologue =
    [
        0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF8,
    ];

    private static readonly byte[] OriginalSpellRegionPrologue =
    [
        0x83, 0xEC, 0x18, 0x56, 0x8B, 0xF1,
    ];

    private static readonly byte[] OriginalAllegPanelPrologue =
    [
        0x83, 0xEC, 0x0C, 0x53, 0x55, 0x56, 0x8B, 0xF1,
    ];

    private static readonly byte[] OriginalSetIntPrologue =
    [
        0x8B, 0x44, 0x24, 0x04, 0x83, 0xEC, 0x14, 0x56,
    ];

    private static readonly byte[] OriginalInfoBoxPrologue =
    [
        0x51, 0x8B, 0x44, 0x24, 0x08,
    ];

    // Stock SpellRegion::Update _ftol2 pair constants (IEEE doubles).
    private static readonly byte[] SpellRegionZero = BitConverter.GetBytes(0.0);
    private static readonly byte[] SpellRegionInv60 = BitConverter.GetBytes(1.0 / 60.0);
    private static readonly byte[] SpellRegionHundred = BitConverter.GetBytes(100.0);
    private static readonly byte[] SpellRegionHundredth = BitConverter.GetBytes(0.01);
    private static readonly byte[] SpellRegionSixty = BitConverter.GetBytes(60.0);
    private static readonly byte[] SpellRegionFormatPadded = "%d:0%d\0"u8.ToArray();
    private static readonly byte[] SpellRegionFormatUnpadded = "%d:%d\0"u8.ToArray();

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

    private static readonly NativeClientDddAccelerationProfile PublicProfile = new(
        PublicProfileId,
        CommunityClientBootstrap.ExpectedSize,
        CommunityClientBootstrap.ExpectedSha256,
        PreferredImageBase,
        UseTimeRva,
        PublicExpectedUseTimeSignature,
        DownloadStatusRva,
        ExpectedDownloadStatusSignature,
        VersionLiteralRva,
        OriginalVersionLiteral,
        new NativeClientSpellRegionHook(
            SpellRegionUpdateRva,
            SpellRegionUpdateSize,
            SpellRegionUpdateFileOffset,
            PublicExpectedSpellRegionSignature,
            SpellDurationRva,
            PlayerModuleRva,
            TimerCurTimeRva,
            Ftol2Rva,
            TextRegionGetTextRva,
            TextRegionSetTextRva,
            SprintfIatRva),
        new NativeClientXpLabelHook(
            TextRegionSetIntSignedRva,
            PublicExpectedSetIntSignedSignature,
            TextRegionSetIntUnsignedRva,
            PublicExpectedSetIntUnsignedSignature,
            StatRegionSetIntRva,
            PublicExpectedStatRegionSignature,
            InfoBoxSetAvailableRva,
            PublicExpectedInfoBoxSignature,
            InfoBoxSetParentTailRva,
            AllegPanelSetXpChangeRva,
            PublicExpectedAllegPanelSignature));

    private static readonly NativeClientDddAccelerationProfile AdminProfile = new(
        AdminProfileId,
        AdminExpectedSize,
        AdminExpectedSha256,
        PreferredImageBase,
        AdminUseTimeRva,
        AdminExpectedUseTimeSignature,
        AdminDownloadStatusRva,
        ExpectedDownloadStatusSignature,
        AdminVersionLiteralRva,
        OriginalVersionLiteral,
        new NativeClientSpellRegionHook(
            AdminSpellRegionUpdateRva,
            SpellRegionUpdateSize,
            AdminSpellRegionUpdateFileOffset,
            AdminExpectedSpellRegionSignature,
            AdminSpellDurationRva,
            AdminPlayerModuleRva,
            AdminTimerCurTimeRva,
            AdminFtol2Rva,
            AdminTextRegionGetTextRva,
            AdminTextRegionSetTextRva,
            AdminSprintfIatRva),
        new NativeClientXpLabelHook(
            AdminTextRegionSetIntSignedRva,
            AdminExpectedSetIntSignedSignature,
            AdminTextRegionSetIntUnsignedRva,
            AdminExpectedSetIntUnsignedSignature,
            AdminStatRegionSetIntRva,
            AdminExpectedStatRegionSignature,
            AdminInfoBoxSetAvailableRva,
            AdminExpectedInfoBoxSignature,
            AdminInfoBoxSetParentTailRva,
            AdminAllegPanelSetXpChangeRva,
            AdminExpectedAllegPanelSignature));

    private static readonly IReadOnlyList<NativeClientDddAccelerationProfile> SupportedProfiles =
        [PublicProfile, AdminProfile];

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

        var profile = ResolveSupportedClientProfile(clientPath);

        var useTimeAddress = Address(profile.PreferredImageBase + profile.UseTimeRva);
        var downloadStatusAddress = Address(profile.PreferredImageBase + profile.DownloadStatusRva);
        var downloadStatusTailAddress = Address(
            profile.PreferredImageBase + profile.DownloadStatusTailRva);
        var spellRegionAddress = Address(
            profile.PreferredImageBase + profile.SpellRegion.UpdateRva);
        var setIntSignedAddress = Address(
            profile.PreferredImageBase + profile.XpLabel.SetIntSignedRva);
        var setIntUnsignedAddress = Address(
            profile.PreferredImageBase + profile.XpLabel.SetIntUnsignedRva);
        var statRegionAddress = Address(
            profile.PreferredImageBase + profile.XpLabel.StatRegionSetIntRva);
        var infoBoxAddress = Address(
            profile.PreferredImageBase + profile.XpLabel.InfoBoxSetAvailableRva);
        var allegPanelAddress = Address(
            profile.PreferredImageBase + profile.XpLabel.AllegPanelSetXpChangeRva);
        var versionAddress = Address(profile.PreferredImageBase + profile.VersionLiteralRva);
        VerifyRemoteBytes(
            processHandle,
            useTimeAddress,
            profile.ExpectedUseTimeSignature,
            "CLCache::UseTime");
        VerifyRemoteBytes(
            processHandle,
            downloadStatusAddress,
            profile.ExpectedDownloadStatusSignature,
            "CLCache::Get_Download_Status");
        VerifyRemoteBytes(
            processHandle,
            spellRegionAddress,
            profile.SpellRegion.ExpectedSignature,
            "SpellRegion::Update");
        VerifyRemoteBytes(
            processHandle,
            setIntSignedAddress,
            profile.XpLabel.ExpectedSetIntSignedSignature,
            "TextRegion::SetInt signed");
        VerifyRemoteBytes(
            processHandle,
            setIntUnsignedAddress,
            profile.XpLabel.ExpectedSetIntUnsignedSignature,
            "TextRegion::SetInt unsigned");
        VerifyRemoteBytes(
            processHandle,
            statRegionAddress,
            profile.XpLabel.ExpectedStatRegionSignature,
            "StatRegion::SetInt");
        VerifyRemoteBytes(
            processHandle,
            infoBoxAddress,
            profile.XpLabel.ExpectedInfoBoxSignature,
            "InfoBox::SetAvailable");
        VerifyRemoteBytes(
            processHandle,
            allegPanelAddress,
            profile.XpLabel.ExpectedAllegPanelSignature,
            "AllegPanel::SetXPChange");
        VerifyRemoteBytes(
            processHandle,
            versionAddress,
            profile.OriginalVersionLiteral,
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
        var spellRegionPatchAttempted = false;
        var setIntSignedPatchAttempted = false;
        var setIntUnsignedPatchAttempted = false;
        var statRegionPatchAttempted = false;
        var infoBoxPatchAttempted = false;
        var allegPanelPatchAttempted = false;
        var versionPatchAttempted = false;
        try
        {
            var code = BuildRemoteCode(remoteCode, profile);
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

            var spellRegionPatch = BuildEntryPatch(
                spellRegionAddress,
                Add(remoteCode, SpellRegionWrapperOffset));
            spellRegionPatchAttempted = true;
            WriteProtectedExact(
                processHandle,
                spellRegionAddress,
                spellRegionPatch,
                "SpellRegion::Update detour");
            FlushExact(
                processHandle,
                spellRegionAddress,
                (nuint)spellRegionPatch.Length,
                "SpellRegion::Update detour");

            var setIntSignedPatch = BuildStolenDetourPatch(
                setIntSignedAddress,
                Add(remoteCode, SetIntSignedWrapperOffset));
            setIntSignedPatchAttempted = true;
            WriteProtectedExact(
                processHandle,
                setIntSignedAddress,
                setIntSignedPatch,
                "TextRegion::SetInt signed detour");
            FlushExact(
                processHandle,
                setIntSignedAddress,
                (nuint)setIntSignedPatch.Length,
                "TextRegion::SetInt signed detour");

            var setIntUnsignedPatch = BuildStolenDetourPatch(
                setIntUnsignedAddress,
                Add(remoteCode, SetIntUnsignedWrapperOffset));
            setIntUnsignedPatchAttempted = true;
            WriteProtectedExact(
                processHandle,
                setIntUnsignedAddress,
                setIntUnsignedPatch,
                "TextRegion::SetInt unsigned detour");
            FlushExact(
                processHandle,
                setIntUnsignedAddress,
                (nuint)setIntUnsignedPatch.Length,
                "TextRegion::SetInt unsigned detour");

            var statRegionPatch = BuildStolenDetourPatch(
                statRegionAddress,
                Add(remoteCode, StatRegionWrapperOffset));
            statRegionPatchAttempted = true;
            WriteProtectedExact(
                processHandle,
                statRegionAddress,
                statRegionPatch,
                "StatRegion::SetInt detour");
            FlushExact(
                processHandle,
                statRegionAddress,
                (nuint)statRegionPatch.Length,
                "StatRegion::SetInt detour");

            var infoBoxPatch = BuildNearJumpPatch(
                infoBoxAddress,
                Add(remoteCode, InfoBoxWrapperOffset));
            infoBoxPatchAttempted = true;
            WriteProtectedExact(
                processHandle,
                infoBoxAddress,
                infoBoxPatch,
                "InfoBox::SetAvailable detour");
            FlushExact(
                processHandle,
                infoBoxAddress,
                (nuint)infoBoxPatch.Length,
                "InfoBox::SetAvailable detour");

            var allegPanelPatch = BuildStolenDetourPatch(
                allegPanelAddress,
                Add(remoteCode, AllegPanelWrapperOffset));
            allegPanelPatchAttempted = true;
            WriteProtectedExact(
                processHandle,
                allegPanelAddress,
                allegPanelPatch,
                "AllegPanel::SetXPChange detour");
            FlushExact(
                processHandle,
                allegPanelAddress,
                (nuint)allegPanelPatch.Length,
                "AllegPanel::SetXPChange detour");

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
                $"Accelerated DAT repair enabled ({CapabilityVersion}; {profile.Id}; up to " +
                $"{MaxMessagesPerFrame} queued records per frame; async writer guard active; " +
                "SpellRegion duration-text hitch bypass active; number-label hitch bypass active).",
                profile,
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
                        spellRegionAddress,
                        spellRegionPatch,
                        "installed SpellRegion::Update detour"),
                    new RemotePatchRegion(
                        setIntSignedAddress,
                        setIntSignedPatch,
                        "installed TextRegion::SetInt signed detour"),
                    new RemotePatchRegion(
                        setIntUnsignedAddress,
                        setIntUnsignedPatch,
                        "installed TextRegion::SetInt unsigned detour"),
                    new RemotePatchRegion(
                        statRegionAddress,
                        statRegionPatch,
                        "installed StatRegion::SetInt detour"),
                    new RemotePatchRegion(
                        infoBoxAddress,
                        infoBoxPatch,
                        "installed InfoBox::SetAvailable detour"),
                    new RemotePatchRegion(
                        allegPanelAddress,
                        allegPanelPatch,
                        "installed AllegPanel::SetXPChange detour"),
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
                    profile.OriginalVersionLiteral,
                    "client version marker",
                    rollbackErrors);
            }

            if (allegPanelPatchAttempted)
            {
                TryRollback(
                    processHandle,
                    allegPanelAddress,
                    OriginalAllegPanelPrologue,
                    "AllegPanel::SetXPChange detour",
                    rollbackErrors);
                TryFlushRollback(
                    processHandle,
                    allegPanelAddress,
                    OriginalAllegPanelPrologue.Length,
                    "AllegPanel::SetXPChange rollback",
                    rollbackErrors);
            }

            if (infoBoxPatchAttempted)
            {
                TryRollback(
                    processHandle,
                    infoBoxAddress,
                    OriginalInfoBoxPrologue,
                    "InfoBox::SetAvailable detour",
                    rollbackErrors);
                TryFlushRollback(
                    processHandle,
                    infoBoxAddress,
                    OriginalInfoBoxPrologue.Length,
                    "InfoBox::SetAvailable rollback",
                    rollbackErrors);
            }

            if (statRegionPatchAttempted)
            {
                TryRollback(
                    processHandle,
                    statRegionAddress,
                    profile.XpLabel.OriginalStatRegionPrologue,
                    "StatRegion::SetInt detour",
                    rollbackErrors);
                TryFlushRollback(
                    processHandle,
                    statRegionAddress,
                    StolenDetourLength,
                    "StatRegion::SetInt rollback",
                    rollbackErrors);
            }

            if (setIntUnsignedPatchAttempted)
            {
                TryRollback(
                    processHandle,
                    setIntUnsignedAddress,
                    OriginalSetIntPrologue,
                    "TextRegion::SetInt unsigned detour",
                    rollbackErrors);
                TryFlushRollback(
                    processHandle,
                    setIntUnsignedAddress,
                    OriginalSetIntPrologue.Length,
                    "TextRegion::SetInt unsigned rollback",
                    rollbackErrors);
            }

            if (setIntSignedPatchAttempted)
            {
                TryRollback(
                    processHandle,
                    setIntSignedAddress,
                    OriginalSetIntPrologue,
                    "TextRegion::SetInt signed detour",
                    rollbackErrors);
                TryFlushRollback(
                    processHandle,
                    setIntSignedAddress,
                    OriginalSetIntPrologue.Length,
                    "TextRegion::SetInt signed rollback",
                    rollbackErrors);
            }

            if (spellRegionPatchAttempted)
            {
                TryRollback(
                    processHandle,
                    spellRegionAddress,
                    OriginalSpellRegionPrologue,
                    "SpellRegion::Update detour",
                    rollbackErrors);
                TryFlushRollback(
                    processHandle,
                    spellRegionAddress,
                    OriginalSpellRegionPrologue.Length,
                    "SpellRegion::Update rollback",
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
        return BuildRemoteCode(remoteCodeAddress, PublicProfile);
    }

    internal static byte[] BuildRemoteCode(
        IntPtr remoteCodeAddress,
        NativeClientDddAccelerationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
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
        var originalContinuation = Address(
            profile.PreferredImageBase + profile.UseTimeRva + 6);
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
        if (statusEnd > SpellRegionWrapperOffset)
        {
            throw new InvalidOperationException(
                "The download-status wrapper overlaps the SpellRegion wrapper.");
        }

        DownloadStatusDrainWrapper.CopyTo(code, DownloadStatusWrapperOffset);

        var spellRegionAddress = Add(remoteCodeAddress, SpellRegionWrapperOffset);
        var spellRegionWrapper = BuildSpellRegionWrapper(spellRegionAddress, profile);
        PlaceWrapperAndTrampoline(
            code,
            SpellRegionWrapperOffset,
            SetIntSignedWrapperOffset,
            SetIntSignedWrapperOffset,
            spellRegionWrapper,
            [],
            "The SpellRegion::Update wrapper overlaps the signed SetInt wrapper.");

        var numbers = profile.XpLabel;
        PlaceStolenSlot(
            code,
            remoteCodeAddress,
            SetIntSignedWrapperOffset,
            SetIntSignedTrampolineOffset,
            SetIntUnsignedWrapperOffset,
            BuildSetIntWrapper(
                Add(remoteCodeAddress, SetIntSignedWrapperOffset),
                Add(remoteCodeAddress, SetIntSignedTrampolineOffset),
                signed: true),
            numbers.OriginalSetIntSignedPrologue,
            Address(profile.PreferredImageBase + numbers.SetIntSignedRva + StolenDetourLength),
            "signed TextRegion::SetInt");
        PlaceStolenSlot(
            code,
            remoteCodeAddress,
            SetIntUnsignedWrapperOffset,
            SetIntUnsignedTrampolineOffset,
            StatRegionWrapperOffset,
            BuildSetIntWrapper(
                Add(remoteCodeAddress, SetIntUnsignedWrapperOffset),
                Add(remoteCodeAddress, SetIntUnsignedTrampolineOffset),
                signed: false),
            numbers.OriginalSetIntUnsignedPrologue,
            Address(profile.PreferredImageBase + numbers.SetIntUnsignedRva + StolenDetourLength),
            "unsigned TextRegion::SetInt");
        PlaceStolenSlot(
            code,
            remoteCodeAddress,
            StatRegionWrapperOffset,
            StatRegionTrampolineOffset,
            InfoBoxWrapperOffset,
            BuildStatRegionWrapper(
                Add(remoteCodeAddress, StatRegionWrapperOffset),
                Add(remoteCodeAddress, StatRegionTrampolineOffset)),
            numbers.OriginalStatRegionPrologue,
            Address(profile.PreferredImageBase + numbers.StatRegionSetIntRva + StolenDetourLength),
            "StatRegion::SetInt");
        PlaceStolenSlot(
            code,
            remoteCodeAddress,
            InfoBoxWrapperOffset,
            InfoBoxTrampolineOffset,
            AllegPanelWrapperOffset,
            BuildInfoBoxWrapper(
                Add(remoteCodeAddress, InfoBoxWrapperOffset),
                Add(remoteCodeAddress, InfoBoxTrampolineOffset),
                Address(profile.PreferredImageBase + numbers.InfoBoxSetParentTailRva)),
            numbers.OriginalInfoBoxPrologue,
            Address(profile.PreferredImageBase + numbers.InfoBoxSetAvailableRva + InfoBoxStolenLength),
            "InfoBox::SetAvailable");
        PlaceStolenSlot(
            code,
            remoteCodeAddress,
            AllegPanelWrapperOffset,
            AllegPanelTrampolineOffset,
            code.Length,
            BuildAllegPanelWrapper(
                Add(remoteCodeAddress, AllegPanelWrapperOffset),
                Add(remoteCodeAddress, AllegPanelTrampolineOffset)),
            OriginalAllegPanelPrologue,
            Address(
                profile.PreferredImageBase + numbers.AllegPanelSetXpChangeRva + StolenDetourLength),
            "AllegPanel::SetXPChange");

        return code;
    }

    private static void PlaceWrapperAndTrampoline(
        byte[] code,
        int wrapperOffset,
        int trampolineOffset,
        int limitOffset,
        byte[] wrapper,
        byte[] trampoline,
        string overlapMessage)
    {
        if (wrapperOffset + wrapper.Length > trampolineOffset)
        {
            throw new InvalidOperationException(overlapMessage);
        }

        wrapper.CopyTo(code, wrapperOffset);
        if (trampoline.Length == 0)
        {
            return;
        }

        if (trampolineOffset + trampoline.Length > limitOffset)
        {
            throw new InvalidOperationException(overlapMessage);
        }

        trampoline.CopyTo(code, trampolineOffset);
    }

    private static void PlaceStolenSlot(
        byte[] code,
        IntPtr remoteCodeAddress,
        int wrapperOffset,
        int trampolineOffset,
        int limitOffset,
        byte[] wrapper,
        byte[] stolenPrologue,
        IntPtr continuation,
        string name)
    {
        var trampoline = BuildStolenTrampoline(
            Add(remoteCodeAddress, trampolineOffset),
            stolenPrologue,
            continuation);
        PlaceWrapperAndTrampoline(
            code,
            wrapperOffset,
            trampolineOffset,
            limitOffset,
            wrapper,
            trampoline,
            $"The {name} wrapper overlaps its trampoline.");
        if (trampolineOffset + trampoline.Length > limitOffset)
        {
            throw new InvalidOperationException(
                $"The {name} trampoline overlaps the next remote-code slot.");
        }
    }

    private static byte[] BuildSpellRegionWrapper(
        IntPtr wrapperAddress,
        NativeClientDddAccelerationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException(
                "The SpellRegion::Update wrapper requires a little-endian host.");
        }
        var hook = profile.SpellRegion;
        var imageBase = profile.PreferredImageBase;
        var playerModule = Address(imageBase + hook.PlayerModuleRva);
        var timer = Address(imageBase + hook.TimerCurTimeRva);
        var spellDuration = Address(imageBase + hook.SpellDurationRva);
        var ftol2 = Address(imageBase + hook.Ftol2Rva);
        var getText = Address(imageBase + hook.GetTextRva);
        var setText = Address(imageBase + hook.SetTextRva);
        var sprintfIat = Address(imageBase + hook.SprintfIatRva);

        var code = new X86CodeBuilder();
        var absoluteFixups = new List<(int Offset, IntPtr Value)>();
        var relativeCalls = new List<(int DisplacementOffset, IntPtr Target)>();

        void EmitAbsolute(IntPtr value)
        {
            absoluteFixups.Add((code.Position, value));
            code.Emit(0, 0, 0, 0);
        }

        void EmitCall(IntPtr target)
        {
            code.Emit(0xE8);
            relativeCalls.Add((code.Position, target));
            code.Emit(0, 0, 0, 0);
        }

        code.Emit(0x55);                                      // push ebp
        code.Emit(0x8B, 0xEC);                                // mov ebp, esp
        code.Emit(0x83, 0xEC, 0x30);                          // sub esp, 30h
        code.Emit(0x56);                                      // push esi
        code.Emit(0x57);                                      // push edi
        code.Emit(0x8B, 0xF1);                                // mov esi, ecx

        code.Emit(0xB9);                                      // mov ecx, UI::playerModule
        EmitAbsolute(playerModule);
        EmitCall(spellDuration);
        code.Emit(0x85, 0xC0);                                // test eax, eax
        code.JumpIf(0x84, "done");                           // jz done

        code.Emit(0x83, 0xBE, 0xD4, 0, 0, 0, 0);              // cmp hasDuration, 0
        code.JumpIf(0x84, "done");                           // jz done

        code.Emit(0xDD, 0x86, 0xC8, 0, 0, 0);                 // fld endTime
        code.Emit(0xDC, 0x25);                                // fsub Timer::cur_time
        EmitAbsolute(timer);
        code.Emit(0xDC, 0x15);                                // fcom qword ptr [zero]
        var fcomZero = code.Position;
        code.Emit(0, 0, 0, 0);
        code.Emit(0xDF, 0xE0);                                // fnstsw ax
        code.Emit(0xF6, 0xC4, 0x05);                          // test ah, 5
        code.JumpIf(0x8A, "keep");                           // jp keep
        code.Emit(0xC7, 0x86, 0xD4, 0, 0, 0, 0, 0, 0, 0);    // hasDuration = 0
        code.Emit(0xDD, 0xD8);                                // fstp st(0)
        code.Jump("done");

        code.Mark("keep");
        code.Emit(0xDC, 0x0D);                                // fmul 1/60
        var fmulInv60 = code.Position;
        code.Emit(0, 0, 0, 0);
        code.Emit(0xDC, 0x0D);                                // fmul 100
        var fmulHundred = code.Position;
        code.Emit(0, 0, 0, 0);
        EmitCall(ftol2);
        code.Emit(0x99);                                      // cdq
        code.Emit(0xB9, 0x64, 0, 0, 0);                       // mov ecx, 100
        code.Emit(0xF7, 0xF9);                                // idiv ecx
        code.Emit(0x89, 0x55, 0xDC);                          // mov [ebp-24h], edx
        code.Emit(0xDB, 0x45, 0xDC);                          // fild [ebp-24h]
        code.Emit(0x8B, 0xF8);                                // mov edi, eax
        code.Emit(0xDC, 0x0D);                                // fmul 0.01
        var fmulHundredth = code.Position;
        code.Emit(0, 0, 0, 0);
        code.Emit(0xDC, 0x0D);                                // fmul 60
        var fmulSixty = code.Position;
        code.Emit(0, 0, 0, 0);
        EmitCall(ftol2);
        code.Emit(0x83, 0xF8, 0x0A);                          // cmp eax, 10
        code.Emit(0x50);                                      // push seconds
        code.Emit(0x57);                                      // push minutes
        code.JumpIf(0x8D, "unpadded");                       // jge unpadded
        code.Emit(0x68);                                      // push "%d:0%d"
        var pushPadded = code.Position;
        code.Emit(0, 0, 0, 0);
        code.Jump("pushbuf");

        code.Mark("unpadded");
        code.Emit(0x68);                                      // push "%d:%d"
        var pushUnpadded = code.Position;
        code.Emit(0, 0, 0, 0);

        code.Mark("pushbuf");
        code.Emit(0x8D, 0x45, 0xE0);                          // lea eax, [ebp-20h]
        code.Emit(0x50);                                      // push eax
        code.Emit(0xFF, 0x15);                                // call dword ptr [sprintf]
        EmitAbsolute(sprintfIat);
        code.Emit(0x83, 0xC4, 0x10);                          // add esp, 10h

        code.Emit(0x8B, 0x8E, 0xC4, 0, 0, 0);                 // mov ecx, duration_txt
        EmitCall(getText);
        code.Emit(0x8D, 0x55, 0xE0);                          // lea edx, [ebp-20h]
        code.Mark("cmp_loop");
        code.Emit(0x8A, 0x08);                                // mov cl, [eax]
        code.Emit(0x3A, 0x0A);                                // cmp cl, [edx]
        code.JumpIf(0x85, "need_set");                       // jne need_set
        code.Emit(0x84, 0xC9);                                // test cl, cl
        code.JumpIf(0x84, "done");                           // jz done
        code.Emit(0x40);                                      // inc eax
        code.Emit(0x42);                                      // inc edx
        code.Jump("cmp_loop");

        code.Mark("need_set");
        code.Emit(0x6A, 0x00);                                // push 0
        code.Emit(0x6A, 0x00);                                // push 0
        code.Emit(0x6A, 0x00);                                // push 0
        code.Emit(0x8D, 0x45, 0xE0);                          // lea eax, [ebp-20h]
        code.Emit(0x50);                                      // push eax
        code.Emit(0x8B, 0x8E, 0xC4, 0, 0, 0);                 // mov ecx, duration_txt
        EmitCall(setText);
        code.Emit(0x8B, 0x86, 0xC4, 0, 0, 0);                 // mov eax, duration_txt
        code.Emit(0x8B, 0x50, 0x0C);                          // mov edx, [eax+0Ch]
        code.Emit(0x8D, 0x48, 0x0C);                          // lea ecx, [eax+0Ch]
        code.Emit(0xFF, 0x52, 0x38);                          // call [edx+38h]

        code.Mark("done");
        code.Emit(0x5F);                                      // pop edi
        code.Emit(0x5E);                                      // pop esi
        code.Emit(0x8B, 0xE5);                                // mov esp, ebp
        code.Emit(0x5D);                                      // pop ebp
        code.Emit(0xC3);                                      // ret

        var body = code.Build();
        var constantOffset = (body.Length + 7) & ~7;
        var data = new byte[
            constantOffset +
            SpellRegionZero.Length +
            SpellRegionInv60.Length +
            SpellRegionHundred.Length +
            SpellRegionHundredth.Length +
            SpellRegionSixty.Length +
            SpellRegionFormatPadded.Length +
            SpellRegionFormatUnpadded.Length];
        body.CopyTo(data, 0);

        var zeroOffset = constantOffset;
        var inv60Offset = zeroOffset + SpellRegionZero.Length;
        var hundredOffset = inv60Offset + SpellRegionInv60.Length;
        var hundredthOffset = hundredOffset + SpellRegionHundred.Length;
        var sixtyOffset = hundredthOffset + SpellRegionHundredth.Length;
        var paddedOffset = sixtyOffset + SpellRegionSixty.Length;
        var unpaddedOffset = paddedOffset + SpellRegionFormatPadded.Length;
        SpellRegionZero.CopyTo(data, zeroOffset);
        SpellRegionInv60.CopyTo(data, inv60Offset);
        SpellRegionHundred.CopyTo(data, hundredOffset);
        SpellRegionHundredth.CopyTo(data, hundredthOffset);
        SpellRegionSixty.CopyTo(data, sixtyOffset);
        SpellRegionFormatPadded.CopyTo(data, paddedOffset);
        SpellRegionFormatUnpadded.CopyTo(data, unpaddedOffset);

        void PatchAbsolute(int offset, IntPtr value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                data.AsSpan(offset, sizeof(uint)),
                unchecked((uint)value.ToInt64()));
        }

        foreach (var (offset, value) in absoluteFixups)
        {
            PatchAbsolute(offset, value);
        }

        PatchAbsolute(fcomZero, Add(wrapperAddress, zeroOffset));
        PatchAbsolute(fmulInv60, Add(wrapperAddress, inv60Offset));
        PatchAbsolute(fmulHundred, Add(wrapperAddress, hundredOffset));
        PatchAbsolute(fmulHundredth, Add(wrapperAddress, hundredthOffset));
        PatchAbsolute(fmulSixty, Add(wrapperAddress, sixtyOffset));
        PatchAbsolute(pushPadded, Add(wrapperAddress, paddedOffset));
        PatchAbsolute(pushUnpadded, Add(wrapperAddress, unpaddedOffset));

        foreach (var (displacementOffset, target) in relativeCalls)
        {
            WriteRelativeDisplacement(
                data,
                displacementOffset,
                Add(wrapperAddress, displacementOffset - 1),
                target);
        }

        return data;
    }

    private static byte[] BuildSetIntWrapper(
        IntPtr wrapperAddress,
        IntPtr trampolineAddress,
        bool signed)
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException(
                "The TextRegion::SetInt wrapper requires a little-endian host.");
        }

        var code = new X86CodeBuilder();
        code.Emit(0x55);                                      // push ebp
        code.Emit(0x8B, 0xEC);                                // mov ebp, esp
        code.Emit(0x56);                                      // push esi
        code.Emit(0x57);                                      // push edi
        code.Emit(0x53);                                      // push ebx
        code.Emit(0x8B, 0xF1);                                // mov esi, ecx
        code.Emit(0x33, 0xFF);                                // xor edi, edi
        code.Emit(0x8B, 0x45, 0x0C);                          // mov eax, [ebp+0Ch] font
        code.Emit(0x0B, 0x45, 0x10);                          // or eax, [ebp+10h] callback
        code.Emit(0x0B, 0x45, 0x14);                          // or eax, [ebp+14h] flag
        code.JumpIf(0x85, "original");                       // jnz original
        code.Emit(0x83, 0xBE);                                // cmp lineCount, 0
        code.Emit(BitConverter.GetBytes(TextRegionLineCountOffset));
        code.Emit(0x00);
        code.JumpIf(0x8E, "original");                       // jle original
        code.Emit(0x8B, 0x86);                                // mov eax, [esi+lineBuff]
        code.Emit(BitConverter.GetBytes(TextRegionLineBuffOffset));
        code.Emit(0x85, 0xC0);                                // test eax, eax
        code.JumpIf(0x84, "original");                       // jz original
        code.Emit(0x8B, 0x00);                                // mov eax, [eax]
        code.Emit(0x85, 0xC0);                                // test eax, eax
        code.JumpIf(0x84, "original");                       // jz original
        code.Emit(0x8B, 0x58, (byte)GlyphStringHeadOffset);    // mov ebx, [eax+4]
        code.Emit(0x85, 0xDB);                                // test ebx, ebx
        code.JumpIf(0x84, "original");                       // jz original
        code.Emit(0x8B, 0x45, 0x08);                          // mov eax, [ebp+8] value

        if (signed)
        {
            code.Emit(0x83, 0xF8, 0x00);                      // cmp eax, 0
            code.JumpIf(0x8D, "magnitude");                   // jge magnitude
            code.Emit(0x83, 0x7B, (byte)GlyphTypeOffset, 0);  // cmp [ebx+type], 0
            code.JumpIf(0x85, "original");                    // jnz original
            code.Emit(0x80, 0x7B, (byte)GlyphCharOffset, (byte)'-');
            code.JumpIf(0x85, "original");                    // jnz original
            code.Emit(0x8B, 0x1B);                            // mov ebx, [ebx]
            code.Emit(0xF7, 0xD8);                            // neg eax
        }

        code.Mark("magnitude");
        code.Emit(0x85, 0xC0);                                // test eax, eax
        code.JumpIf(0x85, "div_loop");                       // jnz div_loop
        code.Emit(0x6A, 0x00);                                // push 0
        code.Emit(0x47);                                      // inc edi
        code.Jump("cmp_loop");

        code.Mark("div_loop");
        code.Emit(0x33, 0xD2);                                // xor edx, edx
        code.Emit(0xB9, 0x0A, 0, 0, 0);                       // mov ecx, 10
        code.Emit(0xF7, 0xF1);                                // div ecx
        code.Emit(0x52);                                      // push edx
        code.Emit(0x47);                                      // inc edi
        code.Emit(0x85, 0xC0);                                // test eax, eax
        code.JumpIf(0x85, "div_loop");                       // jnz div_loop

        code.Mark("cmp_loop");
        code.Emit(0x85, 0xFF);                                // test edi, edi
        code.JumpIf(0x84, "trail");                          // jz trail
        code.Emit(0x85, 0xDB);                                // test ebx, ebx
        code.JumpIf(0x84, "original");                       // jz original
        code.Emit(0x83, 0x7B, (byte)GlyphTypeOffset, 0);      // cmp [ebx+type], 0
        code.JumpIf(0x85, "original");                       // jnz original
        code.Emit(0x58);                                      // pop eax
        code.Emit(0x4F);                                      // dec edi
        code.Emit(0x04, (byte)'0');                           // add al, '0'
        code.Emit(0x38, 0x43, (byte)GlyphCharOffset);         // cmp [ebx+char], al
        code.JumpIf(0x85, "original");                       // jnz original
        code.Emit(0x8B, 0x1B);                                // mov ebx, [ebx]
        code.Jump("cmp_loop");

        code.Mark("trail");
        code.Emit(0x85, 0xDB);                                // test ebx, ebx
        code.JumpIf(0x84, "equal");                          // jz equal
        code.Emit(0x83, 0x7B, (byte)GlyphTypeOffset, 0);      // cmp [ebx+type], 0
        code.JumpIf(0x85, "original");                       // jnz original
        code.Emit(0x80, 0x7B, (byte)GlyphCharOffset, 0x0A);   // cmp [ebx+char], '\n'
        code.JumpIf(0x85, "original");                       // jnz original
        code.Emit(0x8B, 0x1B);                                // mov ebx, [ebx]
        code.Jump("trail");

        code.Mark("equal");
        code.Emit(0x5B);                                      // pop ebx
        code.Emit(0x5F);                                      // pop edi
        code.Emit(0x5E);                                      // pop esi
        code.Emit(0x5D);                                      // pop ebp
        code.Emit(0xC2, 0x10, 0x00);                          // ret 10h

        code.Mark("original");
        code.Emit(0x85, 0xFF);                                // test edi, edi
        code.JumpIf(0x84, "restore");                        // jz restore
        code.Emit(0x58);                                      // pop eax
        code.Emit(0x4F);                                      // dec edi
        code.Jump("original");

        code.Mark("restore");
        code.Emit(0x8B, 0xCE);                                // mov ecx, esi
        code.Emit(0x5B);                                      // pop ebx
        code.Emit(0x5F);                                      // pop edi
        code.Emit(0x5E);                                      // pop esi
        code.Emit(0x5D);                                      // pop ebp
        code.Emit(0xE9);                                      // jmp trampoline
        var trampolineDisp = code.Position;
        code.Emit(0, 0, 0, 0);

        var wrapper = code.Build();
        WriteRelativeDisplacement(
            wrapper,
            trampolineDisp,
            Add(wrapperAddress, trampolineDisp - 1),
            trampolineAddress);
        return wrapper;
    }

    private static byte[] BuildStatRegionWrapper(
        IntPtr wrapperAddress,
        IntPtr trampolineAddress)
    {
        var code = new X86CodeBuilder();
        code.Emit(0x83, 0x7C, 0x24, 0x04, (byte)TotalExperienceQuality);
        code.JumpIf(0x85, "original");                       // jnz original
        code.Emit(0x8B, 0x81);                                // mov eax, [ecx+iXPTotal]
        code.Emit(BitConverter.GetBytes(StatRegionXpTotalOffset));
        code.Emit(0x3B, 0x44, 0x24, 0x08);                    // cmp eax, [esp+8]
        code.JumpIf(0x85, "original");                       // jne original
        code.Emit(0x8B, 0x91);                                // mov edx, [ecx+xp_total]
        code.Emit(BitConverter.GetBytes(StatRegionXpTotalWidgetOffset));
        code.Emit(0x85, 0xD2);                                // test edx, edx
        code.JumpIf(0x84, "original");                       // jz original
        code.Emit(0x83, 0xBA);                                // cmp [edx+lineCount], 0
        code.Emit(BitConverter.GetBytes(TextRegionLineCountOffset));
        code.Emit(0x00);
        code.JumpIf(0x8E, "original");                       // jle original
        code.Emit(0xC2, 0x08, 0x00);                          // ret 8

        code.Mark("original");
        code.Emit(0xE9);                                      // jmp trampoline
        var trampolineDisp = code.Position;
        code.Emit(0, 0, 0, 0);

        var wrapper = code.Build();
        WriteRelativeDisplacement(
            wrapper,
            trampolineDisp,
            Add(wrapperAddress, trampolineDisp - 1),
            trampolineAddress);
        return wrapper;
    }

    private static byte[] BuildInfoBoxWrapper(
        IntPtr wrapperAddress,
        IntPtr trampolineAddress,
        IntPtr setParentTail)
    {
        var code = new X86CodeBuilder();
        code.Emit(0x8B, 0x44, 0x24, 0x04);                    // mov eax, [esp+4]
        code.Emit(0x3B, 0x81);                                // cmp eax, [ecx+iAvailable]
        code.Emit(BitConverter.GetBytes(InfoBoxAvailableOffset));
        code.JumpIf(0x85, "original");                       // jne original
        code.Emit(0x51);                                      // push ecx
        code.Emit(0x56);                                      // push esi
        code.Emit(0x8B, 0xF1);                                // mov esi, ecx
        code.Emit(0x57);                                      // push edi
        code.Emit(0xE9);                                      // jmp SetParent tail
        var tailDisp = code.Position;
        code.Emit(0, 0, 0, 0);

        code.Mark("original");
        code.Emit(0xE9);                                      // jmp trampoline
        var trampolineDisp = code.Position;
        code.Emit(0, 0, 0, 0);

        var wrapper = code.Build();
        WriteRelativeDisplacement(
            wrapper,
            tailDisp,
            Add(wrapperAddress, tailDisp - 1),
            setParentTail);
        WriteRelativeDisplacement(
            wrapper,
            trampolineDisp,
            Add(wrapperAddress, trampolineDisp - 1),
            trampolineAddress);
        return wrapper;
    }

    private static byte[] BuildStolenTrampoline(
        IntPtr trampolineAddress,
        byte[] stolenPrologue,
        IntPtr continuation)
    {
        ArgumentNullException.ThrowIfNull(stolenPrologue);
        var trampoline = new byte[stolenPrologue.Length + 5];
        stolenPrologue.CopyTo(trampoline, 0);
        trampoline[stolenPrologue.Length] = 0xE9;
        WriteRelativeDisplacement(
            trampoline,
            stolenPrologue.Length + 1,
            Add(trampolineAddress, stolenPrologue.Length),
            continuation);
        return trampoline;
    }

    private static byte[] BuildAllegPanelWrapper(
        IntPtr wrapperAddress,
        IntPtr trampolineAddress)
    {
        var code = new X86CodeBuilder();
        code.Emit(0x83, 0xB9, (byte)AllegPanelPatronIdOffset, 0, 0, 0, 0);
        code.JumpIf(0x85, "has_patron");                     // jnz has_patron
        code.Emit(0x8B, 0x81, (byte)AllegPanelXpAvailOffset, 0, 0, 0);
        code.Emit(0x3B, 0x44, 0x24, 0x04);                    // cmp eax, [esp+4]
        code.JumpIf(0x85, "original");                       // jne original
        code.Emit(0x8B, 0x81, (byte)AllegPanelLevelOffset, 0, 0, 0);
        code.Emit(0x3B, 0x44, 0x24, 0x08);                    // cmp eax, [esp+8]
        code.JumpIf(0x85, "original");                       // jne original
        code.Emit(0xC2, 0x0C, 0x00);                          // ret 0Ch

        code.Mark("has_patron");
        code.Emit(0x8B, 0x44, 0x24, 0x04);                    // mov eax, [esp+4]
        code.Emit(0x89, 0x81, (byte)AllegPanelXpAvailOffset, 0, 0, 0);
        code.Emit(0x8B, 0x44, 0x24, 0x08);                    // mov eax, [esp+8]
        code.Emit(0x89, 0x81, (byte)AllegPanelLevelOffset, 0, 0, 0);
        code.Emit(0xC2, 0x0C, 0x00);                          // ret 0Ch

        code.Mark("original");
        code.Emit(0xE9);                                      // jmp trampoline
        var trampolineDisp = code.Position;
        code.Emit(0, 0, 0, 0);

        var wrapper = code.Build();
        WriteRelativeDisplacement(
            wrapper,
            trampolineDisp,
            Add(wrapperAddress, trampolineDisp - 1),
            trampolineAddress);
        return wrapper;
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
        if (OriginalUseTimePrologue.Length != OriginalSpellRegionPrologue.Length)
        {
            throw new InvalidOperationException(
                "UseTime and SpellRegion prologues must stay the same JMP+NOP width.");
        }

        var patch = new byte[OriginalUseTimePrologue.Length];
        patch[0] = 0xE9;
        WriteRelativeDisplacement(patch, 1, useTimeAddress, wrapperAddress);
        patch[5] = 0x90;
        return patch;
    }

    internal static byte[] BuildStolenDetourPatch(IntPtr sourceAddress, IntPtr wrapperAddress)
    {
        var patch = Enumerable.Repeat((byte)0x90, StolenDetourLength).ToArray();
        patch[0] = 0xE9;
        WriteRelativeDisplacement(patch, 1, sourceAddress, wrapperAddress);
        return patch;
    }

    internal static byte[] BuildNearJumpPatch(IntPtr sourceAddress, IntPtr wrapperAddress)
    {
        var patch = new byte[InfoBoxStolenLength];
        patch[0] = 0xE9;
        WriteRelativeDisplacement(patch, 1, sourceAddress, wrapperAddress);
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
        (byte[])PublicExpectedUseTimeSignature.Clone();

    internal static NativeClientDddAccelerationProfile PublicProfileForTest() =>
        PublicProfile;

    internal static NativeClientDddAccelerationProfile AdminProfileForTest() =>
        AdminProfile;

    internal static NativeClientDddAccelerationProfile IdentifySupportedProfileForTest(
        long size,
        string sha256) =>
        IdentifySupportedProfile(size, sha256)
        ?? throw new InvalidDataException(
            $"No verified A09 client profile matches {size:N0} bytes / SHA-256 {sha256}.");

    internal static byte[] DownloadStatusSignatureForTest() =>
        (byte[])ExpectedDownloadStatusSignature.Clone();

    internal static byte[] OriginalUseTimePrologueForTest() =>
        (byte[])OriginalUseTimePrologue.Clone();

    internal static byte[] OriginalDownloadStatusTailForTest() =>
        (byte[])OriginalDownloadStatusTail.Clone();

    internal static byte[] SpellRegionSignatureForTest() =>
        (byte[])PublicExpectedSpellRegionSignature.Clone();

    internal static byte[] OriginalSpellRegionPrologueForTest() =>
        (byte[])OriginalSpellRegionPrologue.Clone();

    internal static byte[] SpellRegionWrapperForTest(IntPtr remoteCodeAddress) =>
        BuildSpellRegionWrapper(
            Add(remoteCodeAddress, SpellRegionWrapperOffset),
            PublicProfile);

    internal static int SpellRegionWrapperLengthForTest(IntPtr remoteCodeAddress) =>
        SpellRegionWrapperForTest(remoteCodeAddress).Length;

    internal static byte[] OriginalSetIntPrologueForTest() =>
        (byte[])OriginalSetIntPrologue.Clone();

    internal static byte[] OriginalInfoBoxPrologueForTest() =>
        (byte[])OriginalInfoBoxPrologue.Clone();

    internal static byte[] OriginalAllegPanelPrologueForTest() =>
        (byte[])OriginalAllegPanelPrologue.Clone();

    internal static byte[] SetIntSignedSignatureForTest() =>
        (byte[])PublicExpectedSetIntSignedSignature.Clone();

    internal static byte[] SetIntUnsignedSignatureForTest() =>
        (byte[])PublicExpectedSetIntUnsignedSignature.Clone();

    internal static byte[] StatRegionSignatureForTest() =>
        (byte[])PublicExpectedStatRegionSignature.Clone();

    internal static byte[] InfoBoxSignatureForTest() =>
        (byte[])PublicExpectedInfoBoxSignature.Clone();

    internal static byte[] SetIntSignedWrapperForTest(IntPtr remoteCodeAddress) =>
        BuildSetIntWrapper(
            Add(remoteCodeAddress, SetIntSignedWrapperOffset),
            Add(remoteCodeAddress, SetIntSignedTrampolineOffset),
            signed: true);

    internal static byte[] SetIntUnsignedWrapperForTest(IntPtr remoteCodeAddress) =>
        BuildSetIntWrapper(
            Add(remoteCodeAddress, SetIntUnsignedWrapperOffset),
            Add(remoteCodeAddress, SetIntUnsignedTrampolineOffset),
            signed: false);

    internal static byte[] StatRegionWrapperForTest(IntPtr remoteCodeAddress) =>
        BuildStatRegionWrapper(
            Add(remoteCodeAddress, StatRegionWrapperOffset),
            Add(remoteCodeAddress, StatRegionTrampolineOffset));

    internal static byte[] InfoBoxWrapperForTest(IntPtr remoteCodeAddress) =>
        BuildInfoBoxWrapper(
            Add(remoteCodeAddress, InfoBoxWrapperOffset),
            Add(remoteCodeAddress, InfoBoxTrampolineOffset),
            Address(PreferredImageBase + InfoBoxSetParentTailRva));

    internal static byte[] AllegPanelWrapperForTest(IntPtr remoteCodeAddress) =>
        BuildAllegPanelWrapper(
            Add(remoteCodeAddress, AllegPanelWrapperOffset),
            Add(remoteCodeAddress, AllegPanelTrampolineOffset));

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

    internal static NativeClientDddAccelerationProfile ResolveSupportedClientProfile(
        string clientPath)
    {
        var file = new FileInfo(clientPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("Missing client.exe.", clientPath);
        }

        var sizeCandidates = SupportedProfiles
            .Where(profile => profile.ExpectedSize == file.Length)
            .ToArray();
        if (sizeCandidates.Length == 0)
        {
            throw new InvalidDataException(
                $"client.exe is {file.Length:N0} bytes; accelerated DAT repair requires the " +
                "exact verified public or Aetherium admin DM client.");
        }

        using var stream = new FileStream(
            clientPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream));
        var profile = IdentifySupportedProfile(file.Length, actualHash);
        if (profile is null)
        {
            throw new InvalidDataException(
                $"client.exe SHA-256 is {actualHash}; accelerated DAT repair requires the " +
                $"exact verified {string.Join(" or ", sizeCandidates.Select(candidate => candidate.Id))} " +
                "client image.");
        }

        return profile;
    }

    private static NativeClientDddAccelerationProfile? IdentifySupportedProfile(
        long size,
        string sha256) =>
        SupportedProfiles.SingleOrDefault(profile =>
            profile.ExpectedSize == size &&
            profile.ExpectedSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase));

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
