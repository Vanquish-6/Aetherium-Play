using System.Security.Cryptography;
using System.Text.Json;

namespace AcLegacyLauncher;

/// <summary>
/// The Unreal counterpart of the classic client hash gate. Unreal builds change with every
/// release, so expected hashes come from unreal-release.json inside the install, signed with a
/// private key that never leaves the publisher. Only the public keys compiled in here are
/// trusted; a key is never read from the install, settings, or the environment.
/// </summary>
internal static class UnrealReleaseVerifier
{
    internal const string ManifestFileName = "unreal-release.json";
    internal const int MaximumManifestBytes = 64 * 1024;
    private const int MaximumFiles = 256;
    private const string Product = "acdm-unreal";

    internal static readonly IReadOnlyDictionary<string, string> PublicKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["unreal-release-2026-11"] =
                "<RSAKeyValue><Modulus>orVkoCwhDHwel/qbfBPk710gp9C1J05rsPp5Fhx4Vn2sAOdM78Yf5sJDasInuPLC/tIv7UNsZK+RoBweFL+P7Ci4HZGU7424tiI1JwBV1+ekKSB6eVtje7twYSgWGuwkjKRUAlfyOCx/E6Q5NlvEotmKfHmV8swfrbCIKZ4Mjh44jCMjHIqIVPsWp7NqIRDN+MguAH96BIijebHPBLKQrxs/DwY12IRWDM6lJySubVNDAar6SLGix2y1QPPqmh/Trqf5sNxlFIw5B+SHEOWQRqm9rd+k2AMFOS2Gcr8shbacnMlVcsOii7Mvsw7nZi/2JTutCVrmofTuSODfXepki61VFE1xVjN4RBPzps67uA/pCq6vgyqshDiEzalFv6vq/OwybOMMmNbs4ss/FWgDenq9ju2sSrvlfY4IF4ZNnHTdz4UZ9729xlfbbnR6N6c4DYWL2gRKzewQqkB/aYuQJ98bj9mRoWfKWQGCK8JI1iqDIPzpE/p3dymLQCM/DQmt</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>",
        };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    internal static FileStream OpenVerifiedExecutable(string projectDirectory, string executable) =>
        OpenVerifiedExecutable(projectDirectory, executable, PublicKeys);

    /// <summary>
    /// Returns the executable opened with writes and deletes denied. Keep it open until the
    /// process has started so the verified bytes are the bytes Windows maps.
    /// </summary>
    internal static FileStream OpenVerifiedExecutable(
        string projectDirectory, string executable, IReadOnlyDictionary<string, string> publicKeys)
    {
        var root = Path.GetFullPath(projectDirectory);
        var relative = NormalizePath(Path.GetRelativePath(root, Path.GetFullPath(executable)));
        if (!IsSafeRelativePath(relative))
            throw new InvalidDataException("The Unreal client executable is outside its install folder.");

        var manifest = new FileInfo(Path.Combine(root, ManifestFileName));
        if (!manifest.Exists)
            throw new InvalidDataException(
                "This Unreal client is not a signed Aetherium release. Reinstall or update the Unreal client.");
        if (manifest.Length is 0 or > MaximumManifestBytes)
            throw new InvalidDataException("The Unreal client release file has an invalid size.");

        var release = Verify(File.ReadAllBytes(manifest.FullName), publicKeys);
        var expected = release.Files.SingleOrDefault(file =>
                string.Equals(NormalizePath(file.Path), relative, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"{Path.GetFileName(executable)} is not part of signed Unreal release {release.Version}.");

        var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length != expected.Size ||
                !Convert.ToHexString(SHA256.HashData(stream)).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"{Path.GetFileName(executable)} does not match signed Unreal release {release.Version}. " +
                    "Reinstall or update the Unreal client.");
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal static UnrealReleasePayload Verify(
        byte[] envelopeBytes, IReadOnlyDictionary<string, string> publicKeys)
    {
        if (envelopeBytes.Length is 0 or > MaximumManifestBytes)
            throw new InvalidDataException("The Unreal client release file has an invalid size.");
        var envelope = JsonSerializer.Deserialize<UnrealReleaseEnvelope>(envelopeBytes, Json)
            ?? throw new InvalidDataException("The Unreal client release file is empty.");
        if (envelope.SchemaVersion != 1 || string.IsNullOrEmpty(envelope.KeyId) ||
            !publicKeys.TryGetValue(envelope.KeyId, out var publicKey))
            throw new InvalidDataException("This launcher does not trust the Unreal client release signing key.");

        byte[] payloadBytes, signature;
        try
        {
            payloadBytes = Convert.FromBase64String(envelope.PayloadBase64 ?? "");
            signature = Convert.FromBase64String(envelope.SignatureBase64 ?? "");
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("The Unreal client release signature is malformed.", ex);
        }

        using var key = RSA.Create();
        key.FromXmlString(publicKey);
        if (key.KeySize < 2048 || payloadBytes.Length == 0 || !key.VerifyData(
                payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("The Unreal client release signature did not verify.");

        var payload = JsonSerializer.Deserialize<UnrealReleasePayload>(payloadBytes, Json)
            ?? throw new InvalidDataException("The signed Unreal client release is empty.");
        if (payload.SchemaVersion != 1 || payload.Product != Product || string.IsNullOrWhiteSpace(payload.Version) ||
            payload.Files is not { Count: > 0 and <= MaximumFiles } ||
            payload.Files.Any(file => file is null || file.Size < 0 || !IsSha256(file.Sha256) ||
                !IsSafeRelativePath(NormalizePath(file.Path ?? ""))) ||
            payload.Files.Select(file => NormalizePath(file.Path).ToUpperInvariant()).Distinct().Count() != payload.Files.Count)
            throw new InvalidDataException("The signed Unreal client release is not valid.");
        return payload;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static bool IsSafeRelativePath(string path) =>
        path.Length > 0 && !Path.IsPathRooted(path) && !path.StartsWith('/') &&
        path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

internal sealed class UnrealReleaseEnvelope
{
    public int SchemaVersion { get; set; }
    public string? KeyId { get; set; }
    public string? PayloadBase64 { get; set; }
    public string? SignatureBase64 { get; set; }
}

internal sealed class UnrealReleasePayload
{
    public int SchemaVersion { get; set; }
    public string Product { get; set; } = "";
    public string Version { get; set; } = "";
    public string PublishedUtc { get; set; } = "";
    public List<UnrealReleaseFile> Files { get; set; } = [];
}

internal sealed class UnrealReleaseFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}
