namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>The kinds of resource every host may have, whatever it runs on and whoever its owners are.</summary>
/// <remarks>
/// Only kinds that mean the same on any machine are named here: a running program, the objects of a container engine,
/// a file. A host's own kinds — a simulator, an editor's state, a working folder of a task — are strings of its own,
/// placed among these by a <see cref="KindOrder"/>.
/// </remarks>
public static class Kinds
{
    /// <summary>A running program.</summary>
    public const string Process = "process";

    /// <summary>A container of a container engine, such as Docker or Podman.</summary>
    public const string Container = "container";

    /// <summary>A network of a container engine.</summary>
    public const string Network = "network";

    /// <summary>A volume of a container engine.</summary>
    public const string Volume = "volume";

    /// <summary>An image of a container engine.</summary>
    public const string Image = "image";

    /// <summary>A file or a directory.</summary>
    public const string File = "file";
}
