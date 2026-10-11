namespace Snail.MCP.CleanUp.Core.Application.Ports;

/// <summary>One kind of thing an agent can leave on a machine: how to find all of them, and how to let one go.</summary>
public interface IResourceKind
{
    /// <summary>The kind it answers for, one of <see cref="Kinds"/> or a kind of its own.</summary>
    string Name { get; }

    /// <summary>Everything of this kind there is right now, with whatever tells whose it is.</summary>
    /// <remarks>
    /// Looking must change nothing. A kind whose program would start a daemon only to be asked a question
    /// does not ask it, and says in its note that it could not see.
    /// </remarks>
    Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken);

    /// <summary>Lets one resource go, after checking it is still the one that was observed.</summary>
    Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken);
}
