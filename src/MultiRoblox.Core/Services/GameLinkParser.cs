using System.Text.RegularExpressions;
using MultiRoblox.Core.Models;

namespace MultiRoblox.Core.Services;

public sealed record ParsedGameLink(long PlaceId, string? JobId = null, string? PrivateServerLinkCode = null, string? AccessCode = null)
{
    public JoinRequest ToJoinRequest()
    {
        if (!string.IsNullOrWhiteSpace(AccessCode) || !string.IsNullOrWhiteSpace(PrivateServerLinkCode))
            return new JoinRequest
            {
                Kind = JoinKind.PrivateServer,
                PlaceId = PlaceId,
                AccessCode = AccessCode,
                LinkCode = PrivateServerLinkCode,
            };
        if (!string.IsNullOrWhiteSpace(JobId))
            return JoinRequest.Server(PlaceId, JobId!);
        return JoinRequest.Place(PlaceId);
    }
}

/// <summary>
/// Accepts whatever the user pastes into the Place ID box: a bare id, a full roblox.com game URL
/// (optionally with <c>?privateServerLinkCode=</c>), a <c>roblox://</c> / <c>roblox-player://</c>
/// deep link, or a share link. Returns the place id plus any server/private-server hints.
/// </summary>
public static class GameLinkParser
{
    private static readonly Regex GamesPath = new(@"/games/(\d+)", RegexOptions.Compiled);
    private static readonly Regex AnyLongNumber = new(@"\b(\d{5,})\b", RegexOptions.Compiled);

    public static bool TryParse(string? input, out ParsedGameLink result)
    {
        result = new ParsedGameLink(0);
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();

        // 1. bare id
        if (long.TryParse(input, out long bare) && bare > 0)
        {
            result = new ParsedGameLink(bare);
            return true;
        }

        // A URL must actually be a Roblox one.
        bool looksLikeUrl = input.Contains("://") || input.Contains(".com", StringComparison.OrdinalIgnoreCase)
                                                 || input.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
        if (looksLikeUrl && !input.Contains("roblox", StringComparison.OrdinalIgnoreCase))
            return false;

        // 2. URL / deep link with a query string
        string? query = null;
        long placeId = 0;

        int q = input.IndexOf('?');
        if (q >= 0)
        {
            query = input[(q + 1)..];
            string head = input[..q];
            var pm = GamesPath.Match(head);
            if (pm.Success) long.TryParse(pm.Groups[1].Value, out placeId);
        }
        else
        {
            var pm = GamesPath.Match(input);
            if (pm.Success) long.TryParse(pm.Groups[1].Value, out placeId);
        }

        string? jobId = null, linkCode = null, accessCode = null;
        if (query is not null)
        {
            // strip a trailing #fragment, then split key=value pairs
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query[..hash];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string key = pair[..eq].Trim().ToLowerInvariant();
                string val = Uri.UnescapeDataString(pair[(eq + 1)..].Trim());
                if (val.Length == 0) continue;
                switch (key)
                {
                    case "placeid": long.TryParse(val, out placeId); break;
                    case "gameid" or "jobid": jobId = val; break;
                    case "privateserverlinkcode" or "linkcode": linkCode = val; break;
                    case "accesscode": accessCode = val; break;
                }
            }
        }

        // 3. roblox://placeId=123 style with no '?'
        if (placeId == 0)
        {
            var m = Regex.Match(input, @"placeId[=:](\d+)", RegexOptions.IgnoreCase);
            if (m.Success) long.TryParse(m.Groups[1].Value, out placeId);
        }

        // 4. last resort: any long number in the string (covers odd share URLs)
        if (placeId == 0 && (input.Contains("roblox.com", StringComparison.OrdinalIgnoreCase) || input.StartsWith("roblox")))
        {
            var m = AnyLongNumber.Match(input);
            if (m.Success) long.TryParse(m.Groups[1].Value, out placeId);
        }

        if (placeId <= 0) return false;
        result = new ParsedGameLink(placeId, jobId, linkCode, accessCode);
        return true;
    }
}
