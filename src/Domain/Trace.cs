namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>One thing a trail saw on the machine, before anyone has said what it is.</summary>
/// <param name="Trail">The trail that saw it: files, processes, ports, launch agents, containers.</param>
/// <param name="Key">What tells it apart within its trail, such as a path.</param>
/// <param name="Title">What a person reads to recognise it.</param>
/// <param name="Bytes">Space it takes, when measured.</param>
/// <remarks>
/// A trace is how something no kind knows about is noticed at all: every trail is followed before a session
/// and after it, and what is new and claimed by nobody is a discovery rather than a silence.
/// </remarks>
public sealed record Trace(string Trail, string Key, string Title, long? Bytes = null)
{
    /// <summary>Its identity across all trails.</summary>
    public string Id => $"{Trail}:{Key}";
}
