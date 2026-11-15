---
name: run-tinapayanrms
description: Build, launch, run, drive and screenshot the TinapayanRMS app (its .NET MAUI Blazor Hybrid window), click through its screens (Products, Raw Materials, Sub-Assemblies, Reports, Settings) and check a UI change in the real app without saving to the dev database. Use when asked to run, start, launch, click through, screenshot or verify the app, or to check a screen, the title bar or the Vim keys in its window.
---

# Run TinapayanRMS

A .NET MAUI Blazor Hybrid window (`src/TinapayanRMS.Maui`, its screens in `src/TinapayanRMS.Components`) over the
local PostgreSQL dev database. It's driven by `.claude/skills/run-tinapayanrms/driver.ps1`, which starts the app with
WebView2's remote debugging on (port 9222) and drives its page through `driver.js` (Node's `playwright-core`, installed
on first use into `%LOCALAPPDATA%\TinapayanRMS\dev\playwright`). Clicks, keys and shots go to the page, not to the mouse
or the screen, so nothing the user has open is touched. Captures go to `%TEMP%\tinapayanrms-shots\<name>.png`: read
each one, it's how you see what an action did. Paths here are relative to the repo root.

**The app works on the dev database `tinapayanrms`, the user's real data. Never click Save, Delete, Move, Clone,
Restore or anything else that writes.** Leave an edit with No when the window asks (Revert on Products).

## Prerequisites

The .NET MAUI workload (`maui-windows`), the `SYNCFUSION_LICENSE_KEY` user environment variable (compiled into the
build; without it the window shows Syncfusion's licence notice), Node.js, and PostgreSQL 18 running (see CLAUDE.md).
Close any copy of the app the user has open first: the window opens where the mouse is, and one copy runs per folder.

```bash
powershell -NoProfile -Command "Get-Process TinapayanRMS* -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,Path"
```

A `TinapayanRMS.Maui` from `..\TinapayanRMS_Web` is another repo's app: leave it alone, but it can't share port 9222.

## Build

```bash
dotnet build src/TinapayanRMS.Maui
```

The Satori GC experiment builds with `-p:UseSatoriGC=true` into `bin\Satori\Debug\…`; set `TINAPAYANRMS_SATORI=1` for every `driver.ps1` call to drive that build instead.

## Run (agent path)

One command per call, from the repo root:

```bash
P=.claude/skills/run-tinapayanrms/driver.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File $P launch                       # start (unless running), wait for the first screen, shot "launch"
powershell -NoProfile -ExecutionPolicy Bypass -File $P go products/13 product       # open an address, then shot "product"
powershell -NoProfile -ExecutionPolicy Bypass -File $P click "text=Costing" costing # click what a Playwright selector finds, then shot
powershell -NoProfile -ExecutionPolicy Bypass -File $P click "#rm-name"             # click with no shot
powershell -NoProfile -ExecutionPolicy Bypass -File $P type "Ube" typed             # type into what has the cursor, then shot
powershell -NoProfile -ExecutionPolicy Bypass -File $P press Escape                 # a key: Enter, Escape, Tab, Control+k
powershell -NoProfile -ExecutionPolicy Bypass -File $P eval "document.title"        # run JavaScript in the page, print the result
powershell -NoProfile -ExecutionPolicy Bypass -File $P shot now                     # the page
powershell -NoProfile -ExecutionPolicy Bypass -File $P window frame                 # the whole window, title bar and frame (PrintWindow)
powershell -NoProfile -ExecutionPolicy Bypass -File $P hit 800 16 20 16             # Windows' hit test at window points: 2 caption, 1 client, 12 top edge
powershell -NoProfile -ExecutionPolicy Bypass -File $P smoke                        # every screen and Settings page, then a record of each record screen
powershell -NoProfile -ExecutionPolicy Bypass -File $P close                        # the title bar's Close; says so if the window stays
```

**`smoke` is the check before an install.** It visits every address, opens the first record in each record screen's
list, and fails (exit 1) on the page's error bar, a screen's error panel, a red toast or an error in the page's console
(the favicon request aside). It saves nothing; opening records unfolds their folders in `view-state.json`, as any pick
does. To check the build the shop will run, install it into a trial folder (`scripts/Install-TinapayanRMS.ps1
-InstallFolder <scratch folder> -NoDesktopShortcut -NoStartMenuShortcut`), start that exe with the debugging port as
`launch` does (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`), and set `TINAPAYANRMS_EXE` to its path for every `driver.ps1` call.

Addresses: `raw-materials`, `sub-assemblies`, `products` (`products/<id>` opens one), `reports`, `price-simulator`,
`print`, `settings` (`settings/units-of-measure`, `material-categories`, `product-categories`, `roles`,
`costing-defaults`, `keyboard`, `database`). Useful selectors: `text=<label>`, `button[aria-label='Close']` (the title
bar's buttons are named: Navigation menu, Find, Lock, Back, Forward, Minimize, Maximize, Close), `.screen-tab-link[href='reports']`,
`.tree-row` (a list's rows; folders carry `aria-expanded`, `.tree-expander` opens one), `.e-dialog .e-footer-content
button:has-text('No')` (a dialog's button), and the fields' ids (`eval "[...document.querySelectorAll('input[id]')].map(i => i.id)"`).

To find data worth looking at, query the dev database read-only, e.g. the composite products:

```bash
"/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U tinapayanrms -d tinapayanrms -At -c "SELECT p.id, p.name FROM products p WHERE p.is_composite AND p.deleted_at IS NULL ORDER BY p.name;"
```

Before you finish, close the window and check nothing was saved (the times should predate the session):

```bash
"/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U tinapayanrms -d tinapayanrms -At -c "SELECT max(updated_at) FROM products; SELECT max(updated_at) FROM raw_materials; SELECT max(saved_at) FROM product_versions;"
```

## Run (human path)

`dotnet run --project src/TinapayanRMS.Maui` opens the window on the monitor the mouse is on.

## Test

```bash
dotnet test --solution TinapayanRMS.slnx
```

## Gotchas

- **It starts signed in.** `launch` sets `TINAPAYANRMS_SIGNED_IN=1`, which a Debug build honours: no sign-in panel and
  no auto-lock, since a check can't know the user's password. To check the panel itself, set `TINAPAYANRMS_SIGN_IN=1`
  and run against a copy of the dev database (`ConnectionStrings__Default=Host=localhost;Database=<copy>;Username=tinapayanrms`),
  never the user's: setting a password there would lock them out. Don't click **Lock** on the dev database either way:
  with no password set there, the panel offers to set one. A Release copy (`TINAPAYANRMS_EXE`) always asks for the password.
- **The page's shot leaves out the window's frame and its title bar's caption**; `window` captures them. Shots of the
  page are of the page as drawn, popups and dialogs included.
- **A load takes a moment.** An action's shot waits 1.2 s, but the first visit to a screen after a start (a Debug build
  compiling as it goes) can take longer: Reports' first visit settles in one to two seconds. Shoot again before deciding
  nothing happened, or `eval` for what should be there.
- **The user may minimize the window** when it opens over their work. A minimized web view doesn't draw, so clicks and
  shots wait and time out (`waiting for element to be visible, enabled and stable`), though `eval` still works and the
  page's script and screens still run. Leave it minimized: check the page through `eval`, clicking from script
  (`element.click()`), and close it with `WM_CLOSE` (PowerShell: `(Get-Process TinapayanRMS.Maui).CloseMainWindow()`).
- **`launch` reuses a copy it started**, and refuses one started without the debugging port (a `dotnet run`, or the user's):
  close that first.
- **`close` asks, as the user's Close does.** With unsaved changes the window asks Save, Don't Save (No) or Cancel and
  stays: shot, then click No. Never Yes.
- **The lists remember which folders were open** between runs, in `%LocalAppData%\TinapayanRMS\Maui\view-state.json`. If
  you close a folder, open it again before you finish: it's the user's view.
- **Vim keys**: with them on (the title bar shows NORMAL), letters typed while no field has the cursor are commands.
  Click a field before `type`.

## Troubleshooting

- **`launch` throws "Not built"**: run the build above.
- **`launch` times out**: the app may have failed as it started; read `%LocalAppData%\TinapayanRMS\Maui\logs\`.
- **A click times out**: the selector found nothing showing; `shot` to see the page, `eval` to look for the element. If
  the shot times out too, the window is minimized (see Gotchas).
- **`close` says it's still open**: the window is asking about unsaved changes; `shot`, then click No.
