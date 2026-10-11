namespace Snail.MCP.CleanUp.Core.Application.Ports;

/// <summary>The written record of what agents left that nothing accounted for.</summary>
public interface IDiscoveries
{
    /// <summary>Writes down what a census found new, or counts one more sighting of what was already written.</summary>
    Task NoteAsync(IReadOnlyList<Discovery> discoveries, CancellationToken cancellationToken);

    /// <summary>Everything written down, with whether it is still there.</summary>
    Task<IReadOnlyList<Discovery>> ListAsync(IReadOnlySet<string> present, CancellationToken cancellationToken);

    /// <summary>Crosses off the discoveries a rule now covers; how many there were.</summary>
    Task<int> SettleAsync(Rule rule, CancellationToken cancellationToken);
}
