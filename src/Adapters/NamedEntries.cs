using System.Globalization;

namespace Snail.Toolkit.Cleanup.Adapters;

/// <summary>Entries under a root whose names carry their owner: the folders of tasks, the keys of projects.</summary>
/// <remarks>
/// The name is the proof (<see cref="Evidence.Label"/>): it was written when the entry was made, so an entry goes once
/// its owner is over, whoever is asking, and an entry whose name carries no owner is never touched. Two graces keep an
/// entry that may still be in use: one counted from when it was last written, one from when its owner ended, as the
/// holder says — a task completed a minute ago may be continued by a person. Time comes from the host's clock.
/// <para>
/// Release looks again before it deletes: an entry that is gone was already released, and one that became a link,
/// changed between directory and file, or was written since it was observed is no longer the entry the plan described,
/// and is left. Links are never followed out of the root: a link among the entries is reported and never removed, a
/// link inside a directory is neither counted nor followed, and a directory is deleted without following the links in
/// it. Sizes are the lengths of the files counted, as <see cref="Resource.Bytes"/> says, and «at least» when a count
/// stopped short.
/// </para>
/// <para>
/// Only what can go is walked: a directory whose owner is still going is kept whatever its size, so its size is left
/// unknown rather than counted file by file at every observation — a running task's build output is the largest thing
/// under a worker's root and the one thing a pass never removes. A file's length comes with the listing and is always
/// given. A directory that goes without having been measured, as the asking owner's own may, is measured as it is
/// released, so the release still says what it gave back.
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

        if (Changed(resource, entry) is { } change)
            return Task.FromResult(Release.NotTheSame(resource, change));

        var released = resource.Bytes is null ? MeasuredAsItGoes(resource, entry, cancellationToken) : resource;

        if (entry is DirectoryInfo directory)
            directory.Delete(recursive: true);
        else
            entry.Delete();

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
        var (bytes, isAtLeast) = isMeasured ? Measured(entry, cancellationToken) : (null, false);

        var resource = new Resource
        {
            Kind = Name,
            Key = entry.FullName,
            Title = $"{Name} {entry.Name} of {owner}",
            Owner = owner,
            Evidence = Evidence.Label,
            Bytes = bytes,
            IsAtLeast = isAtLeast,
            IsRegenerable = options.IsRegenerable,
            Blocker = Blocker(entry, isLink, owner, ended, now),
            Facts = Facts(entry)
        };

        return resource;
    }

    private string? Blocker(FileSystemInfo entry, bool isLink, Owner owner, Dictionary<Owner, DateTimeOffset> ended, DateTimeOffset now)
    {
        if (isLink)
            return "a link: what it points at is never followed out of the root";

        if (now.UtcDateTime - entry.LastWriteTimeUtc < options.Grace)
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

    private static (long? Bytes, bool IsAtLeast) Measured(FileSystemInfo entry, CancellationToken cancellationToken)
    {
        if (entry is FileInfo file)
            return (file.Length, false);

        long bytes = 0;
        var counted = 0;

        try
        {
            foreach (var inside in ((DirectoryInfo)entry).EnumerateFiles("*", Walking))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (++counted > CountedAtMost)
                    return (bytes, true);

                bytes += inside.Length;
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return (bytes, true);
        }

        return (bytes, false);
    }

    /// <summary>The resource with the size of an entry that was not measured when it was observed, measured now that it goes.</summary>
    private static Resource MeasuredAsItGoes(Resource resource, FileSystemInfo entry, CancellationToken cancellationToken)
    {
        var (bytes, isAtLeast) = Measured(entry, cancellationToken);

        return resource with { Bytes = bytes, IsAtLeast = isAtLeast };
    }

    /// <summary>What the entry was when it was observed, so that a release can tell it is still the same one.</summary>
    private static Dictionary<string, string> Facts(FileSystemInfo entry)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EntryFact] = TypeOf(entry),
            [WrittenFact] = entry.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
        };

        if (entry is FileInfo { LinkTarget: null } file)
            facts[LengthFact] = file.Length.ToString(CultureInfo.InvariantCulture);

        return facts;
    }

    private static string? Changed(Resource resource, FileSystemInfo entry)
    {
        if (entry.LinkTarget is not null)
            return "it is a link now, and links are never followed out of the root";

        if (!string.Equals(resource.Facts.GetValueOrDefault(EntryFact), TypeOf(entry), StringComparison.Ordinal))
            return $"it is a {TypeOf(entry)} now";

        if (!string.Equals(resource.Facts.GetValueOrDefault(WrittenFact), entry.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return "written since it was observed";

        if (entry is FileInfo file && !string.Equals(resource.Facts.GetValueOrDefault(LengthFact), file.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            return "its length changed since it was observed";

        return null;
    }

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
}
