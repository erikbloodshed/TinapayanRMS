# 0008. Benchmarks, startup timing and performance budgets

**Status:** Accepted, 2026-11-21.

## Context

ADR 0002's migration is done, and more refactors will follow, each moving how the screens reach the database and work
out their figures. Nothing in the repository says whether one made the app slower. Every measurement so far was made
for one question and thrown away: file-based `.bench/` apps timing a reader or a screen state's load, startup-hook
probes on the window's UI thread, a node script over the web view's debugging port, rebuilt from notes each time (the
build plan's log keeps some of their numbers, such as a record screen's open holding the UI thread 46 to 71 ms).

What those measurements taught:

- **Timings taken minutes apart on this laptop aren't comparable** (PLAN.md, Development Environment): a run right after
  the test suite was 2 to 4 times slower, and one build's start ranged 2.1 to 6 s. A before and after has to alternate.
- **Counts don't drift**: the SQL commands an operation sends are exact, and what it allocates moves by a few per cent.
  `SqlCommandCounter` (Tests.Common) already holds two reads to theirs.
- **The start is where the user waits**, and it's a fresh process each time: the JIT, the assemblies loading and the
  first queries are all in it, so a warm benchmark can't see it.

## Decision

Two projects under `tests/`, a script, and the golden master's anonymized copy of the shop's data
(`Mcp.Tests/Golden/costing-fixture.backup`) to measure on, restored into databases named `tinapayanrms_bench_*`, never
the developer's.

- **`TinapayanRMS.Benchmarks`** (an exe): each measured operation is a *scenario* (`Scenarios/`), set up once and run
  as often as asked, built from the app's own registrations (`ServiceRegistration.AddTinapayanRMS`), so a refactor that
  changes a constructor changes nothing there. A scenario checks its own result: a screen shows its failures rather
  than throwing them, and a failed read is quick, so a broken scenario would otherwise time its error and look fast.
  - The screens' reads (the use cases' queries, `SavedProductReader`), the Price Simulator's costing, a price save with
    its cost cascade, the record views opened and a product picked (on a one-thread synchronization context, as the
    window's UI thread), and the start: timed warm by **BenchmarkDotNet**, in process (`Suites/`).
  - The start, cold (`startup sample`): one process a sample, doing what `MauiProgram` and the page do as the app opens,
    without the window: the services built, `DatabaseReadiness`, the sign-in panel's load, Raw Materials loaded. Each
    stage's time, and what it took in SQL commands, methods compiled, assemblies and memory.
  - Its commands (`bench`, `startup prepare|sample|drop`, `clean`, `list`) are what the script runs on each side's
    build, an older commit's included: they keep their words.
- **`TinapayanRMS.Benchmarks.Tests`**: every scenario held to its **budget** (`Budgets/budgets.approved.txt`): the SQL
  commands exactly, as ADR 0004's ratchets hold a count (more fails, and so does fewer until the lower one is approved),
  and the memory within 10% over and 20% under. In the suite, so local CI checks them. Approved as the golden master's
  figures are, on the developer's PC (`TINAPAYANRMS_APPROVE_BUDGETS=1`), the diff read before it's kept: a budget is
  raised for a reason the commit says, never to make the test pass.
- **`scripts/Compare-Benchmarks.ps1`**: a base commit (a worktree) against the working tree, both built `-c Bench`,
  each on databases its own build restores, in alternating processes: the benchmarks by rounds, the cold start by pairs,
  and with `-Window` the published app's start in its window (when it showed, when the sign-in panel was ready, how long
  Raw Materials' list took after signing in), off the desktop's edge, its page watched over the web view's debugging
  port. A time is worse when the median of head ÷ base over the pairs is over the threshold (10%) and chance alone would
  show head that much slower 1.5% of the time or less (every round of one side beyond every round of the other, or 9
  starts of 10): a table holds some twenty times, and a looser bar would trip on an unchanged tree. A count is worse by
  one. The restored databases are vacuumed and analyzed before anything is timed: autovacuum analyzing a fresh restore
  mid-run made an unchanged tree look a third slower in three reads, every round, when it was first tried.

## Consequences

- A refactor's effect on speed is a command away, on realistic data, the same each time; and one that adds a query or
  a lot of allocation fails the suite before it's merged.
- A change meant to move a budget is one approval and a diff to read, as the golden master's is.
- The budgets measure the suite's own (Debug) build, alone in their process, with tiered compilation off so that
  optimized recompiles don't move what an operation allocates by when they come. The timings measure `-c Bench`.
- A comparison takes about ten minutes (`-Quick` about three), longer with `-Window`, which also flashes a window on
  the screen at each start: it's run by hand before a merge that could move speed, not on every change.
- `Test-Changed.ps1` runs the budgets on nearly every change, as Benchmarks.Tests references Components.
- Not covered yet: the window's own open, pick and key timings and its UI-thread time, which the startup-hook probe
  and the CDP scripts measured by hand. Wall time includes UI-thread work, as it lies on the path timed.

## Rejected

- **Wall-time limits in the suite.** The test projects run side by side and the laptop's speed swings 2 to 4 times;
  a limit loose enough never to fail by chance would catch nothing.
- **BenchmarkDotNet's own baselines across commits.** It compares jobs in one run, which can't hold two builds of the
  same assemblies; and runs minutes apart aren't comparable here. The script alternates processes instead.
- **BenchmarkDotNet's out-of-process toolchain.** It builds a child project per benchmark, which needs it to know
  .NET 11's moniker, and would restore the databases once per benchmark rather than once a run. The comparison's own
  alternating processes keep the two builds apart.
- **A hand-written timing harness.** BenchmarkDotNet's warm-up, pilot, outlier handling and memory diagnoser are what a
  harness needs, and it's the tool the .NET world reads; the cold start, which BenchmarkDotNet can't take in process,
  is the one part timed by hand.
- **A faster start by publishing self-contained or composite ReadyToRun** (tried 2026-11-06): the user found the start
  no faster in use and the publish too slow and too large. The window's start is measured on the publish the shop runs.
