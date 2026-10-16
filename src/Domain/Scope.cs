namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>Which resources a plan is about.</summary>
public enum Scope
{
    /// <summary>What the asking owner holds.</summary>
    Mine,

    /// <summary>What owners that are over left behind.</summary>
    Orphans,

    /// <summary>Shared caches that rebuild themselves; never part of anything else.</summary>
    Caches,

    /// <summary>All of the above at once.</summary>
    Everything
}
