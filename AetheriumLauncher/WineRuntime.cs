using System.Runtime.InteropServices;

namespace AcLegacyLauncher;

/// <summary>
/// Detects a Wine/Proton host while this Windows launcher is running inside
/// a compatibility prefix. Used to skip dgVoodoo and to relax job-object
/// containment that some Wine builds do not implement.
/// </summary>
internal static class WineRuntime
{
    private static readonly Lazy<Detection> Detected = new(Detect);

    internal static bool IsWine => Detected.Value.IsWine;

    internal static string? Version => Detected.Value.Version;

    internal static string LaunchDetail
    {
        get
        {
            if (!IsWine)
            {
                return string.Empty;
            }

            var version = string.IsNullOrWhiteSpace(Version) ? "Wine" : $"Wine {Version}";
            return $"Compatibility layer: {version} (dgVoodoo skipped; wineserver owns process lifetime).";
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr WineGetVersion();

    private static Detection Detect()
    {
        try
        {
            var ntdll = GetModuleHandleW("ntdll.dll");
            if (ntdll == IntPtr.Zero)
            {
                return Detection.None;
            }

            var versionExport = GetProcAddress(ntdll, "wine_get_version");
            if (versionExport == IntPtr.Zero)
            {
                return Detection.None;
            }

            var wineGetVersion = Marshal.GetDelegateForFunctionPointer<WineGetVersion>(versionExport);
            var versionPointer = wineGetVersion();
            var version = versionPointer == IntPtr.Zero
                ? null
                : Marshal.PtrToStringAnsi(versionPointer);
            return new Detection(true, version);
        }
        catch
        {
            return Detection.None;
        }
    }

    private sealed record Detection(bool IsWine, string? Version)
    {
        internal static Detection None { get; } = new(false, null);
    }
}
