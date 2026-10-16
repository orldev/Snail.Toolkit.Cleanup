namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>What a plan does with a resource.</summary>
public enum Verdict
{
    /// <summary>Release it: ownership is proven and nothing stands in the way.</summary>
    Remove,

    /// <summary>Name it and leave it: it may be an owner's, but that is not proven, or something blocks it.</summary>
    Report,

    /// <summary>Leave it alone on purpose: somebody asked, or another owner still going holds it.</summary>
    Keep
}
