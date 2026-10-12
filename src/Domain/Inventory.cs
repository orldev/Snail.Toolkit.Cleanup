namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>Everything one census found on the machine, with what it knows about who holds what.</summary>
/// <param name="TakenAt">When the census was taken.</param>
/// <param name="Asker">The owner who asked for it.</param>
/// <param name="Resources">Every resource found, ownership already settled against the ledger.</param>
/// <param name="Sessions">The sessions the machine knows, alive or not.</param>
/// <param name="Kept">The ids somebody asked to be left alone, with the reason.</param>
/// <param name="Discoveries">What appeared and nothing accounts for.</param>
/// <param name="Notes">What could not be seen, kind by kind.</param>
public sealed record Inventory(
    DateTimeOffset TakenAt,
    Owner Asker,
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<Session> Sessions,
    IReadOnlyDictionary<string, string> Kept,
    IReadOnlyList<Discovery> Discoveries,
    IReadOnlyDictionary<string, string> Notes)
{
    /// <summary>How long each kind and trail took to look, keyed <c>kind:name</c> and <c>trail:name</c>, and the whole as <c>census</c>.</summary>
    public IReadOnlyDictionary<string, TimeSpan> Timings { get; init; } = new Dictionary<string, TimeSpan>();

    /// <summary>The owners whose sessions are still running.</summary>
    /// <remarks>Built anew on every read, so that a copy made <c>with</c> other sessions is never stale: read it once, outside a loop.</remarks>
    public IReadOnlySet<Owner> Alive => Sessions.Where(session => session.IsAlive).Select(session => session.Owner).ToHashSet();
}
