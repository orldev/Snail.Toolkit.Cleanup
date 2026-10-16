using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Tests.Support;

/// <summary>A kind of resource that exists only in the test: it sees what it is told to and remembers what it let go.</summary>
internal sealed class FakeKind(string name) : IResourceKind
{
    public string Name { get; } = name;

    public List<Resource> Present { get; } = [];

    public List<string> Released { get; } = [];

    public HashSet<string> Refusing { get; } = new(StringComparer.Ordinal);

    public string? Blindness { get; set; }

    public Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        if (Blindness is not null)
            throw new InvalidOperationException(Blindness);

        return Task.FromResult(new Observation(Name, [.. Present], []));
    }

    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        if (Refusing.Contains(resource.Id))
            return Task.FromResult(Release.Refused(resource, "refused on purpose"));

        Released.Add(resource.Id);
        Present.RemoveAll(present => present.Id == resource.Id);

        return Task.FromResult(Release.Done(resource, "released by the fake"));
    }
}

/// <summary>Owners the test says exist.</summary>
internal sealed class FakeHolders(Owner current, params Holder[] holders) : IHolders
{
    public Task<IReadOnlyList<Holder>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Holder>>(holders);

    public Task<Owner> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(current);
}

/// <summary>A trail the test lays: it shows what it is told to.</summary>
internal sealed class FakeTrail(string name) : ITrail
{
    public string Name { get; } = name;

    public List<Trace> Present { get; } = [];

    /// <summary>How many times it was followed.</summary>
    public int Follows { get; private set; }

    public Trace Add(string key)
    {
        var trace = new Trace(Name, key, key);

        Present.Add(trace);

        return trace;
    }

    public Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        Follows++;

        return Task.FromResult<IReadOnlyList<Trace>>([.. Present]);
    }

    public Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken) => Task.FromResult(trace with { Bytes = 42 });
}
