using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Benchmarks;

/// <summary>Looking over a worker's workspace root where a few running tasks hold large build outputs and a few ended ones are small.</summary>
/// <remarks>
/// The shape of a Snail.Swarm.Agent worker an hourly pass looks at: four tasks still running, each with ten thousand files
/// of build output, and four tasks over, each with ten. Only what can go is worth a walk; the running tasks' folders stay
/// whatever their size, so their files are what the observation must not cost. Measured on 2026-10-16 (Apple M4,
/// ShortRun): 247 ms and 27.8 MB a look while every folder was walked, 0.29 ms and 50 KB once only the ended ones were.
/// </remarks>
[MemoryDiagnoser]
public class NamedEntriesBenchmarks
{
    private const int Running = 4;

    private const int Ended = 4;

    private const int FilesOfRunning = 10_000;

    private const int FilesOfEnded = 10;

    private string _root = "";

    private NamedEntries _entries = null!;

    private Holder[] _holders = [];

    [GlobalSetup]
    public void Setup()
    {
        using var process = Process.GetCurrentProcess();
        var started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();

        _root = Path.Combine(Path.GetTempPath(), $"s3l-test-{Environment.ProcessId}-{started}-{Path.GetRandomFileName()[..8]}");

        List<Holder> holders = [];

        for (var index = 0; index < Running; index++)
            holders.Add(new Holder(Folder(FilesOfRunning), IsAlive: true));

        for (var index = 0; index < Ended; index++)
            holders.Add(new Holder(Folder(FilesOfEnded), IsAlive: false) { EndedAt = DateTimeOffset.UnixEpoch });

        _holders = [.. holders];
        _entries = new NamedEntries(
            new NamedEntriesOptions { Kind = "workspace", Root = _root, OwnerKind = "task", OwnerIn = IdIn },
            TimeProvider.System);
    }

    [GlobalCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    /// <summary>One look over the root, as a pass makes it before it plans.</summary>
    [Benchmark]
    public async Task<int> ObserveAsync() => (await _entries.ObserveAsync(_holders, CancellationToken.None)).Resources.Count;

    private static string? IdIn(string name) => Guid.TryParseExact(name, "N", out var id) ? id.ToString("n") : null;

    private Owner Folder(int files)
    {
        var id = Guid.NewGuid().ToString("n");
        var output = Directory.CreateDirectory(Path.Combine(_root, id, "bin"));

        for (var index = 0; index < files; index++)
            File.WriteAllBytes(Path.Combine(output.FullName, $"{index}.dll"), [1, 2, 3, 4]);

        return new Owner("task", id);
    }
}
