using Microsoft.Extensions.Time.Testing;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Application;
using Snail.MCP.CleanUp.Core.Domain;
using static Snail.MCP.CleanUp.Tests.Support.Things;

namespace Snail.MCP.CleanUp.Tests.Core;

/// <summary>
/// The account never assumes it knows every kind of leftover there is. What appears during a session and
/// nothing accounts for is written down — and one rule later, it is a kind the account knows.
/// </summary>
public sealed class DiscoveryTests : IDisposable
{
    private readonly Scratch _scratch = new();

    private readonly LedgerFile _file;

    private readonly FakeTrail _files = new("file");

    private readonly FakeKind _known = new(Kinds.File);

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UnixEpoch);

    private Session[] _sessions = [new(Me.Id, 1, DateTimeOffset.UnixEpoch, "/work/me", IsAlive: true)];

    public DiscoveryTests() => _file = new LedgerFile(_scratch.File("ledger.db"));

    private JsonRules Rules => new(_scratch.File("rules.json"), [new Rule("file", "~/.claude/**", RuleAction.Ignore)]);

    private Census Census => CensusOf(_known);

    private Census CensusOf(Snail.MCP.CleanUp.Core.Application.Ports.IResourceKind kind) => new(
        [kind],
        [_files],
        new FakeSessions(Me, _sessions),
        new SqliteLedger(_file),
        Rules,
        new SqliteBaselines(_file),
        new SqliteDiscoveries(_file),
        _clock);

    private Task<Inventory> TakenAsync() => Census.TakeAsync(Me, CancellationToken.None);

    [Fact]
    public async Task WhatWasThereWhenTheSessionBegan_IsNeverADiscovery()
    {
        _files.Add("/Users/dev/Library/Old");

        await TakenAsync();

        Assert.Empty((await TakenAsync()).Discoveries);
    }

    [Fact]
    public async Task WhatAppearedDuringTheSession_AndNothingKnows_IsWrittenDownWithItsSize()
    {
        await TakenAsync();
        _files.Add("/Users/dev/Library/Application Support/NewTool");

        var discovery = Assert.Single((await TakenAsync()).Discoveries);

        Assert.Equal("file:/Users/dev/Library/Application Support/NewTool", discovery.Trace.Id);
        Assert.Equal(Me, discovery.Owner);
        Assert.Equal(42, discovery.Trace.Bytes);
        Assert.True(discovery.IsPresent);
    }

    /// <summary>A census asks a trace for its id several times; the id is made once, and a trace made alike is still equal.</summary>
    [Fact]
    public void Trace_Id_IsMadeOnce_AndStaysTheSame_ThroughWith()
    {
        var trace = new Trace("file", "/tmp/thing", "/tmp/thing");

        var unasked = new Trace("file", "/tmp/thing", "/tmp/thing");

        Assert.Same(trace.Id, trace.Id);
        Assert.Equal("file:/tmp/thing", (trace with { Bytes = 42, Title = "thing" }).Id);
        Assert.Equal(unasked, trace);
        Assert.Equal(unasked.GetHashCode(), trace.GetHashCode());
        Assert.NotEqual(unasked, trace with { Bytes = 42 });
    }

    [Fact]
    public async Task Discovery_SeenAgain_IsCountedNotDuplicated()
    {
        await TakenAsync();
        _files.Add("/tmp/thing");

        await TakenAsync();

        Assert.Equal(2, Assert.Single((await TakenAsync()).Discoveries).Sightings);
    }

    /// <summary>«Agents leave this» stays true for a while after the thing is gone, so the record stays too.</summary>
    [Fact]
    public async Task Discovery_WhoseThingIsGone_StaysWrittenDown_MarkedAbsent()
    {
        await TakenAsync();
        var trace = _files.Add("/tmp/thing");

        await TakenAsync();
        _files.Present.Remove(trace);

        Assert.False(Assert.Single((await TakenAsync()).Discoveries).IsPresent);
    }

    /// <summary>A record nobody prunes grows by every temporary file an agent ever made; one gone long enough is crossed off.</summary>
    [Fact]
    public async Task Discovery_GoneLongerThanItIsRemembered_IsCrossedOff_FromTheFile()
    {
        await TakenAsync();
        var trace = _files.Add("/tmp/thing");

        await TakenAsync();
        _files.Present.Remove(trace);

        _clock.Advance(Discovery.Remembered - TimeSpan.FromMinutes(1));
        Assert.False(Assert.Single((await TakenAsync()).Discoveries).IsPresent);

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Empty((await TakenAsync()).Discoveries);
        Assert.Equal(0, await RowsAsync());
    }

    /// <summary>
    /// A thing still there is seen again by every census — even when no running session finds it new any more, because
    /// each began after it — and it is never crossed off, not even after a census that came days late.
    /// </summary>
    [Fact]
    public async Task Discovery_StillThere_IsSeenAgainAtEveryCensus_AndNeverCrossedOff()
    {
        await TakenAsync();
        _files.Add("/tmp/thing");
        await TakenAsync();

        _sessions = [new(Neighbour.Id, 2, DateTimeOffset.UnixEpoch, "/work/neighbour", IsAlive: true)];
        await TakenAsync();

        _clock.Advance(Discovery.Remembered + TimeSpan.FromDays(1));
        var discovery = Assert.Single((await TakenAsync()).Discoveries);
        var written = Assert.Single(await new SqliteDiscoveries(_file).ListAsync(new HashSet<string>(), _clock.GetUtcNow(), CancellationToken.None));

        Assert.True(discovery.IsPresent);
        Assert.Equal(_clock.GetUtcNow(), discovery.LastSeen);
        Assert.Equal(_clock.GetUtcNow(), written.LastSeen);
        Assert.Equal(1, discovery.Sightings);
    }

    /// <summary>The census returns the record without reading it back, and what it returns is what the file holds.</summary>
    [Fact]
    public async Task Discoveries_OfACensus_AreWhatTheLedgerHolds_AfterIt()
    {
        await TakenAsync();
        _files.Add("/tmp/first");
        await TakenAsync();

        _clock.Advance(TimeSpan.FromMinutes(1));
        _files.Add("/tmp/second");

        var taken = (await TakenAsync()).Discoveries;
        var present = _files.Present.Select(trace => trace.Id).ToHashSet(StringComparer.Ordinal);
        var read = await new SqliteDiscoveries(_file).ListAsync(present, _clock.GetUtcNow(), CancellationToken.None);

        Assert.Equal(["file:/tmp/second", "file:/tmp/first"], taken.Select(discovery => discovery.Trace.Id));
        Assert.Equal(read, taken);
        Assert.Equal(2, taken[1].Sightings);
        Assert.Equal(42, taken[1].Trace.Bytes);
    }

    [Fact]
    public async Task WhatAKindClaims_IsNotADiscovery()
    {
        await TakenAsync();
        var trace = _files.Add("/tmp/scratchpad");
        var claiming = new FakeKind(Kinds.File);

        var census = new Census(
            [new ClaimingKind(trace.Id)],
            [_files],
            new FakeSessions(Me, _sessions),
            new SqliteLedger(_file),
            Rules,
            new SqliteBaselines(_file),
            new SqliteDiscoveries(_file),
            _clock);

        Assert.Empty((await census.TakeAsync(Me, CancellationToken.None)).Discoveries);
        Assert.Empty(claiming.Present);
    }

    /// <summary>A trail a kind already followed to look is read from its observation: one walk of the files per census, not two.</summary>
    [Fact]
    public async Task Trail_AKindAlreadyFollowed_IsNotWalkedAgain_AndStillShowsWhatIsNew()
    {
        var census = CensusOf(new WalkingKind(_files));

        await census.TakeAsync(Me, CancellationToken.None);
        _files.Add("/tmp/thing");

        var discovery = Assert.Single((await census.TakeAsync(Me, CancellationToken.None)).Discoveries);

        Assert.Equal("file:/tmp/thing", discovery.Trace.Id);
        Assert.Equal(0, _files.Follows);
    }

    [Fact]
    public async Task Trail_NoKindFollowed_IsWalkedByTheCensus_OncePerCensus()
    {
        _known.Blindness = "could not look";

        await TakenAsync();

        Assert.Equal(1, _files.Follows);
    }

    [Fact]
    public async Task WhatAShippedRuleIgnores_IsNotADiscovery()
    {
        await TakenAsync();
        _files.Add($"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.claude/projects/x.jsonl");

        Assert.Empty((await TakenAsync()).Discoveries);
    }

    /// <summary>The whole idea: a discovery becomes a known kind by one line of data, and its record is settled.</summary>
    [Fact]
    public async Task Rule_LearnedFromADiscovery_CoversItFromThenOn()
    {
        await TakenAsync();
        _files.Add("/Users/dev/Library/Application Support/Code/WebStorage/11");
        await TakenAsync();

        var rule = new Rule("file", "/Users/dev/Library/Application Support/Code/WebStorage/*", RuleAction.Cache) { BlockedWhileRunning = "Code" };

        await Rules.AddAsync(rule, CancellationToken.None);
        var settled = await new SqliteDiscoveries(_file).SettleAsync(rule, CancellationToken.None);

        Assert.Equal(1, settled);
        Assert.Empty((await TakenAsync()).Discoveries);
        Assert.Contains(rule, await Rules.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Discovery_InsideASessionsDirectory_IsThatSessions_EvenWithOthersRunning()
    {
        _sessions = [_sessions[0], new(Neighbour.Id, 2, DateTimeOffset.UnixEpoch, "/work/neighbour", IsAlive: true)];

        await TakenAsync();
        _files.Add("/work/neighbour/.venv");
        _files.Add("/Users/dev/.cache/somewhere");

        var discoveries = (await TakenAsync()).Discoveries;

        Assert.Equal(Neighbour, discoveries.Single(discovery => discovery.Trace.Key == "/work/neighbour/.venv").Owner);
        Assert.Equal(Owner.Nobody, discoveries.Single(discovery => discovery.Trace.Key == "/Users/dev/.cache/somewhere").Owner);
    }

    [Fact]
    public async Task Discovery_IsNeverARemoval()
    {
        await TakenAsync();
        _files.Add("/tmp/thing");

        var plan = Planner.Decide(await TakenAsync(), Scope.Everything, "plan");

        Assert.Single(plan.Discoveries);
        Assert.Empty(plan.Removals);
    }

    private async Task<long> RowsAsync()
    {
        await using var connection = await _file.OpenAsync(CancellationToken.None);
        await using var count = connection.CreateCommand();

        count.CommandText = "SELECT COUNT(*) FROM discoveries;";

        return (long)(await count.ExecuteScalarAsync(CancellationToken.None))!;
    }

    public void Dispose()
    {
        _file.Dispose();
        _scratch.Dispose();
    }

    /// <summary>A kind that looks by following a trail, the way the file kind follows the file trail.</summary>
    private sealed class WalkingKind(FakeTrail trail) : Snail.MCP.CleanUp.Core.Application.Ports.IResourceKind
    {
        public string Name => Kinds.File;

        public Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken) =>
            Task.FromResult(new Observation(Name, [], []) { Followed = new Walk(trail.Name, [.. trail.Present]) });

        public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) =>
            Task.FromResult(Release.AlreadyGone(resource));
    }

    private sealed class ClaimingKind(string claimed) : Snail.MCP.CleanUp.Core.Application.Ports.IResourceKind
    {
        public string Name => Kinds.File;

        public Task<Observation> ObserveAsync(IReadOnlyList<Session> sessions, CancellationToken cancellationToken) =>
            Task.FromResult(new Observation(Name, [], [claimed]));

        public Task<Release> ReleaseAsync(Resource resource, CancellationToken cancellationToken) =>
            Task.FromResult(Release.AlreadyGone(resource));
    }
}
