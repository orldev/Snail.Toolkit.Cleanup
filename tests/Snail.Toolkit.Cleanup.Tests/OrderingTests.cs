using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.Toolkit.Cleanup.Tests.Support.Things;

namespace Snail.Toolkit.Cleanup.Tests;

public sealed class OrderingTests
{
    private static Decision Removing(Resource resource) => new(resource, Verdict.Remove, "proven");

    private static IReadOnlyList<string> Order(params Decision[] decisions) =>
        [.. Ordering.Of(decisions).Select(decision => decision.Resource.Id)];

    /// <summary>A simulator stopped while the test runner still drives it hangs the runner instead of ending it.</summary>
    [Fact]
    public void Removals_GoInTheOrderOfTheirKinds()
    {
        var order = Order(
            Removing(Of(Kinds.File, "/scratch")),
            Removing(Of(Kinds.Simulator, "udid")),
            Removing(Of(Kinds.Process, "7@1")),
            Removing(Of(Kinds.Network, "net")),
            Removing(Of(Kinds.Container, "box")));

        Assert.Equal(["process:7@1", "simulator:udid", "container:box", "network:net", "file:/scratch"], order);
    }

    [Fact]
    public void WhatAResourceMustFollow_GoesBeforeIt_WhateverItsKind()
    {
        var file = Of(Kinds.File, "/log");
        var process = Of(Kinds.Process, "7@1") with { After = [file.Id] };

        Assert.Equal([file.Id, process.Id], Order(Removing(process), Removing(file)));
    }

    [Fact]
    public void ACycle_LosesNoRemoval()
    {
        var first = Of(Kinds.Process, "1@1") with { After = ["process:2@1"] };
        var second = Of(Kinds.Process, "2@1") with { After = ["process:1@1"] };

        Assert.Equal(2, Order(Removing(first), Removing(second)).Count);
    }

    [Fact]
    public void Removals_ComeBeforeWhatIsOnlyReportedOrKept()
    {
        var order = Order(
            new Decision(Of(Kinds.Process, "kept"), Verdict.Keep, "asked"),
            new Decision(Of(Kinds.Process, "named"), Verdict.Report, "unproven"),
            Removing(Of(Kinds.File, "/go")));

        Assert.Equal(["file:/go", "process:named", "process:kept"], order);
    }

    /// <summary>The order taken in one pass is the order the plan was always taken apart in, cycles and all.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Removals_OrderedInOnePass_ComeInTheOrderTheyAlwaysCame(int seed)
    {
        var random = new Random(seed);
        string[] kinds = [Kinds.Process, Kinds.Simulator, Kinds.Container, Kinds.Network, Kinds.Volume, Kinds.File, "unknown"];
        var ids = Enumerable.Range(0, 300).Select(index => $"{kinds[random.Next(kinds.Length)]}:{random.Next(1_000):d4}").Distinct().ToList();
        List<Decision> decisions =
        [
            .. ids.Select(id => new Decision(
                Of(id[..id.IndexOf(':', StringComparison.Ordinal)], id[(id.IndexOf(':', StringComparison.Ordinal) + 1)..]) with
                {
                    After = [.. Enumerable.Range(0, random.Next(4)).Select(_ => random.Next(10) == 0 ? "file:elsewhere" : ids[random.Next(ids.Count)])]
                },
                random.Next(5) == 0 ? Verdict.Report : Verdict.Remove,
                "random"))
        ];

        Assert.Equal(Reference(decisions).Select(decision => decision.Resource.Id), Ordering.Of(decisions).Select(decision => decision.Resource.Id));
    }

    /// <summary>The ordering as it was first written, which looked over every waiting removal for each one taken.</summary>
    private static List<Decision> Reference(List<Decision> decisions)
    {
        var waiting = decisions.Where(decision => decision.Verdict == Verdict.Remove).ToDictionary(decision => decision.Resource.Id, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<Decision>();

        while (waiting.Count > 0)
        {
            var ready = waiting.Values
                .Where(decision => decision.Resource.After.All(before => done.Contains(before) || !waiting.ContainsKey(before)))
                .OrderBy(decision => Kinds.Rank(decision.Resource.Kind))
                .ThenBy(decision => decision.Resource.Id, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? waiting.Values.OrderBy(decision => Kinds.Rank(decision.Resource.Kind)).ThenBy(decision => decision.Resource.Id, StringComparer.Ordinal).First();

            waiting.Remove(ready.Resource.Id);
            done.Add(ready.Resource.Id);
            ordered.Add(ready);
        }

        return
        [
            .. ordered,
            .. decisions
                .Where(decision => decision.Verdict != Verdict.Remove)
                .OrderBy(decision => decision.Verdict)
                .ThenBy(decision => Kinds.Rank(decision.Resource.Kind))
                .ThenBy(decision => decision.Resource.Id, StringComparer.Ordinal)
        ];
    }
}
