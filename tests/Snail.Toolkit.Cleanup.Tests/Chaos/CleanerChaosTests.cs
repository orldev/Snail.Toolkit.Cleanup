using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.Toolkit.Cleanup.Tests.Support.Things;

namespace Snail.Toolkit.Cleanup.Tests.Chaos;

/// <summary>Crash tests of the cleaner: what it answers when its ledger, its host or its plan let it down.</summary>
public sealed class CleanerChaosTests
{
    private readonly FakeKind _files = new(Kinds.File);

    /// <summary>
    /// Breaks: the record of what went. The file is released, then the ledger fails to cross it off; the failure escapes
    /// ApplyAsync and takes the release with it, so the host cannot tell anyone what is gone.
    /// </summary>
    [Fact]
    public async Task Apply_WhenTheLedgerFailsToForget_StillAnswersWithWhatWasReleased()
    {
        var file = Of(Kinds.File, "/scratch/me", Me);
        var plan = Planner.Decide(Inventory([file]), Scope.Mine, "plan", Order);
        var cleaner = new Cleaner([_files], new FailingLedger());

        var releases = await cleaner.ApplyAsync(plan, CancellationToken.None);

        Assert.Equal([file.Id], _files.Released);
        Assert.Equal(Outcome.Released, Assert.Single(releases).Outcome);
    }

    /// <summary>
    /// Breaks: the record of what went when the host stops the cleanup. The first resource goes, then the token is
    /// cancelled; the cancellation escapes ApplyAsync and the first release is never told.
    /// </summary>
    [Fact]
    public async Task Apply_CancelledAfterTheFirstRelease_StillAnswersWithThatRelease()
    {
        using var stopping = new CancellationTokenSource();
        var first = Of(Kinds.File, "/scratch/a", Me);
        var second = Of(Kinds.File, "/scratch/b", Me);
        var plan = Planner.Decide(Inventory([first, second]), Scope.Mine, "plan", Order);
        var cleaner = new Cleaner([new CancellingKind(Kinds.File, stopping)], new NoLedger());

        var releases = await cleaner.ApplyAsync(plan, stopping.Token);

        Assert.Equal(2, releases.Count);
        Assert.Equal((first.Id, Outcome.Released), (releases[0].ResourceId, releases[0].Outcome));
        Assert.Equal((second.Id, Outcome.Failed), (releases[1].ResourceId, releases[1].Outcome));
        Assert.Contains("stopped", releases[1].Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Breaks: the lifetime of a plan. <see cref="Plan.Lifetime"/> says a plan past ten minutes describes a machine
    /// that no longer exists; a cleaner given a clock refuses a plan made in 1970 instead of carrying it out.
    /// </summary>
    [Fact]
    public async Task Apply_OfAPlanPastItsLifetime_RemovesNothing()
    {
        var file = Of(Kinds.File, "/scratch/me", Me);
        var plan = Planner.Decide(Inventory([file]), Scope.Mine, "plan", Order);

        Assert.False(plan.IsFresh(DateTimeOffset.UtcNow));

        var releases = await new Cleaner([_files], new MemoryLedger(), null, TimeProvider.System).ApplyAsync(plan, CancellationToken.None);

        Assert.Empty(_files.Released);
        Assert.All(releases, release => Assert.NotEqual(Outcome.Released, release.Outcome));
    }

    /// <summary>Breaks: a removal that waits for itself, and twice for the same other one; the order must still place it, once.</summary>
    [Fact]
    public void Ordering_RemovalWaitingForItselfAndTwiceForAnother_IsPlacedOnce()
    {
        var file = Of(Kinds.File, "/log");
        var selfish = Of(Kinds.Process, "7@1") with { After = ["process:7@1", file.Id, file.Id] };
        var ordered = Ordering.Of([new Decision(selfish, Verdict.Remove, "proven"), new Decision(file, Verdict.Remove, "proven")], Order);

        Assert.Equal([file.Id, selfish.Id], ordered.Select(decision => decision.Resource.Id));
    }

    /// <summary>A ledger whose store is locked the moment it is asked to write.</summary>
    private sealed class FailingLedger : ILedger
    {
        public Task RecordAsync(Holding holding, CancellationToken cancellationToken) => throw new IOException("the ledger is locked");

        public Task ForgetAsync(string resourceId, CancellationToken cancellationToken) => throw new IOException("the ledger is locked");

        public Task<IReadOnlyList<Holding>> HoldingsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Holding>>([]);

        public Task KeepAsync(string resourceId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> TryUnkeepAsync(string resourceId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> KeptAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    /// <summary>A kind that releases what it is given and then asks the host to stop, as a user closing the window does.</summary>
    private sealed class CancellingKind(string name, CancellationTokenSource stopping) : IResourceKind
    {
        public string Name => name;

        public Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken) =>
            Task.FromResult(new Observation(Name, [], []));

        public async Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await stopping.CancelAsync();

            return Release.Done(resource, "released, then asked to stop");
        }
    }
}
