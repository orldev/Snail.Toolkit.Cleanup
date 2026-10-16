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
