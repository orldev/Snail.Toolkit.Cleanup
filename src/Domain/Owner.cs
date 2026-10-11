namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>Who a resource belongs to: a session of an agent on this machine, a task of a platform, or nobody known.</summary>
/// <remarks>
/// An owner is a kind and an id rather than a session id alone, because the same account is kept for two
/// masters: a session of Claude Code on a developer's machine and a task of a platform running many agents.
/// The kind keeps an id of one from ever being read as an id of the other.
/// </remarks>
public readonly record struct Owner(string Kind, string Id)
{
    /// <summary>The owner nobody can be proven to be.</summary>
    public static Owner Nobody { get; } = new("nobody", string.Empty);

    /// <summary>Whether an owner is known at all.</summary>
    public bool IsKnown => !string.Equals(Kind, Nobody.Kind, StringComparison.Ordinal);

    /// <summary>The session of an agent with this id.</summary>
    public static Owner Session(string id) => new("session", id);

    /// <summary>The task of a platform with this id.</summary>
    public static Owner Task(string id) => new("task", id);

    /// <summary>The owner a text written by <see cref="ToString"/> names; nobody when it names none.</summary>
    public static Owner Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Nobody;

        var colon = text.IndexOf(':', StringComparison.Ordinal);

        return colon <= 0 || colon == text.Length - 1 ? Nobody : new Owner(text[..colon], text[(colon + 1)..]);
    }

    /// <summary>The form an owner is stored and compared in: <c>kind:id</c>.</summary>
    public override string ToString() => IsKnown ? $"{Kind}:{Id}" : Kind;
}
