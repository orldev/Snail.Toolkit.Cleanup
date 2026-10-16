namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>An owner the host knows of — a session of an agent, a task, a project — and whether it is still going.</summary>
/// <param name="Owner">Who it is.</param>
/// <param name="IsAlive">Whether it is still going, so that what it holds is nobody else's to remove.</param>
/// <remarks>
/// A census and a plan ask an owner two things: who it is, and whether it is still going. Everything else is what some
/// owners have and others do not — when it began, when it ended, the directory it works in — so it is optional, and an
/// owner that is not an agent's session is never made to invent a process or a directory.
/// <para>
/// The record is open on purpose: a host carries what only its own owners have, such as the process of an agent's
/// session, on a record derived from this one, and its kinds read it back from the holders they are given.
/// </para>
/// </remarks>
public record Holder(Owner Owner, bool IsAlive)
{
    /// <summary>When it began, when that is known.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When it ended, when it has and that is known: what a grace after the end is counted from.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>The directory it works in, when it has one: what appears inside it while it runs is taken for its own.</summary>
    public string? Directory { get; init; }
}
