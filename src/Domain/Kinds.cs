namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>The kinds of resource this account knows by name, in the order they are taken apart.</summary>
/// <remarks>
/// The order is what makes a release safe without a provider having to know its neighbours: a process goes
/// before the directory it writes into, the programs driving a simulator before the simulator, a container
/// before the network and the volume it holds. A kind not listed here goes last, after everything that
/// could still be using it. A resource can still name what it must follow, and that wins over the order.
/// </remarks>
public static class Kinds
{
    /// <summary>A running program.</summary>
    public const string Process = "process";

    /// <summary>A rule forwarding a port between this machine and a device.</summary>
    public const string Forward = "forward";

    /// <summary>An iOS simulator device.</summary>
    public const string Simulator = "simulator";

    /// <summary>A running Android emulator.</summary>
    public const string Emulator = "emulator";

    /// <summary>An Android virtual device definition.</summary>
    public const string VirtualDevice = "avd";

    /// <summary>A Docker container.</summary>
    public const string Container = "container";

    /// <summary>A Docker network.</summary>
    public const string Network = "network";

    /// <summary>A Docker volume.</summary>
    public const string Volume = "volume";

    /// <summary>A Docker image.</summary>
    public const string Image = "image";

    /// <summary>Docker's build cache.</summary>
    public const string BuildCache = "buildcache";

    /// <summary>Docker images no tag points at any more, left by every rebuild of a tag.</summary>
    public const string DanglingImages = "dangling";

    /// <summary>BuildKit's records of finished builds.</summary>
    public const string BuildHistory = "buildhistory";

    /// <summary>What an editor keeps about a folder or a window.</summary>
    public const string Editor = "editor";

    /// <summary>A git worktree.</summary>
    public const string Worktree = "worktree";

    /// <summary>A file or a directory.</summary>
    public const string File = "file";

    /// <summary>The order kinds are taken apart in.</summary>
    public static IReadOnlyList<string> Order { get; } =
        [Process, Forward, Emulator, Simulator, VirtualDevice, Container, Network, Volume, Image, DanglingImages, BuildCache, BuildHistory, Worktree, Editor, File];

    /// <summary>Where a kind stands in <see cref="Order"/>; an unknown kind stands after all of them.</summary>
    public static int Rank(string kind)
    {
        var position = Order.IndexOf(kind);

        return position < 0 ? Order.Count : position;
    }

    private static int IndexOf(this IReadOnlyList<string> kinds, string kind)
    {
        for (var position = 0; position < kinds.Count; position++)
        {
            if (string.Equals(kinds[position], kind, StringComparison.Ordinal))
                return position;
        }

        return -1;
    }
}
