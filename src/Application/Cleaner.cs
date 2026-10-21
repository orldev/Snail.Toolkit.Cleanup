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
/// <para>
/// What went is always told. A ledger that cannot be told, a host that stops the cleanup, a kind that fails halfway: each
/// is written into the release it happened to, and the releases before it are answered as they were — a release lost
/// with an exception is a resource gone that nobody can account for. A cleaner given a clock also refuses a plan past
/// <see cref="Plan.Lifetime"/>: it describes a machine that no longer exists.
/// </para>
/// </remarks>
/// <param name="kinds">The kinds that release what they see.</param>
/// <param name="ledger">The ledger a released resource is crossed off.</param>
/// <param name="holders">The owners, asked again right before the cleaner starts which of them are going; null to take the plan's word.</param>
/// <param name="clock">The clock a plan's age is read by; null to take the plan's word that it is fresh.</param>
public sealed class Cleaner(IEnumerable<IResourceKind> kinds, ILedger ledger, IHolders? holders, TimeProvider? clock)
{
    private const string Stopped = "not attempted: the cleanup was stopped";

    private readonly Dictionary<string, IResourceKind> _kinds = kinds.ToDictionary(kind => kind.Name, StringComparer.Ordinal);

    /// <summary>A cleaner that takes the plan's word for which owners are over and for how fresh it is.</summary>
    /// <remarks>For a host whose owners cannot come back once over, such as tasks that ended; any other host passes its holders.</remarks>
    public Cleaner(IEnumerable<IResourceKind> kinds, ILedger ledger)
        : this(kinds, ledger, null, null)
    {
    }

    /// <summary>A cleaner that asks the holders again and takes the plan's word for how fresh it is.</summary>
    public Cleaner(IEnumerable<IResourceKind> kinds, ILedger ledger, IHolders? holders)
        : this(kinds, ledger, holders, null)
    {
    }

    /// <summary>The removals of the plan, carried out, each with how it ended.</summary>
    public async Task<IReadOnlyList<Release>> ApplyAsync(Plan plan, CancellationToken cancellationToken)
    {
        if (StaleBecause(plan) is { } stale)
            return [.. plan.Removals.Select(removal => Release.Refused(removal.Resource, stale))];

        var unreleased = new HashSet<string>(StringComparer.Ordinal);
        var releases = new List<Release>();
        var goingNow = await GoingAsync(cancellationToken);

        foreach (var removal in plan.Removals)
        {
            var resource = removal.Resource;
            var release = await ReleasedAsync(resource, plan.Asker, goingNow, unreleased, cancellationToken);

            if (release.Outcome is Outcome.Released or Outcome.Gone)
                release = await CrossedOffAsync(release, cancellationToken);
            else
                unreleased.Add(resource.Id);

            releases.Add(release);
        }

        return releases;
    }

    /// <summary>Why the plan is too old to be carried out; null when it is fresh or when there is no clock to tell by.</summary>
    private string? StaleBecause(Plan plan)
    {
        if (clock is null)
            return null;

        var now = clock.GetUtcNow();

        if (plan.IsFresh(now))
            return null;

        var age = now - plan.MadeAt;

        return $"not attempted: the plan was shown {age.TotalMinutes:f0} minutes ago, past its lifetime of {Plan.Lifetime.TotalMinutes:f0}";
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

    private async Task<Release> ReleasedAsync(Resource resource, Owner asker, IReadOnlySet<Owner> goingNow, HashSet<string> unreleased, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Release.Refused(resource, Stopped);

        if (IsGoingAgain(resource, asker, goingNow))
            return Release.NotTheSame(resource, $"{resource.Owner} is going again since the plan was shown");

        if (resource.After.FirstOrDefault(unreleased.Contains) is { } waitedFor)
            return Release.Refused(resource, $"not attempted: {waitedFor} had to go first and did not");

        if (!_kinds.TryGetValue(resource.Kind, out var kind))
            return Release.Refused(resource, $"no kind of the host releases {resource.Kind}");

        try
        {
            return await kind.ReleaseAsync(resource, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Release.Refused(resource, "stopped while it was being released: whether it went is not known");
        }
        catch (Exception failure)
        {
            return Release.Refused(resource, failure.Message);
        }
    }

    /// <summary>The release crossed off the ledger; a ledger that cannot be told leaves the release standing, saying so, since the resource is gone either way.</summary>
    private async Task<Release> CrossedOffAsync(Release release, CancellationToken cancellationToken)
    {
        try
        {
            await ledger.ForgetAsync(release.ResourceId, cancellationToken);

            return release;
        }
        catch (OperationCanceledException)
        {
            return release with { Detail = $"{release.Detail}; the ledger was not told: the cleanup was stopped" };
        }
        catch (Exception failure)
        {
            return release with { Detail = $"{release.Detail}; the ledger could not be told: {failure.Message}" };
        }
    }
}
