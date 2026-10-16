namespace Snail.Toolkit.Cleanup.Application;

/// <summary>Carries out the removals of a plan that was shown, one resource at a time, in the plan's order.</summary>
/// <remarks>
/// Each kind checks again, right before it acts, that the resource is still the one the plan described;
/// what changed since is left alone and said so. A resource whose predecessor failed to go is not
/// attempted either: the order exists because the later one depends on the earlier one being gone.
/// </remarks>
public sealed class Cleaner(IEnumerable<IResourceKind> kinds, ILedger ledger)
{
    private readonly Dictionary<string, IResourceKind> _kinds = kinds.ToDictionary(kind => kind.Name, StringComparer.Ordinal);

    /// <summary>The removals of the plan, carried out, each with how it ended.</summary>
    public async Task<IReadOnlyList<Release>> ApplyAsync(Plan plan, CancellationToken cancellationToken)
    {
        var unreleased = new HashSet<string>(StringComparer.Ordinal);
        var releases = new List<Release>();

        foreach (var removal in plan.Removals)
        {
            var resource = removal.Resource;
            var release = await ReleasedAsync(resource, unreleased, cancellationToken);

            if (release.Outcome is Outcome.Released or Outcome.Gone)
                await ledger.ForgetAsync(resource.Id, cancellationToken);
            else
                unreleased.Add(resource.Id);

            releases.Add(release);
        }

        return releases;
    }

    private async Task<Release> ReleasedAsync(Resource resource, HashSet<string> unreleased, CancellationToken cancellationToken)
    {
        if (resource.After.FirstOrDefault(unreleased.Contains) is { } waitedFor)
            return Release.Refused(resource, $"not attempted: {waitedFor} had to go first and did not");

        if (!_kinds.TryGetValue(resource.Kind, out var kind))
            return Release.Refused(resource, $"no kind of the host releases {resource.Kind}");

        try
        {
            return await kind.ReleaseAsync(resource, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return Release.Refused(resource, failure.Message);
        }
    }
}
