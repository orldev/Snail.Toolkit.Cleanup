namespace Snail.Toolkit.Cleanup.Application.Ports;

/// <summary>Plans that were shown, kept until they are carried out or go stale.</summary>
public interface IPlans
{
    /// <summary>Keeps a plan under its id.</summary>
    Task SaveAsync(Plan plan, CancellationToken cancellationToken);

    /// <summary>The plan with this id; null when there is none.</summary>
    Task<Plan?> FindAsync(string id, CancellationToken cancellationToken);
}
