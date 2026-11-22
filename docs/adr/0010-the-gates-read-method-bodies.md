# 0010. The gates read method bodies, the app's and the servers' too

**Status:** Accepted, 2026-11-23, the owner keeping its rule for when a gate needs a record. Supersedes
[ADR 0004](0004-quality-gates.md) in what it says the gates check (its banned APIs, its doubles, its data access, the
code metrics' reach) and in its rejection of ArchUnitNET and NetArchTest; 0004's ratchet, and why the gates are
ratchets, stand. Records what four changes of 2026-11-22 built without a record.

## Context

ADR 0004 set the gates on 2026-11-20 and said where each one stopped. A review on 2026-11-22 planted on 75108f8 one of
each thing a gate was meant to refuse, and these got through:

- `BannedSymbols.txt` named `Task.Wait` and `Task.WaitAll` without parameters, which matched 1 of their 13 overloads;
- a `double` cast to a `decimal`: 0004 calls a cast "a built-in conversion the analyzer can't see", and
  `DecimalNumbersTests` read what the types below the screens hold, take and give, not what their methods do;
- `DecimalNumbersTests` and `RoundingModeTests` didn't read UseCases;
- `DataAccessTests` read what a screen's type holds, not what its methods do: a method could resolve a repository from
  the services, or make a context, holding neither;
- `ProductForm`'s CA1506 suppression, on the class, silenced the check for each of its members.

The same day closed those, and the three blind spots the review left for an open item: the code metrics skipped a
`.razor` file's code, which the analyzers see only in the files Razor generates and pass over as generated; an awaiter's
`GetResult` and a `ValueTask`'s `Result` blocked unbanned; and the IL checks couldn't see the app and the two servers,
which Components.Tests didn't reference, a service asked for by a `Type` in a variable, or a `double` boxed before
`Convert.ToDecimal(object)`. Four changes in all, each asked for by the owner (the build plan's log, 2026-11-22: "The
quality gates' holes closed", "CA1502 and CA1506 read a `.razor` file's code", "Every blocking wait on async work is
banned", "The IL checks read the app and the two servers…").

They changed rules 0004 set, which ADR 0001 says is a new record's, and none was written. So 0004 still says a cast
can't be seen, names three ways to block on async work where the list now holds 23 entries, and turns down ArchUnitNET
and NetArchTest until "a rule needs to read method bodies across assemblies". `RoundingModeTests` read them already,
with a reader of its own; three checks now do, through one reader the tests share, and the app's and the servers'
assemblies are read from their files.

## Decision

The gates are as below, each a ratchet as 0004 has it.

- **Banned APIs** (`BannedSymbols.txt`, Microsoft.CodeAnalysis.BannedApiAnalyzers, RS0030), each with its reason in the
  file:
  - a `decimal` made from a `double` or a `float` (`Convert.ToDecimal`, `new decimal(...)`), and from what may be one
    boxed (`Convert.ToDecimal(object)`, with a format provider or without, `IConvertible.ToDecimal`, each
    `Convert.ChangeType`): a box hides that it held a double, which keeps its binary noise as a decimal all the same;
  - `Observable.Switch`, but in `ScreenState.LoadLatest`;
  - every way to block on a `Task` or a `ValueTask`: `Task<T>.Result`, `ValueTask<T>.Result`, each of the 13 overloads
    of `Task.Wait` and `Task.WaitAll`, and each awaiter's `GetResult` (a `Task`'s or a `ValueTask`'s, generic or not,
    configured or not: eight). Not listed yet, and used nowhere: `Task.WaitAny`, `IAsyncEnumerable<T>`'s
    `ToBlockingEnumerable`, and Rx's blocking operators (`Wait`, `First`, `Last`, `Single` and their kin), each an
    entry that closes a hole in this ban (below), in the build plan's open items.

  Where one is let through, a `#pragma` says why, approved by the owner: `ScreenState.LoadLatest`'s `Switch`;
  `BackgroundWork.Stop`'s wait, as the app ends, which can't be awaited; `DataUpgrades.Run`, EF Core's synchronous
  seeding hook, which only `dotnet ef database update` calls. WinUI's XAML compiler writes a `Convert.ChangeType` into
  the app's `obj/**/XamlTypeInfo.g.cs`, which nobody can change: `.editorconfig` turns RS0030 off in that file alone. An
  entry names one overload, and one written without parameters names the overload that takes none: each is listed, and
  a call planted by hand shows a new entry fires before it's committed.

- **Method bodies are read from their IL** (Components.Tests' `Architecture/`). `IlReader` reads a referenced assembly's
  methods by reflection, and fails on an operand it can't resolve. `UnreferencedAssemblies` reads the app
  (`TinapayanRMS.Maui`, Windows only), the Recipe API and the MCP server from the files their builds make, with
  `System.Reflection.Metadata`, without loading them: Components.Tests builds the three without referencing them
  (`ReferenceOutputAssembly="false"`) and names their files in its own `AssemblyMetadata`.
  - `DecimalNumbersTests`: nothing turns a `double`, `float` or `Half` into a `decimal` (a cast's operator,
    `decimal.CreateChecked` and its kin) in any of the app's assemblies: the layers, the screens, the app and the two
    servers. In the layers below Components, the screens' states (Presentation) and now UseCases included, nothing
    holds, takes or gives a double or a float, but what `MeantForDoubles` names, as before.
  - `RoundingModeTests`: every rounding in those same layers, UseCases now among them, names its midpoint mode.
  - `DataAccessTests`: no method of the screens' (Presentation and Components) but the composition root's
    (`ServiceRegistration`) reaches data access without holding it, resolved from the services, made, cast to or
    called; and none asks the services for anything, or makes anything with them, by a `Type`, whose IL shows only a
    variable, where a type argument or an `[Inject]` property can be read. What a screen may not hold, nor reach, now
    includes a `DbContext`, ADO.NET's connections, commands and data sources, and anything of a database library's but
    its exceptions.

  Each of the three carries planted cases it must find (`DataAccessTests.Planted`, the casts in `DecimalNumbersTests`,
  the rounding in `RoundingModeTests`), so a reader that stops seeing fails.

- **The code metrics read a `.razor` file's code.** `.editorconfig` marks the files Razor generates from it
  (`*_razor.g.cs`, and `*_razor.decl.g.cs` for the `@code` members) `generated_code = false`, and the root
  `.globalconfig` gives them CA1502 and CA1506 as warnings, since a generated file takes no severity from
  `.editorconfig`'s sections. It turns off there the six rules that reading also woke on what Razor writes (CA1000,
  CA1305, CA1816, CA1822, CA1826, CA1861), which `.editorconfig` turns back on for the files on disk. A suppression on a
  type silences its members too, so a type's own coupling is excused by a `#pragma` on its declaration, as
  `ProductForm`'s is, never an attribute on the class.

- **The layers are `LayerTests.MayReference`'s table**, which lists every reference each project may make. ADR 0002's
  diagram draws the main line through them; the table also lets Presentation reference Domain, ApplicationCore and
  Pricing, Infrastructure Domain, Components Infrastructure (the composition root, as 0002 says in words), and the two
  servers Infrastructure and Pricing.

- **The ratchet, as built.** What's over a limit is listed with its count, and the count is lowered in the commit that
  lowers it. `FileSizeTests` lowers a file's length once the file is 25 lines under it (`Slack`), so an edit's few lines
  don't churn the list: the listed number only goes down, while the file may grow back within the slack. On 2026-11-23
  `DataAccessTests.StillReaching` and `LayerTests.StillThere` are empty, `ConstructorDependencyTests.StillOver` lists 3
  types and `FileSizeTests.StillOver` 9 files.

- **What needs a record.** A new kind of refusal (a new family of APIs banned, a new check, a gate reaching code it
  didn't read) changes this record's rule and is a record's, as ADR 0001 says. An entry that closes a hole in a ban
  already made (another overload), a planted case, or a lowered number needs none.

## Consequences

- 0004 still says why the gates are ratchets and where they started; this record says what they check. Where the two
  differ (the banned APIs, the casts, data access, the code metrics' reach, the libraries turned down), this one holds.
- A double turned into a decimal is refused wherever the app's code is, the screens and the window included, and so is a
  wait that blocks on a `Task` or a `ValueTask`. `NumberField`, the one user of `Convert.ChangeType`, reads its value by its type.
- Components.Tests builds the app and the two servers first (an up-to-date build of the app took 13 s), so a change to
  any of them runs it.
- The IL doesn't show everything: an `await` isn't a call to `GetResult` the analyzer sees, a `Type` in a variable could
  be anything (so a service asked for by one is refused outright), and what's reached by reflection isn't read. Those
  stay with review.
- The readers are the tests' own code, some 230 lines, and follow what the compiler emits: with runtime async an async
  method's body stays in the method, which `DataAccessTests.Planted.ResolvedAfterAnAwait` holds the reader to.

## Rejected

- **ArchUnitNET or NetArchTest, now that method bodies are read**, the condition 0004 set for taking one up. The checks
  ask a few things of an operand (a conversion's parameter types, whether a call takes a `Type`, what a resolved
  service's type argument is) about types the tests already reason over by reflection. The runtime's own reader does it
  in some 230 lines, with no package for Dependabot to move and no second model of the types to keep in step with
  reflection's.
- **Mono.Cecil to read the IL.** The same, for a package where the runtime has a reader.
- **An analyzer of our own**, a Roslyn project giving each finding its line as it's typed. A project to build, test and
  load into every build, for what the suite already refuses; worth it if a rule needs to be seen in the editor.
- **Banned symbols alone.** A cast is a built-in conversion and a `Type` in a variable names nothing: neither is a symbol
  the analyzer can be given.
