# 0016. TUnit on Microsoft.Testing.Platform

**Status:** Accepted, 2026-11-06, at the owner's request, "for its speed and its modern C#". Recorded after the fact
on 2026-11-25, from the build plan's log (2026-11-05 to 2026-11-07) and PLAN.md ("Tech Stack", "Development
Environment"), as the open item from the ADR review of 2026-11-23 asked. What each level of the suite tests is
[ADR 0006](0006-testing-strategy.md)'s; this record is what runs it.

## Context

Until 2026-11-06 the tests were xUnit v3. On 2026-11-05 the projects had moved onto Microsoft.Testing.Platform's runner
(`UseMicrosoftTestingPlatformRunner`), so that `scripts/Watch-Tests.ps1` could rerun a class on each save by the
platform's hot reload: on xUnit's own runner the test executable refused the platform's filters. The .NET 11 SDK's
`dotnet test` has no VSTest support of its own.

The suite's time is two chains that run a test at a time whatever the framework (Infrastructure's backup tests, each
migrating a throwaway database, and the UI's off-screen grid tests, as they were), beside the tests that can run at
once.

## Decision

- **Every test project runs on TUnit** (`TUnit.Engine`; `Tests.Common` takes `TUnit.Core`, so it stays a library), on
  Microsoft.Testing.Platform, which `global.json`'s `test.runner` makes `dotnet test`'s runner. VSTest-style flags
  don't work; tests are picked with `--treenode-filter`.
- **AwesomeAssertions for the assertions**, not TUnit's own: no assertion changed in the move. AwesomeAssertions rather
  than FluentAssertions because FluentAssertions 8 and later need a paid licence for a for-profit product, and
  AwesomeAssertions is a same-API, MIT-licensed fork of its last free release.
- **Every test runs in parallel unless a `[NotInParallel]` key holds it apart.** A database collection is a key: each
  database test class carries `[NotInParallel(X)]` and a keyed `[ClassDataSource<DatabaseFixture>]`, one fixture, so
  one database, per key, keys side by side and a key's tests one at a time.
- **`tests/TestRunSettings.cs`, in every test project, caps the tests running at once at the processor count** (TUnit
  runs twice that), and turns off TUnit's HTML and JSON report.
- **`tests/HotReloadReruns.cs` works around TUnit's hot reload reruns**, under hot reload only, until a TUnit release
  stops completing a session's test queue at the end of every run (an open item).

## Consequences

- Measured on the 12-processor PC, the full suite (2,201 tests) as `dotnet test --solution` runs it: xUnit 5m22s and
  5m45s; TUnit at its default of 24 at once 5m43s to 6m35s; TUnit capped at 12, 4m29s to 4m51s. The ApplicationCore
  tests alone ran in 0.84 to 0.88 s against xUnit's 1.0 to 1.1 s.
- Tests are found by a source generator at build time, not by reflection as the run starts.
- A class's own tests run side by side, so a test sharing anything with another (a static, a file, a window, a
  database) needs a key it shares with them.
- The workaround reaches TUnit's internals by name, so each TUnit upgrade checks it still finds them, and that it's
  still needed.
- Mutation testing (Stryker.NET) on TUnit is unchecked (ADR 0006).

## Rejected

- **Staying on xUnit v3.** Slower in the measurement above, and the owner wanted TUnit's modern C#.
- **TUnit at its default parallelism.** Twenty-four tests at once beside the one-at-a-time chains slowed them, so the
  suite was slower than xUnit's; and one run in nine at that load hit a PostgreSQL file error (58P01) never seen at 12.
- **TUnit's own assertions.** Every assertion would have changed for no gain.
- **FluentAssertions 8.** Its licence.
