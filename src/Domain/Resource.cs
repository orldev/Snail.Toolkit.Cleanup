namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>Something an owner may have left behind: what it is, whose it is, and how sure that is.</summary>
public sealed record Resource
{
    /// <summary>What kind of thing it is: one of <see cref="Kinds"/>, a kind of the host, or a kind a learned rule names.</summary>
    public required string Kind { get; init; }

    /// <summary>What tells it apart from every other of its kind, stable for as long as it exists.</summary>
    /// <remarks>
    /// A process is keyed by its id and the moment it started, never by its id alone: ids are reused, and a
    /// key without the start would one day name somebody else's program.
    /// </remarks>
    public required string Key { get; init; }

    /// <summary>What a person reads to recognise it.</summary>
    public required string Title { get; init; }

    /// <summary>Whose it is, as far as can be told.</summary>
    public Owner Owner { get; init; } = Owner.Nobody;

    /// <summary>How the owner is known.</summary>
    public Evidence Evidence { get; init; }

    /// <summary>Space it takes on disk, when that was measured.</summary>
    public long? Bytes { get; init; }

    /// <summary>Whether the count of <see cref="Bytes"/> stopped short, so that it takes at least that much and maybe far more.</summary>
    public bool IsAtLeast { get; init; }

    /// <summary>When it came to be, when that is known.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Whether it rebuilds itself when removed, so removing it frees space rather than losing work.</summary>
    public bool IsRegenerable { get; init; }

    /// <summary>Whether it belongs to the machine rather than to anyone: a cache every owner writes into.</summary>
    /// <remarks>A shared resource is never removed on ownership, only when caches are asked for by name.</remarks>
    public bool IsShared { get; init; }

    /// <summary>Why it cannot be released right now, when something stands in the way.</summary>
    public string? Blocker { get; init; }

    /// <summary>The ids of resources that have to be released before this one.</summary>
    public IReadOnlyList<string> After { get; init; } = [];

    /// <summary>What the kind that found it needs to recognise it again and to release it.</summary>
    public IReadOnlyDictionary<string, string> Facts { get; init; } = new Dictionary<string, string>();

    /// <summary>Its identity across the whole account: kind and key.</summary>
    public string Id => $"{Kind}:{Key}";
}
