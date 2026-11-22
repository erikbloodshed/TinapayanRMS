# 0012. Figures hear of a change from whoever made it

**Status:** Accepted, 2026-11-23, the owner choosing "ADR 0012, with a figures check" and accepting the gap it names at
the forms. Restates the reason for [ADR 0003](0003-mutable-observable-screen-state.md)'s rejection of signals, which
[ADR 0009](0009-close-the-migration.md) took away, and adds a check to [ADR 0006](0006-testing-strategy.md)'s random
sequences. Changes nothing built.

## Context

ADR 0003 turned down signals, automatic dependency tracking as MobX does it, because "every getter would have to report
its reads; the draft's single version counter gets the same safety for its figures with nothing to report". ADR 0009
dropped that counter: nothing read it in the two drafts that kept one, and Products' drafts never had one. What signals
were weighed against is gone, and no record says what they're weighed against now.

As built (the developer manual's "Screen state"), a figure worked out from a table or from many fields is a getter over a
`Derived<T>`, worked out when it's read and only if it's been marked stale since. It's marked broadly, by events rather
than by each setter: `RowChanges.OnAnyChange` on each table it reads, and the part's own `OnPropertyChanged` where it
reads the part's fields. Where it reads something else, a call marks it (`InvalidateCosts`, named on 24 lines of
Presentation, `BomInputsChanged`, the cost basis, the shop's defaults), as `RefreshDirty`, on 24, tells a dirty check. A
mark too many costs one more computation; a mark missed shows a stale figure, the cost 0009 named as left.

Some figures are tested for a missed mark. After every step of their random sequences, `ProductCostingTests` compares
the costing's figures with a fresh pricing of its inputs as they are then (`ShouldPriceAsNow`), its harness telling the
costing only what the form tells it (`InputsChanged`, as `ProductForm.InvalidateCosts` does); and `ProductDraftTests`
checks that the product's costs follow its fields (`ShouldPriceTheFields`), with nothing in its harness marking them,
so a mark that stopped fails it. The other drafts' figures are checked by no sequence: Sub-Assemblies' batch figures
(`SubAssemblyBatch`) and its packaging set's cost (`SubAssemblyPackagingSet`), and a made ingredient's BOM totals
(`RawMaterialRecipe`). The recipe's section form works out figures of its own (`ProductUsageForm`), a form's, not a
draft's. And a call a form makes is outside every draft's sequences, whose harness makes it in the form's place.

## Decision

- **Figures stay as built**: a `Derived<T>` marked stale broadly, by the events of what it reads, and by a call where
  it reads something else. A form follows what its drafts raise (ADR 0009).
- **Signals stay turned down**, now for the reason 0009 gave against building the counter: either is a rewrite of how
  every figure and dirty check hears of a change, in every screen's state, with no bug that asks for it.
- **The drafts' sequences check each figure after every step** against a fresh working-out from the draft's inputs as
  they are then, the harness marking nothing but what the form's own code would, as in `ProductCostingTests`:
  Sub-Assemblies' batch figures and packaging set's cost (`SubAssemblyDraftTests`), and a made ingredient's BOM totals
  (`RawMaterialDraftTests`). A
  figure only a form has, as the section form's, is checked at the form, as 0009 has the tab switch. The build plan's
  open items hold the work, beside the steps of ADR 0006 that 0009 kept open.
- **To be revisited** if a stale figure gets past those sequences once they take every step, or if a figure comes to
  read another screen's state, where no event of its own part would mark it.

## Consequences

- 0003's choice holds, for a reason that's true today. The cost it named, that what a write sets off depends on who
  wrote it, stays, with a check for each figure that would show a mark gone missing.
- Each sequence asserts more after every step. Its fresh working-out is the figure's own arithmetic over the inputs as
  they stand (a batch's weight and cost from its lines, a BOM's totals by `IngredientBomRules.CalculateCost`, as
  `ProductCostingTests` prices with `ProductCostSummary`): a check of the marks, not of the arithmetic, which the rules'
  tests hold.
- A mark a form makes (`InvalidateCosts` and its kind) stays outside the drafts' sequences, whose harness makes it in
  the form's place: a form that stops making one is caught by the form's own tests, examples, as now.
- Until the checks are built, a missed mark in Sub-Assemblies or Raw Materials is caught only by an example someone
  wrote.

## Rejected

- **Signals now**, a library's or the app's own tracking in `Derived<T>`: each getter reports its reads, and nothing is
  marked by hand. The rewrite 0009 declined; and a figure that reads through a part's method, or a row's, has to be
  found to report.
- **Taking the counter back**, as ADR 0002 planned it: what 0009 dropped, for the same reason.
- **Leaving the figures as the tests have them.** Two drafts' figures would be checked by examples alone, where the
  sequences that would check them are being built already (0006's steps, 0009's open item).
