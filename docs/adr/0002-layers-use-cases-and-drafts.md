# 0002. Layers, use cases and drafts

**Status:** Accepted, 2026-11-20. Migration in progress: phases 0 (guardrails), 1 (Sub-Assemblies) and 2 (Raw Materials) done; phase 3 (Products) begun, its use cases in place and its Packaging, Components, Costing and Recipe drafts, and the product's own draft over them; `ProductForm` still over 700 lines. What
`Rebase` does is narrowed by [ADR 0007](0007-a-load-repoints-a-draft.md): it re-points a draft, and merges nothing.

## Context

The layers are sound: Domain, ApplicationCore (rules, ports, validators), Pricing, Infrastructure, and the screens'
Presentation and Components. What isn't is the screens' state. A record screen's state class does three jobs:

1. **Use cases**: load, save, clone, delete, the transaction, validation, the saved-version check. `ProductsState` took
   21 constructor parameters on 2026-11-20, 14 of them repositories, plus `IUnitOfWork` and three validators;
   `ProductForm.SaveAsync` builds the entity, validates it, works out what changed and calls `VersionedSaves`.
2. **The editing model**: the record being edited, its rules, its snapshots, its dirty check, and merging a load into
   it. Spread over `ProductForm` (1,980 lines), `ProductRecipe` (1,685) and their parts, and held together by calls
   every writer has to remember: `RefreshDirty` 38 times, `InvalidateCosts` 27, `ApplyRefresh` 15, `IsBusyApplying` 13.
3. **The screen**: tabs, picks, prompts, toasts, commands.

The cost shows: Presentation references Infrastructure; 37 of Presentation.Tests' files need a database, since a state
can't be built without its repositories; and the bugs that come back are where the jobs meet, a load merging into a
form with edits and an undo after a load (6b652d7, 6d1a1fc on 2026-11-20).

## Decision

```
Maui ─► Components ─► Presentation ─► UseCases ─► Pricing ─► ApplicationCore ─► Domain
                         │                                    ▲
                         └─ drafts (no I/O)     Infrastructure ┘ (implements the ports)
Components, the composition root, is the one place above Infrastructure that sees it.
```

- **A new project, `TinapayanRMS.UseCases`**, the application layer, above ApplicationCore and Pricing and below
  Presentation, with no reference to Infrastructure. One editor per aggregate for its commands (`ISubAssemblyEditor`,
  `IRawMaterialEditor`, `IProductEditor`: load, save, clone, delete, move, rename) and query services for the lists,
  Reports and the Price Simulator. They own the transaction (`IUnitOfWork`), validation, the saved-version check and the
  versioned saves; a save takes a request and gives a result record; a refusal stays a `BusinessRuleException`. They're
  tested on PostgreSQL, as the repositories are.
- **Each record screen in Presentation is three pieces**:
  - **`XDraft`**, the editing model: mutable and observable (ADR 0003), with its rows, and nothing else: no service, no
    I/O, no screen concern. It's made from a saved record (`From(detail)`), holds the editing rules, captures and
    restores its snapshots, says what a Save would write (`ToSaveRequest`, only what changed), and keeps a `Version`
    that any change anywhere in it moves, which its worked-out figures and its dirty check are cached on, in place of
    every call to `RefreshDirty` and `InvalidateCosts`. **`Rebase(fresh)`** is the one place a load meets the user's
    edits: a three-way merge of the record as it was saved, as it's saved now, and as edited, in place of `ApplyRefresh`,
    `IsRefreshing` and the merges spread through the parts.
  - **`XForm`**, the screen: tabs, picks, commands, prompts, and the undo history over the draft. It depends on its
    draft and its editor's interface.
  - **`XState`**, the view: the list, the record tabs, and its load through the query services, as now.
- Domain, ApplicationCore, Pricing, Infrastructure, Components, Maui and the two servers keep their places. Nothing is
  renamed for the sake of it.

### Migration, a screen at a time, each phase shipped on its own

| Phase | What | Done when |
|---|---|---|
| 0 | The guardrails of ADR 0004: the layers, data access and constructor sizes held where they are | Done 2026-11-20 |
| 1 | The UseCases project, `SubAssemblyEditor`, `SubAssemblyDraft` with `Rebase`, model-based tests of it (ADR 0006) | Its form and view hold no data-access type and take at most six things; the costing golden master unchanged; checked in the window |
| 2 | Raw Materials the same way; the cost cascade stays in `IngredientCostPropagator`, called by its editor | The same |
| 3 | Products, a part at a time: Packaging, Components, Costing, then the Recipe | `ProductForm` under 700 lines; Presentation no longer uses `Infrastructure.Services` |
| 4 | Settings, Reports, the Price Simulator and Printing onto query services; Presentation's reference to Infrastructure removed | `DataAccessTests.StillReaching` and `LayerTests.StillThere` empty |

No feature work on a screen while it's being moved, and each phase passes the whole suite, the golden master and a
check in the window before it's merged.

## Consequences

- The bug class that keeps coming back lives in one object per screen, the draft, which is tested without a database or
  a UI thread, by properties over random sequences of edits, loads, undos and saves (ADR 0006).
- The screens' tests use fakes of the editors' interfaces where they used a database; the editors' tests use the
  database, as the repositories' do.
- One more project, and an interface between the screens and what they save. Each editor is a thin layer over what the
  screens already call, so the cost is in moving code, not in writing new logic.
- Four phases of moving code, during which the screens being moved get no features.

## Rejected

- **An immutable store (Redux, Fluxor).** Editing needs mutable rows: `LineGrid` keys its rows and keeps the cursor by
  object, and its add row becomes a line by changing in place. An immutable store would need a layer of mutable view
  rows kept in step with it, which is the merging this decision takes out, moved rather than gone (ADR 0003).
- **A mediator (MediatR) or a pipeline of handlers.** Indirection with nothing to gain in one process, and commercially
  licensed since 2025. Plain interfaces say what a screen can do.
- **A generic repository, event sourcing, Result types in place of exceptions.** Each is churn without a matching gain:
  the repositories are already per aggregate, nothing needs an event history beyond the product versions, and
  `RunSafelyAsync` already turns a refusal into words.
- **Renaming ApplicationCore to Application and moving the rules to Domain.** Tidier names, and a rename of every
  namespace and test, for nothing the app does differently.
- **Leaving the state classes as they are and splitting them by size.** Splitting by size moves code between files
  without separating the jobs, which is what the parts did on 2026-11-14.
