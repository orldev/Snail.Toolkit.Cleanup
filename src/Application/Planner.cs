namespace Snail.Toolkit.Cleanup.Application;

/// <summary>Decides what a cleanup does with every resource a census found, and in which order.</summary>
/// <remarks>
/// A pure function of the inventory, the scope and the order, with no machine behind it, because this is the part
/// that decides whether something is destroyed: it has to be provable by tests that build an inventory by hand, not by
/// tests that need the real resources to exist.
/// </remarks>
public static class Planner
{
    /// <summary>The plan for one scope of one inventory, its removals in the order of the host's kinds.</summary>
    /// <remarks>
    /// The order is asked for rather than assumed: a host whose own kinds were left out of it would see them taken apart
    /// last, after the processes that may still be using them.
    /// </remarks>
    public static Plan Decide(Inventory inventory, Scope scope, string id, KindOrder order)
    {
        var alive = inventory.Alive;
        var decisions = new List<Decision>(inventory.Resources.Count);

        foreach (var resource in inventory.Resources)
        {
            if (Judge(resource, inventory, alive, scope) is { } decision)
                decisions.Add(decision);
        }

        return new Plan(id, inventory.Asker, scope, inventory.TakenAt, Ordering.Of(decisions, order), Discovered(inventory, alive, scope));
    }

    private static Decision? Judge(Resource resource, Inventory inventory, IReadOnlySet<Owner> alive, Scope scope)
    {
        var standing = StandingOf(resource, inventory.Asker, alive);

        if (!Covers(scope, standing))
            return null;

        if (inventory.Kept.TryGetValue(resource.Id, out var keptBecause))
            return new Decision(resource, Verdict.Keep, $"kept on request: {keptBecause}");

        return standing switch
        {
            Standing.HeldByAnother => new Decision(resource, Verdict.Keep, $"held by {resource.Owner}, which is still running"),
            Standing.Unowned => new Decision(resource, Verdict.Report, "nothing ties it to an owner"),
            _ when resource.Blocker is { } blocker => new Decision(resource, Verdict.Report, blocker),
            Standing.Shared => Shared(resource),
            _ => Owned(resource, standing)
        };
    }

    private static Decision Shared(Resource resource) =>
        resource.IsRegenerable
            ? new Decision(resource, Verdict.Remove, "a shared cache that rebuilds itself")
            : new Decision(resource, Verdict.Report, "shared, and it does not rebuild itself");

    /// <summary>Removed with proof, reported without: whether evidence is proof is <see cref="EvidenceStrength.IsProof"/>'s to say, once.</summary>
    private static Decision Owned(Resource resource, Standing standing)
    {
        var whose = standing == Standing.Orphaned ? $"{resource.Owner}, which has ended" : $"{resource.Owner}";

        if (resource.Evidence == Evidence.Appeared)
            return new Decision(resource, Verdict.Report, $"appeared while {whose} was running, which is not proof that it made it");

        if (!resource.Evidence.IsProof())
            return new Decision(resource, Verdict.Report, "nothing ties it to an owner");

        return new Decision(resource, Verdict.Remove, Proven(resource.Evidence, whose));
    }

    /// <summary>What the proof is, in the words of the evidence.</summary>
    private static string Proven(Evidence evidence, string whose) => evidence switch
    {
        Evidence.Ledger => $"recorded at birth for {whose}",
        Evidence.Label => $"its name carries {whose}",
        Evidence.Environment => $"its environment carries {whose}: it was started for it",
        Evidence.Ancestry => $"seen running under the process of {whose}",
        _ => $"proven for {whose}"
    };

    private static Standing StandingOf(Resource resource, Owner asker, IReadOnlySet<Owner> alive)
    {
        if (resource.IsShared)
            return Standing.Shared;

        if (!resource.Owner.IsKnown)
            return Standing.Unowned;

        if (resource.Owner == asker)
            return Standing.Mine;

        return alive.Contains(resource.Owner) ? Standing.HeldByAnother : Standing.Orphaned;
    }

    private static bool Covers(Scope scope, Standing standing) => scope switch
    {
        Scope.Mine => standing == Standing.Mine,
        Scope.Orphans => standing == Standing.Orphaned,
        Scope.Caches => standing == Standing.Shared,
        _ => true
    };

    private static IReadOnlyList<Discovery> Discovered(Inventory inventory, IReadOnlySet<Owner> alive, Scope scope) => scope switch
    {
        Scope.Mine => [.. inventory.Discoveries.Where(discovery => discovery.Owner == inventory.Asker)],
        Scope.Orphans => [.. inventory.Discoveries.Where(discovery => discovery.Owner != inventory.Asker && !alive.Contains(discovery.Owner))],
        Scope.Caches => [],
        _ => inventory.Discoveries
    };

    /// <summary>Where a resource stands relative to the owner asking.</summary>
    private enum Standing
    {
        Mine,
        Orphaned,
        HeldByAnother,
        Shared,
        Unowned
    }
}
