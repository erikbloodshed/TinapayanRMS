# 0006. Testing strategy

**Status:** Accepted, 2026-11-20. Narrowed by [ADR 0009](0009-close-the-migration.md): the screens' tests may use the database. The
random sequences stand, and aren't all built yet. Narrowed by [ADR 0011](0011-change-control-on-the-developers-pc.md)
too: local CI is to run the window's smoke run, once it's built, where 0006 kept the window out of CI. Extended by
[ADR 0012](0012-figures-hear-of-a-change-from-whoever-made-it.md): the sequences check each figure after every step.

## Context

The suite is large (some 50,000 lines of tests to 58,000 of code) and passes, and the costing golden master catches any
figure that moves. The bugs that get through are in how a record form coordinates a load, an undo, the dirty check and
the tabs: sequences of steps, each fine alone. The screens' tests need a database, because their states can't be built
without repositories (ADR 0002), so they're slow to run and few sequences get tried.

## Decision

Each layer is tested where it's cheapest to test it well:

| Level | What | How |
|---|---|---|
| Rules and drafts | `*Rules`, validators, and ADR 0002's drafts | Plain unit tests, no database, no UI thread |
| Drafts' invariants | Random sequences of edits, loads (with and without changes saved elsewhere), undos, redos, saves and tab switches | Model-based tests with a property-testing library (CsCheck), checking that undoing every step gives back the record as loaded; that the dirty check always equals "differs from what was saved"; that a load that finds nothing new changes neither the form nor whether it's dirty; that undo and redo across a load keep what the load brought |
| Use cases and repositories | The editors and query services, the repositories, the cost cascade | On PostgreSQL, through the existing fixtures |
| Costing | Every product's figures from an anonymized copy of the shop's data | The golden master (Mcp.Tests), as now |
| Screens | A form or a view over fakes of its editor's interface | Presentation.Tests, on `UiThread`, without a database once its screen has moved |
| Components | Drawing, binding, keys | bUnit (Components.Tests), as now |
| The architecture | Layers, data access, sizes, numbers, rounding | Components.Tests' `Architecture/` (ADR 0004) |
| The window | Focus, typing, drag and drop, printing, dialogs | The `run-tinapayanrms` driver, for each screen a change reaches |

A bug fixed gets the test that would have caught it, at the lowest level that can show it; one in a draft's sequence of
steps becomes an invariant, or a fixed example of one, so the whole class is covered and not just the one case.

## Consequences

- The sequences that break the forms are tried by the thousand in seconds, where today a test tries the one sequence
  someone thought of.
- Model-based tests need a model of what each step should do; for a draft it's small (the record as saved and the
  steps since), but it's code to keep in step with the draft.
- The screens' tests get faster as each screen moves, and the database tests concentrate on the use cases.

## Rejected

- **More example-based tests of the forms.** Each covers the sequence its author imagined; the bugs found are the ones
  nobody did.
- **Mutation testing (Stryker.NET) as a gate.** Useful as an occasional audit of the rules' tests, too slow to gate every
  build, and its support for TUnit on Microsoft.Testing.Platform is to be checked first.
- **End-to-end tests in the window on CI.** The window needs WebView2 and a desktop session; the driver is for checking
  a change by hand, and bUnit covers what can be drawn without it.
