using System.Buffers.Binary;

namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>What every trail showed when an owner began, kept as 64-bit hashes of the trace ids: all a census asks of it is whether it held an id.</summary>
/// <remarks>
/// A baseline is kept for as long as its owner is going, and on a busy machine it holds tens of thousands of ids: kept
/// as strings, the baselines of the owners going took 40 MB that never died. A hash takes eight bytes.
/// <para>
/// Two ids can share a hash. Then a trace that is new is taken for one its owner began with, and that one discovery
/// is not written down; never the other way round, since an id the baseline holds always has its own hash. With n ids
/// in a baseline a new trace is taken for an old one with odds n / 2^64: for 50 000 ids, 2.7 in 10^15 a trace — once
/// in about 7 billion censuses that each see 50 000 new traces.
/// </para>
/// </remarks>
public sealed class Baseline
{
    private const ulong Basis = 14_695_981_039_346_656_037;

    private const ulong Prime = 1_099_511_628_211;

    private readonly HashSet<ulong> _hashes = [];

    /// <summary>How many distinct hashes it holds.</summary>
    public int Count => _hashes.Count;

    /// <summary>Adds the id of a trace the owner began with.</summary>
    public void Add(string traceId) => _hashes.Add(Hash(traceId));

    /// <summary>Whether the owner began with this trace, but for the odds in the remarks.</summary>
    public bool Contains(string traceId) => _hashes.Contains(Hash(traceId));

    /// <summary>The hashes it holds, eight little-endian bytes each, in no order: a baseline whole, for a host that stores it as one value.</summary>
    /// <remarks>
    /// Stored as one row per id, the baselines of eleven sessions took 413 453 rows and 115 MB of a ledger with their index
    /// (2026-10-16); as hashes they are eight bytes an id.
    /// </remarks>
    public byte[] ToBytes()
    {
        var bytes = new byte[_hashes.Count * sizeof(ulong)];
        var offset = 0;

        foreach (var hash in _hashes)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), hash);
            offset += sizeof(ulong);
        }

        return bytes;
    }

    /// <summary>The baseline <see cref="ToBytes"/> wrote; bytes past the last whole hash are no hash and are passed over.</summary>
    public static Baseline FromBytes(ReadOnlySpan<byte> bytes)
    {
        var baseline = new Baseline();

        for (var offset = 0; offset + sizeof(ulong) <= bytes.Length; offset += sizeof(ulong))
            baseline._hashes.Add(BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]));

        return baseline;
    }

    /// <summary>FNV-1a over the UTF-16 code units of an id: 64 bits, where <see cref="string.GetHashCode()"/> has 32 and the odds above would be four billion times worse.</summary>
    private static ulong Hash(ReadOnlySpan<char> traceId)
    {
        var hash = Basis;

        foreach (var unit in traceId)
        {
            hash ^= unit;
            hash *= Prime;
        }

        return hash;
    }
}
