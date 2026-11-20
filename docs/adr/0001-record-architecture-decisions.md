# 0001. Record architecture decisions

**Status:** Accepted, 2026-11-20.

## Context

Between 2026-11-14 and 2026-11-20 the screens' foundations changed five times: partial classes became parts,
ReactiveUI went, the ViewModels were renamed, edits became methods and figures `Derived<T>`, and each record view
opened its records in tabs. Each change was reasoned well, but the reasons live in the build plan's log, one row among
hundreds, and in PLAN.md, which describes the app as it now is rather than why. Nothing stood between one rewrite and
the next to ask whether the last had settled.

## Decision

Decisions about the architecture are recorded in `docs/adr/`, one file each, numbered, with the context, the decision,
its consequences and the alternatives turned down. A record isn't edited once accepted, beyond its status: a decision
that changes is a new record superseding it. PLAN.md and the developer manual describe what the records decided, and
link to them for why.

## Consequences

- A structural change starts as a record, so it's argued before it's built, and the next one can see what it rests on.
- The build plan's log stays the history of what changed and when; the records hold why.
- One more file to write for a structural change. A change inside the rules the records set needs none.

## Rejected

- **Keep decisions in PLAN.md only.** It describes the present, so a decision's reasons and its rejected alternatives
  are rewritten away as the app changes.
- **Keep them in the build plan's log.** It's a log: a decision is one dated row among hundreds, and a later row that
  changes it doesn't say so where the first one is.
