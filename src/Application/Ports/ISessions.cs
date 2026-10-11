namespace Snail.MCP.CleanUp.Core.Application.Ports;

/// <summary>The sessions of agents this machine knows of, and the one this process belongs to.</summary>
public interface ISessions
{
    /// <summary>Every session known, alive or ended.</summary>
    Task<IReadOnlyList<Session>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The owner this process acts for; nobody when it cannot tell.</summary>
    Task<Owner> CurrentAsync(CancellationToken cancellationToken);
}
