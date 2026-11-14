using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcLegacyLauncher;

internal sealed class AethTankRelease
{
    internal AethTankRelease(AethTankManifestPayload payload, byte[] envelope)
    {
        Payload = payload;
        Envelope = envelope.ToArray();
    }

    public string Version => Payload.Version;
    public long PackageSize => Payload.PackageSize;
    public string Protection => Payload.Protection;
    internal AethTankManifestPayload Payload { get; }
    internal byte[] Envelope { get; }
}

internal sealed class AethTankManifestEnvelope
{
    public int SchemaVersion { get; set; }
    public string KeyId { get; set; } = "";
    public string PayloadBase64 { get; set; } = "";
    public string SignatureBase64 { get; set; } = "";
}

internal sealed class AethTankManifestPayload
{
    public int SchemaVersion { get; set; }
    public string Product { get; set; } = "";
    public string Version { get; set; } = "";
    public string Channel { get; set; } = "";
    public string ReleaseStatus { get; set; } = "";
    public string PublishedUtc { get; set; } = "";
    public string MinLauncherVersion { get; set; } = "";
    public List<AethTankManifestClient> Clients { get; set; } = [];
    public string Architecture { get; set; } = "";
    public string Protection { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string PackageSha256 { get; set; } = "";
    public long PackageSize { get; set; }
    public List<AethTankManifestFile> Files { get; set; } = [];
    public string DllSignerThumbprint { get; set; } = "";
}

internal sealed class AethTankManifestClient
{
    public string Profile { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

internal sealed class AethTankManifestFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

internal static class AethTankManifest
{
    internal const int MaximumEnvelopeBytes = 64 * 1024;
    internal const long MaximumPackageBytes = 32L * 1024 * 1024;
    internal const string PublicClientProfile = NativeClientDddAcceleration.PublicProfileId;
    internal const string PublicClientSha256 = CommunityClientBootstrap.ExpectedSha256;
    internal const string AdminClientProfile = NativeClientDddAcceleration.AdminProfileId;
    internal const string AdminClientSha256 = NativeClientDddAcceleration.AdminExpectedSha256;
    internal static readonly IReadOnlyDictionary<string, string> SupportedClients =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PublicClientProfile] = PublicClientSha256,
            [AdminClientProfile] = AdminClientSha256,
        };
    internal static readonly string[] AllowedFiles = ["aeth-tank.dll", "README.txt", "LICENSE.txt"];
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        WriteIndented = true,
    };

    internal static AethTankRelease Verify(
        ReadOnlySpan<byte> envelopeBytes, AethTankAddonOptions options)
    {
        if (envelopeBytes.Length == 0 || envelopeBytes.Length > MaximumEnvelopeBytes)
            throw new InvalidDataException("The Aeth-Tank release manifest has an invalid size.");
        RejectDuplicateProperties(envelopeBytes);
        var envelope = JsonSerializer.Deserialize<AethTankManifestEnvelope>(envelopeBytes, Json)
            ?? throw new InvalidDataException("The Aeth-Tank release envelope is missing.");
        if (envelope.SchemaVersion != 1 || string.IsNullOrWhiteSpace(envelope.KeyId) ||
            !options.PublicKeys.TryGetValue(envelope.KeyId, out var publicKey))
            throw new InvalidDataException("This launcher does not trust the Aeth-Tank release signing key.");
        if (string.IsNullOrWhiteSpace(envelope.PayloadBase64) || string.IsNullOrWhiteSpace(envelope.SignatureBase64))
            throw new InvalidDataException("The Aeth-Tank signed release payload is missing.");
        byte[] payloadBytes, signature;
        try
        {
            payloadBytes = Convert.FromBase64String(envelope.PayloadBase64);
            signature = Convert.FromBase64String(envelope.SignatureBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("The Aeth-Tank release signature is malformed.", ex);
        }
        if (payloadBytes.Length == 0 || payloadBytes.Length > MaximumEnvelopeBytes)
            throw new InvalidDataException("The signed Aeth-Tank release payload has an invalid size.");
        using var key = RSA.Create();
        key.ImportFromPem(publicKey);
        if (key.KeySize < 2048 || !key.VerifyData(
                payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("The Aeth-Tank release signature did not verify.");
        RejectDuplicateProperties(payloadBytes);
        var payload = JsonSerializer.Deserialize<AethTankManifestPayload>(payloadBytes, Json)
            ?? throw new InvalidDataException("The Aeth-Tank release description is missing.");
        ValidatePayload(payload, options);
        return new AethTankRelease(payload, envelopeBytes.ToArray());
    }

    private static void ValidatePayload(AethTankManifestPayload p, AethTankAddonOptions options)
    {
        if (new[] { p.Product, p.Version, p.Channel, p.ReleaseStatus, p.PublishedUtc,
                p.MinLauncherVersion, p.Architecture,
                p.Protection, p.PackageUrl, p.PackageSha256 }
            .Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("A required Aeth-Tank release field is missing.");
        if (p.SchemaVersion != 2 || p.Product != "aeth-tank" || p.Channel != "private" ||
            p.ReleaseStatus != "distributable" || p.Architecture != "x86" ||
            p.Protection is not ("vmprotect" or "themida" or "xollvm") ||
            !ValidClients(p.Clients))
            throw new InvalidDataException("This is not a supported protected Aeth-Tank release.");
        var version = ParseVersion(p.Version);
        var minimum = ParseVersion(p.MinLauncherVersion);
        if (options.LauncherVersion < minimum)
            throw new InvalidDataException(
                $"Aeth-Tank {p.Version} requires Aetherium Play {p.MinLauncherVersion} or later.");
        if (!DateTimeOffset.TryParse(p.PublishedUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var published) ||
            published.Offset != TimeSpan.Zero || published > DateTimeOffset.UtcNow.AddMinutes(10))
            throw new InvalidDataException("The Aeth-Tank release publication time is invalid.");
        ValidateDownloadUri(p.PackageUrl, options.DownloadHosts);
        if (p.PackageSize <= 0 || p.PackageSize > MaximumPackageBytes ||
            !IsHex(p.PackageSha256, 64) || p.DllSignerThumbprint is null ||
            p.DllSignerThumbprint.Length != 0 && !IsHex(p.DllSignerThumbprint, 40))
            throw new InvalidDataException("The Aeth-Tank package identity is invalid.");
        if (p.Files is null || p.Files.Count != AllowedFiles.Length)
            throw new InvalidDataException("The Aeth-Tank release must contain exactly the approved runtime files.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in p.Files)
        {
            if (file is null || !AllowedFiles.Contains(file.Path, StringComparer.Ordinal) ||
                !names.Add(file.Path) || file.Size <= 0 || file.Size > MaximumPackageBytes ||
                !IsHex(file.Sha256, 64))
                throw new InvalidDataException("The Aeth-Tank release contains an unapproved file description.");
            total = checked(total + file.Size);
        }
        if (total > MaximumPackageBytes)
            throw new InvalidDataException("The unpacked Aeth-Tank release is too large.");
    }

    internal static bool SupportsClient(AethTankManifestPayload payload, string profile) =>
        payload.Clients.Any(client => string.Equals(client.Profile, profile, StringComparison.Ordinal)
            && SupportedClients.TryGetValue(profile, out var expected)
            && string.Equals(client.Sha256, expected, StringComparison.OrdinalIgnoreCase));

    private static bool ValidClients(List<AethTankManifestClient>? clients)
    {
        if (clients is null || clients.Count != SupportedClients.Count) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var client in clients)
        {
            if (client is null || !seen.Add(client.Profile) ||
                !SupportedClients.TryGetValue(client.Profile, out var expected) ||
                !string.Equals(client.Sha256, expected, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return seen.SetEquals(SupportedClients.Keys);
    }

    internal static Version ParseVersion(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 32 || text.Count(c => c == '.') != 2 ||
            text.Any(c => c != '.' && !char.IsAsciiDigit(c)) ||
            !Version.TryParse(text, out var version) || version.Major > 65535 ||
            version.Minor > 65535 || version.Build > 65535 || version.Revision != -1 ||
            text != $"{version.Major}.{version.Minor}.{version.Build}")
            throw new InvalidDataException("Aeth-Tank versions must use three canonical numeric components.");
        return version;
    }

    internal static Uri ValidateDownloadUri(string text, IReadOnlySet<string> allowedHosts)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort || uri.IsLoopback ||
            !allowedHosts.Contains(uri.IdnHost) || IPAddress.TryParse(uri.IdnHost, out _))
            throw new InvalidDataException("The Aeth-Tank download address is not an approved HTTPS endpoint.");
        return uri;
    }

    internal static bool IsHex(string? text, int length) =>
        text is not null && text.Length == length && text.All(Uri.IsHexDigit);

    internal static void VerifyFile(string path, AethTankManifestFile expected)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expected.Size ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"The installed Aeth-Tank file {expected.Path} has changed.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var digest = Convert.ToHexString(SHA256.HashData(input));
        if (!digest.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The Aeth-Tank file {expected.Path} failed its SHA-256 check.");
    }

    internal static void RejectDuplicateProperties(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 8 });
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                objects.Push(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
                objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName &&
                !objects.Peek().Add(reader.GetString()!))
                throw new InvalidDataException("Duplicate JSON fields are not accepted in Aeth-Tank metadata.");
        }
    }
}

internal static class AethTankBinaryVerifier
{
    internal static void Verify(string path, AethTankManifestPayload release)
    {
        VerifyNativeImage(path);
        var expected = AethTankManifest.ParseVersion(release.Version);
        var info = FileVersionInfo.GetVersionInfo(path);
        if (info.FileMajorPart != expected.Major || info.FileMinorPart != expected.Minor ||
            info.FileBuildPart != expected.Build || info.FilePrivatePart != 0 ||
            info.ProductMajorPart != expected.Major || info.ProductMinorPart != expected.Minor ||
            info.ProductBuildPart != expected.Build || info.ProductPrivatePart != 0)
            throw new InvalidDataException("The Aeth-Tank DLL version does not match its signed release.");
        // The signed release envelope already binds the package and exact DLL
        // SHA-256. Authenticode is optional defense-in-depth for publishers who
        // have a commercial certificate; it is not required for private releases.
        if (!string.IsNullOrEmpty(release.DllSignerThumbprint))
            VerifyAuthenticode(path, release.DllSignerThumbprint);
    }

    internal static void VerifyNativeImage(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var pe = new PEReader(input);
        var headers = pe.PEHeaders;
        if (headers.CoffHeader.Machine != Machine.I386 ||
            (headers.CoffHeader.Characteristics & Characteristics.Dll) == 0 ||
            headers.PEHeader is not { Magic: PEMagic.PE32 } optional ||
            optional.CorHeaderTableDirectory.Size != 0 ||
            optional.ExportTableDirectory.Size < 40)
            throw new InvalidDataException("Aeth-Tank must be a native x86 Windows DLL.");
        var directory = optional.ExportTableDirectory;
        var exports = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent();
        if (exports.Length < 40)
            throw new InvalidDataException("The Aeth-Tank export directory is incomplete.");
        var span = exports.AsSpan();
        var functionCount = BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);
        var nameCount = BinaryPrimitives.ReadUInt32LittleEndian(span[24..]);
        if (functionCount != 1 || nameCount != 1)
            throw new InvalidDataException("The Aeth-Tank DLL must expose only CreateACPlugin.");
        var functionRva = ReadU32(pe, BinaryPrimitives.ReadInt32LittleEndian(span[28..]));
        var nameRva = ReadU32(pe, BinaryPrimitives.ReadInt32LittleEndian(span[32..]));
        var ordinalBytes = pe.GetSectionData(BinaryPrimitives.ReadInt32LittleEndian(span[36..])).GetContent();
        if (ordinalBytes.Length < 2 || BinaryPrimitives.ReadUInt16LittleEndian(ordinalBytes.AsSpan()) != 0)
            throw new InvalidDataException("The Aeth-Tank exported function ordinal is invalid.");
        var nameBytes = pe.GetSectionData(checked((int)nameRva)).GetContent();
        ReadOnlySpan<byte> expectedName = "CreateACPlugin\0"u8;
        if (nameBytes.Length < expectedName.Length || !nameBytes.AsSpan(0, expectedName.Length).SequenceEqual(expectedName))
            throw new InvalidDataException("The Aeth-Tank DLL entry point is missing.");
        if (functionRva == 0 ||
            functionRva >= (uint)directory.RelativeVirtualAddress &&
            functionRva < (ulong)directory.RelativeVirtualAddress + (uint)directory.Size ||
            !headers.SectionHeaders.Any(s =>
                (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0 &&
                functionRva >= (uint)s.VirtualAddress &&
                functionRva < (ulong)s.VirtualAddress + (uint)Math.Max(s.VirtualSize, s.SizeOfRawData)))
            throw new InvalidDataException("The Aeth-Tank entry point is not executable native code.");
    }

    private static uint ReadU32(PEReader pe, int rva)
    {
        var bytes = pe.GetSectionData(rva).GetContent();
        if (bytes.Length < 4) throw new InvalidDataException("The Aeth-Tank export table is incomplete.");
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan());
    }

    private static void VerifyAuthenticode(string path, string expectedThumbprint)
    {
        using var rawCertificate = X509Certificate.CreateFromSignedFile(path);
        using var signed = new X509Certificate2(rawCertificate);
        if (!signed.Thumbprint.Equals(expectedThumbprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Aeth-Tank DLL publisher does not match the signed release.");
        using var file = new TrustFile(path);
        var data = new WinTrustData
        {
            Size = (uint)Marshal.SizeOf<WinTrustData>(), UiChoice = 2,
            RevocationChecks = 1, UnionChoice = 1, FileInfo = file.Pointer,
            StateAction = 1, ProviderFlags = 0x80,
        };
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try
        {
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (result != 0)
                throw new InvalidDataException($"Windows could not verify the Aeth-Tank publisher signature (0x{result:X8}).");
        }
        finally
        {
            data.StateAction = 2;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    private sealed class TrustFile : IDisposable
    {
        internal IntPtr Pointer { get; }
        internal TrustFile(string path)
        {
            var value = new WinTrustFileInfo
            {
                Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), Path = path,
            };
            Pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(value, Pointer, false);
        }
        public void Dispose()
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(Pointer);
            Marshal.FreeHGlobal(Pointer);
        }
    }
}
