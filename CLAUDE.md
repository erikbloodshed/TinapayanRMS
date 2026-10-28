# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**`PLAN.md` is the project guide and the source of truth** for the tech stack, domain model, application architecture, build phases and conventions. This file only covers what you need before you open it, and it doesn't repeat PLAN.md. PLAN.md is about 1,150 lines of very long bullets (roughly 150k tokens), so it can't be read in one go. List its sections with `grep -n "^#" PLAN.md`, then read the ones the task touches. Keep PLAN.md up to date when a decision changes.

TinapayanRMS is a WPF desktop app for a bakeshop. It tracks raw material prices and cascades cost changes up through made ingredients and sub-assemblies to each finished product's cost and selling price.

## Commands

Run everything from the repo root. The .NET 10 SDK is pinned in `global.json`, which also sets `test.runner` to Microsoft.Testing.Platform. VSTest-style flags such as `--filter` and `--collect` don't work here.

```sh
dotnet build TinapayanRMS.slnx
dotnet test --solution TinapayanRMS.slnx                                          # all tests
dotnet test --project tests/TinapayanRMS.ApplicationCore.Tests --filter-class "*UnitConversionRulesTests"
dotnet test --project tests/TinapayanRMS.UI.Tests --filter-method "*SomeTestName"
dotnet run --project src/TinapayanRMS.UI                                          # the build needs SYNCFUSION_LICENSE_KEY

dotnet ef migrations add <Name> --project src/TinapayanRMS.Infrastructure
dotnet ef database update --project src/TinapayanRMS.Infrastructure
dotnet ef migrations has-pending-model-changes --project src/TinapayanRMS.Infrastructure
dotnet ef dbcontext optimize --project src/TinapayanRMS.Infrastructure --output-dir CompiledModels --namespace TinapayanRMS.Infrastructure.CompiledModels
```

- **Migrations come only from `dotnet ef migrations add`.** Never write or edit one by hand. `TinapayanRMSDbContextFactory` provides the design-time DbContext.
- **The app runs on a compiled EF Core model** (`Infrastructure/CompiledModels/`, found by EF without a `UseModel` call). Regenerate it with `dotnet ef dbcontext optimize` (above) after any model change, and never edit it by hand. `CompiledModelTests` fail while it's stale.
- **Work counts as done** when the solution builds, all tests pass, and any new migration applies cleanly to the local database. A change users can see (a screen, button, field, rule or cost formula) also updates the user manual, `docs/user-manual.md`, in the same change. A change to anything the developer manual, `docs/developer-manual.md`, describes (the setup, commands, solution layout, architecture, a pattern or convention, or a type or file it names) also updates it in the same change.
- **The build compiles the Syncfusion licence key into the app from the `SYNCFUSION_LICENSE_KEY` user environment variable** (an `AssemblyMetadata` item in the UI project), so an installed copy needs no variable. `App`'s static constructor reads the compiled-in key, falls back to the variable, and throws at startup with neither. `dotnet publish` fails without the variable. A plain build succeeds without it, but that app can't start.
- **The app, the tests and `dotnet ef` need the local PostgreSQL 18 server** (service `postgresql-x64-18`, `localhost:5432`), reached as the `tinapayanrms` role. Its password is in `%APPDATA%\postgresql\pgpass.conf`, which Npgsql, `psql` and `pg_dump` all read, so no connection string carries one. The dev database is `tinapayanrms`. Query it with `& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -U tinapayanrms -d tinapayanrms` (`psql` isn't on the PATH). Every table and column name is snake_case (`raw_materials.cost_per_base_unit`). SQL Server Express and LocalDB are still installed, holding the data from before the move (2026-10-21), but are unused.
- **Build settings are centralized.** `Directory.Build.props` sets the shared compiler settings, the .NET analyzers (`AnalysisLevel` `latest-recommended`, with per-folder exceptions in `.editorconfig`) and the target frameworks (`net10.0` by default; UI and UI.Tests use `$(WindowsTargetFramework)`). `tests/Directory.Build.props` adds the xUnit and assertion packages to every `*.Tests` project. `Directory.Packages.props` turns on Central Package Management, so a `PackageReference` never carries a `Version`: add or bump versions there.
- **Docker isn't used anywhere in this project.**
- **`ilspycmd` (11.0, a global dotnet tool) is installed for decompiling and inspecting .NET assemblies.** Use it instead of writing reflection scripts. For example, `ilspycmd -l c <dll>` lists the classes in an assembly, `ilspycmd -t <Full.Type.Name> <dll>` decompiles one type, and `ilspycmd -m "M:Namespace.Type.Method(System.String)" <dll>` decompiles one member. The Syncfusion assemblies are in the NuGet cache under `~/.nuget/packages/syncfusion.*/<version>/lib/`.
- **`dotnet-stack`, `dotnet-dump` and `dotnet-trace` (global dotnet tools) are installed for diagnosing the running app.** Use them instead of guessing or adding temporary logging. `dotnet-stack report -p <pid>` shows where a frozen UI thread is blocked. `dotnet-dump collect -p <pid>` followed by `dotnet-dump analyze <file>` covers crashes, deadlocks and memory growth. `dotnet-trace collect -p <pid>` covers slowness. Find the PID with `dotnet-trace ps`.

## Architecture in brief

- **Layers:**
  - `Domain` holds the entities and has no dependencies.
  - `ApplicationCore` holds the business and costing rules (static `*Rules` classes), the repository interfaces and the FluentValidation validators.
  - `Infrastructure` holds EF Core (PostgreSQL through Npgsql, snake_case names), the repositories, and the cross-entity services (the cost propagators and `ProductCloner`).
  - `UI` is WPF with Syncfusion and ReactiveUI. The DI host is set up in `CompositionRoot.cs`.
  - **Each folder is its own namespace, and files are grouped by kind.** Only app-startup plumbing and the DbContext pair sit at a project root.
- **Some costs are stored, others are computed on read.**
  - Stored: `RawMaterial.CostPerBaseUnit` and `BufferedCostPerBaseUnit`, and `SubAssembly.TotalWeight` and `TotalCost`. When a price, BOM or line changes, `IngredientCostPropagator` updates the made ingredients in dependency order (using `IngredientCostGraph`), then `SubAssemblyCostPropagator` updates the sub-assemblies, both on the save's own context, before its transaction commits. A computed cost is stored at its column's scale (`RawMaterialRules.StoredCost`), or an unchanged one reads as changed.
  - Computed on read by `ProductRules` and `CostingRules`: product usages, packaging lines, and product cost and price. A composite product's lines bring each held product's recipe cost, which `SavedProductReader` also works out on read.
  - Before changing any cost path, read PLAN.md's "Reactive Costing Cascade" section.
- **`TinapayanRMSDbContext` comes from `SharedTransactionDbContextFactory`**, registered as `IDbContextFactory` and as `IUnitOfWork`. ViewModels are DI singletons, so each operation creates its own short-lived context. Inside `IUnitOfWork.RunAsync`, every context shares one transaction, so its reads must not run side by side, and a repository begins its transaction with `BeginOrJoinTransactionAsync`.
- **A failure never escapes a screen.** Rules throw `BusinessRuleException`. A ViewModel runs an action in `RunSafelyAsync`, a `LoadCommand`'s body in `LoadSafelyAsync`, and a load driven by a selection through `LoadLatest`, never a bare `Observable.Switch(...).Subscribe()`: an error reaching `Switch` ends the subscription for good.
- **ReactiveUI 26 has quirks.** It needs the builder bootstrap in `ReactiveUIBootstrapper.cs`. Its parameterless commands are `ReactiveCommand<RxVoid, RxVoid>`. Import that type with `using RxVoid = ReactiveUI.Primitives.RxVoid;` and never with `using ReactiveUI.Primitives;`, which causes CS0121 ambiguities with `System.Reactive.Linq`. The package also imports `ReactiveUI.Binding` globally, and its `Switch` clashes with Rx's the same way, so write `Observable.Switch(source)` rather than `source.Switch()`.
- **Tests (xUnit v3, AwesomeAssertions) use a real database.**
  - There is one test project per src layer: `ApplicationCore.Tests` (Rules and Validators, with no database), `Infrastructure.Tests` and `UI.Tests`. The last two share their database fixtures through `tests/TinapayanRMS.Tests.Common`, a plain library.
  - Each database-backed project runs against its own database on the PostgreSQL server, `tinapayanrms_test_infrastructure` or `tinapayanrms_test_ui`, never the dev `tinapayanrms` database. The backup tests also make and drop `tinapayanrms_test_backup_*` databases of their own. `dotnet test --solution` runs the projects side by side, so they can't share one. Respawn resets it before each test. Nothing is mocked.
  - Database test classes inherit `PersistenceTestBase` from `Tests.Common`. **Each concrete class must also carry `[Collection(DatabaseCollection.Name)]`.** The attribute is deliberately left off the base class. `DatabaseFixture` and `DatabaseCollection` are defined in each project's own `Fixtures/`, because xUnit only finds a collection definition in the tests' own assembly.
- **Costs are `decimal` at full precision** (`numeric(18,4)` by convention in `ConfigureConventions`). Round only for display and for the final selling price.

## UI work

- **Prefer a Syncfusion control over its native WPF equivalent.** All Syncfusion packages share one version, the `SyncfusionVersion` property in `Directory.Packages.props`, so they upgrade together. The theme is the `Syncfusion.Themes.Windows11Light.WPF` package.
- **To learn how a Syncfusion control is meant to be used**, start with the `syncfusion-wpf-*` skills in `.claude/skills/`. Next, check the local Sample Browser source at `C:\Users\Public\Documents\Syncfusion\WPF\34.2.2\SampleBrowser\` (a v34 install, older than the v35 packages), and grep file contents there, since demo filenames often don't name the control. Only then decompile the assembly with `ilspycmd`.
- **Before launching the app to test it**, close any copy the user already has open. Run the app on the secondary monitor. PLAN.md's Application Architecture section has the UI-automation notes (DPI awareness, which Syncfusion controls need real clicks).
