namespace Snail.MCP.CleanUp.Core.Application.Ports;

/// <summary>What every trail showed when a session began: the line «new» is measured from.</summary>
public interface IBaselines
{
    /// <summary>The trace ids seen when the owner's session began, as hashes; null when none was taken.</summary>
    Task<Baseline?> FindAsync(Owner owner, CancellationToken cancellationToken);

    /// <summary>Takes the baseline of an owner, once; a second call changes nothing.</summary>
    Task SaveAsync(Owner owner, IReadOnlyList<string> traceIds, DateTimeOffset takenAt, CancellationToken cancellationToken);

    /// <summary>Drops the baselines of every owner but these, once their sessions are over.</summary>
    Task KeepOnlyAsync(IReadOnlySet<Owner> owners, CancellationToken cancellationToken);
}
