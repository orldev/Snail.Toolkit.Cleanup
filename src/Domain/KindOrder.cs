using System.Collections.Frozen;

namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>The order kinds of resource are taken apart in: the general kinds, with whatever kinds a host places among them.</summary>
/// <remarks>
/// The order is what makes a release safe without a kind having to know its neighbours: a process goes before the
/// directory it writes into, a container before the network and the volume it holds and the image it runs. A kind the
/// order does not name goes last, after everything that could still be using it. A resource can still name what it must
/// follow, and that wins over the order.
/// <para>
/// A host places its own kinds where they belong — the programs driving a device after the processes and before the
/// device, the device before the containers — with <see cref="After"/> and <see cref="Before"/>, each of which answers
/// a new order and leaves this one as it was. The ranks are looked up in a frozen table made once per order: a plan
/// asks for the rank of every resource it orders.
/// </para>
/// </remarks>
public sealed class KindOrder
{
    private readonly FrozenDictionary<string, int> _ranks;

    /// <summary>An order of these kinds, the first taken apart first.</summary>
    /// <exception cref="ArgumentException">A kind is named twice, so that it would have two places.</exception>
    public KindOrder(IEnumerable<string> kinds)
    {
        Listed = [.. kinds];

        var ranks = new Dictionary<string, int>(Listed.Count, StringComparer.Ordinal);

        for (var position = 0; position < Listed.Count; position++)
        {
            if (!ranks.TryAdd(Listed[position], position))
                throw new ArgumentException($"the kind {Listed[position]} is named twice", nameof(kinds));
        }

        _ranks = ranks.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The general kinds, in their order: <c>process</c>, <c>container</c>, <c>network</c>, <c>volume</c>, <c>image</c>, <c>file</c>.</summary>
    public static KindOrder General { get; } = new([Kinds.Process, Kinds.Container, Kinds.Network, Kinds.Volume, Kinds.Image, Kinds.File]);

    /// <summary>The kinds in the order they are taken apart.</summary>
    public IReadOnlyList<string> Listed { get; }

    /// <summary>Where a kind stands; a kind not listed stands after all of them.</summary>
    public int Rank(string kind) => _ranks.TryGetValue(kind, out var position) ? position : Listed.Count;

    /// <summary>This order with the kinds placed right after one it lists, in the order given.</summary>
    /// <exception cref="ArgumentException">The kind to place them after is not listed, or one of them already is.</exception>
    public KindOrder After(string kind, params string[] kinds) => Placed(At(kind) + 1, kinds);

    /// <summary>This order with the kinds placed right before one it lists, in the order given.</summary>
    /// <exception cref="ArgumentException">The kind to place them before is not listed, or one of them already is.</exception>
    public KindOrder Before(string kind, params string[] kinds) => Placed(At(kind), kinds);

    private int At(string kind) =>
        _ranks.TryGetValue(kind, out var position) ? position : throw new ArgumentException($"the kind {kind} is not in the order", nameof(kind));

    private KindOrder Placed(int position, string[] kinds) => new([.. Listed.Take(position), .. kinds, .. Listed.Skip(position)]);
}
