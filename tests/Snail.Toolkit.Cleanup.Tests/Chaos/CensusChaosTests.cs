using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Application.Ports;
using Snail.Toolkit.Cleanup.Domain;
using static Snail.Toolkit.Cleanup.Tests.Support.Things;

namespace Snail.Toolkit.Cleanup.Tests.Chaos;

/// <summary>Crash tests of the census: holders with odd shapes, a rules file that cannot be read, kinds that collide or dawdle.</summary>
public sealed class CensusChaosTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly FakeTrail _files = new("file");

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UnixEpoch);

    private readonly MemoryLedger _ledger = new();

    private readonly MemoryBaselines _baselines = new();

    private readonly MemoryDiscoveries _discoveries = new();

    private Census CensusOf(IReadOnlyList<Holder> sessions, params IResourceKind[] kinds) => new(
        kinds,
        [_files],
        new FakeHolders(Me, [.. sessions]),
        _ledger,
        new JsonRules(_scratch.File("rules.json"), []),
        _baselines,
        _discoveries,
        _clock);

    /// <summary>
    /// Breaks: whose a discovery is. A holder whose directory is the empty string, as a host that stores «none» as «»
    /// gives, is taken to work in «/», and everything that appears anywhere on the machine is its.
    /// </summary>
    [Fact]
    public async Task Discovery_WithAHolderWhoseDirectoryIsEmpty_IsNobodys()
    {
        Holder[] sessions = [new(Me, IsAlive: true) { Directory = "" }, new(Neighbour, IsAlive: true) { Directory = "/work/neighbour" }];
        var census = CensusOf(sessions, new FakeKind(Kinds.File));

        await census.TakeAsync(Me, CancellationToken.None);
        _files.Add("/Users/dev/.cache/somewhere");

        var discovery = Assert.Single((await census.TakeAsync(Me, CancellationToken.None)).Discoveries);

        Assert.Equal(Owner.Nobody, discovery.Owner);
    }

    /// <summary>
    /// Breaks: a census when the rules file cannot be read. The kinds have looked and the resources are known; a rules
    /// file nobody may read should cost the discoveries and a note, not the whole census.
    /// </summary>
    [Fact]
    public async Task Census_WhenTheRulesFileCannotBeRead_StillAnswersWithTheResources()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
            return;

        var known = new FakeKind(Kinds.File);
        var rules = _scratch.File("rules.json");

        known.Present.Add(Of(Kinds.File, "/scratch", Me));
        await File.WriteAllTextAsync(rules, """{"rules":[]}""");
        File.SetUnixFileMode(rules, UnixFileMode.None);

        try
        {
            var inventory = await CensusOf([Alive(Me)], known).TakeAsync(Me, CancellationToken.None);

            Assert.Single(inventory.Resources);
        }
        finally
        {
            File.SetUnixFileMode(rules, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// Breaks: two kinds of one name that both could not look. Each note is keyed by the kind's name, and the second
    /// key fails the whole census with an ArgumentException at the worst moment, when the daemons are down.
    /// </summary>
    [Fact]
    public async Task Census_WithTwoKindsOfOneNameBothBlind_DoesNotFail()
    {
        var docker = new FakeKind(Kinds.Container) { Blindness = "docker is not running" };
        var podman = new FakeKind(Kinds.Container) { Blindness = "podman is not running" };

        var inventory = await CensusOf([Alive(Me)], docker, podman).TakeAsync(Me, CancellationToken.None);

        Assert.Contains(Kinds.Container, inventory.Notes.Keys);
    }

    /// <summary>
    /// Breaks: the parallel look. The census asks every kind at once with Task.WhenAll, but a kind that looks
    /// synchronously, as NamedEntries does, answers a finished task: four kinds of 100 ms each take 400 ms, not 100.
    /// </summary>
    [Fact]
    public async Task Census_OverFourKindsThatLookSynchronously_LooksAtThemAtOnce()
    {
        IResourceKind[] kinds = [.. Enumerable.Range(0, 4).Select(index => new SlowKind($"slow-{index}", TimeSpan.FromMilliseconds(100)))];
        var census = CensusOf([Alive(Me)], kinds);
        var started = Stopwatch.GetTimestamp();

        await census.TakeAsync(Me, CancellationToken.None);

        var took = Stopwatch.GetElapsedTime(started);

        Assert.True(took < TimeSpan.FromMilliseconds(300), $"four looks of 100 ms took {took.TotalMilliseconds:f0} ms");
    }

    /// <summary>Breaks: a rules file named without a folder; the directory of «rules.json» is the empty string, which cannot be created.</summary>
    [Fact]
    public async Task JsonRules_AtAPathWithoutAFolder_IsWrittenBesideTheProgram()
    {
        var path = $"s3l-chaos-{Guid.NewGuid():n}.json";

        try
        {
            await new JsonRules(path, []).AddAsync(new Rule("file", "/tmp/x", RuleAction.Ignore), CancellationToken.None);

            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    public void Dispose() => _scratch.Dispose();

    /// <summary>A kind that looks the way a kind walking a disk does: on the thread that asked, for as long as it takes.</summary>
    private sealed class SlowKind(string name, TimeSpan takes) : IResourceKind
    {
        public string Name => name;

        public Task<Observation> ObserveAsync(IReadOnlyList<Holder> holders, CancellationToken cancellationToken)
        {
            Thread.Sleep(takes);

            return Task.FromResult(new Observation(Name, [], []));
        }

        public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) => Task.FromResult(Release.AlreadyGone(resource));
    }
}
