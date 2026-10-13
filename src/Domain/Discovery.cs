namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>Something new that appeared during a session and that no known kind or learned rule accounts for.</summary>
/// <param name="Trace">What was seen.</param>
/// <param name="Owner">The session during which it appeared, when only one was running or it appeared inside its directory.</param>
/// <param name="FirstSeen">When it was first noticed.</param>
/// <param name="LastSeen">When it was last noticed.</param>
/// <param name="Sightings">How many censuses have seen it.</param>
/// <remarks>
/// A discovery is never removed. It is written down so that a person can see what agents leave that nobody
/// has thought of yet, and turn it into a rule — the account learns the new kind as data, not as code.
/// While the thing is there, every census sees it again and it stays in the report. It stays written down for
/// <see cref="Remembered"/> after the thing itself is gone, because «agents leave this» is still true for a while —
/// and no longer, because a record nobody prunes grows by every temporary file an agent ever made.
/// </remarks>
public sealed record Discovery(Trace Trace, Owner Owner, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, int Sightings)
{
    /// <summary>How long a discovery stays written down after its thing was last seen.</summary>
    /// <remarks>Measured on 2026-10-13: a ledger nothing pruned held 100 000 discoveries, and every census read all of them twice.</remarks>
    public static TimeSpan Remembered { get; } = TimeSpan.FromDays(3);

    /// <summary>Whether the thing is still there at the last census.</summary>
    public bool IsPresent { get; init; } = true;
}
