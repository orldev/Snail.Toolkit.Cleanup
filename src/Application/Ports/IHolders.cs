namespace Snail.Toolkit.Cleanup.Application.Ports;

/// <summary>The owners the host knows of — sessions, tasks, projects — and the one this process acts for.</summary>
/// <remarks>
/// A host that cannot read its owners fails rather than answer with fewer: an owner missing from the list is taken for
/// one that is over, and what it holds would be removed. A census that cannot list its holders is no census at all.
/// </remarks>
public interface IHolders
{
    /// <summary>Every owner known, still going or over.</summary>
    Task<IReadOnlyList<Holder>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The owner this process acts for; nobody when it cannot tell.</summary>
    Task<Owner> CurrentAsync(CancellationToken cancellationToken);
}
