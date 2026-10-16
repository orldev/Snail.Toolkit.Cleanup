# Snail.Toolkit.Cleanup

The part of cleaning up what agents and tasks leave behind that does not depend on where it runs: who owns each
resource and how that is known, what a cleanup may remove, and in which order. A host describes the places resources
live in — processes, containers, folders — as kinds; the library takes a census of them, decides a plan and carries it
out, and removes nothing whose owner is not proven to be over.

It is used by [Snail.MCP.CleanUp](https://github.com/orldev/Snail.MCP.CleanUp), which keeps account of what agents
leave on a developer's machine, and by Snail.Swarm.Agent, whose workers clear what finished tasks and retired projects
left on their disks.

```csharp
var inventory = await census.TakeAsync(asker, cancellationToken);
var plan = Planner.Decide(inventory, Scope.Orphans, planId);
var releases = await new Cleaner(kinds, ledger).ApplyAsync(plan, cancellationToken);
```

What holds, whatever the host:

- **Proof before removal.** A resource goes on ownership alone only when its evidence is proof: its name or label
  carries the owner, the host recorded it at birth, its environment carries the owner, or it was seen running under the
  owner's own process. Something that merely appeared while an owner ran is reported, never removed.
- **A check again before release.** Each kind makes sure, right before it acts, that the resource is still the one the
  plan described; what changed is left alone and said so.
- **Order.** Removals go in dependency order — a process before the folder it writes into, a container before its
  network — and a resource whose predecessor failed to go is not attempted.
- **Nothing removed when owners cannot be read.** An owner the host could not read is not an owner that is over.

The public API: [docs/reference](docs/reference/Snail.Toolkit.Cleanup.md). Built on
[Snail.Sdk](https://github.com/orldev/Snail.Sdk): the rules, the public API kept in `src/PublicAPI.*.txt`, validation
against the last release, the shared workflows.
