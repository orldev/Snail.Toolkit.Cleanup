using BenchmarkDotNet.Attributes;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Benchmarks;

/// <summary>A whole census over a trail the size of a busy machine's, with every port in memory: what the census itself costs.</summary>
/// <remarks>
/// A busy Mac's file trail was 32 000 entries on 2026-10-13; a kind follows it to look, and the census reads it from that
/// observation. Beside it a trail of 2 000 processes the census walks itself, 5 000 resources of which half are written in
/// the ledger, sixteen rules, and two owners going: one whose baseline lacks the newest thousand entries, which are
/// therefore discoveries seen again at every census, and one that took its baseline with everything there.
/// </remarks>
[MemoryDiagnoser]
public class CensusBenchmarks
{
    private const int Entries = 32_000;

    private const int NewEntries = 1_000;

    private const int Processes = 2_000;

    private const int Resources = 5_000;

    private static readonly Holder Began = new(new Owner("session", "bench-began"), IsAlive: true) { Directory = "/work/began" };

    private static readonly Holder Settled = new(new Owner("session", "bench-settled"), IsAlive: true) { Directory = "/work/settled" };

    private Census _census = null!;

    private IReadOnlyList<Trace> _traces = [];

    private IReadOnlyList<Rule> _rules = [];

    [GlobalSetup]
    public async Task Setup()
    {
        List<Trace> files = [.. Enumerable.Range(0, Entries).Select(index => File($"/private/var/folders/xx/T/clr-debug-pipe-{100_000 + index}-in"))];
        List<Trace> processes = [.. Enumerable.Range(0, Processes).Select(index => new Trace("process", $"{index}@1791828291", $"program {index}"))];
        List<Resource> resources = [.. Enumerable.Range(0, Resources).Select(Made)];

        _traces = files;
        _rules = Rules();

        var ledger = new MemoryLedger();
        var baselines = new MemoryBaselines();

        foreach (var resource in resources.Where((_, index) => index % 2 == 0))
            await ledger.RecordAsync(new Holding(resource.Id, Began.Owner, Evidence.Ledger, DateTimeOffset.UnixEpoch, resource.Title), CancellationToken.None);

        await baselines.SaveAsync(Began.Owner, [.. files.Take(Entries - NewEntries).Select(trace => trace.Id)], DateTimeOffset.UnixEpoch, CancellationToken.None);
        await baselines.SaveAsync(Settled.Owner, [.. files.Select(trace => trace.Id), .. processes.Select(trace => trace.Id)], DateTimeOffset.UnixEpoch, CancellationToken.None);

        _census = new Census(
            [new WalkingKind("file", resources, files)],
            [new StillTrail("file", files), new StillTrail("process", processes)],
            new StillHolders(Began, Settled),
            ledger,
            new StillRules(_rules),
            baselines,
            new MemoryDiscoveries(),
            TimeProvider.System);

        await _census.TakeAsync(Began.Owner, CancellationToken.None);
    }

    /// <summary>The census a running host takes when something changed: the record already holds what is new.</summary>
    [Benchmark]
    public async Task<int> CensusAsync() => (await _census.TakeAsync(Began.Owner, CancellationToken.None)).Discoveries.Count;

    /// <summary>Every trace of the file trail matched against every rule, as a census asks of what nothing claims.</summary>
    [Benchmark]
    public int MatchRules()
    {
        var matched = 0;

        foreach (var trace in _traces)
        {
            foreach (var rule in _rules)
            {
                if (rule.Matches(trace))
                {
                    matched++;

                    break;
                }
            }
        }

        return matched;
    }

    private static Trace File(string path) => new("file", path, path);

    private static Resource Made(int index) => new()
    {
        Kind = "file",
        Key = $"/private/var/folders/xx/T/clr-debug-pipe-{100_000 + index}-in",
        Title = $"pipe {index}",
        Owner = index % 3 == 0 ? Owner.Nobody : Began.Owner,
        Evidence = index % 3 == 0 ? Evidence.None : Evidence.Label
    };

    /// <summary>Sixteen rules of the shapes a host ships: fixed folders, one level of wildcard, whole subtrees.</summary>
    private static List<Rule> Rules() =>
    [
        new("file", "~/.claude/**", RuleAction.Ignore),
        new("file", "~/Library/Caches/*", RuleAction.Cache),
        new("file", "~/Library/Application Support/Code/WebStorage/*", RuleAction.Cache),
        new("file", "~/Library/Developer/Xcode/DerivedData/*", RuleAction.Cache),
        new("file", "~/.nuget/packages/**", RuleAction.Ignore),
        new("file", "~/.npm/_cacache/**", RuleAction.Cache),
        new("file", "/private/var/folders/*/*/T/com.apple.*", RuleAction.Ignore),
        new("file", "/private/var/folders/*/*/C/**", RuleAction.Ignore),
        new("file", "/private/var/folders/xx/T/dotnet-diagnostic-*-socket", RuleAction.Report),
        new("file", "/private/var/folders/xx/T/s3l-test-*", RuleAction.Report),
        new("file", "/tmp/claude-*/**", RuleAction.Report),
        new("file", "~/Library/Logs/**", RuleAction.Ignore),
        new("file", "~/.cache/**", RuleAction.Cache),
        new("process", "/System/**", RuleAction.Ignore),
        new("process", "/usr/libexec/*", RuleAction.Ignore),
        new("process", "/Applications/*/Contents/MacOS/*", RuleAction.Ignore)
    ];
}
