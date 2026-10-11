namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>One session of an agent, or anything else whose resources outlive it — a task, a project — and whether it is still going.</summary>
/// <param name="Id">The id the agent itself gives the session.</param>
/// <param name="Pid">The process of the agent, when it is known.</param>
/// <param name="StartedAt">When the session began.</param>
/// <param name="Directory">The directory the agent works in.</param>
/// <param name="IsAlive">Whether its process is still the same program that started it.</param>
public sealed record Session(string Id, int? Pid, DateTimeOffset? StartedAt, string? Directory, bool IsAlive)
{
    /// <summary>What kind of owner this is: a session unless a host says otherwise, such as a task of a platform.</summary>
    public string OwnerKind { get; init; } = Owner.Session(string.Empty).Kind;

    /// <summary>The owner the session's resources are written under.</summary>
    public Owner Owner => new(OwnerKind, Id);
}
