using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiRoblox.Core.Services;

/// <summary>
/// Read-only game/discovery endpoints used by the server browser, recent-games / favorites lists and
/// the player finder. Uses a single account's <see cref="RobloxClient"/> for auth where required.
/// </summary>
public sealed class GamesClient
{
    private readonly RobloxClient _client;
    private readonly HttpClient _http;

    public GamesClient(RobloxClient client)
    {
        _client = client;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MultiRoblox/1.0");
    }

    // --- Server browser -------------------------------------------------

    public async Task<ServerPage> GetPublicServersAsync(
        long placeId, string? cursor = null, string sortOrder = "Asc", int limit = 100, CancellationToken ct = default)
    {
        string url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder={sortOrder}&limit={limit}";
        if (!string.IsNullOrEmpty(cursor)) url += $"&cursor={Uri.EscapeDataString(cursor)}";
        var page = await _http.GetFromJsonAsync<ServerPage>(url, ct);
        return page ?? new ServerPage();
    }

    /// <summary>All public servers, following pagination (bounded by <paramref name="maxPages"/>).</summary>
    public async Task<IReadOnlyList<GameServer>> GetAllPublicServersAsync(
        long placeId, int maxPages = 10, CancellationToken ct = default)
    {
        var all = new List<GameServer>();
        string? cursor = null;
        for (int i = 0; i < maxPages; i++)
        {
            var page = await GetPublicServersAsync(placeId, cursor, ct: ct);
            all.AddRange(page.Data);
            if (string.IsNullOrEmpty(page.NextPageCursor)) break;
            cursor = page.NextPageCursor;
        }
        return all;
    }

    public async Task<GameServer?> GetSmallestJoinableServerAsync(long placeId, CancellationToken ct = default)
        => (await GetAllPublicServersAsync(placeId, ct: ct))
            .Where(s => s.Playing < s.MaxPlayers)
            .OrderBy(s => s.Playing)
            .FirstOrDefault();

    // --- Recent games & favorites -------------------------------------

    /// <summary>The signed-in user's recently-played games.</summary>
    public async Task<IReadOnlyList<GameSummary>> GetRecentGamesAsync(long userId, CancellationToken ct = default)
    {
        // omni-recommendations / games list — recently played sort
        var json = await AuthedGetAsync(
            $"https://games.roblox.com/v2/users/{userId}/games?sortOrder=Desc&limit=25", ct);
        return ParseGameSummaries(json);
    }

    public async Task<IReadOnlyList<GameSummary>> GetFavoriteGamesAsync(long userId, CancellationToken ct = default)
    {
        var json = await AuthedGetAsync(
            $"https://games.roblox.com/v2/users/{userId}/favorite/games?sortOrder=Desc&limit=50", ct);
        return ParseGameSummaries(json);
    }

    /// <summary>Resolve a place id to its game name (for a locally-kept favorites list).</summary>
    public async Task<string?> GetPlaceNameAsync(long placeId, CancellationToken ct = default)
    {
        try
        {
            var json = await AuthedGetAsync(
                $"https://games.roblox.com/v1/games/multiget-place-details?placeIds={placeId}", ct);
            if (json.ValueKind == JsonValueKind.Array && json.GetArrayLength() > 0)
            {
                var e = json[0];
                if (e.TryGetProperty("name", out var n)) return n.GetString();
            }
        }
        catch { }

        // Fallback: universe lookup (no auth needed).
        try
        {
            var u = await _http.GetFromJsonAsync<JsonElement>(
                $"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct);
            if (u.TryGetProperty("universeId", out var uid))
            {
                var g = await _http.GetFromJsonAsync<JsonElement>(
                    $"https://games.roblox.com/v1/games?universeIds={uid.GetInt64()}", ct);
                if (g.TryGetProperty("data", out var d) && d.GetArrayLength() > 0
                    && d[0].TryGetProperty("name", out var name))
                    return name.GetString();
            }
        }
        catch { }
        return null;
    }

    // --- Player finder (presence) ------------------------------------

    public sealed record PlayerLocation(long UserId, int PresenceType, long? PlaceId, string? GameId, string? LastLocation);

    public async Task<IReadOnlyList<PlayerLocation>> FindPlayersAsync(IEnumerable<long> userIds, CancellationToken ct = default)
    {
        var el = await _client.GetPresencesAsync(userIds, ct);
        var list = new List<PlayerLocation>();
        if (el.TryGetProperty("userPresences", out var arr))
        {
            foreach (var p in arr.EnumerateArray())
            {
                list.Add(new PlayerLocation(
                    p.GetProperty("userId").GetInt64(),
                    p.TryGetProperty("userPresenceType", out var t) ? t.GetInt32() : 0,
                    p.TryGetProperty("placeId", out var pl) && pl.ValueKind == JsonValueKind.Number ? pl.GetInt64() : null,
                    p.TryGetProperty("gameId", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null,
                    p.TryGetProperty("lastLocation", out var ll) ? ll.GetString() : null));
            }
        }
        return list;
    }

    public async Task<long?> ResolveUsernameAsync(string username, CancellationToken ct = default)
    {
        using var res = await _http.PostAsJsonAsync(
            "https://users.roblox.com/v1/usernames/users",
            new { usernames = new[] { username }, excludeBannedUsers = false }, ct);
        res.EnsureSuccessStatusCode();
        var el = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (el.TryGetProperty("data", out var d) && d.GetArrayLength() > 0)
            return d[0].GetProperty("id").GetInt64();
        return null;
    }

    // --- helpers -----------------------------------------------------

    private async Task<JsonElement> AuthedGetAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={_client.CurrentToken}");
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    private static IReadOnlyList<GameSummary> ParseGameSummaries(JsonElement json)
    {
        var list = new List<GameSummary>();
        if (!json.TryGetProperty("data", out var data)) return list;
        foreach (var g in data.EnumerateArray())
        {
            list.Add(new GameSummary
            {
                UniverseId = g.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                PlaceId = g.TryGetProperty("rootPlace", out var rp) && rp.TryGetProperty("id", out var pid)
                    ? pid.GetInt64() : 0,
                Name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            });
        }
        return list;
    }
}

public sealed class ServerPage
{
    [JsonPropertyName("nextPageCursor")] public string? NextPageCursor { get; set; }
    [JsonPropertyName("previousPageCursor")] public string? PreviousPageCursor { get; set; }
    [JsonPropertyName("data")] public List<GameServer> Data { get; set; } = new();
}

public sealed class GameServer
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("maxPlayers")] public int MaxPlayers { get; set; }
    [JsonPropertyName("playing")] public int Playing { get; set; }
    [JsonPropertyName("playerTokens")] public List<string> PlayerTokens { get; set; } = new();
    [JsonPropertyName("ping")] public int Ping { get; set; }
    [JsonPropertyName("fps")] public double Fps { get; set; }
}

public sealed class GameSummary
{
    public long UniverseId { get; set; }
    public long PlaceId { get; set; }
    public string Name { get; set; } = "";
}
