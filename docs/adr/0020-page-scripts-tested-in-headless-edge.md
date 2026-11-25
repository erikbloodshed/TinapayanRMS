# 0020. The page scripts are tested in headless Edge

**Status:** Accepted, 2026-11-25, the owner's choice of three ("Playwright .NET + Edge"), on the open item from the
2026-11-18 architecture review: "The page scripts' logic in the suite (`app.js`, `vim.js`), which no automated test
runs". Extends [ADR 0006](0006-testing-strategy.md) with a level of its own.

## Context

The screens' own JavaScript is two files in Components' `wwwroot`. `app.js` (about 1,100 lines) is the browser's side of
the screens. It refuses what a number box can't take before the character lands, and takes the one number out of a
paste or a drop. It selects a box's text as it takes the cursor, unless a click put the caret somewhere. It breaks a
printout into pages and adds the running heads and "Continued on page" rows. It holds the shortcuts (Find's Ctrl+K, the
undo keys), the drags (a table's lines, a list's rows) and the title bar's caption. `vim.js` (about 1,000 lines) is the
Vim keys: their modes, every key in every context, the labels f puts up, the command line and the sum.

None of it ran in a test. bUnit draws the components' markup and raises their C# events; it has no browser, so the
scripts never load there. The window is checked by hand through the `run-tinapayanrms` driver, and before an install by
`scripts/Test-Window.ps1`, which visits the screens but presses none of the keys and pastes nothing. The build plan's
log has bugs that lived only there: a number box's caret selected after Tab (2026-11-08), a drop of "5.5" into "1.25"
making "1.255.5", Escape in a list's name box needing two presses, Find needing two Escapes with the Vim keys on. Each
was found and checked by hand, in the window or in headless Edge from a scratch script, and nothing keeps it fixed.

Almost all of the scripts' logic is about the DOM and its events. It reads where the cursor is, refuses or rewrites an
`InputEvent`, and measures laid-out boxes (the page breaks, a drag's scrolling). A test needs a real engine with layout
and focus, and WebView2 is Edge's engine.

## Decision

- **A test project of its own, `tests/TinapayanRMS.PageScripts.Tests`**, on TUnit like the rest (ADR 0016), runs the
  scripts in **headless Edge through Microsoft.Playwright** (the .NET package). Playwright drives the Edge installed on
  the PC (`Channel = "msedge"`), so no browser is downloaded. Edge comes with Windows and is on GitHub's Windows runner,
  so `dotnet test` runs these tests on the PC and in CI alike.
- **The files under test are the app's own**: the project links `app.js` and `vim.js` from Components' `wwwroot` into
  its output. Each test loads them into a page of its own, in a browser context of its own, over a few lines of markup
  shaped as the components draw it, such as a `.number-field` around an input or a `.print-document` of `.print-sheet`s.
  It doesn't load Blazor, the components or Syncfusion.
- **What the scripts call back into C# is a stand-in in the page**: an object with `invokeMethodAsync` that records
  each call and answers as the component would, such as `HintLabelsFor`'s labels. The Vim keys are turned on with the
  app's own key table and command list as the page gets them, `VimKeymap.ForPage()` and `VimCommandLine.ForPage()`,
  which `VimKeys.razor` sends too. So a key changed in C# is the key tested, which is why the project references
  Components.
- **Keys are pressed and text typed by Playwright's keyboard and mouse**, which make trusted events, as a person's are:
  `vim.js` ignores untrusted ones, and `beforeinput` comes only from real typing. A paste or a drop is a `ClipboardEvent`
  or `DragEvent` made in the page with a `DataTransfer`, since a headless browser has no clipboard to paste from.
- **The browser is started once per test run** and shared. Each test gets a new context and page, so tests run in
  parallel as TUnit runs them.

The levels of ADR 0006 gain one:

| Level | What | How |
|---|---|---|
| Page scripts | `app.js` and `vim.js`: what the browser does before the server hears of it | PageScripts.Tests, in headless Edge through Playwright, over markup shaped as the components draw it |

## Consequences

- The behaviour the window's checks found, and fixed by hand, is pinned: the number boxes' rules, select-on-focus, a
  printout's page breaks, Find's key, the Vim keys' modes and moves.
- A test of a script runs against a page put together for it, not the screen itself. It shows the script does what it
  should with markup shaped as the components draw it, not that a component still draws that shape. bUnit's tests of the
  components' markup and the checks in the window stay as they are.
- The suite needs Edge on the PC that runs it. Windows has it, and so does GitHub's Windows runner; the project fails
  at its first test, saying so, where there's none.
- Microsoft.Playwright adds its driver (Node.js and Playwright's server, about 100 MB) to the project's output and the
  NuGet cache. Its version goes in `Directory.Packages.props` like any other. Each new Playwright drives a newer Edge
  too, so an update is checked against the Edge installed.
- The `run-tinapayanrms` driver keeps its own `playwright-core` from npm, outside the repo. The two don't meet.

## Rejected

- **Node's test runner with jsdom.** It needs a `package.json` and an npm step in CI. jsdom has no layout, so the page
  breaks, a drag's scrolling and anything that measures can't be tested. Its focus and `beforeinput` aren't Edge's,
  and those are where the scripts' bugs were.
- **Playwright from Node (`@playwright/test`), as the driver uses it.** It's the same engine with a second test runner,
  a `package.json` and an npm step beside the suite, and its results don't reach `dotnet test` or `Test-Changed.ps1`.
- **Jint or another JavaScript engine in .NET.** It has no DOM.
- **Testing the scripts through the window, on WebView2 itself.** It needs a desktop session, which CI hasn't
  (ADR 0006, ADR 0013), and a Blazor host. `Test-Window.ps1` checks the screens there by hand before an install.
- **Moving the logic into C#.** What the scripts do has to happen in the browser, before the server hears of a key:
  refusing a character, selecting a box's text, laying out a printout's pages.
