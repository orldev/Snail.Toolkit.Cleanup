using Microsoft.Extensions.Time.Testing;
using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Tests.Chaos;

/// <summary>
/// Crash tests of the one kind the library ships. Each test says what it breaks and why; a test that fails is the proof
/// of its bug, and one that passes withdraws the finding.
/// </summary>
public sealed class NamedEntriesChaosTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Grace = TimeSpan.FromHours(1);

    private readonly Scratch _scratch = new();

    private readonly FakeTimeProvider _clock = new(Now);

    private NamedEntries Tasks => new(TaskFolders, _clock);

    private NamedEntriesOptions TaskFolders => new()
    {
        Kind = "workspace",
        Root = _scratch.Path,
        OwnerKind = "task",
        OwnerIn = GuidIn,
        Grace = Grace,
        IsRegenerable = true
    };

    /// <summary>
    /// Breaks: the check again before release. The kind compares the folder's own write time, which a file written one
    /// level down does not change: a step that started writing into <c>step-0/</c> after the plan was shown is not
    /// seen, and the folder goes with the step's output in it.
    /// </summary>
    [Fact]
    public async Task Folder_WrittenOneLevelDownBetweenPlanAndRelease_IsLeftAsNoLongerTheSame()
    {
        var folder = Folder(Guid.NewGuid(), TimeSpan.FromHours(5));
        var plan = await PlannedAsync(Tasks);

        await File.WriteAllTextAsync(Path.Combine(folder, "step-0", "late.log"), "written after the plan was shown");

        var release = Assert.Single(await new Cleaner([Tasks], new NoLedger()).ApplyAsync(plan, CancellationToken.None));

        Assert.Equal(Outcome.Changed, release.Outcome);
        Assert.True(Directory.Exists(folder));
    }

    /// <summary>
    /// Breaks: the grace after the last write. A lingering build of an erased task writes into <c>step-0/</c> every
    /// minute while the folder's own write time stays five hours old, so the grace never applies and the folder goes
    /// from under the build.
    /// </summary>
    [Fact]
    public async Task Folder_OfAnErasedOwner_BeingWrittenOneLevelDown_IsKeptWithinTheGrace()
    {
        var folder = Folder(Guid.NewGuid(), TimeSpan.FromHours(5), inside: root =>
        {
            var busy = Path.Combine(root, "step-0", "busy.log");

            File.WriteAllText(busy, "still being written");
            File.SetLastWriteTimeUtc(busy, (Now - TimeSpan.FromMinutes(5)).UtcDateTime);
            Directory.SetLastWriteTimeUtc(Path.Combine(root, "step-0"), (Now - TimeSpan.FromMinutes(5)).UtcDateTime);
        });

        var decision = Assert.Single((await PlannedAsync(Tasks, Scope.Everything)).Decisions);

        Assert.Equal(Verdict.Report, decision.Verdict);
        Assert.True(Directory.Exists(folder));
    }

    /// <summary>
    /// Breaks: two clients applying one plan at once. Every cleaner passes the check again, every one deletes, and the
    /// ones that lose the race fail on a folder that is gone and report a failure for a release that happened.
    /// </summary>
    [Fact]
    public async Task Plan_AppliedByEightCleanersAtOnce_ReportsTheFolderGoneAndNeverFailed()
    {
        Folder(Guid.NewGuid(), TimeSpan.FromHours(5));

        var plan = await PlannedAsync(Tasks);
        var cleaner = new Cleaner([Tasks], new NoLedger());
        var runs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => cleaner.ApplyAsync(plan, CancellationToken.None))));
        var outcomes = runs.Select(releases => Assert.Single(releases).Outcome).ToList();

        Assert.DoesNotContain(Outcome.Failed, outcomes);
        Assert.Contains(Outcome.Released, outcomes);
    }

    /// <summary>Breaks: a root whose entries go while the kind looks; a folder deleted in the middle of a walk must not end the look.</summary>
    [Fact]
    public async Task Root_WhoseEntriesAreDeletedWhileItIsObserved_IsStillObservedWithoutFailing()
    {
        var folders = Enumerable.Range(0, 200).Select(_ => Folder(Guid.NewGuid(), TimeSpan.FromHours(5))).ToList();
        var deleter = Task.Run(() =>
        {
            foreach (var folder in folders)
            {
                Directory.Delete(folder, recursive: true);
                Thread.Sleep(1);
            }
        });

        var failure = await Record.ExceptionAsync(async () =>
        {
            while (!deleter.IsCompleted)
                await Tasks.ObserveAsync([], CancellationToken.None);
        });

        await deleter;

        Assert.Null(failure);
    }

    /// <summary>Breaks: the arithmetic of the graces at the ends of time; the longest grace and the earliest end must not overflow.</summary>
    [Fact]
    public async Task Folder_OfAnOwnerEndedAtTheDawnOfTime_WithTheLongestGrace_IsReportedNotCrashed()
    {
        var task = Guid.NewGuid();
        var patient = new NamedEntries(TaskFolders with { Grace = TimeSpan.MaxValue }, _clock);

        Folder(task, TimeSpan.FromHours(5));

        var ended = new Holder(Owner("task", task), IsAlive: false) { EndedAt = DateTimeOffset.MinValue };
        var plan = await PlannedAsync(patient, Scope.Everything, ended);

        Assert.Equal(Verdict.Report, Assert.Single(plan.Decisions).Verdict);
    }

    /// <summary>
    /// Breaks: a write time from the future, as a restored backup or a skewed clock leaves it. With no grace at all,
    /// nothing can be «written within the grace»; whatever blocks such a folder must say what it is.
    /// </summary>
    [Fact]
    public async Task Folder_WrittenInTheFuture_WithNoGrace_IsNotSaidToBeWithinTheGrace()
    {
        var hasty = new NamedEntries(TaskFolders with { Grace = TimeSpan.Zero }, _clock);

        Folder(Guid.NewGuid(), TimeSpan.FromHours(-48));

        var decision = Assert.Single((await PlannedAsync(hasty, Scope.Everything)).Decisions);

        Assert.DoesNotContain("within the grace", decision.Reason, StringComparison.Ordinal);
    }

    public void Dispose() => _scratch.Dispose();

    private static string? GuidIn(string name) => Guid.TryParseExact(name, "N", out var id) ? id.ToString("n") : null;

    private static Owner Owner(string kind, Guid id) => new(kind, id.ToString("n"));

    private async Task<Plan> PlannedAsync(IResourceKind kind, Scope scope = Scope.Orphans, params Holder[] holders)
    {
        var observed = await kind.ObserveAsync(holders, CancellationToken.None);
        var inventory = new Inventory(_clock.GetUtcNow(), observed.Resources, holders);

        return Planner.Decide(inventory, scope, "plan", KindOrder.General.Before(Kinds.File, "workspace"));
    }

    private string Folder(Guid task, TimeSpan age, Action<string>? inside = null)
    {
        var folder = _scratch.File(task.ToString("n"));

        Directory.CreateDirectory(Path.Combine(folder, "step-0"));
        File.WriteAllText(Path.Combine(folder, "step-0", "out.txt"), "0123456789");
        Aged(folder, Now - age);
        inside?.Invoke(folder);
        Directory.SetLastWriteTimeUtc(folder, (Now - age).UtcDateTime);

        return folder;
    }

    /// <summary>The folder and everything in it written at one moment, as a task that stopped then leaves them.</summary>
    private static void Aged(string folder, DateTimeOffset at)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(entry, at.UtcDateTime);

        Directory.SetLastWriteTimeUtc(folder, at.UtcDateTime);
    }
}
