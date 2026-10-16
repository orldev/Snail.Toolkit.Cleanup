namespace Snail.Toolkit.Cleanup.Application.Ports;

/// <summary>The written record of what owners left that nothing accounted for.</summary>
public interface IDiscoveries
{
    /// <summary>Writes down what a census found new, or counts one more sighting of what was already written.</summary>
    Task NoteAsync(IReadOnlyList<Discovery> discoveries, CancellationToken cancellationToken);

    /// <summary>Everything written down, with whether it is still there, as a census at <paramref name="now"/> leaves it.</summary>
    /// <remarks>
    /// What is still there is seen again at <paramref name="now"/>. What has been gone longer than
    /// <see cref="Discovery.Remembered"/> is crossed off, and is not read to be crossed off.
    /// </remarks>
    Task<IReadOnlyList<Discovery>> ListAsync(IReadOnlySet<string> present, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Crosses off the discoveries a rule now covers; how many there were.</summary>
    Task<int> SettleAsync(Rule rule, CancellationToken cancellationToken);
}
