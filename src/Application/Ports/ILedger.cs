namespace Snail.MCP.CleanUp.Core.Application.Ports;

/// <summary>The written account of who holds what, kept across sessions so that one which died is still answered for.</summary>
public interface ILedger
{
    /// <summary>Writes down a holding, replacing what was known of that resource only when the new evidence is stronger.</summary>
    Task RecordAsync(Holding holding, CancellationToken cancellationToken);

    /// <summary>Crosses a resource off, once it is gone.</summary>
    Task ForgetAsync(string resourceId, CancellationToken cancellationToken);

    /// <summary>Every holding written down.</summary>
    Task<IReadOnlyList<Holding>> HoldingsAsync(CancellationToken cancellationToken);

    /// <summary>Asks for a resource to be left alone, with the reason.</summary>
    Task KeepAsync(string resourceId, string reason, CancellationToken cancellationToken);

    /// <summary>Withdraws a request to leave a resource alone; false when there was none.</summary>
    Task<bool> UnkeepAsync(string resourceId, CancellationToken cancellationToken);

    /// <summary>The resources somebody asked to be left alone, with the reasons.</summary>
    Task<IReadOnlyDictionary<string, string>> KeptAsync(CancellationToken cancellationToken);
}
