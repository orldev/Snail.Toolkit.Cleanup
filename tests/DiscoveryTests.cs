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

    private Census Census => new(
        [_known],
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

    [Fact]
    public async Task Discovery_SeenAgain_IsCountedNotDuplicated()
    {
        await TakenAsync();
        _files.Add("/tmp/thing");

        await TakenAsync();

        Assert.Equal(2, Assert.Single((await TakenAsync()).Discoveries).Sightings);
    }

    /// <summary>«Agents leave this» stays true after the thing is gone, so the record stays too.</summary>
    [Fact]
    public async Task Discovery_WhoseThingIsGone_StaysWrittenDown_MarkedAbsent()
    {
        await TakenAsync();
        var trace = _files.Add("/tmp/thing");

        await TakenAsync();
        _files.Present.Remove(trace);

        Assert.False(Assert.Single((await TakenAsync()).Discoveries).IsPresent);
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

    public void Dispose()
    {
        _file.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _scratch.Dispose();
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
