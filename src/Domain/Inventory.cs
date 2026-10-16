using System.Collections.Frozen;

namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>Everything one census found, with what it knows about who holds what.</summary>
/// <param name="TakenAt">When the census was taken.</param>
/// <param name="Resources">Every resource found, ownership already settled against the ledger.</param>
/// <param name="Holders">The owners the host knows, still going or over.</param>
/// <remarks>
/// The three positional parts are what a plan cannot be decided without. The rest has a default, so a host that keeps
/// no ledger, takes no baselines and asks for nobody in particular builds one from its resources and its owners alone.
/// </remarks>
public sealed record Inventory(DateTimeOffset TakenAt, IReadOnlyList<Resource> Resources, IReadOnlyList<Holder> Holders)
{
    /// <summary>The owner who asked for it; nobody when the host asks on its own behalf.</summary>
    public Owner Asker { get; init; } = Owner.Nobody;

    /// <summary>The ids somebody asked to be left alone, with the reason.</summary>
    public IReadOnlyDictionary<string, string> Kept { get; init; } = FrozenDictionary<string, string>.Empty;

    /// <summary>What appeared and nothing accounts for.</summary>
    public IReadOnlyList<Discovery> Discoveries { get; init; } = [];

    /// <summary>What could not be seen, kind by kind.</summary>
    public IReadOnlyDictionary<string, string> Notes { get; init; } = FrozenDictionary<string, string>.Empty;

    /// <summary>How long each kind and trail took to look, keyed <c>kind:name</c> and <c>trail:name</c>, and the whole as <c>census</c>.</summary>
    public IReadOnlyDictionary<string, TimeSpan> Timings { get; init; } = FrozenDictionary<string, TimeSpan>.Empty;

    /// <summary>The owners still going.</summary>
    /// <remarks>Built anew on every read, so that a copy made <c>with</c> other holders is never stale: read it once, outside a loop.</remarks>
    public IReadOnlySet<Owner> Alive => Holders.Where(holder => holder.IsAlive).Select(holder => holder.Owner).ToHashSet();
}
