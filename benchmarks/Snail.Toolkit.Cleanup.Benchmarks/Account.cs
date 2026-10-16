using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Benchmarks;

/// <summary>A ledger in memory, so that a census benchmark measures the census and not a database.</summary>
internal sealed class MemoryLedger : ILedger
{
    private readonly Dictionary<string, Holding> _holdings = new(StringComparer.Ordinal);

    public Task RecordAsync(Holding holding, CancellationToken cancellationToken)
    {
        if (!_holdings.TryGetValue(holding.ResourceId, out var known) || holding.Evidence >= known.Evidence)
            _holdings[holding.ResourceId] = holding;

        return Task.CompletedTask;
    }

    public Task ForgetAsync(string resourceId, CancellationToken cancellationToken)
    {
        _holdings.Remove(resourceId);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Holding>> HoldingsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Holding>>([.. _holdings.Values]);

    public Task KeepAsync(string resourceId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> TryUnkeepAsync(string resourceId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<IReadOnlyDictionary<string, string>> KeptAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}

/// <summary>Baselines in memory, taken once per owner.</summary>
internal sealed class MemoryBaselines : IBaselines
{
    private readonly Dictionary<Owner, Baseline> _taken = [];

    public Task<Baseline?> FindAsync(Owner owner, CancellationToken cancellationToken) => Task.FromResult(_taken.GetValueOrDefault(owner));

    public Task SaveAsync(Owner owner, IReadOnlyList<string> traceIds, DateTimeOffset takenAt, CancellationToken cancellationToken)
    {
        if (_taken.ContainsKey(owner))
            return Task.CompletedTask;

        var baseline = new Baseline();

        foreach (var id in traceIds)
            baseline.Add(id);

        _taken[owner] = baseline;

        return Task.CompletedTask;
    }

    public Task KeepOnlyAsync(IReadOnlySet<Owner> owners, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Discoveries in memory, keyed by trace: counted when seen again, as a host's record counts them.</summary>
internal sealed class MemoryDiscoveries : IDiscoveries
{
    private readonly Dictionary<string, Discovery> _written = new(StringComparer.Ordinal);

    public Task NoteAsync(IReadOnlyList<Discovery> discoveries, CancellationToken cancellationToken)
    {
        foreach (var discovery in discoveries)
        {
            _written[discovery.Trace.Id] = _written.TryGetValue(discovery.Trace.Id, out var known)
                ? known with { LastSeen = discovery.LastSeen, Sightings = known.Sightings + 1 }
                : discovery;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Discovery>> ListAsync(IReadOnlySet<string> present, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var listed = new List<Discovery>(_written.Count);

        foreach (var discovery in _written.Values)
            listed.Add(discovery with { LastSeen = now, IsPresent = present.Contains(discovery.Trace.Id) });

        return Task.FromResult<IReadOnlyList<Discovery>>(listed);
    }

    public Task<int> SettleAsync(Rule rule, CancellationToken cancellationToken) => Task.FromResult(0);
}

/// <summary>A kind that looks by following a trail, the way a host's file kind does, and hands the census what it walked.</summary>
internal sealed class WalkingKind(string name, IReadOnlyList<Resource> resources, IReadOnlyList<Trace> walked) : IResourceKind
{
    public string Name => name;

    public Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken) =>
        Task.FromResult(new Observation(Name, resources, []) { Followed = new Walk(Name, walked) });

    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) => Task.FromResult(Release.AlreadyGone(resource));
}

/// <summary>A trail that always shows the same traces.</summary>
internal sealed class StillTrail(string name, IReadOnlyList<Trace> traces) : ITrail
{
    public string Name => name;

    public Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken) => Task.FromResult(traces);

    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) => Task.FromResult(trace with { Bytes = 4_096 });
}

/// <summary>The owners of the benchmark, always the same.</summary>
internal sealed class StillHolders(params Holder[] holders) : IHolders
{
    public Task<IReadOnlyList<Holder>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Holder>>(holders);

    public Task<Owner> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(holders[0].Owner);
}

/// <summary>Rules that never change.</summary>
internal sealed class StillRules(IReadOnlyList<Rule> rules) : IRules
{
    public Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(rules);

    public Task AddAsync(Rule rule, CancellationToken cancellationToken) => Task.CompletedTask;
}
