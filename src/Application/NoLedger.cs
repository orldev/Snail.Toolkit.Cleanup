namespace Snail.Toolkit.Cleanup.Application;

/// <summary>The ledger of a host whose resources carry their owner in their own names, so there is nothing to write down.</summary>
/// <remarks>
/// A platform that names a task's folder after the task, and a project's key after the project, proves ownership
/// by the name alone. Such a host plans and cleans with the same planner and cleaner as everyone else, and hands
/// them this in place of a ledger it would never read.
/// </remarks>
public sealed class NoLedger : ILedger
{
    /// <inheritdoc/>
    public Task RecordAsync(Holding holding, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ForgetAsync(string resourceId, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<IReadOnlyList<Holding>> HoldingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Holding>>([]);

    /// <inheritdoc/>
    public Task KeepAsync(string resourceId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<bool> TryUnkeepAsync(string resourceId, CancellationToken cancellationToken) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, string>> KeptAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}
