# 0003. Mutable, observable screen state

**Status:** Accepted, 2026-11-20. Records the state model the app has used since ReactiveUI went (2026-11-16).

## Context

The screens' state could be mutable objects that say when they change, or immutable values replaced by a store. The
second makes undo putting an old value back, and a dirty check an equality; Fluxor, a Redux for Blazor, was weighed for
it. But every table the user types into is the app's own `LineGrid`, whose cells are bound straight to a row's
properties (`ValueChanged="v => row.Grams = v"`), and it depends on the row being the same object from keystroke to
keystroke:

- `<tr @key="row">` keeps the row's element, and so the cursor in the cell being typed into, by the object;
- the add row's draft becomes a line by changing in place: its own `PropertyChanged` moves it into the lines;
- the row picked, and each row's handlers, are kept by reference;
- a row's rules are its setters' (typing grams lets a typed percentage go; picking an ingredient prices the row).

## Decision

The screens' state stays mutable and observable: `ObservableState`, a plain `INotifyPropertyChanged`, with
`ScreenCommand`, `Interaction`, `Derived<T>` and `UndoHistory<TSnapshot>`, the app's own, and no MVVM or state library.
Snapshots for undo and the dirty check stay immutable records. Rx and DynamicData stay for what they do: a load a newer
pick cancels (`LoadLatest`), what one screen asks of another, and sorted and refreshed lists. ADR 0002's drafts are
built on this, and take over what it costs: one object per screen owns the snapshots, the version its figures are
cached on, and the merge of a load.

## Consequences

- Editing stays as it is: a keystroke sets a property on the row it's typed into, and nothing is rebuilt.
- Mutable state's costs stay too: what a write sets off depends on who wrote it (the user, a load, an undo), and figures
  worked out from the form must hear of every change. ADR 0002's draft puts both in one place per screen.

## Rejected

- **An immutable store (Fluxor, Redux) with reducers.** A keystroke would make a new row; keyed by object, the row's
  element and the cursor go with it. Keying by id fixes that, but the draft row, the pick and the handlers all need
  their own fixes, and a table of mutable view rows kept in step with the store's would be needed anyway: the merging
  that ADR 0002 takes out, back as a projection.
- **An MVVM library** (ReactiveUI, which the app had until 2026-11-16, or CommunityToolkit.Mvvm). `ObservableState` is
  some fifty lines, and the screens' trouble is the jobs their state classes do, not the code to say a property changed.
- **Signals or automatic dependency tracking** (as MobX does). Every getter would have to report its reads; the draft's
  single version counter gets the same safety for its figures with nothing to report.
