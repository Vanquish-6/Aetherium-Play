using System.Globalization;

namespace AcLegacyLauncher;

internal static class LauncherIniReader
{
    internal sealed class DataCenterInfo
    {
        public string Name { get; init; } = string.Empty;

        public string ServerAddress { get; init; } = string.Empty;

        public int ServerPort { get; init; }
    }

    internal static DataCenterInfo? ReadPrimaryDataCenter(string installDirectory)
    {
        var iniPath = Path.Combine(installDirectory, "launcher.ini");
        if (!File.Exists(iniPath))
        {
            return null;
        }

        string? currentSection = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadAllLines(iniPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1];
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0 || currentSection is null)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            values[$"{currentSection}:{key}"] = value;
        }

        if (!values.TryGetValue("DataCenter_0:serveraddress", out var serverAddress)
            || string.IsNullOrWhiteSpace(serverAddress))
        {
            return null;
        }

        values.TryGetValue("DataCenter_0:name", out var name);

        var port = 9000;
        if (values.TryGetValue("DataCenter_0:serverport", out var portText)
            && int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
        {
            port = parsedPort;
        }

        return new DataCenterInfo
        {
            Name = name ?? string.Empty,
            ServerAddress = serverAddress,
            ServerPort = port,
        };
    }
}
