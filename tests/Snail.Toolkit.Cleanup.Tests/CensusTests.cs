using Microsoft.Extensions.Time.Testing;
using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;
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
            new FakeHolders(Me, Alive(Me), Dead(Gone)),
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
    public async Task Inventory_KnowsWhichOwnersAreAlive() =>
        Assert.Equal([Me], (await Census().TakeAsync(Me, CancellationToken.None)).Alive);

    /// <summary>A host carries what only its own owners have on a record of its own, and its kinds get that record back.</summary>
    [Fact]
    public async Task HoldersOfTheHostsOwnShape_ReachItsKinds_AsTheHostMadeThem()
    {
        var watching = new WatchingKind();
        var census = new Census(
            [watching],
            [],
            new FakeHolders(Me, new AgentSession(Me, 4242)),
            Ledger,
            new JsonRules(_scratch.File("rules.json"), []),
            _baselines,
            _discoveries,
            new FakeTimeProvider(DateTimeOffset.UnixEpoch));

        await census.TakeAsync(Me, CancellationToken.None);

        Assert.Equal(4242, Assert.IsType<AgentSession>(Assert.Single(watching.Given)).Pid);
    }

    public void Dispose() => _scratch.Dispose();

    /// <summary>What a host of agents knows of its sessions beyond what the library asks: the process of the agent.</summary>
    private sealed record AgentSession(Owner Owner, int Pid) : Holder(Owner, IsAlive: true);

    /// <summary>A kind that remembers the holders it was given.</summary>
    private sealed class WatchingKind : IResourceKind
    {
        public IReadOnlyList<Holder> Given { get; private set; } = [];

        public string Name => Kinds.Process;

        public Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
        {
            Given = holders;

            return Task.FromResult(new Observation(Name, [], []));
        }

        public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) => Task.FromResult(Release.AlreadyGone(resource));
    }
}
