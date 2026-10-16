namespace Snail.Toolkit.Cleanup.Adapters;

/// <summary>Which entries under a root a <see cref="NamedEntries"/> kind looks at.</summary>
public enum EntryType
{
    /// <summary>Directories, each taken apart with everything in it.</summary>
    Directories,

    /// <summary>Files.</summary>
    Files
}
