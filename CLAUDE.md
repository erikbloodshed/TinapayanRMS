# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**`PLAN.md` is the project guide and the source of truth** for the tech stack, domain model, application architecture, build phases and conventions. This file only covers what you need before you open it, and it doesn't repeat PLAN.md. PLAN.md is about 530 lines of very long bullets (roughly 75k tokens), so it can't be read in one go. List its sections with `grep -n "^#" PLAN.md`, then read the ones the task touches. Keep PLAN.md up to date when a decision changes.

TinapayanRMS is a WPF desktop app for a bakeshop. It tracks raw material prices and cascades cost changes up through made ingredients and sub-assemblies to each finished product's cost and selling price.

## Commands

Run everything from the repo root. The .NET 10 SDK is pinned in `global.json`, which also sets `test.runner` to Microsoft.Testing.Platform. VSTest-style flags such as `--filter` and `--collect` don't work here.

```sh
dotnet build TinapayanRMS.slnx
dotnet test --solution TinapayanRMS.slnx                                          # all tests
dotnet test --project tests/TinapayanRMS.Tests --filter-class "*UnitConversionRulesTests"
dotnet test --project tests/TinapayanRMS.Tests --filter-method "*SomeTestName"
dotnet run --project src/TinapayanRMS.UI                                          # needs SYNCFUSION_LICENSE_KEY

dotnet ef migrations add <Name> --project src/TinapayanRMS.Infrastructure
dotnet ef database update --project src/TinapayanRMS.Infrastructure
dotnet ef migrations has-pending-model-changes --project src/TinapayanRMS.Infrastructure
```

- **Migrations come only from `dotnet ef migrations add`.** Never write or edit one by hand. `TinapayanRMSDbContextFactory` provides the design-time DbContext.
- **Work counts as done** when the solution builds, all tests pass, and any new migration applies cleanly to the local database. A change users can see (a screen, button, field, rule or cost formula) also updates the user manual, `docs/user-manual.md`, in the same change.
- **The app needs the `SYNCFUSION_LICENSE_KEY` user environment variable.** `App`'s static constructor throws at startup without it. The build doesn't need it.
- **The app, the tests and `dotnet ef` need the local SQL Server Express instance** (`.\SQLEXPRESS`, service `MSSQL$SQLEXPRESS`), reached with Windows authentication. Use `sqlcmd -S ".\SQLEXPRESS" -E` to query it. LocalDB is still installed but unused.
- **Build settings are centralized.** `Directory.Build.props` sets the shared compiler settings, the .NET analyzers (`AnalysisLevel` `latest-recommended`, with per-folder exceptions in `.editorconfig`) and the target frameworks (`net10.0` by default; UI and Tests use `$(WindowsTargetFramework)`). `Directory.Packages.props` turns on Central Package Management, so a `PackageReference` never carries a `Version`: add or bump versions there.
- **Docker isn't used anywhere in this project.**
- **`ilspycmd` (11.0, a global dotnet tool) is installed for decompiling and inspecting .NET assemblies.** Use it instead of writing reflection scripts. For example, `ilspycmd -l c <dll>` lists the classes in an assembly, `ilspycmd -t <Full.Type.Name> <dll>` decompiles one type, and `ilspycmd -m "M:Namespace.Type.Method(System.String)" <dll>` decompiles one member. The Syncfusion assemblies are in the NuGet cache under `~/.nuget/packages/syncfusion.*/<version>/lib/`.
- **`dotnet-stack`, `dotnet-dump` and `dotnet-trace` (global dotnet tools) are installed for diagnosing the running app.** Use them instead of guessing or adding temporary logging. `dotnet-stack report -p <pid>` shows where a frozen UI thread is blocked. `dotnet-dump collect -p <pid>` followed by `dotnet-dump analyze <file>` covers crashes, deadlocks and memory growth. `dotnet-trace collect -p <pid>` covers slowness. Find the PID with `dotnet-trace ps`.

## Architecture in brief

- **Layers:**
  - `Domain` holds the entities and has no dependencies.
  - `ApplicationCore` holds the business and costing rules (static `*Rules` classes), the repository interfaces and the FluentValidation validators.
  - `Infrastructure` holds EF Core (SQL Server), the repositories, and the cross-entity services (the cost propagators and `ProductCloner`).
  - `UI` is WPF with Syncfusion and ReactiveUI. The DI host is set up in `CompositionRoot.cs`.
  - **Each folder is its own namespace, and files are grouped by kind.** Only app-startup plumbing and the DbContext pair sit at a project root.
- **Some costs are stored, others are computed on read.**
  - Stored: `RawMaterial.CostPerBaseUnit` and `BufferedCostPerBaseUnit`, and `SubAssembly.TotalWeight` and `TotalCost`. When a price, BOM or line changes, `IngredientCostPropagator` updates the made ingredients in dependency order (using `IngredientCostGraph`), then `SubAssemblyCostPropagator` updates the sub-assemblies.
  - Computed on read by `ProductRules` and `CostingRules`: product usages, packaging lines, and product cost and price.
  - Before changing any cost path, read PLAN.md's "Reactive Costing Cascade" section.
- **`TinapayanRMSDbContext` is registered with `AddDbContextFactory`.** ViewModels are DI singletons, so each operation creates its own short-lived context.
- **ReactiveUI 24 has quirks.** It needs the builder bootstrap in `ReactiveUIBootstrapper.cs`. Its parameterless commands are `ReactiveCommand<RxVoid, RxVoid>`. Import that type with `using RxVoid = ReactiveUI.Primitives.RxVoid;` and never with `using ReactiveUI.Primitives;`, which causes CS0121 ambiguities with `System.Reactive.Linq`.
- **Tests (xUnit v3, AwesomeAssertions) use a real database.**
  - They run against `TinapayanRMS_Test` on SQL Server Express, never the dev `TinapayanRMS` database, and Respawn resets it before each test. Nothing is mocked.
  - Persistence test classes inherit `Fixtures/PersistenceTestBase`. **Each concrete class must also carry `[Collection(DatabaseCollection.Name)]`.** The attribute is deliberately left off the base class.
- **Costs are `decimal` at full precision** (`decimal(18,4)` by convention in `ConfigureConventions`). Round only for display and for the final selling price.

## UI work

- **Prefer a Syncfusion control over its native WPF equivalent.** All Syncfusion packages share one version, the `SyncfusionVersion` property in `Directory.Packages.props`, so they upgrade together. The theme is the `Syncfusion.Themes.Windows11Light.WPF` package.
- **To learn how a Syncfusion control is meant to be used**, start with the `syncfusion-wpf-*` skills in `.claude/skills/`. Next, check the local Sample Browser source at `C:\Users\Public\Documents\Syncfusion\WPF\34.2.2\SampleBrowser\`, and grep file contents there, since demo filenames often don't name the control. Only then decompile the assembly with `ilspycmd`.
- **Before launching the app to test it**, close any copy the user already has open. Run the app on the secondary monitor. PLAN.md's Application Architecture section has the UI-automation notes (DPI awareness, which Syncfusion controls need real clicks).
