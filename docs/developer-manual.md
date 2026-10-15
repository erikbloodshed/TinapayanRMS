# TinapayanRMS Developer Manual

TinapayanRMS is the recipe and costing app of Tinapayan Festival Bakeshoppe. It's a Windows desktop app built with WPF, Syncfusion and ReactiveUI on .NET 10, and it keeps its data in SQL Server through EF Core and Dapper. The shop records what its raw materials cost. The app works out what every recipe, product and selling price comes to, and when a price changes it carries the change up to every product that uses it.

This manual is for developers who build, maintain or extend the app. It explains how the code is organised, which patterns it follows and why, and what you need to know and install before you change it. It assumes you know C#, but not this codebase.

> **PLAN.md is the source of truth.** This manual describes the code as it is and points into [PLAN.md](../PLAN.md) for the reasons and history behind each decision. Where the two disagree, the code and PLAN.md win: fix this manual.

| Document | Written for | What it covers |
|---|---|---|
| [docs/user-manual.md](user-manual.md) | The shop's staff | Every screen, and how costs are worked out, as a user sees them |
| docs/developer-manual.md (this file) | Developers | How the code is built, and how to change it safely |
| [PLAN.md](../PLAN.md) | Developers and AI agents | Every design decision with its reason and date: tech stack, domain model, architecture, costing cascade, build phases and conventions |
| [CLAUDE.md](../CLAUDE.md) | AI coding agents | The commands and rules an agent needs before it changes the code |

PLAN.md is long: about 1,150 lines of very long bullets. List its sections with `grep -n "^#" PLAN.md` and read the ones your change touches.

## Contents

1. [The application](#1-the-application)
2. [Prerequisites](#2-prerequisites)
3. [Getting started](#3-getting-started)
4. [The solution](#4-the-solution)
5. [Architecture](#5-architecture)
6. [The domain model](#6-the-domain-model)
7. [Costing](#7-costing)
8. [Data access](#8-data-access)
9. [The UI layer](#9-the-ui-layer)
10. [Testing](#10-testing)
11. [Conventions](#11-conventions)
12. [Common changes, step by step](#12-common-changes-step-by-step)
13. [Diagnostics and troubleshooting](#13-diagnostics-and-troubleshooting)
14. [Further reading](#14-further-reading)

---

## 1. The application

### What it does

- **Raw materials.** Ingredients and packaging materials. Each has a base unit, any number of alternative units (a bag of flour, a pack of boxes) and a cost. A purchased material is priced from its brand prices. An ingredient can also be *made* from other ingredients (a bill of materials), or *linked* to another ingredient as a share of its cost (egg white from egg).
- **Sub-assemblies.** In-house recipes such as a dough, a filling or a topping: ingredient lines in grams, with a stored batch weight and batch cost. A sub-assembly in the Packaging role is a *packaging set* instead, such as a gift box with its ribbon.
- **Products.** A recipe of sub-assemblies, each cut into pieces of a set size, plus packaging and costing percentages. The app works out the cost and a cost-plus selling price per sellable unit. A *composite* product holds other products, such as a gift box of assorted breads.
- **Reports, printing and scaling.** The Product Cost Summary and Raw Materials Price Summary reports, printed recipes and costings, and a recipe scaled to any number of units.

It's a single-user desktop app. There's no server, no login and no network service of its own. The database is a local SQL Server Express instance, reached with Windows authentication.

### Screens

| Screen | View | ViewModel |
|---|---|---|
| Raw Materials | `RawMaterialsView`, with its cards in `Views/RawMaterials/` | `RawMaterialsViewModel` |
| Sub-Assemblies | `SubAssembliesView`, with its list in `Views/SubAssemblies/` | `SubAssembliesViewModel` |
| Products | `ProductsView`, with its cards in `Views/Products/` | `ProductsViewModel` |
| Reports | `ReportsView` | `ReportsViewModel` |
| Production Plan | `ProductionPlanView` | `ProductionPlanViewModel` |
| What-If Prices | `WhatIfView` | `WhatIfViewModel` |
| Print (opened from Products) | `ProductPrintView` | `PrintTabViewModel` |
| Settings dialog: Units of Measure, Categories, Product Categories, Roles, Costing Defaults, Keyboard | `SettingsWindow`, and one view per tab | `UnitsOfMeasureViewModel`, `CategoriesViewModel`, `ProductCategoriesViewModel`, `SubAssemblyRolesViewModel`, `CostingDefaultsViewModel`, `KeyboardSettingsViewModel` |

The [user manual](user-manual.md) describes every screen. Read its [Key ideas](user-manual.md#1-key-ideas) chapter before you read the code, because the code uses the same words.

### Glossary

| Term | In code | Meaning |
|---|---|---|
| Raw material | `RawMaterial` | Anything bought or made to produce a product. Its `Type` is `Ingredient` or `Packaging`, and it's fixed once saved. |
| Base unit | `RawMaterial.BaseUnitId` | The unit a material's quantities and costs are kept in. It's always the gram for an ingredient (`BaseUnitId` is null) and is chosen per packaging material. |
| Alternative unit | `RawMaterialUnit` | A multiple of the base unit, such as a bag of 25,000 g. Standard units, such as the kilogram and the inch, have fixed multipliers (`StandardUnitRules`). |
| Source | `RawMaterialSource` | Where an ingredient's cost comes from: `Purchased` (brand prices), `Made` (a bill of materials) or `Linked` (a share of another ingredient's cost). Packaging is always purchased. Fixed once saved. |
| Brand price | `RawMaterialBrandPrice` | What one brand of a purchased material costs now, in one of its units. The material is costed at its dearest brand per base unit. |
| Price history | `RawMaterialPriceHistory` | A log of the prices a material was costed at. Deleting an entry reprices nothing. |
| Bill of materials (BOM) | `IngredientBomLine` | A made ingredient's components, in grams. BOMs nest to any depth. `WeightLossPercent` shrinks the output. |
| Cost buffer, buffered cost | `CostBufferPercent`, `BufferedCostPerBaseUnit` | A cushion on top of the true cost (`CostPerBaseUnit`). Everything downstream is costed at the buffered cost. |
| Sub-assembly | `SubAssembly` | A recipe of ingredient lines (`SubAssemblyLine`), or a packaging set of packaging lines (`SubAssemblyPackagingLine`). It stores its `TotalWeight` and `TotalCost`. |
| Role | `SubAssemblyRole` | What a sub-assembly is in a recipe (Dough, Filling, …). Roles set the order of a product's recipe tabs. `IsPackaging` marks the role packaging sets go in. `IsProductOwned` makes a section made in a product that product's own. |
| Standalone or own | `SubAssembly.OwnerProductId` | A standalone sub-assembly (null owner) lives on the Sub-Assemblies screen, and products link it. An own sub-assembly belongs to one product and is edited in that product's recipe. |
| Usage | `ProductSubAssemblyUsage` | One sub-assembly in a product's recipe, with the product's own piece size and shrinkage. The piece size is either a weight per piece or a yield: one is typed and the other is worked out. |
| Add-in | `UsageAddInLine` | A product's own extra ingredient on top of a sub-assembly it links. |
| Packaging line, packaging link | `PackagingLine`, `PackagingLink` | A packaging material used by one sellable unit; or a link that reads a product's packaging from another product or from a packaging set. |
| Quantity Required | `Product.QuantityRequired` | Pieces per sellable unit: 8 for a Box of 8. A recipe is written per piece, and costing is per sellable unit. |
| Composite product | `Product.IsComposite`, `ProductComponent` | A product made of other products, plus its own packaging and sections in product-owned roles. |
| Costing parameters, costing defaults | `CostingParameters`, `CostingDefaults` | A product's labor, overhead, administrative, markup and VAT percentages; and the values a new product starts with (its stage among them), with the shrinkage a new Recipe Structure section and the cost buffer a new raw material start with. |
| Stage | `ProductStage` | Research and Development, or Production: the Products tree's two top folders. |
| Baker's percentage | `SubAssemblyLine.IsFormulaBase`, `AuthoredBakersPercent` | A line's weight as a percentage of the total weight of the lines ticked as the formula's base. |

---

## 2. Prerequisites

### Knowledge

You'll be productive sooner if you're comfortable with the following.

| Area | What to know | Where it's used |
|---|---|---|
| C# 14 and .NET 10 | Primary constructors, records and `record struct`, `required` members, collection expressions, pattern matching, nullable reference types, `async`/`await` | Everywhere |
| WPF and XAML | Data binding and `DataContext` inheritance, dependency and attached properties, resources, styles and templates, routed events, the `Dispatcher` and STA threads | `src/TinapayanRMS.UI` |
| MVVM with ReactiveUI 26 and Rx | `ReactiveObject`, `RaiseAndSetIfChanged`, `WhenAnyValue`, `ToProperty` (`ObservableAsPropertyHelper`), `ReactiveCommand`, `Interaction`, `Subject`, Rx operators | `ViewModels/` |
| DynamicData | `SourceCache<T, TKey>`, `Connect().SortAndBind(...)` | List ViewModels |
| EF Core 10 | `DbContext`, `IEntityTypeConfiguration<T>`, migrations, change tracking, transactions, `ExecuteDeleteAsync` | Writes and the schema, in `Infrastructure` |
| Dapper and T-SQL | Multi-mapped joins (`splitOn`), CTEs, `STRING_SPLIT` | Reads, in `Infrastructure` |
| SQL Server | Named instances, Windows authentication, `sqlcmd` | Setup, tests, debugging |
| Syncfusion Essential Studio for WPF | `SfDataGrid`, `SfTreeView`, `SfTreeGrid`, `ComboBoxAdv`, `TabControlExt`, `ButtonAdv`, `DoubleTextBox` and its kin, `SfNavigationDrawer`, `ChromelessWindow`, `SfSkinManager`, `PrintPreview` | Every view |
| FluentValidation | `AbstractValidator<T>` | `ApplicationCore/Validators` |
| .NET Generic Host and DI | `HostBuilder`, service lifetimes | `CompositionRoot.cs` |
| Serilog | The static `Log.Logger`, `ForContext` | Logging |
| xUnit v3 on Microsoft.Testing.Platform | Facts, theories, fixtures, collections | `tests/` |
| AwesomeAssertions | `Should()` assertions. It's a fork of FluentAssertions 7, with the same API. | `tests/` |
| Respawn | Resetting a database between tests | The test fixtures |
| MSBuild | `Directory.Build.props`, Central Package Management | The build |
| The domain | Recipe costing in a bakery: bills of materials, yield, shrinkage, baker's percentage, cost-plus pricing | Read the user manual's [Key ideas](user-manual.md#1-key-ideas) and [How costs are worked out](user-manual.md#12-how-costs-are-worked-out) |

### Software

| Tool | Version | Notes |
|---|---|---|
| Windows | 10 or 11 | WPF only runs on Windows. Icons use Segoe Fluent Icons (Windows 11), and fall back to Segoe MDL2 Assets on Windows 10. |
| .NET SDK | 10.0.401, or a later 10.0 feature band | Pinned in [global.json](../global.json) with `rollForward: latestFeature`. |
| SQL Server Express | Instance `.\SQLEXPRESS` (service `MSSQL$SQLEXPRESS`) | Reached with Windows authentication. Your Windows account must be able to create databases, because the tests create their own. `sqlcmd` is useful. LocalDB isn't used. |
| `dotnet-ef` | 10.0.x, matching EF Core 10.0.12 | `dotnet tool install --global dotnet-ef --version 10.0.12` |
| Syncfusion licence key | Essential Studio 35.x | Set as the `SYNCFUSION_LICENSE_KEY` user environment variable. The app won't start without it, and the UI tests that build Syncfusion controls are skipped without it. The build doesn't need it. A key covers one major version, so moving `SyncfusionVersion` to a new major version needs a new key. A program started before the variable changed, such as the IDE and its terminals, keeps the old value until it's restarted. |
| An IDE | Any with .NET 10 support | Visual Studio, VS Code with the C# Dev Kit, or Rider. Git ignores `.vs/`, `.vscode/` and `.idea/`. |
| Optional tools | | `ilspycmd` (decompiles Syncfusion's assemblies), and `dotnet-stack`, `dotnet-dump` and `dotnet-trace` (diagnose the running app). Syncfusion's Essential Studio installer puts the demo source of every control in `C:\Users\Public\Documents\Syncfusion\WPF\<version>\SampleBrowser\`. |

Docker isn't used anywhere in this project.

---

## 3. Getting started

### First-time setup

Run every command from the repository root.

1. **Check the SDK.** `dotnet --version` should print 10.0.401 or later.
2. **Check SQL Server Express:**
   ```powershell
   sqlcmd -S ".\SQLEXPRESS" -E -Q "SELECT @@VERSION"
   ```
3. **Set the Syncfusion licence key** as a user environment variable, then restart your terminal and IDE so they see it:
   ```powershell
   [Environment]::SetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", "<your key>", "User")
   ```
4. **Install the EF Core tools:** `dotnet tool install --global dotnet-ef --version 10.0.12`.
5. **Build:** `dotnet build TinapayanRMS.slnx`.
6. **Create the database.** This creates `TinapayanRMS` on `.\SQLEXPRESS` and applies every migration, including the rows the migrations seed, such as the starting sub-assembly roles:
   ```powershell
   dotnet ef database update --project src/TinapayanRMS.Infrastructure
   ```
   SQL Server Express turns `AUTO_CLOSE` on for the databases it creates. That makes the first read after a pause slow, so turn it off:
   ```powershell
   sqlcmd -S ".\SQLEXPRESS" -E -Q "ALTER DATABASE TinapayanRMS SET AUTO_CLOSE OFF"
   ```
7. **Run the app:** `dotnet run --project src/TinapayanRMS.UI`. With an empty database, follow the user manual's [First-time setup](user-manual.md#3-first-time-setup).
8. **Run the tests:** `dotnet test --solution TinapayanRMS.slnx`. The first run creates the test databases `TinapayanRMS_Test_Infrastructure` and `TinapayanRMS_Test_UI`. The tests never touch `TinapayanRMS`.

### Connection strings

| Used by | Where it's set |
|---|---|
| The app | `ConnectionStrings:Default` in [src/TinapayanRMS.UI/appsettings.json](../src/TinapayanRMS.UI/appsettings.json) |
| `dotnet ef` | Hard-coded in the design-time factory, [TinapayanRMSDbContextFactory](../src/TinapayanRMS.Infrastructure/TinapayanRMSDbContextFactory.cs) |
| The tests | The app's `appsettings.json`, which `TinapayanRMS.Tests.Common` copies into each test project's output. Each project swaps in its own database name. |

If your SQL Server instance isn't `.\SQLEXPRESS`, change the first two.

### Where the app writes

- **Logs**: `logs/tinapayanrms-<date>.log` beside the executable. In a debug build that's `src/TinapayanRMS.UI/bin/Debug/net10.0-windows10.0.19041.0/logs/`. There's one file a day, and the last 31 are kept.
- **View state**: `%LocalAppData%\TinapayanRMS\view-state.json` remembers which tree folders were left open (on Sub-Assemblies, which were left closed), and whether the Vim keys are on (`"vim-keys": ["on"]`). It's safe to delete.
- **Backups**: `%ProgramData%\TinapayanRMS\Backups`, one folder for every Windows user of the computer. SQL Server writes the `.bak` files itself, as its service account (`NT SERVICE\MSSQL$SQLEXPRESS`), which can't write into a user's profile. So the app creates this folder and, as its owner, gives the service account and `BUILTIN\Users` Modify on it, inherited by every file ([BackupFolder](../src/TinapayanRMS.Infrastructure/Services/BackupFolder.cs)). The tests use `%ProgramData%\TinapayanRMS\Test Backups\<guid>` and delete it afterwards.

### Command reference

| Task | Command |
|---|---|
| Build | `dotnet build TinapayanRMS.slnx` |
| Run | `dotnet run --project src/TinapayanRMS.UI` |
| All tests | `dotnet test --solution TinapayanRMS.slnx` |
| One test project | `dotnet test --project tests/TinapayanRMS.ApplicationCore.Tests` |
| One test class | `dotnet test --project tests/TinapayanRMS.ApplicationCore.Tests --filter-class "*UnitConversionRulesTests"` |
| One test | `dotnet test --project tests/TinapayanRMS.UI.Tests --filter-method "*Declining_the_question_saves_nothing"` |
| Coverage (a Cobertura report in `TestResults/`) | `dotnet test --solution TinapayanRMS.slnx --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml` |
| Add a migration | `dotnet ef migrations add <Name> --project src/TinapayanRMS.Infrastructure` |
| Check the model matches the migrations | `dotnet ef migrations has-pending-model-changes --project src/TinapayanRMS.Infrastructure` |
| Apply migrations | `dotnet ef database update --project src/TinapayanRMS.Infrastructure` |
| Query the database | `sqlcmd -S ".\SQLEXPRESS" -E -d TinapayanRMS -Q "SELECT Name, CostPerBaseUnit FROM RawMaterials"` |

The tests run on Microsoft.Testing.Platform, which `global.json` turns on (`test.runner`). VSTest-style flags such as `--filter` and `--collect` don't work: use the `--filter-*` options and `--coverage` instead.

Close any copy of the app you have running before you build. A running app locks its own output files.

### When a change is done

A change is done when:

- the solution builds,
- every test passes,
- any new migration applies cleanly to your local database,
- a change users can see (a screen, button, field, rule or cost formula) has updated the [user manual](user-manual.md) in the same change,
- a change to anything this manual describes (the setup, commands, solution layout, architecture, a pattern or convention, or a type or file it names) has updated this manual in the same change, and
- a decision that changed is recorded in [PLAN.md](../PLAN.md).

---

## 4. The solution

### Projects

| Project | Target framework | References | Contents |
|---|---|---|---|
| `src/TinapayanRMS.Domain` | `net10.0` | Nothing | `Entities/`, `Enums/` |
| `src/TinapayanRMS.ApplicationCore` | `net10.0` | Domain; FluentValidation | `Interfaces/`, `Rules/`, `Validators/` |
| `src/TinapayanRMS.Infrastructure` | `net10.0` | Domain, ApplicationCore; EF Core (SQL Server), Dapper | `TinapayanRMSDbContext` and its design-time factory at the root; `Configurations/`, `Migrations/`, `Queries/`, `Repositories/`, `Services/` |
| `src/TinapayanRMS.UI` | `net10.0-windows10.0.19041.0` | All three; ReactiveUI.WPF, DynamicData, System.Reactive, the Generic Host, Serilog, Syncfusion | App startup at the root; `Views/`, `ViewModels/`, `Controls/`, `Converters/`, `Printing/`, `Exporting/`, `Vim/`, `Themes/`, `Assets/Icons/` |
| `tests/TinapayanRMS.ApplicationCore.Tests` | `net10.0` | Domain, ApplicationCore | Rule and validator tests, with no database |
| `tests/TinapayanRMS.Infrastructure.Tests` | `net10.0` | Infrastructure, Tests.Common | Persistence tests against the project's own database |
| `tests/TinapayanRMS.UI.Tests` | `net10.0-windows10.0.19041.0` | UI, Tests.Common | ViewModel, control, printing, export, converter and Vim-key tests |
| `tests/TinapayanRMS.Tests.Common` | `net10.0` | Infrastructure; Respawn | The shared database fixtures. It's a plain library, not a test project. |

The two UI projects target Windows SDK 10.0.19041 because ReactiveUI.WPF needs it. The bare `net10.0-windows` moniker resolves to `windows7.0`, which is too low.

### Folders are namespaces

Every folder in a project is a sub-namespace: `src/TinapayanRMS.UI/ViewModels/` holds `TinapayanRMS.UI.ViewModels`. A new file goes in the folder for its kind, never at a project root. Only app-startup plumbing (`App`, `CompositionRoot`, `ReactiveUIBootstrapper`, `ConfirmDeleteHandlerRegistration`, `BindingErrorLog`) and the DbContext pair sit at a root. Code in a sub-namespace sees its parent namespaces without a `using`; sibling folders need one.

| A new… | Goes in | Its tests go in |
|---|---|---|
| Entity or enum | `Domain/Entities/`, `Domain/Enums/` | (tested through the layers above) |
| Business or costing rule | `ApplicationCore/Rules/` | `ApplicationCore.Tests/Rules/` |
| Validator | `ApplicationCore/Validators/` | `ApplicationCore.Tests/Validators/` |
| Repository | Interface in `ApplicationCore/Interfaces/`, implementation in `Infrastructure/Repositories/` | `Infrastructure.Tests/Persistence/` |
| EF configuration | `Infrastructure/Configurations/` | `Infrastructure.Tests/Persistence/` |
| Cross-entity service | `Infrastructure/Services/` | `Infrastructure.Tests/` |
| Screen | View in `UI/Views/`, ViewModel in `UI/ViewModels/` | `UI.Tests/ViewModels/` |
| Card of a large screen | `UI/Views/<Screen>/` | — |
| Reusable control or behaviour | `UI/Controls/` | `UI.Tests/Controls/` |
| Value converter | `UI/Converters/` | `UI.Tests/Converters/` |
| Something written to a file, such as an Excel export | `UI/Exporting/` | `UI.Tests/Exporting/` |
| Part of the Vim keys | `UI/Vim/` | `UI.Tests/Vim/` |
| Icon image file (`.png` or `.ico`) | `UI/Assets/Icons/` | — |

A ViewModel file can also hold the small row and edit types that only its screen uses. For example, `ProductsViewModel.cs` defines `ProductUsageEdit` and `ProductCostSummary`.

### Build configuration

- [Directory.Build.props](../Directory.Build.props) sets `net10.0`, `ImplicitUsings`, `Nullable`, the `WindowsTargetFramework` property, and `AnalysisLevel` `latest-recommended` (the SDK's built-in analyzers at their Recommended rule set).
- [tests/Directory.Build.props](../tests/Directory.Build.props) imports it. It gives every project whose name ends in `.Tests` the xUnit v3, AwesomeAssertions, test SDK and coverage packages, and a global `using Xunit`.
- [Directory.Packages.props](../Directory.Packages.props) turns on Central Package Management. Versions live only there, so a `PackageReference` never has a `Version`. The EF Core and Syncfusion versions are the `EFCoreVersion` and `SyncfusionVersion` properties. Every Syncfusion package must be on the same version, so upgrade them together.
- [.editorconfig](../.editorconfig) marks `Migrations/` as generated code, turns off CA1707 (underscores in names) in `tests/`, and turns off CA1716 everywhere.
- Warnings aren't treated as errors, but the build is kept free of them.

### Other files at the root

`.claude/`, `apm.yml`, `apm.lock.yaml` and `skills-lock.json` hold AI-assistant tooling: Syncfusion's WPF agent skills, and a UI-builder agent. You don't need them to build or run the app. However, the `syncfusion-wpf-*` skills in `.claude/skills/` are readable Markdown guides to each Syncfusion control.

### A reading order for the code

1. The entities [RawMaterial](../src/TinapayanRMS.Domain/Entities/RawMaterial.cs), [SubAssembly](../src/TinapayanRMS.Domain/Entities/SubAssembly.cs) and [Product](../src/TinapayanRMS.Domain/Entities/Product.cs). Their XML comments explain the domain.
2. [CostingRules](../src/TinapayanRMS.ApplicationCore/Rules/CostingRules.cs), [ProductRules](../src/TinapayanRMS.ApplicationCore/Rules/ProductRules.cs) and [IngredientCostGraph](../src/TinapayanRMS.ApplicationCore/Rules/IngredientCostGraph.cs).
3. [IngredientCostPropagator](../src/TinapayanRMS.Infrastructure/Services/IngredientCostPropagator.cs) and [SubAssemblyCostPropagator](../src/TinapayanRMS.Infrastructure/Services/SubAssemblyCostPropagator.cs).
4. [ProductCategoryRepository](../src/TinapayanRMS.Infrastructure/Repositories/ProductCategoryRepository.cs), the simplest repository; then `SaveAsync` in [RawMaterialRepository](../src/TinapayanRMS.Infrastructure/Repositories/RawMaterialRepository.cs).
5. [App.xaml.cs](../src/TinapayanRMS.UI/App.xaml.cs), [CompositionRoot.cs](../src/TinapayanRMS.UI/CompositionRoot.cs) and [MainWindow.xaml.cs](../src/TinapayanRMS.UI/Views/MainWindow.xaml.cs).
6. [ViewModelBase](../src/TinapayanRMS.UI/ViewModels/ViewModelBase.cs), then the smallest complete screen: [CategoriesViewModel](../src/TinapayanRMS.UI/ViewModels/CategoriesViewModel.cs) with [CategoriesView.xaml](../src/TinapayanRMS.UI/Views/CategoriesView.xaml).
7. [CollectionMerge](../src/TinapayanRMS.UI/ViewModels/CollectionMerge.cs) and [UndoHistory](../src/TinapayanRMS.UI/ViewModels/UndoHistory.cs).
8. [ReactiveCostCascadeTests](../tests/TinapayanRMS.Infrastructure.Tests/Persistence/ReactiveCostCascadeTests.cs), which runs the whole cascade end to end.

Then move on to the large screens. `ProductsViewModel.cs` is about 5,800 lines and `RawMaterialsViewModel.cs` about 2,900, so navigate them by command and member with your IDE's outline, rather than reading them top to bottom.

---

## 5. Architecture

### Layers

```
┌──────────────────────────── TinapayanRMS.UI ──────────────────────────────┐
│  Views (XAML + code-behind)  ◄── bind to ──  ViewModels (ReactiveUI)      │
│  Controls, Converters, Printing, Themes                                   │
│  App and CompositionRoot (the only code that names Infrastructure types)  │
└───────────────┬───────────────────────────────────────────────────────────┘
                │ uses the repository interfaces, Rules and validators
┌───────────────▼──────────── TinapayanRMS.ApplicationCore ─────────────────┐
│  Interfaces: repository contracts, and the draft records saves take       │
│  Rules: static, pure business and costing logic                           │
│  Validators: FluentValidation pre-checks for the forms                    │
└───────────────▲───────────────────────────────────────────────────────────┘
                │ implements the interfaces, calls the Rules
┌───────────────┴──────────── TinapayanRMS.Infrastructure ──────────────────┐
│  TinapayanRMSDbContext, Configurations, Migrations     EF Core: writes    │
│  Queries                                               Dapper: reads      │
│  Repositories; Services (cost propagators, ProductCloner, view state)     │
└───────────────────────────────────────────────────────────────────────────┘
                every layer uses
┌──────────────────────────── TinapayanRMS.Domain ──────────────────────────┐
│  Entities and enums: plain classes with no dependencies                   │
└───────────────────────────────────────────────────────────────────────────┘
```

The layering is fixed:

- **Domain** holds the persisted entities as plain classes with documented properties. It has no dependencies and no behaviour beyond default values.
- **ApplicationCore** holds the rules, the repository contracts and the validators. It knows nothing of EF Core, SQL or WPF, so everything in it can be unit tested without a database.
- **Infrastructure** does all the I/O: EF Core for writes, the schema and migrations; Dapper for reads; and the services that work across entities.
- **UI** depends on ApplicationCore's interfaces. Only `CompositionRoot` names an Infrastructure type, to register it.

Business and costing rules go in ApplicationCore, and EF Core and other I/O stay in Infrastructure. A ViewModel never sees a `DbContext`.

### Startup

1. **`App`'s static constructor** ([App.xaml.cs](../src/TinapayanRMS.UI/App.xaml.cs)) runs before anything touches a Syncfusion type. It builds the Serilog logger and, in Debug builds, adds the binding-error log. It registers the Syncfusion licence, and throws (after logging) if `SYNCFUSION_LICENSE_KEY` is missing. It also sets app-wide control defaults: 24 px text boxes and buttons, right-aligned number boxes, editable pickers that filter as you type, and the grid, picker and undo behaviours from `Controls/`.
2. **`App.OnStartup`** hooks unhandled-exception logging and registers the Windows11Light theme settings, with the accent recoloured to the app's Sky and every text size 2 px larger than the theme's default. It then builds and starts the host from `CompositionRoot.CreateHostBuilder`, resolves `MainWindow`, themes it with `SfSkinManager.SetTheme` and shows it.
3. **`CompositionRoot.CreateHostBuilder`** ([CompositionRoot.cs](../src/TinapayanRMS.UI/CompositionRoot.cs)) initialises ReactiveUI, reads `appsettings.json` and registers everything: the DbContext factory, the propagators, the repositories, `SavedProductReader`, the view-state store, the logger, the validators, the ViewModels and `MainWindow`.
4. **`MainWindow`'s constructor** ([MainWindow.xaml.cs](../src/TinapayanRMS.UI/Views/MainWindow.xaml.cs)) creates each screen's view around its ViewModel. It maps each tab to its screen's `LoadCommand` and to its Back and Forward handling, and wires up the requests that one screen makes of another. When the window has loaded, it loads the tab on show.
5. Once the window shows, **`App`** starts two things off the UI thread: a check for migrations the database doesn't have yet, and the day's automatic backup (`IDatabaseBackups.BackUpDailyIfDueAsync`), taken the first time the app starts each day. A backup that fails is only logged.
6. **`App.OnExit`** disposes the host, which disposes the singleton ViewModels, and flushes the log. After a restore, `App.RestartAfterExit` has set it to start the app again, once the log is closed.

ReactiveUI 26 must be initialised exactly once per process, before any `ReactiveObject` exists. [ReactiveUIBootstrapper](../src/TinapayanRMS.UI/ReactiveUIBootstrapper.cs) does it with `RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().WithWpf().BuildApp()`, and tests that build ViewModels call `ReactiveUIBootstrapper.EnsureInitialized()` themselves.

### Dependency injection and lifetimes

Everything in the container is a **singleton**: the repositories, the services (`ProductCloner`, `CostRecalculator`, `SavedProductReader`), the validators, every ViewModel and `MainWindow`. A screen lives for the whole session, and so does its ViewModel.

The DbContext is registered with **`AddDbContextFactory`**, never `AddDbContext`. A singleton can't hold a `DbContext`: two screens loading at once would share it, and EF Core throws when a second operation starts on a context that's still busy. That happened the first time the app ran. So every repository takes `IDbContextFactory<TinapayanRMSDbContext>` and creates a short-lived context for each call:

```csharp
public class ProductCategoryRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductCategoryRepository
{
    public async Task UpdateAsync(ProductCategory category, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await dbContext.ProductCategories.FirstOrDefaultAsync(c => c.Id == category.Id, cancellationToken)
            ?? throw new BusinessRuleException($"Category {category.Id} does not exist.");

        existing.Name = category.Name;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

Never inject `TinapayanRMSDbContext` into a repository or a ViewModel.

Views aren't in the container. `MainWindow` and `SettingsWindow` create them with `new SomeView(viewModel)`. [CompositionRootTests](../tests/TinapayanRMS.UI.Tests/CompositionRootTests.cs) resolves every screen's ViewModel, so a missing registration fails a test rather than the app.

A ViewModel that owns a DynamicData `SourceCache` is `sealed` and `IDisposable`, so that the host disposes the cache at exit.

### How a change travels through the layers

Take a user who types a new price for cake flour on Raw Materials and presses **Save**:

1. **The ViewModel.** `RawMaterialsViewModel.SaveCommand` runs inside `RunSafelyAsync`. It runs the FluentValidation validators for what it's about to save, and stops with an `ErrorMessage` if one fails.
2. **The repository, in one transaction.** `IRawMaterialRepository.SaveAsync(material, units, brandPrices, bomLines)` opens one context and one transaction. It saves the material through `RawMaterialRules`' checks, replaces the units and brand prices, and reprices the material at its dearest brand, logging one price history entry if the cost moved. It replaces the BOM lines if they were sent.
3. **The cascade, in the same transaction.** `IngredientCostPropagator` recomputes every made or linked ingredient that depends on the flour, in dependency order. It then hands the changed ids to `SubAssemblyCostPropagator`, which recomputes the `TotalWeight` and `TotalCost` of every sub-assembly that uses any of them. Then the transaction commits. A rule that throws anywhere, or a cascade that fails part-way, rolls all of it back.
4. **The DbContext** stamps `CostUpdatedAt` on every material whose stored cost changed.
5. **Back in the ViewModel**, `LoadCommand` merges the fresh data into the screen, and a success toast says "Cake Flour was saved."
6. **The Products screen** stores no costs. The next time its tab shows, its `LoadCommand` reads the new sub-assembly costs, and `ProductRules` and `CostingRules` work out the new selling price on read.

### Key decisions

| Decision | Why | Read more in PLAN.md |
|---|---|---|
| A DbContext factory, and one short-lived context per operation | ViewModels are singletons, and screens load at the same time | [Application Architecture](../PLAN.md#application-architecture) |
| Reads through Dapper, writes through EF Core | Plain SQL reads are fast and explicit. Every write still goes through EF Core, so the cost propagators run and a duplicate name surfaces as `DbUpdateException`. | [Conventions](../PLAN.md#conventions) |
| Material and sub-assembly costs stored and propagated; product costs computed on read | BOMs nest to any depth, so their costs are worth keeping current. A product's figures are quick to work out from those stored costs, and working them out on every read is simpler than keeping them in step. | [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) |
| A pure ordering function plus a database orchestrator, instead of DynamicData joins | Simpler, testable without a database, and the database stays the single source of truth | [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) |
| Cost-plus pricing, in three separate steps | The shop owner can check each step by hand | Application Architecture, "Cost-plus pricing" |
| `decimal` at full precision, rounded only for display and the final price | Rounding each figure before adding them up drifts | Conventions, "Decimal precision" |
| One Save per screen, in one transaction, with the cost cascade it causes | A rejected part, or a cascade that fails, leaves nothing half-saved and no stored cost stale | Application Architecture, "One Save writes the whole material"; [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) |
| A failure is shown on its screen, and the app carries on | A crash loses unsaved work, and one failed read mustn't stop a screen working | Application Architecture, "A failure is shown on its screen" |
| Screens merge fresh data instead of reloading it | A tab keeps its selection, scroll position and unsaved edits | Application Architecture, "A tab keeps its view state" |
| Tests on a real database, with nothing mocked | They catch what mocks miss: SQL, constraints and EF Core's behaviour | [Development Environment](../PLAN.md#development-environment) |
| Syncfusion controls over native ones | The whole UI picks up the theme consistently | [Syncfusion UI Framework](../PLAN.md#syncfusion-ui-framework) |
| AwesomeAssertions, not FluentAssertions | FluentAssertions 8 needs a paid licence for commercial use | [Tech Stack](../PLAN.md#tech-stack) |

---

## 6. The domain model

The entities in `Domain/Entities` are plain classes, and their XML comments are the best reference for each property. The behaviour lives in the `*Rules` classes.

### What feeds what

Each arrow reads "is used by". Costs flow the same way.

```
INGREDIENTS
  any ingredient ──► IngredientBomLine ──► made ingredient              (BOMs nest to any depth)
  any ingredient ──► linked ingredient (LinkedToId)                     (egg white from egg)
  any ingredient ──► SubAssemblyLine ──► SubAssembly ──► ProductSubAssemblyUsage ──► Product
  any ingredient ──► UsageAddInLine ──► (extras on one product's usage)

PACKAGING
  packaging material ──► PackagingLine ─────────────────────────────────────────────► Product
  packaging material ──► SubAssemblyPackagingLine ──► packaging set ──► PackagingLink ──► Product

COMPOSITES
  Product ──► ProductComponent ──► composite Product                    (composites nest; no loops)

PER PRODUCT
  CostingParameters (one per product), started from CostingDefaults (one row)
```

### Entities

| Entity | What it is | Worth knowing |
|---|---|---|
| `RawMaterial` | An ingredient or a packaging material | `Type` and `Source` never change once saved. Names are unique across both types. `CostPerBaseUnit` and `BufferedCostPerBaseUnit` are stored as `decimal(28,20)`. Check constraints back up the type and source rules. |
| `RawMaterialUnit` | An alternative unit, defined in base units | Never the base unit itself: a line or price with no unit is in the base unit. Correcting a multiplier reprices the material. A unit in use can't be deleted. |
| `RawMaterialBrandPrice` | A purchased material's price for one brand, in one unit | The Raw Materials Save replaces them as a set. |
| `RawMaterialPriceHistory` | A price the material was costed at | A log. Nothing reads it to cost anything. |
| `IngredientBomLine` | One component of a made ingredient, in grams | Components are ingredients only, and a BOM can't contain itself, directly or through others (`IngredientBomRules.EnsureNoCycle`). |
| `SubAssemblyRole` | Dough, Filling, Topping, Packaging, … | The starting roles are seeded by migrations, and are edited in Settings. The Packaging role (`IsPackaging`) can't be deleted, and nothing clears its flag. |
| `SubAssembly` | A recipe, or a packaging set | Names are unique. A null `OwnerProductId` means standalone. It stores `TotalWeight` and `TotalCost`, and can't be deleted while a product links it. |
| `SubAssemblyLine` | An ingredient in a recipe, in grams | Grams are always stored. `AuthoredBakersPercent` records a percentage the user typed instead. |
| `SubAssemblyPackagingLine` | A packaging material in a packaging set | Costed on read. |
| `Product` | A sellable product | Names are unique, deleted products' included. `IsComposite` is fixed once saved. `QuantityRequired` is at least 1, and exactly 1 for a composite. Deleting one only stamps `DeletedAt` (a soft delete): see [Deleted products](#deleted-products). |
| `ProductCategory` | Breads, Cakes, … | A list of its own, separate from raw material categories. |
| `ProductSubAssemblyUsage` | A sub-assembly in a product's recipe | Stores only the typed piece-size figure (`AuthoredField`, `AuthoredValue`) and `ShrinkagePercent`. |
| `UsageAddInLine` | A product's extra ingredient on a linked usage | Never changes the sub-assembly that the usage links. |
| `PackagingLine` | A packaging material per sellable unit | Stores both reciprocal quantities and which one was typed (`AuthoredField`). |
| `PackagingLink` | A product's packaging, read from another product or from a set | Exactly one target, and no loops (`PackagingLinkRules`). |
| `ProductComponent` | A product inside a composite | `Quantity` is at least 1, and a composite never contains itself (`CompositeProductRules.EnsureNoCycle`). |
| `CostingParameters` | A product's five percentages | One per product. Never linked or shared. |
| `CostingDefaults` | The stage and percentages a new product starts with, a new section's shrinkage and a new raw material's cost buffer | At most one row (Id 1). With no row, new products go in Production (`DefaultNewProductStage`), the costing reads as zero, the shrinkage as 3% (`DefaultShrinkagePercent`) and the cost buffer as 10% (`DefaultCostBufferPercent`). |
| `UnitOfMeasure`, `RawMaterialCategory` | Lookups edited in Settings | A unit's name is stored in lowercase. A category is for one type of material. |

### Deleted products

A product is deleted in two steps, so a delete can be taken back. `IProductRepository.DeleteAsync` stamps `Product.DeletedAt` and changes nothing else, which moves the product to the Products list's **Deleted** folder. `RestoreAsync` clears it. `DeletePermanentlyAsync` is the hard delete, with its cascades.

- **Reads leave deleted products out by default.** `GetAllAsync` returns the products in use, and `GetDeletedAsync` returns the deleted ones. `GetByIdAsync` and `GetByIdsAsync` return either, since a holder reads what it holds by id. Every screen except Products reads through `GetAllAsync`, so a deleted product is in no list, picker, report, print, What-If or plan. There's no global query filter: one would also drop the rows of the "in use" checks below.
- **A deleted product still holds what it uses.** Its usages, packaging lines, links and composite lines stay, so a restore brings it back whole. The `Restrict` keys keep refusing deletes of what it uses. The checks that name products (`SubAssemblyRepository`'s Used By and refusals, `RawMaterialRepository.GetUsedByAsync`, a composite's role refusals) name it "Cheese Roll (deleted)", through `ProductRules.ListedName`.
- **The checks on what leans on a product run for both deletes.** Neither a soft nor a hard delete goes through while another product, deleted or not, links its packaging or holds it in a composite (`ProductRepository.EnsureNothingLeansOnAsync`). Nothing in use ever leans on a deleted product, so a restore never needs a check.
- **A deleted product can't be newly linked or held.** `PackagingLinkRepository` and `ProductComponentRepository.SetComponentsAsync` refuse it by name. Its name stays taken: Add, Update and Rename refuse it first (`ProductRules.EnsureNameNotHeldByDeleted`), so the message points to the Deleted folder instead of the unique index's "That name is already in use."
- **On the Products screen**, `ProductsViewModel.DeletedProducts` feeds `ProductTree.DeletedFolder` and nothing else. A deleted row (`ProductLeafNode.IsDeleted`) is picked the way a folder is: the form doesn't open it. Its menu has Restore and Delete Permanently, and `ProductTree.MoveTo` refuses it as a drag source or a drop target. `OpenProduct` on a deleted id picks its row in the Deleted folder.

### Where rules are enforced

Three layers check the same rules, each for a different reason.

| Layer | Where | What it gives | On failure |
|---|---|---|---|
| Form pre-checks | FluentValidation validators in `ApplicationCore/Validators`, which a ViewModel runs before it saves | An immediate, friendly message | `ErrorMessage` is set, and nothing reaches the repository |
| Business rules (the authority) | Static `*Rules` methods, which the repositories call before they write anything destructive | The real guarantee, whichever code path writes | A `BusinessRuleException`, whose message `RunSafelyAsync` shows exactly as written |
| The database (a backstop) | Unique indexes, check constraints and `Restrict` foreign keys, in `Infrastructure/Configurations` | Integrity even if a code path forgets a check | A `DbUpdateException` or a `SqlException`, which [Failure](../src/TinapayanRMS.UI/ViewModels/Failure.cs) puts plainly by SQL Server's error number: "That name is already in use.", "This item is currently in use elsewhere and can't be deleted.", and so on |

The database's messages say what kind of thing went wrong, never which rule. So a rule that a user can break must be checked first in a `*Rules` method, with a message that says what's wrong. Validators exist only for entities that have a Save form. A set of lines saved together (BOM lines, recipe lines, packaging lines) gets its checks across rows from its Rules class instead.

---

## 7. Costing

Before you change any cost path, read the user manual's [How costs are worked out](user-manual.md#12-how-costs-are-worked-out), which has worked examples, and PLAN.md's [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade).

### Stored and computed costs

| Figure | Stored? | Kept correct by |
|---|---|---|
| A material's `CostPerBaseUnit` and `BufferedCostPerBaseUnit` | Stored | The Raw Materials Save and the brand-price repository (purchased); `IngredientCostPropagator` (made and linked) |
| A material's `CostUpdatedAt` | Stored | `TinapayanRMSDbContext.SaveChangesAsync`, from what actually changed |
| A sub-assembly's `TotalWeight` and `TotalCost` | Stored | `SubAssemblyLineRepository.SetLinesAsync` when its lines are saved; `SubAssemblyCostPropagator` when an ingredient's cost changes |
| A usage's worked-out piece size, final weight and cost per piece | Computed on read | `ProductRules` |
| Packaging line and packaging set costs | Computed on read | `ProductRules` |
| A product's material cost, base cost, selling price and markup | Computed on read | `ProductRules`, `CostingRules` |
| What a product brings into a composite | Computed on read | `SavedProductReader`, `CompositeProductRules` |

### The cascade

```
A saved brand price, a BOM or weight-loss edit, a link or share edit,
a buffer edit, or a corrected unit multiplier
  │
  ▼
the ingredient's stored cost
  │
  ▼
IngredientCostPropagator
  recomputes every made or linked ingredient that depends on it,
  components first (IngredientCostGraph.GetDependentsInOrder)
  │
  ▼
SubAssemblyCostPropagator
  recomputes TotalWeight and TotalCost of every sub-assembly that uses a changed ingredient
  │
  ▼  nothing more is stored
products, packaging, composites, Reports and Print
  work their figures out on read (ProductRules, CostingRules, SavedProductReader)

A packaging material's saved price ──► its stored cost, which every packaging line and set reads (no propagation needed)
```

The repositories call the propagators; the UI never does. The callers are `RawMaterialRepository` (add, update and Save), `RawMaterialBrandPriceRepository`, `RawMaterialUnitRepository` (a corrected multiplier) and `IngredientBomLineRepository`. Each call comes after the write has been saved and before its transaction commits. The propagators are static classes that run on the caller's context, so a save and the cascade it causes commit together or not at all. Each loads what it needs once, recomputes in memory, and saves once. A recomputed cost is stored at its column's scale (see [Precision and rounding](#precision-and-rounding)), so one worked out again from unchanged inputs equals what's stored, and isn't written or dated again.

`IngredientCostGraph` is pure and lives in ApplicationCore. It finds the dependents with a reverse breadth-first search, then orders them with a depth-first topological sort. A linked ingredient's link is just one more edge in the same graph. `IngredientCostPropagator`, in Infrastructure, loads every ingredient and BOM line, asks the graph for the order, and recomputes each ingredient from its components' costs, which are already fresh by then. Sub-assemblies don't nest, so they need no ordering.

The cascade doesn't push changes live to other screens. A screen picks up new costs the next time it's shown, through its `LoadCommand`.

**Recalculate Costs**, on Settings' Costing Defaults tab, is the safety net for a change made outside the app, such as by a script. [CostRecalculator](../src/TinapayanRMS.Infrastructure/Services/CostRecalculator.cs) runs the propagators' own code over every made and linked ingredient, in dependency order (`IngredientCostGraph.InDependencyOrder`), and then every sub-assembly, in one transaction, and returns how many were wrong. Purchased materials aren't touched: their cost comes straight from their saved prices.

### Formulas and where they live

| Figure | Formula | Code |
|---|---|---|
| Purchased cost per base unit | The dearest brand's price ÷ the base units in its unit | `RawMaterialRules.ApplyBrandPrices`, `UnitConversionRules.CostPerBaseUnit` |
| Purchased buffered cost | A fixed amount, set to cost × (1 + buffer %) on the first price, or whenever the % is typed | `RawMaterialRules.BufferedCost` |
| Made: output grams | Σ line grams × (1 − weight loss %) | `IngredientBomRules.CalculateOutputGrams` |
| Made: cost per gram | Σ (line grams × component cost per gram) ÷ output grams. The buffered cost rolls up the components' buffered costs, and adds no buffer of its own. | `IngredientBomRules.CalculateCost` |
| Linked: cost per gram | The linked-to ingredient's cost × cost share % ÷ weight share %, for both costs | `RawMaterialRules.LinkedCost` |
| Sub-assembly totals | Σ grams; Σ (grams × the ingredient's buffered cost per gram) | `SubAssemblyRules.CalculateTotals` |
| A usage's batch | The sub-assembly's totals plus its add-ins | `ProductRules.CalculateEffectiveBatch` |
| Final weight | Batch weight × (1 − shrinkage %) | `ProductRules.CalculateFinalWeight` |
| Piece size | Yield = final weight ÷ weight per piece, or weight per piece = final weight ÷ yield, depending on which was typed | `ProductRules.ResolvePieceSize` |
| Usage cost per piece | Batch cost × weight per piece ÷ final weight | `ProductRules.CalculateUsageCostPerPiece` |
| Packaging line cost | Cost per base unit × the quantity in base units | `ProductRules.CalculatePackagingLineCost` |
| Material cost per sellable unit | Σ usage costs per piece × Quantity Required, + Σ packaging | `ProductRules.CalculateMaterialCost` |
| Labor, overhead, administrative | Each is its % of the sub-assembly cost alone, never of the packaging | `CostingRules.CalculateCostFromSubAssemblyPercent` |
| Selling price | Base cost = sub-assemblies + packaging + labor + overhead + administrative; pre-VAT price = base cost × (1 + markup %); selling price = pre-VAT price × (1 + VAT %) | `CostingRules.CalculateSellingPrice` |
| Markup at the current price | (current price ÷ (1 + VAT %) − base cost) ÷ base cost | `CostingRules.CalculateMarkupPercentFromPrice` |
| A composite's line | The product's recipe cost per sellable unit × quantity, plus its packaging cost when the composite includes it | `CompositeProductRules.CalculateComponentCost`, `CalculateComponentPackagingCost` |

A made or linked ingredient's costs are worked out by **`IngredientCostRules.Recompute`**, which the cascade (`IngredientCostPropagator`) stores and the What-If screen uses without storing (`IngredientCostRules.Reprice`: copies of the changed materials and of every ingredient depending on them, in dependency order). A what-if price change moves a material's cost and buffered cost by the same factor (`PriceChangeRules.CostsAfter`).

[SavedProductReader](../src/TinapayanRMS.UI/ViewModels/SavedProductReader.cs) is the one place where a saved product is read and priced. The Products screen, Reports and Print all go through it, so they always agree. It reads any number of products together (`ReadDetailsAsync`, `ReadAllAsync`) in about fifteen queries however many there are, on the repositories' plural reads: packaging link chains are followed in memory, and the products a composite holds are read with it and costed bottom-up. For the What-If screen, `Build` takes substitute materials (by id) for its sections and packaging, and `PricedAt` prices a composite's products again with them, however deep.

### Precision and rounding

- Every cost is a `decimal` at full precision through the whole cascade. Round only for display, and for the final selling price.
- `TinapayanRMSDbContext.ConfigureConventions` makes every `decimal` column `decimal(18,4)`, so a new column can't fall back to SQL Server's `decimal(18,2)`. The two costs per base unit are `decimal(28,20)`, so a buffered cost typed per bag comes back exactly as typed after it's divided down to the gram.
- A value that goes through a `decimal(18,4)` column comes back rounded to 4 places: 1/6 becomes 0.1667. A test that reads a value back from the database asserts the rounded value.
- A stored figure worked out in memory is set at its column's scale, after it's worked out at full precision: a cost per base unit through `RawMaterialRules.StoredCost` (20 places), a sub-assembly's totals through `TinapayanRMSDbContext.Stored` (4), each rounded half away from zero as SQL Server rounds. At full precision, a cost that doesn't come out even (170 ÷ 240) never equals the stored one, so a Save that changed nothing would read it as changed, log a price again and move the cost's date.
- On screen: 2 decimals for the Products screen's per-piece costing figures and for prices, and 4 for a cost per base unit and for a recipe tab's cost per piece. Grid quantities show 1 decimal and accept up to 4. No amount shows a currency symbol.

### Buffered and unbuffered costs

Everything is costed at the buffered cost. The Products screen's **Unbuffered** switch (`CostBasis.Unbuffered`) prices what it shows and prints from the true cost instead. It changes only which cost is read (`RawMaterialRules.CostPerBaseUnitAt`). Nothing stored changes, and the propagators don't know the switch exists.

---

## 8. Data access

### Reads through Dapper, writes through EF Core

Every public repository read is plain SQL run by Dapper. It returns the same entity objects, with the same navigation properties filled in, that EF Core's `Include` chains used to return. Everything else goes through EF Core: every write, the schema and every migration, and any read inside a save that loads an entity in order to change it.

A read, from [ProductCategoryRepository](../src/TinapayanRMS.Infrastructure/Repositories/ProductCategoryRepository.cs):

```csharp
public Task<IReadOnlyList<ProductCategory>> GetAllAsync(CancellationToken cancellationToken) =>
    dbContextFactory.QueryAsync<ProductCategory>(
        $"SELECT {Columns.Of<ProductCategory>("c")} FROM ProductCategories c ORDER BY c.Name", null, cancellationToken);
```

The read helpers are in `Infrastructure/Queries`:

- **[DapperReads](../src/TinapayanRMS.Infrastructure/Queries/DapperReads.cs)** borrows a DbContext's connection, so reads use the same connection string as EF Core. `QueryAsync` and `QuerySingleOrDefaultAsync` run either on the factory, in a context of their own, or on a save's context. On a context that holds a transaction, a read joins that transaction (`DapperReads.Command`). `ReadAsync` gives a read that needs several queries, such as a multi-mapped join, a context of its own.
- **[Columns](../src/TinapayanRMS.Infrastructure/Queries/Columns.cs)** lists every entity's columns, `Id` first, because a multi-mapped join splits on `Id`. `Columns.Of<T>(alias)` writes the SELECT list.
- **`RawMaterialReads` and `SubAssemblyReads`** load the shared graphs: materials with their category, base unit and units; sub-assemblies with their role and owner.

The rules for reads:

- **`Columns` must list exactly EF Core's columns.** A property it leaves out comes back as its default value, with no error. [ColumnsTests](../tests/TinapayanRMS.Infrastructure.Tests/Queries/ColumnsTests.cs) compares every list with EF Core's model, so a new or renamed property fails that test until `Columns` has it. (That's what Infrastructure's `InternalsVisibleTo` is for.)
- **Table names are the `DbSet` names, written into the SQL.** Renaming a table means updating that SQL by hand; the persistence tests will catch any you miss.
- **Read many records in one query, never one query each.** A read for several products (or anything else) is a plural method, such as `GetForProductsAsync(ids)`, rather than a loop of single reads: Reports once made nine queries a product.
- **A list of ids is one parameter:** `WHERE x.Id IN ({DapperReads.IdsTable})` with `new { ids = DapperReads.IdList(ids) }`. It's split by `STRING_SPLIT`, so it never hits SQL Server's limit of 2,100 parameters.
- **An optional navigation is a `LEFT JOIN`.** Dapper returns null for a split whose `Id` is null.
- **A collection navigation, such as a material's `Units`, is a second query stitched on by id,** not a join, because Dapper's multi-mapping over several result sets is synchronous only.
- **There's no identity resolution.** A material on two lines is two objects.
- **Every read has an explicit `ORDER BY`.** Lines are read `ORDER BY SortOrder, Id`.
- Dapper maps a `date` column to `DateOnly`, and an enum stored as a string back to the enum, on its own.

Never write through Dapper. A Dapper write would skip the cost propagators and the `CostUpdatedAt` stamp, leaving stored costs stale.

### Writing safely

- **Update by loading, then applying.** An `UpdateAsync` loads the tracked entity, copies across only the fields a user can edit, and saves. Never call `dbContext.Update(incoming)` on an object the caller built: it marks every property as modified, and silently overwrites the stored costs. If an edited field feeds a stored figure (a unit's multiplier feeds the cost), the same method recomputes that figure.
- **Delete with `ExecuteDeleteAsync`.** It runs at once, outside the change tracker, so a foreign key's refusal arrives as a `SqlException`, not a `DbUpdateException`. `RunSafelyAsync` shows it as "This item is currently in use elsewhere and can't be deleted." A test of it expects `SqlException`. It also runs inside the context's transaction when there is one, so a set of lines replaced with it commits or rolls back with the rest.
- **Replace a set of lines whole.** The `SetLinesAsync` methods first check every incoming row, and every record the rows refer to. Only then do they delete the old rows with `ExecuteDeleteAsync`, insert the new ones with `SortOrder` set to each row's position, recompute any totals and save. A rejected set leaves the old lines untouched. The set a method replaces must match exactly what the UI edits as one unit.
- **One Save, one transaction.** `RawMaterialRepository.SaveAsync` and `ProductRepository.SaveAsync` write everything a screen's form holds in one context and one transaction. Each step is an `internal static` method that takes the context, so steps from several repositories compose. Each step saves as it goes, so the checks of later steps see what earlier ones wrote. A part passed as `null` means "leave it as saved". The caller's entity is copied, never attached, so a rolled-back save never leaves an id on it. The cost cascade runs once, at the end, on the same context and in the same transaction, so it commits with the save or not at all.
- **Save inputs are immutable records:** `RecipeDraft`, `NewSubAssemblyDraft`, `LinkedUsageDraft`, `PackagingLinkDraft`, `ProductMove`, `RawMaterialMove` and the `RecipeLine` record struct, all in `ApplicationCore/Interfaces`.
- **`CostUpdatedAt` is stamped centrally**, in `TinapayanRMSDbContext.SaveChangesAsync`, by comparing the costs' original and current values. Don't set it by hand.

The shape of an aggregate save, abridged from `RawMaterialRepository.SaveAsync`:

```csharp
await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
int id;
var costChanged = false;
await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
{
    var fields = new RawMaterial { Id = material.Id, Name = material.Name /* … the form's fields … */ };
    costChanged = fields.Id == 0
        ? await AddAsync(dbContext, fields, cancellationToken)       // internal static: rules first, then SaveChanges
        : await UpdateAsync(dbContext, fields, cancellationToken);
    id = fields.Id;

    // … the units, brand prices and BOM lines, each through another repository's internal static method …

    if (costChanged)
    {
        await IngredientCostPropagator.RecomputeAndPropagateAsync(dbContext, id, cancellationToken);   // same context, before the commit
    }

    await transaction.CommitAsync(cancellationToken);
}
```

### Cross-entity services

`Infrastructure/Services` holds the work that spans entities:

- **`IngredientCostPropagator` and `SubAssemblyCostPropagator`**: the cascade. They're internal static classes that run on a repository's context, inside its transaction, and aren't exposed through ApplicationCore interfaces.
- **`CostRecalculator`** (`ICostRecalculator`): Settings' Recalculate Costs, the propagators' code run over every made and linked ingredient and every sub-assembly, in one transaction.
- **`IngredientDependencies`**: loads the ingredient dependency graph, made of BOM edges and link edges, for the cycle checks.
- **`ProductCloner`** (`IProductCloner`): Clone and Create Variant. Both link the sub-assemblies the source links and copy the source's own ones. A clone copies the packaging; a variant links it.
- **`JsonViewStateStore`** (`IViewStateStore`): the view-state file. A missing or broken file reads as empty, and a failed write is dropped.
- **`DatabaseBackups`** (`IDatabaseBackups`): Settings' Database tab and the daily automatic backup. It runs `BACKUP DATABASE … COPY_ONLY, CHECKSUM` and `RESTORE DATABASE … REPLACE` on its own non-pooled connections to `master`, one at a time (a `SemaphoreSlim`, since SQL Server refuses two backups of a database at once). A restore checks the file first (`RESTORE HEADERONLY`, `FILELISTONLY`, `VERIFYONLY`), takes a safety backup, puts everyone out of the database (`SINGLE_USER WITH ROLLBACK IMMEDIATE`), restores with its files moved onto the database's own, and brings an older backup up to date with `MigrateAsync`. A backup from a later version, or of another database, is refused (`BackupRules.UpdatesNeeded`). Whatever fails after the safety backup puts the safety backup back. A new backup or a file copied in never reuses a name in the folder: names go to the second and `WITH INIT` would overwrite, so `BackupRules.FreeFileNameFor` moves the time on a second while the name is taken. The app then restarts, since every singleton ViewModel holds data from before. `Failure` puts SQL Server's backup errors plainly, looking at every error in the exception, since 3013 follows the real one.

### Schema conventions

- There's one `IEntityTypeConfiguration<T>` per entity in `Configurations/`, and `OnModelCreating` applies them all from the assembly.
- Every `DbSet` name is its table's name.
- Every name a user chooses (materials, categories, products, sub-assemblies, roles, units) has a unique index.
- Enums are stored as strings (`HasConversion<string>()`), so the data reads clearly and Dapper maps them back by name.
- Owned children cascade on delete (lines, units, prices, costing parameters). References are `Restrict`, so a material that a line uses can't be deleted. A product's category is `SetNull`.
- Check constraints back up the rules. They're named `CK_<Table>_<Rule>`.
- Every line table has a `SortOrder`, because users drag lines into order.
- Where the CLR default is a meaningful value, a column default is paired with `HasSentinel`, as on `Product.QuantityRequired` and `Product.Stage`.
- On a unique index over a nullable column, EF Core adds `WHERE [Column] IS NOT NULL`, so null rows may repeat. Add `.HasFilter(null)` if null rows must collide too. (`sqlcmd` writes to a table with a filtered index need `SET QUOTED_IDENTIFIER ON;` first.)

### Migrations

Migrations come only from `dotnet ef migrations add`. Never write or edit one by hand. (A few early migrations carry hand-added data-conversion SQL from before this rule.)

1. Change the entity, its configuration, and its `Columns` list.
2. `dotnet ef migrations add <Name> --project src/TinapayanRMS.Infrastructure`.
3. Read the generated `Up` and `Down` before you accept them.
4. `dotnet ef migrations has-pending-model-changes --project src/TinapayanRMS.Infrastructure` should report no changes.
5. `dotnet ef database update --project src/TinapayanRMS.Infrastructure`.
6. Run the tests. The fixtures migrate the test databases themselves.

To discard a migration you've generated but haven't applied anywhere, run `dotnet ef migrations remove --project src/TinapayanRMS.Infrastructure`.

**Renaming a column without losing data.** EF Core pairs renamed columns by guesswork. If you rename two columns of the same type on one table in one step, it can pair them crosswise and swap their data, or drop and re-add them. Use two migrations instead:

1. Rename the properties, and pin the old column names with `HasColumnName("OldName")`. Generate any other pending migration now; it carries no column change.
2. Remove the pins and generate again. The only difference is now the column names, so EF Core emits an unambiguous `RenameColumn`.

---

## 9. The UI layer

### The shell

[MainWindow](../src/TinapayanRMS.UI/Views/MainWindow.xaml) is an `AppWindow`, which is a Syncfusion `ChromelessWindow`: the title bar is drawn in the theme, with Back and Forward on its right, just before Minimize. The hamburger button opens an `SfNavigationDrawer` menu with Raw Materials, Sub-Assemblies, Products, Reports, Production Plan and What-If Prices, and a Settings button at the bottom. The screens are the tabs of `MainTabControl`, a `TabControlExt` with two important settings:

- **`CloseMode="Hide"`**: closing a tab hides it rather than deleting it, so the menu can always show it again.
- **`IsDisableUnloadTabItemExtContent="True"`**: every tab's content stays loaded. Scroll positions, sort orders, sub-tabs and typed text survive a switch without any code.

The app starts with only Raw Materials, Sub-Assemblies and Products open: Reports, Production Plan and What-If Prices start hidden (`Visibility="Collapsed"`), like a closed tab, until the menu shows them. The Print tab stays hidden until Products asks for it. The Settings dialog, `SettingsWindow`, is built afresh each time it opens, over the same singleton ViewModels. When it closes, it disposes its delete confirmations and releases its controls (grids, text boxes and picker filters), which would otherwise keep every closed dialog in memory. Then the main window reloads the screen on show, so its pickers include anything added in Settings.

### Views and cards

A view is a `UserControl` whose constructor takes its ViewModel:

```csharp
public RawMaterialsView(RawMaterialsViewModel viewModel)
{
    InitializeComponent();
    DataContext = viewModel;
    viewModel.RegisterNativeMessageBoxHandler();   // ConfirmDelete as a Yes/No MessageBox
    // … handlers for the screen's other Interactions (prompts) …
}
```

A large screen is split into card `UserControl`s in a folder named after it (`Views/RawMaterials/`, `Views/SubAssemblies/`, `Views/Products/`). A card never sets its own `DataContext`: it inherits the screen's ViewModel. The screen's XAML decides when each card shows. Keep each XAML file shallow, and keep a card's code-behind to view-only work, such as focus and scrolling.

Each screen puts an `ErrorToast` over its content, and a `SuccessToast` where it reports success. Both bind to the screen's `ErrorMessage` and `SuccessMessage`, which every `ViewModelBase` has.

### ViewModels

**[ViewModelBase](../src/TinapayanRMS.UI/ViewModels/ViewModelBase.cs)** is the base of every screen's ViewModel. It provides:

- `ErrorMessage` and `SuccessMessage`, which the toasts show.
- **`RunSafelyAsync(action)`**, which clears both messages and runs the action. Whatever the action throws becomes `ErrorMessage`, in the words [Failure](../src/TinapayanRMS.UI/ViewModels/Failure.cs) gives it: a `BusinessRuleException`'s message as written; what the database refused or couldn't do, put plainly by its error number (a duplicate name, something in use, a database that can't be reached); a file that couldn't be written or opened (`IOException`, `UnauthorizedAccessException`); and anything else, a bug, as "Something went wrong, and the details went to the log: …". A refusal is logged as a warning, anything else as an error with its stack. Nothing reaches ReactiveUI, which would hand it to `App`.
- **`LoadSafelyAsync(load)`**, which every `LoadCommand` runs its body in. A load that fails shows why in the same words, without clearing the notices (a screen loads after its own Save, and mustn't take that Save's message away), and a later load that works clears it.
- **`LoadLatest(values, load)`**, for a load driven by a selection: each value's load runs as it comes, a newer value cancels a load still running, and one that fails shows why while the next value loads as usual. Never write `Observable.Switch(...).Subscribe()` for this by hand: an exception that reaches `Switch` ends the subscription, and from then on no pick loads anything.
- **`ShowFailure(exception, context)`**, for a failure that code catches itself, such as a read nothing awaits.
- **`RunWithConfirmationAsync(message, action)`**, which asks through the `ConfirmDelete` interaction first.
- **`ExportToExcelAsync(title)`**, which asks the view to write its grids to a workbook through the `ExportToExcel` interaction, and says where the file went. See [Exporting to Excel](#exporting-to-excel).
- **`ApplyRefresh`/`IsRefreshing`** and **`ApplyRestore`/`IsRestoring`**, which mark a load merging data in and an undo putting a form back. Code that reacts to user edits skips while either is set (`IsBusyApplying`).
- A logger for the concrete class (`logger.ForContext(GetType())`).

A command that deletes, or does anything else that can't be undone, goes through `RunWithConfirmationAsync`. A command that saves runs inside `RunSafelyAsync`, and runs its validator first. A command that only changes the form in memory isn't wrapped.

The smallest complete screen is the Settings dialog's Categories tab. Abridged from [CategoriesViewModel](../src/TinapayanRMS.UI/ViewModels/CategoriesViewModel.cs):

```csharp
public sealed class CategoriesViewModel : ViewModelBase, IDisposable
{
    private readonly SourceCache<RawMaterialCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categories;
    public ReadOnlyObservableCollection<RawMaterialCategory> Categories => _categories;

    private RawMaterialCategory? _selectedCategory;
    public RawMaterialCategory? SelectedCategory
    {
        get => _selectedCategory;
        set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public CategoriesViewModel(IRawMaterialCategoryRepository repository, IValidator<RawMaterialCategory> validator, ILogger logger)
        : base(logger)
    {
        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();

        // Fills the form from the selection, but not while a load is only pointing the selection at its fresh copy.
        this.WhenAnyValue(x => x.SelectedCategory)
            .Where(category => category is not null && !IsRefreshing)
            .Subscribe(category => { /* copy into the Editing* properties */ });

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await repository.GetAllAsync(CancellationToken.None);   // 1. fetch
            var selectedId = SelectedCategory?.Id;                                  // 2. remember picks by id
            ApplyRefresh(() =>                                                      // 3. merge, and pick again by id
            {
                _categoryCache.MergeFrom(categories);
                SelectedCategory = Categories.FirstOrDefault(c => c.Id == selectedId);
            });
        });

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var category = new RawMaterialCategory { /* … from the Editing* properties … */ };
            var validation = validator.Validate(category);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            await repository.AddAsync(category, CancellationToken.None);   // or UpdateAsync
            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the '{SelectedCategory!.Name}' category? This can't be undone.", async () =>
            {
                await repository.DeleteAsync(SelectedCategory!.Id, CancellationToken.None);
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedCategory).Select(category => category is not null));
    }

    public void Dispose() => _categoryCache.Dispose();
}
```

The rules for ViewModels:

- **Properties** use `RaiseAndSetIfChanged`.
- **A derived property** uses `WhenAnyValue(...).Select(...).ToProperty(...)`, but only when every input is itself an observable property. If the real input is a collection the code changes directly, use a plain property and recompute it explicitly at each place the collection changes. Calling `RaisePropertyChanged` on a `ToProperty` property doesn't recompute it.
- **Commands without a parameter** are `ReactiveCommand<RxVoid, RxVoid>`. Import the type with `using RxVoid = ReactiveUI.Primitives.RxVoid;`. Never write `using ReactiveUI.Primitives;`: its own `Select`, `Where` and `Subscribe` clash with `System.Reactive.Linq`'s (error CS0121). `System.Reactive` is referenced directly for the Rx operators.
- **Switching to the latest inner observable** is written `Observable.Switch(source)`, never `source.Switch()`. ReactiveUI imports `ReactiveUI.Binding` into every file, and that namespace has a `Switch` of its own that clashes with Rx's (error CS0121). `ViewModelBase.LoadLatest` uses it to load a selection's details, so a newer pick cancels an older load that is still running.
- **Lists** are a DynamicData `SourceCache<T, int>` bound with `Connect().SortAndBind(out collection, comparer).Subscribe()`, or an `ObservableCollection<T>` of editable rows.
- **Several reads at once** start in parallel with `Task.Run`, are awaited together with `Task.WhenAll`, and are applied in one step on the UI thread. Code inside `Task.Run` calls repositories only and never touches ViewModel state. (`ProductsViewModel`'s `LoadCommand` starts about a dozen reads at once this way.)
- **Dialogs and prompts** are `Interaction`s that the view handles: `ConfirmDelete` (a Yes/No `MessageBox`, through `RegisterNativeMessageBoxHandler`), `ExportToExcel` (a Save dialog and the export, through `RegisterExcelExport`), and `PromptForName` and `PromptForNewCategory` (`TextPromptWindow.Prompt`). Tests register handlers of their own.
- **One screen asks another for something** through an `IObservable<T>` backed by a `Subject<T>`: `ProductOpenRequests` (from Raw Materials, Sub-Assemblies and Reports), `RawMaterialOpenRequests` (from Reports), `NewProductRequests` (from Sub-Assemblies' New Product, handled by `ProductsViewModel.StartNewProductFrom`), `SubAssemblyOpenRequests` and `PrintRequests` (from Products). `MainWindow` subscribes to them, calls the target screen's `Open*` (or `StartNewProductFrom`) method and shows its tab, so the ViewModels never know about each other.

### Loading and refreshing

- **There's one load trigger: showing a tab.** `MainWindow.LoadSelectedTab` runs the shown tab's `LoadCommand` at startup and on every `MainTabControl.SelectionChanged`. It ignores `SelectionChanged` events that bubble up from grids and pickers, and skips a tab whose last load is still running. Views never load data on `Loaded`. The Settings dialog loads its lists when it opens.
- **Every `LoadCommand` is a fetch-then-merge, never a clear-and-reload.** It awaits all its reads first, then records the ids of the selected row and of every picker's choice, then merges inside `ApplyRefresh` and picks everything again by id. A `ComboBoxAdv` can clear its own `SelectedItem` when its item is replaced mid-merge, which is why the ids are read before the merge.
- **[CollectionMerge](../src/TinapayanRMS.UI/ViewModels/CollectionMerge.cs)** (`MergeFrom`) keeps the existing object for every row whose values haven't changed, because that object is what a grid's selection and scroll position and a picker's `SelectedItem` are tied to. `HasSameValues` compares scalar properties, plus those of referenced entities one level deep. A collection that a row owns (a material's `Units`) counts as part of the row.
- **Subscriptions driven by a selection skip while `IsRefreshing` is set.** Pointing a selection at its refreshed copy must not reload the form or throw away unsaved edits.
- Save and Delete call the same `LoadCommand` afterwards, so the record you saved stays selected.
- **A load that fails says so, and the screen keeps working.** `LoadSafelyAsync` and `LoadLatest` show the failure in the screen's error notice, and the next tab show or pick tries again. A selection's load that fails clears what it would have replaced rather than leave another record's details under the new pick.

### Editing, undo and Save

Raw Materials, Sub-Assemblies and Products edit a whole record in memory, and one Save writes it all (see [One Save, one transaction](#writing-safely)). Save is offered only when there's something to save: its `canExecute` is `History.IsDirty`.

Undo and redo follow the Memento pattern:

- **[UndoHistory&lt;TSnapshot&gt;](../src/TinapayanRMS.UI/ViewModels/UndoHistory.cs)** keeps a screen's undo and redo stacks. It knows nothing about what a snapshot holds. The screen gives it a way to take a snapshot, a way to compare two, and a way to tell whether recording is allowed (not while loading or restoring).
- **Snapshots are immutable records**: `RawMaterialFormSnapshot`, `SubAssemblyFormSnapshot` and `ProductFormSnapshot`. They hold the related records by id, not by object, because a refresh swaps in fresh objects and comparing those by reference would look like an edit.
- **Behaviours mark where a step starts and ends**: `controls:FieldUndoStep.Recorder="{Binding}"` makes one step of a visit to a field, and `controls:GridUndoSteps.Recorder="{Binding}"` makes one step of each change in a grid.
- **`UndoShortcuts`** sends Ctrl+Z and Ctrl+Y to the screen under the cursor, through its `IUndoScreen`. It's a class handler on `PreviewKeyDown`, because Syncfusion's number boxes take both keys for their own undo.
- **`History.IsDirty`** compares the form with the snapshot it was last saved or loaded with. It decides whether Save is offered, whether Back and Forward may change the record a screen has open, and whether a printout says "Includes unsaved changes".

### Navigation history

[NavigationHistory](../src/TinapayanRMS.UI/ViewModels/NavigationHistory.cs) holds the Back and Forward stacks of `ScreenVisit(AppScreen, RecordId)`. Back keeps up to 50 stops. A step is any change of screen: the menu, the tab strip, a closed tab, or a link from one screen to another. Picking another record in a list isn't a step. `MainWindow` records each step with the record the screen had open as it was left. Going back reopens that record through the screen's `Open*` method, unless the screen's form holds unsaved changes. Alt+Left and Alt+Right work too: they're handled on the window's `PreviewKeyDown`, which checks `SystemKey` because WPF reports a key pressed with Alt as `Key.System`.

### Vim keys

[Vim/](../src/TinapayanRMS.UI/Vim/) holds an optional layer of Vim-style keys over the whole app, in the manner of Vimium. The user turns it on in Settings → Keyboard. `KeyboardSettingsViewModel` keeps the choice in the view state (`vim-keys`), and `App.OnStartup` applies it. While it's off, each handler returns after a single `bool` check.

- **Where keys are caught.** `VimKeys.Register()`, called from `App`'s static constructor, adds class handlers on `Window` for `PreviewKeyDown`, `PreviewTextInput` and `GotKeyboardFocus` (with `handledEventsToo`).
  - Letters come from `PreviewTextInput`. `SfDataGrid` starts a cell edit for a letter in `OnTextInput`, which this handler runs before, and `e.Text` is already the keyboard layout's character.
  - Keys that type nothing (Esc, Ctrl+R) come from `PreviewKeyDown`.
- **Modes.**
  - Insert is the app as it always was. It applies when focus is in a `TextBoxBase` (a field, or a picker's text) or in a grid cell being edited.
  - In a grid, Insert lasts until Esc (`_gridIsInsert`, set whenever a cell is seen being edited). Tab, Enter or an arrow ends the cell's edit, but the next letter or digit still goes through, so the grid starts that cell's edit itself. A grid reached from somewhere else takes the mode the keys were in (`Arrive`), so Tab from a field in Insert goes on typing. Esc on a cell that isn't being edited takes the grid back to Normal.
  - Normal applies everywhere else. It also applies to a field reached with `j`/`k` or left with Esc. In Normal, every printable character is handled, so none types or starts an edit.
  - Esc in a cell commits it through `GridEditCommit.Commit(grid, keepCurrentCell: true)` instead of cancelling it. Esc is handled only when it has something to do, so otherwise a dialog's Cancel still gets it.
  - Ctrl chords stand for other keys, as in Vim: Ctrl+[ is Esc, and Ctrl+N and Ctrl+P are Down and Up while a dropdown or suggestion list is open (`KeyForChord`). `PressInstead` sends the key through `InputManager.ProcessInput`, so it reaches what a typed key reaches. That includes a `ButtonAdv.IsCancel` button, which takes Esc as an access key and only through the input manager. Ctrl is still held, so it's lifted from the thread's key state (`GetKeyboardState`/`SetKeyboardState`) while the key goes through. WPF reads modifiers live from that state, and Ctrl+Down would jump a list to its last row.
- **Speed.** Where the keyboard is (a `Place`: field, grid, tree, table, other or overlay) is worked out once per focus change and cached. A key is then a `FrozenDictionary` lookup (`VimKeySequence`, built from `VimKeymap`) plus the action.
- **Keys stand for the keys they replace.**
  - In grids, trees and lists, `j` is raised as Down and `dd` as Delete. In a tree, `o` is raised as Enter and `r` as F2.
  - Each is raised on the element that has the keyboard, as WPF's `KeyboardDevice` raises a real key: `PreviewKeyDown`, then `KeyDown` unless the first was handled. So the controls' own handling and the app's behaviours (`GridFillerRows`, `GridUndoSteps`, the trees' `Tree_KeyDown`) apply unchanged.
  - A raised key reads Ctrl and Shift from the physical keyboard (`KeyboardDevice.Modifiers`), so only keys typed without a modifier are raised. `gg`, `G`, `0` and `$` use the grid's `MoveCurrentCell`, with `GetFirstDataRowIndex`, `GetLastDataRowIndex` (which skips the filler rows and the add row) and the column helpers. The trees pick their row through `SelectedItems`.
  - In a form, `j`/`k` call `MoveFocus` (Next or Previous), because an arrow key would step a number box's value.
- **Each list card has a toolbar under its search box**: icon-only `ButtonAdv`s (`SizeMode="Small"`, the name in `Label` and `ToolTip`, `Focusable="False"` so Tab still goes from the search box to the tree) bound to the commands of the tree's empty-space menu (`EmptySpaceMenu` in each ViewModel), with no parameter. Raw Materials' two New Category items share one `DropDownButtonAdv`. A list command added to that menu belongs in the toolbar too. The properties are set on each button, not through a `Style`, so the theme's template stays intact. A Small `ButtonAdv` comes with `Padding` 2,0,4,0 and draws the 14 px glyph in the top-left corner of a 16 × 16 icon box, so each toolbar button sets `IconWidth` and `IconHeight` to 14 and `Padding` to 3,1,3,0 to centre it. The `DropDownButtonAdv` ignores `Padding`, so it gets a 16-tall icon box with the glyph at its bottom instead. Their icons include `NewFolderIconTemplate`, `ExpandAllIconTemplate` and `CollapseAllIconTemplate` in `App.xaml`.
- **`SfTreeView` in Extended mode** (the Products, Raw Materials and Sub-Assemblies lists) has three behaviours to work around:
  - its Home only moves the focus border, without picking the row;
  - its arrow keys do nothing while the current row is hidden, for example by a search;
  - a `SelectedItem` set from outside is inserted in front of the rows already picked, rather than replacing them.

  So `gg`, `G`, and `j`/`k` from a hidden row pick their row by replacing `SelectedItems`: clear it, then add the row, as the screens' `SelectedTreeNode` setters do.
- **Windows** take part through `IVimHost`, which `MainWindow` and `SettingsWindow` implement. It provides `gt`/`gT` (`VimTabs.Step`), Back and Forward, the screens the command line names, and Settings.
- **The command line** (`:`) is parsed by `VimCommandLine` and run by `VimCommands`. A command runs on the screen found by walking `DataContext` up from the keyboard, through `IRecordScreen` (New, Save, Delete, `OpenByName`) and `IRevertScreen`. It goes through `ICommand.Execute`, as a button does, so its errors end up where they always do.
- **Overlays.** `VimOverlay` is one adorner per window, on the content's adorner layer. It holds:
  - the hint labels (`HintTargets`, `HintLabels`), all drawn in a single `OnRender`;
  - the command bar;
  - the cheat sheet, built from `VimKeymap` so it lists what the keys actually do.

  A hint clicks a `ButtonBase` or `SplitButtonAdv` through its protected `OnClick`, by reflection, because `ButtonAdv`'s automation peer has no Invoke pattern. A hint on a picker puts the keyboard in its `PART_Editable` box (`VimKeys.FocusPicker`), not on the picker: a Shift+Tab out of the box leaves a `ComboBoxAdv` unfocusable for good, and its `Focus()` then does nothing.
- **To add a key**, add a `VimKeyBinding` to `VimKeymap` (keys, action, contexts, description). Then handle its `VimAction` in `VimKeys.Perform`, or in the `In*` method for its place. The cheat sheet picks it up.
- **Slow keys.** Debug builds log any key the layer takes more than 4 ms to handle (`The Vim key … took … ms`). Most of that time is the screen's own response, such as a picked row loading its form.
- **Tests** are in `UI.Tests/Vim/`.
  - The parser, keymap, command line and labels are tested without WPF.
  - `VimKeysGridTests` raises text input and key presses on a real off-screen grid.
  - `VimKeysPickerTests` checks a hint reaches a real off-screen picker, including one a Shift+Tab has left unfocusable.
  - These tests switch the static `VimKeys.State` on and off, so they run in `OffscreenGridCollection`.

### Controls and behaviours

The app extends Syncfusion's controls with **attached behaviours** in `Controls/` rather than subclassing them. You turn a behaviour on in XAML, and it hooks the control's events. Behaviours that apply everywhere are registered once, in `App`'s static constructor.

| Behaviour | Set on | What it does |
|---|---|---|
| `GridFillerRows.IsEnabled` | A grid with height to fill | Pads the space under the last row with inert, empty rows |
| `GridRowReordering.IsEnabled` | An editable grid of lines | Lets rows be dragged into order. The collection's order is what's saved as `SortOrder`. |
| `GridRowInserting.IsEnabled` | An editable grid of lines with an `ItemFactory` | Puts a blank line from the `ItemFactory` in above the current one, as one undo step, and opens its first column. It's run by the `GridRowInserting.InsertLine` routed command: a button with `CommandTarget` set to the grid, the Insert key, or Vim's `O`. It's on the ingredient line tables of Sub-Assemblies and of a product's section form, including its add-ins. |
| `GridAddNewRow.ItemFactory` | An editable grid | Makes the add row edit a row the screen creates |
| `GridEditCommit` | An editable grid | Commits an open cell edit when focus leaves the grid, so Save sees it |
| `GridCellEditing.IsEnabled` | A grid | Makes a column editable on some rows and not others |
| `GridUndoSteps.Recorder`, `FieldUndoStep.Recorder` | A grid; a field | Undo steps (see above) |
| `CheckboxFilterFill.IsEnabled` | A grid that filters | Widens a filter pop-up's list of values to the pop-up. The theme fixes it at 188px, which cut long names off. |
| `AutoCompleteDropDown.IsEnabled` | A grid with a drop-down column that's typed into | Fixes Syncfusion's drop-down cell. Pair it with `AllowNullInput="True"` on the column. |
| `OptionalColumns.IsOptional`, `CostBasisColumns` | Grid columns | Show or hide columns from the screen's state. A grid's columns aren't in the visual tree, so they can't bind to it themselves. |
| `NumericColumn`, `NumericEditing.IsEnabled` | Number columns; their grid | Thousands separators and right alignment. `TypedDecimalDigits` accepts more decimals than the column shows. |
| `MouseWheelForwarding`, `FocusScrolling` | Stacked grids; tall pages | Keep the page scrolling under the mouse, and stop a click from scrolling the page |
| `CategoryTextCommit.Screen` | A Category picker | Offers to add a typed name that isn't a category yet |
| `ReadOnlyText.IsGreyed` | A `DoubleTextBox` | Greys out a number that can't be typed |
| `GridKeyboardFocus`, `GridHeaderHeight`, `PickerMatchHighlight`, `UndoShortcuts` | Every grid, picker or window (registered in `App`) | Focus sits on a cell, never the whole grid; header rows fit their text (two lines for a grid whose headings wrap, `GridHeaderHeight.Lines`); a picker highlights its best match for Enter or Tab; Ctrl+Z and Ctrl+Y |
| `AppWindow`, `ChromelessWindowDpi`, `ChromelessWindowMask`, `RestoreRendering` | Windows | The chromeless title bar, and fixes for its DPI changes, content mask and restore-from-taskbar flicker |
| `ErrorToast`, `SuccessToast` | Each screen | Show `ErrorMessage` and `SuccessMessage` |

### Theme and visual conventions

- **The theme** is Syncfusion's `Syncfusion.Themes.Windows11Light.WPF` package. A window is themed with `SfSkinManager.SetTheme(window, new Theme("Windows11Light"))` before it's shown: `App.OnStartup` does it for `MainWindow`, and `MainWindow.OpenSettings` for the Settings dialog. `ApplyThemeAsDefaultStyle` is deliberately left unset, because it would restyle every window. `SizeMode` is `Default`.
- **Colours** come from [Themes/Palette.xaml](../src/TinapayanRMS.UI/Themes/Palette.xaml), the Catppuccin Latte palette with derived Tint, Deep and Wash steps that make each hue readable as text or as a background. [App.xaml](../src/TinapayanRMS.UI/App.xaml) names them as design tokens (`PageBackgroundBrush`, `CardBackgroundBrush`, `AccentBrush`, `ErrorForegroundBrush`, …) and defines the shared styles (`CardStyle`, `SectionHeaderStyle`, `FormLabelStyle`, `FieldLabelStyle`, `FigureTileStyle`, …). Views use the tokens, never a hex colour or the raw palette.
- **Icons** are Segoe Fluent Icons glyphs, through the `*IconTemplate` data templates and `IconGlyphStyle`. Don't add an icon package: one was tried, and its icons weren't crisp. WPF draws emoji as outlines only, so a full-colour picture is an icon file (below).
- **Icon files**: an icon that has to be an image rather than a glyph (a downloaded `.png` or `.ico`) goes in [Assets/Icons/](../src/TinapayanRMS.UI/Assets/Icons/). The UI project compiles every `.png` and `.ico` there into the app as a WPF `Resource`, so a view refers to it as `/Assets/Icons/<name>.png`, and nothing needs adding to the `.csproj`. The folder holds no code, so it isn't a namespace. Windows scales the app by the display's setting (125% on the development PC), so an image is nearly always resized, and enlarging one blurs it. Download it larger than it's shown (64 px for 16), and set its `Width`, `Height` and `RenderOptions.BitmapScalingMode="HighQuality"`, as the Products list (64 px shown at 16) and the Used By tiles (128 px shown at 32) do for the product images (`Bread_`, `Loaf_`, `Cake_`, `Pastry_` and `Composite_64px.png` and `_128px.png`). Set `UseLayoutRounding="True"` on the `Image` too: the app doesn't round layout to whole pixels, and an image placed between them blurs.
- **Sizes**: every text box and button is 24 px tall, number boxes are right-aligned, and the body text is 14 px. `App` sets all of these.
- **Forms**: an input's label goes on its left in a stacked form (`FormLabelStyle`, lined up with `SharedSizeGroup`). A row of fields side by side keeps each label above its field (`FieldLabelStyle`). A value that can never be typed is a `TextBlock`, not a read-only text box.
- **Amounts** have no currency symbol, and use the decimals in [Precision and rounding](#precision-and-rounding).
- **Keep it plain.** Use the plainest standard control and the fewest columns. Fold a derived or helper value into a figure that's already shown rather than giving it a column of its own. A real cost component, though, gets its own column, and the line's total includes it.
- **Grids**: every `SfDataGrid` sets `GridLinesVisibility="Both"` and `CurrentCellBorderBrush="{StaticResource AccentBrush}"`. A grid with height to spare uses `controls:GridFillerRows.IsEnabled="True"`. A grid whose lines are typed into shows a row header. Quantity columns use `NumericColumn` with `NumberDecimalDigits="1" TypedDecimalDigits="4"`, which needs `controls:NumericEditing.IsEnabled="True"` on the grid.

### Working with Syncfusion

- **Prefer a Syncfusion control** to its native WPF equivalent (`ButtonAdv` over `Button`, `TabControlExt` over `TabControl`, `ComboBoxAdv`, `SfDataGrid`, `SfTextBoxExt`, `DoubleTextBox`), so the whole UI picks up the theme. Use a native control only when Syncfusion has none that fits.
- **To add a control**, add its package's `PackageReference` (with no version) to the UI project, and a `PackageVersion` at `$(SyncfusionVersion)` in `Directory.Packages.props` if it's a new package. The theme package already styles every control.
- **To learn how a control is meant to be used**, start with its `syncfusion-wpf-*` skill in `.claude/skills/`. Then read the Sample Browser source in `C:\Users\Public\Documents\Syncfusion\WPF\34.2.2\SampleBrowser\` (a v34 install, older than the packages), and grep file contents there, because the demo file names often don't name the control. Only then decompile the assembly with `ilspycmd`: the packages are in `%UserProfile%\.nuget\packages\syncfusion.*\<version>\lib\`.
- **Known traps:**
  - Bind an editable `DoubleTextBox`'s `Value` with `UpdateSourceTrigger=PropertyChanged`. Otherwise the ViewModel only sees the value when the box loses focus, and the buttons and figures that depend on it lag behind.
  - A `DoubleTextBox` bound to a `decimal?` needs `TargetNullValue={x:Null}` on its binding.
  - A `ButtonAdv` takes its icon from `IconTemplate`, not `SmallIcon` or `LargeIcon`. Always set its `Label`, which otherwise reads "Button".
  - An `SfTreeGrid` must be themed on its own (its own `SfSkinManager.Theme` and `SizeMode`), never through its window. Otherwise it un-themes every `SfDataGrid` in the window. `TreeGridThemeScopeTests` checks this.
  - A grid's columns aren't in the visual tree, so a column property can't bind to the screen. Use a behaviour such as `OptionalColumns` instead.
  - Syncfusion's number boxes take Ctrl+Z and Ctrl+Y for themselves. That's why `UndoShortcuts` exists.
  - A window built over long-lived ViewModels must release its grids, text boxes and picker filters when it closes, as `SettingsWindow.ReleaseControls` does, or it stays in memory.

### Printing

The Products screen prints a product, its scaled recipe, or a whole folder of products. The parts, in [Printing/](../src/TinapayanRMS.UI/Printing/):

1. `ProductsViewModel.PrintRequests` asks for the Print tab. `MainWindow` hands the choice to `PrintTabViewModel.Pick` and shows the tab.
2. A **`ProductPrintout`** (immutable records, in `ViewModels/ProductPrintout.cs`) holds what's printed. It's taken from the screen as shown, unsaved edits included, or from `SavedProductReader` for a set of saved products.
3. **`ProductPrintLayout`** builds the page content in code, as blocks.
4. **`PrintPagination`** is pure: it splits the blocks into pages, keeps headings with what follows them, and continues a long table under "(continued)".
5. **`ProductPrintManager`**, a subclass of Syncfusion's `PrintManager`, lays the printout out again whenever the paper, margins or orientation change. It overrides `PrintDocument` to print on the printer's own ticket and to report errors, which the base class swallows.
6. Syncfusion's **`PrintPreview`** pane shows the pages. `PrintPaneFit` and `PrintPaneTemplates.xaml` fit the pane to the app's 14 px text. The window must be themed before the pane goes into it.

### Exporting to Excel

Reports' tables go to an Excel workbook with **Export to Excel**. The parts, in [Exporting/](../src/TinapayanRMS.UI/Exporting/):

1. The ViewModel's command calls `ExportToExcelAsync(title)`, which raises `ViewModelBase.ExportToExcel` with the export's title and, once the view answers with a path, sets `SuccessMessage` to where the file went. It runs inside `RunSafelyAsync`, so a file open in Excel shows as an error notice.
2. The view answers through `RegisterExcelExport(this, title => sheets)` (`ExcelExportHandlerRegistration`): it asks for a path with a Save dialog, then writes the `ExcelSheet`s it picks for that title. The grids are the view's, because the sort, the filters and the hidden columns the user chose live on them, not in the ViewModel.
3. **`ExcelExport.Save`** writes each grid on a sheet of its own through Syncfusion's grid converter (`Syncfusion.DataGridExcelExport.WPF`, which brings XlsIO, licensed by the same key). It exports the grid's `View`, so the current sort and filters come with it. It also does three things the converter leaves undone:
   - it leaves out hidden columns by name (`ExcludeColumns`), since the converter writes them too;
   - it gives each numeric column the grid's decimals with a thousands separator (`#,##0.00`), since the converter's own format has none;
   - it fits the columns to their contents, since the converter copies the grid's on-screen widths.
   `GridFillerRows`' padding is a grid's unbound rows, which stay out because `ExportUnBoundRows` is left false. Figures are written as numbers at full precision; the format only shows them rounded. The workbook is built in memory before the file is touched, so a file that can't be written is left as it was.

`ExcelExportTests` checks all of this against a real grid read back with XlsIO.

The Production Plan prints through the same layout and manager: a `ProductPrintSet` made with `ProductPrintSet.Of(PrintedPlan, …)` holds no products, and `ProductPrintLayout.AddPlan` lays out what to make, the mixing sheet and the purchase requisition with the same headings, tables and footer. It opens in [PrintPreviewWindow](../src/TinapayanRMS.UI/Views/PrintPreviewWindow.xaml.cs), a dialog that themes itself before the pane goes in, so its opener mustn't theme it again.

The What-If Prices screen's **Print Report** goes through the same dialog, with each product's recipe and costing at the what-if prices beside today's:

- [WhatIfReport](../src/TinapayanRMS.UI/ViewModels/WhatIfReport.cs) pairs a product's `SavedProduct` priced now with the same product priced what-if. Both are built from one `ProductDetail`, so their sections, lines, packaging and products pair up by index. It sets `NowCost` on each line, and `NowBatchCost` on each section, whose cost a change moves, and puts `PrintedWhatIf` (today's `ProductCostSummary` and the changes reaching the product) on the printout. A change reaches a product when `IngredientCostRules.ReachedBy` (the material and every made or linked ingredient depending on it) meets `WhatIfReport.MaterialIdsOf` (its sections', add-ins' and packaging's materials, and those of the products a composite holds).
- `WhatIfViewModel` keeps the last recalculation's what-if `SavedProduct`s and the changes it tried, so the report always matches the table. `PrintChoices` builds a `PrintChoice` for the rows picked in the grid (`SelectedProducts`, bound to the grid's `SelectedItems`, picked again by id after each recalculation) and one for every row shown. `ShownProducts` is the grid's `View.Records`, which the view supplies, so the report follows the column filters and sort.
- A `ProductPrintSet` with `PriceChanges` set is a What-If report. With the costing included, `ProductPrintLayout` gives its first page the price changes and a contents with each product's base cost now and what-if. Each product gets a Price Changes Applied table, a Cost Now column on every recipe, packaging and products table (moved lines in the accent), and a costing with Now, What-If and Change columns. Without the costing it prints the recipes as a plain set does.
- `PrintPreviewWindow(IReadOnlyList<PrintChoice>, Theme)` shows a Print dropdown when there's more than one choice, and Recipe and Costing tick boxes for products (the plan keeps its single Costs).

### The Production Plan

[ProductionPlanRules](../src/TinapayanRMS.ApplicationCore/Rules/ProductionPlanRules.cs) is pure: from the products to make (`PlanRecipe`s, by id), it adds up each section's unrounded batches (`RecipeScalingRules.BatchRatio`) per sub-assembly and set of add-ins, rounds them up once (`WholeBatches`), then opens made ingredients into their BOMs and linked ones into what they're cut from, taking the largest of one ingredient's parts. [ProductionPlanBuilder](../src/TinapayanRMS.UI/ViewModels/ProductionPlanBuilder.cs) maps saved products (`SavedProduct`) to its inputs and names and costs its result. `ProductionPlanViewModel` keeps the plan in the view-state file under `production-plan`, as `"id:units"`.

### Logging and errors

- Serilog's static `Log.Logger` is built in `App`'s static constructor, so even a licence failure is logged. It writes one rolling file a day under `logs/`, keeping 31. It's registered in DI as a `Serilog.ILogger`.
- `RunSafelyAsync`, `LoadSafelyAsync` and `LoadLatest` log a refusal as a warning, and any other failure as an error with its stack.
- `App` handles an exception that nothing else caught on the UI thread (`DispatcherUnhandledException`): it logs it, shows a message saying the app is still running, and carries on. If more than three arrive within ten seconds, [UnhandledErrorGuard](../src/TinapayanRMS.UI/UnhandledErrorGuard.cs) lets the app close, since they'd come faster than anyone could act on them. A failure ReactiveUI was left holding arrives wrapped in its `UnhandledErrorException`, and `App` shows the cause inside. It logs exceptions from other threads (`AppDomain.UnhandledException`) and unobserved task exceptions.
- Once the main window shows, `App` checks off the UI thread for migrations the database doesn't have yet, and says so in a message.
- In Debug builds, [BindingErrorLog](../src/TinapayanRMS.UI/BindingErrorLog.cs) writes WPF's binding errors to the log as `WPF binding …` warnings, even without a debugger attached. Some are expected at every startup and harmless: one `Cannot find governing FrameworkElement` for each `GridMultiColumnDropDownList.ItemsSource` or `GridCheckBoxColumn.AllowEditing` binding while its grid is built, and a `FindAncestor` warning from Syncfusion's `TabItemExt` template.

---

## 10. Testing

### How the tests work

- **Nothing is mocked.** Rules are tested as pure functions. Repositories, services and ViewModels are tested on a real SQL Server database that Respawn resets before every test. Grid behaviours are tested in real `SfDataGrid`s in windows placed off-screen.
- **Each database-backed project has its own database**: `TinapayanRMS_Test_Infrastructure` and `TinapayanRMS_Test_UI`. `dotnet test --solution` runs the projects side by side, and a shared database would be reset under the other project's running test.
- **Each database is migrated once per test run**, by the fixture's `MigrateAsync`, and its `AUTO_CLOSE` is turned off.
- **Respawn clears every table** except `__EFMigrationsHistory` before each test. That includes rows the migrations seeded, so a test creates every row it needs.

| Project | What it tests | Needs |
|---|---|---|
| `ApplicationCore.Tests` | `Rules/` and `Validators/`, as pure functions | Nothing |
| `Infrastructure.Tests` | `Persistence/` (repositories and the cascade), `Queries/` (`ColumnsTests`), `Services/` (`JsonViewStateStoreTests`) | SQL Server Express |
| `UI.Tests` | `ViewModels/` on real repositories, `Controls/` in off-screen grids, `Printing/`, `Converters/`, `CompositionRootTests` | SQL Server Express; the licence key for the tests that build Syncfusion controls |
| `Tests.Common` | Nothing: it's the shared fixture library | — |

### The database fixtures

- **[DatabaseFixtureBase](../tests/TinapayanRMS.Tests.Common/Fixtures/DatabaseFixtureBase.cs)** (in Tests.Common) reads the app's connection string, swaps in the project's database name, migrates the database, and resets it with Respawn.
- **`DatabaseFixture`** and **`DatabaseCollection`** are defined in each project's own `Fixtures/` folder, because xUnit only finds a collection definition in the tests' own assembly. Each `DatabaseFixture` is one line that names its database.
- **[PersistenceTestBase](../tests/TinapayanRMS.Tests.Common/Fixtures/PersistenceTestBase.cs)** resets the database before each test. It provides `CreateDbContext()`, `CreateDbContextFactory()`, the test's `Cancellation` token, and seeding helpers such as `AddIngredientAsync`, `AddRoleAsync`, `AddProductAsync`, `AddProductWithSubAssemblyAsync`, `SetPriceAsync` and `SeedUnitOfMeasureAsync`.
- **[ThrowawayDatabase](../tests/TinapayanRMS.Tests.Common/Fixtures/ThrowawayDatabase.cs)** is a migrated database of one test's own on the same server, dropped when the test is done. The backup tests restore over it, which would put every other test out of the shared database.
- **Every concrete test class must carry `[Collection(DatabaseCollection.Name)]`.** It's deliberately left off the base class: xUnit isn't guaranteed to honour it there, and without it tests could run in parallel against the one database.

### Writing tests

A rule test needs no fixture:

```csharp
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class CostingRulesTests
{
    [Fact]
    public void CalculateSellingPrice_adds_the_markup_then_the_VAT()
    {
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 20m, packagingCostPerPiece: 2m,
            laborPercent: 25m, overheadPercent: 10m, administrativePercent: 5m, marginPercent: 30m, vatPercent: 12m);

        baseCost.Should().Be(30m);
        preVatPrice.Should().Be(39m);
        sellingPrice.Should().Be(43.68m);
    }
}
```

A persistence test acts through the repository, as the app does, and asserts on a fresh read:

```csharp
using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class ProductCategoryRenamePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Renaming_a_category_changes_only_its_name()
    {
        var repository = new ProductCategoryRepository(CreateDbContextFactory());
        var breads = new ProductCategory { Name = "Breads" };
        await repository.AddAsync(breads, Cancellation);

        await repository.UpdateAsync(new ProductCategory { Id = breads.Id, Name = "Loaves" }, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { breads.Id, Name = "Loaves" });
    }
}
```

A ViewModel test builds the ViewModel on real repositories, stands in for the dialogs, and drives its commands. Abridged from [SubAssemblyRolesViewModelTests](../tests/TinapayanRMS.UI.Tests/ViewModels/SubAssemblyRolesViewModelTests.cs):

```csharp
[Collection(DatabaseCollection.Name)]
public class SubAssemblyRolesViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRolesViewModel CreateViewModel(bool confirm)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var viewModel = new SubAssemblyRolesViewModel(
            new SubAssemblyRoleRepository(CreateDbContextFactory()), new SubAssemblyRoleValidator(), new LoggerConfiguration().CreateLogger());
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(confirm));   // stands in for the MessageBox
        return viewModel;
    }

    [Fact]
    public async Task Declining_the_question_saves_nothing()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var viewModel = CreateViewModel(confirm: false);
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRole = viewModel.Roles.Single(r => r.Id == fillingRoleId);

        viewModel.EditingIsProductOwned = false;
        await viewModel.SaveCommand.Execute();

        // … assert on the ViewModel, and on a fresh read from the database …
    }
}
```

Some loads start from a selection, with nothing to await. Tests of those wait until a condition has held for a moment: see the helpers in `ProductsViewModelRefreshTests`.

A test that creates WPF objects runs on an STA thread:

- `StaThread.Run(() => …)` for plain WPF objects.
- `StaThread.RunLicensed(() => …)` when it builds Syncfusion controls. It's skipped if `SYNCFUSION_LICENSE_KEY` isn't set, because an unlicensed control shows a dialog.
- `StaThread.RunThemed(() => …)` when it themes a window. All such tests share one STA thread, with the app's theme settings, because SfSkinManager ties a theme's resources to the thread that loaded them.
- `FailingDbContextFactory` (in UI.Tests' `Fixtures/`) wraps a factory and refuses every context while its `Fail` is set: for a test that a screen survives a database that stops answering, and recovers once it answers. A test that a write is all or nothing fails it part-way with an EF Core save interceptor instead (`CostCascadeTransactionPersistenceTests`).
- `SqlCommandCounter` (also in UI.Tests' `Fixtures/`) counts the SQL commands sent from where it's started: for a test that a read's round trips don't grow with what it reads.
- `OffscreenGrid.Run(create, test)` shows a real grid off-screen. Its test classes carry `[Collection(OffscreenGridCollection.Name)]`, so that only one off-screen window is up at a time.

### Naming

Test classes are named `<Subject>Tests`, such as `CostingRulesTests` or `ProductSavePersistenceTests`. Test methods are sentences with underscores: either `Method_scenario_result` (`EnsureValidParameters_rejects_negative_labor_cost`) or a plain statement (`Declining_the_question_saves_nothing`). CA1707 is turned off in `tests/` for this.

### Coverage

Coverage comes from Microsoft's MTP coverage extension (see the [command reference](#command-reference)); `coverlet` doesn't work under Microsoft.Testing.Platform. When you read the raw report, ignore `Infrastructure/Migrations`: its generated code never runs under the reset-a-migrated-database model, and it drags Infrastructure's raw figure down.

---

## 11. Conventions

### Code

- **Use modern C# 14 and .NET 10 idioms**: primary constructors (every repository has one), collection expressions, `record` and `record struct` for immutable shapes (drafts, snapshots, printouts), `required` members, file-scoped namespaces and pattern matching.
- **Nullable reference types are on everywhere.** Treat their warnings as real.
- **Everything that touches the database is `async`** and takes a `CancellationToken`. ViewModels pass `CancellationToken.None`.
- **A rule throws `BusinessRuleException` with a message written for the shop's staff**, because `RunSafelyAsync` shows it exactly as written. That goes for a repository's checks and a screen's own form checks too. A plain `InvalidOperationException` reads as a bug: EF Core and SqlClient throw those for failures.
- **Strings**: compare names with `StringComparison.OrdinalIgnoreCase`; sort for display with `StringComparer.CurrentCultureIgnoreCase`; format display dates with `CultureInfo.CurrentCulture`, and log output with `CultureInfo.InvariantCulture`.
- **Correctness and performance come before brevity.** Query only the rows and columns you need, rather than loading more and filtering in memory. Avoid needless intermediate collections in hot paths such as the cascade.
- **Comments explain why.** Types and non-obvious members have XML doc comments that give their domain meaning. Inline comments record a decision and its reason, often with its date. Match the comment density of the code around yours.
- **Keep the build free of warnings.**

### Documentation and process

- **The user manual follows the app.** A change users can see updates [docs/user-manual.md](user-manual.md) in the same change. The manual is written for the shop's staff: it describes current behaviour only, shows labels in bold exactly as the screen does, gives amounts without a currency symbol, and uses no code names.
- **PLAN.md is the living plan.** Record each decision there, with its reason and date, and keep its sections current. History belongs in PLAN.md; the manuals describe only what's true now.
- **This manual follows the code.** A change to anything it describes (the setup, commands, solution layout, architecture, a pattern or convention, or a type or file it names) updates it in the same change. Like the user manual, it describes only what's true now.
- **Verify a UI change in the running app**, not just in the tests. Close any copy you already have open first.
- **Commit messages** are short sentences in the imperative that describe the change, as in "Let a raw material have any number of alternative units".

---

## 12. Common changes, step by step

### Add a field to an existing entity

1. Add the property to the entity in `Domain/Entities`, with an XML comment that gives its meaning.
2. Configure it in the entity's `*Configuration` class if conventions aren't enough: a length, precision, conversion, default or check constraint.
3. Add the column to its entity's list in `Infrastructure/Queries/Columns.cs`. `ColumnsTests` fails until you do.
4. Copy it in the repository's write path: the `UpdateAsync` that loads and then applies, and the field copy at the top of the aggregate `SaveAsync`, if there is one.
5. Add any rule it needs to the right `*Rules` class (and its tests), and to the validator if a form edits it.
6. Generate, read and apply the migration (see [Migrations](#migrations)).
7. In the ViewModel, add the editing property, its load and save mapping, and its place in the form snapshot (so undo and the dirty check include it). Bind it in the view.
8. Add tests: rules, persistence, and the ViewModel.
9. Update the user manual if users can see the field, and PLAN.md's Domain Model section.

### Add an entity and its table

Everything in the previous recipe, plus:

1. A `DbSet` in `TinapayanRMSDbContext`, named as the table should be.
2. An `IEntityTypeConfiguration<T>` in `Configurations/`, with the unique indexes, delete behaviours and check constraints the rules need.
3. A repository interface in `ApplicationCore/Interfaces`, and its implementation in `Infrastructure/Repositories`, taking `IDbContextFactory<TinapayanRMSDbContext>` through its primary constructor. Reads use Dapper and `Columns`; writes use EF Core.
4. Registrations in `CompositionRoot`: the repository as a singleton, and `IValidator<T>` if the entity has a Save form.

Respawn picks up the new table by itself.

### Add a business rule

1. Add a static method to the right `*Rules` class in ApplicationCore. Keep it pure, and make it throw `BusinessRuleException` with a message a user can act on.
2. Call it from the repository's write path, before anything is deleted or written.
3. If a form can break the rule, mirror it in the entity's validator for an immediate message.
4. If the database can enforce it, add a check constraint or unique index as a backstop, which needs a migration.
5. Test it in `ApplicationCore.Tests/Rules`, and test the write path that calls it in `Infrastructure.Tests/Persistence`.

### Change a cost formula

1. Read PLAN.md's [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade), and the Domain Model bullets for the entities involved.
2. Change or add the formula in its Rules class, with unit tests.
3. If the figure is stored, make sure every write path that can change one of its inputs recomputes it, and that the propagators do too. If it's computed on read, the Rules change is enough: `SavedProductReader` and the screens share it.
4. Extend `ReactiveCostCascadeTests` with the end-to-end case.
5. Update the user manual's [How costs are worked out](user-manual.md#12-how-costs-are-worked-out), including its worked example, in the same change.
6. Record the decision in PLAN.md.

### Add a screen

1. Write the ViewModel in `ViewModels/`, deriving from `ViewModelBase`. Make it `sealed`, and `IDisposable` if it owns a `SourceCache`. Give it a fetch-then-merge `LoadCommand`. Register it as a singleton in `CompositionRoot`.
2. Write the view in `Views/`: a `UserControl` whose constructor takes the ViewModel, sets `DataContext` and calls `RegisterNativeMessageBoxHandler()`. Give it an `ErrorToast`.
3. In `MainWindow.xaml`, add a `TabItemExt` to `MainTabControl`, and a `ListBoxItem` to the navigation menu with an `AutomationProperties.Name`.
4. In `MainWindow.xaml.cs`, set the tab's content, add its `LoadCommand` to `_loadCommands`, and add its `HistoryScreen` to `_historyScreens`, with a new `AppScreen` value.
5. Add a resolve check to `CompositionRootTests`, and ViewModel tests.
6. Add a section to the user manual.

### Add a Settings tab

1. Write the ViewModel and view as for a screen, and register the ViewModel.
2. Add the tab to `SettingsWindow.xaml`. In `SettingsWindow`'s constructor, set its content, add its `RegisterNativeMessageBoxHandler()` to the disposables, and run its `LoadCommand` on `Loaded`. A view that answers interactions of its own (file dialogs, as the Database tab's does) registers them in its constructor and is `IDisposable`; add the view itself to the disposables, so its handlers go when Settings closes.
3. Pass the ViewModel through `MainWindow` to `OpenSettings`.

### Export a grid to Excel

1. Give the grid an `x:Name`.
2. In the ViewModel, add a command that runs `ExportToExcelAsync("<title>")`. The title names the file and lets the view tell several exports apart.
3. In the view's constructor, call `viewModel.RegisterExcelExport(this, title => [new ExcelSheet("<sheet>", TheGrid)])`, and give the view a `SuccessToast` for the "Saved … in …" notice.
4. Add an **Export to Excel** `ButtonAdv` with `ExportIconTemplate`, bound to the command.
5. Test the command with a handler of your own on `ExportToExcel`.

### Use a new Syncfusion control

1. Add its package to the UI project, as in [Working with Syncfusion](#working-with-syncfusion).
2. Learn how it's meant to be set up from its skill, then the Sample Browser, then `ilspycmd`.
3. Check that it doesn't change how other controls are themed (remember the `SfTreeGrid` trap).
4. Check it in the running app at the app's text size.

### Add a grid behaviour

1. Write a static class in `Controls/` with an attached property, such as `IsEnabled` or `Recorder`. In the property's change callback, hook the grid's events, and unhook them when the grid unloads.
2. Test it in a real grid with `OffscreenGrid`, in `UI.Tests/Controls`.

---

## 13. Diagnostics and troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| The app closes at startup, and the log says `SYNCFUSION_LICENSE_KEY` isn't set | The variable isn't visible to the process | Set the user environment variable, then restart your terminal or IDE |
| A screen says "Couldn't reach the database.", and the log has "A network-related or instance-specific error" | SQL Server Express isn't running, or the instance name differs | Start the `MSSQL$SQLEXPRESS` service, and check `appsettings.json` |
| A message at startup says the database is out of date | A migration hasn't been applied to it | `dotnet ef database update --project src/TinapayanRMS.Infrastructure` |
| A message says something went wrong and the app is still running | An exception nothing else caught | Read the log for its stack |
| Queries that take about 1 ms take 10–50 ms, and vary from run to run | SQL Server is short of memory: Windows trims its plan cache and data, so every query compiles again | Free memory, or give the instance a `min server memory`. Compare timings in one process, alternating before and after |
| `dotnet ef` can't connect | The design-time factory's connection string is hard-coded | Use `.\SQLEXPRESS`, or edit `TinapayanRMSDbContextFactory` |
| The build fails with `CS0103: The name 'InitializeComponent' does not exist`, or `CS2001` for `*.g.cs` files, all against a `*_wpftmp.csproj` | The WPF markup pass has gone stale | Run `dotnet clean src/TinapayanRMS.UI`, build the UI project on its own, then build the solution |
| The build can't copy `TinapayanRMS.UI.exe` or a DLL | The app is running | Close it |
| `--filter` isn't recognised, or zero tests ran | VSTest flags under Microsoft.Testing.Platform | Use `--filter-class`, `--filter-method` and `--coverage` |
| Some UI tests are reported as skipped | No licence key | Set `SYNCFUSION_LICENSE_KEY` |
| The first query after a pause is slow | `AUTO_CLOSE` is on | `ALTER DATABASE … SET AUTO_CLOSE OFF` |
| A binding silently does nothing | A binding error | Check the Debug build's log for `WPF binding` warnings |
| Every `SfDataGrid` suddenly looks unthemed | An `SfTreeGrid` is themed through its window | Theme it locally |
| The UI freezes | Something blocks the UI thread | `dotnet-stack report -p <pid>`. Find the PID with `dotnet-trace ps`. |
| A crash, deadlock or memory growth | | `dotnet-dump collect -p <pid>`, then `dotnet-dump analyze <file>` |
| Something is slow | | `dotnet-trace collect -p <pid>` |
| A test harness stalls for seconds while building a view with a chart | Syncfusion's telemetry blocks when there's no `SynchronizationContext` | Build views inside a running dispatcher (`Dispatcher.CurrentDispatcher.InvokeAsync` after `Dispatcher.Run()`), or call `Syncfusion.Telemetry.Telemetry.Disable()` first |

When you compare the behaviour of two versions of the code, clean the UI project before each build and check that the output says "Build succeeded": a stale markup pass can leave the previous binaries in place. If it matters, confirm what was built with `ilspycmd -t <Type> <dll>`.

### Driving the UI from a script

Off-screen harnesses and UI-automation scripts have pitfalls that PLAN.md records in detail (Application Architecture and Development Environment). In short:

- Make the script's thread per-monitor DPI-aware before it places, clicks or captures anything, because the app is per-monitor DPI-aware ([app.manifest](../src/TinapayanRMS.UI/app.manifest)).
- Find the main window by its class and title. `Process.MainWindowHandle` can pick one of the app's hidden windows.
- Syncfusion's buttons, pickers and number boxes need real clicks: `ButtonAdv` has no Invoke pattern, and `ValuePattern.SetValue` doesn't commit a `DoubleTextBox`.
- Bring the app to the foreground before you send keystrokes, and check it's still there, or the keys go to whatever window is in front.
- UI Automation's `FocusedElement` reports the window, not the grid, while an `SfDataGrid` has the keyboard. Judge a grid's focus from the screen instead, for example by whether the current cell's border moves with the keys.
- `new App()` posts WPF's startup to the dispatcher, so a harness that runs the dispatcher also runs `OnStartup`, which shows the real main window. Collect what it posts and abort it first.

---

## 14. Further reading

### PLAN.md, by section

| Section | Read it when |
|---|---|
| [Tech Stack](../PLAN.md#tech-stack) | You touch a package, the ReactiveUI bootstrap, or the test runner |
| [Solution Structure](../PLAN.md#solution-structure) | You add a project or a folder |
| [Domain Model](../PLAN.md#domain-model) | You change an entity or a rule. Each entity's bullets give its rules and the reasons for them. |
| [Syncfusion UI Framework](../PLAN.md#syncfusion-ui-framework) | You use a Syncfusion control, or change the theme, the DPI handling or printing |
| [Application Architecture](../PLAN.md#application-architecture) | You change a screen, a repository pattern or a migration |
| [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) | You change any cost |
| [Development Environment](../PLAN.md#development-environment) | You set up a machine, or change the tests' infrastructure |
| [Build Plan](../PLAN.md#build-plan) | You want the history: every phase and dated change |
| [Conventions](../PLAN.md#conventions) | Always |

### Library documentation

- ReactiveUI: <https://www.reactiveui.net/docs/>
- DynamicData: <https://github.com/reactivemarbles/DynamicData>
- EF Core: <https://learn.microsoft.com/ef/core/>
- Dapper: <https://github.com/DapperLib/Dapper>
- FluentValidation: <https://docs.fluentvalidation.net/>
- Syncfusion WPF: <https://help.syncfusion.com/wpf/welcome-to-syncfusion-essential-wpf>
- xUnit: <https://xunit.net/>
- Respawn: <https://github.com/jbogard/Respawn>
- Serilog: <https://serilog.net/>
- Catppuccin palette: <https://catppuccin.com/palette>
