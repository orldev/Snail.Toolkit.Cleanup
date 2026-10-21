namespace Snail.Toolkit.Cleanup.Application.Ports;

/// <summary>One kind of thing an owner can leave behind: how to find all of them, and how to let one go.</summary>
public interface IResourceKind
{
    /// <summary>The kind it answers for, one of <see cref="Kinds"/> or a kind of the host, placed by its <see cref="KindOrder"/>.</summary>
    string Name { get; }

    /// <summary>Everything of this kind there is right now, with whatever tells whose it is among the holders given.</summary>
    /// <remarks>
    /// Looking must change nothing. A kind whose program would start a daemon only to be asked a question
    /// does not ask it, and says in its note that it could not see. A kind may look synchronously and answer a
    /// finished task, as one walking a disk does: the census runs every kind on a thread of its own, so they are
    /// looked at at once either way.
    /// </remarks>
    Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken);

    /// <summary>Lets one resource go, after checking it is still the one that was observed.</summary>
    /// <remarks>
    /// There is no timeout but the token: a release that hangs holds the cleanup until the host cancels, and a release
    /// cancelled midway is answered as failed, with whether it went unknown. A failure thrown out of here becomes a
    /// failed release with the exception's message; a kind that can say more answers <see cref="Release.Refused"/> itself.
    /// </remarks>
    Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken);
}
