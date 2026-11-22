# Architecture decision records

Each file here records one decision about how TinapayanRMS is built: what was decided, why, what it costs, and what
was considered and turned down. PLAN.md describes the app as it is; these say why it's that way, and they stay as
written. A decision that changes gets a new record that supersedes the old one, and the old one's status says so.

| # | Decision | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-layers-use-cases-and-drafts.md) | Layers, use cases and drafts | Accepted; migration complete 2026-11-21, closed by 0009; narrowed by 0007 and 0009 |
| [0003](0003-mutable-observable-screen-state.md) | Mutable, observable screen state | Accepted; narrowed by 0009; its rejection of signals restated by 0012 |
| [0004](0004-quality-gates.md) | Quality gates the build enforces | Accepted, its text edited after (0009); partly superseded by 0010 |
| [0005](0005-change-control-and-dependencies.md) | Change control and dependencies | Accepted, amended 2026-11-21; superseded by 0011 |
| [0006](0006-testing-strategy.md) | Testing strategy | Accepted; narrowed by 0009 and 0011, extended by 0012 |
| [0007](0007-a-load-repoints-a-draft.md) | A load re-points a draft; it doesn't merge saves made elsewhere | Accepted |
| [0008](0008-benchmarks-and-performance-budgets.md) | Benchmarks, startup timing and performance budgets | Accepted |
| [0009](0009-close-the-migration.md) | The migration closes: what it dropped, and what stays open | Accepted |
| [0010](0010-the-gates-read-method-bodies.md) | The gates read method bodies, the app's and the servers' too | Accepted |
| [0011](0011-change-control-on-the-developers-pc.md) | Change control on the developer's PC | Accepted |
| [0012](0012-figures-hear-of-a-change-from-whoever-made-it.md) | Figures hear of a change from whoever made it | Accepted |

## Writing one

Copy the shape of an existing record: **Status**, **Context** (the forces, with evidence), **Decision**, **Consequences**
(what gets better, what it costs) and **Rejected** (each alternative and why). Number it next, add it to the table above,
and log it in the build plan (docs/plan/build-plan.md). Write one before a change that adds a project, a layer, a
framework or a library the app's code is built around, or that changes a rule another record set, a new kind of gate
included ([ADR 0010](0010-the-gates-read-method-bodies.md)).
