namespace MultiRoblox.Core.Models;

public enum JoinKind
{
    /// <summary>Join any available public server for the place.</summary>
    FollowPlace,
    /// <summary>Join a specific server (JobId).</summary>
    SpecificServer,
    /// <summary>Join a private / VIP server via access + link codes.</summary>
    PrivateServer,
    /// <summary>Follow a user into whatever game they are in.</summary>
    FollowUser,
}

/// <summary>Describes where an account should be launched.</summary>
public sealed class JoinRequest
{
    public JoinKind Kind { get; init; } = JoinKind.FollowPlace;
    public long PlaceId { get; init; }
    public string? JobId { get; init; }
    public string? AccessCode { get; init; }
    public string? LinkCode { get; init; }
    public long FollowUserId { get; init; }

    public static JoinRequest Place(long placeId) => new() { Kind = JoinKind.FollowPlace, PlaceId = placeId };

    public static JoinRequest Server(long placeId, string jobId) =>
        new() { Kind = JoinKind.SpecificServer, PlaceId = placeId, JobId = jobId };

    public static JoinRequest Private(long placeId, string accessCode, string? linkCode = null) =>
        new() { Kind = JoinKind.PrivateServer, PlaceId = placeId, AccessCode = accessCode, LinkCode = linkCode };
}
