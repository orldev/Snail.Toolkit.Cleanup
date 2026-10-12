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
        var started = clock.GetTimestamp();
        var timings = new System.Collections.Concurrent.ConcurrentDictionary<string, TimeSpan>(StringComparer.Ordinal);
        var known = await sessions.ListAsync(cancellationToken);
        var observations = await Task.WhenAll(kinds.Select(kind => TimedAsync($"kind:{kind.Name}", timings, () => ObservedAsync(kind, known, cancellationToken))));
        var holdings = await TimedAsync("ledger:holdings", timings, async () => (await ledger.HoldingsAsync(cancellationToken)).ToDictionary(holding => holding.ResourceId, StringComparer.Ordinal));
        var now = clock.GetUtcNow();

        List<Resource> settled = [.. observations.SelectMany(observation => observation.Resources).Select(resource => Settled(resource, holdings))];

        await TimedAsync("ledger:holdings", timings, async () =>
        {
            await RememberAncestryAsync(settled, holdings, now, cancellationToken);
            await ForgetTheGoneAsync(observations, settled, holdings.Values.Where(holding => holding.RecordedAt < began - Settling), cancellationToken);

            return true;
        });

        var (found, trailNotes) = await NoticeAsync(observations, settled, known, now, timings, cancellationToken);
        var notes = observations
            .Where(observation => observation.Note is not null)
            .Select(observation => KeyValuePair.Create(observation.Kind, observation.Note!))
            .Concat(trailNotes);

        var kept = await TimedAsync("ledger:holdings", timings, () => ledger.KeptAsync(cancellationToken));

        timings["census"] = clock.GetElapsedTime(started);

        return new Inventory(now, asker, settled, known, kept, found, notes.ToDictionary()) { Timings = timings.ToDictionary(StringComparer.Ordinal) };
    }

    /// <summary>Runs one look and adds how long it took to the time written down under its name.</summary>
    private async Task<T> TimedAsync<T>(string name, System.Collections.Concurrent.ConcurrentDictionary<string, TimeSpan> timings, Func<Task<T>> look)
    {
        var started = clock.GetTimestamp();

        try
        {
            return await look();
        }
        finally
        {
            var took = clock.GetElapsedTime(started);

            timings.AddOrUpdate(name, took, (_, before) => before + took);
        }
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
        System.Collections.Concurrent.ConcurrentDictionary<string, TimeSpan> timings,
        CancellationToken cancellationToken)
    {
        var shown = await rules.ListAsync(cancellationToken);
        var followed = await Task.WhenAll(trails.Select(trail => TimedAsync($"trail:{trail.Name}", timings, () => FollowedAsync(trail, shown, cancellationToken))));
        var traces = followed.SelectMany(trail => trail.Traces).ToList();
        var ids = new string[traces.Count];

        for (var index = 0; index < traces.Count; index++)
            ids[index] = traces[index].Id;

        var present = new HashSet<string>(ids, StringComparer.Ordinal);
        var alive = known.Where(session => session.IsAlive).ToList();

        await TimedAsync("ledger:baselines", timings, async () =>
        {
            await BeginAsync(alive, ids, now, cancellationToken);

            return true;
        });

        var written = await TimedAsync("ledger:discoveries", timings, () => discoveries.ListAsync(present, cancellationToken));
        var fresh = await TimedAsync("noticing", timings, () => NewAsync(traces, ids, Claimed(observations, settled), shown, alive, written, now, cancellationToken));

        await TimedAsync("ledger:discoveries", timings, async () =>
        {
            await discoveries.NoteAsync(fresh, cancellationToken);

            return true;
        });
        await TimedAsync("ledger:baselines", timings, async () =>
        {
            await baselines.KeepOnlyAsync(alive.Select(session => session.Owner).ToHashSet(), cancellationToken);

            return true;
        });

        var notes = followed.Where(trail => trail.Note is not null).Select(trail => KeyValuePair.Create($"trail:{trail.Name}", trail.Note!));

        return (await TimedAsync("ledger:discoveries", timings, () => discoveries.ListAsync(present, cancellationToken)), notes);
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
    private async Task BeginAsync(IReadOnlyList<Session> alive, IReadOnlyList<string> seen, DateTimeOffset now, CancellationToken cancellationToken)
    {
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
        IReadOnlyList<string> ids,
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

        for (var index = 0; index < traces.Count; index++)
        {
            var (trace, id) = (traces[index], ids[index]);

            if (claimed.Contains(id) || IsKnown(trace, shown))
                continue;

            List<Session>? during = null;

            foreach (var (session, seen) in began)
            {
                if (!seen.Contains(id))
                    (during ??= []).Add(session);
            }

            if (during is null)
                continue;

            var measured = noted.Contains(id) ? trace : await MeasuredAsync(trace, cancellationToken);

            fresh.Add(new Discovery(measured, OwnerOf(trace, during), now, now, 1));
        }

        return fresh;
    }

    private static bool IsKnown(Trace trace, IReadOnlyList<Rule> shown)
    {
        foreach (var rule in shown)
        {
            if (rule.Matches(trace))
                return true;
        }

        return false;
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
