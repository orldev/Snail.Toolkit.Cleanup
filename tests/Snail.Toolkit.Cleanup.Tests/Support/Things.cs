using Snail.MCP.CleanUp.Core.Domain;

namespace Snail.Toolkit.Cleanup.Tests.Support;

/// <summary>Resources and sessions written by hand, the way a test describes a machine.</summary>
internal static class Things
{
    public static readonly Owner Me = Owner.Session("me");

    public static readonly Owner Neighbour = Owner.Session("neighbour");

    public static readonly Owner Gone = Owner.Session("gone");

    public static Resource Of(string kind, string key, Owner? owner = null, Evidence evidence = Evidence.Ledger) => new()
    {
        Kind = kind,
        Key = key,
        Title = $"{kind} {key}",
        Owner = owner ?? Owner.Nobody,
        Evidence = evidence
    };

    public static Session Alive(Owner owner) => new(owner.Id, 1, DateTimeOffset.UnixEpoch, "/work", IsAlive: true);

    public static Session Dead(Owner owner) => new(owner.Id, 2, DateTimeOffset.UnixEpoch, "/work", IsAlive: false);

    public static Inventory Inventory(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<string, string>? kept = null,
        IReadOnlyList<Discovery>? discoveries = null) =>
        new(
            DateTimeOffset.UnixEpoch,
            Me,
            resources,
            [Alive(Me), Alive(Neighbour), Dead(Gone)],
            kept ?? new Dictionary<string, string>(),
            discoveries ?? [],
            new Dictionary<string, string>());
}
