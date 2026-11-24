# 0011. Change control on the developer's PC

**Status:** Superseded by [ADR 0013](0013-ci-on-github-changes-straight-to-main.md), 2026-11-25: the repository went public, and CI
runs on GitHub again. Accepted, 2026-11-23, the owner choosing "Straight to main after review" and "Add a smoke run to
local CI", and keeping the review's account in the build plan's log. Supersedes [ADR 0005](0005-change-control-and-dependencies.md)
whole, its amendment included; what still stands of it is restated here. Narrows [ADR 0006](0006-testing-strategy.md)
in one alternative it turned down, tests in the window on CI.

## Context

ADR 0005 (2026-11-20) built change control on GitHub's build workflow: a structural change through a pull request the
workflow passed, an everyday change checked by the workflow once it was on `main`, `main`'s protection the owner's to
turn on, and Dependabot's pull requests, the workflow's actions among them, checked by it too. On 2026-11-21 GitHub
refused the workflow's runs, as the account has no paid plan, and the owner had it removed: `scripts/Invoke-LocalCI.ps1`
runs what it ran, on the developer's PC. 0005 took that as an amendment at its foot, so read from the top it still says
that:

- a structural change needs "the build workflow passing on it", and an everyday one is checked by CI "there", on `main`;
- protecting `main` is the owner's to turn on;
- Dependabot watches "the workflow's actions", each update "a pull request the build workflow checks";
- pull requests "cost Actions minutes", followed by "Since 2026-11-21 they cost nothing";
- every change through a pull request is turned down because "CI on `main` catches them soon enough";
- every build imports a prerelease SDK, Satori GC's, which is to be named so or taken out.

ADR 0009 has since ruled out amending an accepted record. The practice moved as well. Pull requests #5 to #24 (2026-11-20
to 2026-11-22) went through as 0005 said, but the structural work of 2026-11-22 reached `main` with none: the gates' holes
and ADR 0009 from local branches, and the code metrics over `.razor` files, the ban on blocking waits and the IL checks
committed on `main` itself (ADR 0010). No prerelease is left: EF Core and Npgsql's provider reached 11.0.0 on 2026-11-21,
and Satori GC was dropped on 2026-11-22.

ADR 0006 turned down end-to-end tests in the window on CI because "the window needs WebView2 and a desktop session".
GitHub's runner had neither; the PC local CI runs on has both. And the `run-tinapayanrms` driver already has a `smoke`
run, the check before an install: it visits every address, opens the first record of each record screen, and fails on
the page's error bar, a screen's error panel, a red toast or an error in the page's console.

## Decision

- **One structural change at a time**, as 0005 had it: finished and checked in the window before the next begins, and
  never in a commit with feature work. A structural change is one ADR 0001 says needs a record: a project, a layer, a
  framework or a library the code is built around, a change to a rule another record set, and a new kind of gate
  ([ADR 0010](0010-the-gates-read-method-bodies.md)).
- **Every change goes straight to `main`**, once local CI passes on the working tree. A structural change is reviewed
  too, before it's committed (`/code-review` of the working tree), its findings fixed or answered, the review reading it
  against CLAUDE.md's rules, which say when a record is needed. Its row in the build plan's log says what the review
  found, so one left out shows where the change is told.
- **Local CI is the one check** (`scripts/Invoke-LocalCI.ps1`): the solution built from scratch with warnings as errors,
  `dotnet ef migrations has-pending-model-changes`, and the whole suite, the costing golden master and the performance
  budgets in it, each step stopping the run when it fails. It checks the working tree, saying how many files aren't
  committed, so it passes for a commit only once they are. Nothing on GitHub checks a push, and `main` isn't protected:
  a ruleset that requires a check needs the check to run on GitHub.
- **Local CI drives the window, in a fourth step**, once it's built (the build plan's open items). After the suite, the
  Debug build just made is started signed in (`TINAPAYANRMS_SIGNED_IN`) on a scratch database restored from the golden
  master's anonymized fixture, its window past the desktop's edge, and the driver's `smoke` run goes through it, failing
  on what `smoke` fails on and on an error the app logs meanwhile. It saves nothing, puts the user's view state back,
  and leaves no database or backup behind. Until it's built, local CI is the three steps above. How the screens behave
  (typing, focus, drag and drop, printing, the dialogs) stays with bUnit and the checks by hand in the window that 0006
  names.
- **Dependabot proposes the updates** (`.github/dependabot.yml`): NuGet packages and the .NET SDK, weekly on Mondays,
  grouped so packages that must move together do (Syncfusion; MAUI; EF Core with Npgsql and EFCore.NamingConventions;
  the runtime's Microsoft.Extensions and ASP.NET Core; the test packages), with WiX held below 6.0 (its licence,
  2026-11-15). Each pull request is checked by local CI on its branch, and merged on its own, never inside another
  change. One that moves too little of its group is made by hand instead, as TUnit 1.87.12 replaced #4, which moved two
  of its packages (2026-11-21).
- **No prerelease package in a release build**, but one `Directory.Packages.props` names with what it waits for. On
  2026-11-23 there's none.

## Consequences

- One record says how a change reaches `main`. 0005 stays as written, and its status points here.
- A failure is found on the PC before the push, by the same checks for every change. What's checked is the working
  tree, not a clean checkout, so a file left out of a commit can pass: the count of files not committed is there to
  catch it.
- Nothing stands between a structural change and `main` but local CI and the review, both run by hand on the PC. A
  review left out is left out silently, which the log's row is there to show.
- With its fourth step, local CI opens the app's window, past the desktop's edge, while it runs, and takes longer by the
  smoke run. It already refuses to start while this repo's app is open, which the smoke run needs closed too.
- A week's dependency updates still arrive as a few pull requests, each checked as any change is.

## Rejected

- **A pull request for each structural change**, as 0005 had it and #5 to #24 did. With nothing on GitHub to check it,
  a pull request holds a merge the PC has already checked and a review that runs on the working tree as well; the
  owner's choice, 2026-11-23.
- **A branch merged locally, without a pull request**, as the gates' holes and ADR 0009 went in on 2026-11-22. A branch
  to keep in step with `main` and merge, for the checks the working tree already gets.
- **A self-hosted GitHub runner on this PC.** The same script on the same PC through a runner service, for a check
  GitHub couldn't require of `main` without a paid plan anyway.
- **A pre-push hook that runs local CI.** Every push would wait for the whole suite, a comment's fix included, and a
  hook is skipped as easily as a script is.
- **Tests of the screens' behaviour in the window, in local CI.** Each would be a script to keep in step with the
  screens, run on a laptop whose speed swings two to four times; the smoke run checks that each address opens and
  draws, and behaviour stays with bUnit and the checks by hand.
- **Renovate, or updating each dependency as it comes out**, as 0005 found: Dependabot is built into GitHub and does
  what's needed, and a week's updates are checked at once.
