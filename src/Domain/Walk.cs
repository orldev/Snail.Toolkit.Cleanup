namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>What a trail showed when a kind followed it to look.</summary>
/// <param name="Trail">The name of the trail, such as <c>file</c>.</param>
/// <param name="Traces">Everything it showed.</param>
public sealed record Walk(string Trail, IReadOnlyList<Trace> Traces);
