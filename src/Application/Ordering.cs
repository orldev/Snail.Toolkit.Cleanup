namespace Snail.MCP.CleanUp.Core.Application;

/// <summary>Puts the decisions of a plan in the order they can be carried out safely.</summary>
/// <remarks>
/// Removals go first and in dependency order: whatever a resource says it must follow comes before it, and
/// among the rest the order of <see cref="Kinds"/> decides. The decisions that remove nothing follow, so that
/// reading a plan from the top shows what will happen before what will not.
/// </remarks>
public static class Ordering
{
    /// <summary>The decisions, removals first in a safe order, then everything else.</summary>
    public static IReadOnlyList<Decision> Of(IReadOnlyList<Decision> decisions)
    {
        var removals = decisions.Where(decision => decision.Verdict == Verdict.Remove).ToList();
        var rest = decisions
            .Where(decision => decision.Verdict != Verdict.Remove)
            .OrderBy(decision => decision.Verdict)
            .ThenBy(decision => Kinds.Rank(decision.Resource.Kind))
            .ThenBy(decision => decision.Resource.Id, StringComparer.Ordinal);

        return [.. Removals(removals), .. rest];
    }

    /// <summary>
    /// A topological order with a priority: of the decisions whose predecessors are done, the one whose kind goes
    /// earliest is taken next. A cycle cannot be honoured, so what is left in one goes in kind order at the end
    /// rather than not at all.
    /// </summary>
    private static IEnumerable<Decision> Removals(List<Decision> removals)
    {
        var waiting = removals.ToDictionary(decision => decision.Resource.Id, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);

        while (waiting.Count > 0)
        {
            var ready = waiting.Values
                .Where(decision => decision.Resource.After.All(before => done.Contains(before) || !waiting.ContainsKey(before)))
                .OrderBy(Priority)
                .ThenBy(decision => decision.Resource.Id, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? waiting.Values.OrderBy(Priority).ThenBy(decision => decision.Resource.Id, StringComparer.Ordinal).First();

            waiting.Remove(ready.Resource.Id);
            done.Add(ready.Resource.Id);

            yield return ready;
        }
    }

    private static int Priority(Decision decision) => Kinds.Rank(decision.Resource.Kind);
}
