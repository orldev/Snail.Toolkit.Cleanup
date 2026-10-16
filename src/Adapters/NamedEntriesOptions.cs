namespace Snail.Toolkit.Cleanup.Adapters;

/// <summary>What a <see cref="NamedEntries"/> kind looks at: which entries under which root, and how a name carries its owner.</summary>
/// <remarks>
/// Two instances of the kind cover a platform worker: the folders of tasks under the workspace root, each named for its
/// task, and the keys of projects in a folder of their own, each named for its project and matched by a pattern.
/// </remarks>
public sealed record NamedEntriesOptions
{
    /// <summary>The kind of resource the entries are, as the host names it and places it in its <see cref="KindOrder"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>The directory the entries sit directly in; nothing below it or beside it is looked at.</summary>
    public required string Root { get; init; }

    /// <summary>The kind of owner a name carries, such as <c>task</c> or <c>project</c>.</summary>
    public required string OwnerKind { get; init; }

    /// <summary>The id of the owner an entry's name carries; null when the name carries none, and then the entry is never touched.</summary>
    public required Func<string, string?> OwnerIn { get; init; }

    /// <summary>Whether the entries are directories or files.</summary>
    public EntryType Entries { get; init; } = EntryType.Directories;

    /// <summary>The names looked at, with <c>*</c> for any run of characters and <c>?</c> for one, matched with case.</summary>
    public string Pattern { get; init; } = "*";

    /// <summary>How long an entry is left after it was last written and after its owner ended: something may still be using it.</summary>
    public TimeSpan Grace { get; init; }

    /// <summary>Whether an entry rebuilds itself when removed, as a working copy a task can make again does.</summary>
    public bool IsRegenerable { get; init; }

    /// <summary>What a release says it did with an entry, in the host's own words; null for «deleted from» the root.</summary>
    /// <remarks>
    /// The host knows why an entry of its own could go — a task's folder is a cache because the task's branch carries its
    /// work — and its log reads better saying so than naming the root every time. The entry is given as it was released,
    /// measured when it was.
    /// </remarks>
    public Func<Resource, string>? ReleaseDetail { get; init; }
}
