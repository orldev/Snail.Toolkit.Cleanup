namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>Which resources a plan is about.</summary>
public enum Scope
{
    /// <summary>What the asking session holds.</summary>
    Mine,

    /// <summary>What sessions that are no longer running left behind.</summary>
    Orphans,

    /// <summary>Shared caches that rebuild themselves; never part of anything else.</summary>
    Caches,

    /// <summary>All of the above at once.</summary>
    Everything
}
