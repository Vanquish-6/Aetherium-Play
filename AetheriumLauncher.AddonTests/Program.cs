using AcLegacyLauncher;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// These fixtures never load a plugin or contact a network service. Trust is
// injected into a private temporary manager; production trust remains empty.
var tests = new AddonTests();
await tests.Run();

internal sealed class AddonTests
{
    private const string Feed = "https://releases.example.test/aeth-tank.json";
    private const string Package = "https://download.example.test/aeth-tank.zip";
    private readonly RSA key = RSA.Create(2048);
    private readonly string root = Path.Combine(Path.GetTempPath(), "aeth-addon-" + Guid.NewGuid().ToString("N"));
    private int checks;

    internal async Task Run()
    {
        try
        {
            Directory.CreateDirectory(root);
            ManifestChecks();
            NativeChecks();
            await XollvmPackageChecks();
            await LifecycleChecks();
            await RejectionChecks();
            UnrealReleaseChecks();
            Console.WriteLine($"PASS: {checks} add-on signature, packaging, installation, lifecycle, launch-lease and Unreal release checks");
        }
        finally
        {
            key.Dispose();
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); }
                catch (IOException ex) { Console.Error.WriteLine("Fixture cleanup: " + ex.Message); }
            }
        }
    }

    private void UnrealReleaseChecks()
    {
        var install = Path.Combine(root, "unreal", "ACDM");
        var exe = Path.Combine(install, "Binaries", "Win64", "ACDM.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllBytes(exe, Encoding.ASCII.GetBytes("MZ signed unreal client"));
        var trusted = new Dictionary<string, string> { ["test-unreal"] = key.ToXmlString(false) };

        void Sign(object payload, RSA signer, string keyId = "test-unreal")
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            File.WriteAllText(Path.Combine(install, UnrealReleaseVerifier.ManifestFileName), JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                keyId,
                payloadBase64 = Convert.ToBase64String(bytes),
                signatureBase64 = Convert.ToBase64String(
                    signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
            }));
        }
        object Release(string path, string? sha = null, long? size = null) => new
        {
            schemaVersion = 1,
            product = "acdm-unreal",
            version = "test-1",
            publishedUtc = "2026-11-14T00:00:00Z",
            files = new[] { new { path, size = size ?? new FileInfo(exe).Length,
                sha256 = sha ?? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe))) } },
        };
        FileStream Open() => UnrealReleaseVerifier.OpenVerifiedExecutable(install, exe, trusted);

        Sign(Release("Binaries/Win64/ACDM.exe"), key);
        using (var pinned = Open())
        {
            Check(pinned.Position == 0, "verified Unreal executable is returned at offset zero");
            Reject(() => File.WriteAllText(exe, "swapped while launching"),
                "verified Unreal executable cannot be replaced while the launcher holds it");
        }
        Sign(Release(@"Binaries\Win64\ACDM.exe"), key);
        Open().Dispose();
        Check(true, "backslash release paths match");

        File.WriteAllBytes(exe, Encoding.ASCII.GetBytes("MZ edited unreal client"));
        Reject(() => Open().Dispose(), "edited Unreal executable is refused");
        Sign(Release("Binaries/Win64/ACDM.exe"), key);
        Open().Dispose();

        using (var stranger = RSA.Create(2048))
        {
            Sign(Release("Binaries/Win64/ACDM.exe"), stranger);
            Reject(() => Open().Dispose(), "release signed by an untrusted key is refused");
        }
        Sign(Release("Binaries/Win64/ACDM.exe"), key, keyId: "unknown-key");
        Reject(() => Open().Dispose(), "release naming an unpinned key id is refused");
        Sign(Release("Binaries/Win64/Other.exe"), key);
        Reject(() => Open().Dispose(), "executable missing from the release is refused");
        Sign(Release("../Binaries/Win64/ACDM.exe"), key);
        Reject(() => Open().Dispose(), "release paths outside the install are refused");
        Sign(Release("Binaries/Win64/ACDM.exe", size: 1), key);
        Reject(() => Open().Dispose(), "size mismatch is refused");
        Sign(Release("Binaries/Win64/ACDM.exe", sha: "not-a-hash"), key);
        Reject(() => Open().Dispose(), "malformed hash is refused");

        File.Delete(Path.Combine(install, UnrealReleaseVerifier.ManifestFileName));
        Reject(() => Open().Dispose(), "unsigned Unreal install is refused");
    }

    private void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or
            CryptographicException or JsonException or IOException or BadImageFormatException or
            ArgumentException or OverflowException)
        {
            checks++;
            return;
        }
        throw new Exception("FAIL (accepted): " + message);
    }

    private async Task RejectAsync(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or
            CryptographicException or JsonException or IOException or BadImageFormatException or
            ArgumentException or OverflowException or HttpRequestException or OperationCanceledException)
        {
            checks++;
            return;
        }
        throw new Exception("FAIL (accepted): " + message);
    }

    private void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { checks++; return; }
        throw new Exception("FAIL (accepted): " + message);
    }

    private async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T) { checks++; return; }
        throw new Exception("FAIL (accepted): " + message);
    }

    private AethTankAddonOptions Options(string folder = "default") => new(
        Path.Combine(root, folder), Feed,
        new Dictionary<string, string> { ["test-key"] = key.ExportSubjectPublicKeyInfoPem() },
        new HashSet<string>(["download.example.test"], StringComparer.OrdinalIgnoreCase),
        new Version(1, 0, 45, 0));

    private static byte[] NativeDll(byte marker = 0)
    {
        var bytes = new byte[0x600];
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; U32(0x3c, 0x80);
        U32(0x80, 0x4550); U16(0x84, 0x14c); U16(0x86, 1);
        U16(0x94, 0xe0); U16(0x96, 0x2102);
        const int opt = 0x98;
        U16(opt, 0x10b); U32(opt + 16, 0x1100); U32(opt + 20, 0x1000);
        U32(opt + 28, 0x400000); U32(opt + 32, 0x1000); U32(opt + 36, 0x200);
        U16(opt + 40, 4); U16(opt + 48, 4); U32(opt + 56, 0x2000);
        U32(opt + 60, 0x200); U16(opt + 68, 2); U32(opt + 92, 16);
        U32(opt + 96, 0x1000); U32(opt + 100, 0x80);
        const int section = 0x178;
        Encoding.ASCII.GetBytes(".text").CopyTo(bytes, section);
        U32(section + 8, 0x400); U32(section + 12, 0x1000);
        U32(section + 16, 0x400); U32(section + 20, 0x200);
        U32(section + 36, 0x60000020);
        U32(0x20c, 0x1060); U32(0x210, 1); U32(0x214, 1); U32(0x218, 1);
        U32(0x21c, 0x1040); U32(0x220, 0x1044); U32(0x224, 0x1048);
        U32(0x240, 0x1100); U32(0x244, 0x1050); U16(0x248, 0);
        Encoding.ASCII.GetBytes("CreateACPlugin\0").CopyTo(bytes, 0x250);
        Encoding.ASCII.GetBytes("aeth-tank.dll\0").CopyTo(bytes, 0x260);
        bytes[0x300] = 0xc3; bytes[^1] = marker;
        return bytes;
    }

    private static Dictionary<string, byte[]> Contents(byte marker = 0) => new(StringComparer.Ordinal)
    {
        ["aeth-tank.dll"] = NativeDll(marker),
        ["README.txt"] = "Synthetic fixture. Never run this DLL."u8.ToArray(),
        ["LICENSE.txt"] = "Synthetic test-only content."u8.ToArray(),
    };

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] Archive(Dictionary<string, byte[]> content, Action<ZipArchive>? extra = null,
        Action<ZipArchiveEntry>? configureEntry = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in content)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
                configureEntry?.Invoke(entry);
                using var stream = entry.Open();
                stream.Write(bytes);
            }
            extra?.Invoke(zip);
        }
        return output.ToArray();
    }

    private AethTankManifestPayload Payload(string version = "0.1.0", byte marker = 0,
        string protection = "vmprotect")
    {
        var contents = Contents(marker);
        var archive = Archive(contents);
        return new()
        {
            SchemaVersion = 2, Product = "aeth-tank", Version = version,
            Channel = "private", ReleaseStatus = "distributable",
            PublishedUtc = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
            MinLauncherVersion = "1.0.45",
            Clients = AethTankManifest.SupportedClients.Select(pair => new AethTankManifestClient
            {
                Profile = pair.Key, Sha256 = pair.Value,
            }).ToList(),
            Architecture = "x86",
            Protection = protection, PackageUrl = Package, PackageSha256 = Hash(archive),
            PackageSize = archive.Length,
            DllSignerThumbprint = new string('1', 40),
            Files = contents.Select(pair => new AethTankManifestFile
            {
                Path = pair.Key, Size = pair.Value.Length, Sha256 = Hash(pair.Value),
            }).ToList(),
        };
    }

    private byte[] Envelope(AethTankManifestPayload payload) =>
        EnvelopeBytes(JsonSerializer.SerializeToUtf8Bytes(payload, AethTankManifest.Json));

    private byte[] EnvelopeBytes(byte[] payload) => JsonSerializer.SerializeToUtf8Bytes(
        new AethTankManifestEnvelope
        {
            SchemaVersion = 1, KeyId = "test-key",
            PayloadBase64 = Convert.ToBase64String(payload),
            SignatureBase64 = Convert.ToBase64String(key.SignData(
                payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
        }, AethTankManifest.Json);

    private AethTankRelease Verify(AethTankManifestPayload payload) =>
        AethTankManifest.Verify(Envelope(payload), Options());

    private void ManifestChecks()
    {
        foreach (var protection in new[] { "vmprotect", "themida", "xollvm" })
        {
            var release = Verify(Payload(protection: protection));
            Check(release.Version == "0.1.0" && release.Protection == protection,
                protection + " signed manifest preserves the distinct provider");
        }
        var mutations = new (string Name, Action<AethTankManifestPayload> Apply)[]
        {
            ("draft", p => p.ReleaseStatus = "draft"),
            ("unprotected", p => p.Protection = "none"),
            ("wrong protector", p => p.Protection = "zip"),
            ("noncanonical protector", p => p.Protection = "Xollvm"),
            ("wrong product", p => p.Product = "launcher"),
            ("wrong channel", p => p.Channel = "untrusted"),
            ("wrong schema", p => p.SchemaVersion = 1),
            ("wrong architecture", p => p.Architecture = "x64"),
            ("wrong client", p => p.Clients[0].Profile = "admin"),
            ("wrong client hash", p => p.Clients[0].Sha256 = new string('0', 64)),
            ("null client hash", p => p.Clients[0].Sha256 = null!),
            ("missing client", p => p.Clients.RemoveAt(0)),
            ("duplicate client", p => p.Clients[1].Profile = p.Clients[0].Profile),
            ("null version", p => p.Version = null!),
            ("null URL", p => p.PackageUrl = null!),
            ("null signer", p => p.DllSignerThumbprint = null!),
            ("null files", p => p.Files = null!),
            ("null file row", p => p.Files[1] = null!),
            ("null file hash", p => p.Files[1].Sha256 = null!),
            ("traversal version", p => p.Version = "../0.1.0"),
            ("noncanonical version", p => p.Version = "00.1.0"),
            ("four part version", p => p.Version = "0.1.0.0"),
            ("huge version", p => p.Version = "65536.1.0"),
            ("new launcher required", p => p.MinLauncherVersion = "1.0.46"),
            ("future publication", p => p.PublishedUtc = DateTimeOffset.UtcNow.AddDays(2).ToString("O")),
            ("non-UTC publication", p => p.PublishedUtc = "2026-11-10T10:00:00-08:00"),
            ("HTTP package", p => p.PackageUrl = "http://download.example.test/file"),
            ("unapproved host", p => p.PackageUrl = "https://evil.example.test/file"),
            ("userinfo package", p => p.PackageUrl = "https://user@download.example.test/file"),
            ("fragment package", p => p.PackageUrl = Package + "#ignored"),
            ("port package", p => p.PackageUrl = "https://download.example.test:444/file"),
            ("empty package", p => p.PackageSize = 0),
            ("oversized package", p => p.PackageSize = AethTankManifest.MaximumPackageBytes + 1),
            ("bad package hash", p => p.PackageSha256 = "abc"),
            ("bad publisher", p => p.DllSignerThumbprint = "anything"),
            ("missing file", p => p.Files.RemoveAt(1)),
            ("extra file", p => p.Files.Add(new() { Path = "secret.pdb", Size = 1, Sha256 = new string('1', 64) })),
            ("duplicate file", p => p.Files[1].Path = "aeth-tank.dll"),
            ("path traversal", p => p.Files[1].Path = "../README.txt"),
            ("wrong file casing", p => p.Files[0].Path = "Aeth-Tank.dll"),
            ("zero file", p => p.Files[0].Size = 0),
            ("bad file hash", p => p.Files[0].Sha256 = new string('z', 64)),
            ("unpacked bomb", p => p.Files[0].Size = AethTankManifest.MaximumPackageBytes),
        };
        foreach (var protection in new[] { "vmprotect", "xollvm" })
        {
            foreach (var (name, apply) in mutations)
            {
                var payload = Payload(protection: protection); apply(payload);
                Reject(() => Verify(payload), protection + ": " + name);
            }
        }
        var envelope = Envelope(Payload(protection: "xollvm"));
        var untrusted = Options() with { PublicKeys = new Dictionary<string, string>() };
        Reject(() => AethTankManifest.Verify(envelope, untrusted), "xollvm still requires a trusted signing key");
        var parsed = JsonSerializer.Deserialize<AethTankManifestEnvelope>(envelope, AethTankManifest.Json)!;
        var signature = parsed.SignatureBase64;
        parsed.SignatureBase64 = "";
        Reject(() => AethTankManifest.Verify(JsonSerializer.SerializeToUtf8Bytes(parsed, AethTankManifest.Json), Options()),
            "unsigned xollvm envelope");
        parsed.SignatureBase64 = signature;
        var relabeled = JsonSerializer.Deserialize<AethTankManifestPayload>(
            Convert.FromBase64String(parsed.PayloadBase64), AethTankManifest.Json)!;
        relabeled.Protection = "vmprotect";
        parsed.PayloadBase64 = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(relabeled, AethTankManifest.Json));
        Reject(() => AethTankManifest.Verify(JsonSerializer.SerializeToUtf8Bytes(parsed, AethTankManifest.Json), Options()),
            "protection provider cannot be relabeled after signing");
        parsed.PayloadBase64 = null!;
        Reject(() => AethTankManifest.Verify(JsonSerializer.SerializeToUtf8Bytes(parsed, AethTankManifest.Json), Options()), "null envelope payload");
        parsed.PayloadBase64 = Convert.ToBase64String("{}"u8);
        Reject(() => AethTankManifest.Verify(JsonSerializer.SerializeToUtf8Bytes(parsed, AethTankManifest.Json), Options()), "changed signed bytes");
        var duplicatePayload = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(Payload(), AethTankManifest.Json))
                .Replace("\"schemaVersion\": 2,", "\"schemaVersion\": 2, \"schemaVersion\": 2,"));
        Reject(() => AethTankManifest.Verify(EnvelopeBytes(duplicatePayload), Options()), "duplicate signed property");
        var duplicateEnvelope = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(envelope)
            .Replace("\"keyId\": \"test-key\",", "\"keyId\": \"test-key\", \"keyId\": \"test-key\","));
        Reject(() => AethTankManifest.Verify(duplicateEnvelope, Options()), "duplicate envelope property");
        var unknownPayload = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(
            JsonSerializer.SerializeToUtf8Bytes(Payload(), AethTankManifest.Json))
            .Replace("\"product\":", "\"extra\":true,\"product\":"));
        Reject(() => AethTankManifest.Verify(EnvelopeBytes(unknownPayload), Options()), "unknown signed property");
        Reject(() => AethTankManifest.Verify(new byte[AethTankManifest.MaximumEnvelopeBytes + 1], Options()), "oversized envelope");
        Check(AethTankAddonManager.Default.GetStatus().ReleaseConfigured, "production release trust is configured");
        Check(AethTankReleasePolicy.ManifestUrl ==
                "https://aetherium.ac/downloads/aeth-tank/release-envelope.json" &&
            AethTankReleasePolicy.PublicKeys.Count == 1 &&
            AethTankReleasePolicy.PublicKeys.ContainsKey("aeth-tank-manifest-2026-01") &&
            AethTankReleasePolicy.DownloadHosts.SetEquals(["aetherium.ac"]),
            "production endpoint, trust key and download host are pinned");
    }

    private void NativeChecks()
    {
        var path = Path.Combine(root, "fixture.dll");
        File.WriteAllBytes(path, NativeDll());
        AethTankBinaryVerifier.VerifyNativeImage(path);
        Check(true, "native PE and exported plugin entry");
        var wrong = NativeDll();
        BinaryPrimitives.WriteUInt16LittleEndian(wrong.AsSpan(0x84), 0x8664);
        File.WriteAllBytes(path, wrong);
        Reject(() => AethTankBinaryVerifier.VerifyNativeImage(path), "x64 image");
        wrong = NativeDll(); wrong[0x250] = (byte)'X'; File.WriteAllBytes(path, wrong);
        Reject(() => AethTankBinaryVerifier.VerifyNativeImage(path), "missing expected export");
        wrong = NativeDll(); BinaryPrimitives.WriteUInt32LittleEndian(wrong.AsSpan(0x240), 0x1060);
        File.WriteAllBytes(path, wrong);
        Reject(() => AethTankBinaryVerifier.VerifyNativeImage(path), "forwarded export");
        wrong = NativeDll(); BinaryPrimitives.WriteUInt32LittleEndian(wrong.AsSpan(0x214), 2);
        File.WriteAllBytes(path, wrong);
        Reject(() => AethTankBinaryVerifier.VerifyNativeImage(path), "extra export");
        wrong = NativeDll(); BinaryPrimitives.WriteUInt32LittleEndian(wrong.AsSpan(0x178 + 36), 0x40000040);
        File.WriteAllBytes(path, wrong);
        Reject(() => AethTankBinaryVerifier.VerifyNativeImage(path), "non-executable entry");
        File.WriteAllBytes(path, NativeDll());
        Reject(() => AethTankBinaryVerifier.Verify(path, Payload()), "missing version resource");
        foreach (var protection in new[] { "vmprotect", "themida", "xollvm" })
        {
            var unsigned = Payload("0.0.0", protection: protection);
            Throws<CryptographicException>(() => AethTankBinaryVerifier.Verify(path, unsigned),
                protection + " unsigned DLL fails the production Authenticode gate");
        }
        File.Delete(path);
    }

    private AethTankAddonManager Manager(string folder, FakeHttp handler, Func<bool>? game = null) =>
        new(Options(folder), new HttpClient(handler), game ?? (() => false),
            (path, _) => AethTankBinaryVerifier.VerifyNativeImage(path));

    private AethTankRelease Configure(FakeHttp handler, string version = "0.1.0", byte marker = 0,
        byte[]? archiveOverride = null, string protection = "vmprotect")
    {
        var archive = archiveOverride ?? Archive(Contents(marker));
        var payload = Payload(version, marker, protection);
        payload.PackageSha256 = Hash(archive); payload.PackageSize = archive.Length;
        var envelope = Envelope(payload);
        handler.Responses[Feed] = (HttpStatusCode.OK, envelope);
        handler.Responses[Package] = (HttpStatusCode.OK, archive);
        return AethTankManifest.Verify(envelope, Options());
    }

    private async Task XollvmPackageChecks()
    {
        var transport = new FakeHttp();
        var manager = Manager("xollvm", transport);
        var release = Configure(transport, protection: "xollvm");
        Check((await manager.CheckForUpdateAsync())?.Protection == "xollvm",
            "signed xollvm feed retains its provider");

        var changedPackage = transport.Responses[Package].Body.ToArray();
        changedPackage[^1] ^= 1;
        transport.Responses[Package] = (HttpStatusCode.OK, changedPackage);
        await RejectAsync(() => manager.InstallAsync(release), "xollvm package hash mismatch at unchanged size");
        Check(!manager.GetStatus().Installed, "changed xollvm package never activates");

        var changedFiles = Contents();
        changedFiles["aeth-tank.dll"] = NativeDll(99);
        release = Configure(transport, archiveOverride: Archive(changedFiles), protection: "xollvm");
        await RejectAsync(() => manager.InstallAsync(release), "xollvm signed package with mismatched DLL hash");
        Check(!manager.GetStatus().Installed, "changed xollvm DLL never activates");

        // An accepted test fixture uses only the internal native-image verifier;
        // it does not claim the fixture is actually obfuscated or publisher-signed.
        release = Configure(transport, protection: "xollvm");
        await manager.InstallAsync(release);
        Check(manager.GetStatus().Installed && manager.GetStatus().CurrentVersion == release.Version,
            "verified xollvm fixture follows ordinary installation");
        manager.Uninstall();

        // The real production binary verifier remains mandatory for xollvm.
        // Fixture version 0.0.0 lets this unsigned PE reach the Authenticode gate.
        var strict = new AethTankAddonManager(Options("xollvm-authenticode"), new HttpClient(transport),
            () => false, AethTankBinaryVerifier.Verify);
        release = Configure(transport, version: "0.0.0", protection: "xollvm");
        await ThrowsAsync<CryptographicException>(() => strict.InstallAsync(release),
            "signed xollvm manifest cannot authorize an unsigned DLL");
        Check(!strict.GetStatus().Installed, "unsigned xollvm DLL never activates");
    }

    private async Task LifecycleChecks()
    {
        var transport = new FakeHttp();
        var manager = Manager("life", transport);
        var initial = manager.GetStatus();
        Check(initial.ReleaseConfigured && !initial.ManagedStateExists && !initial.Installed, "initial opt-in");
        manager.RememberPrompt(false);
        Check(manager.GetStatus().PromptDismissed && !manager.GetStatus().DontAskAgain, "not now remembered");
        manager.RememberPrompt(true);
        manager.RememberPrompt(false);
        Check(manager.GetStatus().DontAskAgain, "never ask decision persists");
        Configure(transport);
        var release = await manager.CheckForUpdateAsync();
        Check(release?.Version == "0.1.0", "signed release check");
        Directory.CreateDirectory(manager.DataDirectory);
        var profile = Path.Combine(manager.DataDirectory, "player-profile.cfg");
        File.WriteAllText(profile, "private user settings");
        await manager.InstallAsync(release!);
        var status = manager.GetStatus();
        Check(status.Installed && status.Enabled && status.CurrentVersion == "0.1.0", "opt-in installation");
        Check(status.PreviousVersion is null, "first install has no rollback");
        Check(File.ReadAllText(profile) == "private user settings", "install preserves user data");
        Check(await manager.CheckForUpdateAsync() is null, "same release is not redownloaded");
        using (var lease = manager.AcquireForLaunch(AethTankManifest.PublicClientProfile))
        {
            Check(lease.PluginPath is not null && lease.ManagedStateExists, "managed launch selection");
            Check(lease.DataDirectory == manager.DataDirectory, "stable data root");
            Reject(() => File.WriteAllText(lease.PluginPath!, "changed"), "DLL pinned against writes");
            Reject(() => File.Delete(lease.PluginPath!), "DLL pinned against replacement/deletion");
            using var second = manager.AcquireForLaunch(AethTankManifest.PublicClientProfile);
            Check(second.PluginPath == lease.PluginPath, "concurrent client leases");
            using var admin = manager.AcquireForLaunch(AethTankManifest.AdminClientProfile);
            Check(admin.PluginPath == lease.PluginPath, "same signed release supports verified admin client");
            Reject(manager.Uninstall, "uninstall while leased");
            Reject(manager.Rollback, "rollback while leased");
            var next = Configure(transport, "0.2.0", 2);
            await RejectAsync(() => manager.InstallAsync(next), "update while leased");
            manager.SetEnabled(false);
            Check(!manager.GetStatus().Enabled, "disable is next-launch configuration");
            using var disabled = manager.AcquireForLaunch(AethTankManifest.PublicClientProfile);
            Check(disabled.PluginPath is null && disabled.ManagedStateExists, "disabled still owns managed choice");
        }
        manager.SetEnabled(true);
        var update = Configure(transport, "0.2.0", 2);
        await manager.InstallAsync(update);
        status = manager.GetStatus();
        Check(status.CurrentVersion == "0.2.0" && status.PreviousVersion == "0.1.0", "atomic active version update");
        Check(File.ReadAllText(profile) == "private user settings", "update preserves user data");
        manager.Rollback();
        Check(manager.GetStatus().CurrentVersion == "0.1.0", "rollback chooses verified previous release");
        manager.Rollback();
        Check(manager.GetStatus().CurrentVersion == "0.2.0", "rollback retains both versions");
        using (var lease = manager.AcquireForLaunch("unsupported-client"))
            Check(lease.PluginPath is null, "incompatible client runs without add-on");
        string dll;
        using (var lease = manager.AcquireForLaunch(AethTankManifest.PublicClientProfile))
            dll = lease.PluginPath!;
        File.WriteAllText(dll, "tampered");
        var reports = new List<string>();
        using (var lease = manager.AcquireForLaunch(AethTankManifest.PublicClientProfile, reports.Add))
        {
            Check(lease.PluginPath is null, "tampered installed file not loaded");
            Check(reports.Count == 1 && reports[0].Contains("not enabled"), "tamper produces actionable report");
            Reject(manager.Uninstall, "unverified launch still blocks mutation");
        }
        manager.Uninstall();
        Check(!manager.GetStatus().Installed && !manager.GetStatus().Enabled, "uninstall clears activation");
        Check(manager.GetStatus().ManagedStateExists, "uninstall prevents legacy dev fallback");
        Check(File.ReadAllText(profile) == "private user settings", "uninstall preserves user profiles");
        Check(!Directory.Exists(Path.Combine(Options("life").RootDirectory, "versions")), "uninstall removes version files");
        File.WriteAllText(Path.Combine(Options("life").RootDirectory, "state.json"), "{broken");
        Check(!manager.GetStatus().Enabled && manager.GetStatus().Message.Contains("unavailable"), "corrupt state fails closed");
        using (var lease = manager.AcquireForLaunch(AethTankManifest.PublicClientProfile))
            Check(lease.PluginPath is null && lease.ManagedStateExists, "corrupt managed state does not invoke dev fallback");
        manager.Uninstall();
        Check(!manager.GetStatus().Installed, "uninstall recovers corrupt state");
        var leaseUntilExit = manager.AcquireForLaunch(AethTankManifest.PublicClientProfile);
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0")
        { UseShellExecute = false, CreateNoWindow = true })!)
        {
            leaseUntilExit.HoldUntilExit(process);
            process.WaitForExit();
        }
        manager.Uninstall();
        Check(true, "process exit releases launch gate");
    }

    private async Task RejectionChecks()
    {
        var handler = new FakeHttp();
        var manager = Manager("reject", handler);
        var release = Configure(handler);
        handler.Responses[Package] = (HttpStatusCode.OK, "not the signed package"u8.ToArray());
        await RejectAsync(() => manager.InstallAsync(release), "download size/hash mismatch");
        Check(!manager.GetStatus().Installed, "failed install does not activate");
        Check(!Directory.EnumerateDirectories(Path.Combine(Options("reject").RootDirectory, "staging")).Any(),
            "failed install cleans staging");
        var invalidArchives = new List<(string Name, byte[] Bytes)>();
        invalidArchives.Add(("unexpected source file", Archive(Contents(), zip =>
        {
            using var stream = zip.CreateEntry("plugin.c").Open(); stream.WriteByte(0);
        })));
        var traversal = Contents();
        traversal["../README.txt"] = traversal["README.txt"]; traversal.Remove("README.txt");
        invalidArchives.Add(("ZIP traversal", Archive(traversal)));
        var duplicate = Archive(Contents(), zip =>
        {
            using var stream = zip.CreateEntry("README.txt").Open(); stream.WriteByte(0);
        });
        invalidArchives.Add(("duplicate ZIP entry", duplicate));
        var modified = Contents(); modified["aeth-tank.dll"] = NativeDll(55);
        invalidArchives.Add(("signed ZIP with wrong file hash", Archive(modified)));
        invalidArchives.Add(("ZIP symbolic link", Archive(Contents(), configureEntry: entry =>
        {
            if (entry.FullName == "README.txt") entry.ExternalAttributes = unchecked((int)0xa1ff0000);
        })));
        invalidArchives.Add(("ZIP Windows reparse entry", Archive(Contents(), configureEntry: entry =>
        {
            if (entry.FullName == "README.txt") entry.ExternalAttributes = (int)FileAttributes.ReparsePoint;
        })));
        foreach (var (name, bytes) in invalidArchives)
        {
            var candidate = Configure(handler, archiveOverride: bytes);
            await RejectAsync(() => manager.InstallAsync(candidate), name);
            Check(!manager.GetStatus().Installed, name + " stays inactive");
        }
        var gameManager = Manager("game", handler, () => true);
        release = Configure(handler);
        await RejectAsync(() => gameManager.InstallAsync(release), "unmanaged game process blocks install");
        Reject(gameManager.Uninstall, "unmanaged game process blocks uninstall");
        Reject(gameManager.Rollback, "unmanaged game process blocks rollback");
        Check(!gameManager.GetStatus().Installed, "game guard preserves previous state");
        var appearedDuringDownload = false;
        var raceTransport = new FakeHttp();
        var raceManager = Manager("race", raceTransport, () => appearedDuringDownload);
        release = Configure(raceTransport);
        raceTransport.BeforeResponse = uri =>
        {
            if (uri != Package) return;
            Throws<AethTankAddonBusyException>(() => raceManager.AcquireForLaunch(AethTankManifest.PublicClientProfile),
                "Play cannot race a held installation lease");
            appearedDuringDownload = true;
        };
        await RejectAsync(() => raceManager.InstallAsync(release), "game appears before activation");
        Check(!raceManager.GetStatus().Installed, "late game check preserves inactive state");
        release = Configure(handler);
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            await RejectAsync(() => manager.InstallAsync(release, cancellationToken: cancel.Token), "cancelled install");
        }
        handler.Responses[Feed] = (HttpStatusCode.Redirect, []);
        await RejectAsync(async () => await manager.CheckForUpdateAsync(), "manifest redirect");
        handler.Responses[Feed] = (HttpStatusCode.OK, "<!DOCTYPE html><title>Aetherium</title>"u8.ToArray());
        await ThrowsAsync<InvalidOperationException>(async () => await manager.CheckForUpdateAsync(),
            "website HTML is not a published release");
        handler.Responses[Feed] = (HttpStatusCode.NotFound, []);
        await ThrowsAsync<InvalidOperationException>(async () => await manager.CheckForUpdateAsync(),
            "missing release is not a parser failure");
        handler.Responses[Feed] = (HttpStatusCode.OK, new byte[AethTankManifest.MaximumEnvelopeBytes + 1]);
        await RejectAsync(async () => await manager.CheckForUpdateAsync(), "oversized remote manifest");
        var unavailableHttp = new FakeHttp();
        var unavailable = new AethTankAddonManager(Options("none") with
        { ManifestUrl = "", PublicKeys = new Dictionary<string, string>() },
            new HttpClient(unavailableHttp), () => false, (_, _) => throw new Exception("must not validate"));
        Check(await unavailable.CheckForUpdateAsync() is null && unavailableHttp.Requests == 0,
            "unconfigured release performs no network access");
        using (var lease = unavailable.AcquireForLaunch(AethTankManifest.PublicClientProfile))
        {
            Check(lease.PluginPath is null && !lease.ManagedStateExists, "unconfigured normal game launch lease");
            Reject(unavailable.Uninstall, "unconfigured launch still prevents future mutation");
        }
        foreach (var obstruction in new[] { "root-file", "lock-directory" })
        {
            var blockedOptions = Options(obstruction) with
            { ManifestUrl = "", PublicKeys = new Dictionary<string, string>() };
            if (obstruction == "root-file")
                File.WriteAllText(blockedOptions.RootDirectory, "unrelated existing file");
            else
                Directory.CreateDirectory(Path.Combine(blockedOptions.RootDirectory, "launch.lock"));
            var blocked = new AethTankAddonManager(blockedOptions, new HttpClient(new FakeHttp()),
                () => false, (_, _) => throw new Exception("must not validate"));
            var blockedReports = new List<string>();
            using (var lease = blocked.AcquireForLaunch(AethTankManifest.PublicClientProfile, blockedReports.Add))
            {
                Check(lease.PluginPath is null && lease.ManagedStateExists,
                    obstruction + " permits ordinary launch without developer fallback");
                Check(blockedReports.Count == 1 && blockedReports[0].Contains("unavailable"),
                    obstruction + " reports unavailable optional storage");
                using var process = new Process();
                lease.HoldUntilExit(process);
                Check(true, obstruction + " passive lease lifetime handoff needs no storage");
            }
            if (obstruction == "lock-directory")
                Throws<UnauthorizedAccessException>(blocked.Uninstall, "invalid lock path prevents mutation");
            else
                Reject(blocked.Uninstall, obstruction + " cannot mutate unavailable storage");
            if (obstruction == "root-file")
            {
                Check(blocked.GetStatus().ManagedStateExists && blocked.GetStatus().Message.Contains("unavailable"),
                    "invalid root status suppresses unmanaged offers");
                Check(File.ReadAllText(blockedOptions.RootDirectory) == "unrelated existing file",
                    "invalid root remains untouched");
            }
        }
        var directoryState = Manager("directory-state", new FakeHttp());
        Directory.CreateDirectory(Path.Combine(Options("directory-state").RootDirectory, "state.json"));
        Check(directoryState.GetStatus().ManagedStateExists && directoryState.GetStatus().Message.Contains("unavailable"),
            "state directory is unavailable, not an absent managed choice");
        using (var lease = directoryState.AcquireForLaunch(AethTankManifest.PublicClientProfile))
        {
            Check(lease.PluginPath is null && lease.ManagedStateExists, "invalid state path suppresses developer fallback");
            Reject(directoryState.Uninstall, "invalid state launch still holds mutation gate");
        }
        var longOptions = Options("long") with { RootDirectory = Path.Combine(root, new string('x', 100)) };
        var tooLong = new AethTankAddonManager(longOptions, new HttpClient(handler),
            () => false, (path, _) => AethTankBinaryVerifier.VerifyNativeImage(path));
        release = Configure(handler);
        await RejectAsync(() => tooLong.InstallAsync(release), "native data path length bound");
        Check(!tooLong.GetStatus().Installed, "long path never activates");

        foreach (var fault in new[] { "manifest", "extra", "missing" })
        {
            var faultTransport = new FakeHttp();
            var faultManager = Manager("fault-" + fault, faultTransport);
            var candidate = Configure(faultTransport);
            await faultManager.InstallAsync(candidate);
            var versionRoot = Path.Combine(Options("fault-" + fault).RootDirectory, "versions", candidate.Version);
            if (fault == "manifest") File.WriteAllText(Path.Combine(versionRoot, "release-envelope.json"), "{\"schemaVersion\":1}");
            if (fault == "extra") File.WriteAllText(Path.Combine(versionRoot, "unapproved.pdb"), "not allowed");
            if (fault == "missing") File.Delete(Path.Combine(versionRoot, "README.txt"));
            var faultReports = new List<string>();
            using (var lease = faultManager.AcquireForLaunch(AethTankManifest.PublicClientProfile, faultReports.Add))
            {
                Check(lease.PluginPath is null && faultReports.Count == 1, fault + " disables only the add-on");
            }
            faultManager.Uninstall();
            Check(!faultManager.GetStatus().Installed, fault + " rejection releases the launch lease");
        }

        var failureTransport = new FakeHttp();
        var failureManager = Manager("unexpected", failureTransport);
        await failureManager.InstallAsync(Configure(failureTransport));
        var exceptional = new AethTankAddonManager(Options("unexpected"), new HttpClient(failureTransport),
            () => false, (_, _) => throw new NotSupportedException("synthetic validator fault"));
        Throws<NotSupportedException>(() => exceptional.AcquireForLaunch(AethTankManifest.PublicClientProfile),
            "unexpected verifier exception propagates");
        Throws<ApplicationException>(() => failureManager.AcquireForLaunch(AethTankManifest.PublicClientProfile,
            _ => throw new ApplicationException("synthetic reporting fault")), "unexpected reporter fault");
        Throws<InvalidOperationException>(() => failureManager.AcquireForLaunch(AethTankManifest.PublicClientProfile,
            _ => throw new InvalidOperationException("synthetic closed UI")), "reporter fault in expected-error path");
        failureManager.Uninstall();
        Check(!failureManager.GetStatus().Installed, "unexpected callbacks release both DLL and launch leases");
    }
}

internal sealed class FakeHttp : HttpMessageHandler
{
    internal readonly Dictionary<string, (HttpStatusCode Status, byte[] Body)> Responses = new(StringComparer.Ordinal);
    internal int Requests;
    internal Action<string>? BeforeResponse;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests++;
        BeforeResponse?.Invoke(request.RequestUri!.AbsoluteUri);
        if (!Responses.TryGetValue(request.RequestUri!.AbsoluteUri, out var value))
            throw new InvalidOperationException("Unexpected test HTTP request.");
        return Task.FromResult(new HttpResponseMessage(value.Status)
        {
            Content = new ByteArrayContent(value.Body), RequestMessage = request,
        });
    }
}
