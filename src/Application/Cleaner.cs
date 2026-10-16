namespace Snail.Toolkit.Cleanup.Application;

/// <summary>Carries out the removals of a plan that was shown, one resource at a time, in the plan's order.</summary>
/// <remarks>
/// Each kind checks again, right before it acts, that the resource is still the one the plan described;
/// what changed since is left alone and said so. A resource whose predecessor failed to go is not
/// attempted either: the order exists because the later one depends on the earlier one being gone.
/// <para>
/// A kind can tell that a resource is still the same; it cannot tell that its owner is still over. A plan may be carried
/// out minutes after it was shown, and an owner taken for ended then may be going again by now — a session resumed under
/// the same id took its scratchpad back while a plan of another session still listed it as an orphan's (2026-10-16). So a
/// cleaner given the holders asks them once more before it starts, and leaves alone what belongs to an owner going again.
/// </para>
/// </remarks>
/// <param name="kinds">The kinds that release what they see.</param>
/// <param name="ledger">The ledger a released resource is crossed off.</param>
/// <param name="holders">The owners, asked again right before the cleaner starts which of them are going; null to take the plan's word.</param>
public sealed class Cleaner(IEnumerable<IResourceKind> kinds, ILedger ledger, IHolders? holders)
{
    private readonly Dictionary<string, IResourceKind> _kinds = kinds.ToDictionary(kind => kind.Name, StringComparer.Ordinal);

    /// <summary>A cleaner that takes the plan's word for which owners are over.</summary>
    /// <remarks>For a host whose owners cannot come back once over, such as tasks that ended; any other host passes its holders.</remarks>
    public Cleaner(IEnumerable<IResourceKind> kinds, ILedger ledger)
        : this(kinds, ledger, null)
    {
    }

    /// <summary>The removals of the plan, carried out, each with how it ended.</summary>
    public async Task<IReadOnlyList<Release>> ApplyAsync(Plan plan, CancellationToken cancellationToken)
    {
        var unreleased = new HashSet<string>(StringComparer.Ordinal);
        var releases = new List<Release>();
        var goingNow = await GoingAsync(cancellationToken);

        foreach (var removal in plan.Removals)
        {
            var resource = removal.Resource;
            var release = IsGoingAgain(resource, plan.Asker, goingNow)
                ? Release.NotTheSame(resource, $"{resource.Owner} is going again since the plan was shown")
                : await ReleasedAsync(resource, unreleased, cancellationToken);

            if (release.Outcome is Outcome.Released or Outcome.Gone)
                await ledger.ForgetAsync(resource.Id, cancellationToken);
            else
                unreleased.Add(resource.Id);

            releases.Add(release);
        }

        return releases;
    }

    /// <summary>The owners going at this moment; none asked for when the cleaner has no holders.</summary>
    private async Task<IReadOnlySet<Owner>> GoingAsync(CancellationToken cancellationToken)
    {
        if (holders is null)
            return new HashSet<Owner>();

        var listed = await holders.ListAsync(cancellationToken);
        var going = listed.Where(holder => holder.IsAlive).Select(holder => holder.Owner);

        return going.ToHashSet();
    }

    /// <summary>Whether a resource the plan removes as somebody else's belongs to an owner that is going now.</summary>
    /// <remarks>What the asker holds is the asker's to remove, and a shared cache is nobody's, so neither is asked about.</remarks>
    private static bool IsGoingAgain(Resource resource, Owner asker, IReadOnlySet<Owner> goingNow) =>
        !resource.IsShared && resource.Owner.IsKnown && resource.Owner != asker && goingNow.Contains(resource.Owner);

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
