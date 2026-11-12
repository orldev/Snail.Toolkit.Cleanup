namespace Snail.Toolkit.Cleanup.Application;

/// <summary>A thread made for one look of the census, outside the thread pool, which its awaits come back to.</summary>
/// <remarks>
/// A kind of the host walks its disk where it is: before its first await, and after one too — Snail.MCP.CleanUp's
/// outputs and file kinds read the process table or the rules first and then walk for seconds. A thread that only
/// starts the look would hand that walk to the pool at the first await, so the thread is also the look's
/// <see cref="SynchronizationContext"/>: an await that captures it, as an await does unless told not to, goes on here.
/// <para>
/// What is posted once the look has ended goes to the pool, so work the look left behind — a task it started and
/// another look awaits — never waits for a thread that is gone.
/// </para>
/// </remarks>
internal sealed class LookThread : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _posted = new();

    private bool _isOver;

    /// <summary>The look's answer, looked for on a thread of its own; the caller goes on from the pool.</summary>
    public static Task<T> RunAsync<T>(Func<Task<T>> look, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => new LookThread().Run(look, answer)) { IsBackground = true, Name = "Snail.Toolkit.Cleanup look" };

        thread.Start();

        return answer.Task;
    }

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_posted)
        {
            if (!_isOver)
            {
                _posted.Enqueue((d, state));
                Monitor.Pulse(_posted);

                return;
            }
        }

        ThreadPool.QueueUserWorkItem(static posted => posted.Callback(posted.State), (Callback: d, State: state), preferLocal: false);
    }

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("a look's thread is only posted to");

    /// <inheritdoc/>
    public override SynchronizationContext CreateCopy() => this;

    private void Run<T>(Func<Task<T>> look, TaskCompletionSource<T> answer)
    {
        SetSynchronizationContext(this);

        try
        {
            var looking = look();

            looking.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(Wake);
            Pump(looking);
            answer.SetFromTask(looking);
        }
        catch (Exception failure)
        {
            End();
            answer.SetException(failure);
        }
    }

    /// <summary>Runs what the look's awaits post here until the look has ended and nothing is left to run.</summary>
    private void Pump(Task looking)
    {
        while (true)
        {
            (SendOrPostCallback Callback, object? State) next;

            lock (_posted)
            {
                while (_posted.Count == 0 && !looking.IsCompleted)
                    Monitor.Wait(_posted);

                if (_posted.Count == 0)
                {
                    _isOver = true;

                    return;
                }

                next = _posted.Dequeue();
            }

            next.Callback(next.State);
        }
    }

    private void End()
    {
        lock (_posted)
        {
            _isOver = true;

            while (_posted.TryDequeue(out var left))
                ThreadPool.QueueUserWorkItem(static posted => posted.Callback(posted.State), left, preferLocal: false);
        }
    }

    /// <summary>Wakes the pump when the look has ended, so that it sees the end even with nothing posted.</summary>
    private void Wake()
    {
        lock (_posted)
            Monitor.Pulse(_posted);
    }
}
