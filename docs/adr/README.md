# Architecture decision records

Each file here records one decision about how TinapayanRMS is built: what was decided, why, what it costs, and what
was considered and turned down. PLAN.md describes the app as it is; these say why it's that way, and they stay as
written. A decision that changes gets a new record that supersedes the old one, and the old one's status says so.

| # | Decision | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-layers-use-cases-and-drafts.md) | Layers, use cases and drafts | Accepted; migration in progress (phases 0 to 2 done); `Rebase` narrowed by 0007 |
| [0003](0003-mutable-observable-screen-state.md) | Mutable, observable screen state | Accepted |
| [0004](0004-quality-gates.md) | Quality gates the build enforces | Accepted |
| [0005](0005-change-control-and-dependencies.md) | Change control and dependencies | Accepted |
| [0006](0006-testing-strategy.md) | Testing strategy | Accepted |
| [0007](0007-a-load-repoints-a-draft.md) | A load re-points a draft; it doesn't merge saves made elsewhere | Accepted |

## Writing one

Copy the shape of an existing record: **Status**, **Context** (the forces, with evidence), **Decision**, **Consequences**
(what gets better, what it costs) and **Rejected** (each alternative and why). Number it next, add it to the table above,
and log it in the build plan (docs/plan/build-plan.md). Write one before a change that adds a project, a layer, a
framework or a library the app's code is built around, or that changes a rule another record set.
