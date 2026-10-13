namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>One thing a trail saw on the machine, before anyone has said what it is.</summary>
/// <param name="Trail">The trail that saw it: files, processes, ports, launch agents, containers.</param>
/// <param name="Key">What tells it apart within its trail, such as a path.</param>
/// <param name="Title">What a person reads to recognise it.</param>
/// <param name="Bytes">Space it takes, when measured.</param>
/// <remarks>
/// A trace is how something no kind knows about is noticed at all: every trail is followed before a session
/// and after it, and what is new and claimed by nobody is a discovery rather than a silence.
/// </remarks>
public sealed record Trace(string Trail, string Key, string Title, long? Bytes = null)
{
    private string? _id;

    /// <summary>The trail that saw it: files, processes, ports, launch agents, containers.</summary>
    /// <remarks>Fixed once made, not even changed by <c>with</c>, because <see cref="Id"/> is made from it.</remarks>
    public string Trail { get; } = Trail;

    /// <summary>What tells it apart within its trail, such as a path.</summary>
    /// <remarks>Fixed once made, not even changed by <c>with</c>, because <see cref="Id"/> is made from it.</remarks>
    public string Key { get; } = Key;

    /// <summary>Whether the count of <c>Bytes</c> stopped short, so that it takes at least that much and maybe far more.</summary>
    public bool IsAtLeast { get; init; }

    /// <summary>Its identity across all trails.</summary>
    /// <remarks>
    /// Made the first time it is asked for, then kept: a census asks a trace for its id several times, and each asking
    /// built a new string. A trace nobody asks for its id never makes one.
    /// </remarks>
    public string Id => _id ??= $"{Trail}:{Key}";

    /// <summary>Equal when trail, key, title and size are: whether the id was made yet is not part of what a trace is.</summary>
    public bool Equals(Trace? other) =>
        ReferenceEquals(this, other)
        || (other is not null
            && string.Equals(Trail, other.Trail, StringComparison.Ordinal)
            && string.Equals(Key, other.Key, StringComparison.Ordinal)
            && string.Equals(Title, other.Title, StringComparison.Ordinal)
            && Bytes == other.Bytes
            && IsAtLeast == other.IsAtLeast);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Trail, Key, Title, Bytes, IsAtLeast);
}
