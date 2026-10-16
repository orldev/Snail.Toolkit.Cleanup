using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.Toolkit.Cleanup.Tests.Support.Things;

namespace Snail.Toolkit.Cleanup.Tests;

public sealed class CensusTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly FakeKind _processes = new(Kinds.Process);

    private readonly MemoryLedger _ledger = new();

    private readonly MemoryBaselines _baselines = new();

    private readonly MemoryDiscoveries _discoveries = new();

    private MemoryLedger Ledger => _ledger;

    private Census Census(params FakeKind[] kinds) =>
        new(
            kinds.Length > 0 ? kinds : [_processes],
            [],
            new FakeSessions(Me, Alive(Me), Dead(Gone)),
            Ledger,
            new JsonRules(_scratch.File("rules.json"), []),
            _baselines,
            _discoveries,
            new FakeTimeProvider(DateTimeOffset.UnixEpoch));

    /// <summary>Once the agent has died, its server is a child of nothing; only the ledger still says whose it was.</summary>
    [Fact]
    public async Task Ownership_WrittenInTheLedger_OutranksWhatLookingShows()
    {
        _processes.Present.Add(Of(Kinds.Process, "7@1"));
        await Ledger.RecordAsync(new Holding("process:7@1", Gone, Evidence.Ancestry, DateTimeOffset.UnixEpoch, "server"), CancellationToken.None);

        var resource = Assert.Single((await Census().TakeAsync(Me, CancellationToken.None)).Resources);

        Assert.Equal(Gone, resource.Owner);
        Assert.Equal(Evidence.Ancestry, resource.Evidence);
    }

    [Fact]
    public async Task ResourceSeenUnderARunningAgent_IsWrittenDown_SoItIsStillKnownAfterTheAgentDies()
    {
        _processes.Present.Add(Of(Kinds.Process, "7@1", Me, Evidence.Ancestry));

        await Census().TakeAsync(Me, CancellationToken.None);

        var holding = Assert.Single(await Ledger.HoldingsAsync(CancellationToken.None));

        Assert.Equal(Me, holding.Owner);
    }

    [Fact]
    public async Task Holding_OfAResourceNoLongerThere_IsCrossedOff()
    {
        await Ledger.RecordAsync(new Holding("process:7@1", Me, Evidence.Ledger, DateTimeOffset.UnixEpoch.AddMinutes(-1), "ended"), CancellationToken.None);

        await Census().TakeAsync(Me, CancellationToken.None);

        Assert.Empty(await Ledger.HoldingsAsync(CancellationToken.None));
    }

    /// <summary>A kind that could not look says nothing about what is gone: docker being stopped does not mean the containers are.</summary>
    [Fact]
    public async Task Holding_OfAKindThatCouldNotLook_IsKept_AndTheCensusSaysWhy()
    {
        var containers = new FakeKind(Kinds.Container) { Blindness = "docker is not running" };

        await Ledger.RecordAsync(new Holding("container:web", Me, Evidence.Label, DateTimeOffset.UnixEpoch, "web"), CancellationToken.None);

        var inventory = await Census(containers).TakeAsync(Me, CancellationToken.None);

        Assert.Single(await Ledger.HoldingsAsync(CancellationToken.None));
        Assert.Contains("docker is not running", inventory.Notes[Kinds.Container], StringComparison.Ordinal);
    }

    /// <summary>A program written down a moment ago may be missing from a process table read a moment before.</summary>
    [Fact]
    public async Task Holding_WrittenAMomentAgo_IsNotCrossedOffForNotBeingSeenYet()
    {
        await Ledger.RecordAsync(new Holding("process:7@1", Me, Evidence.Ledger, DateTimeOffset.UnixEpoch, "just started"), CancellationToken.None);

        await Census().TakeAsync(Me, CancellationToken.None);

        Assert.Single(await Ledger.HoldingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Inventory_KnowsWhichSessionsAreAlive() =>
        Assert.Equal([Me], (await Census().TakeAsync(Me, CancellationToken.None)).Alive);

    public void Dispose() => _scratch.Dispose();
}
