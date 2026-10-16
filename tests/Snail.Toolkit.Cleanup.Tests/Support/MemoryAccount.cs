using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Tests.Support;

/// <summary>A ledger kept in memory, with the promises a host's ledger keeps: a weaker sighting never overwrites a stronger one.</summary>
internal sealed class MemoryLedger : ILedger
{
    private readonly Dictionary<string, Holding> _holdings = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _kept = new(StringComparer.Ordinal);

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

    public Task<IReadOnlyList<Holding>> HoldingsAsync(CancellationToken cancellationToken)
    {
        List<Holding> holdings =
        [
            .. _holdings.Values
                .OrderBy(holding => holding.RecordedAt)
                .Select(holding => holding with { KeptBecause = _kept.GetValueOrDefault(holding.ResourceId) })
        ];

        return Task.FromResult<IReadOnlyList<Holding>>(holdings);
    }

    public Task KeepAsync(string resourceId, string reason, CancellationToken cancellationToken)
    {
        _kept[resourceId] = reason;

        return Task.CompletedTask;
    }

    public Task<bool> TryUnkeepAsync(string resourceId, CancellationToken cancellationToken) => Task.FromResult(_kept.Remove(resourceId));

    public Task<IReadOnlyDictionary<string, string>> KeptAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_kept, StringComparer.Ordinal));
}

/// <summary>Baselines kept in memory: taken once per owner, dropped once the owner is over.</summary>
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

    public Task KeepOnlyAsync(IReadOnlySet<Owner> owners, CancellationToken cancellationToken)
    {
        foreach (var owner in _taken.Keys.Where(owner => !owners.Contains(owner)).ToList())
            _taken.Remove(owner);

        return Task.CompletedTask;
    }
}

/// <summary>Discoveries kept in memory the way a host's record keeps them: counted when seen again, crossed off once gone long enough.</summary>
internal sealed class MemoryDiscoveries : IDiscoveries
{
    private readonly List<Discovery> _written = [];

    /// <summary>How many discoveries are written down.</summary>
    public int Count => _written.Count;

    public Task NoteAsync(IReadOnlyList<Discovery> discoveries, CancellationToken cancellationToken)
    {
        foreach (var discovery in discoveries)
        {
            var at = _written.FindIndex(written => string.Equals(written.Trace.Id, discovery.Trace.Id, StringComparison.Ordinal));

            if (at < 0)
            {
                _written.Add(discovery with { Sightings = 1 });

                continue;
            }

            var known = _written[at];
            var trace = discovery.Trace.Bytes is null ? known.Trace : known.Trace with { Bytes = discovery.Trace.Bytes, IsAtLeast = discovery.Trace.IsAtLeast };

            _written[at] = known with { Trace = trace, LastSeen = discovery.LastSeen, Sightings = known.Sightings + 1 };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Discovery>> ListAsync(IReadOnlySet<string> present, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var forgotten = now - Discovery.Remembered;
        var listed = new List<Discovery>();

        for (var index = 0; index < _written.Count; index++)
        {
            var isPresent = present.Contains(_written[index].Trace.Id);

            if (isPresent)
                _written[index] = _written[index] with { LastSeen = now };

            if (isPresent || _written[index].LastSeen >= forgotten)
                listed.Add(_written[index] with { IsPresent = isPresent });
        }

        _written.RemoveAll(discovery => discovery.LastSeen < forgotten);

        return Task.FromResult<IReadOnlyList<Discovery>>([.. listed.OrderByDescending(discovery => discovery.FirstSeen)]);
    }

    public Task<int> SettleAsync(Rule rule, CancellationToken cancellationToken) =>
        Task.FromResult(_written.RemoveAll(discovery => rule.Matches(discovery.Trace)));
}
