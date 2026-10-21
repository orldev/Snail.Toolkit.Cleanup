using Microsoft.Extensions.Time.Testing;
using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Tests;

/// <summary>Entries whose names carry their owner, as a platform worker keeps them: the folders of tasks and the keys of projects.</summary>
public sealed class NamedEntriesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Grace = TimeSpan.FromHours(1);

    private readonly Scratch _scratch = new();

    private readonly Scratch _elsewhere = new();

    private readonly FakeTimeProvider _clock = new(Now);

    private NamedEntries Tasks => new(TaskFolders, _clock);

    private NamedEntries Keys => new(ProjectKeys, _clock);

    /// <summary>The working folders of tasks, each named for its task: what a worker's own task folders were.</summary>
    private NamedEntriesOptions TaskFolders => new()
    {
        Kind = "workspace",
        Root = _scratch.Path,
        OwnerKind = "task",
        OwnerIn = GuidIn,
        Grace = Grace,
        IsRegenerable = true
    };

    /// <summary>The keys of projects in a folder of their own, a key and the half-written copies beside it named for its project.</summary>
    private NamedEntriesOptions ProjectKeys => new()
    {
        Kind = "project-key",
        Root = _scratch.File(".credentials"),
        OwnerKind = "project",
        OwnerIn = KeyOwnerIn,
        Entries = EntryType.Files,
        Pattern = "*.key*",
        Grace = Grace
    };

    [Fact]
    public async Task FolderOfAnOwnerStillGoing_IsKept()
    {
        var task = Guid.NewGuid();
        var folder = Folder(task, TimeSpan.FromHours(5));

        var releases = await SweptAsync(Tasks, new Holder(Owner("task", task), IsAlive: true));

        Assert.Empty(releases);
        Assert.True(Directory.Exists(folder));
    }

    /// <summary>A person may take a task up again a minute after it ended; within the grace its folder stays.</summary>
    [Fact]
    public async Task FolderOfAnOwnerEndedWithinTheGrace_IsKept()
    {
        var task = Guid.NewGuid();
        var folder = Folder(task, TimeSpan.FromHours(5));

        var releases = await SweptAsync(Tasks, Ended(Owner("task", task), TimeSpan.FromMinutes(10)));

        Assert.Empty(releases);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task FolderOfAnOwnerEndedPastTheGrace_IsDeleted()
    {
        var task = Guid.NewGuid();
        var folder = Folder(task, TimeSpan.FromHours(5));

        var release = Assert.Single(await SweptAsync(Tasks, Ended(Owner("task", task), TimeSpan.FromHours(2))));

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.False(Directory.Exists(folder));
    }

    /// <summary>An owner erased from the host's records has no holder at all: what its name marks belongs to nothing any more.</summary>
    [Fact]
    public async Task FolderOfAnErasedOwner_IsDeleted()
    {
        var folder = Folder(Guid.NewGuid(), TimeSpan.FromHours(5));

        await SweptAsync(Tasks);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task FolderOfAnErasedOwner_WrittenWithinTheGrace_IsKept()
    {
        var folder = Folder(Guid.NewGuid(), TimeSpan.FromMinutes(5));

        await SweptAsync(Tasks);

        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task EntryWhoseNameCarriesNoOwner_IsNeverTouched()
    {
        var foreign = _scratch.File("not-a-task");

        Directory.CreateDirectory(foreign);
        Directory.SetLastWriteTimeUtc(foreign, Now.AddDays(-9).UtcDateTime);

        var observed = await Tasks.ObserveAsync([], CancellationToken.None);

        Assert.Empty(observed.Resources);
        Assert.True(Directory.Exists(foreign));
    }

    /// <summary>A step that started writing into the folder after the plan was made means the plan describes another folder.</summary>
    [Fact]
    public async Task FolderWrittenBetweenPlanAndRelease_IsLeft_AsNoLongerTheSame()
    {
        var folder = Folder(Guid.NewGuid(), TimeSpan.FromHours(5));
        var plan = await PlannedAsync(Tasks);

        await File.WriteAllTextAsync(Path.Combine(folder, "new-step.log"), "written");

        var release = Assert.Single(await new Cleaner([Tasks], new NoLedger()).ApplyAsync(plan, CancellationToken.None));

        Assert.Equal(Outcome.Changed, release.Outcome);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task EntryReplacedByOneOfTheOtherType_IsLeft_AsNoLongerTheSame()
    {
        var folder = Folder(Guid.NewGuid(), TimeSpan.FromHours(5));
        var plan = await PlannedAsync(Tasks);

        Directory.Delete(folder, recursive: true);
        await File.WriteAllTextAsync(folder, "a file where the folder was");

        var release = Assert.Single(await new Cleaner([Tasks], new NoLedger()).ApplyAsync(plan, CancellationToken.None));

        Assert.Equal(Outcome.Changed, release.Outcome);
        Assert.True(File.Exists(folder));
    }

    [Fact]
    public async Task EntryGoneBetweenPlanAndRelease_WasAlreadyReleased()
    {
        var folder = Folder(Guid.NewGuid(), TimeSpan.FromHours(5));
        var plan = await PlannedAsync(Tasks);

        Directory.Delete(folder, recursive: true);

        Assert.Equal(Outcome.Gone, Assert.Single(await new Cleaner([Tasks], new NoLedger()).ApplyAsync(plan, CancellationToken.None)).Outcome);
    }

    /// <summary>A link named for an owner points somewhere the kind was never given; it is named, and nothing it points at is touched.</summary>
    [Fact]
    public async Task LinkAmongTheEntries_PointingOutsideTheRoot_IsReportedAndNeverFollowed()
    {
        var outside = Directory.CreateDirectory(_elsewhere.File("precious"));
        var kept = Path.Combine(outside.FullName, "data.txt");
        var link = _scratch.File(Guid.NewGuid().ToString("n"));

        await File.WriteAllTextAsync(kept, "not the kind's");
        Directory.CreateSymbolicLink(link, outside.FullName);

        var plan = await PlannedAsync(Tasks, Scope.Everything);
        var decision = Assert.Single(plan.Decisions);

        Assert.Equal(Verdict.Report, decision.Verdict);
        Assert.Null(decision.Resource.Bytes);
        Assert.Empty(await new Cleaner([Tasks], new NoLedger()).ApplyAsync(plan, CancellationToken.None));
        Assert.True(File.Exists(kept));
    }

    /// <summary>A link inside a folder is neither counted nor followed when the folder goes.</summary>
    [Fact]
    public async Task LinkInsideAFolder_PointingOutsideTheRoot_IsNotCounted_AndOutlivesTheFolder()
    {
        var outside = Directory.CreateDirectory(_elsewhere.File("precious"));
        var kept = Path.Combine(outside.FullName, "data.txt");

        await File.WriteAllBytesAsync(kept, new byte[4_096]);

        var folder = Folder(Guid.NewGuid(), TimeSpan.FromHours(5), inside: root => Directory.CreateSymbolicLink(Path.Combine(root, "shortcut"), outside.FullName));
        var observed = Assert.Single((await Tasks.ObserveAsync([], CancellationToken.None)).Resources);

        await SweptAsync(Tasks);

        Assert.Equal(10, observed.Bytes);
        Assert.False(observed.IsAtLeast);
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(kept));
    }

    [Fact]
    public async Task Folder_IsMeasuredAsTheLengthsOfItsFiles_AndProvenByItsName()
    {
        var task = Guid.NewGuid();

        Folder(task, TimeSpan.FromHours(5));

        var resource = Assert.Single((await Tasks.ObserveAsync([], CancellationToken.None)).Resources);

        Assert.Equal(10, resource.Bytes);
        Assert.Equal(Owner("task", task), resource.Owner);
        Assert.Equal(Evidence.Label, resource.Evidence);
        Assert.True(resource.IsRegenerable);
    }

    /// <summary>A running task's build output is the largest thing under the root and is kept whatever its size: it is never walked.</summary>
    [Fact]
    public async Task FolderOfAnOwnerStillGoing_IsNotWalked_AndIsKeptWithItsSizeUnknown()
    {
        var task = Guid.NewGuid();
        var holder = new Holder(Owner("task", task), IsAlive: true);

        Folder(task, TimeSpan.FromHours(5));

        var decision = Assert.Single((await PlannedAsync(Tasks, Scope.Everything, holder)).Decisions);

        Assert.Equal(Verdict.Keep, decision.Verdict);
        Assert.Null(decision.Resource.Bytes);
        Assert.False(decision.Resource.IsAtLeast);
        Assert.Null(decision.Resource.Blocker);
    }

    [Fact]
    public async Task FolderOfAnOwnerOver_EndedOrErased_IsMeasuredBeforeItGoes()
    {
        var ended = Guid.NewGuid();

        Folder(ended, TimeSpan.FromHours(5));
        Folder(Guid.NewGuid(), TimeSpan.FromHours(5));

        var plan = await PlannedAsync(Tasks, Scope.Orphans, Ended(Owner("task", ended), TimeSpan.FromHours(2)));

        Assert.Equal(2, plan.Removals.Count);
        Assert.All(plan.Removals, removal => Assert.Equal(10, removal.Resource.Bytes));
    }

    /// <summary>The asking owner's own folder may go while it is still going; it was not walked when observed, so it is measured as it goes.</summary>
    [Fact]
    public async Task FolderOfTheAskingOwner_NotMeasuredWhenObserved_IsMeasuredAsItGoes()
    {
        var task = Owner("task", Guid.NewGuid());
        Holder[] holders = [new(task, IsAlive: true)];

        Folder(Guid.Parse(task.Id), TimeSpan.FromHours(5));

        var observed = await Tasks.ObserveAsync(holders, CancellationToken.None);
        var inventory = new Inventory(Now, observed.Resources, holders) { Asker = task };
        var plan = Planner.Decide(inventory, Scope.Mine, "plan", KindOrder.General.Before(Kinds.File, "workspace"));

        var release = Assert.Single(await new Cleaner([Tasks], new NoLedger()).ApplyAsync(plan, CancellationToken.None));

        Assert.Null(Assert.Single(plan.Removals).Resource.Bytes);
        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.Equal(10, release.Bytes);
        Assert.False(release.IsAtLeast);
    }

    /// <summary>A folder whose count stopped short — here at a directory it may not read — says so, and so does its release.</summary>
    [Fact]
    public async Task FolderOfAnOwnerOver_CountedShort_IsReleasedAsAtLeastWhatWasCounted()
    {
        if (OperatingSystem.IsWindows())
            return;

        var task = Guid.NewGuid();
        var locked = "";

        Folder(task, TimeSpan.FromHours(5), inside: root =>
        {
            locked = Directory.CreateDirectory(Path.Combine(root, "locked")).FullName;
            File.WriteAllText(Path.Combine(locked, "secret.txt"), "unreadable");
            Aged(locked, Now - TimeSpan.FromHours(5));
            File.SetUnixFileMode(locked, UnixFileMode.None);
        });

        try
        {
            var plan = await PlannedAsync(Tasks, Scope.Orphans, Ended(Owner("task", task), TimeSpan.FromHours(2)));
            var measured = Assert.Single(plan.Removals).Resource;

            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var release = Assert.Single(await new Cleaner([Tasks], new NoLedger()).ApplyAsync(plan, CancellationToken.None));

            Assert.True(measured.IsAtLeast);
            Assert.Equal(Outcome.Released, release.Outcome);
            Assert.Equal(measured.Bytes, release.Bytes);
            Assert.True(release.IsAtLeast);
        }
        finally
        {
            if (Directory.Exists(locked))
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Release_WithoutWordsOfTheHost_SaysWhereTheEntryWasDeletedFrom()
    {
        Folder(Guid.NewGuid(), TimeSpan.FromHours(5));

        var release = Assert.Single(await SweptAsync(Tasks));

        Assert.Equal($"deleted from {Path.GetFullPath(_scratch.Path)}", release.Detail);
    }

    /// <summary>A host that knows why its entries may go says so in its own words, as a worker does of a task's folder.</summary>
    [Fact]
    public async Task Release_WithWordsOfTheHost_SaysThem_OfTheEntryAsReleased()
    {
        var folder = Folder(Guid.NewGuid(), TimeSpan.FromHours(5));
        var worded = new NamedEntries(TaskFolders with { ReleaseDetail = entry => $"erased: the task's branch carries its work ({entry.Bytes} bytes)" }, _clock);

        var release = Assert.Single(await SweptAsync(worded));

        Assert.Equal(Outcome.Released, release.Outcome);
        Assert.Equal("erased: the task's branch carries its work (10 bytes)", release.Detail);
        Assert.False(Directory.Exists(folder));
    }

    /// <summary>Two instances over one root: the folders of tasks are directories, the keys of projects are files matched by a pattern.</summary>
    [Fact]
    public async Task FilesAndDirectories_EachKindSeesOnlyItsOwn()
    {
        var task = Guid.NewGuid();
        var retired = Guid.NewGuid();
        var active = Guid.NewGuid();
        var folder = Folder(task, TimeSpan.FromHours(5));
        var retiredKey = Key($"{retired:n}.key", TimeSpan.FromDays(3));
        var halfWritten = Key($"{retired:n}.key.tmp42", TimeSpan.FromDays(3));
        var activeKey = Key($"{active:n}.key", TimeSpan.FromDays(3));
        var notes = Key($"{retired:n}.txt", TimeSpan.FromDays(3));

        var tasks = (await Tasks.ObserveAsync([], CancellationToken.None)).Resources;
        var keys = (await Keys.ObserveAsync([], CancellationToken.None)).Resources;

        await SweptAsync(Keys, new Holder(Owner("project", active), IsAlive: true));

        Assert.Equal([folder], tasks.Select(resource => resource.Key));
        Assert.Equal([retiredKey, halfWritten], keys.Where(resource => resource.Owner == Owner("project", retired)).Select(resource => resource.Key).Order(StringComparer.Ordinal));
        Assert.Equal(3, Assert.Single(keys, resource => resource.Key == retiredKey).Bytes);
        Assert.False(File.Exists(retiredKey));
        Assert.False(File.Exists(halfWritten));
        Assert.True(File.Exists(activeKey));
        Assert.True(File.Exists(notes));
        Assert.True(Directory.Exists(folder));
    }

    /// <summary>A key is its project's up to the first dot, and only when <c>.key</c> follows it: a renamed old copy matches the pattern and is still nobody's.</summary>
    [Fact]
    public async Task KeyName_WithAnythingButKeyAfterTheId_CarriesNoOwner()
    {
        var retired = Guid.NewGuid();
        var key = Key($"{retired:n}.key", TimeSpan.FromDays(3));
        var halfWritten = Key($"{retired:n}.key.{Guid.NewGuid():n}", TimeSpan.FromDays(3));
        var oldCopy = Key($"{retired:n}.old.key", TimeSpan.FromDays(3));
        var deploy = Key("deploy.key", TimeSpan.FromDays(3));
        var publicHalf = Key($"{retired:n}.pub", TimeSpan.FromDays(3));
        var bare = Key($"{retired:n}", TimeSpan.FromDays(3));

        var keys = (await Keys.ObserveAsync([], CancellationToken.None)).Resources;

        await SweptAsync(Keys);

        Assert.Equal([key, halfWritten], keys.Select(resource => resource.Key).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(key));
        Assert.False(File.Exists(halfWritten));
        Assert.True(File.Exists(oldCopy));
        Assert.True(File.Exists(deploy));
        Assert.True(File.Exists(publicHalf));
        Assert.True(File.Exists(bare));
    }

    [Fact]
    public async Task RootThatWasNeverMade_HoldsNothing() =>
        Assert.Empty((await new NamedEntries(TaskFolders with { Root = _scratch.File("never") }, _clock).ObserveAsync([], CancellationToken.None)).Resources);

    [Fact]
    public async Task ResourceNotDirectlyUnderTheRoot_IsRefused()
    {
        var elsewhere = Directory.CreateDirectory(_elsewhere.File(Guid.NewGuid().ToString("n")));
        var forged = new Resource { Kind = "workspace", Key = elsewhere.FullName, Title = "forged", Owner = Owner("task", Guid.NewGuid()), Evidence = Evidence.Label };

        var release = await Tasks.ReleaseAsync(forged, CancellationToken.None);

        Assert.Equal(Outcome.Failed, release.Outcome);
        Assert.True(elsewhere.Exists);
    }

    public void Dispose()
    {
        _scratch.Dispose();
        _elsewhere.Dispose();
    }

    private static string? GuidIn(string name) => Guid.TryParseExact(name, "N", out var id) ? id.ToString("n") : null;

    /// <summary>The project a key is named for: its id up to the first dot, and <c>.key</c> right after it.</summary>
    private static string? KeyOwnerIn(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);

        return dot > 0 && name.AsSpan(dot).StartsWith(".key", StringComparison.Ordinal) ? GuidIn(name[..dot]) : null;
    }

    private static Owner Owner(string kind, Guid id) => new(kind, id.ToString("n"));

    private static Holder Ended(Owner owner, TimeSpan ago) => new(owner, IsAlive: false) { EndedAt = Now - ago };

    private async Task<Plan> PlannedAsync(IResourceKind kind, Scope scope = Scope.Orphans, params Holder[] holders)
    {
        var observed = await kind.ObserveAsync(holders, CancellationToken.None);
        var inventory = new Inventory(_clock.GetUtcNow(), observed.Resources, holders);

        return Planner.Decide(inventory, scope, "plan", KindOrder.General.Before(Kinds.File, "workspace", "project-key"));
    }

    private async Task<IReadOnlyList<Release>> SweptAsync(IResourceKind kind, params Holder[] holders) =>
        await new Cleaner([kind], new NoLedger()).ApplyAsync(await PlannedAsync(kind, Scope.Orphans, holders), CancellationToken.None);

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

    private string Key(string name, TimeSpan age)
    {
        var file = Path.Combine(_scratch.File(".credentials"), name);

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "key");
        File.SetLastWriteTimeUtc(file, (Now - age).UtcDateTime);

        return file;
    }
}
