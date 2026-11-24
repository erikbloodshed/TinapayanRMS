# 0013. CI on GitHub, changes straight to main

**Status:** Accepted, 2026-11-25, the owner making the repository public ("so that we can run the github actions
unlimited. ci would run on the github from now on") and choosing "Straight to main" and, for local CI, "Remove it".
Supersedes [ADR 0011](0011-change-control-on-the-developers-pc.md) whole; what still stands of it is restated here.
Withdraws 0011's narrowing of [ADR 0006](0006-testing-strategy.md): the window stays out of CI, as 0006 had it.

## Context

ADR 0011 (2026-11-23) made local CI, `scripts/Invoke-LocalCI.ps1` on the developer's PC, the one check, because GitHub
refused the build workflow's runs on 2026-11-21: the repository was private, the account has no paid plan, and its
Windows minutes had run out. The workflow it replaced, `.github/workflows/build.yml` (2026-11-14 to 2026-11-21, removed
in c4073caa), ran the same three steps on GitHub's Windows runner against a PostgreSQL 18 of its own.

On 2026-11-25 the owner made the repository public. GitHub's standard runners cost nothing for a public repository, so
the reason the workflow went is gone. Local CI meanwhile cost every push about five minutes on the PC (the build from
scratch, one to two; the suite, about three and a half), with the PC's test databases and processor taken meanwhile.

0011 turned down a pull request per change only because "nothing on GitHub" could check it, and a self-hosted runner
because GitHub couldn't require a check of `main` without a paid plan. Both premises have changed; the owner was asked
again how changes reach `main` and kept them going straight there.

0011 also planned a fourth step for local CI: the window's smoke run, the `run-tinapayanrms` driver's `smoke` over the
Debug build on a scratch database. It needs WebView2 and a desktop session, which GitHub's runner hasn't (ADRs 0006
and 0011).

The repository's whole history went public with it. It was checked on 2026-11-25 for what shouldn't be: no Syncfusion
licence key, no connection string's password, no private key or token was ever committed. The passwords the app uses
are in each user's `pgpass.conf`, the keys in user environment variables and the installed Recipe API's own settings.

## Decision

- **GitHub CI is the one check** (`.github/workflows/build.yml`, its steps as they were on 2026-11-21): on GitHub's
  Windows runner, the .NET SDK `global.json` names with the MAUI workload it ships with, EDB's PostgreSQL 18.7 set up as
  the developer's (the `tinapayanrms` role, its password in `pgpass.conf`, the tools in `Program Files\PostgreSQL\18`),
  the whole solution built with warnings as errors, `dotnet ef migrations has-pending-model-changes`, and the whole
  suite, the costing golden master and the performance budgets in it, each step stopping the run when it fails. Of a new
  migration it checks that none is missing and that it applies to data: the golden master's and the benchmarks' tests
  restore an anonymized copy of the shop's data as the app restores a backup, through every migration since the copy
  was taken. Applying it to the developer's own database stays a step on the PC.
- **It runs on every push to `main` and every pull request**, the docs' included, since the suite checks the paths they
  name (only a push of `.claude/` alone runs nothing). Each push to `main` gets a run of its own, never cancelled by the
  next, so each commit has its own answer; a pull request's newer push cancels its older run; and a run can be started
  by hand.
- **Every change goes straight to `main`**, and is checked by the run its push starts. A run that fails is fixed before
  anything else is pushed; a failure that isn't the change's, one of the intermittent ones the build plan's open items
  name, is answered by running the failed jobs again until its open item is done. Before a push, the change is checked by what it reaches (`scripts/Test-Changed.ps1`); the
  whole suite is CI's to run. `main` isn't protected: a ruleset that requires the check refuses a push straight to it.
- **`scripts/Invoke-LocalCI.ps1` goes.** The same steps can still be run by hand (`dotnet build -warnaserror`,
  `dotnet ef migrations has-pending-model-changes`, `dotnet test --solution`), but nothing requires them.
- **One structural change at a time**, as 0011 had it: finished and checked in the window before the next begins, and
  never in a commit with feature work. A structural change is one ADR 0001 says needs a record: a project, a layer, a
  framework or a library the code is built around, a change to a rule another record set, and a new kind of gate
  ([ADR 0010](0010-the-gates-read-method-bodies.md)). It's reviewed before it's committed (`/code-review` of the working
  tree), its findings fixed or answered, and its row in the build plan's log says what the review found.
- **The window's smoke run is a script of its own**, run by hand before an install (the build plan's open items), not a
  step of CI: the window stays out of CI, as ADR 0006 had it. How the screens behave stays with bUnit and the checks by
  hand in the window that 0006 names.
- **Dependabot proposes the updates** (`.github/dependabot.yml`): NuGet packages, the .NET SDK and the workflow's actions,
  weekly on Mondays, grouped so packages that must move together do (Syncfusion; MAUI; EF Core with Npgsql and
  EFCore.NamingConventions; the runtime's Microsoft.Extensions and ASP.NET Core; the test packages), with WiX held below
  6.0 (its licence, 2026-11-15). Each pull request is checked by the workflow and merged on its own, never inside another
  change. One that moves too little of its group is made by hand instead.
- **No prerelease package in a release build**, but one `Directory.Packages.props` names with what it waits for. On
  2026-11-25 there's none.

## Consequences

- One record says how a change reaches `main`. 0011 stays as written, and its status points here.
- A push takes no more of the PC than the tests its change reaches, and every push is checked by the same steps on a
  clean checkout: a file left out of a commit fails the run, where local CI checked the working tree.
- A failure is found after the push, not before it, so `main` can be red until it's fixed: a run took 12 to 20 minutes
  on 2026-11-21 (the runner's PostgreSQL and workload set up, then the build and a smaller suite), against local CI's five.
- The runner differs from the PC: a test that leans on the PC's speed or timing can fail there and not here, as the
  suite's two intermittent failures in the build plan's open items might. The performance budgets (ADR 0008) were
  approved on the PC and run on the runner for the first time with this change: a figure that differs there is
  answered in a change of its own.
- The code and its history are public: the anonymized fixture the golden master and the benchmarks restore, the docs,
  and the shop's name in them. A secret must never reach a commit, as it never has.
- Nothing stands between a structural change and `main` but the review, run by hand. A review left out is left out
  silently, which the log's row is there to show.

## Rejected

- **A pull request for every change, `main` protected by a ruleset that requires the check.** Free on a public
  repository, and `main` would never be red, but each merge would wait for a run; the owner's choice, 2026-11-25.
- **A pull request for each structural change only**, as 0005 had it. The owner kept every change going straight to
  `main`, 2026-11-25.
- **Keeping local CI, required before a push or by choice.** Required, it doubles the check and keeps the wait on every
  push; by choice, it's a second script to keep in step with the workflow. The owner had it removed.
- **The smoke run on GitHub's runner.** It needs WebView2 and a desktop session (ADR 0006).
- **A self-hosted runner on this PC**, as 0011 found: the hosted runner is free now, and it checks a clean checkout.
