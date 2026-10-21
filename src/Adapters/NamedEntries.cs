using System.Globalization;

namespace Snail.Toolkit.Cleanup.Adapters;

/// <summary>Entries under a root whose names carry their owner: the folders of tasks, the keys of projects.</summary>
/// <remarks>
/// The name is the proof (<see cref="Evidence.Label"/>): it was written when the entry was made, so an entry goes once
/// its owner is over, whoever is asking, and an entry whose name carries no owner is never touched. Two graces keep an
/// entry that may still be in use: one counted from when it was last written, one from when its owner ended, as the
/// holder says — a task completed a minute ago may be continued by a person. Time comes from the host's clock.
/// <para>
/// «Written» means anywhere inside: a file written one level down changes nothing on the directory itself, so the newest
/// write of everything walked is what the grace is counted from and what a release compares. A directory in the future,
/// as a restored backup or a skewed clock leaves it, is blocked by that rather than by a grace it is not within.
/// </para>
/// <para>
/// Release looks again before it deletes: an entry that is gone was already released, and one that became a link,
/// changed between directory and file, or was written since it was observed is no longer the entry the plan described,
/// and is left. The inside of a directory is compared only when both walks saw the whole of it; a walk cut short says
/// nothing about what it did not see. A folder that another cleaner took while this one was looking is gone, not failed. Links are never followed out of the root: a link among the entries is reported and never removed, a
/// link inside a directory is neither counted nor followed, and a directory is deleted without following the links in
/// it. Sizes are the lengths of the files counted, as <see cref="Resource.Bytes"/> says, and «at least» when a count
/// stopped short.
/// </para>
/// <para>
/// Only what can go is walked: a directory whose owner is still going is kept whatever its size, so its size is left
/// unknown rather than counted file by file at every observation — a running task's build output is the largest thing
/// under a worker's root and the one thing a pass never removes. A file's length comes with the listing and is always
/// given. A directory is walked once more as it is released, so the release compares the newest write and still says
/// what it gave back.
/// </para>
/// </remarks>
public sealed class NamedEntries(NamedEntriesOptions options, TimeProvider clock) : IResourceKind
{
    /// <summary>
    /// How many files of one directory are counted before the count stops and its size is given as at least that much:
    /// one folder holding a whole repository's build output must not hold a census up.
    /// </summary>
    public const int CountedAtMost = 100_000;

    private const string EntryFact = "entry";

    private const string WrittenFact = "written";

    private const string InsideFact = "written-inside";

    private const string LengthFact = "length";

    private static readonly EnumerationOptions Listing = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseSensitive,
        MatchType = MatchType.Simple
    };

    private static readonly EnumerationOptions Walking = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = true
    };

    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Root));

    /// <inheritdoc/>
    public string Name => options.Kind;

    /// <inheritdoc/>
    /// <remarks>A root that does not exist holds nothing: a host whose first entry is yet to be made has nothing to clean.</remarks>
    public Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root))
            return Task.FromResult(new Observation(Name, [], []));

        var ended = Ended(holders);
        var alive = Alive(holders);
        var now = clock.GetUtcNow();
        var found = new List<Resource>();

        foreach (var entry in new DirectoryInfo(_root).EnumerateFileSystemInfos(options.Pattern, Listing))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Described(entry, alive, ended, now, cancellationToken) is { } resource)
                found.Add(resource);
        }

        List<string> claimed = [.. found.Select(resource => $"{Kinds.File}:{resource.Key}")];

        return Task.FromResult(new Observation(Name, found, claimed));
    }

    /// <inheritdoc/>
    public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
    {
        var path = resource.Key;

        if (!IsDirectlyUnderRoot(path))
            return Task.FromResult(Release.Refused(resource, $"not directly under {_root}, the only place this kind removes from"));

        var entry = EntryAt(path);

        if (entry is null)
            return Task.FromResult(Release.AlreadyGone(resource));

        if (entry.LinkTarget is not null)
            return Task.FromResult(Release.NotTheSame(resource, "it is a link now, and links are never followed out of the root"));

        var measure = Measured(entry, cancellationToken);

        if (Changed(resource, entry, measure) is { } change)
            return Task.FromResult(Release.NotTheSame(resource, change));

        var released = resource.Bytes is null ? resource with { Bytes = measure.Bytes, IsAtLeast = measure.IsAtLeast } : resource;

        try
        {
            if (entry is DirectoryInfo directory)
                directory.Delete(recursive: true);
            else
                entry.Delete();
        }
        catch (Exception failure) when (failure is DirectoryNotFoundException or FileNotFoundException)
        {
            return Task.FromResult(EntryAt(path) is null ? Release.AlreadyGone(resource) : Release.Refused(resource, failure.Message));
        }

        var detail = options.ReleaseDetail?.Invoke(released) ?? $"deleted from {_root}";

        return Task.FromResult(Release.Done(released, detail));
    }

    private Resource? Described(FileSystemInfo entry, HashSet<Owner> alive, Dictionary<Owner, DateTimeOffset> ended, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var isLink = entry.LinkTarget is not null;

        if (!isLink && !IsOfType(entry))
            return null;

        if (options.OwnerIn(entry.Name) is not { } id)
            return null;

        var owner = new Owner(options.OwnerKind, id);
        var isMeasured = !isLink && (entry is FileInfo || !alive.Contains(owner));
        var measure = isMeasured ? Measured(entry, cancellationToken) : Measure.Unwalked(entry);

        var resource = new Resource
        {
            Kind = Name,
            Key = entry.FullName,
            Title = $"{Name} {entry.Name} of {owner}",
            Owner = owner,
            Evidence = Evidence.Label,
            Bytes = measure.Bytes,
            IsAtLeast = measure.IsAtLeast,
            IsRegenerable = options.IsRegenerable,
            Blocker = Blocker(isLink, owner, measure.WrittenAt, ended, now),
            Facts = Facts(entry, measure)
        };

        return resource;
    }

    private string? Blocker(bool isLink, Owner owner, DateTime writtenAt, Dictionary<Owner, DateTimeOffset> ended, DateTimeOffset now)
    {
        if (isLink)
            return "a link: what it points at is never followed out of the root";

        var sinceWritten = now.UtcDateTime - writtenAt;

        if (sinceWritten < TimeSpan.Zero)
            return "written in the future: the clock of whatever wrote it is wrong, so when it was last used cannot be told";

        if (sinceWritten < options.Grace)
            return "written within the grace: something may still be using it";

        if (ended.TryGetValue(owner, out var endedAt) && now - endedAt < options.Grace)
            return $"{owner} ended within the grace: it may still be taken up again";

        return null;
    }

    private bool IsOfType(FileSystemInfo entry) =>
        options.Entries == EntryType.Directories ? entry is DirectoryInfo : entry is FileInfo;

    /// <summary>The owners of the kind this looks for that are still going, whose entries are kept and so never walked.</summary>
    private HashSet<Owner> Alive(IReadOnlyList<Holder> holders)
    {
        var alive = new HashSet<Owner>();

        foreach (var holder in holders)
        {
            if (holder.IsAlive && string.Equals(holder.Owner.Kind, options.OwnerKind, StringComparison.Ordinal))
                alive.Add(holder.Owner);
        }

        return alive;
    }

    /// <summary>When each owner of the kind this looks for ended, among the holders that are over and say when.</summary>
    private Dictionary<Owner, DateTimeOffset> Ended(IReadOnlyList<Holder> holders)
    {
        var ended = new Dictionary<Owner, DateTimeOffset>();

        foreach (var holder in holders)
        {
            if (!holder.IsAlive && holder.EndedAt is { } endedAt && string.Equals(holder.Owner.Kind, options.OwnerKind, StringComparison.Ordinal))
                ended.TryAdd(holder.Owner, endedAt);
        }

        return ended;
    }

    /// <summary>The lengths of the files of an entry and the newest write anywhere inside it, a directory's own included.</summary>
    private static Measure Measured(FileSystemInfo entry, CancellationToken cancellationToken)
    {
        if (entry is FileInfo file)
            return new Measure(file.Length, false, file.LastWriteTimeUtc);

        long bytes = 0;
        var counted = 0;
        var writtenAt = entry.LastWriteTimeUtc;

        try
        {
            foreach (var inside in ((DirectoryInfo)entry).EnumerateFileSystemInfos("*", Walking))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (inside.LastWriteTimeUtc > writtenAt)
                    writtenAt = inside.LastWriteTimeUtc;

                if (inside is not FileInfo counting)
                    continue;

                if (++counted > CountedAtMost)
                    return new Measure(bytes, true, writtenAt);

                bytes += counting.Length;
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new Measure(bytes, true, writtenAt);
        }

        return new Measure(bytes, false, writtenAt);
    }

    /// <summary>
    /// What the entry was when it was observed, so that a release can tell it is still the same one: its own write time
    /// always, and the newest write inside a directory when the whole of it was walked.
    /// </summary>
    private static Dictionary<string, string> Facts(FileSystemInfo entry, Measure measure)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EntryFact] = TypeOf(entry),
            [WrittenFact] = Ticks(entry.LastWriteTimeUtc)
        };

        if (entry is DirectoryInfo && measure.IsWhole)
            facts[InsideFact] = Ticks(measure.WrittenAt);

        if (entry is FileInfo { LinkTarget: null } file)
            facts[LengthFact] = file.Length.ToString(CultureInfo.InvariantCulture);

        return facts;
    }

    /// <summary>Why the entry is no longer the one observed; the inside of a directory is compared only when both walks saw the whole of it.</summary>
    private static string? Changed(Resource resource, FileSystemInfo entry, Measure measure)
    {
        if (!string.Equals(resource.Facts.GetValueOrDefault(EntryFact), TypeOf(entry), StringComparison.Ordinal))
            return $"it is a {TypeOf(entry)} now";

        if (!string.Equals(resource.Facts.GetValueOrDefault(WrittenFact), Ticks(entry.LastWriteTimeUtc), StringComparison.Ordinal))
            return "written since it was observed";

        if (resource.Facts.TryGetValue(InsideFact, out var inside) && measure.IsWhole && !string.Equals(inside, Ticks(measure.WrittenAt), StringComparison.Ordinal))
            return "written inside since it was observed";

        if (entry is FileInfo file && !string.Equals(resource.Facts.GetValueOrDefault(LengthFact), file.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return "its length changed since it was observed";

        return null;
    }

    private static string Ticks(DateTime at) => at.Ticks.ToString(CultureInfo.InvariantCulture);

    private static string TypeOf(FileSystemInfo entry) => entry is DirectoryInfo ? "directory" : "file";

    /// <summary>The entry at a path as it is now, a link taken as the link itself; null when nothing is there.</summary>
    private static FileSystemInfo? EntryAt(string path)
    {
        var file = new FileInfo(path);

        if (file.LinkTarget is not null)
            return file;

        if (Directory.Exists(path))
            return new DirectoryInfo(path);

        return file.Exists ? file : null;
    }

    private bool IsDirectlyUnderRoot(string path) =>
        string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)), _root, StringComparison.Ordinal);

    /// <summary>What a walk of an entry found: its size, whether the count stopped short, and the newest write inside.</summary>
    private readonly record struct Measure(long? Bytes, bool IsAtLeast, DateTime WrittenAt)
    {
        /// <summary>Whether the walk saw everything, so that the newest write is the newest of the whole entry.</summary>
        public bool IsWhole => Bytes is not null && !IsAtLeast;

        /// <summary>An entry that was not walked: no size, and its own write time for the newest.</summary>
        public static Measure Unwalked(FileSystemInfo entry) => new(null, false, entry.LastWriteTimeUtc);
    }
}
