using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Tests;

public sealed class KindOrderTests
{
    [Fact]
    public void General_ListsTheKindsEveryHostMayHave_InTheOrderTheyAreTakenApart() =>
        Assert.Equal([Kinds.Process, Kinds.Container, Kinds.Network, Kinds.Volume, Kinds.Image, Kinds.File], KindOrder.General.Listed);

    [Fact]
    public void Rank_OfAKindNotListed_StandsAfterEveryListedKind() =>
        Assert.True(KindOrder.General.Rank("simulator") > KindOrder.General.Rank(Kinds.File));

    [Fact]
    public void After_PlacesTheHostsKinds_RightAfterTheOneNamed_AndLeavesTheOrderItWasMadeFrom()
    {
        var order = KindOrder.General.After(Kinds.Process, "mcp", "simulator");

        Assert.Equal([Kinds.Process, "mcp", "simulator", Kinds.Container, Kinds.Network, Kinds.Volume, Kinds.Image, Kinds.File], order.Listed);
        Assert.Equal(6, KindOrder.General.Listed.Count);
    }

    [Fact]
    public void Before_PlacesTheHostsKinds_RightBeforeTheOneNamed() =>
        Assert.Equal(
            [Kinds.Process, Kinds.Container, Kinds.Network, Kinds.Volume, Kinds.Image, "workspace", "project-key", Kinds.File],
            KindOrder.General.Before(Kinds.File, "workspace", "project-key").Listed);

    /// <summary>A host of agents keeps the order it always had by placing its own kinds among the general ones.</summary>
    [Fact]
    public void HostOfAgents_PlacingItsKinds_GetsTheOrderItAlwaysHad()
    {
        var order = KindOrder.General
            .After(Kinds.Process, "mcp", "forward", "emulator", "simulator", "avd")
            .After(Kinds.Image, "dangling", "buildcache", "buildhistory")
            .Before(Kinds.File, "worktree", "editor", "testresults", "remains", "outside");

        Assert.Equal(
            [
                "process", "mcp", "forward", "emulator", "simulator", "avd", "container", "network", "volume", "image", "dangling", "buildcache",
                "buildhistory", "worktree", "editor", "testresults", "remains", "outside", "file"
            ],
            order.Listed);
    }

    [Fact]
    public void Kind_NamedTwice_IsRefused() =>
        Assert.Throws<ArgumentException>(() => KindOrder.General.After(Kinds.Process, Kinds.File));

    [Fact]
    public void Placing_NextToAKindNotInTheOrder_IsRefused() =>
        Assert.Throws<ArgumentException>(() => KindOrder.General.Before("simulator", "emulator"));
}
