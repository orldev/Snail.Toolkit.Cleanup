using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.Toolkit.Cleanup.Tests.Support.Things;

namespace Snail.Toolkit.Cleanup.Tests;

public sealed class CleanerTests
{
    private readonly FakeKind _processes = new(Kinds.Process);

    private readonly FakeKind _files = new(Kinds.File);

    private Plan Planned(params Resource[] resources) => Planner.Decide(Inventory(resources), Scope.Mine, "plan", Order);

    private readonly MemoryLedger _ledger = new();

    private Cleaner Cleaner => new([_processes, _files], _ledger);

    [Fact]
    public async Task Removals_AreCarriedOutInThePlansOrder_AndCrossedOffTheLedger()
    {
        var process = Of(Kinds.Process, "7@1", Me);
        var file = Of(Kinds.File, "/scratch", Me);
        var ledger = _ledger;

        await ledger.RecordAsync(new Holding(process.Id, Me, Evidence.Ledger, DateTimeOffset.UnixEpoch, "p"), CancellationToken.None);

        var releases = await Cleaner.ApplyAsync(Planned(file, process), CancellationToken.None);

        Assert.Equal([process.Id, file.Id], releases.Select(release => release.ResourceId));
        Assert.All(releases, release => Assert.Equal(Outcome.Released, release.Outcome));
        Assert.Empty(await ledger.HoldingsAsync(CancellationToken.None));
    }

    /// <summary>The order exists because the later one depends on the earlier being gone.</summary>
    [Fact]
    public async Task Resource_WhosePredecessorFailedToGo_IsNotAttempted()
    {
        var process = Of(Kinds.Process, "7@1", Me);
        var file = Of(Kinds.File, "/log", Me) with { After = [process.Id] };

        _processes.Refusing.Add(process.Id);

        var releases = await Cleaner.ApplyAsync(Planned(process, file), CancellationToken.None);

        Assert.All(releases, release => Assert.Equal(Outcome.Failed, release.Outcome));
        Assert.Empty(_files.Released);
    }

    /// <summary>
    /// The plan took Gone for ended; by the time it is carried out Gone is going again — a session resumed under its id.
    /// What it holds stays, and the release says why.
    /// </summary>
    [Fact]
    public async Task Orphan_WhoseOwnerIsGoingAgainAtTheApply_IsLeftAlone()
    {
        var scratchpad = Of(Kinds.File, "/scratch/gone", Gone);
        var plan = Planner.Decide(Inventory([scratchpad]), Scope.Orphans, "plan", Order);
        var cleaner = new Cleaner([_files], _ledger, new FakeHolders(Me, Alive(Me), Alive(Gone)));

        var release = Assert.Single(await cleaner.ApplyAsync(plan, CancellationToken.None));

        Assert.Equal(Outcome.Changed, release.Outcome);
        Assert.Contains("going again", release.Detail, StringComparison.Ordinal);
        Assert.Empty(_files.Released);
    }

    [Fact]
    public async Task Orphan_WhoseOwnerIsStillOverAtTheApply_IsReleased()
    {
        var scratchpad = Of(Kinds.File, "/scratch/gone", Gone);
        var plan = Planner.Decide(Inventory([scratchpad]), Scope.Orphans, "plan", Order);
        var cleaner = new Cleaner([_files], _ledger, new FakeHolders(Me, Alive(Me), Dead(Gone)));

        var release = Assert.Single(await cleaner.ApplyAsync(plan, CancellationToken.None));

        Assert.Equal(Outcome.Released, release.Outcome);
    }

    /// <summary>What the asker holds is the asker's to remove, alive as the asker is.</summary>
    [Fact]
    public async Task AskersOwn_WithTheAskerGoing_IsReleased()
    {
        var file = Of(Kinds.File, "/scratch/me", Me);
        var cleaner = new Cleaner([_files], _ledger, new FakeHolders(Me, Alive(Me)));

        var release = Assert.Single(await cleaner.ApplyAsync(Planned(file), CancellationToken.None));

        Assert.Equal(Outcome.Released, release.Outcome);
    }

    [Fact]
    public async Task Resource_OfAKindNothingHereReleases_IsRefusedWithTheReason()
    {
        var release = Assert.Single(await Cleaner.ApplyAsync(Planned(Of(Simulator, "udid", Me)), CancellationToken.None));

        Assert.Equal(Outcome.Failed, release.Outcome);
        Assert.Contains(Simulator, release.Detail, StringComparison.Ordinal);
    }
}
