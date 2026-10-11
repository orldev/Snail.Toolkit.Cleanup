using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Core;

/// <summary>What a cleanup decides about each resource: the rules that stand between an agent and the developer's own things.</summary>
public sealed class PlannerTests
{
    private static Decision Only(Plan plan) => Assert.Single(plan.Decisions);

    private static Plan Decided(Scope scope, params Resource[] resources) =>
        Planner.Decide(Inventory(resources), scope, "plan");

    [Theory]
    [InlineData(Evidence.Ledger)]
    [InlineData(Evidence.Label)]
    [InlineData(Evidence.Ancestry)]
    public void MyResource_WithProof_IsRemoved(Evidence evidence) =>
        Assert.Equal(Verdict.Remove, Only(Decided(Scope.Mine, Of(Kinds.Process, "1@1", Me, evidence))).Verdict);

    /// <summary>Appearing during a session is a coincidence of time: the developer may have started it by hand.</summary>
    [Fact]
    public void MyResource_ThatOnlyAppearedDuringTheSession_IsReportedNotRemoved()
    {
        var decision = Only(Decided(Scope.Mine, Of(Kinds.Container, "web", Me, Evidence.Appeared)));

        Assert.Equal(Verdict.Report, decision.Verdict);
        Assert.Contains("not proof", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void OrphanedResource_WithProof_IsRemovedWhenOrphansAreAskedFor() =>
        Assert.Equal(Verdict.Remove, Only(Decided(Scope.Orphans, Of(Kinds.Simulator, "udid", Gone, Evidence.Label))).Verdict);

    [Fact]
    public void OrphanedResource_IsNotPartOfMine() =>
        Assert.Empty(Decided(Scope.Mine, Of(Kinds.Simulator, "udid", Gone, Evidence.Label)).Decisions);

    /// <summary>The lesson of `simctl shutdown all`: another running agent's simulator is never anybody else's to stop.</summary>
    [Theory]
    [InlineData(Scope.Mine)]
    [InlineData(Scope.Orphans)]
    [InlineData(Scope.Caches)]
    public void ResourceOfAnotherRunningSession_IsLeftOutOfEveryNarrowScope(Scope scope) =>
        Assert.Empty(Decided(scope, Of(Kinds.Simulator, "udid", Neighbour, Evidence.Ledger)).Decisions);

    [Fact]
    public void ResourceOfAnotherRunningSession_IsKeptEvenWhenEverythingIsAskedFor() =>
        Assert.Equal(Verdict.Keep, Only(Decided(Scope.Everything, Of(Kinds.Simulator, "udid", Neighbour, Evidence.Ledger))).Verdict);

    [Fact]
    public void KeptResource_IsKeptEvenWithProof()
    {
        var resource = Of(Kinds.Process, "1@1", Me, Evidence.Ledger);
        var plan = Planner.Decide(Inventory([resource], new Dictionary<string, string> { [resource.Id] = "dev server I use" }), Scope.Mine, "plan");

        Assert.Equal(Verdict.Keep, Only(plan).Verdict);
        Assert.Contains("dev server I use", Only(plan).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockedResource_IsReportedWithWhatBlocksIt()
    {
        var decision = Only(Decided(Scope.Mine, Of(Kinds.File, "/x", Me) with { Blocker = "Code is running" }));

        Assert.Equal(Verdict.Report, decision.Verdict);
        Assert.Equal("Code is running", decision.Reason);
    }

    [Fact]
    public void SharedCache_ThatRebuildsItself_IsRemovedOnlyWhenCachesAreAskedFor()
    {
        var cache = Of(Kinds.File, "/cache") with { IsShared = true, IsRegenerable = true };

        Assert.Equal(Verdict.Remove, Only(Decided(Scope.Caches, cache)).Verdict);
        Assert.Empty(Decided(Scope.Mine, cache).Decisions);
        Assert.Empty(Decided(Scope.Orphans, cache).Decisions);
    }

    [Fact]
    public void SharedThing_ThatDoesNotRebuildItself_IsOnlyReported() =>
        Assert.Equal(Verdict.Report, Only(Decided(Scope.Caches, Of(Kinds.File, "/state") with { IsShared = true })).Verdict);

    [Fact]
    public void UnownedResource_IsOnlyNamed_AndOnlyWhenEverythingIsAskedFor()
    {
        var stray = Of(Kinds.Container, "stray");

        Assert.Equal(Verdict.Report, Only(Decided(Scope.Everything, stray)).Verdict);
        Assert.Empty(Decided(Scope.Mine, stray).Decisions);
    }

    /// <summary>A platform's tasks own their folders the way sessions own theirs: a finished task's folder is an orphan.</summary>
    [Fact]
    public void ResourceOfAFinishedTask_IsAnOrphan_AndOfARunningOne_IsKept()
    {
        var finished = Owner.Task("done");
        var running = Owner.Task("busy");
        var inventory = Inventory([Of("workspace", "/w/done", finished, Evidence.Label), Of("workspace", "/w/busy", running, Evidence.Label)]) with
        {
            Asker = Owner.Nobody,
            Sessions = [new Session("done", null, null, null, IsAlive: false) { OwnerKind = "task" }, new Session("busy", null, null, null, IsAlive: true) { OwnerKind = "task" }]
        };

        Assert.Equal(["workspace:/w/done"], Planner.Decide(inventory, Scope.Orphans, "plan").Removals.Select(removal => removal.Resource.Id));
        Assert.Equal(Verdict.Keep, Planner.Decide(inventory, Scope.Everything, "plan").Decisions.Single(decision => decision.Resource.Key == "/w/busy").Verdict);
    }

    [Fact]
    public void Discoveries_GoToTheSessionTheyAppearedIn_AndAreNeverDecisions()
    {
        Discovery Found(Owner owner) => new(new Trace("file", $"/{owner.Id}", owner.Id), owner, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1);

        var inventory = Inventory([], discoveries: [Found(Me), Found(Gone), Found(Neighbour)]);

        Assert.Equal([Me], Planner.Decide(inventory, Scope.Mine, "plan").Discoveries.Select(discovery => discovery.Owner));
        Assert.Equal([Gone], Planner.Decide(inventory, Scope.Orphans, "plan").Discoveries.Select(discovery => discovery.Owner));
        Assert.Empty(Planner.Decide(inventory, Scope.Everything, "plan").Decisions);
    }
}
