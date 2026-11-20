# 0004. Quality gates the build enforces

**Status:** Accepted, 2026-11-20.

## Context

The codebase's rules were written down (CLAUDE.md, PLAN.md, the developer manual) and kept by review. Some were already
checked (`DecimalNumbersTests`, `RoundingModeTests`, CI failing on any warning); the layers, how the screens reach the
database and how big a state class may grow were not. ADR 0002 moves the screens to a new shape over four phases, and
nothing would stop new code being written the old way meanwhile.

## Decision

The build and the tests enforce the rules that can be checked, each starting from where the code stood on 2026-11-20
and only tightening, a **ratchet**: what was over a limit is listed, nothing new may join the list, and a listed count
that goes down must be lowered where it's listed, so it can't creep back up.

- **The layers** (`LayerTests`, Components.Tests' `Architecture/`): each project under `src` references only the layers
  ADR 0002 allows, with Presentation's reference to Infrastructure listed until phase 4 takes it out; a new project
  fails until it's given a place. Domain, ApplicationCore, Pricing and Infrastructure use no UI framework and none of the
  screens' Rx or DynamicData; Presentation uses no UI framework; Domain doesn't use EF Core.
- **How the screens reach the database** (`DataAccessTests`): no type in Presentation or Components holds a repository,
  the unit of work, the DbContext, another port Infrastructure implements, or a service built on those (Pricing's
  `SavedProductReader` and `VersionedSaves`), in a field, its constructor or an `[Inject]` property, except the 34 types
  listed with their counts (121 between them), which ADR 0002's phases bring to none. The window's view state
  (`IViewStateStore`) is a file of the user's, not the database.
- **Constructor size** (`ConstructorDependencyTests`): a screen's state, part or row takes at most six things through
  its constructor, except the 15 listed with their counts.
- **File size** (`FileSizeTests`, from 2026-11-21, ADR 0002's amendment): a file of the screens' states (Presentation) is
  at most 700 lines, except the nine listed with their lengths, each lowered once it's 25 lines shorter.
- **Code metrics** (.editorconfig): a method's cyclomatic complexity over 25 (CA1502), a type coupled to over 95 types or
  a member to over 40 (CA1506), the analyzers' own thresholds, as warnings, which CI's build refuses. The 22 places over
  them on 2026-11-20 carry a `SuppressMessage` saying why: a screen's state until its phase moves it, a composition root
  by design, the rest until they next change. Tests are left out: a test class reaches into whatever its scenarios need.
- **Banned APIs** (`BannedSymbols.txt`, Microsoft.CodeAnalysis.BannedApiAnalyzers, RS0030): `Convert.ToDecimal` and
  `new decimal(...)` from a `double` or `float` (since .NET 11 they keep the binary noise); `Observable.Switch`, but in
  `ScreenState.LoadLatest`, the one place it's let through; `Task.Result`, `Task.Wait` and `Task.WaitAll`. A cast from a
  `double` to a `decimal` is a built-in conversion the analyzer can't see: `DecimalNumbersTests` keeps doubles out of
  the code below the screens instead.

## Consequences

- New code can't be written the old way: a new screen part holding a repository fails the tests, and so does a
  project reference the layers don't allow.
- Each phase of ADR 0002 shows as numbers going down, and the lists say what's left.
- Lowering a listed number is a one-line change in the same commit as the work that lowered it.
- The rules about what a screen's handler may redraw, and when, aren't symbols a build can ban (`InvokeAsync(Action)`
  has seventeen right uses in Components): they stay with review and with `RedrawGate`.

## Rejected

- **ArchUnitNET or NetArchTest.** The rules are about project references and what a type takes or holds, which plain
  reflection reads in a few lines, the way `DecimalNumbersTests` already did. Worth taking up if a rule needs to read
  method bodies across assemblies.
- **Code metric thresholds at today's worst.** They'd let everything below the worst grow up to it. The analyzers' own
  thresholds, with today's exceptions named, hold the line where it means something.
- **A separate architecture test project.** Components.Tests already references every layer but the app and the two
  servers, needs no database, and held the existing guards; the project files are read from disk, so the app and the
  servers are checked too.
