namespace AcLegacyLauncher;

/// <summary>
/// Public verification material only. Release downloads remain unavailable
/// until an approved feed, RSA verification key, and download host are built
/// into the launcher. Never read a trust key from a downloaded manifest, user
/// settings, an environment variable, or the add-on directory.
/// </summary>
internal static class AethTankReleasePolicy
{
    internal const string ManifestUrl =
        "https://aetherium.ac/downloads/aeth-tank/release-envelope.json";

    // Set these only as part of a reviewed launcher release. Private signing
    // keys and storage credentials belong exclusively on the publishing side.
    internal static readonly IReadOnlyDictionary<string, string> PublicKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["aeth-tank-manifest-2026-01"] = """
                -----BEGIN PUBLIC KEY-----
                MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAtSlJwjoBURRoOgK2Tm/i
                5Rb5XBtkkPUB/FMHt8HSNZUeI3tbedMB3jEwbtu7q+jW2bDyUWjow+u0uob6n0rl
                ouqwk1k+aASnaK6H1Bps8zkfAjBYtB5870kCk3K1GBWXX//AO+OQj2NOMV/vJxEf
                yZtS5M4x98LKbhRLQ8I66cqYJAETfyYeR12D/jwNuOJWKioA44xiW4gbmTIGtuVU
                Z8eM0fsWNDFGaOpthMzVUWNLgi+zVVLR/FdHAzWw1jOu2ckYjt23L42p2N46XNsD
                4wPHxc6urym7pQYJRe/aFO4vABzHFUWh+ndtRU0WG+r64pCnqMN0F3l20lACZwx8
                O8yIXXKEj9c4nOwmwslTZtIXRKwiAeteSkpHDS3+q9n8+EdBjqYxoBuGzc3vJMdZ
                TLFe4CwlSzQMJLaBFp5pZVlOhCTL+miztxUpylLpMMl/xn01puESygG6qvP74IqV
                AtIyW2FMxzjdFpcWYTDWDIykwkPNmycpDHrpiKXzUuWVAgMBAAE=
                -----END PUBLIC KEY-----
                """,
        };
    internal static readonly IReadOnlySet<string> DownloadHosts =
        new HashSet<string>(["aetherium.ac"], StringComparer.OrdinalIgnoreCase);

    internal static AethTankAddonOptions CreateOptions() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AetheriumPlay", "Addons", "AethTank"),
        ManifestUrl,
        PublicKeys,
        DownloadHosts,
        typeof(AethTankReleasePolicy).Assembly.GetName().Version ?? new Version(0, 0));
}

internal sealed record AethTankAddonOptions(
    string RootDirectory,
    string ManifestUrl,
    IReadOnlyDictionary<string, string> PublicKeys,
    IReadOnlySet<string> DownloadHosts,
    Version LauncherVersion);
