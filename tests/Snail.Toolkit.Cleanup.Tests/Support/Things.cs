using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Tests.Support;

/// <summary>Resources and owners written by hand, the way a test describes a machine.</summary>
internal static class Things
{
    /// <summary>The kind of owner the tests' sessions are, as a host of agents names it.</summary>
    public const string SessionKind = "session";

    /// <summary>A kind of the host, not of the library: a device the programs driving it must let go of first.</summary>
    public const string Simulator = "simulator";

    public static readonly Owner Me = new(SessionKind, "me");

    public static readonly Owner Neighbour = new(SessionKind, "neighbour");

    public static readonly Owner Gone = new(SessionKind, "gone");

    /// <summary>The order of a host that has simulators: right after the processes that drive them.</summary>
    public static readonly KindOrder Order = KindOrder.General.After(Kinds.Process, Simulator);

    public static Resource Of(string kind, string key, Owner? owner = null, Evidence evidence = Evidence.Ledger) => new()
    {
        Kind = kind,
        Key = key,
        Title = $"{kind} {key}",
        Owner = owner ?? Owner.Nobody,
        Evidence = evidence
    };

    public static Holder Alive(Owner owner) => new(owner, IsAlive: true) { StartedAt = DateTimeOffset.UnixEpoch, Directory = "/work" };

    public static Holder Dead(Owner owner) => new(owner, IsAlive: false) { StartedAt = DateTimeOffset.UnixEpoch, Directory = "/work" };

    public static Inventory Inventory(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<string, string>? kept = null,
        IReadOnlyList<Discovery>? discoveries = null) =>
        new(DateTimeOffset.UnixEpoch, resources, [Alive(Me), Alive(Neighbour), Dead(Gone)])
        {
            Asker = Me,
            Kept = kept ?? new Dictionary<string, string>(),
            Discoveries = discoveries ?? []
        };
}
