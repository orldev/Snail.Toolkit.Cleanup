using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.Toolkit.Cleanup.Tests.Support.Things;

namespace Snail.Toolkit.Cleanup.Tests;

public sealed class CleanerTests
{
    private readonly FakeKind _processes = new(Kinds.Process);

    private readonly FakeKind _files = new(Kinds.File);

    private Plan Planned(params Resource[] resources) => Planner.Decide(Inventory(resources), Scope.Mine, "plan");

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

    [Fact]
    public async Task Resource_OfAKindNothingHereReleases_IsRefusedWithTheReason()
    {
        var release = Assert.Single(await Cleaner.ApplyAsync(Planned(Of(Kinds.Simulator, "udid", Me)), CancellationToken.None));

        Assert.Equal(Outcome.Failed, release.Outcome);
        Assert.Contains(Kinds.Simulator, release.Detail, StringComparison.Ordinal);
    }
}
