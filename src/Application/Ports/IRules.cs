namespace Snail.MCP.CleanUp.Core.Application.Ports;

/// <summary>The rules the account knows: the ones it ships with and the ones it learned.</summary>
public interface IRules
{
    /// <summary>Every rule, the learned ones after the shipped ones so that they are read last.</summary>
    Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Writes a learned rule down, where a person can read and edit it.</summary>
    Task AddAsync(Rule rule, CancellationToken cancellationToken);
}
