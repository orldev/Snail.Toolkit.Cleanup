namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>One resource, what is to be done with it, and why.</summary>
public sealed record Decision(Resource Resource, Verdict Verdict, string Reason);

/// <summary>What a cleanup would do, decided and shown before anything is done.</summary>
/// <param name="Id">The id <c>apply</c> is given, so that only what was shown is carried out.</param>
/// <param name="Asker">The owner the plan was made for.</param>
/// <param name="Scope">Which resources it is about.</param>
/// <param name="MadeAt">When it was decided.</param>
/// <param name="Decisions">Every resource in scope with its verdict, removals in the order they are carried out.</param>
/// <param name="Discoveries">What appeared that no kind accounts for: named, never removed.</param>
public sealed record Plan(
    string Id,
    Owner Asker,
    Scope Scope,
    DateTimeOffset MadeAt,
    IReadOnlyList<Decision> Decisions,
    IReadOnlyList<Discovery> Discoveries)
{
    /// <summary>How long a plan may be carried out after it was shown.</summary>
    /// <remarks>
    /// The machine moves while a plan waits: a process ends, its id is given to another. Past this the plan
    /// describes a machine that no longer exists, and is made again rather than trusted.
    /// </remarks>
    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(10);

    /// <summary>The decisions that release something, in the order they are carried out.</summary>
    public IReadOnlyList<Decision> Removals => [.. Decisions.Where(decision => decision.Verdict == Verdict.Remove)];

    /// <summary>Whether the plan is still fresh enough to be carried out at this moment.</summary>
    public bool IsFresh(DateTimeOffset now) => now - MadeAt <= Lifetime;
}
