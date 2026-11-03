---
name: run-tinapayanrms
description: Build, launch, run, drive and screenshot the TinapayanRMS WPF desktop app on the secondary monitor, click through its screens (Products, Raw Materials, Sub-Assemblies) and check a UI change in the real app without saving to the dev database. Use when asked to run, start, launch, click through, screenshot or verify the app, or to check that a view's bindings work.
---

# Run TinapayanRMS

A Windows WPF app (`src/TinapayanRMS.UI`) over the local PostgreSQL dev database. It's driven by
`.claude/skills/run-tinapayanrms/driver.ps1`, a PowerShell script that places the window on the secondary monitor,
clicks at window coordinates, types, and captures the window or the whole monitor to `%TEMP%\tinapayanrms-shots\`.
Read each capture: it's how you see what the click did. Paths here are relative to the repo root.

**The app works on the dev database `tinapayanrms`, the user's real data. Never click Save, Delete, Move, Clone or
anything else that writes.** Discard edits with Revert (it re-reads, and asks first).

## Prerequisites

The `SYNCFUSION_LICENSE_KEY` user environment variable (the build compiles it in; without it the app won't start), and
PostgreSQL 18 running (see CLAUDE.md). Close any copy of the app the user has open first:

```bash
powershell -NoProfile -Command "Get-Process TinapayanRMS* -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,MainWindowTitle"
```

`TinapayanRMS.UI` is this app. `TinapayanRMS.Web` is a different project: leave it alone.

## Build

```bash
dotnet build src/TinapayanRMS.UI
```

## Run (agent path)

One command per call, from the repo root:

```bash
P=.claude/skills/run-tinapayanrms/driver.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File $P launch               # start (unless running), maximize on the secondary monitor, shot "launch"
powershell -NoProfile -ExecutionPolicy Bypass -File $P tab Products products # click a main tab by its title, then shot "products"
powershell -NoProfile -ExecutionPolicy Bypass -File $P click 184 207 almond  # real click at window x,y (pixels of a shot), then shot
powershell -NoProfile -ExecutionPolicy Bypass -File $P click 1066 275        # click with no shot
powershell -NoProfile -ExecutionPolicy Bypass -File $P screen dropdown       # capture the whole monitor: popups and dialogs show
powershell -NoProfile -ExecutionPolicy Bypass -File $P click 298 91          # e.g. the Products search box
powershell -NoProfile -ExecutionPolicy Bypass -File $P type "Ube-Cheese" search   # SendKeys into the focused control, then shot
powershell -NoProfile -ExecutionPolicy Bypass -File $P type "^a{BACKSPACE}" cleared
powershell -NoProfile -ExecutionPolicy Bypass -File $P rect                  # the window's screen rectangle
powershell -NoProfile -ExecutionPolicy Bypass -File $P close                 # CloseMainWindow; says so if it's still open
```

Then read the PNG it printed (`%TEMP%\tinapayanrms-shots\<name>.png`). Take coordinates from the latest shot: the
window is maximized at the monitor's origin, so a shot's pixel is the click's coordinate.

To find data worth looking at, query the dev database read-only, e.g. the composite products:

```bash
"/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U tinapayanrms -d tinapayanrms -At -c "SELECT p.name, p.stage, coalesce(c.name,'-') FROM products p LEFT JOIN product_categories c ON c.id = p.category_id WHERE p.is_composite AND p.deleted_at IS NULL ORDER BY p.name;"
```

Before you finish, check nothing was saved (both times should predate the session):

```bash
"/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U tinapayanrms -d tinapayanrms -At -c "SELECT max(updated_at) FROM products; SELECT max(saved_at) FROM product_versions;"
```

## Run (human path)

`dotnet run --project src/TinapayanRMS.UI` opens the window on whichever monitor Windows picks, and holds the terminal
until it's closed.

## Test

```bash
dotnet test --solution TinapayanRMS.slnx
```

## Gotchas

- **Only one copy runs.** A second start brings the open copy to the front and exits, so with the user's published copy
  open, a new build never starts, and `launch` drives the old one: check `Get-Process TinapayanRMS.UI | Select Id,Path`.
  From another folder it shows a message first, which waits for an answer that only `screen` can see.
- **The tests never load a view.** A wrong `{Binding}` path fails silently at runtime (a blank field, a dead button), so
  a ViewModel or XAML change is only checked once a shot shows the field filled.
- **Popups aren't in a window shot.** `shot` uses PrintWindow on MainWindow, which leaves out drop-downs
  (`DropDownButtonAdv` menus, a grid cell's suggestion list) and dialogs (Revert's "Confirm"). Use `screen`.
- **The product, material and sub-assembly trees' rows have no UI Automation names**, so they're clicked by
  coordinates. The app remembers which folders were open between runs, so an old shot's coordinates can point at a
  different row: take a fresh shot first, and if you collapse a folder, open it again before you finish (it's the
  user's view).
- **A click can only take focus.** After typing in the search box, or with a notification showing, the first click on
  the tree or a button may just move focus. Shoot after each click and click again if nothing changed.
- **A `DropDownButtonAdv` (Add Sub-Assembly, New) opens on one click and closes on the next.** Click once, `screen`,
  then click the item.
- **Clicking a grid's column header sorts it.** Harmless, but the rows move.
- **Escape in a grid's add row commits the suggestion instead of cancelling it** (seen in a composite's Products table:
  "Mini" + Escape added "Mini Caramel Cinnamon"). Discard with Revert: it opens a Confirm dialog (`screen` to see it;
  Yes is at about 740,444 on a 1,366-wide window), and the screen says "… was put back as it was last saved."
- **The title bar shows the Vim layer's mode, NORMAL or INSERT.** Only `type` while it says INSERT (a text box has
  focus); in NORMAL, keys are Vim commands.
- **Coordinates are physical pixels.** The driver is per-monitor DPI aware; a DPI-unaware script sees the 125% primary
  monitor as 1,536 × 864 and gets scaled rectangles.
- **Finding MainWindow:** its title is "Tinapayan Festival Bakeshoppe - Recipe & Costing", not "TinapayanRMS", and
  `Process.MainWindowHandle` can be a hidden window; the driver looks for the `HwndWrapper[…]` window with that title.
- **Editing the driver:** PowerShell variable names are case-insensitive, so a local `$b` overwrites the `$B` argument.

## Troubleshooting

- **`launch` throws "not built"** - run the build above.
- **A click landed on the wrong row** - folders opened or closed since the shot you measured; take a new shot.
- **Nothing seems to happen after a click** - it was a focus click (click again), or it opened a popup or dialog that
  only `screen` shows.
- **Revert is greyed out while Save is still enabled** - its Confirm dialog is waiting: `screen`, then click Yes or No.
- **`close` says it's still open** - the app is asking about unsaved changes: `screen` to see it, and don't save.
