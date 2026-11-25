# 0017. A .NET MAUI Blazor Hybrid window

**Status:** Accepted, 2026-11-07, at the owner's request ("port that .net maui blazor hybrid ui here giving the similar
look and functionality of the current desktop ui"), built beside the WPF app; made the app on 2026-11-08 ("I'm
interested in moving forward with this"), the WPF app's last commit tagged `wpf-final`. Recorded after the fact on
2026-11-25, from the build plan's log, [docs/plan/maui-app.md](../plan/maui-app.md) and PLAN.md, as the open item from
the ADR review of 2026-11-23 asked. The screens' layers since are ADRs 0002 and 0003's; their components
[ADR 0018](0018-syncfusion-blazor.md)'s.

## Context

Until 2026-11-07 the app was a WPF window with Syncfusion's WPF controls over ReactiveUI ViewModels. The sibling repo
`../TinapayanRMS_Web` had begun as a copy of this one (2026-10-10), become a Blazor Web App with shop accounts and a
database of its own, and moved into a .NET MAUI Blazor Hybrid window on 2026-11-07, level with this app's features
that day. Its lower layers were copies of this repo's under the same assembly names.

On 2026-11-08 the owner dropped WPF parity ("we don't need to match whatever the previous WPF app's have. Besides I'm
planning to make this codebase cross platform with Linux and MacOS in the future").

## Decision

- **The app is a .NET MAUI Blazor Hybrid window on Windows**: the screens are Blazor components drawn by WebView2,
  running in the app's process beside the screen states and the rules, with nothing listening on the network.
- **The screens are one Razor class library** (`TinapayanRMS.Components`, `net11.0`), and the window only hosts it
  (`TinapayanRMS.Maui`: the page, the web view, the window, and what only a window can do, behind `IAppWindow`,
  `IFileDialogs` and `IPagePrinter`). A Linux or macOS host would bring its own page and window and draw the same
  library.
- **The web repo's screens were copied, not referenced**: one process loads only one of each assembly name, and its
  copies brought its accounts and database. The two repos went their own ways; this one is where the screens are
  worked on.
- **No shop accounts and Windows only**, the owner's choices, asked: the web repo's accounts and its Mac target went.
- **The window is chromeless** (the owner's, 2026-11-07): the page draws the title bar, and Windows takes the gaps
  between its buttons as the caption.
- **The WPF app is gone** (2026-11-08): `main` is the MAUI app, an install script replaced the WPF copy, and the
  shop's computer runs the MAUI app since 2026-11-14.

## Consequences

- The screens are web pages: CSS for the look, an address per screen and record (so Back and Forward work), printing
  through a print-styled page and the web view's dialog, the Vim keys read in the page (`vim.js`).
- What a web view does its own way had to be found and built around: a redraw for an earlier keystroke overwrote the
  ones typed since (the shared fields keep their own text while focused); WinUI's web view never hands the page its
  drag events (the app's drags follow the pointer); a redraw per change made a pick draw its screen 445 times
  (`RedrawGate`); an `SfGrid` redraws all its rows per change (`TableRows`). [docs/plan/maui-app.md](../plan/maui-app.md)
  has each.
- The window leans on MAUI's internals: its own title bar row is hidden by calling the internal
  `NavigationRootManager.SetTitleBarVisibility`, checked after each MAUI update; the process is ended as the window
  closes, since left to itself it stayed 17 to 22 seconds.
- bUnit has no web view, so focus, typing, drag and drop, printing, the dialogs, the Vim keys and the title bar are
  checked in the window, by the `run-tinapayanrms` driver (ADR 0006), never in CI (ADR 0013).
- The screen states no longer had to suit another UI framework, which let ReactiveUI go (2026-11-16, ADR 0003).
- Cross-platform is open: MAUI has no Linux target, and Mac Catalyst can't hold a window's close open; a spike of a
  second host for the library is an open item before any date for it.

## Rejected

- **Keeping the WPF app beside it.** The owner's first choice, 2026-11-07, to retire it later; it was retired the next
  day, when WPF parity was dropped.
- **Referencing the web repo** rather than copying its screens: the same assembly names, its accounts and its database.
- **A Blazor Web App, served to a browser**: the web repo's form until it moved into the MAUI window itself, on
  2026-11-07, before the port. Served, it needs a server listening and accounts in front of it, where the window
  serves the app's own files to itself.
- **The Mac target**, for now: Windows only, the owner's choice. Cross-platform is planned, through a second host.
