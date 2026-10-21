using System.Collections.Concurrent;

namespace Snail.Toolkit.Cleanup.Application;

/// <summary>Takes stock of the machine: asks every kind what it sees, settles whose each thing is, and notices what nothing knows.</summary>
/// <remarks>
/// The ledger outranks what a kind can tell by looking, because it remembers what looking can no longer
/// show: a program started under an owner's process is a child of nothing once that process has ended, and only the
/// line written while it was alive still says whose it was. That is also why a census writes down every resource it
/// sees running under an owner still going — the next census may be the first one after that owner is gone.
/// <para>
/// Kinds only know what they were written for, so every census also follows the trails and compares them
/// with what each owner still going began with. What is new, claimed by no kind and covered by no rule is a
/// discovery: the account never assumes it already knows every kind of leftover there is.
/// </para>
/// <para>
/// The kinds are asked at once, each on a thread of its own: a kind that looks synchronously, as one walking a disk does,
/// would otherwise answer a finished task and the kinds would look one after another — four looks of 100 ms took 404 ms
/// (2026-10-21).
/// </para>
/// </remarks>
public sealed class Census(
    IEnumerable<IResourceKind> kinds,
    IEnumerable<ITrail> trails,
    IHolders holders,
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
        var timings = new ConcurrentDictionary<string, TimeSpan>(StringComparer.Ordinal);
        var known = await holders.ListAsync(cancellationToken);
        var observations = await Task.WhenAll(kinds.Select(kind => TimedAsync($"kind:{kind.Name}", timings, () => ObservedAsync(kind, known, cancellationToken))));
        var holdings = await TimedAsync("ledger:holdings", timings, async () => (await ledger.HoldingsAsync(cancellationToken)).ToDictionary(holding => holding.ResourceId, StringComparer.Ordinal));
        var now = clock.GetUtcNow();

        List<Resource> settled = [.. observations.SelectMany(observation => observation.Resources).Select(resource => Settled(resource, holdings))];

        await TimedAsync("ledger:holdings", timings, async () =>
        {
            await RememberAncestryAsync(settled, holdings, now, cancellationToken);
            await ForgetTheGoneAsync(observations, settled, holdings.Values.Where(holding => holding.RecordedAt < began - Settling), cancellationToken);
        });

        var noticed = await NoticeAsync(observations, settled, known, now, timings, cancellationToken);
        var kept = await TimedAsync("ledger:holdings", timings, () => ledger.KeptAsync(cancellationToken));

        timings["census"] = clock.GetElapsedTime(started);

        var inventory = new Inventory(now, settled, known)
        {
            Asker = asker,
            Kept = kept,
            Discoveries = noticed.Found,
            Notes = Noted(observations, noticed.Notes),
            Timings = timings.ToDictionary(StringComparer.Ordinal)
        };

        return inventory;
    }

    /// <summary>Runs one look and adds how long it took to the time written down under its name.</summary>
    private async Task<T> TimedAsync<T>(string name, ConcurrentDictionary<string, TimeSpan> timings, Func<Task<T>> look)
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

    /// <summary>Runs one piece of bookkeeping and adds how long it took to the time written down under its name.</summary>
    private async Task TimedAsync(string name, ConcurrentDictionary<string, TimeSpan> timings, Func<Task> work)
    {
        var started = clock.GetTimestamp();

        try
        {
            await work();
        }
        finally
        {
            var took = clock.GetElapsedTime(started);

            timings.AddOrUpdate(name, took, (_, before) => before + took);
        }
    }

    /// <summary>What a kind sees, on a thread of its own, or why it could not look: a kind that fails says so instead of failing the census.</summary>
    private static async Task<Observation> ObservedAsync(IResourceKind kind, IReadOnlyList<Holder> known, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => kind.ObserveAsync(known, cancellationToken), cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return Observation.Unseen(kind.Name, $"could not look: {failure.Message}");
        }
    }

    /// <summary>The notes of what could not be seen, one line per name: two kinds of one name that both could not look share a line.</summary>
    private static Dictionary<string, string> Noted(IReadOnlyList<Observation> observations, IReadOnlyList<KeyValuePair<string, string>> trailNotes)
    {
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var observation in observations)
        {
            if (observation.Note is not { } note)
                continue;

            notes[observation.Kind] = notes.TryGetValue(observation.Kind, out var before) ? $"{before}; {note}" : note;
        }

        foreach (var (name, note) in trailNotes)
            notes[name] = note;

        return notes;
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

    /// <summary>
    /// Follows every trail, takes the baselines still missing, writes down what is new, and returns the whole record;
    /// when the rules cannot be read, nothing is noticed and the note says so, since without them every trace would be new.
    /// </summary>
    private async Task<Noticed> NoticeAsync(
        IReadOnlyList<Observation> observations,
        IReadOnlyList<Resource> settled,
        IReadOnlyList<Holder> known,
        DateTimeOffset now,
        ConcurrentDictionary<string, TimeSpan> timings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Rule> shown;

        try
        {
            shown = await rules.ListAsync(cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return new Noticed([], [KeyValuePair.Create("rules", $"could not read the rules, so nothing was noticed: {failure.Message}")]);
        }

        var walked = Walked(observations);
        var followed = await Task.WhenAll(trails.Select(trail => walked.TryGetValue(trail.Name, out var traces)
            ? Task.FromResult<(string Name, IReadOnlyList<Trace> Traces, string? Note)>((trail.Name, traces, null))
            : TimedAsync($"trail:{trail.Name}", timings, () => FollowedAsync(trail, shown, cancellationToken))));
        var traces = followed.SelectMany(trail => trail.Traces).ToList();
        var ids = new string[traces.Count];

        for (var index = 0; index < traces.Count; index++)
            ids[index] = traces[index].Id;

        var present = new HashSet<string>(ids, StringComparer.Ordinal);
        var alive = known.Where(holder => holder.IsAlive).ToList();

        await TimedAsync("ledger:baselines", timings, () => BeginAsync(alive, ids, now, cancellationToken));

        var written = await TimedAsync("ledger:discoveries", timings, () => discoveries.ListAsync(present, now, cancellationToken));
        var fresh = await TimedAsync("noticing", timings, () => NewAsync(traces, ids, Claimed(observations, settled), shown, alive, written, now, cancellationToken));

        await TimedAsync("ledger:discoveries", timings, () => discoveries.NoteAsync(fresh, cancellationToken));
        await TimedAsync("ledger:baselines", timings, () => baselines.KeepOnlyAsync(alive.Select(holder => holder.Owner).ToHashSet(), cancellationToken));

        List<KeyValuePair<string, string>> notes = [.. followed.Where(trail => trail.Note is not null).Select(trail => KeyValuePair.Create($"trail:{trail.Name}", trail.Note!))];

        return new Noticed(Merged(written, fresh), notes);
    }

    /// <summary>
    /// The record as this census leaves it, without reading it back: what it noted laid over what it read, the way
    /// <see cref="IDiscoveries.NoteAsync"/> writes it — one more sighting, seen now, the size when it was measured.
    /// </summary>
    /// <remarks>Newest first, as the record is read: what was noted for the first time was first seen now.</remarks>
    private static IReadOnlyList<Discovery> Merged(IReadOnlyList<Discovery> written, IReadOnlyList<Discovery> fresh)
    {
        if (fresh.Count == 0)
            return written;

        var noted = new Dictionary<string, Discovery>(fresh.Count, StringComparer.Ordinal);

        foreach (var discovery in fresh)
            noted[discovery.Trace.Id] = discovery;

        var seenAgain = new List<Discovery>(written.Count);

        foreach (var discovery in written)
        {
            if (!noted.Remove(discovery.Trace.Id, out var again))
            {
                seenAgain.Add(discovery);

                continue;
            }

            seenAgain.Add(discovery with
            {
                Trace = again.Trace.Bytes is null ? discovery.Trace : discovery.Trace with { Bytes = again.Trace.Bytes, IsAtLeast = again.Trace.IsAtLeast },
                LastSeen = again.LastSeen,
                Sightings = discovery.Sightings + 1,
                IsPresent = true
            });
        }

        return [.. fresh.Where(discovery => noted.ContainsKey(discovery.Trace.Id)), .. seenAgain];
    }

    /// <summary>The trails the kinds already followed to look, so that no trail is walked twice in one census.</summary>
    private static Dictionary<string, IReadOnlyList<Trace>> Walked(IReadOnlyList<Observation> observations)
    {
        var walked = new Dictionary<string, IReadOnlyList<Trace>>(StringComparer.Ordinal);

        foreach (var observation in observations)
        {
            if (observation.Followed is { } followed)
                walked.TryAdd(followed.Trail, followed.Traces);
        }

        return walked;
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

    /// <summary>The first census an owner takes part in is the line its «new» is measured from.</summary>
    private async Task BeginAsync(IReadOnlyList<Holder> alive, IReadOnlyList<string> seen, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var holder in alive)
            await baselines.SaveAsync(holder.Owner, seen, now, cancellationToken);
    }

    /// <summary>
    /// What appeared since some owner still going began and nothing accounts for. A trace inside an owner's
    /// directory is that owner's; otherwise it is the owner's only when it was the one going, because with two
    /// owners going «it appeared» does not say whose it is.
    /// </summary>
    private async Task<IReadOnlyList<Discovery>> NewAsync(
        IReadOnlyList<Trace> traces,
        IReadOnlyList<string> ids,
        HashSet<string> claimed,
        IReadOnlyList<Rule> shown,
        IReadOnlyList<Holder> alive,
        IReadOnlyList<Discovery> written,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var noted = written.Select(discovery => discovery.Trace.Id).ToHashSet(StringComparer.Ordinal);
        var began = new List<(Holder Holder, Baseline Seen)>();

        foreach (var holder in alive)
        {
            if (await baselines.FindAsync(holder.Owner, cancellationToken) is { } seen)
                began.Add((holder, seen));
        }

        var fresh = new List<Discovery>();

        for (var index = 0; index < traces.Count; index++)
        {
            var (trace, id) = (traces[index], ids[index]);

            if (claimed.Contains(id) || IsKnown(trace, shown))
                continue;

            List<Holder>? during = null;

            foreach (var (holder, seen) in began)
            {
                if (!seen.Contains(id))
                    (during ??= []).Add(holder);
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

    /// <summary>The owner whose directory the trace is inside, or the only owner going; a holder with no directory, or an empty one, works nowhere.</summary>
    private static Owner OwnerOf(Trace trace, IReadOnlyList<Holder> during)
    {
        var inside = during.FirstOrDefault(holder => holder.Directory is { Length: > 0 } directory && IsInside(trace.Key, directory));

        if (inside is not null)
            return inside.Owner;

        return during.Count == 1 ? during[0].Owner : Owner.Nobody;
    }

    private static bool IsInside(string path, string directory) =>
        path.StartsWith($"{Path.TrimEndingDirectorySeparator(directory)}/", StringComparison.Ordinal);

    /// <summary>What a census noticed: the discoveries as the record now holds them, and the trails or rules it could not read.</summary>
    private sealed record Noticed(IReadOnlyList<Discovery> Found, IReadOnlyList<KeyValuePair<string, string>> Notes);
}
