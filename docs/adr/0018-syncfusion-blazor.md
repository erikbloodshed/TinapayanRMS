# 0018. Syncfusion's Blazor components

**Status:** Accepted, 2026-11-07, with the window ([ADR 0017](0017-maui-blazor-hybrid-window.md)), whose screens came
built on them; how they're loaded, themed and iconed settled on 2026-11-06 and 2026-11-08, each at the owner's request.
Recorded after the fact on 2026-11-25, from the build plan's log, [docs/plan/maui-app.md](../plan/maui-app.md) ("How
it's built") and PLAN.md ("Syncfusion UI Framework"), as the open item from the ADR review of 2026-11-23 asked. The
log's Phase 3 built the WPF app's shell on Syncfusion's WPF controls without saying why; the owner gave the reason on
2026-11-25: "Syncfusion was selected because of its maximum component coverage, besides since i have a developer
lincense, i can use syncfusion components for free".

## Context

**Syncfusion was picked for how many components it covers, and because the owner's developer licence lets the app use
them at no cost.** One vendor's set reaches nearly everything the screens draw (grids with export, combo boxes, tabs,
dialogs, toasts, tooltips, charts), so the app builds only what none of them does.

The WPF app had been built on Syncfusion's WPF controls since Phase 3, under a licence key that covers Syncfusion's
Blazor components and XlsIO too. The web repo's screens, ported on 2026-11-07, were Blazor components on Syncfusion
Blazor, which the same key licenses.

## Decision

- **A Syncfusion Blazor component is used where one fits**, over a plain HTML control. Every Syncfusion package is at
  one version, `SyncfusionVersion` in `Directory.Packages.props`, so they upgrade together; Dependabot groups them
  (ADR 0013).
- **Where Syncfusion's don't fit, the app has its own**: `TextField` and `NumberField` (they keep their own text while
  focused, which Syncfusion's boxes don't), `PickField` (an `SfComboBox` of the app's own, bound by index), `LineGrid`
  (every cell of a typed table live at once), `FolderTree` (the screen state owns the nodes) and `MenuButton`.
- **The theme is Fluent 2's lite file**, recoloured to the app's palette (Catppuccin Latte) in `app.css`.
- **The icons are Syncfusion's own**, from the theme's icon font, drawn by `SfIcon` (2026-11-08): a screen state names
  an icon by what it stands for (`AppIcon`), and `AppIcons` picks Syncfusion's for it.
- **The page loads the scripts of only the components the screens use** (`syncfusion.js`, 2026-11-08), and Blazor's
  start waits for them.
- **Syncfusion's telemetry is off** (2026-11-06), before anything touches a Syncfusion type.
- **The licence key is compiled in** from the `SYNCFUSION_LICENSE_KEY` user environment variable, which keeps it out of
  git; `dotnet publish` fails without it.
- **How a component is meant to be used comes from Syncfusion's Blazor docs server** (`.mcp.json`), then its type read
  with `ilspycmd`.

## Consequences

- The screens get grids with sorting, filtering, grouping and Excel export (`SfGrid.ExportToExcelAsync`, on XlsIO),
  dialogs, toasts and a chart without building them, at one vendor's release pace and under its licence.
- Syncfusion's ways have to be learnt and built around, each found in the window and written down in the screen notes:
  an `SfGrid` hooks a row added after it bound and can't unhook it (no grid's rows notify); an `SfTab` given a
  `Height` threw and ended the page; `SfComboBox` reads an enum's first value as empty (so `PickField` binds by index)
  and held each filter back 300 ms (its delay is 0).
- A component new to the screens goes into `syncfusion.js` too, or it fails in the window; `SyncfusionScriptsTests`
  checks the list, and `AppIconsTests` that the theme draws every icon.
- Icons come with the theme on any system, not from a Windows font, which a Linux or macOS host needs.

## Rejected

- **Another vendor's set, or Blazor's own controls and smaller libraries combined**: fewer components in one set, and,
  for a paid one, a cost the owner's Syncfusion licence doesn't have.
- **Segoe Fluent Icons at the WPF app's code points**, as the port brought them: a Windows font, where the app is to
  run on Linux and macOS later (the owner, 2026-11-08).
- **Syncfusion's all-components script**: 6.95 MB, every component Syncfusion makes, run before Blazor started. The
  screens' components need 1.64 MB of it; loading only those halved the web view's JS heap at a start (21.2 to 9.2 MB).
- **The full Fluent 2 theme**: 5.48 MB, with rules for a touch size nothing here uses; the lite file is 4.31 MB.
- **Syncfusion's text boxes where typing follows**, **`SfTreeView`** for the lists and **`SfGrid`'s editing** for typed
  tables: the reasons the app's own components above exist.
