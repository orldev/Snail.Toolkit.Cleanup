# Snail.Toolkit.Cleanup

The part of cleaning up what agents and tasks leave behind that does not depend on where it runs: who owns each
resource and how that is known, what a cleanup may remove, and in which order. A host describes the places resources
live in — processes, containers, folders — as kinds; the library takes a census of them, decides a plan and carries it
out, and removes nothing whose owner is not proven to be over.

It is used by [Snail.MCP.CleanUp](https://github.com/orldev/Snail.MCP.CleanUp), which keeps account of what agents
leave on a developer's machine, and by Snail.Swarm.Agent, whose workers clear what finished tasks and retired projects
left on their disks.

```csharp
using Snail.Toolkit.Cleanup.Application;
using Snail.Toolkit.Cleanup.Domain;

Holder[] owners = [new(new Owner("task", finishedId), IsAlive: false), new(new Owner("task", runningId), IsAlive: true)];
var inventory = new Inventory(clock.GetUtcNow(), resources, owners);
var plan = Planner.Decide(inventory, Scope.Orphans, planId, KindOrder.General);
var releases = await new Cleaner(kinds, new NoLedger()).ApplyAsync(plan, cancellationToken);
```

A host with a ledger, baselines and trails takes the inventory with a `Census` instead, and the inventory it answers
also carries the asking owner, what was kept on request, the discoveries and what could not be seen.

A plan may be carried out minutes after it was decided. A host whose owners can come back — a session of an agent is
resumed under the same id — gives the cleaner its holders, `new Cleaner(kinds, ledger, holders)`: they are asked once
more before anything goes, and what belongs to an owner going again is left alone, its release `Changed`. A host whose
owners never come back once over, such as finished tasks, may leave them out.

A host that stores baselines whole keeps `Baseline.ToBytes()`, eight bytes an id, and reads it back with
`Baseline.FromBytes`.

## Owners

An `Owner` is a kind the host names and an id: `session:…`, `task:…`, `project:…`. A `Holder` is an owner the host
knows of and whether it is still going, with when it began, when it ended and the directory it works in when the
host knows them. The record is open, so a host carries what only its own owners have — the process of an agent's
session — on a record of its own, and its kinds get that record back.

## Kinds and their order

The library names the kinds every host may have: `process`, `container`, `network`, `volume`, `image`, `file`, taken
apart in that order. A host places its own kinds among them, and a kind left out of the order goes last:

```csharp
var order = KindOrder.General
    .After(Kinds.Process, "mcp", "forward", "emulator", "simulator", "avd")
    .After(Kinds.Image, "dangling", "buildcache", "buildhistory")
    .Before(Kinds.File, "worktree", "editor", "testresults", "remains", "outside");
```

## Entries named for their owner

`NamedEntries` is a ready-made kind for entries under a root whose name carries their owner — the folder of a task
named for the task, the key of a project named for the project. The name is the proof; an entry goes once its owner
is over or erased, but not within a grace after it was last written or after its owner ended, and never when it
changed since it was observed. Links are never followed out of the root. A platform worker covers its folders and its
keys with two instances:

```csharp
static string? GuidIn(string name) => Guid.TryParseExact(name, "N", out var id) ? id.ToString("n") : null;

static string? KeyOwnerIn(string name)
{
    var dot = name.IndexOf('.', StringComparison.Ordinal);

    return dot > 0 && name.AsSpan(dot).StartsWith(".key", StringComparison.Ordinal) ? GuidIn(name[..dot]) : null;
}

IResourceKind[] kinds =
[
    new NamedEntries(new NamedEntriesOptions
    {
        Kind = "workspace", Root = root, OwnerKind = "task", OwnerIn = GuidIn, Grace = grace, IsRegenerable = true
    }, clock),
    new NamedEntries(new NamedEntriesOptions
    {
        Kind = "project-key", Root = Path.Combine(root, ".credentials"), OwnerKind = "project",
        OwnerIn = KeyOwnerIn, Entries = EntryType.Files, Pattern = "*.key*", Grace = grace
    }, clock)
];
```

A key is named for its project up to the first dot, and `.key` follows that dot: `<id>.key` and the half-written
`<id>.key.<guid>` a crash leaves beside it are the project's, while `<id>.old.key`, `<id>.pub` and `deploy.key` carry
no owner and are never touched — the pattern only narrows the listing, the owner is read by `OwnerIn`.

A task's holder says when it ended (`EndedAt`), and the grace is counted from there; a task erased from the host's
records has no holder, and its folder goes once it has not been written for the grace.

Only what can go is measured. The folder of a task still going is kept whatever its size, so it is never walked and
its `Bytes` stays unknown; the folders of tasks that ended or were erased are counted file by file, «at least» when the
count stopped short (`NamedEntries.CountedAtMost` files, or a directory it may not read). A file's length comes with
the listing and is always given.

A release says `deleted from <root>`. A host that knows why its entries may go says so in its own words instead, given
the entry as it was released: `ReleaseDetail = _ => "erased: the task's branch carries its work"`.

## What holds, whatever the host


- **Proof before removal.** A resource goes on ownership alone only when its evidence is proof: its name or label
  carries the owner, the host recorded it at birth, its environment carries the owner, or it was seen running under the
  owner's own process. Something that merely appeared while an owner ran is reported, never removed.
- **A check again before release.** Each kind makes sure, right before it acts, that the resource is still the one the
  plan described; what changed is left alone and said so.
- **Order.** Removals go in dependency order — a process before the folder it writes into, a container before its
  network — and a resource whose predecessor failed to go is not attempted.
- **Nothing removed when owners cannot be read.** A host that cannot list its owners fails the census rather than
  answer with fewer: an owner missing from the list would be taken for one that is over.

The public API: [docs/reference](docs/reference/Snail.Toolkit.Cleanup.md). Built on
[Snail.Sdk](https://github.com/orldev/Snail.Sdk): the rules, the public API kept in `src/PublicAPI.*.txt`, validation
against the last release, the shared workflows.
