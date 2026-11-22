# 0005. Change control and dependencies

**Status:** Accepted, 2026-11-20. Amended 2026-11-21: CI runs on the developer's PC, not on GitHub (below). Superseded
by [ADR 0011](0011-change-control-on-the-developers-pc.md), which restates what stands.

## Context

The app runs the shop's costing, and every change went straight onto `main` and was pushed: 151 commits in the twelve
days to 2026-11-20, with features and restructuring mixed. CI (build, migration check, every test on PostgreSQL 18)
runs after the push, so a failure is found once it's already on `main`. Dependencies were updated by hand as they came
out, sometimes days apart (TUnit 1.85.0 on 2026-11-16, 1.86.0 on 11-17, 1.86.6 on 11-18), and two release candidates
run in the installed app: `Npgsql.EntityFrameworkCore.PostgreSQL` 11.0.0-rc.3 and EF Core 11.0.0-rc.3, held there until
Npgsql's provider has an 11.0.0 release.

## Decision

- **One structural change at a time.** A change that ADR 0001 says needs a record (a project, a layer, a framework, a
  rule another record set) starts from an accepted record, is finished and checked in the window before the next
  begins, and is never mixed with feature work in a commit. ADR 0002's phases are such changes, one after another.
- **Structural changes go through a pull request**: a short-lived branch, the build workflow passing on it, and a
  review (`/code-review`) before it's merged. Everyday changes inside the rules may still go straight to `main`, and CI
  checks them there.
- **Protecting `main`** (a ruleset requiring the build workflow) is the owner's to turn on: on a private repository it
  needs a paid GitHub plan, and it ends pushing straight to `main` for every change, the everyday ones included.
- **Dependencies are updated by Dependabot** (`.github/dependabot.yml`): weekly, on Mondays, grouped so packages that
  must move together do (Syncfusion; MAUI; EF Core with Npgsql and EFCore.NamingConventions; the runtime's
  Microsoft.Extensions and ASP.NET Core; the test packages), the .NET SDK and the workflow's actions too, each as a pull
  request the build workflow checks. WiX stays below 6.0 (its licence, 2026-11-15). An update is merged on its own, never
  inside another change.
- **No prerelease package in a release build**, but one named in Directory.Packages.props with what it waits for. On
  2026-11-20 those were EF Core and Npgsql's provider at 11.0.0-rc.3, which moved to 11.0.0 on 2026-11-21; and the
  Maui project imports the Satori GC experiment's SDK (`PublishWithSatoriGC`, `11.0.1-satori.…`) in every build, off
  unless asked for, which is to be named so or taken out (the build plan's open items).

### Amendment, 2026-11-21: CI on the developer's PC

GitHub refused to start the build workflow's runs on PR #21: the account has no paid plan, and the private repository's
Windows minutes (counted double) had run out. Even when they ran, every merge waited on a run of its own, on top of the same
checks already run on the PC. The owner's call: "Remove the Github CI workflow, I don't have a paid account on github, we
could locally CI the codebase."

- **`.github/workflows/build.yml` is gone, and `scripts/Invoke-LocalCI.ps1` runs what it ran**, in the same order: the
  whole solution built from scratch with warnings as errors, `dotnet ef migrations has-pending-model-changes`, and the
  whole suite, on the developer's PC and its PostgreSQL 18.
- **A pull request's check is local CI passing on its branch**, with `/code-review`, before it's merged; an everyday
  change runs it before it's pushed. Nothing on GitHub checks a push any more.
- **Protecting `main` is dropped**: a ruleset requiring a check needs the check to run on GitHub.
- **Dependabot stays** (it costs nothing): its pull requests are checked by local CI like any other, and it no longer
  watches the workflow's actions, since there are none.

The trade: a clean checkout of the commit is no longer what's checked, the working tree is, so a file left out of a
commit can pass locally; the script says how many files aren't committed, and passes for the commit only once they are.

## Consequences

- A failure in a structural change is found on its branch, not on `main`.
- A week's dependency updates arrive as a few checked pull requests rather than as hand edits between other work.
- Pull requests and their runs cost Actions minutes, Windows' counting double: weekly grouping keeps that to a handful.
  Since 2026-11-21 they cost nothing, as no CI runs on GitHub (the amendment).

## Rejected

- **Every change through a pull request.** Most changes in a one-developer repository are small and inside the rules;
  CI on `main` catches them soon enough, and the gate is kept for what can break the structure.
- **Renovate.** More configurable, but Dependabot is built into GitHub, needs no app installed, and does what's needed.
- **Updating dependencies as they're released.** Each update is a change to check; a weekly batch is checked once.
