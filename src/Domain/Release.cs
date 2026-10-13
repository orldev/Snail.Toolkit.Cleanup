namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>How releasing one resource ended.</summary>
public enum Outcome
{
    /// <summary>It is gone, and this release is why.</summary>
    Released,

    /// <summary>It was already gone; nothing was done.</summary>
    Gone,

    /// <summary>It is no longer the thing the plan described, so it was left alone.</summary>
    Changed,

    /// <summary>Releasing it failed; the reason says how.</summary>
    Failed
}

/// <summary>The end of releasing one resource.</summary>
/// <param name="ResourceId">What was released.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Detail">What happened, in words: where a file went, which signal ended a process, why it failed.</param>
/// <param name="Bytes">Space it gave back, when known.</param>
public sealed record Release(string ResourceId, Outcome Outcome, string Detail, long? Bytes = null)
{
    /// <summary>Whether <see cref="Bytes"/> is only what was counted before the count stopped, so that it gave back at least that much.</summary>
    public bool IsAtLeast { get; init; }

    /// <summary>Released now.</summary>
    public static Release Done(Resource resource, string detail) => new(resource.Id, Outcome.Released, detail, resource.Bytes) { IsAtLeast = resource.IsAtLeast };

    /// <summary>Already gone.</summary>
    public static Release AlreadyGone(Resource resource) => new(resource.Id, Outcome.Gone, "already gone");

    /// <summary>No longer what the plan described.</summary>
    public static Release NotTheSame(Resource resource, string detail) => new(resource.Id, Outcome.Changed, detail);

    /// <summary>Releasing it failed.</summary>
    public static Release Refused(Resource resource, string detail) => new(resource.Id, Outcome.Failed, detail);
}
