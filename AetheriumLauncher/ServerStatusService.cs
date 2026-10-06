using System.Net.Http.Headers;
using System.Text.Json;

namespace AcLegacyLauncher;

internal sealed record RealmStatusSnapshot(bool IsOnline, int OnlineCount);

internal sealed record ServerStatusSnapshot(
    bool StatusKnown,
    RealmStatusSnapshot? White,
    RealmStatusSnapshot? Red)
{
    public static ServerStatusSnapshot Unknown { get; } = new(false, null, null);
}

internal static class ServerStatusService
{
    internal const string StatusUrl = "https://aetherium.ac/api/server/status";

    private static readonly HttpClient Http = CreateClient();

    public static async Task<ServerStatusSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(StatusUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ServerStatusSnapshot.Unknown;
        }
    }

    internal static ServerStatusSnapshot ParseForTest(string json) => Parse(json);

    private static ServerStatusSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("realms", out var realms) || realms.ValueKind != JsonValueKind.Array)
        {
            return ServerStatusSnapshot.Unknown;
        }

        RealmStatusSnapshot? white = null;
        RealmStatusSnapshot? red = null;

        foreach (var realm in realms.EnumerateArray())
        {
            var id = ReadString(realm, "id");
            var kind = ReadString(realm, "kind");
            var status = new RealmStatusSnapshot(
                ReadBool(realm, "worldReady") ?? ReadBool(realm, "isOnline") ?? false,
                Math.Max(0, ReadInt(realm, "onlineCount") ?? 0));

            if (string.Equals(id, "white", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "npk", StringComparison.OrdinalIgnoreCase))
            {
                white = status;
            }
            else if (string.Equals(id, "red", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(kind, "pk", StringComparison.OrdinalIgnoreCase))
            {
                red = status;
            }
        }

        return white is null && red is null
            ? ServerStatusSnapshot.Unknown
            : new ServerStatusSnapshot(true, white, red);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var number))
        {
            return null;
        }

        return number;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AetheriumPlay", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
