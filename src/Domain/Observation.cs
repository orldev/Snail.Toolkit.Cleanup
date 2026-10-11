namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>What one kind saw: its resources, the traces they account for, and anything it could not see.</summary>
/// <param name="Kind">The kind that looked.</param>
/// <param name="Resources">What it found.</param>
/// <param name="Claimed">The trace ids its resources account for, so they are not mistaken for discoveries.</param>
/// <param name="Note">Why the answer may be incomplete, such as a daemon that is not running; null when it is complete.</param>
public sealed record Observation(string Kind, IReadOnlyList<Resource> Resources, IReadOnlyList<string> Claimed, string? Note = null)
{
    /// <summary>A kind that looked and could not see, with the reason.</summary>
    public static Observation Unseen(string kind, string note) => new(kind, [], [], note);
}
