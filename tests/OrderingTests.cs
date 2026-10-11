using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Core;

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
}
