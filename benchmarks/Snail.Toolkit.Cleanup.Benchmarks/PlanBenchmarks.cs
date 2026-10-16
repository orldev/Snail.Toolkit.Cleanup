using BenchmarkDotNet.Attributes;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;

namespace Snail.Toolkit.Cleanup.Benchmarks;

/// <summary>Deciding a plan and ordering its removals over an inventory of five thousand resources.</summary>
/// <remarks>
/// The sizes are the ones the ordering was measured at when it was made one pass (2026-10-12): five thousand removals, a
/// third of them processes, every eleventh waiting for the one before it.
/// </remarks>
[MemoryDiagnoser]
public class PlanBenchmarks
{
    private const int Resources = 5_000;

    private Inventory _inventory = null!;

    private IReadOnlyList<Decision> _removals = [];

    [GlobalSetup]
    public void Setup()
    {
        var owner = Owner.Session("bench");
        List<Resource> resources = [.. Enumerable.Range(0, Resources).Select(index => Made(index, owner))];

        _inventory = new Inventory(
            DateTimeOffset.UnixEpoch,
            owner,
            resources,
            [new Session("bench", 1, null, null, IsAlive: true)],
            new Dictionary<string, string>(),
            [],
            new Dictionary<string, string>());
        _removals = [.. resources.Select(resource => new Decision(resource, Verdict.Remove, "proven"))];
    }

    /// <summary>Every resource judged and the plan ordered, the way a host plans a cleanup of everything.</summary>
    [Benchmark]
    public int DecidePlan() => Planner.Decide(_inventory, Scope.Everything, "bench").Decisions.Count;

    /// <summary>Five thousand removals put in a safe order.</summary>
    [Benchmark]
    public int OrderRemovals() => Ordering.Of(_removals).Count;

    private static Resource Made(int index, Owner owner) => new()
    {
        Kind = index % 3 == 0 ? Kinds.Process : Kinds.File,
        Key = $"{index}",
        Title = $"resource {index}",
        Owner = index % 2 == 0 ? owner : Owner.Session($"gone-{index % 7}"),
        Evidence = (Evidence)(index % 5),
        After = index % 11 == 0 ? [$"{Kinds.Process}:{index - 1}"] : []
    };
}
