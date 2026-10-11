namespace Snail.MCP.CleanUp.Core.Application;

/// <summary>Takes stock of the machine: asks every kind what it sees, settles whose each thing is, and notices what nothing knows.</summary>
/// <remarks>
/// The ledger outranks what a kind can tell by looking, because it remembers what looking can no longer
/// show: a server started under an agent is a child of nothing once the agent has died, and only the line
/// written while it was alive still says whose it was. That is also why a census writes down every resource
/// it sees running under a live agent — the next census may be the first one after that agent is gone.
/// <para>
/// Kinds only know what they were written for, so every census also follows the trails and compares them
/// with what each running session began with. What is new, claimed by no kind and covered by no rule is a
/// discovery: the account never assumes it already knows every kind of leftover there is.
/// </para>
/// </remarks>
public sealed class Census(
    IEnumerable<IResourceKind> kinds,
    IEnumerable<ITrail> trails,
    ISessions sessions,
    ILedger ledger,
    IRules rules,
    IBaselines baselines,
    IDiscoveries discoveries,
    TimeProvider clock)
{
    /// <summary>
    /// How old a holding must be before a census may cross it off for not being seen. A program written down
    /// a moment ago may not be in a process table read a moment before that, and crossing it off would forget
    /// the one thing the ledger was certain of.
    /// </summary>
    public static TimeSpan Settling { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The inventory of this moment, for the owner who asked.</summary>
    public async Task<Inventory> TakeAsync(Owner asker, CancellationToken cancellationToken)
    {
        var began = clock.GetUtcNow();
        var known = await sessions.ListAsync(cancellationToken);
        var observations = await Task.WhenAll(kinds.Select(kind => ObservedAsync(kind, known, cancellationToken)));
        var holdings = (await ledger.HoldingsAsync(cancellationToken)).ToDictionary(holding => holding.ResourceId, StringComparer.Ordinal);
        var now = clock.GetUtcNow();

        List<Resource> settled = [.. observations.SelectMany(observation => observation.Resources).Select(resource => Settled(resource, holdings))];

        await RememberAncestryAsync(settled, holdings, now, cancellationToken);
        await ForgetTheGoneAsync(observations, settled, holdings.Values.Where(holding => holding.RecordedAt < began - Settling), cancellationToken);

        var (found, trailNotes) = await NoticeAsync(observations, settled, known, now, cancellationToken);
        var notes = observations
            .Where(observation => observation.Note is not null)
            .Select(observation => KeyValuePair.Create(observation.Kind, observation.Note!))
            .Concat(trailNotes);

        return new Inventory(now, asker, settled, known, await ledger.KeptAsync(cancellationToken), found, notes.ToDictionary());
    }

    /// <summary>What a kind sees, or why it could not look: a kind that fails says so instead of failing the census.</summary>
    private static async Task<Observation> ObservedAsync(IResourceKind kind, IReadOnlyList<Session> known, CancellationToken cancellationToken)
    {
        try
        {
            return await kind.ObserveAsync(known, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return Observation.Unseen(kind.Name, $"could not look: {failure.Message}");
        }
    }

    private static Resource Settled(Resource resource, Dictionary<string, Holding> holdings)
    {
        if (!holdings.TryGetValue(resource.Id, out var holding))
            return resource;

        var isStronger = holding.Evidence > resource.Evidence || !resource.Owner.IsKnown;

        return isStronger ? resource with { Owner = holding.Owner, Evidence = holding.Evidence } : resource;
    }

    private async Task RememberAncestryAsync(
        IReadOnlyList<Resource> settled,
        Dictionary<string, Holding> holdings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var unwritten = settled.Where(resource => resource.Evidence == Evidence.Ancestry && !holdings.ContainsKey(resource.Id));

        foreach (var resource in unwritten)
            await ledger.RecordAsync(new Holding(resource.Id, resource.Owner, resource.Evidence, now, resource.Title), cancellationToken);
    }

    /// <summary>Crosses off what the ledger holds and a kind that looked in full no longer sees.</summary>
    /// <remarks>A kind that could not look says nothing about what is gone, so its holdings stay.</remarks>
    private async Task ForgetTheGoneAsync(
        IReadOnlyList<Observation> observations,
        IReadOnlyList<Resource> settled,
        IEnumerable<Holding> holdings,
        CancellationToken cancellationToken)
    {
        var lookedInFull = observations.Where(observation => observation.Note is null).Select(observation => observation.Kind).ToHashSet(StringComparer.Ordinal);
        var present = settled.Select(resource => resource.Id).ToHashSet(StringComparer.Ordinal);
        var gone = holdings.Where(holding => lookedInFull.Contains(KindOf(holding.ResourceId)) && !present.Contains(holding.ResourceId));

        foreach (var holding in gone)
            await ledger.ForgetAsync(holding.ResourceId, cancellationToken);
    }

    private static string KindOf(string resourceId) => resourceId[..Math.Max(resourceId.IndexOf(':', StringComparison.Ordinal), 0)];

    /// <summary>Follows every trail, takes the baselines still missing, writes down what is new, and reads back the whole record.</summary>
    private async Task<(IReadOnlyList<Discovery> Found, IEnumerable<KeyValuePair<string, string>> Notes)> NoticeAsync(
        IReadOnlyList<Observation> observations,
        IReadOnlyList<Resource> settled,
        IReadOnlyList<Session> known,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var shown = await rules.ListAsync(cancellationToken);
        var followed = await Task.WhenAll(trails.Select(trail => FollowedAsync(trail, shown, cancellationToken)));
        var traces = followed.SelectMany(trail => trail.Traces).ToList();
        var present = traces.Select(trace => trace.Id).ToHashSet(StringComparer.Ordinal);
        var alive = known.Where(session => session.IsAlive).ToList();

        await BeginAsync(alive, traces, now, cancellationToken);

        var written = await discoveries.ListAsync(present, cancellationToken);
        var fresh = await NewAsync(traces, Claimed(observations, settled), shown, alive, written, now, cancellationToken);

        await discoveries.NoteAsync(fresh, cancellationToken);
        await baselines.KeepOnlyAsync(alive.Select(session => session.Owner).ToHashSet(), cancellationToken);

        var notes = followed.Where(trail => trail.Note is not null).Select(trail => KeyValuePair.Create($"trail:{trail.Name}", trail.Note!));

        return (await discoveries.ListAsync(present, cancellationToken), notes);
    }

    private static async Task<(string Name, IReadOnlyList<Trace> Traces, string? Note)> FollowedAsync(
        ITrail trail,
        IReadOnlyList<Rule> shown,
        CancellationToken cancellationToken)
    {
        try
        {
            return (trail.Name, await trail.FollowAsync(shown, cancellationToken), null);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return (trail.Name, [], $"could not follow: {failure.Message}");
        }
    }

    /// <summary>
    /// The traces something already accounts for: what a kind says it covers, and every resource that has an
    /// owner, because a resource of kind <c>file</c> and a trace of trail <c>file</c> with the same key are the
    /// same thing seen twice.
    /// </summary>
    private static HashSet<string> Claimed(IReadOnlyList<Observation> observations, IReadOnlyList<Resource> settled) =>
    [
        .. observations.SelectMany(observation => observation.Claimed),
        .. settled.Where(resource => resource.Owner.IsKnown).Select(resource => resource.Id)
    ];

    /// <summary>The first census a session takes part in is the line its «new» is measured from.</summary>
    private async Task BeginAsync(IReadOnlyList<Session> alive, IReadOnlyList<Trace> traces, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<string> seen = [.. traces.Select(trace => trace.Id)];

        foreach (var session in alive)
            await baselines.SaveAsync(session.Owner, seen, now, cancellationToken);
    }

    /// <summary>
    /// What appeared since some running session began and nothing accounts for. A trace inside a session's
    /// directory is that session's; otherwise it is the session's only when it was the one running, because
    /// with two sessions running «it appeared» does not say whose it is.
    /// </summary>
    private async Task<IReadOnlyList<Discovery>> NewAsync(
        IReadOnlyList<Trace> traces,
        HashSet<string> claimed,
        IReadOnlyList<Rule> shown,
        IReadOnlyList<Session> alive,
        IReadOnlyList<Discovery> written,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var noted = written.Select(discovery => discovery.Trace.Id).ToHashSet(StringComparer.Ordinal);
        var began = new List<(Session Session, IReadOnlySet<string> Seen)>();

        foreach (var session in alive)
        {
            if (await baselines.FindAsync(session.Owner, cancellationToken) is { } seen)
                began.Add((session, seen));
        }

        var fresh = new List<Discovery>();

        foreach (var trace in traces.Where(trace => !claimed.Contains(trace.Id) && !shown.Any(rule => rule.Matches(trace))))
        {
            var during = began.Where(entry => !entry.Seen.Contains(trace.Id)).Select(entry => entry.Session).ToList();

            if (during.Count == 0)
                continue;

            var measured = noted.Contains(trace.Id) ? trace : await MeasuredAsync(trace, cancellationToken);

            fresh.Add(new Discovery(measured, OwnerOf(trace, during), now, now, 1));
        }

        return fresh;
    }

    private async Task<Trace> MeasuredAsync(Trace trace, CancellationToken cancellationToken)
    {
        var trail = trails.FirstOrDefault(candidate => string.Equals(candidate.Name, trace.Trail, StringComparison.Ordinal));

        try
        {
            return trail is null ? trace : await trail.MeasureAsync(trace, cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return trace;
        }
    }

    private static Owner OwnerOf(Trace trace, IReadOnlyList<Session> during)
    {
        var inside = during.FirstOrDefault(session => session.Directory is { } directory && IsInside(trace.Key, directory));

        if (inside is not null)
            return inside.Owner;

        return during.Count == 1 ? during[0].Owner : Owner.Nobody;
    }

    private static bool IsInside(string path, string directory) =>
        path.StartsWith($"{Path.TrimEndingDirectorySeparator(directory)}/", StringComparison.Ordinal);
}
