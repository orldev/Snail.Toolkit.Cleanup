namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>Who a resource belongs to: an owner of a kind the host names, with its id, or nobody known.</summary>
/// <param name="Kind">What kind of owner it is, as the host names it: a session of an agent, a task, a project.</param>
/// <param name="Id">The id the owner has among the owners of its kind.</param>
/// <remarks>
/// An owner is a kind and an id rather than an id alone, because one account can be kept for owners of several kinds
/// at once — the sessions of agents, the tasks of a platform, its projects — and the kind keeps an id of one from ever
/// being read as an id of another. The kinds are the host's: the library names none but <see cref="Nobody"/>.
/// </remarks>
public readonly record struct Owner(string Kind, string Id)
{
    /// <summary>The owner nobody can be proven to be.</summary>
    public static Owner Nobody { get; } = new("nobody", string.Empty);

    /// <summary>Whether an owner is known at all.</summary>
    public bool IsKnown => !string.Equals(Kind, Nobody.Kind, StringComparison.Ordinal);

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
