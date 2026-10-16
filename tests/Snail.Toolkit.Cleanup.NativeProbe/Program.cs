using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;

var asker = new Owner("session", "probe");
var ended = new Owner("task", "ended");

Resource[] resources =
[
    new() { Kind = Kinds.File, Key = "/scratch", Title = "scratch", Owner = ended, Evidence = Evidence.Label },
    new() { Kind = Kinds.Process, Key = "7@1", Title = "server", Owner = ended, Evidence = Evidence.Ledger }
];

var inventory = new Inventory(DateTimeOffset.UnixEpoch, resources, [new Holder(ended, IsAlive: false)]) { Asker = asker };

var plan = Planner.Decide(inventory, Scope.Orphans, "probe", KindOrder.General);
var order = string.Join(',', plan.Removals.Select(removal => removal.Resource.Id));

using var process = System.Diagnostics.Process.GetCurrentProcess();
var started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
var folder = Path.Combine(Path.GetTempPath(), $"s3l-test-{Environment.ProcessId}-{started}-{Path.GetRandomFileName()[..8]}");

Directory.CreateDirectory(folder);

try
{
    var rules = new JsonRules(Path.Combine(folder, "rules.json"), []);

    await rules.AddAsync(new Rule("file", "/tmp/probe/*", RuleAction.Cache) { Note = "native" }, CancellationToken.None);

    var learned = await rules.ListAsync(CancellationToken.None);
    var isRead = learned is [{ Action: RuleAction.Cache, Note: "native" }] && learned[0].Matches(new Trace("file", "/tmp/probe/x", "x"));

    Console.WriteLine($"plan: {order}; rule read back: {isRead}");

    return string.Equals(order, "process:7@1,file:/scratch", StringComparison.Ordinal) && isRead ? 0 : 1;
}
finally
{
    Directory.Delete(folder, recursive: true);
}
