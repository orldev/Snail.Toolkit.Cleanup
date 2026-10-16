namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>What a rule says to do with the traces it matches.</summary>
public enum RuleAction
{
    /// <summary>They are known and harmless: never a discovery, never a resource.</summary>
    Ignore,

    /// <summary>They are a known kind of leftover: shown as resources, removed only with proof of an owner.</summary>
    Report,

    /// <summary>They are a shared cache that rebuilds itself: removed only when caches are asked for.</summary>
    Cache
}

/// <summary>A kind of leftover written down as data: which traces it covers and what they are.</summary>
/// <param name="Trail">The trail whose traces it matches, such as <c>file</c>.</param>
/// <param name="Pattern">The keys it matches: <c>*</c> stands for one path segment, <c>**</c> for any number, <c>~</c> for home.</param>
/// <param name="Action">What is done with what it matches.</param>
/// <remarks>
/// Rules are how the account learns a kind nobody wrote code for. A discovery shows something new; one
/// line in the rules file turns every later trace like it into something known — without a release of
/// the host, and without anyone having to rediscover it.
/// </remarks>
public sealed record Rule(string Trail, string Pattern, RuleAction Action)
{
    /// <summary>Why the rule exists, for the person reading the file later.</summary>
    public string? Note { get; init; }

    /// <summary>A program that keeps what this rule matches open; while it runs, nothing matched may be released.</summary>
    public string? BlockedWhileRunning { get; init; }

    /// <summary>Whether the rule covers this trace.</summary>
    public bool Matches(Trace trace) =>
        string.Equals(Trail, trace.Trail, StringComparison.Ordinal) && Glob.Matches(Pattern, trace.Key);
}
