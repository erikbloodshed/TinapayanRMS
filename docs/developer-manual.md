# TinapayanRMS Developer Manual

TinapayanRMS is the recipe and costing app of Tinapayan Festival Bakeshoppe. It's a Windows desktop app built with WPF, Syncfusion and ReactiveUI on .NET 11, and it keeps its data in PostgreSQL through EF Core and Dapper. The shop records what its raw materials cost. The app works out what every recipe, product and selling price comes to, and when a price changes it carries the change up to every product that uses it.

This manual is for developers who build, maintain or extend the app. It explains how the code is organised, which patterns it follows and why, and what you need to know and install before you change it. It assumes you know C#, but not this codebase.

> **PLAN.md is the source of truth.** This manual describes the code as it is and points into [PLAN.md](../PLAN.md) for the reasons and history behind each decision. Where the two disagree, the code and PLAN.md win: fix this manual.

| Document | Written for | What it covers |
|---|---|---|
| [docs/user-manual.md](user-manual.md) | The shop's staff | Every screen, and how costs are worked out, as a user sees them |
| docs/developer-manual.md (this file) | Developers | How the code is built, and how to change it safely |
| [PLAN.md](../PLAN.md) and [docs/plan/](plan/) | Developers and AI agents | Every design decision with its reason and date: tech stack, domain model, architecture, costing cascade and conventions in PLAN.md; each main screen's design notes, the Syncfusion notes and the build plan in `docs/plan/` |
| [CLAUDE.md](../CLAUDE.md) | AI coding agents | The commands and rules an agent needs before it changes the code |

PLAN.md is long: an overview ("At a glance"), then about 800 lines of very long bullets. List its sections with `grep -n "^#" PLAN.md` and read the ones your change touches. Its largest parts moved to `docs/plan/` on 2026-10-28 and are still part of it.

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

- **Raw materials.** Ingredients and packaging materials. Each has a base unit, any number of alternative units (a bag of flour, a pack of boxes) and a cost. A purchased material is priced from its brand prices. An ingredient can also be *made* from a sub-assembly, with add-ins of its own on top, or *linked* to another ingredient as a share of its cost (egg white from egg).
- **Sub-assemblies.** In-house recipes such as a dough, a filling or a topping: ingredient lines in grams, with a stored batch weight and batch cost. A sub-assembly in the Packaging role is a *packaging set* instead, such as a gift box with its ribbon.
- **Products.** A recipe of sub-assemblies, each cut into pieces of a set size, plus packaging and costing percentages. The app works out the cost and a cost-plus selling price per sellable unit. A *composite* product holds other products, such as a gift box of assorted breads.
- **Reports, printing and scaling.** The Product Cost Summary and Raw Materials Price Summary reports, printed recipes and costings, and a recipe scaled to any number of units.

It's a single-user desktop app. There's no server, no login and no network service of its own. The database is a local PostgreSQL server, reached as the `tinapayanrms` role with the password in the password file.

### Screens

| Screen | View | ViewModel |
|---|---|---|
| Raw Materials | `RawMaterialsView`, with its cards in `Views/RawMaterials/` | `RawMaterialsViewModel` |
| Sub-Assemblies | `SubAssembliesView`, with its list in `Views/SubAssemblies/` | `SubAssembliesViewModel` |
| Products | `ProductsView`, with its cards in `Views/Products/` | `ProductsViewModel` |
| Reports | `ReportsView` | `ReportsViewModel` |
| Price Simulator | `PriceSimulatorView` | `PriceSimulatorViewModel` |
| Print (opened from Products) | `ProductPrintView` | `PrintTabViewModel` |
| Settings dialog: Units of Measure, Categories, Product Categories, Roles, Costing Defaults, Keyboard | `SettingsWindow`, and one view per tab | `UnitsOfMeasureViewModel`, `CategoriesViewModel`, `ProductCategoriesViewModel`, `SubAssemblyRolesViewModel`, `CostingDefaultsViewModel`, `KeyboardSettingsViewModel` |

The [user manual](user-manual.md) describes every screen. Read its [Key ideas](user-manual.md#1-key-ideas) chapter before you read the code, because the code uses the same words.

### Glossary

| Term | In code | Meaning |
|---|---|---|
| Raw material | `RawMaterial` | Anything bought or made to produce a product. Its `Type` is `Ingredient` or `Packaging`, and it's fixed once saved. |
| Base unit | `RawMaterial.BaseUnitId` | The unit a material's quantities and costs are kept in. It's always the gram for an ingredient (`BaseUnitId` is null) and is chosen per packaging material. |
| Alternative unit | `RawMaterialUnit` | A multiple of the base unit, such as a bag of 25,000 g. Standard units, such as the kilogram and the inch, have fixed multipliers (`StandardUnitRules`). |
| Source | `RawMaterialSource` | Where an ingredient's cost comes from: `Purchased` (brand prices), `Made` (a sub-assembly and its add-ins) or `Linked` (a share of another ingredient's cost). Packaging is always purchased. Fixed once saved. |
| Category type | `RawMaterialCategoryType` | What a `RawMaterialCategory` holds: `Ingredient` (purchased and linked ingredients), `Made` (made ingredients only) or `Packaging`. `RawMaterialRules.CategoryTypeFor` gives the one a material goes in, and `MaterialTypeOf` the material type a category holds, so a Made category's tree folder sits in Ingredients. |
| Brand price | `RawMaterialBrandPrice` | What one brand of a purchased material costs now, in one of its units. The material is costed at its dearest brand per base unit, or at the brand in its `PreferredBrand` (Cost From) while that has a price. |
| Price history | `RawMaterialPriceHistory` | A log of the prices a material was costed at. Deleting an entry reprices nothing. |
| Bill of materials (BOM) | `IngredientBomLine` | A made ingredient's components, in grams: the lines of the sub-assembly it's made from (`RawMaterial.SubAssemblyId`), then its own add-ins (`IngredientAddInLine`). A read model, never stored (`Infrastructure/Queries/IngredientBoms`). BOMs nest to any depth. `WeightLossPercent` shrinks the output. |
| Cost buffer, buffered cost | `CostBufferPercent`, `BufferedCostPerBaseUnit` | A cushion on top of the true cost (`CostPerBaseUnit`). Everything downstream is costed at the buffered cost. |
| Sub-assembly | `SubAssembly` | A recipe of ingredient lines (`SubAssemblyLine`), or a packaging set of packaging lines (`SubAssemblyPackagingLine`). Its batch weight and cost are worked out from its lines whenever it's read. |
| Role | `SubAssemblyRole` | What a sub-assembly is in a recipe (Dough, Filling, …). Roles set the order of a product's recipe tabs. `IsPackaging` marks the role packaging sets go in. `IsProductOwned` makes a section made in a product that product's own. |
| Standalone or own | `SubAssembly.OwnerProductId` | A standalone sub-assembly (null owner) lives on the Sub-Assemblies screen, and products link it. An own sub-assembly belongs to one product and is edited in that product's recipe. |
| Usage | `ProductSubAssemblyUsage` | One sub-assembly in a product's recipe, with the product's own piece size. The piece size is either a weight per piece or a yield: one is typed and the other is worked out from the batch's total weight. |
| Add-in | `UsageAddInLine` | A product's own extra ingredient on top of a sub-assembly it links. |
| Packaging line, packaging link | `PackagingLine`, `PackagingLink` | A packaging material used by one sellable unit; or a link that reads a product's packaging from another product or from a packaging set. |
| Quantity Required | `Product.QuantityRequired` | Pieces per sellable unit: 8 for a Box of 8. A recipe is written per piece, and costing is per sellable unit. |
| Composite product | `Product.IsComposite`, `ProductComponent` | A product made of other products, plus its own packaging and sections in product-owned roles. |
| Costing parameters, costing defaults | `CostingParameters`, `CostingDefaults` | A product's labor, overhead, administrative and markup percentages; and the values a new product starts with (its stage among them), with the cost buffer a new raw material starts with and the shop's VAT and Wastage %, which every product is priced with (`ProductDetail.VatPercent` and `WastagePercent`, read with every product). A version snapshot keeps the VAT and wastage it was priced with, and the History tab lists a change in either after the product's own changes (`ProductVersionRules.ShopChanges`), never as one: a Save records no version for them alone. The Products list's **Apply Costing Defaults** sets the products picked to the defaults' percentages, with a version of each one it changes (`VersionedSaves.ApplyCostingAsync`). |
| Stage | `ProductStage` | Research and Development, or Production: the Products tree's two top folders. |
| Baker's percentage | `SubAssemblyLine.IsFormulaBase`, `AuthoredBakersPercent` | A line's weight as a percentage of the total weight of the lines ticked as the formula's base. |

---

## 2. Prerequisites

### Knowledge

You'll be productive sooner if you're comfortable with the following.

| Area | What to know | Where it's used |
|---|---|---|
| C# 15 and .NET 11 | Primary constructors, records and `record struct`, `required` members, collection expressions, pattern matching, nullable reference types, `async`/`await` | Everywhere |
| WPF and XAML | Data binding and `DataContext` inheritance, dependency and attached properties, resources, styles and templates, routed events, the `Dispatcher` and STA threads | `src/TinapayanRMS.UI` |
| MVVM with ReactiveUI 26 and Rx | `ReactiveObject`, `RaiseAndSetIfChanged`, `WhenAnyValue`, `ToProperty` (`ObservableAsPropertyHelper`), `ReactiveCommand`, `Interaction`, `Subject`, Rx operators | `ViewModels/` |
| DynamicData | `SourceCache<T, TKey>`, `Connect().SortAndBind(...)` | List ViewModels |
| EF Core 11 | `DbContext`, `IEntityTypeConfiguration<T>`, migrations, change tracking, transactions, `ExecuteDeleteAsync` | Writes and the schema, in `Infrastructure` |
| Dapper and PostgreSQL SQL | Multi-mapped joins (`splitOn`), recursive CTEs (`WITH RECURSIVE`, stopped at a loop by `CYCLE`), arrays with `unnest` | Reads, in `Infrastructure` |
| PostgreSQL | Roles and the password file, snake_case names, `psql`, `pg_dump` and `pg_restore`, ICU collations | Setup, tests, backups, debugging |
| Syncfusion Essential Studio for WPF | `SfDataGrid`, `SfTreeView`, `SfTreeGrid`, `ComboBoxAdv`, `TabControlExt`, `ButtonAdv`, `DoubleTextBox` and its kin, `SfNavigationDrawer`, `ChromelessWindow`, `SfSkinManager`, `PrintPreview` | Every view |
| FluentValidation | `AbstractValidator<T>` | `ApplicationCore/Validators` |
| .NET Generic Host and DI | `HostBuilder`, service lifetimes | `CompositionRoot.cs` |
| Serilog | The static `Log.Logger`, `ForContext` | Logging |
| xUnit v3 on Microsoft.Testing.Platform | Facts, theories, fixtures, collections | `tests/` |
| AwesomeAssertions | `Should()` assertions. It's a fork of FluentAssertions 7, with the same API. | `tests/` |
| Respawn | Resetting a database between tests | The test fixtures |
| MSBuild | `Directory.Build.props`, Central Package Management | The build |
| The domain | Recipe costing in a bakery: bills of materials, yield, wastage, baker's percentage, cost-plus pricing | Read the user manual's [Key ideas](user-manual.md#1-key-ideas) and [How costs are worked out](user-manual.md#12-how-costs-are-worked-out) |

### Software

| Tool | Version | Notes |
|---|---|---|
| Windows | 10 or 11 | WPF only runs on Windows. Icons use Segoe Fluent Icons (Windows 11), and fall back to Segoe MDL2 Assets on Windows 10. |
| .NET SDK | 11.0.100-rc.3.26522.148 (a preview), or a later 11.0 feature band | Pinned in [global.json](../global.json) with `allowPrerelease` and `rollForward: latestFeature`. |
| PostgreSQL | 18 (EDB's Windows installer, service `postgresql-x64-18`, `localhost:5432`) | Reached as the `tinapayanrms` role, which must be able to create databases (`CREATEDB`), because the tests create their own. Its password goes in the password file, `%APPDATA%\postgresql\pgpass.conf`, so no connection string carries one. The installer's `bin` folder holds `psql`, and the `pg_dump` and `pg_restore` the backups run. |
| `dotnet-ef` | 11.0.x, matching EF Core (`EFCoreVersion` in [Directory.Packages.props](../Directory.Packages.props), now 11.0.0-rc.3.26522.148) | `dotnet tool update --global dotnet-ef --version 11.0.0-rc.3.26522.148` (it installs the tool if it isn't there) |
| Syncfusion licence key | Essential Studio 35.x | Set as the `SYNCFUSION_LICENSE_KEY` user environment variable. The build compiles it into the app, so the app runs on a computer without the variable, but an app built without it won't start, and `dotnet publish` fails without it. The UI tests that build Syncfusion controls read the variable themselves and are skipped without it. After changing the key, rebuild. A key covers one major version, so moving `SyncfusionVersion` to a new major version needs a new key. A program started before the variable changed, such as the IDE and its terminals, keeps the old value until it's restarted. |
| An IDE | Any with .NET 11 support | Visual Studio, VS Code with the C# Dev Kit, or Rider. Git ignores `.vs/`, `.vscode/` and `.idea/`. |
| Optional tools | | `ilspycmd` (decompiles Syncfusion's assemblies), and `dotnet-stack`, `dotnet-dump` and `dotnet-trace` (diagnose the running app). Syncfusion's Essential Studio installer puts the demo source of every control in `C:\Users\Public\Documents\Syncfusion\WPF\<version>\SampleBrowser\`. |

Docker isn't used anywhere in this project.

---

## 3. Getting started

### First-time setup

Run every command from the repository root.

1. **Check the SDK.** `dotnet --version` should print 11.0.100-rc.3.26522.148 or later.
2. **Set up PostgreSQL.** Install PostgreSQL 18, not an older one: `TinapayanRMSDbContext.Configure` tells Npgsql the server is 18 (`SetPostgresVersion(18, 0)`), so the SQL it writes may use what 18 has. Then, as the `postgres` superuser, create the app's role, and put its password in the password file (one line, `localhost:5432:*:tinapayanrms:<password>`):
   ```powershell
   $psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
   & $psql -h localhost -U postgres -c "CREATE ROLE tinapayanrms LOGIN CREATEDB PASSWORD '<password>'"
   New-Item -ItemType Directory -Force "$env:APPDATA\postgresql" | Out-Null
   Add-Content "$env:APPDATA\postgresql\pgpass.conf" "localhost:5432:*:tinapayanrms:<password>"
   & $psql -h localhost -U tinapayanrms -d postgres -c "SELECT version()"
   ```
3. **Set the Syncfusion licence key** as a user environment variable, then restart your terminal and IDE so they see it:
   ```powershell
   [Environment]::SetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", "<your key>", "User")
   ```
   If you use an AI assistant, also set `SYNCFUSION_API_KEY` the same way, to the API key from your syncfusion.com account. Syncfusion's documentation MCP server needs it (see [Working with Syncfusion](#working-with-syncfusion)). The build doesn't.
4. **Install the EF Core tools** at the `EFCoreVersion` in `Directory.Packages.props`: `dotnet tool update --global dotnet-ef --version 11.0.0-rc.3.26522.148`.
5. **Build:** `dotnet build TinapayanRMS.slnx`.
6. **Create the database.** This creates `tinapayanrms` and applies every migration, including the rows the migrations seed, such as the starting sub-assembly roles:
   ```powershell
   dotnet ef database update --project src/TinapayanRMS.Infrastructure
   ```
7. **Run the app:** `dotnet run --project src/TinapayanRMS.UI`. With an empty database, follow the user manual's [First-time setup](user-manual.md#3-first-time-setup).
8. **Run the tests:** `dotnet test --solution TinapayanRMS.slnx`. The first run creates the test databases `tinapayanrms_test_infrastructure`, `tinapayanrms_test_ui`, `tinapayanrms_test_pricing`, `tinapayanrms_test_mcp` and `tinapayanrms_test_recipeapi`. The tests never touch `tinapayanrms`.

### Connection strings

| Used by | Where it's set |
|---|---|
| The app | `ConnectionStrings:Default` in [src/TinapayanRMS.UI/appsettings.json](../src/TinapayanRMS.UI/appsettings.json) |
| `dotnet ef` | Hard-coded in the design-time factory, [TinapayanRMSDbContextFactory](../src/TinapayanRMS.Infrastructure/TinapayanRMSDbContextFactory.cs) |
| The tests | The app's `appsettings.json`, which `TinapayanRMS.Tests.Common` copies into each test project's output. Each project swaps in its own database name. |

Both are `Host=localhost;Database=tinapayanrms;Username=tinapayanrms`, with no password: Npgsql reads it from the password file. If your server, port or role differ, change both. A connection string may carry a `Password` instead, and the backups pass it on to `pg_dump` and `pg_restore`, but it's then in a file anyone with the folder can read.

The backups look for `pg_dump` and `pg_restore` in `C:\Program Files\PostgreSQL\<version>\bin`, newest version first, then on the `PATH`. Set `Backups:ToolsFolder` in `appsettings.json` to use another folder.

### Where the app writes

- **Logs**: `logs/tinapayanrms-<date>.log` beside the executable. In a debug build that's `src/TinapayanRMS.UI/bin/Debug/net11.0-windows10.0.19041.0/win-x64/logs/`. There's one file a day, and the last 31 are kept.
- **View state**: `%LocalAppData%\TinapayanRMS\view-state.json` remembers which tree folders were left open (on Sub-Assemblies, which were left closed), whether the Vim keys are on (`"vim-keys": ["on"]`), and the Reports tables' filters, sort and hidden columns (`reports-*`, through `GridLayoutStore` and `Controls/GridLayoutKeeping`, which any other grid can use the same way). It's safe to delete.
- **Backups**: `%ProgramData%\TinapayanRMS\Backups`, one folder for every Windows user of the computer. `pg_dump` writes the `.backup` files as the user running the app. The app creates this folder and, as its owner, gives `BUILTIN\Users` Modify on it, inherited by every file, so anyone using the app can copy, restore and delete any backup ([BackupFolder](../src/TinapayanRMS.Infrastructure/Services/BackupFolder.cs)). The `.bak` files SQL Server wrote before the move to PostgreSQL (2026-10-21) are no longer listed and can't be restored. The tests use `%ProgramData%\TinapayanRMS\Test Backups\<guid>` and delete it afterwards.

### Command reference

| Task | Command |
|---|---|
| Build | `dotnet build TinapayanRMS.slnx` |
| Run | `dotnet run --project src/TinapayanRMS.UI` |
| All tests | `dotnet test --solution TinapayanRMS.slnx` |
| One test project | `dotnet test --project tests/TinapayanRMS.ApplicationCore.Tests` |
| One test class | `dotnet test --project tests/TinapayanRMS.ApplicationCore.Tests --filter-class "*UnitConversionRulesTests"` |
| One test | `dotnet test --project tests/TinapayanRMS.UI.Tests --filter-method "*Declining_the_question_saves_nothing"` |
| Only the test projects your changes reach (see [Running fewer tests while developing](#running-fewer-tests-while-developing)) | `powershell -ExecutionPolicy Bypass -File scripts/Test-Changed.ps1` |
| One class again each time you save, by hot reload | `powershell -ExecutionPolicy Bypass -File scripts/Watch-Tests.ps1 UI -Class "*ProductsViewModelRefreshTests"` |
| Coverage (a Cobertura report in `TestResults/`) | `dotnet test --solution TinapayanRMS.slnx --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml` |
| Add a migration | `dotnet ef migrations add <Name> --project src/TinapayanRMS.Infrastructure` |
| Check the model matches the migrations | `dotnet ef migrations has-pending-model-changes --project src/TinapayanRMS.Infrastructure` |
| Apply migrations | `dotnet ef database update --project src/TinapayanRMS.Infrastructure` |
| Regenerate the compiled model, after any model change | `dotnet ef dbcontext optimize --project src/TinapayanRMS.Infrastructure --output-dir CompiledModels --namespace TinapayanRMS.Infrastructure.CompiledModels` |
| Publish the app (ReadyToRun, to `src/TinapayanRMS.UI/bin/Release/net11.0-windows10.0.19041.0/win-x64/publish/`) | `dotnet publish src/TinapayanRMS.UI` |
| Query the database | `& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -U tinapayanrms -d tinapayanrms -c "SELECT name, cost_per_base_unit FROM raw_materials"` |
| Run the MCP server by hand, as an AI assistant does from `.mcp.json` (see [Checking costs from an AI assistant](#checking-costs-from-an-ai-assistant)) | `powershell -ExecutionPolicy Bypass -File scripts/Start-McpServer.ps1` |
| Run the Recipe API (see [The Recipe API](#the-recipe-api)), then try it | `dotnet run --project src/TinapayanRMS.RecipeApi`, then `curl http://localhost:5290/api/v1/mixes` |
| Install the Recipe API for this Windows user, starting at sign-in (in an administrator PowerShell to let the LAN in) | `powershell -ExecutionPolicy Bypass -File scripts/Install-RecipeApi.ps1` |

The tests run on Microsoft.Testing.Platform, which `global.json` turns on (`test.runner`). VSTest-style flags such as `--filter` and `--collect` don't work: use the `--filter-*` options and `--coverage` instead. A test project run directly (`dotnet run`, or its `.exe`) runs on it too, with the same options (`UseMicrosoftTestingPlatformRunner` in `tests/Directory.Build.props`, 2026-11-05); before, that ran xUnit's own console runner, whose options are `-class` and `-xml`.

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
| `src/TinapayanRMS.Domain` | `net11.0` | Nothing | `Entities/`, `Enums/` |
| `src/TinapayanRMS.ApplicationCore` | `net11.0` | Domain; FluentValidation | `Interfaces/`, `Rules/`, `Validators/` |
| `src/TinapayanRMS.Infrastructure` | `net11.0` | Domain, ApplicationCore; EF Core (Npgsql, with EFCore.NamingConventions), Dapper, the Generic Host | `TinapayanRMSDbContext` and its design-time factory, `DatabaseHost` and `ReadOnlyConnection` at the root; `Configurations/`, `Migrations/`, `CompiledModels/`, `Queries/`, `Repositories/`, `Services/` |
| `src/TinapayanRMS.Pricing` | `net11.0` | ApplicationCore | What a saved product costs, the one way the screens and both servers work it out: `SavedProductReader` and what it builds, at its root. `Versions/` holds the product versions' snapshot and the saves that record them (`VersionedSaves`, `ProductVersionRecorder`) |
| `src/TinapayanRMS.UI` | `net11.0-windows10.0.19041.0` | All four; ReactiveUI.WPF, DynamicData, System.Reactive, the Generic Host, Serilog, Syncfusion | App startup at the root; `Views/`, `ViewModels/`, `Controls/`, `Converters/`, `Printing/`, `Exporting/`, `Vim/`, `Themes/`, `Assets/Icons/` |
| `src/TinapayanRMS.Mcp` | `net11.0` | Infrastructure, Pricing; ModelContextProtocol | An MCP server for AI coding assistants (see [Checking costs from an AI assistant](#checking-costs-from-an-ai-assistant)): `Program` at the root; `Tools/`, `Results/`, `Serialization/` |
| `src/TinapayanRMS.RecipeApi` | `net11.0` | Infrastructure, Pricing; ASP.NET Core (a framework reference), Serilog | A read-only HTTP API for the shop's other apps (see [The Recipe API](#the-recipe-api)): `Program`, `RecipeApiHost` and `recipeapi.json` at the root; `Contracts/`, `Endpoints/` |
| `tests/TinapayanRMS.ApplicationCore.Tests` | `net11.0` | Domain, ApplicationCore | Rule and validator tests, with no database |
| `tests/TinapayanRMS.Infrastructure.Tests` | `net11.0` | Infrastructure, Tests.Common | Persistence tests against the project's own database |
| `tests/TinapayanRMS.UI.Tests` | `net11.0-windows10.0.19041.0` | UI, Tests.Common | ViewModel, control, printing, export, converter and Vim-key tests |
| `tests/TinapayanRMS.Pricing.Tests` | `net11.0` | Pricing, Tests.Common | The pricing and the versioned saves (`Versions/`), some of it against the project's own database. What a composite prints stays in `UI.Tests`, since the printouts are the UI's |
| `tests/TinapayanRMS.Mcp.Tests` | `net11.0` | Mcp, Tests.Common | The MCP server's tools against the project's own database |
| `tests/TinapayanRMS.RecipeApi.Tests` | `net11.0` | RecipeApi, Tests.Common; ASP.NET Core's TestHost | The Recipe API, served in memory, against the project's own database |
| `tests/TinapayanRMS.Tests.Common` | `net11.0` | Infrastructure, Pricing; Respawn | The shared database fixtures, and `VersionRecorders`, which builds the versioned saves over real repositories. It's a plain library, not a test project. |

The UI projects target Windows SDK 10.0.19041 because ReactiveUI.WPF needs it. The bare `net11.0-windows` moniker resolves to `windows7.0`, which is too low. The MCP and Recipe API projects are on plain `net11.0`, which can't reference a Windows project, so neither server can come to depend on the UI by accident.

### Folders are namespaces

Every folder in a project is a sub-namespace: `src/TinapayanRMS.UI/ViewModels/` holds `TinapayanRMS.UI.ViewModels`. A new file goes in the folder for its kind, never at a project root. Only app-startup plumbing (`App`, `CompositionRoot`, `ReactiveUIBootstrapper`, `ConfirmDeleteHandlerRegistration`, `BindingErrorLog`, Infrastructure's `DatabaseHost` and `ReadOnlyConnection`, the MCP server's `Program`, and the Recipe API's `Program` and `RecipeApiHost`), the DbContext pair, and `TinapayanRMS.Pricing`, which is one kind of thing throughout, sit at a root. Code in a sub-namespace sees its parent namespaces without a `using`; sibling folders need one.

| A new… | Goes in | Its tests go in |
|---|---|---|
| Entity or enum | `Domain/Entities/`, `Domain/Enums/` | (tested through the layers above) |
| Business or costing rule | `ApplicationCore/Rules/` | `ApplicationCore.Tests/Rules/` |
| Validator | `ApplicationCore/Validators/` | `ApplicationCore.Tests/Validators/` |
| Repository | Interface in `ApplicationCore/Interfaces/`, implementation in `Infrastructure/Repositories/` | `Infrastructure.Tests/Persistence/` |
| EF configuration | `Infrastructure/Configurations/` | `Infrastructure.Tests/Persistence/` |
| Cross-entity service | `Infrastructure/Services/` | `Infrastructure.Tests/` |
| Anything a server prices with as well as the screens | `Pricing/` | `Pricing.Tests/` |
| Screen | View in `UI/Views/`, ViewModel in `UI/ViewModels/` | `UI.Tests/ViewModels/` |
| Card of a large screen | `UI/Views/<Screen>/` | — |
| Reusable control or behaviour | `UI/Controls/` | `UI.Tests/Controls/` |
| Value converter | `UI/Converters/` | `UI.Tests/Converters/` |
| Something written to a file, such as an Excel export | `UI/Exporting/` | `UI.Tests/Exporting/` |
| Part of the Vim keys | `UI/Vim/` | `UI.Tests/Vim/` |
| Icon image file (`.png` or `.ico`) | `UI/Assets/Icons/` | — |

A file can hold a few small, closely related types, such as a tree's rows in `ProductTree.cs`. The screen ViewModels hold only their screen: the row and edit types a screen uses have files of their own beside it, such as `ProductUsageEdit.cs` and `PackagingLineRow.cs` for Products.

### Build configuration

- [Directory.Build.props](../Directory.Build.props) sets `net11.0`, `ImplicitUsings`, `Nullable`, the `WindowsTargetFramework` property, `AnalysisLevel` `latest-recommended` (the SDK's built-in analyzers at their Recommended rule set), and .NET 11's runtime async (`<Features>$(Features);runtime-async=on</Features>`): `async` methods run on the runtime's own support rather than as compiler-made state machines, so a decompiled assembly shows no `<…>d__` class for one, only for iterators. It's still opt-in in the RC3 SDK. Delete the line to go back. It also leaves the commit out of every build but `dotnet publish`'s (`EnableSourceControlManagerQueries` and `EnableSourceLink` are off unless `_IsPublishing`, 2026-11-04). The SDK otherwise puts the commit's hash into every assembly's version and Source Link file, so the first build after any commit, even one that changed only docs, recompiled every project. A published copy still says which commit it was built from: `1.0.0+<hash>`, its file's Product version. The build also writes each project's folder, drive letter's case and all, into a file the compiler reads (`build_property.ProjectDir`, in `obj/.../<Project>.GeneratedMSBuildEditorConfig.editorconfig`), so a build from `c:\…` after one from `C:\…`, or the other way, recompiles every project too.
- [tests/Directory.Build.props](../tests/Directory.Build.props) imports it. It gives every project whose name ends in `.Tests` the xUnit v3, AwesomeAssertions, test SDK and coverage packages, and a global `using Xunit`.
- [Directory.Packages.props](../Directory.Packages.props) turns on Central Package Management. Versions live only there, so a `PackageReference` never has a `Version`. The EF Core, Microsoft.Extensions and Syncfusion versions are the `EFCoreVersion`, `MicrosoftExtensionsVersion` and `SyncfusionVersion` properties. Every Syncfusion package must be on the same version, so upgrade them together. Transitive pinning is on, so the "Transitive pins" group can lift a transitive dependency to a fixed version: currently `Microsoft.EntityFrameworkCore.Relational`, held at `EFCoreVersion` so every project gets one EF Core. `EFCore.NamingConventions` has no .NET 11 release yet, and its 10.0.1 asks for EF Core 10. It works on EF Core 11 (the snake_case names, the model-changes check and the compiled model all hold), so move it to its 11.x when there is one. `dotnet list TinapayanRMS.slnx package --include-transitive --vulnerable` lists what still needs one.
- [.editorconfig](../.editorconfig) marks `Migrations/` as generated code, turns off CA1707 (underscores in names) in `tests/`, and turns off CA1716 everywhere. The files in `CompiledModels/` carry their own `<auto-generated />` header, so the analyzers skip them too.
- Warnings aren't treated as errors, but the build is kept free of them.
- The UI project sets `PublishReadyToRun`, with its `win-x64` runtime identifier, so `dotnet publish` precompiles every assembly the app ships, and less code waits on the JIT at startup. `dotnet build` and `dotnet run` ignore it.

### Other files at the root

`.claude/`, `apm.yml`, `apm.lock.yaml` and `skills-lock.json` hold AI-assistant tooling: Syncfusion's WPF agent skills, and a UI-builder agent. You don't need them to build or run the app. However, the `syncfusion-wpf-*` skills in `.claude/skills/` are readable Markdown guides to each Syncfusion control. `.mcp.json` tells an AI assistant how to start two MCP servers: this repo's `src/TinapayanRMS.Mcp`, through `scripts/Start-McpServer.ps1` (see [Checking costs from an AI assistant](#checking-costs-from-an-ai-assistant)), and Syncfusion's documentation server, `syncfusion-wpf` (see [Working with Syncfusion](#working-with-syncfusion)).

### A reading order for the code

1. The entities [RawMaterial](../src/TinapayanRMS.Domain/Entities/RawMaterial.cs), [SubAssembly](../src/TinapayanRMS.Domain/Entities/SubAssembly.cs) and [Product](../src/TinapayanRMS.Domain/Entities/Product.cs). Their XML comments explain the domain.
2. [CostingRules](../src/TinapayanRMS.ApplicationCore/Rules/CostingRules.cs), [ProductRules](../src/TinapayanRMS.ApplicationCore/Rules/ProductRules.cs) and [IngredientCostGraph](../src/TinapayanRMS.ApplicationCore/Rules/IngredientCostGraph.cs).
3. [IngredientCostPropagator](../src/TinapayanRMS.Infrastructure/Services/IngredientCostPropagator.cs).
4. [ProductCategoryRepository](../src/TinapayanRMS.Infrastructure/Repositories/ProductCategoryRepository.cs), the simplest repository; then `SaveAsync` in [RawMaterialRepository](../src/TinapayanRMS.Infrastructure/Repositories/RawMaterialRepository.cs).
5. [App.xaml.cs](../src/TinapayanRMS.UI/App.xaml.cs), [CompositionRoot.cs](../src/TinapayanRMS.UI/CompositionRoot.cs) and [MainWindow.xaml.cs](../src/TinapayanRMS.UI/Views/MainWindow.xaml.cs).
6. [ViewModelBase](../src/TinapayanRMS.UI/ViewModels/ViewModelBase.cs), then the smallest complete screen: [CategoriesViewModel](../src/TinapayanRMS.UI/ViewModels/CategoriesViewModel.cs) with [CategoriesView.xaml](../src/TinapayanRMS.UI/Views/CategoriesView.xaml).
7. [CollectionMerge](../src/TinapayanRMS.UI/ViewModels/CollectionMerge.cs) and [UndoHistory](../src/TinapayanRMS.UI/ViewModels/UndoHistory.cs).
8. [ReactiveCostCascadeTests](../tests/TinapayanRMS.Infrastructure.Tests/Persistence/ReactiveCostCascadeTests.cs), which runs the whole cascade end to end.

Then move on to the large screens. `ProductsViewModel.cs` is about 4,890 lines and `RawMaterialsViewModel.cs` about 3,100, so navigate them by command and member with your IDE's outline, rather than reading them top to bottom. Parts of the Products screen have ViewModels of their own, which it holds and its views bind through: the Costing tab is [ProductCosting](../src/TinapayanRMS.UI/ViewModels/ProductCosting.cs) (`Costing`), the Recipe Structure's Packaging tab [ProductPackaging](../src/TinapayanRMS.UI/ViewModels/ProductPackaging.cs) (`Packaging`), a composite's Products table [ProductComponents](../src/TinapayanRMS.UI/ViewModels/ProductComponents.cs) (`ComponentsTable`), the add/edit sub-assembly form [ProductUsageForm](../src/TinapayanRMS.UI/ViewModels/ProductUsageForm.cs) (`UsageForm`), which works out its own piece size and figures, the Scaling tab [ProductScaling](../src/TinapayanRMS.UI/ViewModels/ProductScaling.cs) (`Scaling`), and the History tab `ProductVersionHistory` (`VersionHistory`), which reads its versions and saves their notes; restoring one stays on the screen, which puts it into the form. The screen keeps the undo history and the commands, which change them as undo steps.

---

## 5. Architecture

### Layers

```
┌──────────────────────────── TinapayanRMS.UI ──────────────────────────────┐
│  Views (XAML + code-behind)  ◄── bind to ──  ViewModels (ReactiveUI)      │
│  Controls, Converters, Printing, Themes                                   │
│  App and CompositionRoot (which register Infrastructure's types)          │
└───────────────┬───────────────────────────────────────────────────────────┘
                │ prices saved products with
┌───────────────▼──────────── TinapayanRMS.Pricing ─────────────────────────┐
│  SavedProductReader and what it builds. Mcp and RecipeApi use it too      │
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
│  Repositories; Services (cost propagator, ProductCloner, view state)      │
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
- **Pricing** is what a saved product costs, read and worked out one way: `SavedProductReader`, the immutable `SavedProduct` it builds (its `PricedSection`s and `PricedPackagingLine`s), and `ProductCostSummary`, which is worked out from a `ProductCostInputs` record: the screen fills one from the form and the reader from what's saved. A section's batch and piece figures come from `SectionFigures.Of` and a packaging line's cost from `PricedPackagingLine.Price`, for the screens' editable rows and saved products alike, and a row gives its immutable form with `ToPriced()`, which printouts and scaling read. A price tried on the Price Simulator is worked out and checked by `TriedPriceChange.Of`, for its Price Changes table and the MCP server's `simulate_price_changes`. A saved product's sections are named as the screen names them, two in one role apart by their labels ("Dough" beside "Dough: Paste"). It knows ApplicationCore's interfaces and rules, and nothing of EF Core, WPF, Syncfusion or ReactiveUI, so the Recipe API and the MCP server price exactly as the screens do with no UI in them. Everything in it is immutable: the editable rows the screens type into (`ProductUsageEdit`, `PackagingLineRow`, `SimulatedPriceChange`, on `ObservableRow`) are the UI's, in `ViewModels/`, so the servers can't reach them.
- **UI** depends on ApplicationCore's interfaces and on Pricing. `CompositionRoot` names Infrastructure's types to register them, and the screens say what failed in `Failure`'s words.

Business and costing rules go in ApplicationCore, and EF Core and other I/O stay in Infrastructure. A ViewModel never sees a `DbContext`.

### Startup

1. **`App`'s static constructor** ([App.xaml.cs](../src/TinapayanRMS.UI/App.xaml.cs)) runs before anything touches a Syncfusion type. It builds the Serilog logger and, in Debug builds, adds the binding-error log. It registers the Syncfusion licence: the key the build compiled in (the UI project's `SyncfusionLicenseKey` `AssemblyMetadata` item, taken from `SYNCFUSION_LICENSE_KEY`), or else the variable itself. It throws (after logging) with neither. It also sets app-wide control defaults: 24 px text boxes and buttons, right-aligned number boxes, editable pickers that filter as you type, and the grid, picker and undo behaviours from `Controls/`.
2. **`App.OnStartup`** hooks unhandled-exception logging and claims the app (see [One copy at a time](#one-copy-at-a-time)): a copy that finds another open closes here, having started nothing. It then registers the Windows11Light theme settings, with the accent recoloured to the app's Sky and every text size 2 px larger than the theme's default. It then builds and starts the host from `CompositionRoot.CreateHostBuilder`, resolves `MainWindow`, themes it with `SfSkinManager.SetTheme` and shows it. A failure in any of that goes to [AppStartup](../src/TinapayanRMS.UI/AppStartup.cs), which logs it, says why and closes the app with exit code 1: with no window yet, there's nothing to carry on in.
3. **`CompositionRoot.CreateHostBuilder`** ([CompositionRoot.cs](../src/TinapayanRMS.UI/CompositionRoot.cs)) initialises ReactiveUI and starts from [DatabaseHost](../src/TinapayanRMS.Infrastructure/DatabaseHost.cs), which reads `appsettings.json` and registers the DbContext factory and the repositories, as it does for the two servers. It then registers the rest: `SavedProductReader`, `ProductVersionRecorder`, the view-state store, the logger, the validators, the ViewModels and `MainWindow`.
4. **`MainWindow`'s constructor** ([MainWindow.xaml.cs](../src/TinapayanRMS.UI/Views/MainWindow.xaml.cs)) creates the first tab's view around its ViewModel. Every other tab's view is made the first time the tab is shown (`CreateScreen`). It maps each tab to its screen's `LoadCommand` and to its Back and Forward handling, and wires up the requests that one screen makes of another. When the window has loaded, it loads the tab on show.
5. Once the window shows, **`App`** starts two things off the UI thread: a check for migrations the database doesn't have yet, and the day's automatic backup (`AutomaticBackups.RunAsync`, over `IDatabaseBackups.BackUpDailyIfDueAsync`), taken the first time the app starts each day and checked again every hour by a `DispatcherTimer`, so a copy left open over midnight takes the next day's. Each one is also copied to the second folder chosen on Settings → Database, when there is one (kept in the view-state file under `backup-copy-folder`). A backup or copy that fails is logged and shown on the Database tab, and stays shown after a restart (under `backup-problem` and `backup-copy-problem` in the view-state file) until one goes through. If the automatic backup failed and the newest backup of this database is three days old or more, `App` also shows a message, once a run (`AutomaticBackups.OverdueWarningAsync`, `BackupRules.IsOverdue`).
6. **`App.OnExit`** disposes the host, which disposes the singleton ViewModels, flushes the log, and lets go of the app. After a restore, `App.RestartAfterExit` has set it to start the app again (`Process.StartAndForget`), once all of that is done, or the new copy would find this one still open.

ReactiveUI 26 must be initialised exactly once per process, before any `ReactiveObject` exists. [ReactiveUIBootstrapper](../src/TinapayanRMS.UI/ReactiveUIBootstrapper.cs) does it with `RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().WithWpf().BuildApp()`, and tests that build ViewModels call `ReactiveUIBootstrapper.EnsureInitialized()` themselves.

### One copy at a time

Nothing in the database notices a row changing after a screen read it (there are no concurrency tokens), so two copies side by side would save over each other with neither saying so. [SingleInstanceGuard](../src/TinapayanRMS.UI/SingleInstanceGuard.cs) keeps the app to one copy for each Windows sign-in, with a named mutex, `Local\TinapayanRMS`. Windows frees it when a copy crashes or is ended, so there's nothing to clear up.

- **A second copy** asks the open one to come to the front, over two named events, and closes. It asks again every 250 ms for up to 30 seconds (`Patience`), because the open copy may only be starting: it listens once its window shows. It answers from its UI thread, so an answer means it isn't frozen. No answer in time says so in a message. If the open copy closes meanwhile, the one asking gets the app and carries on.
- **`dotnet run` or a new build with the published app open doesn't start the new build.** A copy from another folder says where the open one runs from, then brings it forward, so the old build isn't taken for the new one. A copy from the same folder comes forward without a word. This only works against a build that has the guard, so publish again to protect a copy started from a shortcut.
- **It doesn't reach further than the sign-in.** Another Windows user on this PC, or another PC on the same database, can still save over this one: only a check made when saving could catch those. A copy running as an administrator can't be opened by one that isn't, so that says so too (`OutOfReach`).
- **A mutex belongs to the thread that took it,** so `TryClaim` and `Dispose` both run on the UI thread, and a test gives each copy a thread of its own (`SingleInstanceGuardTests`). The window itself comes forward through Win32 (`SingleInstanceGuard.BringToFront`), since WPF's `Activate` and `SystemCommands.RestoreWindow` fall short with a dialog or message box open.

### Dependency injection and lifetimes

Everything in the container is a **singleton**: the repositories, the services (`ProductCloner`, `CostRecalculator`, `SavedProductReader`, `ProductVersionRecorder`, `VersionedSaves`), the validators, every ViewModel and `MainWindow`. A screen lives for the whole session, and so does its ViewModel.

The DbContext comes from a **factory**, never `AddDbContext`. A singleton can't hold a `DbContext`: two screens loading at once would share it, and EF Core throws when a second operation starts on a context that's still busy. That happened the first time the app ran. So every repository takes `IDbContextFactory<TinapayanRMSDbContext>` and creates a short-lived context for each call. The factory is [SharedTransactionDbContextFactory](../src/TinapayanRMS.Infrastructure/Services/SharedTransactionDbContextFactory.cs), registered as both `IDbContextFactory<TinapayanRMSDbContext>` and `IUnitOfWork` (see "Unit of work" below):

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

**Unit of work.** `IUnitOfWork.RunAsync(work)` runs several repositories' calls as one transaction: it commits when `work` returns and rolls everything back when it throws. While it runs, every context the factory makes on that async flow shares its one connection and its Serializable transaction, whichever repository asks. A repository begins its own transaction with `BeginOrJoinTransactionAsync` ([Transactions](../src/TinapayanRMS.Infrastructure/Services/Transactions.cs)), never `BeginTransactionAsync`, so inside a unit of work it joins that one instead of failing. Two rules follow:

- **Reads inside a unit of work run one at a time.** They share one connection, and Npgsql refuses a second command while one is running. So `SavedProductReader` and `ProductVersionRecorder` read in sequence, never with `Task.WhenAll`.
- **Only [VersionedSaves](../src/TinapayanRMS.Pricing/Versions/VersionedSaves.cs) uses it today**, for the saves that record product versions (see "Product versions"). Outside a unit of work, each context has a connection of its own, as before.

Views aren't in the container. `MainWindow` and `SettingsWindow` create them with `new SomeView(viewModel)`. [CompositionRootTests](../tests/TinapayanRMS.UI.Tests/CompositionRootTests.cs) resolves every screen's ViewModel, so a missing registration fails a test rather than the app.

A ViewModel that owns a DynamicData `SourceCache` is `sealed` and `IDisposable`, so that the host disposes the cache at exit.

### How a change travels through the layers

Take a user who types a new price for cake flour on Raw Materials and presses **Save**:

1. **The ViewModel.** `RawMaterialsViewModel.SaveCommand` runs inside `RunSafelyAsync`. It runs the FluentValidation validators for what it's about to save, and stops with an `ErrorMessage` if one fails.
2. **The repository, in one transaction.** `IRawMaterialRepository.SaveAsync(material, units, brandPrices, addIns)` opens one context and one transaction. It saves the material through `RawMaterialRules`' checks, replaces the units and brand prices, and reprices the material at its dearest brand, logging one price history entry if the cost moved. It replaces a made ingredient's add-ins if they were sent, then checks the made ingredient as a whole (`EnsureMadeIngredientFitsAsync`: a sub-assembly it can be made from, add-ins its recipe lacks, no loop).
3. **The cascade, in the same transaction.** `IngredientCostPropagator` recomputes every made or linked ingredient that depends on the flour, in dependency order. Nothing above the ingredients stores a cost: every sub-assembly and product using them is costed from its lines when it's next read. Then the transaction commits. A rule that throws anywhere, or a cascade that fails part-way, rolls all of it back.
4. **The DbContext** stamps `CostUpdatedAt` on every material whose stored cost changed.
5. **Back in the ViewModel**, `LoadCommand` merges the fresh data into the screen, and a success toast says "Cake Flour was saved."
6. **The Products screen** stores no costs. The next time its tab shows, its `LoadCommand` reads the new sub-assembly costs, and `ProductRules` and `CostingRules` work out the new selling price on read.

### Key decisions

| Decision | Why | Read more in PLAN.md |
|---|---|---|
| A DbContext factory, and one short-lived context per operation | ViewModels are singletons, and screens load at the same time | [Application Architecture](../PLAN.md#application-architecture) |
| Reads through Dapper, writes through EF Core | Plain SQL reads are fast and explicit. Every write still goes through EF Core, so the cost propagator runs and a duplicate name surfaces as `DbUpdateException`. | [Conventions](../PLAN.md#conventions) |
| Material and sub-assembly costs stored and propagated; product costs computed on read | BOMs nest to any depth, so their costs are worth keeping current. A product's figures are quick to work out from those stored costs, and working them out on every read is simpler than keeping them in step. | [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) |
| A pure ordering function plus a database orchestrator, instead of DynamicData joins | Simpler, testable without a database, and the database stays the single source of truth | [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) |
| Cost-plus pricing, in three separate steps | The shop owner can check each step by hand | Application Architecture, "Cost-plus pricing" |
| `decimal` at full precision, rounded only for display and the final price | Rounding each figure before adding them up drifts | Conventions, "Decimal precision" |
| One Save per screen, in one transaction, with the cost cascade it causes | A rejected part, or a cascade that fails, leaves nothing half-saved and no stored cost stale | Application Architecture, "One Save writes the whole material"; [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) |
| A failure is shown on its screen, and the app carries on | A crash loses unsaved work, and one failed read mustn't stop a screen working | Application Architecture, "A failure is shown on its screen" |
| Screens merge fresh data instead of reloading it | A tab keeps its selection, scroll position and unsaved edits | Application Architecture, "A tab keeps its view state" |
| Tests on a real database, with nothing mocked | They catch what mocks miss: SQL, constraints and EF Core's behaviour | [Development Environment](../PLAN.md#development-environment) |
| Syncfusion controls over native ones | The whole UI picks up the theme consistently | [Syncfusion notes](plan/syncfusion.md) |
| AwesomeAssertions, not FluentAssertions | FluentAssertions 8 needs a paid licence for commercial use | [Tech Stack](../PLAN.md#tech-stack) |

### The Recipe API

[src/TinapayanRMS.RecipeApi](../src/TinapayanRMS.RecipeApi) lets the shop's other apps read the recipes, on this PC or another one on the LAN. TinapayanBMS (`../TinapayanBMS`) is the first. It's a small ASP.NET Core server, with minimal-API endpoints and JSON in camelCase, under `/api/v1`:

| Endpoint | What it returns | Read through |
|---|---|---|
| `GET /status` | `{ app, apiVersion }`, to check the address, the key and the database | `ICostingDefaultsRepository.GetAsync`, only to see that it can read |
| `GET /ingredients` | Every ingredient (not packaging), with its category and source, a made one's weight loss and sub-assembly, and a linked one's ingredient and weight share | `IRawMaterialRepository.GetAllAsync` |
| `GET /boms` | Every made ingredient's BOM lines in order: its sub-assembly's lines, then its add-ins (`isAddIn`) | `IIngredientBomLineRepository.GetAllAsync` |
| `GET /materials` | Every raw material, packaging included: its type, base unit, buffered cost per base unit, and the purchase unit and price of the brand its cost comes from (`PurchaseUnit.Of`), as the purchase requisition prices them | `IRawMaterialRepository.GetAllAsync`, `IRawMaterialBrandPriceRepository.GetAllAsync` |
| `GET /categories` | The Ingredient and Made categories | `IRawMaterialCategoryRepository.GetAllAsync` |
| `GET /products?search=` | The products in use, each with its category, stage and pieces per unit | `IProductRepository.GetAllAsync` |
| `GET /products/recipes` | Each product in use as TinapayanBMS's planning reads it: pieces per unit, a composite's products, and each section's sub-assembly, name, role, piece size (`authoredField`, `authoredValue`), lines and add-ins, grams per batch, and the Wastage % it's priced with. No packaging | `SavedProductReader.ReadAllAsync` |
| `GET /mixes` | Every sub-assembly with lines that isn't a packaging set, a product's own included, with its role and batch weight | `ISubAssemblyRepository.GetLinkableAsync` |
| `GET /mixes/details` | Every mix as `/mixes` lists them, each with its lines as `/mixes/{id}` gives them: what TinapayanBMS's sync copies in one call | `ISubAssemblyRepository.GetLinkableAsync`, `ISubAssemblyLineRepository.GetForSubAssembliesAsync` |
| `GET /mixes/{id}` | One mix and its lines, grams per batch, in the recipe's order, each with `isFormulaBase` | `ISubAssemblyLineRepository.GetForSubAssembliesAsync` |

- **It can't write.** Its host is `DatabaseHost`'s with the web server on top (`RecipeApiHost.AddRecipeApi`), on the read-only connection the MCP server uses ([ReadOnlyConnection](../src/TinapayanRMS.Infrastructure/ReadOnlyConnection.cs)).
- **It has no UI in it** (2026-11-04). It's on plain `net11.0`, on `Infrastructure` and `Pricing`, so its figures are the screens' without WPF, Syncfusion or ReactiveUI coming with them: it publishes at about 10 MB, where it was 77 MB. `appsettings.json` is still the UI project's, linked into its output.
- **Planning lives in TinapayanBMS** (2026-10-31). TinapayanBMS copies `/ingredients`, `/boms`, `/materials`, `/mixes/details` and `/products/recipes` into its own database and plans from them with its port of what was `ProductionPlanRules` and `ProductionPlanBuilder`; this app's Production Plan screen, its saved plans, Reports' Production Results and `POST /plan` were then taken out (see [The Production Plan](#the-production-plan)). Removing `/plan` and `/plans` from `/api/v1` broke no caller: TinapayanBMS, the only one, had stopped using them.
- **The contract is `Contracts/RecipeContracts.cs`.** The apps that call it keep their own copies of those records, so add to a record rather than renaming or removing a field, or start `/api/v2`.
- **Where it listens** is in `recipeapi.json` beside it: Kestrel's `http://*:5290`, every address, so the LAN can reach it. The database is the app's own `appsettings.json`'s.
- **Its key** is `RecipeApi:Key` in the same file, read when it starts. With one set, every request has to send it in an `X-Api-Key` header, from this PC too, or gets a 401. With none set, as in the repo's copy and so under `dotnet run`, only this PC is answered and any other address gets a 403 (2026-11-03: until then no key meant anyone who could reach the port got every cost and price). It's plain HTTP, so a key stops whoever doesn't have it, not someone who can watch the network's traffic.
- **What goes wrong comes back as `{ error }`** (`RecipeApiHost`): a `BusinessRuleException` as a 400 with its message, an unknown id as a 404, a database that can't be reached, is behind this version or is too slow as a 503 with `Failure`'s sentence for it, and anything else as a 500 with a fixed sentence, the details only in its log, since they can name columns and the like.
- **Installing it**: `scripts/Install-RecipeApi.ps1` publishes it to a temporary folder, so a build that fails leaves the running copy alone, and only then stops that copy and mirrors what was published to `%LocalAppData%\Programs\TinapayanRMS.RecipeApi` (robocopy `/MIR`), since publishing straight into it never takes away what an earlier version needed. The folder's `recipeapi.json` and `logs` are left as they are. It puts a shortcut in the Startup folder that starts it hidden at sign-in, and starts it. A first install also makes the key, 32 random bytes in hex, writes it to the installed `recipeapi.json` and shows it. Installing again keeps the key that's there. It runs as the signed-in user, not as a service, so it finds that user's `pgpass.conf`. Run in an administrator PowerShell, it also adds a Windows Firewall rule for port 5290, on private networks and from the local subnet only. `scripts/Uninstall-RecipeApi.ps1` takes it all away again, the key included, so a later install makes a new one. It logs to `logs/tinapayanrms-recipeapi-*.log` beside it.
- **Tested by** `RecipeApiTests`, through the host `Program` builds, served in memory by ASP.NET Core's `TestServer` on the project's own database (`tinapayanrms_test_recipeapi`).

---

## 6. The domain model

The entities in `Domain/Entities` are plain classes, and their XML comments are the best reference for each property. The behaviour lives in the `*Rules` classes.

### What feeds what

Each arrow reads "is used by". Costs flow the same way.

```
INGREDIENTS
  any ingredient ──► SubAssemblyLine ──► SubAssembly ──► made ingredient (SubAssemblyId; BOMs nest to any depth)
  any ingredient ──► IngredientAddInLine ──► made ingredient           (its own extras)
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
| `RawMaterial` | An ingredient or a packaging material | `Type` and `Source` never change once saved. Names are unique across both types. `CostPerBaseUnit` and `BufferedCostPerBaseUnit` are stored as `numeric(28,20)`. Check constraints back up the type and source rules. |
| `RawMaterialUnit` | An alternative unit, defined in base units | Never the base unit itself: a line or price with no unit is in the base unit. Correcting a multiplier reprices the material. A unit in use can't be deleted. |
| `RawMaterialBrandPrice` | A purchased material's price for one brand, in one unit | The Raw Materials Save replaces them as a set. |
| `RawMaterialPriceHistory` | A price the material was costed at | A log. Nothing reads it to cost anything. |
| `IngredientAddInLine` | One of a made ingredient's own add-ins, in grams | Ingredients only, each once, none its sub-assembly already has (`IngredientBomRules.EnsureValidAddIns`). Cascades with the made ingredient. |
| `IngredientBomLine` (read model) | One component of a made ingredient, in grams: its sub-assembly's lines, then its add-ins | A made ingredient can't be made from a packaging set or a product's own (`SubAssemblyRules.EnsureLinkableByMadeIngredient`), and its BOM can't contain it, directly or through others (`IngredientBomRules.EnsureNotInOwnRecipe`, `EnsureNoCycle`). |
| `SubAssemblyRole` | Dough, Filling, Topping, Packaging, … | The starting roles are seeded by migrations, and are edited in Settings. The Packaging role (`IsPackaging`) can't be deleted, and nothing clears its flag. |
| `SubAssembly` | A recipe, or a packaging set | Names are unique. A null `OwnerProductId` means standalone. A product's own keeps its label in that product's recipe in `SectionName`, and its name is made from that, the product's name and the role's. Its batch weight and cost are worked out from its lines on read. It can't be deleted while a product links it. |
| `SubAssemblyLine` | An ingredient in a recipe, in grams | Grams are always stored. `AuthoredBakersPercent` records a percentage the user typed instead. |
| `SubAssemblyPackagingLine` | A packaging material in a packaging set | Costed on read. |
| `Product` | A sellable product | Names are unique, deleted products' included. `IsComposite` is fixed once saved. `QuantityRequired` is at least 1, and exactly 1 for a composite. Deleting one only stamps `DeletedAt` (a soft delete): see [Deleted products](#deleted-products). |
| `ProductCategory` | Breads, Cakes, … | A list of its own, separate from raw material categories. Its optional `WastagePercent` prices its products in place of `CostingDefaults.WastagePercent`. |
| `ProductSubAssemblyUsage` | A sub-assembly in a product's recipe | Stores only the typed piece-size figure (`AuthoredField`, `AuthoredValue`). |
| `UsageAddInLine` | A product's extra ingredient on a linked usage | Never changes the sub-assembly that the usage links. |
| `PackagingLine` | A packaging material per sellable unit | Stores both reciprocal quantities and which one was typed (`AuthoredField`). |
| `PackagingLink` | A product's packaging, read from another product or from a set | Exactly one target, and no loops (`PackagingLinkRules`). |
| `ProductComponent` | A product inside a composite | `Quantity` is at least 1, and a composite never contains itself (`CompositeProductRules.EnsureNoCycle`). |
| `CostingParameters` | A product's five percentages | One per product. Never linked or shared. |
| `ProductVersion` | One version in a product's history | `Number` is unique per product. `Content` is a self-contained JSON snapshot that points at nothing. Goes with the product only on a permanent delete. See [Product versions](#product-versions). |
| `PriceScenario`, `PriceScenarioLine` | A set of Price Simulator price changes saved under a name, and one of its changes | Names are unique, ignoring case; saving under one replaces its lines (`IPriceScenarioRepository.SaveAsync`). A line stores exactly one of `ChangePercent` and `NewPrice`, whichever was typed (`PriceScenarioRules`, backed by check constraints). Deleting a raw material deletes its lines. Nothing reads a scenario to cost anything. See [The Price Simulator](#the-price-simulator). |
| `CostingDefaults` | The stage and percentages a new product starts with, a new raw material's cost buffer, and the shop-wide `WastagePercent` | At most one row (Id 1). With no row, new products go in Production (`DefaultNewProductStage`), the costing reads as zero, the cost buffer as 10% (`DefaultCostBufferPercent`) and the wastage as 3% (`DefaultWastagePercent`). Unlike the rest, the wastage isn't a starting value: `SavedProductReader` stamps it on every `ProductDetail` it reads, and the Products screen keeps it from each load, so changing it reprices every product. |
| `UnitOfMeasure`, `RawMaterialCategory` | Lookups edited in Settings | A unit's name is stored in lowercase. A category is for one type of material (`RawMaterialCategoryType`): a material goes only in one of its own, and a category's type changes only while everything in it fits the new one. |


### Deleted products

A product is deleted in two steps, so a delete can be taken back. `IProductRepository.DeleteAsync` stamps `Product.DeletedAt` and changes nothing else, which moves the product to the Products list's **Deleted** folder. `RestoreAsync` clears it. `DeletePermanentlyAsync` is the hard delete, with its cascades.

- **Reads leave deleted products out by default.** `GetAllAsync` returns the products in use, and `GetDeletedAsync` returns the deleted ones. `GetByIdAsync` and `GetByIdsAsync` return either, since a holder reads what it holds by id. Every screen except Products reads through `GetAllAsync`, so a deleted product is in no list, picker, report, print, the Price Simulator or plan. There's no global query filter: one would also drop the rows of the "in use" checks below.
- **A deleted product still holds what it uses.** Its usages, packaging lines, links and composite lines stay, so a restore brings it back whole. The `Restrict` keys keep refusing deletes of what it uses. The checks that name products (`SubAssemblyRepository`'s Used By and refusals, `RawMaterialRepository.GetUsedByAsync`, a composite's role refusals) name it "Cheese Roll (deleted)", through `ProductRules.ListedName`.
- **The checks on what leans on a product run for both deletes.** Neither a soft nor a hard delete goes through while another product, deleted or not, links its packaging or holds it in a composite (`ProductRepository.EnsureNothingLeansOnAsync`). Nothing in use ever leans on a deleted product, so a restore never needs a check.
- **A deleted product can't be newly linked or held.** `PackagingLinkRepository` and `ProductComponentRepository.SetComponentsAsync` refuse it by name. Its name stays taken: Add, Update and Rename refuse it first (`ProductRules.EnsureNameNotHeldByDeleted`), so the message points to the Deleted folder instead of the unique index's "That name is already in use."
- **On the Products screen**, `ProductsViewModel.DeletedProducts` feeds `ProductTree.DeletedFolder` and nothing else. A deleted row (`ProductLeafNode.IsDeleted`) is picked the way a folder is: the form doesn't open it. Its menu has Restore and Delete Permanently, and `ProductTree.MoveTo` refuses it as a drag source or a drop target. `OpenProduct` on a deleted id picks its row in the Deleted folder.

### Product versions

Every save that changes a product's recipe or price records a `ProductVersion`, which the History tab shows and can restore: the product's own Save on the Products screen, and a save elsewhere that reaches it (a sub-assembly it uses, or another product it shares lines or packaging with). So does making it as a clone or a variant.

- **A version is a snapshot, not a set of rows.** [ProductVersionContent](../src/TinapayanRMS.ApplicationCore/Interfaces/ProductVersionContent.cs) holds the sections with their lines and add-ins, the packaging lines as they showed (resolved through any link), a composite's products with what each brought, the costing percentages, the price, and every material used with its costs at that moment. It's stored as JSON (`ToJson`/`FromJson`, through the source-generated `ProductVersionJsonContext`: enums by name, nulls left out; with a `Schema` number for a later change of shape), and its ids are only for Restore and for pairing sections, so deleting anything later leaves old versions readable.
- **A save and its versions are written together or not at all.** Products are priced above Infrastructure (`SavedProductReader.Build`, in Pricing), so the snapshot is taken in the UI project, by [VersionedSaves](../src/TinapayanRMS.UI/ViewModels/VersionedSaves.cs), but in one unit of work with the save (`IUnitOfWork`). `SaveProductAsync` calls `ProductRepository.SaveAsync`, reads the product's `ProductDetail` back inside the same transaction, turns it into the content with [ProductVersionSnapshot](../src/TinapayanRMS.UI/ViewModels/ProductVersionSnapshot.cs)`.From`, and writes it with `IProductVersionRepository.AddAsync`. A version that can't be written takes the Save back with it, and the screen shows the error. `SaveSubAssemblyAsync` (the Sub-Assemblies screen's Save) and `MakeProductFromAsync` (Clone and Create Variant) work the same way. The screen then shows the product and detail the Save read back (`ShowSavedProduct`), without reading them again.
- **"Changed" has one definition.** `AddAsync` records nothing when `ProductVersionRules.IsSameRecipe` says the content matches the latest, and that is defined as `ProductVersionRules.Changes` finding nothing to list. So a version never appears with an empty list of changes, and a rename, a description, or a material's cost never makes one.
- **The first Save of a product with no versions** first records the product as it stood (`VersionedSaves`, `ProductVersionRules.BaselineNote`), so there's something to go back to.
- **A save also records the other products it changes.** [ProductVersionRecorder](../src/TinapayanRMS.Pricing/Versions/ProductVersionRecorder.cs) (a DI singleton; `ProductsViewModel` builds its own over its repositories, as it does its `SavedProductReader`) runs on both sides of a save:
  - `CaptureAsync` before it. `IProductRepository.GetProductsReadingAsync` finds every product that reads through the saved sub-assemblies or product: those using the sub-assembly, those whose packaging links it as a set, and, through packaging links however deep, those whose packaging links one of them. Each is snapshotted as it's saved.
  - `RecordChangesAsync` after it. Each is read again, and one whose recipe changed gets a version, noted with where the change came from. A product with no versions gets its baseline first. They're all written in one transaction by `IProductVersionRepository.AddRangeAsync`, which reads every product's latest version in one query. The number of commands doesn't grow with the products.
  - Sub-Assemblies' Save passes the sub-assembly. The Products screen's Save passes the product, whose own sections bring in the products sharing them; the product itself is left out, since its own Save records it. It captures only when it writes something another product reads: a linked section's lines (`AreLinkedLinesEdited`), or the packaging lines or link. A Save of the product's own figures, costing or own sections can't change another product, so it reads none of them.
  - Reading or recording can't stop or undo the save: a failure shows after it.
  - `RecordedMessage` names the products in the success notice.
  - `ProductsViewModel.LoadCommand` reads a showing History tab's versions again, and clears a hidden one's, so a version recorded from another screen shows.
- **A clone or a variant gets its first version as it's made.** `MakeFromAsync` calls `ProductVersionRecorder.RecordMadeFromAsync`, noted `ProductsViewModel.ClonedFrom` or `VariantOf` plus the source's name. The source's latest version number goes in when its snapshot `IsSameRecipe` as that version. Its first Save then has a history, so it records no baseline.
- **Figures are worked out on read, by the screen's own code.** `ProductVersionSnapshot.ToDetail` rebuilds a `ProductDetail` from the content and `SavedProductReader.Build` prices it: with its own materials for Then, and with the screen's current materials (`materialsById`, the Price Simulator's way in) and each held product's cost now (`ReadComponentCostsAsync`) for Today.
- **The History tab reads lazily.** [ProductVersionHistory](../src/TinapayanRMS.UI/ViewModels/ProductVersionHistory.cs) holds the rows, the picked one and its changes. `ProductsViewModel.RequestVersions` pushes the product to a `LoadLatest` only while `HistoryTab` shows, once per product, and again after a Save.
  - **Compare With** is a list of `VersionComparison` records: `Previous`, `Current`, then one per version (`With(row)`). They're equal by value, so the choice picked is found again in the list `Show` builds for a new read, and goes back to `Previous` for another product. The picker binds `SelectedItem`, and `CompareWith` ignores the null it sends while its list is replaced. Two versions are always compared from the older to the newer.
  - **Cost Over Time** is an `SfChart` of `Trend`: one `VersionTrendPoint` per version, oldest first, with each one's Then figures as doubles. A missing current price is `NaN`, which the chart draws as a gap. It's rebuilt by `Show`, `Reprice` and `Clear`. Its three series brushes are in `HistoryCard.xaml`'s resources: the dataviz palette's first three hues, checked with its validator against the card's colour.
- **Print shows the version picked while the History tab shows**, as it shows the scaled recipe on the Scaling tab: `PreparePrintout` returns `VersionPrintout`, the version's `ProductDetail` built and priced at its costs then, through `SavedProduct.ToPrintout`, titled with the version and footed with `ProductPrintout.VersionNote`. The form isn't applied, since the pages aren't the form's. The tab's Print Version button is the same `PrintCommand`.
- **Restore is an undo step.** `RestoreVersionAsync` builds the version into the form's state and applies it through `RestoreSnapshot` inside `AsUndoStep`, so Save writes it and Undo takes it back, with no restore path of its own. A linked section is linked again with its current lines (Save never writes a version's lines into a shared sub-assembly). Anything it needs that's gone is named, and nothing changes.

### Where rules are enforced

Three layers check the same rules, each for a different reason.

| Layer | Where | What it gives | On failure |
|---|---|---|---|
| Form pre-checks | FluentValidation validators in `ApplicationCore/Validators`, which a ViewModel runs before it saves. A check a repository enforces is the rule itself, run through `RuleValidation.Satisfies`; only a check with no rule behind it (a pick the form must make) is written in the validator | An immediate message, in the rule's own words | `ErrorMessage` is set, and nothing reaches the repository |
| Business rules (the authority) | Static `*Rules` methods, which the repositories call before they write anything destructive | The real guarantee, whichever code path writes | A `BusinessRuleException`, whose message `RunSafelyAsync` shows exactly as written |
| The database (a backstop) | Unique indexes, check constraints and `Restrict` foreign keys, in `Infrastructure/Configurations` | Integrity even if a code path forgets a check | A `DbUpdateException` or a `PostgresException`, which [Failure](../src/TinapayanRMS.Infrastructure/Services/Failure.cs) puts plainly by PostgreSQL's error code (SQLSTATE): "That name is already in use.", "This item is currently in use elsewhere and can't be deleted.", and so on |

The database's messages say what kind of thing went wrong, never which rule. So a rule that a user can break must be checked first in a `*Rules` method, with a message that says what's wrong. Validators exist only for entities that have a Save form. A set of lines saved together (BOM lines, recipe lines, packaging lines) gets its checks across rows from its Rules class instead.

---

## 7. Costing

Before you change any cost path, read the user manual's [How costs are worked out](user-manual.md#12-how-costs-are-worked-out), which has worked examples, and PLAN.md's [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade).

### Stored and computed costs

| Figure | Stored? | Kept correct by |
|---|---|---|
| A material's `CostPerBaseUnit` and `BufferedCostPerBaseUnit` | Stored | The Raw Materials Save and the brand-price repository (purchased); `IngredientCostPropagator` (made and linked) |
| A material's `CostUpdatedAt` | Stored | `TinapayanRMSDbContext.SaveChangesAsync`, from what actually changed |
| A sub-assembly's batch weight and cost | Computed on read | `SubAssemblyRules.CalculateTotals`, from its lines |
| A usage's worked-out piece size and cost per piece | Computed on read | `ProductRules` |
| Packaging line and packaging set costs | Computed on read | `ProductRules` |
| A product's material cost, wastage, base cost, selling price and markup | Computed on read | `ProductRules`, `CostingRules` |
| What a product brings into a composite | Computed on read | `SavedProductReader`, `CompositeProductRules` |

### The cascade

```
A saved brand price, a made ingredient's sub-assembly, add-in or weight-loss edit,
a line edit on a sub-assembly a made ingredient is made from,
a link or share edit, a buffer edit, or a corrected unit multiplier
  │
  ▼
the ingredient's stored cost
  │
  ▼
IngredientCostPropagator
  recomputes every made or linked ingredient that depends on it,
  components first (IngredientCostGraph.GetDependentsInOrder)
  │
  ▼  nothing more is stored
sub-assemblies, products, packaging, composites, Reports and Print
  work their figures out on read (SubAssemblyRules, ProductRules, CostingRules, SavedProductReader)

A packaging material's saved price ──► its stored cost, which every packaging line and set reads (no propagation needed)
```

The repositories call the propagator; the UI never does. The callers are `RawMaterialRepository` (add, update and Save), `RawMaterialBrandPriceRepository`, `RawMaterialUnitRepository` (a corrected multiplier) and `SubAssemblyLineRepository.SetLinesAsync` (for each made ingredient made from the sub-assembly, after a loop check). Each call comes after the write has been saved and before its transaction commits, with every ingredient the save changed at once. The propagator is a static class that runs on the caller's context, so a save and the cascade it causes commit together or not at all. It loads the ingredient graph once, recomputes in memory, and saves once. A recomputed cost is stored at its column's scale (see [Precision and rounding](#precision-and-rounding)), so one worked out again from unchanged inputs equals what's stored, and isn't written or dated again.

`IngredientCostGraph` is pure and lives in ApplicationCore. It finds the dependents with a reverse breadth-first search, then orders them with a depth-first topological sort. A linked ingredient's link is just one more edge in the same graph. `IngredientCostPropagator`, in Infrastructure, loads every ingredient and BOM line, asks the graph for the order, and recomputes each ingredient from its components' costs, which are already fresh by then. Sub-assemblies store no cost, so the cascade stops at the ingredients.

The cascade doesn't push changes live to other screens. A screen picks up new costs the next time it's shown, through its `LoadCommand`.

**Recalculate Costs**, on Settings' Costing Defaults tab, is the safety net for a change made outside the app, such as by a script. [CostRecalculator](../src/TinapayanRMS.Infrastructure/Services/CostRecalculator.cs) runs the propagator's own code over every made and linked ingredient, in dependency order (`IngredientCostGraph.InDependencyOrder`), in one transaction, and returns how many were wrong. Purchased materials aren't touched: their cost comes straight from their saved prices.

### Formulas and where they live

| Figure | Formula | Code |
|---|---|---|
| Purchased cost per base unit | The price of the preferred brand (`RawMaterial.PreferredBrand`), else the dearest, ÷ the base units in its unit | `RawMaterialRules.ApplyBrandPrices` and `FindCostBrandPrice`, `UnitConversionRules.CostPerBaseUnit` |
| Purchased buffered cost | A fixed amount, set to cost × (1 + buffer %) on the first price, or whenever the % is typed | `RawMaterialRules.BufferedCost` |
| Made: output grams | Σ line grams × (1 − weight loss %) | `IngredientBomRules.CalculateOutputGrams` |
| Made: cost per gram | Σ (line grams × component cost per gram) ÷ output grams. The buffered cost rolls up the components' buffered costs, and adds no buffer of its own. | `IngredientBomRules.CalculateCost` |
| Linked: cost per gram | The linked-to ingredient's cost × cost share % ÷ weight share %, for both costs | `RawMaterialRules.LinkedCost` |
| Sub-assembly batch | Σ grams; Σ (grams × the ingredient's buffered cost per gram, or its true cost for the Unbuffered view) | `SubAssemblyRules.CalculateTotals` |
| A usage's batch | The sub-assembly's batch plus its add-ins | `ProductRules.CalculateEffectiveBatch` |
| Piece size | Yield = batch weight ÷ weight per piece, or weight per piece = batch weight ÷ yield, depending on which was typed | `ProductRules.ResolvePieceSize` |
| Usage cost per piece | Batch cost × weight per piece ÷ batch weight | `ProductRules.CalculateUsageCostPerPiece` |
| Packaging line cost | Cost per base unit × the quantity in base units | `ProductRules.CalculatePackagingLineCost` |
| Material cost per sellable unit | Σ usage costs per piece × Quantity Required, + Σ packaging | `ProductRules.CalculateMaterialCost` |
| Wastage, labor, overhead, administrative | Each is its % of the sub-assembly cost alone, never of the packaging. The Wastage % is the product's category's (`ProductCategory.WastagePercent`, through `IProductRepository.GetCategoryWastageForProductsAsync` in `SavedProductReader`, and `EditingCategory` on the Products form) or else the shop's (`CostingDefaults.WastagePercent`); a composite's products bring their cost before theirs, so it's added once, at the composite's own | `CostingRules.CalculateCostFromSubAssemblyPercent` |
| Selling price | Base cost = sub-assemblies + wastage + packaging + labor + overhead + administrative; pre-VAT price = base cost × (1 + markup %); selling price = pre-VAT price × (1 + VAT %) | `CostingRules.CalculateSellingPrice` |
| Markup at the current price | (current price ÷ (1 + VAT %) − base cost) ÷ base cost | `CostingRules.CalculateMarkupPercentFromPrice` |
| Price to keep the markup (Price Simulator) | current price × simulated base cost ÷ base cost (VAT cancels out) | `PriceChangeRules.PriceToKeepMarkup` |
| A composite's line | The product's recipe cost per sellable unit × quantity, plus its packaging cost when the composite includes it | `CompositeProductRules.CalculateComponentCost`, `CalculateComponentPackagingCost` |

A made or linked ingredient's costs are worked out by **`IngredientCostRules.Recompute`**, which the cascade (`IngredientCostPropagator`) stores and the Price Simulator uses without storing (`IngredientCostRules.Reprice`: copies of the changed materials and of every ingredient depending on them, in dependency order). A simulated price change moves a material's cost and buffered cost by the same factor (`PriceChangeRules.CostsAfter`).

[SavedProductReader](../src/TinapayanRMS.Pricing/SavedProductReader.cs) is the one place where a saved product is read and priced. The Products screen, Reports, Print, the Recipe API and the MCP server all go through it, so they always agree. It reads any number of products together (`ReadDetailsAsync`, `ReadAllAsync`) in about fifteen queries however many there are, on the repositories' plural reads: packaging link chains are followed in memory, and the products a composite holds are read with it and costed bottom-up. For the Price Simulator, `Build` takes substitute materials (by id) for its sections and packaging, and `PricedAt` prices a composite's products again with them, however deep.

### Precision and rounding

- Every cost is a `decimal` at full precision through the whole cascade. Round only for display, and for the final selling price.
- `TinapayanRMSDbContext.ConfigureConventions` makes every `decimal` column `numeric(18,4)`, so a new column can't fall back to Npgsql's unconstrained `numeric`, which would keep whatever scale a division left. The two costs per base unit are `numeric(28,20)`, so a buffered cost typed per bag comes back exactly as typed after it's divided down to the gram.
- A value that goes through a `numeric(18,4)` column comes back rounded to 4 places: 1/6 becomes 0.1667. A test that reads a value back from the database asserts the rounded value.
- A stored figure worked out in memory is set at its column's scale, after it's worked out at full precision: a cost per base unit through `RawMaterialRules.StoredCost` (20 places), rounded half away from zero as PostgreSQL rounds a `numeric`. At full precision, a cost that doesn't come out even (170 ÷ 240) never equals the stored one, so a Save that changed nothing would read it as changed, log a price again and move the cost's date.
- On screen: 2 decimals for the Products screen's per-piece costing figures and for prices, and 4 for a cost per base unit and for a recipe tab's cost per piece. Grid quantities show 1 decimal and accept up to 4. No amount shows a currency symbol.
- **A `double` never becomes a `decimal` by a plain conversion.** Since .NET 11 a cast, `Convert.ToDecimal` or `new decimal(double)` keeps the double's exact binary value, so 28.35 becomes 28.349999999999997868371792719, where .NET 10 rounded it to 15 digits. The number boxes and grid editors hold a `double`, so every typed figure crosses over: a two-way box binding takes `DecimalValueConverter`, and a grid's `NumericColumn` parses its edit as a decimal (`ParsingMode` `Decimal`). Both go through the number's text, so 28.35 typed is 28.35. Anywhere else, write the value as a `decimal` from the start, or go through its text the same way (`DecimalValueConverter.ToDecimal`).

### Buffered and unbuffered costs

Everything is costed at the buffered cost. The Products screen's **Unbuffered** switch (`CostBasis.Unbuffered`) prices what it shows and prints from the true cost instead. It changes only which cost is read (`RawMaterialRules.CostPerBaseUnitAt`). Nothing stored changes, and the propagators don't know the switch exists.

The Price Simulator has the same switch (`PriceSimulatorViewModel.CostBasis`, `ToggleCostBasisCommand`). It prices every product as saved at both bases on load (`Data.NowBuffered`, `NowUnbuffered`), so a switch only runs the recalculation again at the other basis and reads nothing. `SavedProductReader.PricedAt` already gives a composite's products both costs, so `Build(product, detail, basis, repriced)` picks the right one. The report's printouts carry the basis, so `ProductPrintLayout` marks them "Unbuffered costs" as it does for Products. The switch is session-only and isn't part of a scenario.

---

## 8. Data access

### Reads through Dapper, writes through EF Core

Every public repository read is plain SQL run by Dapper. It returns the same entity objects, with the same navigation properties filled in, that EF Core's `Include` chains used to return. Everything else goes through EF Core: every write, the schema and every migration, and any read inside a save that loads an entity in order to change it.

A read, from [ProductCategoryRepository](../src/TinapayanRMS.Infrastructure/Repositories/ProductCategoryRepository.cs):

```csharp
public Task<IReadOnlyList<ProductCategory>> GetAllAsync(CancellationToken cancellationToken) =>
    dbContextFactory.QueryAsync<ProductCategory>(
        $"SELECT {Columns.Of<ProductCategory>("c")} FROM product_categories c ORDER BY c.name", null, cancellationToken);
```

The read helpers are in `Infrastructure/Queries`:

- **[DapperReads](../src/TinapayanRMS.Infrastructure/Queries/DapperReads.cs)** borrows a DbContext's connection, so reads use the same connection string as EF Core. `QueryAsync` and `QuerySingleOrDefaultAsync` run either on the factory, in a context of their own, or on a save's context. On a context that holds a transaction, a read joins that transaction (`DapperReads.Command`). `ReadAsync` gives a read that needs several queries, such as a multi-mapped join, a context of its own.
- **[Columns](../src/TinapayanRMS.Infrastructure/Queries/Columns.cs)** lists every entity's columns by their snake_case names, `id` first, because a multi-mapped join splits on `Id` (Dapper matches it without regard to case). Dapper maps `cost_per_base_unit` back to `CostPerBaseUnit`, since `DapperReads` turns on `MatchNamesWithUnderscores`. `Columns.Of<T>(alias)` writes the SELECT list.
- **`RawMaterialReads` and `SubAssemblyReads`** load the shared graphs: materials with their category, base unit and units; sub-assemblies with their role and owner.
- **`IngredientBoms`** is the one definition of a made ingredient's bill of materials, an EF Core query over its sub-assembly's lines and its add-ins. The cascade, the cycle checks and `IngredientBomLineRepository` all read it.

The rules for reads:

- **`Columns` must list exactly EF Core's columns.** A property it leaves out comes back as its default value, with no error. [ColumnsTests](../tests/TinapayanRMS.Infrastructure.Tests/Queries/ColumnsTests.cs) compares every list with EF Core's model, so a new or renamed property fails that test until `Columns` has it. (That's what Infrastructure's `InternalsVisibleTo` is for.)
- **Every name in the SQL is snake_case**: EF Core names each table after its `DbSet` and each column after its property, in snake_case (`UseSnakeCaseNamingConvention`, in `TinapayanRMSDbContext.Configure`), so `RawMaterials.CostPerBaseUnit` is `raw_materials.cost_per_base_unit`. Nothing needs quoting. Renaming a table or column means updating that SQL by hand; the persistence tests will catch any you miss. A column alias is snake_case too (`AS product_id`).
- **Read many records in one query, never one query each.** A read for several products (or anything else) is a plural method, such as `GetForProductsAsync(ids)`, rather than a loop of single reads: Reports once made nine queries a product.
- **A list of ids is one parameter:** `WHERE x.id IN ({DapperReads.IdsTable})` with `new { ids = DapperReads.IdList(ids) }`: an `int[]` that Npgsql sends as one array parameter, which `unnest` turns back into rows. Dapper leaves an array whole for Npgsql rather than expanding it into a list.
- **An optional navigation is a `LEFT JOIN`.** Dapper returns null for a split whose `Id` is null.
- **A collection navigation, such as a material's `Units`, is a second query stitched on by id,** not a join, because Dapper's multi-mapping over several result sets is synchronous only.
- **There's no identity resolution.** A material on two lines is two objects.
- **Every read has an explicit `ORDER BY`.** Lines are read `ORDER BY sort_order, id`.
- **A recursive query over links people make stops at a loop.** The app refuses a loop of made and linked ingredients, composites or packaging links, but one made outside it (a hand edit, say) would run until the command timed out. Put `CYCLE <column> SET is_cycle USING path` after the recursive CTE, as [RawMaterialRepository](../src/TinapayanRMS.Infrastructure/Repositories/RawMaterialRepository.cs)'s `UsedBySql` does, and test it with a loop put in past the repository, as `RawMaterialUsedByPersistenceTests` does. `UNION` in place of `UNION ALL` ends a loop too, but PostgreSQL sizes its hash table from the planner's row estimate, which made Used By about 20 ms slower.
- Dapper maps a `date` column to `DateOnly`, and an enum stored as a string back to the enum, on its own.

Never write through Dapper. A Dapper write would skip the cost propagator and the `CostUpdatedAt` stamp, leaving stored costs stale.

### Writing safely

- **Update by loading, then applying.** An `UpdateAsync` loads the tracked entity, copies across only the fields a user can edit, and saves. Never call `dbContext.Update(incoming)` on an object the caller built: it marks every property as modified, and silently overwrites the stored costs. If an edited field feeds a stored figure (a unit's multiplier feeds the cost), the same method recomputes that figure.
- **Delete with `ExecuteDeleteAsync`.** It runs at once, outside the change tracker, so a foreign key's refusal arrives as a `PostgresException`, not a `DbUpdateException`. `RunSafelyAsync` shows it as "This item is currently in use elsewhere and can't be deleted." A test of it expects `PostgresException`. It also runs inside the context's transaction when there is one, so a set of lines replaced with it commits or rolls back with the rest.
- **Replace a set of lines whole.** The `SetLinesAsync` methods first check every incoming row, and every record the rows refer to. Only then do they delete the old rows with `ExecuteDeleteAsync`, insert the new ones with `SortOrder` set to each row's position, recompute any totals and save. A rejected set leaves the old lines untouched. The set a method replaces must match exactly what the UI edits as one unit.
- **One Save, one transaction.** `RawMaterialRepository.SaveAsync` and `ProductRepository.SaveAsync` write everything a screen's form holds in one context and one transaction. Each step is an `internal static` method that takes the context, so steps from several repositories compose. Each step saves as it goes, so the checks of later steps see what earlier ones wrote. A part passed as `null` means "leave it as saved". The caller's entity is copied, never attached, so a rolled-back save never leaves an id on it. The cost cascade runs once, at the end, on the same context and in the same transaction, so it commits with the save or not at all.
- **Save inputs are immutable records:** `RecipeDraft`, `NewSubAssemblyDraft`, `LinkedUsageDraft`, `PackagingLinkDraft`, `ProductMove`, `RawMaterialMove` and the `RecipeLine` record struct, all in `ApplicationCore/Interfaces`. Where a section's sub-assembly goes (`NewSubAssemblyDraft.Placement`, a `SectionPlacement`) and what the packaging links (`PackagingLinkDraft`) are closed records, one case for each thing Save can do.
- **`CostUpdatedAt` is stamped centrally**, in `TinapayanRMSDbContext.SaveChangesAsync`, by comparing the costs' original and current values. Don't set it by hand.
- **Every saved time comes from the context's clock**: `dbContext.LocalNow()`, which reads `TinapayanRMSDbContext.Clock`. It's the system's unless the factory was given another (`SharedTransactionDbContextFactory(connectionString, clock)`). A repository never reads `DateTime.Now`, so a test that passes a `TestClock` to `CreateDbContextFactory(clock)` can assert the exact time a save stamped. `UpdatedAt` stays a repository's to set, because it means "saved from a form": a recipe-only Save sets it, and a soft delete doesn't, so no rule about changed rows could stamp it.

The shape of an aggregate save, abridged from `RawMaterialRepository.SaveAsync`:

```csharp
await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
int id;
var costChanged = false;
await using (var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken))
{
    var fields = new RawMaterial { Id = material.Id, Name = material.Name /* … the form's fields … */ };
    costChanged = fields.Id == 0
        ? await AddAsync(dbContext, fields, cancellationToken)       // internal static: rules first, then SaveChanges
        : await UpdateAsync(dbContext, fields, cancellationToken);
    id = fields.Id;

    // … the units, brand prices and a made ingredient's add-ins, each through an internal static write method …

    if (costChanged)
    {
        await IngredientCostPropagator.RecomputeAndPropagateAsync(dbContext, id, cancellationToken);   // same context, before the commit
    }

    await transaction.CommitAsync(cancellationToken);
}
```

### Cross-entity services

`Infrastructure/Services` holds the work that spans entities:

- **`IngredientCostPropagator`**: the cascade. It's an internal static class that runs on a repository's context, inside its transaction, and isn't exposed through ApplicationCore interfaces.
- **`CostRecalculator`** (`ICostRecalculator`): Settings' Recalculate Costs, the propagator's code run over every made and linked ingredient, in one transaction.
- **`IngredientDependencies`**: loads the ingredient dependency graph, made of BOM edges (`IngredientBoms`: sub-assembly lines and add-ins) and link edges, for the cycle checks, and `EnsureNoCycleAsync` checks one made ingredient against it.
- **`ProductCloner`** (`IProductCloner`): Clone and Create Variant. A clone links the sub-assemblies the source links, copies the source's own ones and copies the packaging. A variant links every sub-assembly the source uses, the ones it links and its own alike, and has no packaging. It's the only way a product links another product's own sub-assembly. `SetRecipeAsync` takes `linksOthersOwn` for it, and otherwise refuses such a link (`SubAssemblyRules.EnsureLinkable`) unless the product already has it. While a variant links one, the source can't drop it from its recipe or be deleted. On the Products screen, `ProductUsageEdit.LinkedOwner` names the source, and the linked badge (`OpenLinkedSourceCommand`) opens it.
- **`JsonViewStateStore`** (`IViewStateStore`): the view-state file. A missing or broken file reads as empty, and a failed write is dropped.
- **`DatabaseBackups`** (`IDatabaseBackups`): Settings' Database tab and the daily automatic backup, taken by PostgreSQL's own tools through [PostgresTools](../src/TinapayanRMS.Infrastructure/Services/PostgresTools.cs). Each tool runs through .NET 11's `Process.RunAndCaptureTextAsync`, which reads both its pipes as it runs: one that fails throws `PostgresToolException` with what it wrote to standard error, and one that's cancelled is stopped. Every run gets `--no-password`, since with no password available a tool otherwise waits at a prompt on a console nobody can see, and one still running after half an hour (`PostgresTools.DefaultTimeout`) is stopped and throws `TimeoutException`, so no tool can hold up the backups and restores waiting their turn behind it. A backup is `pg_dump --format=custom` into a `.backup.partial` file (`BackupRules.PartialSuffix`), which takes its `.backup` name only once `pg_dump` has finished, so a run cut short is never listed, counted as the day's backup or restored; one left behind is deleted after a day. A copy in or out (`CopyAsync`) goes the same way, so one cut short leaves the file it would have replaced as it was. Backups and restores take turns (a `SemaphoreSlim`), since the day's automatic backup can start while Settings is taking one. A restore reads the file first with `pg_restore --list` (`BackupRules.TryReadArchiveListing`): a file that isn't a backup, or is from a newer PostgreSQL, is refused before anything changes. It then takes a safety backup and restores into a scratch database, `<database>_restoring` (`--no-owner --no-privileges --exit-on-error --single-transaction`). There it brings an older backup up to date with `MigrateAsync`, and refuses one from a later version, or of another app (`BackupRules.UpdatesNeeded`). Only then does it swap the scratch database in: it closes the database to new connections, ends the app's own, renames the database to `<database>_replaced` and the scratch database to its name, and drops the one it replaced. So a backup that's refused or fails part-way never touches the data. Every statement runs on its own unpooled connection to the `postgres` maintenance database. A new backup or a file copied in never reuses a name in the folder: names go to the second, so `BackupRules.FreeFileNameFor` moves the time on a second while the name is taken. The app then restarts, since every singleton ViewModel holds data from before.

### Schema conventions

- There's one `IEntityTypeConfiguration<T>` per entity in `Configurations/`, and `OnModelCreating` applies them all from the assembly.
- Every table is its `DbSet`'s name in snake_case, and every column its property's.
- Every name a user chooses (materials, categories, products, sub-assemblies, roles, units) has a unique index. Every text column has the `case_insensitive` ICU collation (`TinapayanRMSDbContext.CaseInsensitiveCollation`, set in `ConfigureConventions`), so "flour" is a duplicate of "Flour" and names sort without regard to case, as they did on SQL Server.
- That collation counts a space at the end of a name, so a name goes in without spaces at either end: `ProductRules.StoredName`, `RawMaterialRules.StoredName` and `UnitOfMeasureRules` in their repositories, `.Trim()` in the other forms' ViewModels. A new kind of named record needs the same, or `"Flour "` gets past the unique index beside `"Flour"`.
- Enums are stored as strings (`HasConversion<string>()`), so the data reads clearly and Dapper maps them back by name.
- Owned children cascade on delete (lines, units, prices, costing parameters). References are `Restrict`, so a material that a line uses can't be deleted. A product's category is `SetNull`.
- Check constraints back up the rules. They're named `CK_<Table>_<Rule>`. A figure a read divides by or adds up has one (`CK_PackagingLines_AuthoredQuantity`, `CK_RawMaterialUnits_BaseUnitsPerUnit`, `CK_CostingParameters_Percents`, the `_Grams` ones): `SavedProductReader.ReadAllAsync` prices every product together, so one bad row would otherwise fail Reports and the Recipe API for all of them.
- Every line table has a `SortOrder`, because users drag lines into order.
- Where the CLR default is a meaningful value, a column default is paired with `HasSentinel`, as on `Product.QuantityRequired` and `Product.Stage`.
- Every `DateTime` column is `timestamp without time zone` (`ConfigureConventions`): the app stamps the shop's local time (`LocalNow()`), and Npgsql's default, `timestamptz`, takes only UTC. PostgreSQL keeps a time to the microsecond.
- On a unique index over a nullable column, PostgreSQL lets null rows repeat. Add `.AreNullsDistinct(false)` if null rows must collide too.

### Migrations

Migrations come only from `dotnet ef migrations add`. Never write or edit one by hand. (The SQL Server migrations, some with hand-added data-conversion SQL from before this rule, were replaced by one `InitialCreate` for PostgreSQL on 2026-10-21.)

**Data a schema change needs goes in a data upgrade, not the migration**, and so does data an older one left to be filled in later (the target buffers, 2026-11-05). [DataUpgrades](../src/TinapayanRMS.Infrastructure/Upgrades/DataUpgrades.cs) lists one-time changes to the data, oldest first, each a name and a method on the DbContext. EF Core's seeding hook (`UseSeeding` and `UseAsyncSeeding`, set in `TinapayanRMSDbContext.Configure`) runs the ones a database hasn't had after the migrations and inside their transaction, whenever a database is migrated: by `dotnet ef database update`, by a restored backup being brought up to date, and by the test fixtures. Each is recorded in `data_upgrades` and never made again. Each names the migration whose schema it needs, and waits until that's applied, so a database migrated back to an earlier migration skips it; it runs against the schema the migrations leave, so it can't read a column a later migration drops. Add one to the end of the list, never change one that has run, and test it in `DataUpgradesTests`.

1. Change the entity, its configuration, and its `Columns` list.
2. `dotnet ef migrations add <Name> --project src/TinapayanRMS.Infrastructure`.
3. Read the generated `Up` and `Down` before you accept them.
4. `dotnet ef migrations has-pending-model-changes --project src/TinapayanRMS.Infrastructure` should report no changes.
5. Regenerate the compiled model (see [The compiled model](#the-compiled-model)).
6. `dotnet ef database update --project src/TinapayanRMS.Infrastructure`.
7. Run the tests. The fixtures migrate the test databases themselves.

To discard a migration you've generated but haven't applied anywhere, run `dotnet ef migrations remove --project src/TinapayanRMS.Infrastructure`.

**Renaming a column without losing data.** EF Core pairs renamed columns by guesswork. If you rename two columns of the same type on one table in one step, it can pair them crosswise and swap their data, or drop and re-add them. Use two migrations instead:

1. Rename the properties, and pin the old column names with `HasColumnName("OldName")`. Generate any other pending migration now; it carries no column change.
2. Remove the pins and generate again. The only difference is now the column names, so EF Core emits an unambiguous `RenameColumn`.

### The compiled model

The app runs on a compiled EF Core model, the generated code in `Infrastructure/CompiledModels/`, so the first query doesn't spend time building the model from the configurations. EF Core finds it by itself, through the `[assembly: DbContextModel]` attribute in `TinapayanRMSDbContextAssemblyAttributes.cs`, so `TinapayanRMSDbContext.Configure` doesn't call `UseModel`. The app, the tests and the repositories all use it. The `dotnet ef` commands don't: `migrations add`, `has-pending-model-changes` and `dbcontext optimize` itself build the design-time model from `OnModelCreating`.

- **Regenerate it after every model change**: an entity, a configuration, `ConfigureConventions` or `OnModelCreating`. Run `dotnet ef dbcontext optimize --project src/TinapayanRMS.Infrastructure --output-dir CompiledModels --namespace TinapayanRMS.Infrastructure.CompiledModels`. It overwrites the folder, and the result is committed with the change. Never edit the generated files by hand.
- **A stale one still builds and runs.** It finds properties by name, so it keeps mapping the model as it was. [CompiledModelTests](../tests/TinapayanRMS.Infrastructure.Tests/CompiledModels/CompiledModelTests.cs) fail until it's regenerated. They compare it with the model built from the configurations: the entities, properties, keys, relationships and indexes, and the tables, columns, types and constraints they map to.
- **If the regeneration won't build**, for example because an entity class it names was deleted, delete `CompiledModels/` and run the command again.
- A compiled model leaves out what only migrations use, such as column collations and seed data. Those come from the design-time model, so they don't need to match.

---

## 9. The UI layer

### The shell

[MainWindow](../src/TinapayanRMS.UI/Views/MainWindow.xaml) is an `AppWindow`, which is a Syncfusion `ChromelessWindow`: the title bar is drawn in the theme, with Back and Forward on its right, just before Minimize. The hamburger button opens an `SfNavigationDrawer` menu with Raw Materials, Sub-Assemblies, Products, Reports, Production Plan and Price Simulator, and a Settings button at the bottom. The screens are the tabs of `MainTabControl`, a `TabControlExt` with two important settings:

- **`CloseMode="Hide"`**: closing a tab hides it rather than deleting it, so the menu can always show it again.
- **`IsDisableUnloadTabItemExtContent="True"`**: every tab's content stays loaded. Scroll positions, sort orders, sub-tabs and typed text survive a switch without any code.

**A tab's screen is made the first time the tab is shown.** `MainWindow` keeps a factory for each tab (`_screenFactories`), and `CreateScreen` runs it from `MainTabControl.SelectionChanged`. `TabControlExt` takes in the shown tab's content only after it has raised that event, so the content is in place in time. The first tab's screen is made in the constructor, since the control can read it before any `SelectionChanged`. Making all seven at startup used to take most of the time between the window appearing and the app being ready. A ViewModel exists from the start, so a link from another screen can open a record on a screen that isn't made yet: the screen shows the record once it's made and loaded.

The app starts with only Raw Materials, Sub-Assemblies and Products open: Reports and Price Simulator start hidden (`Visibility="Collapsed"`), like a closed tab, until the menu shows them. The Print tab stays hidden until Products asks for it. The Settings dialog, `SettingsWindow`, is built afresh each time it opens, over the same singleton ViewModels. When it closes, it disposes its delete confirmations and releases its controls (grids, text boxes and picker filters), which would otherwise keep every closed dialog in memory. Then the main window reloads the screen on show, so its pickers include anything added in Settings.

### Views and cards

A view is a `UserControl` whose constructor takes its ViewModel:

```csharp
public RawMaterialsView(RawMaterialsViewModel viewModel)
{
    InitializeComponent();
    DataContext = viewModel;
    viewModel.RegisterNativeMessageBoxHandler();   // ConfirmDelete as a Yes/No MessageBox, ConfirmUnsavedChanges as Yes/No/Cancel
    // … handlers for the screen's other Interactions (prompts) …
}
```

A large screen is split into card `UserControl`s in a folder named after it (`Views/RawMaterials/`, `Views/SubAssemblies/`, `Views/Products/`). A card never sets its own `DataContext`: it inherits the screen's ViewModel. The screen's XAML decides when each card shows. Keep each XAML file shallow, and keep a card's code-behind to view-only work, such as focus and scrolling.

Each screen puts an `ErrorToast` over its content, and a `SuccessToast` where it reports success. Both bind to the screen's `ErrorMessage` and `SuccessMessage`, which every `ViewModelBase` has.

### ViewModels

**[ViewModelBase](../src/TinapayanRMS.UI/ViewModels/ViewModelBase.cs)** is the base of every screen's ViewModel. It provides:

- `ErrorMessage` and `SuccessMessage`, which the toasts show.
- **`RunSafelyAsync(action)`**, which clears both messages and runs the action. Whatever the action throws becomes `ErrorMessage`, in the words [Failure](../src/TinapayanRMS.Infrastructure/Services/Failure.cs) gives it: a `BusinessRuleException`'s message as written; what the database refused or couldn't do, put plainly by its error code (a duplicate name, something in use, a database that can't be reached); a file that couldn't be written or opened (`UnauthorizedAccessException`, and an `IOException` in its own terms: open in another program only for a sharing or lock violation, a missing file or folder as that, anything else in Windows' words); what `pg_dump` or `pg_restore` said (`PostgresToolException`); and anything else, a bug, as "Something went wrong, and the details went to the log: …". A refusal is logged as a warning, anything else as an error with its stack. Nothing reaches ReactiveUI, which would hand it to `App`.
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
- **Dialogs and prompts** are `Interaction`s that the view handles: `ConfirmDelete` (a Yes/No `MessageBox`, through `RegisterNativeMessageBoxHandler`), `ConfirmUnsavedChanges` (Yes / No / Cancel for Save / Don't Save / Cancel, through the same), `ExportToExcel` (a Save dialog and the export, through `RegisterExcelExport`), `PromptForName` and `PromptForNewCategory` (`TextPromptWindow.Prompt`), Find (`QuickFindWindow.Pick`, opened by `MainWindow` itself), and the Products and Sub-Assemblies screens' `PromptForMadeIngredient` (`TextPromptWindow.PromptWithChoice`, a name box with a picker under it, taking a `ChoicePromptRequest` that `MadeIngredientPrompt` builds for both).
 Tests register handlers of their own.

- **One screen asks another for something** through an `IObservable<T>` backed by a `Subject<T>`: `ProductOpenRequests` (from Raw Materials, Sub-Assemblies and Reports), `RawMaterialOpenRequests` (from Reports), `NewProductRequests` (from Sub-Assemblies' New Product, handled by `ProductsViewModel.StartNewProductFrom`), `SubAssemblyOpenRequests` and `PrintRequests` (from Products). `MainWindow` subscribes to them, calls the target screen's `Open*` (or `StartNewProductFrom`) method and shows its tab, so the ViewModels never know about each other.

### Loading and refreshing

- **There's one load trigger: showing a tab.** `MainWindow.LoadSelectedTab` runs the shown tab's `LoadCommand` at startup and on every `MainTabControl.SelectionChanged`. It ignores `SelectionChanged` events that bubble up from grids and pickers, and runs the loads through `ScreenLoads`: never two of a tab's side by side, and one asked for while the last runs follows it if the tab still shows, since the running one may have read before another screen saved. Views never load data on `Loaded`. The Settings dialog loads its lists when it opens.
- **Every `LoadCommand` is a fetch-then-merge, never a clear-and-reload.** It awaits all its reads first, then records the ids of the selected row and of every picker's choice, then merges inside `ApplyRefresh` and picks everything again by id. A `ComboBoxAdv` can clear its own `SelectedItem` when its item is replaced mid-merge, which is why the ids are read before the merge.
- **[CollectionMerge](../src/TinapayanRMS.UI/ViewModels/CollectionMerge.cs)** (`MergeFrom`) keeps the existing object for every row whose values haven't changed, because that object is what a grid's selection and scroll position and a picker's `SelectedItem` are tied to. An `ObservableCollection` merge takes a key of any type, and rows that share a key are matched in order. `HasSameValues` compares scalar properties, plus those of referenced entities one level deep. A collection that a row owns (a material's `Units`) counts as part of the row.
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
- **A dirty form is never left without asking.** Products, Raw Materials and Sub-Assemblies are `IUnsavedChangesScreen`s: `ResolveUnsavedChangesAsync(revert)` asks Save / Don't Save / Cancel through `ConfirmUnsavedChanges` (`ViewModelBase.AskAboutUnsavedChangesAsync`) and says whether the form may be left. A screen asks before its own form shows another record (a list pick, through `PickAfterAskingAsync`; New; the `Open*Async` methods the links call). [ScreenNavigator](../src/TinapayanRMS.UI/ViewModels/ScreenNavigator.cs) asks before the screen is left, with `MainWindow` telling it which screen shows and showing the one asked for: a tab change is undone and redone once answered (`TurnBackToUnsaved`, then `AfterAskingAsync`), links go through `OpenOnAsync`, Back and Forward (`GoBack`, `GoForward`) peek at `NavigationHistory.BackTarget`/`ForwardTarget` first, and `OnClosing` asks before the app closes (`CloseAfterAskingAsync`). With `revert`, Don't Save also puts the form back as saved. Products and Print count as one screen. What fails while asking, such as reading the record back, shows on the form, which isn't left. A new path that replaces a form's record, or leaves a screen, goes through one of these.
- **An edit the form hasn't seen yet keeps Save offered** (`IUndoRecorder.IsEditPending`): a grid cell being typed into, and an add row with something typed into it until the row is added or dropped. The click on Save is what ends the edit, so a Save that went disabled as the edit ended would lose that click.

### Navigation history

[NavigationHistory](../src/TinapayanRMS.UI/ViewModels/NavigationHistory.cs) holds the Back and Forward stacks of `ScreenVisit(AppScreen, RecordId)`. Back keeps up to 50 stops. A step is any change of screen: the menu, the tab strip, a closed tab, or a link from one screen to another. Picking another record in a list isn't a step. `MainWindow` records each step with the record the screen had open as it was left. Going back reopens that record through the screen's `Open*` method, unless the screen's form holds unsaved changes. Alt+Left and Alt+Right work too: they're handled on the window's `PreviewKeyDown`, which checks `SystemKey` because WPF reports a key pressed with Alt as `Key.System`.

**Find** (Ctrl+K, on the same `PreviewKeyDown`, or the title bar's magnifier) is [QuickFindWindow](../src/TinapayanRMS.UI/Views/QuickFindWindow.xaml), a chromeless modal dialog built like `TextPromptWindow`, over the singleton [QuickFindViewModel](../src/TinapayanRMS.UI/ViewModels/QuickFindViewModel.cs). Each time it opens, `LoadCommand` reads every raw material, standalone sub-assembly (`GetStandaloneAsync`) and undeleted product into `QuickFindResult(AppScreen, Id, Name, Kind, Group)` rows, and `Query` filters them through `QuickFindViewModel.Match`: every word somewhere in the name, ignoring case and accents (`CompareOptions.IgnoreNonSpace`); exact, then prefix, then a word starting with the first word, then the rest; at most `MostShown` (50). The keyboard stays in the box: its `PreviewKeyDown` moves `SelectedResult` with Up, Down, PageUp and PageDown. `MainWindow.OpenFind` opens the pick through `OpenOnAsync` and the screen's `Open*Async` (`_recordOpeners`), so the unsaved-changes questions are the links' own.

### Vim keys

[Vim/](../src/TinapayanRMS.UI/Vim/) holds an optional layer of Vim-style keys over the whole app, in the manner of Vimium. The user turns it on in Settings → Keyboard. `KeyboardSettingsViewModel` keeps the choice in the view state (`vim-keys`), and `App.OnStartup` applies it. While it's off, each handler returns after a single `bool` check.

- **Where keys are caught.** `VimKeys.Register()`, called from `App`'s static constructor, adds class handlers on `Window` for `PreviewKeyDown`, `PreviewTextInput` and `GotKeyboardFocus` (with `handledEventsToo`).
  - Letters come from `PreviewTextInput`. `SfDataGrid` starts a cell edit for a letter in `OnTextInput`, which this handler runs before, and `e.Text` is already the keyboard layout's character.
  - Keys that type nothing (Esc, Ctrl+R) come from `PreviewKeyDown`.
- **Modes.**
  - Insert is the app as it always was. It applies when focus is in a `TextBoxBase` (a field, or a picker's text) or in a grid cell being edited.
  - A column filter's pop-up isn't part of its grid. `VisualTreeWalk` steps out of a pop-up to its `PlacementTarget`, and the filter's pop-up is placed on the heading's funnel, so walking up from its search box would land in the grid and read as Normal, swallowing every letter. `PlaceOf` stops at the filter control's own pop-up (`Popup { TemplatedParent: GridFilterControl }`), so the search box is a field.
  - In a grid, Insert lasts until Esc (`_gridIsInsert`, set whenever a cell is seen being edited). Tab, Enter or an arrow ends the cell's edit, but the next letter or digit still goes through, so the grid starts that cell's edit itself. A grid reached from somewhere else takes the mode the keys were in (`Arrive`), so Tab from a field in Insert goes on typing. Esc on a cell that isn't being edited takes the grid back to Normal.
  - Normal applies everywhere else. It also applies to a field reached with `j`/`k` or left with Esc. In Normal, every printable character is handled, so none types or starts an edit.
  - Esc in a cell commits it through `GridEditCommit.Commit(grid, keepCurrentCell: true)` instead of cancelling it. Esc is handled only when it has something to do, so otherwise a dialog's Cancel still gets it.
  - Ctrl chords stand for other keys, as in Vim: Ctrl+[ is Esc, and Ctrl+N and Ctrl+P are Down and Up while a dropdown or suggestion list is open (`KeyForChord`). `PressInstead` sends the key through `InputManager.ProcessInput`, so it reaches what a typed key reaches. That includes a `ButtonAdv.IsCancel` button, which takes Esc as an access key and only through the input manager. Ctrl is still held, so it's lifted from the thread's key state (`GetKeyboardState`/`SetKeyboardState`) while the key goes through. WPF reads modifiers live from that state, and Ctrl+Down would jump a list to its last row.
- **Speed.** Where the keyboard is (a `Place`: field, grid, tree, table, other or overlay) is worked out once per focus change and cached. A key is then a `FrozenDictionary` lookup (`VimKeySequence`, built from `VimKeymap`) plus the action.
- **Keys stand for the keys they replace.**
  - In grids, trees and lists, `j` is raised as Down and `dd` as Delete. In a tree, `o` is raised as Enter and `r` as F2.
  - Each is raised on the element that has the keyboard, as WPF's `KeyboardDevice` raises a real key: `PreviewKeyDown`, then `KeyDown` unless the first was handled. So the controls' own handling and the app's behaviours (`GridAddNewRow`, `GridUndoSteps`, the trees' `Tree_KeyDown`) apply unchanged.
  - A raised key reads Ctrl and Shift from the physical keyboard (`KeyboardDevice.Modifiers`), so only keys typed without a modifier are raised. `gg`, `G`, `0` and `$` use the grid's `MoveCurrentCell`, with `GetFirstDataRowIndex`, `GetLastDataRowIndex` (which skips the add row) and the column helpers. The trees pick their row through `SelectedItems`.
  - In a form, `j`/`k` call `MoveFocus` (Next or Previous), because an arrow key would step a number box's value.
- **Each list card has a toolbar under its search box**: icon-only `ButtonAdv`s (`SizeMode="Small"`, the name in `Label` and `ToolTip`, `Focusable="False"` so Tab still goes from the search box to the tree) bound to the commands of the tree's empty-space menu (`EmptySpaceMenu` in each ViewModel), with no parameter. Raw Materials' two New Category items share one `DropDownButtonAdv`. A list command added to that menu belongs in the toolbar too. The properties are set on each button, not through a `Style`, so the theme's template stays intact. A Small `ButtonAdv` comes with `Padding` 2,0,4,0 and draws the 14 px glyph in the top-left corner of a 16 × 16 icon box, so each toolbar button sets `IconWidth` and `IconHeight` to 14 and `Padding` to 3,1,3,0 to centre it. The `DropDownButtonAdv` ignores `Padding`, so it gets a 16-tall icon box with the glyph at its bottom instead. Their icons include `NewFolderIconTemplate`, `ExpandAllIconTemplate` and `CollapseAllIconTemplate` in `App.xaml`.
- **`SfTreeView` in Extended mode** (the Products, Raw Materials and Sub-Assemblies lists) has three behaviours to work around:
  - its Home only moves the focus border, without picking the row;
  - its arrow keys do nothing while the current row is hidden, for example by a search;
  - a `SelectedItem` set from outside is inserted in front of the rows already picked, rather than replacing them.

  So `gg`, `G`, and `j`/`k` from a hidden row pick their row by replacing `SelectedItems`: clear it, then add the row, as the screens' `SelectedTreeNode` setters do.
- **Windows** take part through `IVimHost`, which `MainWindow` and `SettingsWindow` implement. It provides `gt`/`gT` (`VimTabs.Step`), Back and Forward, the screens the command line names, and Settings.
- **The command line** (`:`) is parsed by `VimCommandLine` and run by `VimCommands`. A command runs on the screen found by walking `DataContext` up from the keyboard, through `IRecordScreen` (New, Save, Delete, `OpenByName`) and `IRevertScreen`. It goes through `ICommand.Execute`, as a button does, so its errors end up where they always do. Enter always closes the line first. A parse error, or the reason `VimCommands.Run` returns, then shows in the same bar as a message with no box (`VimOverlay.ShowMessage`), which the next key, click or a 5-second timer puts away.
- **The expression register** (Ctrl+R then `=` in Insert, as Vim's) types the answer to a sum into the box the keyboard is in.
  - Ctrl+R in Insert sets `_awaitingRegister`, shown as `"` in the indicator, as Vim shows it. The next character names the register: `=` opens the expression line (`VimOverlay.ShowExpressionLine`), and any other character is dropped. A key that types nothing (an arrow, Enter, Tab) cancels the wait and does what it does. Ctrl+R in Normal is still redo.
  - The keyboard never leaves the cell. Moving focus to a box in the bar would end the cell's edit (`GridEditCommit.CommitWhenFocusLeaves`) and lose the caret. So the overlay captures keys as the hints do (`Capturing`, `TakeKey`, `TakeText`), and the bar draws the sum as text. Space comes through `TakeKey`, because `DoubleTextBox` handles Space on key-down, so no text input follows it. Every other key is taken and ignored, so an arrow or Tab can't move the cell from under the sum.
  - `VimExpression.Evaluate` is a recursive-descent parser over `decimal` (`+ - * / %`, brackets, unary signs, comma-grouped thousands). Division is decimal, unlike Vim's whole-number division. A sum it can't work out keeps the line open with the reason after it.
  - `VimKeys.TypeAnswer` types the answer a character at a time through `TextCompositionManager.StartComposition`, with `_raising` set. So a number box applies its own mask, limits and decimals, and `GridUndoSteps` sees an ordinary edit. The answer is first rounded (half away from zero) to the decimals the box takes (`DecimalsTaken`: `MaximumNumberDecimalDigits`, which `NumericEditRenderer` sets for a `TypedDecimalDigits` column, else `NumberDecimalDigits`), since the mask would otherwise cut it short. A grid in Insert between cells has no editor yet, so its current cell's edit is begun first (which `GridCellEditing` can still refuse), and the answer is typed once the editor has the keyboard.
- **Overlays.** `VimOverlay` is one adorner per window, on the content's adorner layer. It holds:
  - the hint labels (`HintTargets`, `HintLabels`), all drawn in a single `OnRender`;
  - the command bar;
  - the cheat sheet, built from `VimKeymap` so it lists what the keys actually do.

  A hint clicks a `ButtonBase` or `SplitButtonAdv` through its protected `OnClick`, by reflection, because `ButtonAdv`'s automation peer has no Invoke pattern. A hint on a picker puts the keyboard in its `PART_Editable` box (`VimKeys.FocusPicker`), not on the picker: a Shift+Tab out of the box leaves a `ComboBoxAdv` unfocusable for good, and its `Focus()` then does nothing.
- **To add a key**, add a `VimKeyBinding` to `VimKeymap` (keys, action, contexts, description). Then handle its `VimAction` in `VimKeys.Perform`, or in the `In*` method for its place. The cheat sheet picks it up.
- **Slow keys.** Debug builds log any key the layer takes more than 4 ms to handle (`The Vim key … took … ms`). Most of that time is the screen's own response, such as a picked row loading its form.
- **Tests** are in `UI.Tests/Vim/`.
  - The parser, keymap, command line, expression evaluator (`VimExpressionTests`) and labels are tested without WPF.
  - `VimKeysGridTests` raises text input and key presses on a real off-screen grid.
  - `VimKeysPickerTests` checks a hint reaches a real off-screen picker, including one a Shift+Tab has left unfocusable.
  - These tests switch the static `VimKeys.State` on and off, so they run in `OffscreenGridCollection`.

### Controls and behaviours

The app extends Syncfusion's controls with **attached behaviours** in `Controls/` rather than subclassing them. You turn a behaviour on in XAML, and it hooks the control's events. Behaviours that apply everywhere are registered once, in `App`'s static constructor.

| Behaviour | Set on | What it does |
|---|---|---|
| `GridFillerRows.IsEnabled` | A grid with height to fill | Draws the lines of empty rows in the space under the last row (`GridFillerLayer`, in the grid's template). It adds no rows to the grid, so selection, editing and export never see them |
| `GridRowReordering.IsEnabled` | An editable grid of lines | Lets rows be dragged into order. The collection's order is what's saved as `SortOrder`. |
| `GridRowInserting.IsEnabled` | An editable grid of lines with an `ItemFactory` | Puts a blank line from the `ItemFactory` in above the current one, as one undo step, and opens its first column. It's run by the `GridRowInserting.InsertLine` routed command: a button with `CommandTarget` set to the grid, the Insert key, or Vim's `O`. It's on the ingredient line tables of Sub-Assemblies and of a product's section form, including its add-ins. |
| `GridAddNewRow.ItemFactory` | An editable grid | Makes the add row edit a row the screen creates |
| `GridAddNewRow.DropWhenUntouched` | An editable grid with row selection | Swaps in `UntouchedAddRowSelectionController`. It drops an add row nothing was typed into instead of adding an empty line. Tab from the last cell Tab can reach on a typed-into add row adds the line, as Enter does, and opens the next add row's first cell; the grid only ended the cell's edit there. |
| `GridEditCommit` | An editable grid | Commits an open cell edit when focus leaves the grid, so Save sees it |
| `GridCellEditing.IsEnabled` | A grid | Makes a column editable on some rows and not others |
| `GridUndoSteps.Recorder`, `FieldUndoStep.Recorder` | A grid; a field | Undo steps (see above) |
| `GridHeaderAlignment.IsBottom` | A grid whose headings wrap (`GridHeaderHeight.Lines`) | Puts the headings, and their sort arrows and filter funnels, at the bottom of the header row, so one-line headings line up with the last line of two-line ones. It sets each header cell as it loads rather than through a `HeaderStyle`, which would drop the theme's style unless it was `BasedOn` it. On the Price Simulator's Products table. |
| `CheckboxFilterFill.IsEnabled` | A grid that filters | Widens a filter pop-up's list of values to the pop-up. The theme fixes it at 188px, which cut long names off. |
| `AutoCompleteDropDown.IsEnabled` | A grid with a drop-down column that's typed into | Fixes Syncfusion's drop-down cell. Pair it with `AllowNullInput="True"` on the column. The pick reaches the row only when the edit is committed: the renderer makes each drop-down column's `UpdateTrigger` Explicit before every edit, so no column needs to set it. A commit with text that names nothing in the list keeps the row's value (`AutoCompleteDropDownRenderer.UpdateSource`). |
| `OptionalColumns.IsOptional`, `CostBasisColumns` | Grid columns | Show or hide columns from the screen's state. A grid's columns aren't in the visual tree, so they can't bind to it themselves. |
| `NumericColumn`, `NumericEditing.IsEnabled` | Number columns; their grid | Thousands separators and right alignment. An edit is parsed as a decimal (`ParsingMode` `Decimal`), so 6.67 typed is 6.67 (see [Precision and rounding](#precision-and-rounding)). `TypedDecimalDigits` accepts more decimals than the column shows. |
| `MouseWheelForwarding`, `FocusScrolling` | Stacked grids; tall pages | Keep the page scrolling under the mouse, and stop a click from scrolling the page |
| `CategoryTextCommit.Screen` | A Category picker | Offers to add a typed name that isn't a category yet |
| `ReadOnlyText.IsGreyed` | A `DoubleTextBox` | Greys out a number that can't be typed |
| `GridKeyboardFocus`, `GridHeaderHeight`, `PickerMatchHighlight`, `PickerSelectOnEntry`, `PickerRestoresPick`, `UndoShortcuts` | Every grid, picker or window (registered in `App`) | Focus sits on a cell, never the whole grid; header rows fit their text (two lines for a grid whose headings wrap, `GridHeaderHeight.Lines`); a picker highlights its best match for Enter or Tab; a picker selects its text when clicked or tabbed into, and a letter typed over a picked name stays; a picker emptied while bound to a value that can't be null (an enum) shows that value again once the keyboard leaves; Ctrl+Z and Ctrl+Y |
| `AppWindow`, `ChromelessWindowDpi`, `ChromelessWindowMask`, `RestoreRendering` | Windows | The chromeless title bar, and fixes for its DPI changes, content mask and restore-from-taskbar flicker |
| `ErrorToast`, `SuccessToast` | Each screen | Show `ErrorMessage` and `SuccessMessage` |

### Theme and visual conventions

- **The theme** is Syncfusion's `Syncfusion.Themes.Windows11Light.WPF` package. A window is themed with `SfSkinManager.SetTheme(window, new Theme("Windows11Light"))` before it's shown: `App.OnStartup` does it for `MainWindow`, and `MainWindow.OpenSettings` for the Settings dialog. `ApplyThemeAsDefaultStyle` is deliberately left unset, because it would restyle every window. `SizeMode` is `Default`.
- **Colours** come from [Themes/Palette.xaml](../src/TinapayanRMS.UI/Themes/Palette.xaml), the Catppuccin Latte palette with derived Tint, Deep and Wash steps that make each hue readable as text or as a background. [App.xaml](../src/TinapayanRMS.UI/App.xaml) names them as design tokens (`PageBackgroundBrush`, `CardBackgroundBrush`, `AccentBrush`, `ErrorForegroundBrush`, …) and defines the shared styles (`CardStyle`, `SectionHeaderStyle`, `FormLabelStyle`, `FieldLabelStyle`, `FigureTileStyle`, …). Views use the tokens, never a hex colour or the raw palette.
- **Icons** are Segoe Fluent Icons glyphs, through the `*IconTemplate` data templates and `IconGlyphStyle`. Don't add an icon package: one was tried, and its icons weren't crisp. WPF draws emoji as outlines only, so a full-colour picture is an icon file (below).
- **Icon files**: an icon that has to be an image rather than a glyph (a downloaded `.png` or `.ico`) goes in [Assets/Icons/](../src/TinapayanRMS.UI/Assets/Icons/). The UI project compiles every `.png` and `.ico` there into the app as a WPF `Resource`, so a view refers to it as `/Assets/Icons/<name>.png`, and nothing needs adding to the `.csproj`. The folder holds no code, so it isn't a namespace. Windows scales the app by the display's setting (125% on the development PC), so an image is nearly always resized, and enlarging one blurs it. Download it larger than it's shown (64 px for 16), and set its `Width`, `Height` and `RenderOptions.BitmapScalingMode="HighQuality"`, as the Products list (64 px shown at 16) and the Used By tiles (128 px shown at 32) do for the product images (`Bread_`, `Loaf_`, `Cake_`, `Pastry_`, `Roll_Cake_` and `Composite_64px.png` and `_128px.png`). Set `UseLayoutRounding="True"` on the `Image` too: the app doesn't round layout to whole pixels, and an image placed between them blurs.
- **The app's icon** is `tinapayan-rms-icon.png` (1024 px) in the same folder, and `tinapayan-rms-icon.ico` made from it. The `.ico` is the `.exe`'s icon (`ApplicationIcon` in the UI project), which Explorer and shortcuts show. WPF doesn't hand it on to the windows, so `AppWindow`'s constructor sets each window's `Icon` from the same `.ico` (`AppWindow.IconUri`), for the taskbar and Alt+Tab. The title bars don't draw it (`ShowIcon="False"`). To change the icon, replace the `.png` and make the `.ico` again with 16, 20, 24, 32, 40, 48, 64, 128 and 256 px sizes. Those cover 100% to 200% display scaling: at 125%, Windows takes the 20 and 40 px ones for the window.
- **Sizes**: every text box and button is 24 px tall, number boxes are right-aligned, and the body text is 14 px. `App` sets all of these.
- **Forms**: an input's label goes on its left in a stacked form (`FormLabelStyle`, lined up with `SharedSizeGroup`). A row of fields side by side keeps each label above its field (`FieldLabelStyle`). A value that can never be typed is a `TextBlock`, not a read-only text box.
- **Amounts** have no currency symbol, and use the decimals in [Precision and rounding](#precision-and-rounding).
- **Keep it plain.** Use the plainest standard control and the fewest columns. Fold a derived or helper value into a figure that's already shown rather than giving it a column of its own. A real cost component, though, gets its own column, and the line's total includes it.
- **Grids**: every `SfDataGrid` sets `GridLinesVisibility="Both"` and `CurrentCellBorderBrush="{StaticResource AccentBrush}"`. A grid with height to spare uses `controls:GridFillerRows.IsEnabled="True"`. A grid whose lines are typed into shows a row header. Quantity columns use `NumericColumn` with `NumberDecimalDigits="1" TypedDecimalDigits="4"`, which needs `controls:NumericEditing.IsEnabled="True"` on the grid.

### Working with Syncfusion

- **Prefer a Syncfusion control** to its native WPF equivalent (`ButtonAdv` over `Button`, `TabControlExt` over `TabControl`, `ComboBoxAdv`, `SfDataGrid`, `SfTextBoxExt`, `DoubleTextBox`), so the whole UI picks up the theme. Use a native control only when Syncfusion has none that fits.
- **To add a control**, add its package's `PackageReference` (with no version) to the UI project, and a `PackageVersion` at `$(SyncfusionVersion)` in `Directory.Packages.props` if it's a new package. The theme package already styles every control.
- **To learn how a control is meant to be used**, start with its `syncfusion-wpf-*` skill in `.claude/skills/`. Then ask Syncfusion's MCP server, `syncfusion-wpf`, which searches Syncfusion's current documentation (its one tool is `search_docs`). `.mcp.json` runs it with `dotnet dnx Syncfusion.WPF.MCP --yes`, which needs no install, and passes it the API key from the `SYNCFUSION_API_KEY` user environment variable. That key isn't the licence key: generate it in your syncfusion.com account. Without it, only that server fails to start. Then read the Sample Browser source in `C:\Users\Public\Documents\Syncfusion\WPF\34.2.2\SampleBrowser\` (a v34 install, older than the packages), and grep file contents there, because the demo file names often don't name the control. Only then decompile the assembly with `ilspycmd`: the packages are in `%UserProfile%\.nuget\packages\syncfusion.*\<version>\lib\`.
- **Known traps:**
  - Bind an editable `DoubleTextBox`'s `Value` with `UpdateSourceTrigger=PropertyChanged`. Otherwise the ViewModel only sees the value when the box loses focus, and the buttons and figures that depend on it lag behind.
  - A two-way `DoubleTextBox` or `PercentTextBox` bound to a `decimal` takes `Converter={StaticResource DecimalValueConverter}` on its binding. Without it, WPF's own conversion keeps the box's `double` at its exact binary value, so 28.35 typed reaches the ViewModel as 28.349999999999997868371792719 (see [Precision and rounding](#precision-and-rounding)).
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
   - it writes each figure itself (`CellsExportingEventHandler`, `Handled`), with its own column's decimals and a thousands separator (`#,##0.00`), since the converter's own format has none;
   - it fits the columns to their contents, since the converter copies the grid's on-screen widths.
   A grouped grid's groups go as their caption rows. Each cell is formatted by the grid's own column, as no table has a row's details: a details cell's column would be its `GridViewDefinition`'s grid's, where `ExcelExport` looked it up for the Production Plan's Mixing Sheet until 2026-11-04. `GridFillerRows`' padding is only drawn, never rows of the grid, so it can't reach the file. Figures are written as numbers at full precision; the format only shows them rounded. The workbook is built in memory before the file is touched, so a file that can't be written is left as it was.

`ExcelExportTests` checks all of this against a real grid read back with XlsIO.

The Price Simulator screen's **Print Report** goes through the same dialog, with each product's recipe and costing at the simulated prices beside today's:

- [PriceSimulatorReport](../src/TinapayanRMS.UI/ViewModels/PriceSimulatorReport.cs) pairs a product's `SavedProduct` priced now with the same product priced at simulated prices. Both are built from one `ProductDetail`, so their sections, lines, packaging and products pair up by index. It sets `NowCost` on each line, and `NowBatchCost` on each section, whose cost a change moves, and puts `PrintedSimulation` (today's `ProductCostSummary` and the changes reaching the product) on the printout. A change reaches a product when `IngredientCostRules.ReachedBy` (the material and every made or linked ingredient depending on it) meets `PriceSimulatorReport.MaterialIdsOf` (its sections', add-ins' and packaging's materials, and those of the products a composite holds).
- `PriceSimulatorViewModel` keeps the last recalculation's `SavedProduct`s, now and simulated, at the basis it ran at, and the changes it tried, so the report always matches the table. `PrintChoices` builds a `PrintChoice` for the rows picked in the grid (`SelectedProducts`, bound to the grid's `SelectedItems`, picked again by id after each recalculation) and one for every row shown. `ShownProducts` is the grid's `View.Records`, which the view supplies, so the report follows the column filters and sort.
- A `ProductPrintSet` with `PriceChanges` set is a Price Simulator report. With the costing included, `ProductPrintLayout` gives its first page the price changes and a contents with each product's base cost now and simulated. Each product gets a Price Changes Applied table, a Cost Now column on every recipe, packaging and products table (moved lines in the accent), and a costing with Now, Simulated and Change columns. Without the costing it prints the recipes as a plain set does.
- `PrintPreviewWindow(IReadOnlyList<PrintChoice>, Theme)` shows a Print dropdown when there's more than one choice, and Recipe and Costing tick boxes.

### The Price Simulator

`PriceSimulatorViewModel` prices every product now and with the table's changes (see [Formulas and where they live](#formulas-and-where-they-live) and [Printing](#printing)). Each `PriceSimulatorProductRow` also carries the product's current price and `PriceToKeepMarkup`.

The table can be saved as a **scenario** ([PriceScenario](../src/TinapayanRMS.Domain/Entities/PriceScenario.cs)), kept in the database:

- `SimulatedPriceChange.ToScenarioLine` gives a row's material and whichever of Change % and New Price was typed, and `Open(line)` types it back. So a percentage goes on the price as it is when the scenario is opened.
- `HasUnsavedChanges` compares the table with what was last opened or saved, by value. Picking a scenario over unsaved changes asks first (`ConfirmDelete`). Keeping them picks the open scenario again in the list: `OpenScenarioAsync` starts with `Task.Yield`, so the list has finished its own pick before it's undone.
- The scenarios are an `SfDataGrid` of two columns (Saved, Name; newest first, as `GetAllAsync` reads them) whose `SelectedItem` is `SelectedScenario`. Its setter only asks for a scenario to be opened, and ignores a null, which the grid can send when its rows are replaced. The ViewModel sets the field itself (`ShowSelectedScenario`), and always raises the change, so a pick the user took back is undone in the list too.
- Save asks for a name through `PromptForName` (a `TextPromptWindow`), and asks again before replacing another scenario of that name.

### The Production Plan

Retired on 2026-10-31: production planning moved to TinapayanBMS (`../TinapayanBMS`), which plans from a copy of this app's recipes, prices and costs taken through the Recipe API. Its `ProductionPlanRules`, `SavedPlanRules`, `ProductionPlanBuilder` and `ProductionPlanViewModel`, the saved plans (`saved_plans` and `saved_plan_lines`, dropped by the migration `RetireProductionPlan`, empty at the time), Reports' Production Results (`ProductionResultsAnalysis`), the plan's printout (`PrintedPlan`) and `POST /plan` all went, with their tests, which TinapayanBMS keeps as its own. `RecipeScalingRules` stays: the Scaling tab uses it. What only the plan had used went on 2026-11-04, from a code review: `RecipeScalingRules.HalfBatches`, `BatchesToMake` and `UnitsToBuy`, `GridAutoRowHeight`, and `PrintPreviewWindow`'s one-printout constructor. So did two parts of controls still in use: `GridAddNewRow.ItemDropped`, and Excel export's handling of a row's details (`ExcelExport`'s `GridViewDefinition` cells). PLAN.md's Production Plan entries and the build plan's log keep the history.

### Logging and errors

- Serilog's static `Log.Logger` is built in `App`'s static constructor, so even a licence failure is logged. It writes one rolling file a day under `logs/`, keeping 31. It's registered in DI as a `Serilog.ILogger`.
- `RunSafelyAsync`, `LoadSafelyAsync` and `LoadLatest` log a refusal as a warning, and any other failure as an error with its stack.
- `App` handles an exception that nothing else caught on the UI thread (`DispatcherUnhandledException`): it logs it, shows a message saying the app is still running, and carries on. If more than three arrive within ten seconds, [UnhandledErrorGuard](../src/TinapayanRMS.UI/UnhandledErrorGuard.cs) lets the app close, since they'd come faster than anyone could act on them. A failure ReactiveUI was left holding arrives wrapped in its `UnhandledErrorException`, and `App` shows the cause inside. It logs exceptions from other threads (`AppDomain.UnhandledException`) and unobserved task exceptions. A task nothing awaits has to deal with its own failure, since an unobserved one only reaches a warning in the log, when the task is collected, and the click that started it just does nothing. A screen's goes through `RunSafelyAsync` (`_ = RunSafelyAsync(...)`). `ScreenNavigator`'s moves never fault: they hand it to `showFailure`, and `MainWindow` puts it back on the dispatcher for `App`'s handler. `Dispatcher.InvokeAsync` keeps what its delegate throws in the task it returns, so it counts too; `BeginInvoke` doesn't. None of that is for a start that fails: until the main window shows, [AppStartup](../src/TinapayanRMS.UI/AppStartup.cs) catches it, logs it as fatal, shows its first line and where the log is, and closes the app.
- Once the main window shows, `App` checks off the UI thread for migrations the database doesn't have yet, and says so in a message.
- In Debug builds, [BindingErrorLog](../src/TinapayanRMS.UI/BindingErrorLog.cs) writes WPF's binding errors to the log as `WPF binding …` warnings, even without a debugger attached. Some are expected at every startup and harmless: one `Cannot find governing FrameworkElement` for each `GridMultiColumnDropDownList.ItemsSource` or `GridCheckBoxColumn.AllowEditing` binding while its grid is built, and a `FindAncestor` warning from Syncfusion's `TabItemExt` template.

---

## 10. Testing

### How the tests work

- **Nothing is mocked.** Rules are tested as pure functions. Repositories, services and ViewModels are tested on a real PostgreSQL database that Respawn resets before every test. Grid behaviours are tested in real `SfDataGrid`s in windows placed off-screen.
- **Each database-backed project has its own database**: `tinapayanrms_test_infrastructure`, `tinapayanrms_test_ui`, `tinapayanrms_test_pricing`, `tinapayanrms_test_mcp` and `tinapayanrms_test_recipeapi`. `dotnet test --solution` runs the projects side by side, and a shared database would be reset under the other project's running test. `UI.Tests` and `Infrastructure.Tests` have four database collections each, `DatabaseCollection.A` to `D`, and each collection has a database of its own (the project's, then `_2` to `_4`), so their database tests run four at a time (2026-11-05). With one collection, those tests ran one after another and took about 7 of the suite's 7½ minutes. It now takes 4 to 6 on this PC, depending on what else is running: with everything at once its processor is the limit (85% on average, paging hard with 8 GB).
- **Each database is migrated once per test run**, by the fixture's `MigrateAsync`.
- **Respawn clears every table** except `__EFMigrationsHistory` before each test (it truncates them, and tries again if PostgreSQL ends the truncate as a deadlock with a read the last test left running). That includes rows the migrations seeded, so a test creates every row it needs.

| Project | What it tests | Needs |
|---|---|---|
| `ApplicationCore.Tests` | `Rules/` and `Validators/`, as pure functions | Nothing |
| `Infrastructure.Tests` | `Persistence/` (repositories and the cascade), `Queries/` (`ColumnsTests`), `Upgrades/` (`DataUpgradesTests`), `Services/` (`JsonViewStateStoreTests`, and `DatabaseBackupsTests`, which run the real `pg_dump` and `pg_restore`) | PostgreSQL, with its command-line tools |
| `Pricing.Tests` | `SavedProductReaderBulkTests` and `SavedProductSectionNameTests` on real repositories; `ProductCostSummaryTests` and `TriedPriceChangeTests` without a database | PostgreSQL for the first two |
| `UI.Tests` | `ViewModels/` on real repositories, `Controls/` in off-screen grids, `Printing/`, `Converters/`, `CompositionRootTests`, `AppStartupTests`, `SingleInstanceGuardTests` | PostgreSQL; the licence key for the tests that build Syncfusion controls |
| `Mcp.Tests` | `Tools/` (`CostingToolsTests`): the tools read through the server's read-only connection, priced as the screens price, and the connection refusing a write | PostgreSQL |
| `RecipeApi.Tests` | `Endpoints/` (`RecipeApiTests`): each endpoint served in memory on the API's read-only connection, the key when one is set, and the connection refusing a write | PostgreSQL |
| `Tests.Common` | Nothing: it's the shared fixture library | — |

### Running fewer tests while developing

The whole suite takes 4 to 6 minutes on this PC. While you work, two scripts run less of it (2026-11-05). Before a commit or a merge, run it all: both pick by project or by name, so they can miss a test of something a change reaches some other way.

- **[scripts/Test-Changed.ps1](../scripts/Test-Changed.ps1) runs the test projects your changes can reach.** It takes the files changed since `-Since` (`HEAD` by default, so what isn't committed yet, new files included; `-Since main` for the whole branch) and runs every test project that references, directly or through others, a project one of them is in. It reads the references from the `.csproj` files, so it needs no list of its own. A change to the UI runs `UI.Tests` alone (about 3 minutes, where all of them take 4 to 6); one to `Pricing` runs Pricing, UI, Mcp and RecipeApi's tests; one to `Domain`, `ApplicationCore`, a build setting (`Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, the solution) or a file outside the projects runs them all. A file another project links in counts for it too: the UI's `appsettings.json` reaches every database test project, through `Tests.Common`. Docs, `scripts/` and `.claude/` run nothing. Several projects run side by side from one `dotnet test`, through a solution filter it writes to `%TEMP%`. `-List` says what it would run and why, and runs nothing; `-Class "*ProductsViewModel*"` runs only matching classes, a project with none passing with "Zero tests ran".
- **[scripts/Watch-Tests.ps1](../scripts/Watch-Tests.ps1) runs one class again each time you save**, by Microsoft.Testing.Platform's hot reload (`Microsoft.Testing.Extensions.HotReload`, in every test project; it does nothing unless `TESTINGPLATFORM_HOTRELOAD_ENABLED` is `1`, which only this script sets). It starts the project under `dotnet watch`: a change to a method's body, in a test or the code it tests, is applied to the running tests, which run again; anything hot reload can't apply (a new method, type or parameter, a XAML file) makes `dotnet watch` rebuild and start them again (`DOTNET_WATCH_RESTART_ON_RUDE_EDIT`). Measured on `SubAssemblyRolesViewModelTests` (8 tests): 94 seconds to start, most of it the build, then a broken assertion failed 17 seconds after the save and passed 7 seconds after it was put back. Give it `-Class` or `-Method`, not both; with neither, the whole project runs after each save. Stop it with Ctrl+C.
- **Don't run a project two ways at once.** A watcher holds its project's build, so a `dotnet build` of it fails on locked files, and it uses the project's test databases, so a second run would reset them under its tests. `Test-Changed.ps1` and `Watch-Tests.ps1` refuse to start while that project's tests are running already.
- **Both hand `dotnet` the repo's path as your terminal spells it.** The build records each project's folder with its drive letter's case, so `C:\…` after `c:\…` would recompile every project (see "Build configuration").
### The database fixtures

- **[DatabaseFixtureBase](../tests/TinapayanRMS.Tests.Common/Fixtures/DatabaseFixtureBase.cs)** (in Tests.Common) reads the app's connection string, swaps in the project's database name, migrates the database, and resets it with Respawn.
- **`DatabaseFixture`** and **`DatabaseCollection`** are defined in each project's own `Fixtures/` folder, because xUnit only finds a collection definition in the tests' own assembly. Each `DatabaseFixture` is one line that names its database. xUnit makes a collection fixture once for each collection, and `DatabaseFixtureBase` gives the first one of a run the name as it is and the next ones the name with `_2`, `_3` and so on, so each collection has a database of its own.
- **[PersistenceTestBase](../tests/TinapayanRMS.Tests.Common/Fixtures/PersistenceTestBase.cs)** resets the database before each test. It provides `CreateDbContext()`, `CreateDbContextFactory()` (the app's own `SharedTransactionDbContextFactory`, optionally on a `TestClock`), `CreateUnitOfWork()`, the test's `Cancellation` token, and seeding helpers such as `AddIngredientAsync`, `AddRoleAsync`, `AddProductAsync`, `AddProductWithSubAssemblyAsync`, `SetPriceAsync` and `SeedUnitOfMeasureAsync`. With nothing saved in Settings, every product is priced with the 3% default wastage; a test whose figures are about something else calls `WithoutWastageAsync()` (or `SetWastageAsync`) before the screen or reader reads it.
- **[ThrowawayDatabase](../tests/TinapayanRMS.Tests.Common/Fixtures/ThrowawayDatabase.cs)** is a migrated database of one test's own on the same server (`tinapayanrms_test_backup_` and eight characters of a GUID), dropped when the test is done. The backup tests restore over it, which would put every other test out of the shared database.
- **Every concrete test class must carry `[Collection(DatabaseCollection.Name)]`**, or in `UI.Tests` and `Infrastructure.Tests` one of `DatabaseCollection.A` to `D`. It's deliberately left off the base class: xUnit isn't guaranteed to honour it there, and without it tests could run in parallel against one database. The four are balanced by how long their classes take, so put a new class in the one with least in it (`Infrastructure.Tests`' A is `DatabaseBackupsTests`' alone). A class's own tests always run one at a time.

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

[Collection(DatabaseCollection.B)]
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
[Collection(DatabaseCollection.D)]
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

A theory's `decimal` values with decimals go in `TheoryData`, written as `decimal` literals (`0.0525m`), with `[MemberData]`, never in `[InlineData]`. An attribute can't hold a `decimal`, so `[InlineData(0.0525)]` passes a `double`, which since .NET 11 arrives as 0.0524999999999999980571097069. Whole numbers are safe in `[InlineData]`. See `StandardUnitRulesTests`:

```csharp
public static TheoryData<string, string, decimal> MassUnits => new()
{
    { "pound", "lb", 453.5924m },
    { "ounce", "oz", 28.3495m },
};

[Theory]
[MemberData(nameof(MassUnits))]
public void A_mass_unit_of_an_ingredient_is_fixed_in_grams(string name, string abbreviation, decimal grams)
```

A test that creates WPF objects runs on an STA thread:

- `StaThread.Run(() => …)` for plain WPF objects.
- `StaThread.RunLicensed(() => …)` when it builds Syncfusion controls. It's skipped if `SYNCFUSION_LICENSE_KEY` isn't set, because an unlicensed control shows a dialog.
- `StaThread.RunAsync(async () => …)` for a ViewModel test whose screen works in the background and writes its state when the work comes back: a read nothing awaits (`_ = FetchComponentCostsAsync(…)`, a composite's Products table in `ProductsViewModelCompositeTests`, the Scaling tab in `ProductsViewModelScalingTests`), or a recalculation that checks it's still the latest and then writes (`PriceSimulatorViewModelTests`). It runs the test on a thread with a running dispatcher, so every await comes back to it, as on the app's UI thread. Without it the await came back on the thread pool and changed the screen while the test did: about one run in three lost a line's cost or threw "An item with the same key has already been added", and a stale recalculation could write over a newer one.
- `StaThread.RunThemed(() => …)` when it themes a window. All such tests share one STA thread, with the app's theme settings, because SfSkinManager ties a theme's resources to the thread that loaded them.
- `VersionRecorders.Create(factory)` (in Tests.Common's `Fixtures/`) builds a `ProductVersionRecorder` over real repositories, and `CreateSaves` the `VersionedSaves` over one, for a test that constructs `SubAssembliesViewModel` or tests the saves themselves (`Pricing.Tests/Versions`).
- `FailingDbContextFactory` (in UI.Tests' `Fixtures/`) wraps a factory and refuses every context while its `Fail` is set: for a test that a screen survives a database that stops answering, and recovers once it answers. A test that a write is all or nothing fails it part-way with an EF Core save interceptor instead (`CostCascadeTransactionPersistenceTests`).
- `SqlCommandCounter` (also in UI.Tests' `Fixtures/`) counts the SQL commands sent from where it's started (the activities Npgsql reports on its `Npgsql` ActivitySource): for a test that a read's round trips don't grow with what it reads. Work started in the background from there counts too, so after a ViewModel command read `await counter.SettledAsync()` (no new command for 300 ms), not `Commands`: a load's background reads, such as the Products list's below-markup flags, are often still going when the command returns.
- `OffscreenGrid.Run(create, test)` shows a real grid off-screen. Every test class that builds a real `SfDataGrid` or `SfTreeGrid`, themed ones included, carries `[Collection(OffscreenGridCollection.Name)]`, so they run one at a time: their windows competed for focus, and Syncfusion keeps whether the current cell has been validated in one static flag for the whole process, so a grid beside an edit could make its commit skip writing the value.

### Naming

Test classes are named `<Subject>Tests`, such as `CostingRulesTests` or `ProductSavePersistenceTests`. Test methods are sentences with underscores: either `Method_scenario_result` (`EnsureValidParameters_rejects_negative_labor_cost`) or a plain statement (`Declining_the_question_saves_nothing`). CA1707 is turned off in `tests/` for this.

### Coverage

Coverage comes from Microsoft's MTP coverage extension (see the [command reference](#command-reference)); `coverlet` doesn't work under Microsoft.Testing.Platform. When you read the raw report, ignore `Infrastructure/Migrations`: its generated code never runs under the reset-a-migrated-database model, and it drags Infrastructure's raw figure down.

---

## 11. Conventions

### Code

- **Use modern C# 15 and .NET 11 idioms**: primary constructors (every repository has one), collection expressions, `record` and `record struct` for immutable shapes (drafts, snapshots, printouts), `required` members, file-scoped namespaces and pattern matching.
  - **A fixed set of cases is a `closed` class or record** (C# 15): the list trees' nodes, `RecipeTab`, `PlanIssue`, `PrintScope`, `RecipeTabChoice`, `PackagingLinkDraft`, `PackagingLinkTarget`, `SectionPlacement`. Each case is a sealed record nested in it, with only its own data, in place of a `Kind` enum, nullable fields that only some cases use, or flags that overlap. A switch over one names every case, and `null` when null can come, never `_`, so a new case is a CS8509 warning wherever it's missed.
  - **No `union` for anything XAML binds.** A union is a struct holding its case in `Value`, which bindings don't see through. A union suits a value that's one of some types already there: `PreparedPrintout(ProductPrintout, PrintProblem)`, what `PreparePrintout` gives the Print tab, empty (`default`, matched by `case null`) while there's nothing to print. In a `?:` whose other branch is a case, write `default(PreparedPrintout)`, since a bare `default` takes the case's type. Tests read the case through `Value`.
  - **A closed record that's shown seals its `ToString`** (`PrintScope`), or each case reads "Folder { Label = … }".
  - **`BeEquivalentTo` over a closed base type** needs `o => o.ComparingByValue<TBase>()`, since the base has no members to compare.
  - **`[with(comparer), ..]`** for a collection made with a comparer or capacity, and **labeled `break`/`continue`** in place of a flag or `goto` out of nested loops (IDE0410).
  - **.NET 11's library additions in place of hand-written code**: `Enumerable.FullJoin` for a full outer join by a key that comes once on each side (`ProductVersionRules.Paired`), `Process.RunAndCaptureTextAsync` to run a tool and read what it writes (`PostgresTools.RunAsync`), and `Process.StartAndForget` to start a process the app doesn't watch (the restart after a restore).
- **Nullable reference types are on everywhere.** Treat their warnings as real.
- **Everything that touches the database is `async`** and takes a `CancellationToken`. ViewModels pass `CancellationToken.None`.
- **A rule throws `BusinessRuleException` with a message written for the shop's staff**, because `RunSafelyAsync` shows it exactly as written. That goes for a repository's checks and a screen's own form checks too. A plain `InvalidOperationException` reads as a bug: EF Core and Npgsql throw those for failures.
- **Strings**: compare names with `StringComparison.OrdinalIgnoreCase`; sort for display with `StringComparer.CurrentCultureIgnoreCase`; format display dates with `CultureInfo.CurrentCulture`, and log output with `CultureInfo.InvariantCulture`.
- **Correctness and performance come before brevity.** Query only the rows and columns you need, rather than loading more and filtering in memory. Avoid needless intermediate collections in hot paths such as the cascade.
- **Comments explain why.** Types and non-obvious members have XML doc comments that give their domain meaning. Inline comments record a decision and its reason, often with its date. Match the comment density of the code around yours.
- **Seal every class that isn't a base class** (`AppWindow` is the one base class in `src`), so the JIT can devirtualize calls and type checks are cheaper. A fixed lookup table is a `FrozenDictionary`, and a lookup by a trimmed name uses its span-keyed `GetAlternateLookup<ReadOnlySpan<char>>()` rather than allocating the trimmed string (`StandardUnitRules`).
- **Keep the build free of warnings.** To look for more performance problems than the default rule set finds, rebuild with `-p:AnalysisLevelPerformance=latest-all`. On 2026-10-29 that left only CA1849 on FluentValidation's `Validate` (an in-memory check; `ValidateAsync` only wraps it) and on tests calling the synchronous methods deliberately.

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
5. Add any rule it needs to the right `*Rules` class (and its tests), and run it from the validator (`Satisfies`) if a form edits it.
6. Generate, read and apply the migration, and regenerate the compiled model (see [Migrations](#migrations)).
7. In the ViewModel, add the editing property, its load and save mapping, and its place in the form snapshot (so undo and the dirty check include it). Bind it in the view.
8. Add tests: rules, persistence, and the ViewModel.
9. Update the user manual if users can see the field, and PLAN.md's Domain Model section.

### Add an entity and its table

Everything in the previous recipe, plus:

1. A `DbSet` in `TinapayanRMSDbContext`, named as the table should be.
2. An `IEntityTypeConfiguration<T>` in `Configurations/`, with the unique indexes, delete behaviours and check constraints the rules need.
3. A repository interface in `ApplicationCore/Interfaces`, and its implementation in `Infrastructure/Repositories`, taking `IDbContextFactory<TinapayanRMSDbContext>` through its primary constructor. Reads use Dapper and `Columns`; writes use EF Core, in a transaction begun with `BeginOrJoinTransactionAsync` when they need one.
4. Registrations: the repository as a singleton in `DatabaseHost`, and `IValidator<T>` in `CompositionRoot` if the entity has a Save form.

Respawn picks up the new table by itself.

### Add a business rule

1. Add a static method to the right `*Rules` class in ApplicationCore. Keep it pure, and make it throw `BusinessRuleException` with a message a user can act on.
2. Call it from the repository's write path, before anything is deleted or written.
3. If a form can break the rule, run it from the entity's validator for an immediate message: `RuleFor(x => x.Field).Satisfies(SomeRules.EnsureSomething)`. Don't write the check again in the validator: two copies drift apart, as the costing percentages' and the packaging role's messages had (fixed 2026-11-05).
4. If the database can enforce it, add a check constraint or unique index as a backstop, which needs a migration.
5. Test it in `ApplicationCore.Tests/Rules`, and test the write path that calls it in `Infrastructure.Tests/Persistence`.

### Change a cost formula

1. Read PLAN.md's [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade), and the Domain Model bullets for the entities involved.
2. Change or add the formula in its Rules class, with unit tests.
3. If the figure is stored, make sure every write path that can change one of its inputs recomputes it, and that the propagator does too. If it's computed on read, the Rules change is enough: `SavedProductReader` and the screens share it.
4. Extend `ReactiveCostCascadeTests` with the end-to-end case.
5. Update the user manual's [How costs are worked out](user-manual.md#12-how-costs-are-worked-out), including its worked example, in the same change.
6. Record the decision in PLAN.md.

### Add a screen

1. Write the ViewModel in `ViewModels/`, deriving from `ViewModelBase`. Make it `sealed`, and `IDisposable` if it owns a `SourceCache`. Give it a fetch-then-merge `LoadCommand`. Register it as a singleton in `CompositionRoot`.
2. Write the view in `Views/`: a `UserControl` whose constructor takes the ViewModel, sets `DataContext` and calls `RegisterNativeMessageBoxHandler()`. Give it an `ErrorToast`.
3. In `MainWindow.xaml`, add a `TabItemExt` to `MainTabControl`, and a `ListBoxItem` to the navigation menu with an `AutomationProperties.Name`.
4. In `MainWindow.xaml.cs`, add a factory for the tab's view to `_screenFactories`, its `LoadCommand` to `_loadCommands`, and add its `HistoryScreen` to `_historyScreens`, with a new `AppScreen` value.
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
2. Learn how it's meant to be set up from its skill, then the `syncfusion-wpf` MCP server, then the Sample Browser, then `ilspycmd`.
3. Check that it doesn't change how other controls are themed (remember the `SfTreeGrid` trap).
4. Check it in the running app at the app's text size.

### Add a grid behaviour

1. Write a static class in `Controls/` with an attached property, such as `IsEnabled` or `Recorder`. In the property's change callback, hook the grid's events, and unhook them when the grid unloads.
2. Test it in a real grid with `OffscreenGrid`, in `UI.Tests/Controls`.

---

## 13. Diagnostics and troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| The app closes at startup, and the log says there's no Syncfusion licence key | It was built while `SYNCFUSION_LICENSE_KEY` wasn't visible to the build | Set the user environment variable, restart your terminal or IDE, then rebuild |
| `dotnet publish` fails asking for `SYNCFUSION_LICENSE_KEY` | The variable isn't visible to the build, and an app published without it couldn't start | The same |
| A screen says "Couldn't reach the database.", and the log has "Failed to connect" or "password authentication failed" | PostgreSQL isn't running, or the password file has no line for the server and role | Start the `postgresql-x64-18` service, and check `appsettings.json` and `%APPDATA%postgresqlpgpass.conf` |
| A message at startup says the database is out of date | A migration hasn't been applied to it | `dotnet ef database update --project src/TinapayanRMS.Infrastructure` |
| A message says something went wrong and the app is still running | An exception nothing else caught | Read the log for its stack |
| Timings vary widely from run to run | Other programs on the PC compete for memory and disk | Compare timings in one process, alternating before and after |
| `dotnet ef` can't connect | The design-time factory's connection string is hard-coded | Check the password file, or edit `TinapayanRMSDbContextFactory.DevelopmentConnectionString` |
| Settings → Database says backups need `pg_dump` and `pg_restore` | PostgreSQL's command-line tools aren't in `C:\Program Files\PostgreSQL\<version>\bin` or on the `PATH` | Install them, or set `Backups:ToolsFolder` in `appsettings.json` |
| The build fails with `CS0103: The name 'InitializeComponent' does not exist`, or `CS2001` for `*.g.cs` files, all against a `*_wpftmp.csproj` | The WPF markup pass has gone stale | Run `dotnet clean src/TinapayanRMS.UI`, build the UI project on its own, then build the solution |
| The build can't copy `TinapayanRMS.UI.exe` or a DLL | The app is running | Close it |
| An AI assistant's TinapayanRMS tools give figures from before your change | The MCP server is still running the build it started with | Restart it (`/mcp` in Claude Code). It builds again as it starts |
| `--filter` isn't recognised, or zero tests ran | VSTest flags under Microsoft.Testing.Platform | Use `--filter-class`, `--filter-method` and `--coverage` |
| Some UI tests are reported as skipped | No licence key | Set `SYNCFUSION_LICENSE_KEY` |
| A binding silently does nothing | A binding error | Check the Debug build's log for `WPF binding` warnings |
| Every `SfDataGrid` suddenly looks unthemed | An `SfTreeGrid` is themed through its window | Theme it locally |
| The UI freezes | Something blocks the UI thread | `dotnet-stack report -p <pid>`. Find the PID with `dotnet-trace ps`. |
| A crash, deadlock or memory growth | | `dotnet-dump collect -p <pid>`, then `dotnet-dump analyze <file>` |
| Something is slow | | `dotnet-trace collect -p <pid>` |
| A test harness stalls for seconds while building a view with a chart | Syncfusion's telemetry blocks when there's no `SynchronizationContext` | Build views inside a running dispatcher (`Dispatcher.CurrentDispatcher.InvokeAsync` after `Dispatcher.Run()`), or call `Syncfusion.Telemetry.Telemetry.Disable()` first |

When you compare the behaviour of two versions of the code, clean the UI project before each build and check that the output says "Build succeeded": a stale markup pass can leave the previous binaries in place. If it matters, confirm what was built with `ilspycmd -t <Type> <dll>`.

### Driving the UI from a script

To drive the running app (launch it on the secondary monitor, click, type, capture the window or a popup), use [.claude/skills/run-tinapayanrms/driver.ps1](../.claude/skills/run-tinapayanrms/driver.ps1); its SKILL.md lists the commands and what trips them up. Off-screen harnesses and UI-automation scripts have pitfalls that PLAN.md records in detail (Application Architecture and Development Environment, and [docs/plan/syncfusion.md](plan/syncfusion.md)). In short:

- Make the script's thread per-monitor DPI-aware before it places, clicks or captures anything, because the app is per-monitor DPI-aware ([app.manifest](../src/TinapayanRMS.UI/app.manifest)).
- Find the main window by its class and title. `Process.MainWindowHandle` can pick one of the app's hidden windows.
- Syncfusion's buttons, pickers and number boxes need real clicks: `ButtonAdv` has no Invoke pattern, and `ValuePattern.SetValue` doesn't commit a `DoubleTextBox`.
- Bring the app to the foreground before you send keystrokes, and check it's still there, or the keys go to whatever window is in front.
- UI Automation's `FocusedElement` reports the window, not the grid, while an `SfDataGrid` has the keyboard. Judge a grid's focus from the screen instead, for example by whether the current cell's border moves with the keys.
- `new App()` posts WPF's startup to the dispatcher, so a harness that runs the dispatcher also runs `OnStartup`, which shows the real main window. Collect what it posts and abort it first.
- A file-based harness also needs `#:property PublishAot=false` (WPF can't be trimmed), a `DispatcherSynchronizationContext` on its thread (or a ViewModel's `await` comes back on the thread pool and the screen stays empty), and the UI assembly as `Application.ResourceAssembly`, where a view's `/Assets/...` images are.

### Checking costs from an AI assistant

A product's cost and selling price aren't stored, so SQL can't show them: they're worked out when they're read (see [Stored and computed costs](#stored-and-computed-costs)). [src/TinapayanRMS.Mcp](../src/TinapayanRMS.Mcp) is a [Model Context Protocol](https://modelcontextprotocol.io) server, built on the official C# SDK (the `ModelContextProtocol` package), that gives an AI coding assistant such as Claude Code these figures. It works them out with the app's own code and reads the dev database `tinapayanrms`. Its tools are in [CostingTools](../src/TinapayanRMS.Mcp/Tools/CostingTools.cs):

| Tool | What it returns | Worked out by |
|---|---|---|
| `list_products` | Every product, or those whose name has some text in it, each with its base cost, suggested and rounded selling price, current price and markup | `SavedProductReader.ReadAllAsync`, as Reports prices them |
| `get_product_costing` | One product as the Products screen shows it once opened: each section's batch, lines, add-ins and cost per piece, a composite's products, its packaging, and the Costing tab down to the selling price | `SavedProductReader.ReadAsync` |
| `simulate_price_changes` | What-if: new prices or percentage changes for purchased materials, the made and linked ingredients each reaches, and every product they reach with its cost, suggested price and markup now and with the changes | The Price Simulator's code: `TriedPriceChange`, `IngredientCostRules.Reprice`, `SavedProductReader.PricedAt` and `Build`, and `PriceSimulatorProductRow` |

Each tool takes a product or material by its name, or by a part of it that names only one. A name that matches nothing, or several, comes back as an error that lists the candidates. Each tool also takes `unbuffered`, which prices at the materials' true costs, as the Products screen's Unbuffered switch does. The JSON it returns has every decimal rounded to 6 places (`ToolJson`), though the figures are worked out at full precision.

- **It can't write.** [ReadOnlyConnection](../src/TinapayanRMS.Infrastructure/ReadOnlyConnection.cs), which the Recipe API shares, adds `Options=-c default_transaction_read_only=on` to the connection string from `appsettings.json`, so PostgreSQL refuses any INSERT, UPDATE or DELETE, whatever code runs it. `CostingToolsTests` reads through that connection, and checks that a write is refused.
- **Its figures are the screens'.** The project builds its host with `DatabaseHost` and prices with `TinapayanRMS.Pricing`, where `SavedProductReader` and the simulator's arithmetic live, with no UI in it (2026-11-04). A change to how a product is priced reaches the tools without any change here. A change to the signature of one of those types can break this project's build.
- **Claude Code starts it** from [.mcp.json](../.mcp.json), which runs [scripts/Start-McpServer.ps1](../scripts/Start-McpServer.ps1) in Windows PowerShell. Claude Code asks you to approve a project's server the first time. The script builds the Release configuration, copies what the build put in `src/TinapayanRMS.Mcp/bin/Release/net11.0` to a folder of its own under `bin/Release/runs`, and runs that copy on the script's standard input and output (2026-11-04). A running program holds its files, so while the server ran from `bin/Release/net11.0` itself, through `dotnet run`, another session's start that had to rebuild them failed after about 13 seconds of retries (MSB3026). A server run from a copy holds only the copy. Each copy is named for a hash of its files' paths, sizes and times, so a start with nothing new runs the copy already there, and a copy no server runs from any more goes at the next start (while one runs from it, Windows won't open its `.exe` for writing). One start at a time builds and copies (a named mutex), so two sessions starting together don't build over each other. The server is in a job object that ends it when the script ends, as killing `dotnet run` ended the server it ran. The build's output goes to standard error, which Claude Code keeps in its MCP log, and a build that fails stops the start rather than run an older copy. How soon it answers depends on what has to build, and on whether the compiler is running yet: about 2 seconds when nothing has to, about 5 after a change to the server's own code, about 9 when all five projects it runs on recompile with the compiler running, and about 27 when they recompile with nothing running yet (measured 2026-11-04). Claude Code waits 2 minutes for it, as [.claude/settings.json](../.claude/settings.json) sets its `MCP_TIMEOUT` environment variable to 120000 milliseconds. Before 2026-11-04 it missed the default 30 seconds at every session start: each commit recompiled every project (see "Build configuration"), and the other servers start beside it. PowerShell gives the script's own path a capital drive letter, so it builds from `C:\…` whichever folder Claude Code starts it in, and a Release build from `c:\…` would make its next start recompile every project (also in "Build configuration"). Release keeps its files apart from the Debug ones that `dotnet build` and `dotnet test` write. It's rebuilt each time it starts, so restart it in Claude Code (`/mcp`) to pick up a change. To run it by hand, run the script too: `dotnet run` runs it from `bin/Release/net11.0`, where it would stop a session's start that has to rebuild.
- **Nothing but the protocol may reach standard output.** The host has no console logging. Anything a tool printed with `Console.WriteLine` would break the connection.
- **To add a tool**, add a method marked `[McpServerTool]` (with `ReadOnly = true`) to `CostingTools`, or to a new `[McpServerToolType]` class in `Tools/`, which `WithToolsFromAssembly` finds. Give the method and each parameter a `[Description]`: that's all the assistant reads about it. Put its result records in `Results/`. Throw `McpException` for a refusal the assistant should read; the SDK replaces any other exception's message with a generic one. Test it in `Mcp.Tests/Tools/` on a read-only connection, as `CostingToolsTests.CreateTools` does.

---

## 14. Further reading

### PLAN.md, by section

| Section | Read it when |
|---|---|
| [Tech Stack](../PLAN.md#tech-stack) | You touch a package, the ReactiveUI bootstrap, or the test runner |
| [Solution Structure](../PLAN.md#solution-structure) | You add a project or a folder |
| [Domain Model](../PLAN.md#domain-model) | You change an entity or a rule. Each entity's bullets give its rules and the reasons for them. |
| [Syncfusion notes](plan/syncfusion.md) | You use a Syncfusion control, or change the theme, the DPI handling or printing |
| [Application Architecture](../PLAN.md#application-architecture) | You change a screen, a repository pattern or a migration |
| [Products](plan/products-screen.md), [Raw Materials](plan/raw-materials-screen.md), [Sub-Assemblies](plan/sub-assemblies-screen.md) | You change one of the three main screens: each one's design notes |
| [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) | You change any cost |
| [Development Environment](../PLAN.md#development-environment) | You set up a machine, or change the tests' infrastructure |
| [Build plan](plan/build-plan.md) | You want the history: every phase and dated change, and the open items |
| [Conventions](../PLAN.md#conventions) | Always |

### Library documentation

- ReactiveUI: <https://www.reactiveui.net/docs/>
- DynamicData: <https://github.com/reactivemarbles/DynamicData>
- EF Core: <https://learn.microsoft.com/ef/core/>
- Npgsql and its EF Core provider: <https://www.npgsql.org/efcore/>
- PostgreSQL: <https://www.postgresql.org/docs/18/> (`pg_dump` and `pg_restore` under "Client Applications")
- Dapper: <https://github.com/DapperLib/Dapper>
- FluentValidation: <https://docs.fluentvalidation.net/>
- Syncfusion WPF: <https://help.syncfusion.com/wpf/welcome-to-syncfusion-essential-wpf>
- xUnit: <https://xunit.net/>
- Respawn: <https://github.com/jbogard/Respawn>
- Serilog: <https://serilog.net/>
- Catppuccin palette: <https://catppuccin.com/palette>
