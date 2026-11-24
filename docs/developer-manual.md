# TinapayanRMS Developer Manual

TinapayanRMS is the recipe and costing app of Tinapayan Festival Bakeshoppe. It's a Windows desktop app on .NET 11: a .NET MAUI Blazor Hybrid window, whose screens are Blazor components drawn in a web view, with Syncfusion Blazor components over state classes of the app's own. It keeps its data in PostgreSQL through EF Core and Dapper. The shop records what its raw materials cost. The app works out what every recipe, product and selling price comes to, and when a price changes it carries the change up to every product that uses it.

This manual is for developers who build, maintain or extend the app. It explains how the code is organised, which patterns it follows and why, and what you need to know and install before you change it. It assumes you know C#, but not this codebase.

> **PLAN.md is the source of truth.** This manual describes the code as it is and points into [PLAN.md](../PLAN.md) for the reasons and history behind each decision. Where the two disagree, the code and PLAN.md win: fix this manual.

| Document | Written for | What it covers |
|---|---|---|
| [docs/user-manual.md](user-manual.md) | The shop's staff | Every screen, and how costs are worked out, as a user sees them |
| docs/developer-manual.md (this file) | Developers | How the code is built, and how to change it safely |
| [PLAN.md](../PLAN.md) and [docs/plan/](plan/) | Developers and AI agents | Every design decision with its reason and date: tech stack, domain model, architecture, costing cascade and conventions in PLAN.md; the app's design notes, each main screen's and the build plan in `docs/plan/` |
| [CLAUDE.md](../CLAUDE.md) | AI coding agents | The commands and rules an agent needs before it changes the code; [tests/CLAUDE.md](../tests/CLAUDE.md) has the rules for writing tests, loaded only when an agent works under `tests/` |

PLAN.md is long: an overview ("At a glance"), then about 650 lines of long bullets, describing the app as it is now. List its sections with `grep -n "^#" PLAN.md` and read the ones your change touches. Its largest parts are in `docs/plan/` and are still part of it; the build plan's dated log there holds the history.

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
- **Reports and printing.** The Product Cost Summary and Raw Materials Price Summary reports, and printed recipes and costings.

It's a single-user desktop app. There's no server, no login and no network service of its own.

Its screens were ported on 2026-11-07 from the shop's web copy of it (`../TinapayanRMS_Web`, without that copy's shop accounts or its Mac target); [docs/plan/maui-app.md](plan/maui-app.md) says where they came from. The database is a local PostgreSQL server, reached as the `tinapayanrms` role with the password in the password file.

### Screens

Each screen is a page with an address, its state behind it, and, for the large ones, a folder of parts.

| Screen | Address | Page (in `Components/Pages/`) | State |
|---|---|---|---|
| Raw Materials | `/raw-materials`, `/raw-materials/{id}`, `/raw-materials/new/{number}` | `RawMaterials/RawMaterials.razor`, each tab's form `RawMaterialFormView.razor`, with its parts beside it | `RawMaterialsState` (the view), a `RawMaterialForm` per tab |
| Sub-Assemblies | `/sub-assemblies`, `/sub-assemblies/{id}`, `/sub-assemblies/new/{number}` | `SubAssemblies/SubAssemblies.razor`, each tab's form `SubAssemblyFormView.razor` | `SubAssembliesState` (the view), a `SubAssemblyForm` per tab |
| Products | `/products`, `/products/{id}`, `/products/new/{number}` | `Products/Products.razor`, each tab's form `ProductFormView.razor`, with its parts beside it | `ProductsState` (the view), a `ProductForm` per tab |
| Reports | `/reports` | `Reports.razor` | `ReportsState` |
| Price Simulator | `/price-simulator` | `PriceSimulator.razor` | `PriceSimulatorState` |
| Print, a dialog over the screen it was opened from (Products, Reports, Sub-Assemblies, the Price Simulator) | — | `Printing/PrintDialog.razor`; Products' choices in `Printing/ProductPrintPane.razor` | `PrintPreviews` (open, and on what); Products' `PrintDialogState` |
| Find (Ctrl+K), a dialog over every screen | — | `Layout/FindHost.razor` | `QuickFindState` |
| Settings: Units of Measure, Material Categories, Product Categories, Roles, Costing Defaults, Keyboard, Security, Database | `/settings/<tab>` | `Settings/`, a page per tab | `UnitsOfMeasureState`, `RawMaterialCategoriesState`, `ProductCategoriesState`, `SubAssemblyRolesState`, `CostingDefaultsState`, `KeyboardState`, `SecurityState`, `DatabaseBackupsState` |
| The sign-in panel, over a locked window | none: drawn by `MainLayout` | `Layout/SignInScreen.razor` | `SignInState`, over `AppLock` |

The [user manual](user-manual.md) describes every screen. Read its [Key ideas](user-manual.md#1-key-ideas) chapter before you read the code, because the code uses the same words.

### Glossary

| Term | In code | Meaning |
|---|---|---|
| Raw material | `RawMaterial` | Anything bought or made to produce a product. Its `Type` is `Ingredient` or `Packaging`, and it's fixed once saved. |
| Base unit | `RawMaterial.BaseUnitId` | The unit a material's quantities and costs are kept in. It's always the gram for an ingredient (`BaseUnitId` is null) and is chosen per packaging material. |
| Alternative unit | `RawMaterialUnit` | A multiple of the base unit, such as a bag of 25,000 g. Standard units, such as the kilogram and the inch, have fixed multipliers (`StandardUnitRules`). |
| Source | `RawMaterialSource` | Where an ingredient's cost comes from: `Purchased` (brand prices), `Made` (a sub-assembly and its add-ins) or `Linked` (a share of another ingredient's cost). Packaging is always purchased. Fixed once saved. |
| Category type | `RawMaterialCategoryType` | What a `RawMaterialCategory` holds: `Ingredient` (purchased and linked ingredients), `Made` (made ingredients only) or `Packaging`. `RawMaterialRules.CategoryTypeFor` gives the one a material goes in, and `MaterialTypeOf` the material type a category holds, so a Made category's tree folder sits in Ingredients. |
| Brand price | `RawMaterialBrandPrice` | What one brand of a purchased material costs now, in one of its units. The material is costed at its dearest brand per base unit, or at the brand in its `PreferredBrand` (Basis) while that has a price. |
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
| Costing parameters, costing defaults | `CostingParameters`, `CostingDefaults` | A product's labor, overhead, administrative and markup percentages; and the values a new product starts with (its stage among them), with the cost buffer a new raw material starts with and the shop's VAT and Wastage %, which every product is priced with (`ProductDetail.VatPercent` and `WastagePercent`, read with every product). A version snapshot keeps the VAT and wastage it was priced with, and the History tab lists a change in either after the product's own changes (`ProductVersionRules.ShopChanges`), never as one: a Save records no version for them alone. The Products list's **Apply Costing Defaults** sets the products picked to the defaults' percentages, with a version of each one it changes (`VersionedSaves.ApplyCostingAsync`), all of them together in some 40 round trips however many there are (`ICostingParametersRepository.SetForProductsAsync`, `IProductVersionRepository.AddRangeAsync`, `IProductRepository.StampSavedAsync`). |
| Stage | `ProductStage` | `Innovation` or `Production`: the Products tree's two top folders, named by `ProductRules.StageTitle`. Stored by name through `ProductStageConverter`, and read by Dapper through `ProductStageTypeHandler`; both also read `ResearchAndDevelopment`, Innovation's name until 2026-11-09, as a copy built before then still writes it. |
| Baker's percentage | `SubAssemblyLine.IsFormulaBase`, `AuthoredBakersPercent` | A line's weight as a percentage of the total weight of the lines ticked as the formula's base. |

---

## 2. Prerequisites

### Knowledge

You'll be productive sooner if you're comfortable with the following.

| Area | What to know | Where it's used |
|---|---|---|
| C# 15 and .NET 11 | Primary constructors, records and `record struct`, `required` members, collection expressions, pattern matching, nullable reference types, `async`/`await` | Everywhere |
| Blazor | Components and parameters, `RenderFragment`s, `@key`, cascading values, the component lifecycle, `InvokeAsync` and `StateHasChanged`, scoped services, JavaScript interop | `src/TinapayanRMS.Components` |
| .NET MAUI Blazor Hybrid | `MauiApp`'s builder, `BlazorWebView` and its root components, the window's lifecycle events, WinUI's window (`AppWindow`, its presenter and non-client regions) and file pickers under `Platforms/Windows/` | `src/TinapayanRMS.Maui` |
| HTML, CSS and a little JavaScript | Flexbox and grid layout, CSS variables, print styles (`@page`), DOM events and focus | `Components/wwwroot`, the shared components |
| Screen state classes of the app's own (no MVVM library), with Rx and DynamicData | `ObservableState`, `RaiseAndSetIfChanged`, plain getters, `ScreenCommand`, `Interaction`, `Subject`, `LoadLatest`, DynamicData's `SourceCache` and `AutoRefresh` | `Presentation/` (the bases in `Shared/`) |
| DynamicData | `SourceCache<T, TKey>`, `Connect().SortAndBind(...)` | The screens' lists |
| EF Core 11 | `DbContext`, `IEntityTypeConfiguration<T>`, migrations, change tracking, transactions, `ExecuteDeleteAsync` | Writes and the schema, in `Infrastructure` |
| Dapper and PostgreSQL SQL | Multi-mapped joins (`splitOn`), recursive CTEs (`WITH RECURSIVE`, stopped at a loop by `CYCLE`), arrays with `unnest` | Reads, in `Infrastructure` |
| PostgreSQL | Roles and the password file, snake_case names, `psql`, `pg_dump` and `pg_restore`, ICU collations | Setup, tests, backups, debugging |
| Syncfusion Blazor | `SfGrid`, `SfComboBox`, `SfMultiSelect` (the Print dialog's Print and Categories lists), `SfTab`, `SfDialog`, `SfToast`, `SfTooltip` (every tooltip: the activity bar's names, and the rest through `Tooltips`), `SfButton`, `SfDropDownButton`, `SfChart`; the Fluent 2 theme; XlsIO for workbooks | Every page |
| FluentValidation | `AbstractValidator<T>` | `ApplicationCore/Validators` |
| DI | Service lifetimes, scopes (one per page the web view loads) | `Components/ServiceRegistration.cs` |
| Serilog | The static `Log.Logger`, `ForContext` | Logging |
| TUnit on Microsoft.Testing.Platform | `[Test]`, `[Arguments]` and `[MethodDataSource]`, `[Before(Test)]` hooks, keyed `[ClassDataSource]` fixtures, `[NotInParallel]` | `tests/` |
| AwesomeAssertions | `Should()` assertions. It's a fork of FluentAssertions 7, with the same API. | `tests/` |
| Respawn | Resetting a database between tests | The test fixtures |
| MSBuild | `Directory.Build.props`, Central Package Management | The build |
| The domain | Recipe costing in a bakery: bills of materials, yield, wastage, baker's percentage, cost-plus pricing | Read the user manual's [Key ideas](user-manual.md#1-key-ideas) and [How costs are worked out](user-manual.md#12-how-costs-are-worked-out) |

### Software

| Tool | Version | Notes |
|---|---|---|
| Windows | 10 or 11 | The app only runs on Windows. |
| The web view | WebView2 (the Evergreen runtime, which Windows 10 and 11 have) | The app's window draws its screens in it. |
| .NET MAUI workload | `maui-windows` 11.0.0 (MAUI's own packages, `MauiPackagesVersion` in [Directory.Packages.props](../Directory.Packages.props), are 11.0.10: they can be ahead of the workload) | From an administrator terminal, since the SDK is in Program Files: `dotnet workload install maui-windows --skip-manifest-update`; `dotnet workload list` shows it. A build that asks for `maui-tizen` (NETSDK1147) is missing it. |
| .NET SDK | 11.0.100, or a later 11.0 feature band | Pinned in [global.json](../global.json) with `rollForward: latestFeature`; a preview SDK isn't taken (`allowPrerelease` false). |
| PostgreSQL | 18 (EDB's Windows installer, service `postgresql-x64-18`, `localhost:5432`) | Reached as the `tinapayanrms` role, which must be able to create databases (`CREATEDB`), because the tests create their own. Its password goes in the password file, `%APPDATA%\postgresql\pgpass.conf`, so no connection string carries one. The installer's `bin` folder holds `psql`, and the `pg_dump` and `pg_restore` the backups run. |
| `dotnet-ef` | 11.0.x, matching EF Core (`EFCoreVersion` in [Directory.Packages.props](../Directory.Packages.props), now 11.0.0) | `dotnet tool update --global dotnet-ef --version 11.0.0` (it installs the tool if it isn't there) |
| Syncfusion licence key | Essential Studio 35.x | Set as the `SYNCFUSION_LICENSE_KEY` user environment variable. The build compiles it into the app, so the app runs on a computer without the variable. An app built without it still opens, and Syncfusion shows a licence notice in the window; `dotnet publish` fails without it. The tests don't need it. After changing the key, rebuild. A key covers one major version, so moving `SyncfusionVersion` to a new major version needs a new key. A program started before the variable changed, such as the IDE and its terminals, keeps the old value until it's restarted. |
| An IDE | Any with .NET 11 support | Visual Studio, VS Code with the C# Dev Kit, or Rider. Git ignores `.vs/`, `.vscode/` and `.idea/`. |
| Optional tools | | `ilspycmd` (decompiles Syncfusion's assemblies), and `dotnet-stack`, `dotnet-dump` and `dotnet-trace` (diagnose the running app). Node.js, for the `run-tinapayanrms` skill's driver, which drives the window with `playwright-core` ([Checking a screen in the app's window](#checking-a-screen-in-the-apps-window)). |

Docker isn't used anywhere in this project.

---

## 3. Getting started

### First-time setup

Run every command from the repository root.

1. **Check the SDK.** `dotnet --version` should print 11.0.100 or later.
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
4. **Install the EF Core tools** at the `EFCoreVersion` in `Directory.Packages.props`: `dotnet tool update --global dotnet-ef --version 11.0.0`. The repo's [.config/dotnet-ef.json](../.config/dotnet-ef.json) names `src/TinapayanRMS.Infrastructure` as the project and the startup project, so a `dotnet ef` command needs no `--project`, from any folder in the repo (an explicit `--project` still wins).
5. **Build:** `dotnet build TinapayanRMS.slnx`.
6. **Create the database.** This creates `tinapayanrms` and applies every migration, including the rows the migrations seed, such as the starting sub-assembly roles:
   ```powershell
   dotnet ef database update
   ```
7. **Run the app:** `dotnet run --project src/TinapayanRMS.Maui`. With an empty database, follow the user manual's [First-time setup](user-manual.md#3-first-time-setup).
8. **Run the tests:** `dotnet test --solution TinapayanRMS.slnx`. The first run creates the test databases `tinapayanrms_test_infrastructure`, `tinapayanrms_test_presentation`, `tinapayanrms_test_pricing`, `tinapayanrms_test_mcp`, `tinapayanrms_test_recipeapi` and `tinapayanrms_test_usecases`. The tests never touch `tinapayanrms`.

### Connection strings

| Used by | Where it's set |
|---|---|
| The app | `ConnectionStrings:Default` in [src/TinapayanRMS.Maui/appsettings.json](../src/TinapayanRMS.Maui/appsettings.json), which the build puts inside the app (an embedded resource), with environment variables (`ConnectionStrings__Default`) over it. The MCP server and the Recipe API link the same file into their output. |
| `dotnet ef` | Hard-coded in the design-time factory, [TinapayanRMSDbContextFactory](../src/TinapayanRMS.Infrastructure/TinapayanRMSDbContextFactory.cs) |
| The tests | The app's `appsettings.json`, which `TinapayanRMS.Tests.Common` copies into each test project's output. Each project swaps in its own database name. |

Both are `Host=localhost;Database=tinapayanrms;Username=tinapayanrms`, with no password: Npgsql reads it from the password file. If your server, port or role differ, change both. A connection string may carry a `Password` instead, and the backups pass it on to `pg_dump` and `pg_restore`, but it's then in a file anyone with the folder can read.

The backups look for `pg_dump` and `pg_restore` in `C:\Program Files\PostgreSQL\<version>\bin`, newest version first, then on the `PATH`. Set `Backups:ToolsFolder` in `appsettings.json` to use another folder.

### Where the app writes

- **Logs**: `%LocalAppData%\TinapayanRMS\Maui\logs\tinapayanrms-<date>.log`. There's one file a day, and the last 31 are kept.
- **View state**: `%LocalAppData%\TinapayanRMS\Maui\view-state.json` remembers which tree folders were left closed (`FolderMemory`, so a folder new since opens), whether the Vim keys are on (`"vim-keys": ["on"]`), the Reports grids' filters, sort and hidden columns (`reports-*`, through `GridLayouts`), and the automatic backups' copy folder and last problems. It's safe to delete.
- **The web view's folder**: `%LocalAppData%\TinapayanRMS\Maui\WebView2\<copy>`, WebView2's cache and profile, one for each copy of the app on the disk (`<copy>` is `MauiProgram.CopyKey`, a hash of the app's folder, which `SingleInstance` keys its claim by too): WebView2 won't start a second process on a folder in use with other options, such as the driver's debugging port while your own copy is open. `MauiProgram` gives it to WebView2 as the `WEBVIEW2_USER_DATA_FOLDER` environment variable before the window opens, unless one is set already. Left to itself it went beside the `.exe` (`TinapayanRMS.Maui.exe.WebView2`), which under `Program Files` can't be written, so a per-machine install never loaded its page. `BlazorWebViewInitializing`'s `UserDataFolder` doesn't do it: MAUI builds an environment from it and then starts the WinUI control without it. It's safe to delete.
- **Backups**: `%ProgramData%\TinapayanRMS\Backups`, one folder for every Windows user of the computer. `pg_dump` writes the `.backup` files as the user running the app. The app creates this folder and, as its owner, gives `BUILTIN\Users` Modify on it, inherited by every file, so anyone using the app can copy, restore and delete any backup ([BackupFolder](../src/TinapayanRMS.Infrastructure/Services/BackupFolder.cs)). The `.bak` files SQL Server wrote before the move to PostgreSQL (2026-10-21) are no longer listed and can't be restored. The tests use `%ProgramData%\TinapayanRMS\Test Backups\<guid>` and delete it afterwards.

### Command reference

| Task | Command |
|---|---|
| Build | `dotnet build TinapayanRMS.slnx` |
| Run | `dotnet run --project src/TinapayanRMS.Maui` |
| Run it and drive it from a script (see [Checking a screen in the app's window](#checking-a-screen-in-the-apps-window)) | `powershell -ExecutionPolicy Bypass -File .claude/skills/run-tinapayanrms/driver.ps1 launch` |
| All tests | `dotnet test --solution TinapayanRMS.slnx` |
| One test project | `dotnet test --project tests/TinapayanRMS.ApplicationCore.Tests` |
| One test class | `dotnet test --project tests/TinapayanRMS.ApplicationCore.Tests --treenode-filter "/*/*/UnitConversionRulesTests/*"` |
| One test | `dotnet test --project tests/TinapayanRMS.Presentation.Tests --treenode-filter "/*/*/*/Declining_the_question_saves_nothing"` |
| Only the test projects your changes reach (see [Running fewer tests while developing](#running-fewer-tests-while-developing)) | `powershell -ExecutionPolicy Bypass -File scripts/Test-Changed.ps1` |
| GitHub CI's last runs, which check every push: the build with warnings as errors, the migration check, the whole suite (see [When a change is done](#when-a-change-is-done)) | `gh run list --branch main --limit 3`, then `gh run watch <id>` to follow one, or `gh run view <id> --log-failed` for why it failed |
| One class again each time you save, by hot reload | `powershell -ExecutionPolicy Bypass -File scripts/Watch-Tests.ps1 Presentation -Class "*ProductsStateRefreshTests"` |
| Coverage (a Cobertura report in `TestResults/`) | `dotnet test --solution TinapayanRMS.slnx --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml` |
| Time the benchmarks' scenarios once, warm (see [Benchmarks and performance budgets](#benchmarks-and-performance-budgets)); `--scenario "Screens.*"` for some | `dotnet run -c Release --project tests/TinapayanRMS.Benchmarks -- --quick` |
| Compare how fast a refactor left the app: a base commit against the working tree, the start included (`-Window` for the window's own start) | `powershell -ExecutionPolicy Bypass -File scripts/Compare-Benchmarks.ps1 -Base main` |
| Approve new budgets, after a change meant to move one | `$env:TINAPAYANRMS_APPROVE_BUDGETS = '1'; dotnet test --project tests/TinapayanRMS.Benchmarks.Tests; Remove-Item Env:TINAPAYANRMS_APPROVE_BUDGETS` |
| Add a migration | `dotnet ef migrations add <Name>` |
| Check the model matches the migrations | `dotnet ef migrations has-pending-model-changes` |
| Apply migrations | `dotnet ef database update` |
| Regenerate the compiled model, after any model change | `dotnet ef dbcontext optimize --output-dir CompiledModels --namespace TinapayanRMS.Infrastructure.CompiledModels` |
| Query the database | `& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -h localhost -U tinapayanrms -d tinapayanrms -c "SELECT name, cost_per_base_unit FROM raw_materials"` |
| Run the MCP server by hand, as an AI assistant does from `.mcp.json` (see [Checking costs from an AI assistant](#checking-costs-from-an-ai-assistant)) | `powershell -ExecutionPolicy Bypass -File scripts/Start-McpServer.ps1` |
| Run the Recipe API (see [The Recipe API](#the-recipe-api)), then try it | `dotnet run --project src/TinapayanRMS.RecipeApi`, then `curl http://localhost:5290/api/v1/mixes` |
| Make the database role the Recipe API reads as, which can't write (once per PC, as the user the API runs as) | `powershell -ExecutionPolicy Bypass -File scripts/New-DatabaseReader.ps1` |
| Install the Recipe API for this Windows user, starting at sign-in (in an administrator PowerShell to let the LAN in), reading as that role | `powershell -ExecutionPolicy Bypass -File scripts/Install-RecipeApi.ps1 -ReadOnlyUser tinapayanrms_reader` |
| Install the app, or update the installed copy, for this Windows user (Start menu and Desktop shortcuts) | `powershell -ExecutionPolicy Bypass -File scripts/Install-TinapayanRMS.ps1` |
| Clear a forgotten password, so the app asks for a new one (see [Signing in](#signing-in)); `-Database` for another database, `-Force` not to ask | `powershell -ExecutionPolicy Bypass -File scripts/Reset-Password.ps1` |
| Remove the installed app | `powershell -ExecutionPolicy Bypass -File scripts/Uninstall-TinapayanRMS.ps1` |
| Build the MSI, a per-machine install for every Windows user of a PC, into `artifacts\installer\` | `powershell -ExecutionPolicy Bypass -File scripts/Build-Installer.ps1` |

The tests run on Microsoft.Testing.Platform, which `global.json` turns on (`test.runner`). VSTest-style flags such as `--filter` and `--collect` don't work: use TUnit's `--treenode-filter` and `--coverage` instead. The filter is a path, `/assembly/namespace/class/test`, where `*` matches any characters within a part and `(A)|(B)` either name: `/*/*/*Products*/*` is every test of every class with Products in its name. A test project is a platform executable of its own (TUnit.Engine makes it one), so one run directly (`dotnet run`, or its `.exe`) takes the same options.

Close any copy of the app you have running before you build. A running app locks its own output files: its process is `TinapayanRMS.Maui`.

**Installing the app**: [scripts/Install-TinapayanRMS.ps1](../scripts/Install-TinapayanRMS.ps1) builds it on the PC it installs on, so that PC needs the SDK, the `maui-windows` workload and `SYNCFUSION_LICENSE_KEY` (it checks for the last two first, and reads the key from the user's environment when the shell predates it). It publishes `src/TinapayanRMS.Maui` (Release: framework-dependent ReadyToRun on the machine's .NET 11, unpackaged, with the Windows App SDK in its folder, about 270 MB, most of it the Windows App SDK's) to a temporary folder, so a failed build leaves the installed copy alone. Only then does it close a copy running from the install folder, with `CloseMainWindow`, as its title bar's Close does: one asking about unsaved changes stays open and the script stops, rather than killing it. It mirrors the publish into `%LocalAppData%\Programs\TinapayanRMS` (robocopy `/MIR`, as `Install-RecipeApi.ps1` does), which also clears the web view's folder that copies built before 2026-11-15 kept beside the `.exe`, and makes Start menu and Desktop shortcuts (`-NoDesktopShortcut`, `-NoStartMenuShortcut`; with both and an `-InstallFolder` of its own, a trial install leaves nothing outside that folder). It never touches the database or `%LocalAppData%\TinapayanRMS\Maui`. [Uninstall-TinapayanRMS.ps1](../scripts/Uninstall-TinapayanRMS.ps1) closes the copy the same way and deletes the folder and the shortcuts that open it. Before a new version goes to the shop, install it into a trial folder here and run the driver's smoke check on it ([Checking a screen in the app's window](#checking-a-screen-in-the-apps-window)).

**The MSI**: [scripts/Build-Installer.ps1](../scripts/Build-Installer.ps1) builds `artifacts\installer\TinapayanRMS-<version>-x64.msi`, which installs the app for every Windows user of a PC: into `C:\Program Files\TinapayanRMS`, with Start menu and Desktop shortcuts in the all-users folders, removed from Settings > Apps. The PC it goes on needs no SDK, workload or key, only the .NET 11 Runtime (x64), WebView2 (Windows 10 and 11 have it), and PostgreSQL with each user's `pgpass.conf`. The script checks for the key and the workload as the install script does, publishes the app the same way (Release) into `artifacts\installer\app`, emptied first, and builds [installer/TinapayanRMS.Installer.wixproj](../installer/TinapayanRMS.Installer.wixproj) over it with `AppPublishDir` and `ProductVersion` (`-Version`; by default the app's `ApplicationDisplayVersion` and the commit count, `1.0.414`). It warns when the working tree has changes not committed, as the MSI then carries them under the last commit's version. `artifacts/` is git-ignored.
- **WiX 5.0.2**, an MSBuild SDK whose version is in `global.json` (`msbuild-sdks`), with its extensions' versions in `Directory.Packages.props`. It stays at 5.0.2, the last version under the plain MS-RL licence. From 6.0 its binaries come under the Open Source Maintenance Fee EULA, a monthly fee for any user whose revenue is US$10,000 a year or more, and 7.0 won't build until the EULA is accepted (the user's choice, 2026-11-15).
- The project isn't in `TinapayanRMS.slnx`, so neither a solution build nor CI needs the Syncfusion key. Heat (`HarvestDirectory`, the `WixToolset.Heat` package) harvests every published file into the `AppFiles` component group. [installer/Package.wxs](../installer/Package.wxs) is the rest. A fixed `UpgradeCode` and `MajorUpgrade` remove the version installed before the new one goes in, a same version included (`AllowSameVersionUpgrades`), so a rebuild of one commit installs over itself, and an older one is refused. The Netfx extension's `DotNetCompatibilityCheck` (`Microsoft.NETCore.App` 11.0.0, x64, roll forward `minor`) feeds a launch condition that stops the install before anything changes, saying where to get the runtime. Each shortcut is kept by an HKLM value under `Software\TinapayanRMS`.
- **Windows Installer's checks it suppresses** (`SuppressIces`, with why in the project): ICE03 (the Windows App SDK's WinUI files carry languages older validation tables don't know), ICE38, ICE43 and ICE57 (they take the Start menu and Desktop for the user's own folders, which aren't in a per-machine package), and ICE61 (the same-version upgrade).
- **A copy that's open is asked to close by Windows Installer's Restart Manager**, which finds it by the files it holds, so only the copy in `Program Files`. The window answers it as its Close would ([Unsaved changes](#unsaved-changes)): with nothing unsaved it ends, and with a form's unsaved changes, or a backup or restore running, it stays open and asks its question, so the installer can't close it. WiX's `util:CloseApplication`, tried first, ran after Restart Manager, and it finds the app by its `.exe`'s name alone, so it could only reach copies run from elsewhere, a developer's own build among them.
- The MSI never touches the database or a user's `%LocalAppData%\TinapayanRMS\Maui`: apply new migrations as after any update. It has no wizard: opened, it asks for an administrator and installs. The key is compiled into the app inside it, so keep it within the shop.
- Checked on 2026-11-15: built with no warnings, installed, the smoke check passed on the copy in `Program Files` running as a standard user, and an uninstall with the app open closed it and left no folder, shortcut or registry key.

### When a change is done

A change is done when:

- the solution builds,
- every test passes,
- any new migration applies cleanly to your local database,
- a change users can see (a screen, button, field, rule or cost formula) has updated the [user manual](user-manual.md) in the same change,
- a change to anything this manual describes (the setup, commands, solution layout, architecture, a pattern or convention, or a type or file it names) has updated this manual in the same change, and
- a decision that changed is recorded in [PLAN.md](../PLAN.md).

**CI**, [.github/workflows/build.yml](../.github/workflows/build.yml), checks the build, the tests and the migrations at once on GitHub, on every push to `main` and every pull request (Dependabot's), from a clean checkout ([ADR 0013](adr/0013-ci-on-github-changes-straight-to-main.md)): the whole solution built with warnings as errors (NuGet's vulnerability audit, `NU1900` to `NU1904`, only reports), `dotnet ef migrations has-pending-model-changes` finding nothing, and the whole suite passing, in that order, each stopping the run when it fails. Of a new migration it checks that none is missing, and that it applies to data: the golden master's and the benchmarks' tests restore an anonymized copy of the shop's data as the app restores a backup, which brings it through every migration since the copy was taken. Applying it to your own database stays a step on the PC (the app does it as it opens). It runs on GitHub's Windows runner (the app is a WinUI window, and the backup tests check the backups folder's Windows permissions), with the SDK `global.json` names and the MAUI workload it ships with, and a PostgreSQL 18.7 of its own set up as a developer's: the `tinapayanrms` role, which can create the test databases, its password in `pgpass.conf`, and the tools in `Program Files\PostgreSQL\18`. It takes the runner's `PGUSER` and `PGPASSWORD` out of the environment before the migration check and the tests, since a password there wins over `pgpass.conf` and a backup test expects none. Changes go straight to `main`, so a push is checked after it lands: follow its run (`gh run watch`), and fix a failure before pushing anything else. A failure that isn't the change's, one of the two intermittent ones in the build plan's open items, is answered by running the failed jobs again (`gh run rerun <id> --failed`) until its open item is done. Every push to `main` gets a run of its own, never cancelled by the next; a newer push to a pull request cancels its older run; a push of `.claude/` alone runs nothing, while the docs are checked too (`DocPathsTests`); and the Actions tab starts a run by hand. The repository is public, so its runs cost nothing. From 2026-11-21 to 2026-11-25, while the repository was private and the account had no Actions minutes left, a script ran the same steps on the developer's PC before each push ([ADR 0011](adr/0011-change-control-on-the-developers-pc.md)).

---

## 4. The solution

### Projects

| Project | Target framework | References | Contents |
|---|---|---|---|
| `src/TinapayanRMS.Domain` | `net11.0` | Nothing | `Entities/`, `Enums/` |
| `src/TinapayanRMS.ApplicationCore` | `net11.0` | Domain; FluentValidation | `Interfaces/`, `Rules/`, `Validators/` |
| `src/TinapayanRMS.Infrastructure` | `net11.0` | Domain, ApplicationCore; EF Core (Npgsql, with EFCore.NamingConventions), Dapper, the Generic Host | `TinapayanRMSDbContext` and its design-time factory, `DatabaseHost` and `ReadOnlyConnection` at the root; `Configurations/`, `Migrations/`, `CompiledModels/`, `Queries/`, `Repositories/`, `Services/` |
| `src/TinapayanRMS.Pricing` | `net11.0` | ApplicationCore | What a saved product costs, the one way the screens and both servers work it out: `SavedProductReader` and what it builds, at its root. `Versions/` holds the product versions' snapshot and the saves that record them (`VersionedSaves`, `ProductVersionRecorder`) |
| `src/TinapayanRMS.UseCases` | `net11.0` | ApplicationCore, Pricing | What the screens do with the records ([ADR 0002](adr/0002-layers-use-cases-and-drafts.md)), a folder per screen moved to it: `SubAssemblies/` holds `ISubAssemblyQueries` (what the screen reads), `ISubAssemblyEditor` (Save with its checks and versions, Delete, Rename, Move, Make In-House Ingredient), `ISubAssemblyRoleEditor` and `SharedRecipeImpact`, each with its class; `RawMaterials/` holds `IRawMaterialQueries` (with `MaterialCostShares`), `IRawMaterialEditor` (Save, with the material's, a unit's and a brand price's checks; Delete, Rename, Move, a price history entry's Delete) and `IRawMaterialCategoryEditor`; `Products/` holds `IProductQueries`, `IProductEditor`, `IProductListEditor` and `IProductCategoryEditor`; `Settings/` holds `ILookupListEditor` (what Settings' list tabs do to their lists, which the category and role editors take on too), `UnitOfMeasureEditor`, `ICostingDefaultsEditor` and `IPrintSettingsEditor`; `Access/` holds `IAccessSettingsEditor`; `Database/` holds `IBackupLibrary` and `IDatabaseUpdates`; `QuickFind/` holds `IQuickFindQueries`; `Reports/` holds `IReportQueries`; `PriceSimulator/` holds `IPriceSimulatorQueries` and `IPriceScenarioEditor`; `Printing/` holds `IPrintQueries` |
| `src/TinapayanRMS.Presentation` | `net11.0` | Domain, ApplicationCore, Pricing and UseCases (not Infrastructure, since 2026-11-21); DynamicData, System.Reactive, Serilog | The screens' states, with no UI framework in them: a folder per screen (`RawMaterials/`, `SubAssemblies/`, `Products/`, `Settings/`, `Reports/`, `PriceSimulator/`, `Printing/`), `App/` for what's around the screens, and `Shared/` and `Trees/` for what they share |
| `src/TinapayanRMS.Components` | `net11.0` (Razor) | Infrastructure, Presentation; Blazor, Syncfusion Blazor, XlsIO | The screens: `ServiceRegistration`, `StartupTasks`, `UnhandledFailures` and `OneCopyPerPc` at the root; `Pages/`, `Layout/`, `Shared/`, `Printing/`, `Vim/`; `wwwroot/` (their styles and scripts) |
| `src/TinapayanRMS.Maui` | `net11.0-windows10.0.19041.0` (MAUI) | Components; MAUI, BlazorWebView, Serilog | The app: `MauiProgram`, `App`, `MainPage` and `appsettings.json` at the root; `Platforms/Windows/` (the window), `Services/`, `Resources/AppIcon/`, `wwwroot/index.html` (the host page) |
| `src/TinapayanRMS.Mcp` | `net11.0` | Infrastructure, Pricing; ModelContextProtocol | An MCP server for AI coding assistants (see [Checking costs from an AI assistant](#checking-costs-from-an-ai-assistant)): `Program` at the root; `Tools/`, `Results/`, `Serialization/` |
| `src/TinapayanRMS.RecipeApi` | `net11.0` | Infrastructure, Pricing; ASP.NET Core (a framework reference), Serilog | A read-only HTTP API for the shop's other apps (see [The Recipe API](#the-recipe-api)): `Program`, `RecipeApiHost` and `recipeapi.json` at the root; `Contracts/`, `Endpoints/` |
| `tests/TinapayanRMS.ApplicationCore.Tests` | `net11.0` | Domain, ApplicationCore | Rule and validator tests, with no database |
| `tests/TinapayanRMS.Benchmarks` | `net11.0` (an exe) | Components, Tests.Common; BenchmarkDotNet | What a refactor could make slower ([Benchmarks and performance budgets](#benchmarks-and-performance-budgets)): `Commands` and `Program` at the root; `Scenarios/` (each measured operation, and the databases it runs on), `Suites/` (BenchmarkDotNet's classes and config, the `bench` command), `Startup/` (the app's start without its window, and its database). Not a test project: its name doesn't end in `.Tests` |
| `tests/TinapayanRMS.Benchmarks.Tests` | `net11.0` | Benchmarks | The scenarios held to their approved SQL commands and memory (`BudgetTests`, `Budgets/budgets.approved.txt`), on databases of their own |
| `tests/TinapayanRMS.Infrastructure.Tests` | `net11.0` | Infrastructure, Tests.Common | Persistence tests against the project's own database |
| `tests/TinapayanRMS.Presentation.Tests` | `net11.0` | Presentation, Tests.Common; CsCheck | The screen states on real repositories, against the project's own databases; the drafts' property tests with no database (`SubAssemblyDraftTests`, `RawMaterialDraftTests`) |
| `tests/TinapayanRMS.UseCases.Tests` | `net11.0` | UseCases, Tests.Common | The use cases on real repositories, against the project's own database |
| `tests/TinapayanRMS.Components.Tests` | `net11.0` | Components; bUnit | The app's DI wiring, the page's list of Syncfusion scripts, the shared components and the title bar (rendered with bUnit), the Vim keys' C#, and the architecture's rules (`Architecture/`), with no database and no MAUI |
| `tests/TinapayanRMS.Pricing.Tests` | `net11.0` | Pricing, Tests.Common | The pricing and the versioned saves (`Versions/`), some of it against the project's own database. What a composite prints is tested with the printouts, in `Presentation.Tests` (`ProductPrintoutTests`) |
| `tests/TinapayanRMS.Mcp.Tests` | `net11.0` | Mcp, Tests.Common | The MCP server's tools against the project's own database |
| `tests/TinapayanRMS.RecipeApi.Tests` | `net11.0` | RecipeApi, Tests.Common; ASP.NET Core's TestHost | The Recipe API, served in memory, against the project's own database |
| `tests/TinapayanRMS.Tests.Common` | `net11.0` | Infrastructure, Pricing; Respawn | The shared database fixtures, and `VersionRecorders`, which builds the versioned saves over real repositories. It's a plain library, not a test project. |

Only the Maui project targets Windows (`$(WindowsTargetFramework)`, Windows SDK 10.0.19041, which MAUI's Windows target needs; the bare `net11.0-windows` moniker resolves to `windows7.0`, which is too low). Everything else is on plain `net11.0`, the screens included, so the components and screen states are tested without a window, and neither server can come to depend on the app by accident.

### Folders are namespaces

Every folder in a project is a sub-namespace: `src/TinapayanRMS.Presentation/Products/` holds `TinapayanRMS.Presentation.Products`. A new file goes in the folder for its kind, never at a project root. The screens' code, one kind throughout, goes by screen instead: Components' pages in `Pages/Products/`, and Presentation's state classes with their parts and rows in `Products/`, with what several screens share in Presentation's `Shared/` and the record lists' trees in its `Trees/`. Only app-startup plumbing (the Maui project's `MauiProgram`, `App` and `MainPage`, the Components project's `ServiceRegistration`, `StartupTasks`, `UnhandledFailures` and `OneCopyPerPc`, Infrastructure's `DatabaseHost` and `ReadOnlyConnection`, the MCP server's `Program`, and the Recipe API's `Program` and `RecipeApiHost`), the DbContext pair, and `TinapayanRMS.Pricing`, which is one kind of thing throughout, sit at a root. Code in a sub-namespace sees its parent namespaces without a `using`; sibling folders need one.

| A new… | Goes in | Its tests go in |
|---|---|---|
| Entity or enum | `Domain/Entities/`, `Domain/Enums/` | (tested through the layers above) |
| Business or costing rule | `ApplicationCore/Rules/` | `ApplicationCore.Tests/Rules/` |
| Validator | `ApplicationCore/Validators/` | `ApplicationCore.Tests/Validators/` |
| Repository | Interface in `ApplicationCore/Interfaces/`, implementation in `Infrastructure/Repositories/` | `Infrastructure.Tests/Persistence/` |
| EF configuration | `Infrastructure/Configurations/` | `Infrastructure.Tests/Persistence/` |
| Cross-entity service | `Infrastructure/Services/` | `Infrastructure.Tests/` |
| Anything a server prices with as well as the screens | `Pricing/` | `Pricing.Tests/` |
| Screen | Its state class, with its parts and rows, in a folder for the screen in `Presentation/` (`Presentation/Reports/`); its page in `Components/Pages/` | `Presentation.Tests/`, in a folder of the same name |
| What several screens' states share | `Presentation/Shared/`, or `Presentation/Trees/` for the record lists' trees | `Presentation.Tests/Shared/`, `Presentation.Tests/Trees/` |
| What a screen moved to use cases reads and writes (ADR 0002) | `UseCases/<Screen>/`: an interface and its class, with the records they take and give | `UseCases.Tests/<Screen>/` |
| A moved record screen's draft | `Presentation/<Screen>/`, beside its form (`SubAssemblyDraft`, `RawMaterialDraft`, and Products' a part at a time: `ProductPackagingDraft`, `ProductComponentsDraft`, `ProductCosting`, `ProductRecipeDraft`, and `ProductDraft` over them) | `Presentation.Tests/<Screen>/`, with no database (`SubAssemblyDraftTests`, `RawMaterialDraftTests`, `ProductPackagingDraftTests`, `ProductComponentsDraftTests`, `ProductCostingTests`, `ProductRecipeDraftTests`, `ProductDraftTests`) |
| Part of a large screen | `Components/Pages/<Screen>/` | — |
| Shared component, or a service the pages share | `Components/Shared/` | `Components.Tests/Shared/` |
| Part of the window's frame (the title bar, the activity bar, the tabs, Find) | `Components/Layout/` | `Components.Tests/Layout/` |
| What only the window can do | An interface in `Components/Shared/` (`IAppWindow`, `IFileDialogs`, `IPagePrinter`), its Windows code in `Maui/Platforms/Windows/` | a fake in `Components.Tests/Fixtures/` |
| Part of a printout | `Components/Printing/` | — |
| Part of the Vim keys | `Components/Vim/`, and `Components/wwwroot/vim.js` | `Components.Tests/Vim/` |
| A style | The screen family's stylesheet in `Components/wwwroot/css/`, or `Components/wwwroot/app.css` | — |

A file can hold a few small, closely related types, such as a tree's rows in `ProductTree.cs`. The screens' state classes hold only their screen: the row and edit types a screen uses have files of their own beside it, such as `ProductUsageEdit.cs` and `PackagingLineRow.cs` for Products.

### Build configuration

- [Directory.Build.props](../Directory.Build.props) sets `net11.0`, `ImplicitUsings`, `Nullable`, the `WindowsTargetFramework` property, `AnalysisLevel` `latest-recommended` (the SDK's built-in analyzers at their Recommended rule set), the code-style rules in the build (`EnforceCodeStyleInBuild`) with the compiler reading the doc comments (`GenerateDocumentationFile`, which IDE0005 needs to know whether a using is there for a `<see cref>`; CS1591 and CS1573, the comments a public member lacks, are off, and `PublishDocumentationFiles` keeps the XML files out of a publish), and .NET 11's runtime async (`<Features>$(Features);runtime-async=on</Features>`): `async` methods run on the runtime's own support rather than as compiler-made state machines, so a decompiled assembly shows no `<…>d__` class for one, only for iterators. It's still a preview, opt-in in the release SDK (11.0.100), and kept on as the direction .NET is taking (PLAN.md's Build settings has why, and what was measured). Delete the line to go back. It also leaves the commit out of every build but `dotnet publish`'s (`EnableSourceControlManagerQueries` and `EnableSourceLink` are off unless `_IsPublishing`, 2026-11-04). The SDK otherwise puts the commit's hash into every assembly's version and Source Link file, so the first build after any commit, even one that changed only docs, recompiled every project. A published copy still says which commit it was built from: `1.0.0+<hash>`, its file's Product version. The build also writes each project's folder, drive letter's case and all, into a file the compiler reads (`build_property.ProjectDir`, in `obj/.../<Project>.GeneratedMSBuildEditorConfig.editorconfig`), so a build from `c:\…` after one from `C:\…`, or the other way, recompiles every project too.
- [tests/Directory.Build.props](../tests/Directory.Build.props) imports it. It gives every project whose name ends in `.Tests` the TUnit.Engine, AwesomeAssertions, coverage and hot reload packages, and compiles [tests/HotReloadReruns.cs](../tests/HotReloadReruns.cs) (see [Running fewer tests while developing](#running-fewer-tests-while-developing)) and [tests/TestRunSettings.cs](../tests/TestRunSettings.cs) into each, the second a `[Before(TestDiscovery)]` hook that runs at most as many tests at once as the PC has processors and turns off the HTML report and its JSON. TUnit's own limit is twice the processors, and that many tests beside the chains that run one at a time (the backup tests, the off-screen grids) slowed the whole suite from under 5 minutes to over 6 on this PC; `--maximum-parallel-tests` or `TUNIT_MAX_PARALLEL_TESTS` overrides the limit. TUnit writes the report to `TestResults/` on every run unless told not to (20 MB for Infrastructure.Tests) while recording every traced call, each SQL command Npgsql sends included. Set them back to `true` there for one. TUnit adds `using TUnit.Core;` itself. `Tests.Common` takes only `TUnit.Core`, for the hook and fixture interfaces: `TUnit.Engine` would make it a test executable.
- [Directory.Packages.props](../Directory.Packages.props) turns on Central Package Management. Versions live only there, so a `PackageReference` never has a `Version`. The EF Core, Microsoft.Extensions and Syncfusion versions are the `EFCoreVersion`, `MicrosoftExtensionsVersion` and `SyncfusionVersion` properties. Every Syncfusion package must be on the same version, so upgrade them together. Transitive pinning is on, so the "Transitive pins" group can lift a transitive dependency to a fixed version: currently `Microsoft.EntityFrameworkCore.Relational`, held at `EFCoreVersion` so every project gets one EF Core. `EFCore.NamingConventions` has no .NET 11 release yet, and its 10.0.1 asks for EF Core 10. It works on EF Core 11 (the snake_case names, the model-changes check and the compiled model all hold), so move it to its 11.x when there is one. `dotnet list TinapayanRMS.slnx package --include-transitive --vulnerable` lists what still needs one.
- **The build refuses what the rules forbid** ([ADR 0004](adr/0004-quality-gates.md), what it refuses [ADR 0010](adr/0010-the-gates-read-method-bodies.md)). Directory.Build.props gives every C# project Microsoft.CodeAnalysis.BannedApiAnalyzers over [BannedSymbols.txt](../BannedSymbols.txt): `Convert.ToDecimal` and `new decimal(...)` from a `double` or `float`, and from what may be one boxed (`Convert.ToDecimal(object)`, `IConvertible.ToDecimal`, `Convert.ChangeType`); `Observable.Switch` (let through in `ScreenState.LoadLatest` alone, by a `#pragma`); and every way to block on a `Task` or a `ValueTask`, `Task.Result`, `ValueTask<T>.Result`, each overload of `Task.Wait` and `Task.WaitAll`, and each awaiter's `GetResult` (`GetAwaiter().GetResult()`, configured or not, of a `Task` or a `ValueTask`), let through in two places alone, by a `#pragma`: the app's `BackgroundWork.Stop`, as the app's end can't be awaited, and `DataUpgrades.Run`, EF Core's synchronous seeding hook, which only `dotnet ef database update` calls; a use is warning RS0030, with the reason. An `await` isn't a call to `GetResult` the analyzer sees. `Task.WaitAny`, `IAsyncEnumerable<T>`'s `ToBlockingEnumerable` and Rx's blocking operators (`Wait`, `First`, `Last`, `Single` and their kin) aren't listed yet, and nothing uses them (the build plan's open items). WinUI's XAML compiler writes a `Convert.ChangeType` into the app's `obj/**/XamlTypeInfo.g.cs`, which `.editorconfig` leaves out. A method's entry names one overload, with its parameters, and one without them is the overload that takes none: list each, and plant a call to check that a new entry fires. A cast from a `double` is a built-in conversion it can't see, which `DecimalNumbersTests` reads the IL for instead. The `.editorconfig` turns on two code metrics as warnings at the analyzers' own thresholds: CA1502, a method's cyclomatic complexity over 25, and CA1506, a type coupled to over 95 types or a member to over 40 (not in `tests/`). The 22 places over them on 2026-11-20 carry a `[System.Diagnostics.CodeAnalysis.SuppressMessage]` with its reason; a new one is refused by CI's build (`-warnaserror`). Split the code rather than add another, but a composition root (`MauiProgram.CreateMauiApp`, `RecipeApiHost.AddRecipeApi`) couples to what it registers by design. A suppression also counts toward its type's coupling, as `ProductsState`'s did, and one on a type silences every member in it: excuse a type's own coupling with a `#pragma` on its declaration's line, as `ProductForm`'s is. Both see a `.razor` file's code too (since 2026-11-22), as the compiler does, in the files Razor generates from it (`*_razor.g.cs`, the `@code` members' `*_razor.decl.g.cs`): `.editorconfig` marks those files `generated_code = false`, which analyzers otherwise skip, and the root's [.globalconfig](../.globalconfig) gives them their severities, as a generated file takes none from `.editorconfig`'s sections. It turns off there what else that would wake (CA1000, CA1305, CA1816, CA1822, CA1826, CA1861, which flag what Razor writes or a component's shape asks for), and `.editorconfig`'s `[*.cs]` turns them back on for the files on disk. A finding in a `.razor` file is reported at its own line.
- [.editorconfig](../.editorconfig) makes IDE0005 (a using nothing needs) a warning, so every build reports one and CI's (`-warnaserror`) fails on it; it marks `Migrations/` as generated code and Razor's generated files as not (the code metrics, above), turns off CA1707 (underscores in names) in `tests/`, turns off RS0030 in the app's WinUI-generated `XamlTypeInfo.g.cs`, and turns off CA1716 everywhere. IDE0005 reports a run of unneeded usings at its first line only, and an incremental build reports nothing for a project it doesn't compile: after taking one out, rebuild with `--no-incremental` to see the next. The files in `CompiledModels/` carry their own `<auto-generated />` header, so the analyzers skip them too.
- **Line endings are LF, in the repository and in every working copy** (2026-11-22), as GitHub and most tools expect. [.gitattributes](../.gitattributes) (`* text=auto eol=lf`) makes Git check every text file out with LF whatever a PC's `core.autocrlf` says (Git for Windows sets it to `true`, which alone would give CRLF), and the `.editorconfig`'s `end_of_line = lf` makes the editors write new files that way. A file written with CRLF by a tool that ignores both (Python's `write_text` on Windows, Notepad) still commits as LF, but Git warns that "CRLF will be replaced by LF": convert it, or check it out again once it's committed (`git checkout -- <file>`). `git ls-files --eol` shows each file's endings in the index (`i/`) and the working copy (`w/`).
- Warnings aren't treated as errors, but the build is kept free of them.
- The Maui project compiles the Syncfusion licence key in (an `AssemblyMetadata` item from `SYNCFUSION_LICENSE_KEY`), fails a publish without it (`RequireSyncfusionLicenseKey`), and leaves out of its output the Syncfusion files the page doesn't load: every theme but `fluent2-lite.css`, and the all-components script, `syncfusion-blazor.min.js` (`LeaveOutUnusedSyncfusionFiles`).

### Other files at the root

`installer/` holds the MSI's WiX project, outside the solution, built by `scripts/Build-Installer.ps1` (see [The MSI](#command-reference)). `.claude/` holds AI-assistant tooling, which you don't need to build or run the app: the `run-tinapayanrms` skill, whose driver starts and drives the app's window ([Checking a screen in the app's window](#checking-a-screen-in-the-apps-window)). `.mcp.json` tells an AI assistant how to start two MCP servers: this repo's `src/TinapayanRMS.Mcp`, through `scripts/Start-McpServer.ps1` (see [Checking costs from an AI assistant](#checking-costs-from-an-ai-assistant)), and Syncfusion's Blazor documentation server, `syncfusion-blazor` (see [Working with Syncfusion](#working-with-syncfusion)).

### A reading order for the code

1. The entities [RawMaterial](../src/TinapayanRMS.Domain/Entities/RawMaterial.cs), [SubAssembly](../src/TinapayanRMS.Domain/Entities/SubAssembly.cs) and [Product](../src/TinapayanRMS.Domain/Entities/Product.cs). Their XML comments explain the domain.
2. [CostingRules](../src/TinapayanRMS.ApplicationCore/Rules/CostingRules.cs), [ProductRules](../src/TinapayanRMS.ApplicationCore/Rules/ProductRules.cs) and [IngredientCostGraph](../src/TinapayanRMS.ApplicationCore/Rules/IngredientCostGraph.cs).
3. [IngredientCostPropagator](../src/TinapayanRMS.Infrastructure/Services/IngredientCostPropagator.cs).
4. [ProductCategoryRepository](../src/TinapayanRMS.Infrastructure/Repositories/ProductCategoryRepository.cs), the simplest repository; then `SaveAsync` in [RawMaterialRepository](../src/TinapayanRMS.Infrastructure/Repositories/RawMaterialRepository.cs).
5. [MauiProgram.cs](../src/TinapayanRMS.Maui/MauiProgram.cs), [ServiceRegistration.cs](../src/TinapayanRMS.Components/ServiceRegistration.cs) and [MainLayout.razor](../src/TinapayanRMS.Components/Layout/MainLayout.razor).
6. [ScreenState](../src/TinapayanRMS.Presentation/Shared/ScreenState.cs) and [ScreenBase](../src/TinapayanRMS.Components/Shared/ScreenBase.cs), then the smallest complete screens: [LookupListState](../src/TinapayanRMS.Presentation/Settings/LookupListState.cs), which Settings' list tabs share, and one of them, [RawMaterialCategoriesState](../src/TinapayanRMS.Presentation/Settings/RawMaterialCategoriesState.cs) with [RawMaterialCategories.razor](../src/TinapayanRMS.Components/Pages/Settings/RawMaterialCategories.razor).
7. [CollectionMerge](../src/TinapayanRMS.Presentation/Shared/CollectionMerge.cs) and [UndoHistory](../src/TinapayanRMS.Presentation/Shared/UndoHistory.cs).
8. [ReactiveCostCascadeTests](../tests/TinapayanRMS.Infrastructure.Tests/Persistence/ReactiveCostCascadeTests.cs), which runs the whole cascade end to end.

Then move on to the large screens. `ProductForm` (about 1,900 lines, a tab of the Products view, `ProductsState`), `RawMaterialForm` (about 1,450, a tab of the Raw Materials view, `RawMaterialsState`) and `SubAssemblyForm` (about 600, a tab of the Sub-Assemblies view, `SubAssembliesState`; each view keeps its list and the load) are each one class over parts ("Screen state", "A record screen's state is one class over its parts"): the screen keeps what spans its parts, and each part is a class of its own the screen holds as a get-only property, so open the part for what you're changing rather than reading the screen top to bottom. Each screen's list is one ([ProductsList](../src/TinapayanRMS.Presentation/Products/ProductsList.cs), `RawMaterialsList` and `SubAssembliesList`, on [RecordList](../src/TinapayanRMS.Presentation/Trees/RecordList.cs)); Raw Materials' others are `PurchaseCost`, `Units`, `Recipe`, `UsageExample`, `PriceLog` and `Dependents`, and Sub-Assemblies' `Batch`, `PackagingSet` (a packaging set's lines and cost, [SubAssemblyPackagingSet](../src/TinapayanRMS.Presentation/SubAssemblies/SubAssemblyPackagingSet.cs)) and `Dependents`. Products' form view binds its parts: the recipe is [ProductRecipe](../src/TinapayanRMS.Presentation/Products/ProductRecipe.cs) (`Recipe`: the sections and their tabs, which of the recipe's tables shows, and what Save compares the recipe against), the Costing tab [ProductCosting](../src/TinapayanRMS.Presentation/Products/ProductCosting.cs) (`Costing`: the costs, the captions naming what they're for, and the cost basis), the Recipe Structure's Packaging tab [ProductPackaging](../src/TinapayanRMS.Presentation/Products/ProductPackaging.cs) (`Packaging`), a composite's Products table [ProductComponents](../src/TinapayanRMS.Presentation/Products/ProductComponents.cs) (`ComponentsTable`), the add/edit sub-assembly form [ProductUsageForm](../src/TinapayanRMS.Presentation/Products/ProductUsageForm.cs) (`UsageForm`), which works out its own piece size and figures (`UsageFormFigures`) and holds what a line of the section can be, the recipe's other sections among them (`UsageFormSections`), with its choices built by `SectionFormChoices` and its undo state taken by `SectionFormCapture`, the History tab `ProductVersionHistory` (`VersionHistory`), which reads its versions, saves their notes and works out what Restore puts into the form, which the screen puts there, and where Save files a new product, [NewProductPlace](../src/TinapayanRMS.Presentation/Products/NewProductPlace.cs); the view's LoadCommand reads through its queries ([IProductQueries](../src/TinapayanRMS.UseCases/Products/ProductQueries.cs)`.ReadLoadAsync`), for every open form at once (each form's linked sections, packaging link and composite's products). Each owns its tab's commands ("A part owns its tab", below): the Packaging tab's eight, the History tab's Restore, which hands the screen what to put into the form, the section form's own controls (its Remove Lines, a linked section's Edit and Cancel, the piece size an add starts at), and the Products table's Remove Line and Include each product's packaging tick; the Costing tab keeps the shop's wastage, VAT and price rounding. The screen keeps the undo history and the order an undo puts its parts back in, what the section form does to the recipe, and what spans the parts.

---

## 5. Architecture

### Layers

```
┌──────────────────────────── TinapayanRMS.Maui ────────────────────────────┐
│  The window (chromeless), and the web view its screens are drawn in       │
│  MauiProgram; index.html; Platforms/Windows: the frame, dialogs, closing  │
└───────────────┬───────────────────────────────────────────────────────────┘
                │ hosts
┌───────────────▼──────────── TinapayanRMS.Components ──────────────────────┐
│  Pages and shared components (Blazor)  ── render ──►  the screen states   │
│  Their styles and scripts (wwwroot), served at _content/…Components/      │
│  ServiceRegistration (the code that names Infrastructure)                 │
│  The title bar, printing, the Vim keys                                    │
└───────────────┬───────────────────────────────────────────────────────────┘
                │ renders, and runs the commands of
┌───────────────▼──────────── TinapayanRMS.Presentation ────────────────────┐
│  State classes: each screen's state, commands, undo and loads             │
│  Drafts: the record a screen edits, with no I/O (SubAssemblyDraft)        │
└───────────────┬───────────────────────────────────────────────────────────┘
                │ reads and writes the records through
┌───────────────▼──────────── TinapayanRMS.UseCases ────────────────────────┐
│  Each aggregate's editor and queries: transaction, checks, versions       │
│  Every screen's (ADR 0002); no reference to Infrastructure                │
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
│  Repositories; Services (cost propagator, ProductCloner, backups)         │
└───────────────────────────────────────────────────────────────────────────┘
                every layer uses
┌──────────────────────────── TinapayanRMS.Domain ──────────────────────────┐
│  Entities and enums: plain classes with no dependencies                   │
└───────────────────────────────────────────────────────────────────────────┘
```

The layering is fixed:

- **Domain** holds the persisted entities as plain classes with documented properties. It has no dependencies and no behaviour beyond default values.
- **ApplicationCore** holds the rules, the repository contracts and the validators. It references neither EF Core nor Npgsql and knows nothing of the UI, so everything in it can be unit tested without a database. One rule speaks of the database: `Failure` puts its errors into words by their SQLSTATE codes, through .NET's own `DbException`, and knows EF Core's update exceptions by their type's name.
- **Infrastructure** does all the I/O: EF Core for writes, the schema and migrations; Dapper for reads; and the services that work across entities.
- **Pricing** is what a saved product costs, read and worked out one way: `SavedProductReader`, the immutable `SavedProduct` it builds (its `PricedSection`s and `PricedPackagingLine`s), and `ProductCostSummary`, which is worked out from a `ProductCostInputs` record: the screen fills one from the form and the reader from what's saved. A section's batch and piece figures come from `SectionFigures.Of` and a packaging line's cost from `PricedPackagingLine.Price`, for the screens' editable rows and saved products alike, and a row gives its immutable form with `ToPriced()`. A section can be a line of another of its product's (a sponge on the dough's lines), so a section's figures in the recipe depend on the others': `SectionUses.Apply` prices those lines at the used sections' costs per gram and leaves each used section its pieces of what's left, for `SavedProductReader.Build` and the Products screen's `ProductRecipeDraft.Priced()` alike, which is what the costing and printouts read (`ProductUsageEdit.Figures` is a section's own batch alone). A price tried on the Price Simulator is worked out and checked by `TriedPriceChange.Of`, for its Price Changes table and the MCP server's `simulate_price_changes`. A saved product's sections are named as the screen names them, two in one role apart by their labels ("Dough" beside "Dough: Paste"). It knows ApplicationCore's interfaces and rules, and nothing of EF Core, Blazor, MAUI, Syncfusion or the screens' states, so the Recipe API and the MCP server price exactly as the screens do with no UI in them. Everything in it is immutable: the editable rows the screens type into (`ProductUsageEdit`, `PackagingLineRow`, `SimulatedPriceChange`, on `ObservableRow`) are Presentation's, so the servers can't reach them.
- **UseCases** is what the screens do with the records ([ADR 0002](adr/0002-layers-use-cases-and-drafts.md)): each aggregate's editor (its Save with the checks, the transaction and the product versions; Delete, Rename, Move) and the queries its screen reads, over ApplicationCore's interfaces and Pricing. It never references Infrastructure: the composition root (`ServiceRegistration`) hands it the repositories. Sub-Assemblies is the first screen moved to it (`UseCases/SubAssemblies/`), Raw Materials the second (`UseCases/RawMaterials/`), Products the third (`UseCases/Products/`: its queries, its editor, the list's editor and the Category picker's), Settings' list tabs, Costing Defaults and Printing the first of phase 4 (`UseCases/Settings/`: `ILookupListEditor`, which the category and role editors take on too, `UnitOfMeasureEditor`, `ICostingDefaultsEditor` and `IPrintSettingsEditor`, which `PrintPage` reads), the password and the backups its second (`UseCases/Access/`: `IAccessSettingsEditor`, for the sign-in panel and Security; `UseCases/Database/`: `IBackupLibrary`, for the Database tab and `AutomaticBackups`, and `IDatabaseUpdates`, for `DatabaseReadiness`), Quick Find and Reports its third (`UseCases/QuickFind/IQuickFindQueries`, `UseCases/Reports/IReportQueries`, Reports' Re-apply Target Buffer on `IRawMaterialEditor`), the Price Simulator its fourth (`UseCases/PriceSimulator/`: `IPriceSimulatorQueries`, `IPriceScenarioEditor`), and the Print dialog its fifth (`UseCases/Printing/IPrintQueries`), the last screen that held a repository; and Presentation's reference to Infrastructure went last (`Failure` and the stage's former name, `ProductRules.FormerInnovationName`, moved to ApplicationCore), so phase 4, and ADR 0002's migration, is done.
- **Presentation** holds the screen states. Every screen reads and writes through UseCases' interfaces alone (`DataAccessTests`), using ApplicationCore's rules and Pricing's figures, and knows nothing of Blazor, MAUI, HTML or Syncfusion, so a screen state is tested by driving its commands. The screens say what failed in `Failure`'s words.
- **Components** renders the screen states: the pages, the shared components, the title bar, the printouts and the Vim keys, with their styles and scripts (`wwwroot`, served at `_content/TinapayanRMS.Components/`) and the JS initializer that loads Syncfusion's scripts before Blazor starts. It's the whole UI, the shared Razor class library a host draws: a host gives it a page that links its styles and scripts, and implements what only a window can do, an interface here (`IAppWindow`, `IFileDialogs`, `IPagePrinter`), which the Maui project implements and the tests fake. `ServiceRegistration` is the only code that names an Infrastructure type, to register it.
- **Maui** is the app, the screens' host: the window, the web view that draws them, the host page they're drawn in (`wwwroot/index.html`), and the Windows code under `Platforms/Windows/` (`StartupMessage`, Windows' message box for a start that failed or was refused; the chromeless frame, the file dialogs, asking before the window closes, one copy at a time).

Business and costing rules go in ApplicationCore, and EF Core and other I/O stay in Infrastructure. A screen state never sees a `DbContext`, and a page never calls a repository: it binds to its state.

### Startup

Windows starts the WinUI application ([Platforms/Windows/App.xaml.cs](../src/TinapayanRMS.Maui/Platforms/Windows/App.xaml.cs)), which first makes sure it's the only copy on the PC (see [One copy at a time](#one-copy-at-a-time)), then hooks WinUI's `UnhandledException` (logged as fatal: MAUI logs nothing of an exception on the window's thread), then asks [MauiProgram](../src/TinapayanRMS.Maui/MauiProgram.cs) to build the app. `MauiProgram.CreateMauiApp`, in order:

1. **Turns off Syncfusion's telemetry** (`Syncfusion.Telemetry.Telemetry.Disable()`), before anything touches a Syncfusion type. Syncfusion's grid and charts otherwise report which of them the app uses to Syncfusion over HTTPS.
2. **Gives the app its number culture** ([NumberCulture](../src/TinapayanRMS.Components/Shared/NumberCulture.cs)`.Apply`): the Windows region's, with a point before the decimals, commas between thousands and a plain minus whatever the region uses, on the UI thread and as every thread's default. `NumberField` and the Vim keys' sums read a typed number that way, so in a region with a comma before the decimals a box showed `12,50` and read `12,5` as 125.
3. **Builds the Serilog logger**, so even a failure to start is logged, and hooks what fails with nothing to catch it ([UnhandledFailures](../src/TinapayanRMS.Components/UnhandledFailures.cs)`.Watch`: an exception that ends the process, a task that failed with nothing awaiting it, and a command run nothing awaited that failed; [Logging and errors](#logging-and-errors)).
4. **Registers the Syncfusion licence**: the key the build compiled in (the Maui project's `SyncfusionLicenseKey` `AssemblyMetadata` item, taken from `SYNCFUSION_LICENSE_KEY`), or else the variable itself. Without either it logs an error and goes on, and Syncfusion shows a licence notice in the window.
5. **Reads the configuration**: `appsettings.json`, built into the app as a resource, then environment variables over it.
6. **Registers the services**: MAUI's `BlazorWebView` (with its developer tools in a Debug build), Syncfusion Blazor, the app's own ([ServiceRegistration](../src/TinapayanRMS.Components/ServiceRegistration.cs)`.AddTinapayanRMS`: [DatabaseHost](../src/TinapayanRMS.Infrastructure/DatabaseHost.cs)'s DbContext factory and repositories, as the two servers have them, then the services, the view-state store, the validators, the screen states and what the screens share), and the window's own: the file dialogs (`IFileDialogs`) and the frame (`AppWindowChrome`, as `IAppWindow`). It hooks the window's creation and its messages (`WindowsWindow.Configure` and `OnMessage`: chromeless, centred on the screen the mouse is on, and asking about unsaved changes before it closes).
7. **In a Debug build, starts signed in when `TINAPAYANRMS_SIGNED_IN` is `1`**, with no auto-lock, for a check driven by script, which can't know the password ([Signing in](#signing-in)). A Release build, the installed one, has no such path.
8. **Starts the background work** ([BackgroundWork](../src/TinapayanRMS.Maui/Services/BackgroundWork.cs)): the hosted services a web host would have started, which a MAUI app doesn't. [StartupTasks](../src/TinapayanRMS.Components/StartupTasks.cs) first waits for the database to be made ready for this version ([The app updates its own database](#the-app-updates-its-own-database)), which the sign-in panel waits for too, and puts the reason under the title bar if it can't be. It then takes the day's automatic backup if it's due (`AutomaticBackups.RunAsync`, over `IBackupLibrary.BackUpDailyIfDueAsync`, which is `IDatabaseBackups`'), and checks again every hour, so a copy left open over midnight takes the next day's ([Backups](#backups)). Neither holds the app up, and a failure of either is logged.

A failure in any of that is logged as fatal, Windows' own message box says why and where the log is ([StartupMessage](../src/TinapayanRMS.Maui/Platforms/Windows/StartupMessage.cs)), and the app ends: with no window yet, there's nothing to carry on in, and without the message the app just disappeared.

The window is [App](../src/TinapayanRMS.Maui/App.cs)'s, over [MainPage](../src/TinapayanRMS.Maui/MainPage.cs): a `BlazorWebView` filling it, whose page is [wwwroot/index.html](../src/TinapayanRMS.Maui/wwwroot/index.html), with `Routes` in its `#app`. The page links the screens' styles and scripts from their library, under `_content/TinapayanRMS.Components/`. Blazor starts once the page has Syncfusion's scripts for the screens' components: [syncfusion.js](../src/TinapayanRMS.Components/wwwroot/syncfusion.js) loads them as the page is parsed, and the screens' JS initializer ([TinapayanRMS.Components.lib.module.js](../src/TinapayanRMS.Components/wwwroot/TinapayanRMS.Components.lib.module.js)) holds Blazor's start until they're in ([Working with Syncfusion](#working-with-syncfusion)). The web view serves the page and the static files from `https://0.0.0.1/`, with nothing listening on the network. **The page doesn't zoom**: as the web view starts (`BlazorWebViewInitialized`), `MainPage` turns off WebView2's zoom keys and Ctrl+wheel (`IsZoomControlEnabled`) and its pinch zoom (`IsPinchZoomEnabled`). **Nor does it take drops from outside the window**: `MainPage` turns the web view's `AllowDrop` off as it starts, which MAUI 11.0.10 turned on, so a file dragged in from File Explorer is refused rather than opened in its own program. The app's own drags follow the pointer (`app.js`), not the browser's drag and drop. The driver's keys and wheel come in over the debugging protocol, which never zoomed the page anyway, so a check of this reads those two settings back rather than pressing Ctrl+=. As the window closes, `App` stops the background work (a backup in hand gets ten seconds), flushes the log and ends the process ([The shell](#the-shell) says why it doesn't wait).

### One copy at a time

Two copies side by side would each run their own hourly backups, write the same view-state file and save over each other's settings (only the three record forms check [as they save](#writing-safely)). [SingleInstance](../src/TinapayanRMS.Maui/Platforms/Windows/SingleInstance.cs) keeps the app to one copy on the PC, before building anything:

- **Within a Windows sign-in, one copy per folder**, with a named mutex (`Local\TinapayanRMS.Maui-<the folder's hash>`), which Windows frees when a copy crashes or is ended. A second copy brings the open one's window forward (restoring it if it's minimized) and exits. A copy from another folder is another app: one run from source doesn't stop an installed copy, nor the other way round, so the `run-tinapayanrms` checks run beside an installed copy.
- **Across sign-ins, one copy for the PC** ([OneCopyPerPc](../src/TinapayanRMS.Components/OneCopyPerPc.cs)): the app open in another Windows session refuses the start with a message box saying so. Found by its running processes, whose sessions come from the system's process list (no administrator needed), not by a mutex: the installer puts each Windows user's copy in their own folder, so a mutex named after the folder never met another user's. Two sign-ins starting at the very same moment both refuse, and starting again works. The rule is a pure function of the processes, tested in Components.Tests.
- **It doesn't reach another PC.** Another PC on the same database can still open the app. What keeps the two from saving over each other's records is the check a form's Save makes ([Writing safely](#writing-safely)).

### Rendering, scopes and lifetimes

The app is a **Blazor Hybrid** app: the components run in the app's own process, and the web view only draws them, sending events in and taking changes to the page out, with no server and no HTTP between them. They run on the web view's dispatcher, which is the window's UI thread.

- **Screen states are scoped**, and in Blazor Hybrid a scope is a page the web view loads. So the screens keep their selection and unsaved edits from one screen to another and back, until the page is loaded again: after a restore, which loads it afresh (`NavigateTo(…, forceLoad: true)`) so every screen is built anew.
- **Repositories, services and validators are singletons**: `ProductCloner`, `CostRecalculator`, `SavedProductReader`, `ProductVersionRecorder`, `VersionedSaves`, the backups, the view-state store and the logger. They hold nothing but the context factory, or state that is the computer's rather than a page's (`AppStatus`, `AutomaticBackups`).

The DbContext comes from a **factory**, never `AddDbContext`. A screen state outlives any one operation and a repository is a singleton, so neither can hold a `DbContext`: two screens loading at once would share it, and EF Core throws when a second operation starts on a context that's still busy. That happened the first time the app ran. So every repository takes `IDbContextFactory<TinapayanRMSDbContext>` and creates a short-lived context for each call. The factory is [SharedTransactionDbContextFactory](../src/TinapayanRMS.Infrastructure/Services/SharedTransactionDbContextFactory.cs), registered as both `IDbContextFactory<TinapayanRMSDbContext>` and `IUnitOfWork` (see "Unit of work" below):

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

Never inject `TinapayanRMSDbContext` into a repository or a screen state.

**Unit of work.** `IUnitOfWork.RunAsync(work)` runs several repositories' calls as one transaction: it commits when `work` returns and rolls everything back when it throws. While it runs, every context the factory makes on that async flow shares its one connection and its Serializable transaction, whichever repository asks. A repository begins its own transaction with `BeginOrJoinTransactionAsync` ([Transactions](../src/TinapayanRMS.Infrastructure/Services/Transactions.cs)), never `BeginTransactionAsync`, so inside a unit of work it joins that one instead of failing. Three rules follow:

- **Reads inside a unit of work run one at a time.** They share one connection, and Npgsql refuses a second command while one is running. So `SavedProductReader` and `ProductVersionRecorder` read in sequence, never with `Task.WhenAll`.
- **A run may be run more than once.** Serializable means PostgreSQL can cancel a run because a transaction beside it got there first ("could not serialize access", 40001, or a deadlock, 40P01). `RunAsync` then runs the work again from the start, on a new connection, up to three times (`SharedTransactionDbContextFactory.Attempts`), so the work must do nothing but repository calls: no list it adds to outside itself, no message shown. The repositories copy the entities they're given, so a rolled-back try leaves nothing on them. A rule's refusal isn't tried again.
- **Only [VersionedSaves](../src/TinapayanRMS.Pricing/Versions/VersionedSaves.cs) uses it today**, for the saves that record product versions (see "Product versions"). Outside a unit of work, each context has a connection of its own, as before.

**A read of several queries that must agree goes in a snapshot.** `IUnitOfWork.ReadSnapshotAsync(work)` is the same sharing for reads: one connection, in a Repeatable Read transaction set read only, so every query in it reads as of the first, whatever commits meanwhile, and a write in it is refused. Inside a unit of work it joins that one. [SavedProductReader](../src/TinapayanRMS.Pricing/SavedProductReader.cs) reads every product's detail in one: a Save replaces a product's sections with new rows, and one committing between the reader's add-ins query and its sections query left add-ins whose section wasn't there, which failed the whole read (Reports, Print, the Recipe API's `/products/recipes`). So the reader takes an `IUnitOfWork`; in a test, `CreateUnitOfWork()` (`PersistenceTestBase`) is one on the test's database.

[ServiceRegistrationTests](../tests/TinapayanRMS.Components.Tests/ServiceRegistrationTests.cs) resolves every screen's state, so a missing registration fails a test rather than the app. It builds the services with the view state in memory (`InMemoryViewStateStore`) in place of the developer's own `view-state.json`.

A screen state that owns a DynamicData `SourceCache` is `sealed` and `IDisposable`, so that the page's scope disposes the cache when the page goes.

**A screen is drawn afresh on each visit, and its state outlives it.** So whatever a component hooks into something that lasts longer than it does (a screen state's events, lists or commands, the screen state's rows, a scoped or singleton service) it lets go of in its `Dispose`. Otherwise the screen state holds every screen it was ever shown on, each one still answering its changes. `ScreenBase` keeps its own subscriptions, and the ones a page adds with `AddSubscription`, and disposes them all with the page. `FolderTree` and `LineGrid` unhook their rows in `Dispose`. The title bar, Find, the Vim keys and the undo keys dispose their `DotNetObjectReference`, and the JavaScript registrations they made remove their listeners: a page function that watches for a component returns an object whose `dispose()` stops it, which the component's `DisposeAsync` calls through [PageWatches](../src/TinapayanRMS.Components/Shared/PageWatches.cs)`.StopAsync` (nothing to do when there's none, or the page has gone). The registration is kept through `PageWatches.KeepUnlessGoneAsync(() => _disposed)`, with `DisposeAsync` setting `_disposed` first: a component left while the page was still registering it (a quick Back, the Vim keys' `gt`) found nothing to stop, and the undo keys' document listener then outlived it, blurring the field and calling a let-go reference on every Ctrl+Z. Two rules follow:

- **A page lends its state nothing of its own except through `AddSubscription`.** The Price Simulator's state prints the products in the table's sort and filters, which only the page's grid knows. The page lends that view with `ShowProductsFrom`, whose disposable gives it back as the page goes. A lambda set on the screen state instead would keep the page, its grid and the rows it last showed for as long as the window is open.
- **An `SfGrid`'s rows don't notify** (`INotifyPropertyChanged`). Syncfusion lets go of a grid's `DataSource` when the grid goes, but it hooks a row added to that list after the grid bound without keeping the handler, so it can't unhook it. Each such row the screen state keeps would hold the grid.

Blazor itself holds a screen for one more drawing: a component keeps the frames of its last drawing until it draws again, so the layout keeps the screen shown before until the next move.

**Threads.** The components run on one thread at a time, the web view's dispatcher, which is the window's UI thread, and so do the screen states: every click, key and call from the page's script reaches .NET on it, and an `await` in a screen state comes back to it (nothing in the app uses `ConfigureAwait(false)`, which would leave it). A read handed to `Task.Run` touches no screen state: it takes what it needs, read on the UI thread first, and what it reads is applied once the `await` is back ([Screen state](#screen-state), "Several reads at once"). A screen state's classes aren't thread-safe (`ObservableState`, `ScreenCommand`, `Derived<T>`, `UndoHistory` and the collections the screens draw take no lock), so this is what keeps them whole: a change made from another thread could be lost, or change a list while the screen draws it. What does change on other threads is what the window shares with the work in the background (`StartupTasks`, which checks the database and takes the hourly backups on the thread pool): `AppStatus` and `DatabaseReadiness` raise their events on whichever thread made the change, as do `UnhandledFailures.Reported`, raised on the thread pool for a command run nothing awaited, and `AppLock`, which promises no thread either. A component that handles one goes through `InvokeAsync`, or asks its [RedrawGate](../src/TinapayanRMS.Components/Shared/RedrawGate.cs), which does; `ScreenBase` asks its gate for the screen states' redraws too, which a load asks for by the hundred. Nothing on the UI thread may wait on a task synchronously: it would hold up the window.

### How a change travels through the layers

Take a user who types a new price for cake flour on Raw Materials and presses **Save**:

1. **The page.** Each keystroke in the price's `NumberField` sets the row's property on the screen state, which redraws what follows it. The Save button is a `CommandButton` bound to `RawMaterialForm.SaveCommand`, the Save of the tab's form.
2. **The screen state.** `SaveCommand` runs inside `RunSafelyAsync`. It runs the FluentValidation validators for what it's about to save, and stops with an `ErrorMessage` if one fails.
3. **The repository, in one transaction.** `IRawMaterialRepository.SaveAsync(material, units, brandPrices, addIns)` opens one context and one transaction. It saves the material through `RawMaterialRules`' checks, replaces the units and brand prices, and reprices the material at its dearest brand, logging one price history entry if the cost moved. It replaces a made ingredient's add-ins if they were sent, then checks the made ingredient as a whole (`EnsureMadeIngredientFitsAsync`: a sub-assembly it can be made from, add-ins its recipe lacks, no loop).
4. **The cascade, in the same transaction.** `IngredientCostPropagator` recomputes every made or linked ingredient that depends on the flour, in dependency order. Nothing above the ingredients stores a cost: every sub-assembly and product using them is costed from its lines when it's next read. Then the transaction commits. A rule that throws anywhere, or a cascade that fails part-way, rolls all of it back.
5. **The DbContext** stamps `CostUpdatedAt` on every material whose stored cost changed.
6. **Back in the screen state**, `LoadCommand` merges the fresh data into the screen, and `SuccessMessage` becomes "Cake Flour was saved.", which `ScreenBase` shows as a green toast.
7. **The Products screen** stores no costs. The next time its page opens, its `LoadCommand` reads the new sub-assembly costs, and `ProductRules` and `CostingRules` work out the new selling price on read.

### Key decisions

| Decision | Why | Read more in PLAN.md |
|---|---|---|
| The screen states in a project with no UI framework | The costing, undo, merge-on-refresh and tree behaviour are tested without a window; the screens only draw them | [docs/plan/maui-app.md](plan/maui-app.md) |
| .NET MAUI Blazor Hybrid, ported from the web copy of the app | The screens are Blazor components in an app window of their own, run in the app's process, with no server, HTTP or API between them and the rules; the web copy had built and checked them already | [docs/plan/maui-app.md](plan/maui-app.md) |
| A chromeless window, its title bar drawn by the page | The user's choice: the bar is the theme's, and Windows still drags, snaps and maximizes the window from the gaps between the buttons | [docs/plan/maui-app.md](plan/maui-app.md), "The window" |
| Screen states scoped to the page the window loads | A screen keeps its edits while the app runs, and a reload (after a restore) builds every screen afresh | [Application Architecture](../PLAN.md#application-architecture) |
| A DbContext factory, and one short-lived context per operation | screen states are long-lived, and screens load at the same time | [Application Architecture](../PLAN.md#application-architecture) |
| Reads through Dapper, writes through EF Core | Plain SQL reads are fast and explicit. Every write still goes through EF Core, so the cost propagator runs and a duplicate name surfaces as `DbUpdateException`. | [Conventions](../PLAN.md#conventions) |
| Material and sub-assembly costs stored and propagated; product costs computed on read | BOMs nest to any depth, so their costs are worth keeping current. A product's figures are quick to work out from those stored costs, and working them out on every read is simpler than keeping them in step. | [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) |
| A pure ordering function plus a database orchestrator, instead of DynamicData joins | Simpler, testable without a database, and the database stays the single source of truth | [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) |
| Cost-plus pricing, in three separate steps | The shop owner can check each step by hand | Application Architecture, "Cost-plus pricing" |
| `decimal` at full precision, rounded only for display and the final price | Rounding each figure before adding them up drifts | Conventions, "Decimal precision" |
| One Save per screen, in one transaction, with the cost cascade it causes | A rejected part, or a cascade that fails, leaves nothing half-saved and no stored cost stale | Application Architecture, "One Save writes the whole material"; [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) |
| A failure is shown on its screen, and the app carries on | A crash loses unsaved work, and one failed read mustn't stop a screen working | Application Architecture, "A failure is shown on its screen" |
| Screens merge fresh data instead of reloading it | A screen keeps its selection and unsaved edits through a reload | Application Architecture, "Every `LoadCommand` is a fetch-then-merge" |
| The app's own fields, line tables and trees, rather than Syncfusion's inputs, grid editing and tree view | Typing must reach the screen state key by key without a redraw overwriting it, and the screen state already owns a tree's state | [docs/plan/maui-app.md](plan/maui-app.md), "How it's built" |
| Tests on a real database, with nothing mocked | They catch what mocks miss: SQL, constraints and EF Core's behaviour | [Development Environment](../PLAN.md#development-environment) |
| Syncfusion Blazor components over plain HTML controls | The whole UI picks up the theme consistently | [Syncfusion UI Framework](../PLAN.md#syncfusion-ui-framework) |
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

- **It can't write.** Its host is `DatabaseHost`'s with the web server on top (`RecipeApiHost.AddRecipeApi`), on the read-only connection the MCP server uses ([ReadOnlyConnection](../src/TinapayanRMS.Infrastructure/ReadOnlyConnection.cs)): every transaction starts read-only. The installed copy also connects as `tinapayanrms_reader` (`Database:ReadOnlyUser` in its `recipeapi.json`, which `ReadOnlyConnection.Apply` makes the connection's user), a role that can't write at all, so code that turned the read-only transactions off would still be refused. `scripts/New-DatabaseReader.ps1` makes the role, as postgres: a member of PostgreSQL's `pg_read_all_data`, rather than grants on the tables, since a restore rebuilds the database without them (`pg_restore --no-privileges` into a new database, renamed into place) and a migration adds tables. It can read the server's other databases too, and write none. Its password, 24 random bytes in hex, goes into the Windows user's `pgpass.conf` through `psql`'s standard input and is never shown. Running it again gives the role a new password in place of the old. Under `dotnet run`, in the tests and in the MCP server, no reader is set, so they connect as `tinapayanrms`, read-only.
- **It has no UI in it** (2026-11-04). It's on plain `net11.0`, on `Infrastructure` and `Pricing`, so its figures are the screens' without Blazor, MAUI or Syncfusion coming with them: it publishes at about 10 MB, where it was 77 MB. `appsettings.json` is the app's (`src/TinapayanRMS.Maui`), linked into its output.
- **Planning lives in TinapayanBMS** (2026-10-31). TinapayanBMS copies `/ingredients`, `/boms`, `/materials`, `/mixes/details` and `/products/recipes` into its own database and plans from them with its port of what was `ProductionPlanRules` and `ProductionPlanBuilder`; this app's Production Plan screen, its saved plans, Reports' Production Results and `POST /plan` were then taken out (see [The Production Plan](#the-production-plan)). Removing `/plan` and `/plans` from `/api/v1` broke no caller: TinapayanBMS, the only one, had stopped using them.
- **The contract is `Contracts/RecipeContracts.cs`.** The apps that call it keep their own copies of those records, so add to a record rather than renaming or removing a field, or start `/api/v2`. `RecipeApiContractTests` reads each endpoint's JSON as those apps do and lists its fields by name, so a rename fails there: a field added to `/api/v1` is added to its list.
- **Where it listens** is in `recipeapi.json` beside it: Kestrel's `http://*:5290`, every address, so the LAN can reach it. The database is the app's own `appsettings.json`'s.
- **Its key** is `RecipeApi:Key` in the same file, read when it starts. With one set, every request has to send it in an `X-Api-Key` header, from this PC too, or gets a 401. With none set, as in the repo's copy and so under `dotnet run`, only this PC is answered and any other address gets a 403 (2026-11-03: until then no key meant anyone who could reach the port got every cost and price). It's plain HTTP, so a key stops whoever doesn't have it, not someone who can watch the network's traffic: the shop's network is its own, with no guests on it (the user, 2026-11-22, PLAN.md). If that ever changes, serve HTTPS.
- **What goes wrong comes back as `{ error }`** (`RecipeApiHost`): a `BusinessRuleException` as a 400 with its message, an unknown id as a 404, a database that can't be reached, is behind this version or is too slow as a 503 with `Failure`'s sentence for it, and anything else as a 500 with a fixed sentence, the details only in its log, since they can name columns and the like.
- **Installing it**: `scripts/Install-RecipeApi.ps1` publishes it to a temporary folder, so a build that fails leaves the running copy alone, and only then stops that copy and mirrors what was published to `%LocalAppData%\Programs\TinapayanRMS.RecipeApi` (robocopy `/MIR`), since publishing straight into it never takes away what an earlier version needed. The folder's `recipeapi.json` and `logs` are left as they are. It puts a shortcut in the Startup folder that starts it hidden at sign-in, and starts it. A first install also makes the key, 32 random bytes in hex, writes it to the installed `recipeapi.json` and shows it. Installing again keeps the key that's there. `-ReadOnlyUser tinapayanrms_reader` writes `Database:ReadOnlyUser` into the same file, after checking `pgpass.conf` has the role's password, and installing again without it keeps the one set. It runs as the signed-in user, not as a service, so it finds that user's `pgpass.conf`. Run in an administrator PowerShell, it also adds a Windows Firewall rule for port 5290, on private networks and from the local subnet only. `scripts/Uninstall-RecipeApi.ps1` takes it all away again, the key included, so a later install makes a new one. It logs to `logs/tinapayanrms-recipeapi-*.log` beside it.
- **Its connections don't hold up a restore.** A restore can end only its own role's connections, and waits 30 seconds for the others (`DatabaseBackups`). `ReadOnlyConnection.Of` closes a connection left idle after 10 seconds (`IdleLifetimeSeconds`), where a pool's default of 5 minutes would have refused a restore after any recent request read as `tinapayanrms_reader`.
- **Tested by** `RecipeApiTests` and `RecipeApiContractTests`, through the host `Program` builds, served in memory by ASP.NET Core's `TestServer` on the project's own database (`tinapayanrms_test_recipeapi`).

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
| `SubAssembly` | A recipe, or a packaging set | Names are unique. A null `OwnerProductId` means standalone. A product's own keeps its label in that product's recipe in `SectionName`, and its name is made from that, the product's name and the role's. Its batch weight and cost are worked out from its lines on read. It can't be deleted while a product links it. `UpdatedAt` is when it was last saved, null until a save stamps it. |
| `SubAssemblyLine` | An ingredient in a recipe, in grams | Grams are always stored. `AuthoredBakersPercent` records a percentage the user typed instead. |
| `SubAssemblyPackagingLine` | A packaging material in a packaging set | Costed on read. |
| `Product` | A sellable product | Names are unique, deleted products' included. `IsComposite` is fixed once saved. `QuantityRequired` is at least 1, and exactly 1 for a composite. Deleting one only stamps `DeletedAt` (a soft delete): see [Deleted products](#deleted-products). |
| `ProductCategory` | Breads, Cakes, … | A list of its own, separate from raw material categories. Its optional `WastagePercent` prices its products in place of `CostingDefaults.WastagePercent`. |
| `ProductSubAssemblyUsage` | A sub-assembly in a product's recipe | Stores only the typed piece-size figure (`AuthoredField`, `AuthoredValue`). |
| `UsageAddInLine` | A product's extra ingredient on a linked usage | Never changes the sub-assembly that the usage links. |
| `PackagingLine` | A packaging material per sellable unit | Stores both reciprocal quantities and which one was typed (`AuthoredField`). |
| `PackagingLink` | A product's packaging, read from another product or from a set; or (`IsBulk`) its bulk packaging, read from a set | Exactly one target, and no loops (`PackagingLinkRules`). One of each kind a product at most; a bulk one only to a set. |
| `ProductComponent` | A product inside a composite | `Quantity` is at least 1, and a composite never contains itself (`CompositeProductRules.EnsureNoCycle`). |
| `CostingParameters` | A product's five percentages | One per product. Never linked or shared. |
| `ProductVersion` | One version in a product's history | `Number` is unique per product. `Content` is a self-contained JSON snapshot that points at nothing. Goes with the product only on a permanent delete. See [Product versions](#product-versions). |
| `PriceScenario`, `PriceScenarioLine` | A set of Price Simulator price changes saved under a name, and one of its changes | Names are unique, ignoring case; saving under one replaces its lines (`IPriceScenarioRepository.SaveAsync`, which the screen reaches through `IPriceScenarioEditor`). A line stores exactly one of `ChangePercent` and `NewPrice`, whichever was typed (`PriceScenarioRules`, backed by check constraints). Deleting a raw material deletes its lines. Nothing reads a scenario to cost anything. See [The Price Simulator](#the-price-simulator). |
| `AccessSettings` | The app's one password, as a salted hash, and the auto-lock's minutes | At most one row (Id 1). No row, or no hash, is no password yet; the auto-lock is then 15 minutes (`DefaultAutoLockMinutes`). See [Signing in](#signing-in). |
| `PrintSettings` | How printouts are laid out, set in Settings → Printing: the paper (`PaperSize`), which way up (`PaperOrientation`) and the margins (`PageMargins`); the shop's heading (`ShopName`, `ShopAddress`, `Logo` with `LogoContentType`) and `FooterNote`; the type's size (`PrintTypeSize`); and what's left out (`OmitUnitCosts`, `OmitPercentages`, `OmitFigures`, `OmitPriceComparison`) | At most one row (Id 1); no row is A4, portrait, normal margins and type, no heading or note, everything printed. Each choice is stored by name, refused off its list by `PrintRules.EnsureValid` and by a check constraint. A logo is a PNG or JPEG of at most 512 KB, told by its first bytes (`PrintRules.LogoContentTypeOf`), kept with its type or neither (`CK_PrintSettings_LogoWhole`). The `Omit` flags are negatives so a column added to an existing row starts at false, everything printed. Names are trimmed and blanks kept as null (`PrintRules.Tidy`). Named `PaperOrientation`, not `PageOrientation`, which Syncfusion's grids have too. See [Printing](#printing). |
| `CostingDefaults` | The stage and percentages a new product starts with, a new raw material's cost buffer, and the shop-wide `WastagePercent` | At most one row (Id 1). With no row, new products go in Production (`DefaultNewProductStage`), the costing reads as zero, the cost buffer as 10% (`DefaultCostBufferPercent`) and the wastage as 3% (`DefaultWastagePercent`). Unlike the rest, the wastage isn't a starting value: `SavedProductReader` stamps it on every `ProductDetail` it reads, and the Products screen keeps it from each load, so changing it reprices every product. |
| `UnitOfMeasure`, `RawMaterialCategory` | Lookups edited in Settings | A unit's name is stored in lowercase. A category is for one type of material (`RawMaterialCategoryType`): a material goes only in one of its own, and a category's type changes only while everything in it fits the new one. |


### Deleted products

A product is deleted in two steps, so a delete can be taken back. `IProductRepository.DeleteAsync` stamps `Product.DeletedAt` and changes nothing else, which moves the product to the Products list's **Deleted** folder. `RestoreAsync` clears it. `DeletePermanentlyAsync` is the hard delete, with its cascades.

- **Reads leave deleted products out by default.** `GetAllAsync` returns the products in use, and `GetDeletedAsync` returns the deleted ones. `GetByIdAsync` and `GetByIdsAsync` return either, since a holder reads what it holds by id. Every screen except Products reads through `GetAllAsync`, so a deleted product is in no list, picker, report, print, the Price Simulator or plan. There's no global query filter: one would also drop the rows of the "in use" checks below.
- **A deleted product still holds what it uses.** Its usages, packaging lines, links and composite lines stay, so a restore brings it back whole. The `Restrict` keys keep refusing deletes of what it uses. The checks that name products (`SubAssemblyRepository`'s Dependents and refusals, `RawMaterialRepository.GetDependentsAsync`, a composite's role refusals) name it "Cheese Roll (deleted)", through `ProductRules.ListedName`.
- **The checks on what leans on a product run for both deletes.** Neither a soft nor a hard delete goes through while another product, deleted or not, links its packaging or holds it in a composite (`ProductRepository.EnsureNothingLeansOnAsync`). Nothing in use ever leans on a deleted product, so a restore never needs a check.
- **A deleted product can't be newly linked or held.** `PackagingLinkRepository` and `ProductComponentRepository.SetComponentsAsync` refuse it by name. Its name stays taken: Add, Update and Rename refuse it first (`ProductRules.EnsureNameNotHeldByDeleted`), so the message points to the Deleted folder instead of the unique index's "That name is already in use."
- **On the Products screen**, `ProductsState.DeletedProducts` feeds `ProductTree.DeletedFolder` and nothing else. A deleted row (`ProductLeafNode.IsDeleted`) is picked the way a folder is: the form doesn't open it. Its menu has Restore and Delete Permanently, and `ProductTree.MoveTo` refuses it as a drag source or a drop target. `OpenProduct` on a deleted id picks its row in the Deleted folder (`ProductsState.ShowDeleted`), and opens no tab.

### Product versions

Every save that changes a product's recipe or price records a `ProductVersion`, which the History tab shows and can restore: the product's own Save on the Products screen, and a save elsewhere that reaches it (a sub-assembly it uses, or another product it shares lines or packaging with). So does making it as a clone or a variant.

- **A version is a snapshot, not a set of rows.** [ProductVersionContent](../src/TinapayanRMS.ApplicationCore/Interfaces/ProductVersionContent.cs) holds the sections with their lines and add-ins, the packaging lines as they showed (resolved through any link), a composite's products with what each brought, the costing percentages, the price, and every material used with its costs at that moment. It's stored as JSON (`ToJson`/`FromJson`, through the source-generated `ProductVersionJsonContext`: enums by name, nulls left out; with a `Schema` number for a later change of shape), and its ids are only for Restore and for pairing sections, so deleting anything later leaves old versions readable.
- **A save and its versions are written together or not at all.** Products are priced above Infrastructure (`SavedProductReader.Build`, in Pricing), so the snapshot is taken in Pricing too, by [VersionedSaves](../src/TinapayanRMS.Pricing/Versions/VersionedSaves.cs), in one unit of work with the save (`IUnitOfWork`). `SaveProductAsync` calls `ProductRepository.SaveAsync`, reads the product's `ProductDetail` back inside the same transaction, turns it into the content with [ProductVersionSnapshot](../src/TinapayanRMS.Pricing/Versions/ProductVersionSnapshot.cs)`.From`, and writes it with `IProductVersionRepository.AddAsync`. A version that can't be written takes the Save back with it, and the screen shows the error. `SaveSubAssemblyAsync` (the Sub-Assemblies screen's Save) and `MakeProductFromAsync` (Clone and Create Variant) work the same way. The screen then shows the product and detail the Save read back (`ShowSavedProduct`), without reading them again.
- **"Changed" has one definition.** `AddAsync` records nothing when `ProductVersionRules.IsSameRecipe` says the content matches the latest, and that is defined as `ProductVersionRules.Changes` finding nothing to list. So a version never appears with an empty list of changes, and a rename, a description, or a material's cost never makes one.
- **The first Save of a product with no versions** first records the product as it stood (`VersionedSaves`, `ProductVersionRules.BaselineNote`), so there's something to go back to.
- **A save also records the other products it changes.** [ProductVersionRecorder](../src/TinapayanRMS.Pricing/Versions/ProductVersionRecorder.cs) (a DI singleton, as `SavedProductReader` and `VersionedSaves` are, which the Products use cases are built on) runs on both sides of a save:
  - `CaptureAsync` before it. `IProductRepository.GetProductsReadingAsync` finds every product that reads through the saved sub-assemblies or product: those using the sub-assembly, those whose packaging links it as a set, and, through packaging links however deep, those whose packaging links one of them. Each is snapshotted as it's saved.
  - `RecordChangesAsync` after it. Each is read again, and one whose recipe changed gets a version, noted with where the change came from. A product with no versions gets its baseline first. They're all written in one transaction by `IProductVersionRepository.AddRangeAsync`, which reads every product's latest version in one query. The number of commands doesn't grow with the products.
  - Sub-Assemblies' Save passes the sub-assembly. The Products screen's Save passes the product, whose own sections bring in the products sharing them; the product itself is left out, since its own Save records it. It captures only when it writes something another product reads: a linked section's lines (`AreLinkedLinesEdited`), or the packaging lines or link. A Save of the product's own figures, costing or own sections can't change another product, so it reads none of them.
  - Reading or recording can't stop or undo the save: a failure shows after it.
  - `RecordedMessage` names the products in the success notice.
  - The view's `LoadCommand` reads each form's showing History tab's versions again, and clears a hidden one's (`ProductForm.EndLoadAsync`), so a version recorded from another screen shows.
- **A clone or a variant gets its first version as it's made**, and opens in a kept tab of its own. `ProductsState.MakeFromAsync` asks `IProductEditor.MakeFromAsync`, whose `VersionedSaves.MakeProductFromAsync` calls `ProductVersionRecorder.RecordMadeFromAsync`, noted `ProductEditor.ClonedFrom` or `VariantOf` plus the source's name. The source's latest version number goes in when its snapshot `IsSameRecipe` as that version. Its first Save then has a history, so it records no baseline.
- **Figures are worked out on read, by the screen's own code.** `ProductVersionSnapshot.ToDetail` rebuilds a `ProductDetail` from the content and `SavedProductReader.Build` prices it: with its own materials for Then, and with the screen's current materials (`materialsById`, the Price Simulator's way in) and each held product's cost now (`ReadComponentCostsAsync`) for Today.
- **The History tab reads lazily.** [ProductVersionHistory](../src/TinapayanRMS.Presentation/Products/ProductVersionHistory.cs) holds the rows, the picked one and its changes. `ProductForm.RequestVersions` pushes the product to a `LoadLatest` only while `HistoryTab` shows, once per product, and again after a Save.
  - **Compare With** is a list of `VersionComparison` records: `Previous`, `Current`, then one per version (`With(row)`). They're equal by value, so the choice picked is found again in the list `Show` builds for a new read, and goes back to `Previous` for another product. The picker binds `SelectedItem`, and `CompareWith` ignores the null it sends while its list is replaced. Two versions are always compared from the older to the newer.
  - **Cost Over Time** is an `SfChart` of `Trend`: one `VersionTrendPoint` per version, oldest first, with each one's Then figures as doubles. A missing current price is `NaN`, which the chart draws as a gap. It's rebuilt by `Show`, `Reprice` and `Clear`. Its three series colours are set on the chart in [History.razor](../src/TinapayanRMS.Components/Pages/Products/History.razor): the dataviz palette's first three hues, checked with its validator against the card's colour.
- **Print shows the version picked while the History tab shows**: `PreparePrintout` returns `VersionHistory.PrintoutOf(row, product)`, the version's `ProductDetail` built and priced at its costs then, through `SavedProduct.ToPrintout`, titled with the version and footed with `ProductPrintout.VersionNote`. The form isn't applied, since the pages aren't the form's. The tab's Print Version button is the same `PrintCommand`.
- **Restore is an undo step.** The History tab's `RestoreCommand` (`ProductVersionHistory.RestoreAsync`) reads what the version links as it is now and builds a `RestoredVersion`; the screen applies it through `RestoreSnapshot` inside `AsUndoStep` (`IProductVersionHistoryHost.ApplyVersion`), so Save writes it and Undo takes it back, with no restore path of its own. A linked section is linked again with its current lines (Save never writes a version's lines into a shared sub-assembly), or, when the product owns that sub-assembly now (its role made product-owned since), comes back as the product's own with its current lines, as a load turns it round (`VersionSections`, which builds the sections). Anything it needs that's gone is named, and nothing changes.

### Where rules are enforced

Three layers check the same rules, each for a different reason.

| Layer | Where | What it gives | On failure |
|---|---|---|---|
| Form pre-checks | FluentValidation validators in `ApplicationCore/Validators`, which a screen state runs before it saves. A check a repository enforces is the rule itself, run through `RuleValidation.Satisfies`; only a check with no rule behind it (a pick the form must make) is written in the validator | An immediate message, in the rule's own words | `ErrorMessage` is set, and nothing reaches the repository |
| Business rules (the authority) | Static `*Rules` methods, which the repositories call before they write anything destructive | The real guarantee, whichever code path writes | A `BusinessRuleException`, whose message `RunSafelyAsync` shows exactly as written |
| The database (a backstop) | Unique indexes, check constraints and `Restrict` foreign keys, in `Infrastructure/Configurations` | Integrity even if a code path forgets a check | A `DbUpdateException` or a `PostgresException`, which [Failure](../src/TinapayanRMS.ApplicationCore/Rules/Failure.cs) puts plainly by PostgreSQL's error code (SQLSTATE): "That name is already in use.", "This item is currently in use elsewhere and can't be deleted.", and so on |

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
| Usage cost per piece | Batch cost × weight per piece ÷ batch weight; 0 while the batch weighs nothing or has no piece size (`ProductRules.HasPieces`) | `ProductRules.CalculateUsageCostPerPiece` |
| Packaging line cost | Cost per base unit × the quantity in base units | `ProductRules.CalculatePackagingLineCost` |
| Material cost per sellable unit | Σ usage costs per piece × Quantity Required, + Σ packaging | `ProductRules.CalculateMaterialCost` |
| Wastage, labor, overhead, administrative | Each is its % of the sub-assembly cost alone, never of the packaging. The Wastage % is the product's category's (`ProductCategory.WastagePercent`, through `IProductRepository.GetCategoryWastageForProductsAsync` in `SavedProductReader`, and `EditingCategory` on the Products form) or else the shop's (`CostingDefaults.WastagePercent`); a composite's products bring their cost before theirs, so it's added once, at the composite's own | `CostingRules.CalculateCostFromSubAssemblyPercent` |
| Selling price | Base cost = sub-assemblies + wastage + packaging + labor + overhead + administrative; pre-VAT price = base cost × (1 + markup %); selling price = pre-VAT price × (1 + VAT %) | `CostingRules.CalculateSellingPrice` |
| Markup at the current price | (current price ÷ (1 + VAT %) − base cost) ÷ base cost | `CostingRules.CalculateMarkupPercentFromPrice` |
| Price to keep the markup (Price Simulator) | current price × simulated base cost ÷ base cost (VAT cancels out) | `PriceChangeRules.PriceToKeepMarkup` |
| A composite's line | The product's recipe cost per sellable unit × quantity, plus its packaging cost when the composite includes it | `CompositeProductRules.CalculateComponentCost`, `CalculateComponentPackagingCost` |

A made or linked ingredient's costs are worked out by **`IngredientCostRules.Recompute`**, which the cascade (`IngredientCostPropagator`) stores and the Price Simulator uses without storing (`IngredientCostRules.Reprice`: copies of the changed materials and of every ingredient depending on them, in dependency order, `IngredientCostGraph.GetDependentsInOrder`). A simulated price change moves a material's cost and buffered cost by the same factor (`PriceChangeRules.CostsAfter`).

[SavedProductReader](../src/TinapayanRMS.Pricing/SavedProductReader.cs) is the one place where a saved product is read and priced. The Products screen, Reports, Print, the Recipe API and the MCP server all go through it, so they always agree. It reads any number of products together (`ReadDetailsAsync`, `ReadAllAsync`) in about fifteen queries however many there are, on the repositories' plural reads: packaging link chains are followed in memory, and the products a composite holds are read with it and costed bottom-up. For the Price Simulator, `Build` takes substitute materials (by id) for its sections and packaging, and `PricedAt` prices a composite's products again with them, however deep. [PriceChangeSimulation](../src/TinapayanRMS.Pricing/PriceChangeSimulation.cs)`.Run` puts the two together with `IngredientCostRules.Reprice` and `ReachedBy`: every product priced with a set of price changes, and what each change reaches. The Price Simulator screen and the MCP server's `simulate_price_changes` both go through it, so they can't price a change differently.

### Precision and rounding

- Every cost is a `decimal` at full precision through the whole cascade. Round only for display, and for the final selling price.
- `TinapayanRMSDbContext.ConfigureConventions` makes every `decimal` column `numeric(18,4)`, so a new column can't fall back to Npgsql's unconstrained `numeric`, which would keep whatever scale a division left. The two costs per base unit are `numeric(28,20)`, so a buffered cost typed per bag comes back exactly as typed after it's divided down to the gram.
- A value that goes through a `numeric(18,4)` column comes back rounded to 4 places: 1/6 becomes 0.1667. A test that reads a value back from the database asserts the rounded value.
- A stored figure worked out in memory is set at its column's scale, after it's worked out at full precision: a cost per base unit through `RawMaterialRules.StoredCost` (20 places), rounded half away from zero as PostgreSQL rounds a `numeric`. At full precision, a cost that doesn't come out even (170 ÷ 240) never equals the stored one, so a Save that changed nothing would read it as changed, log a price again and move the cost's date.
- On screen: 2 decimals for the Products screen's per-piece costing figures and for prices, and 4 for a cost per base unit and for a recipe tab's cost per piece. Grid quantities show 1 decimal and accept up to 4. No amount shows a currency symbol.
- **Every rounding names its midpoint mode.** `Math.Round(value, places)` takes a half to the even digit, where a shown number and a stored cost go away from zero, so write the mode every time. A figure compared with what's shown (a flag such as `ProductCostSummary.IsBelowTargetMarkup` or `RawMaterialRules.IsOffTarget`) uses `MidpointRounding.AwayFromZero`, so a markup that shows its target is never flagged short of it. One that feeds a typed or saved figure (a piece size, grams from a baker's %, a standard unit's multiplier, a typed amount's percent, the buffer boxes) says `MidpointRounding.ToEven`, the mode it always had, so nothing saved moves. [RoundingModeTests](../tests/TinapayanRMS.Components.Tests/Architecture/RoundingModeTests.cs) reads every method's IL in Domain, ApplicationCore, Infrastructure, Pricing, UseCases and Presentation and fails on a rounding that names no mode.
- **A `double` never becomes a `decimal` by a plain conversion.** Since .NET 11 a cast, `Convert.ToDecimal` or `new decimal(double)` keeps the double's exact binary value, so 28.35 becomes 28.349999999999997868371792719, where .NET 10 rounded it to 15 digits. A typed figure never is one: `NumberField` reads its text as a `decimal`, so 28.35 typed is 28.35. Anywhere else, write the value as a `decimal` from the start, or go through its text.
- **Nothing below the screens is a `double` or a `float`, and two guards keep it so.** `NumberField` throws as it's drawn when bound to anything but a `decimal`, `decimal?`, `int` or `int?`. [DecimalNumbersTests](../tests/TinapayanRMS.Components.Tests/Architecture/DecimalNumbersTests.cs) scans Domain, ApplicationCore, Infrastructure, Pricing, UseCases and Presentation for a `double`, `float` or `Half`, however wrapped (`double?`, `double[]`, `IReadOnlyList<double>`), in a field, property, parameter, return value or local variable, and lists each one it finds; it also reads their methods' IL, Components', and the app's and the two servers' (from their builds' files, `UnreferencedAssemblies`), for one turned into a `decimal` by a cast or `decimal.CreateChecked` and its kin. The scan's one exception, `MeantForDoubles`, is `VersionTrendPoint`: the History tab's `SfChart` plots doubles, so its points turn the decimals into doubles as they're drawn. A double is right only where a number leaves for something that takes nothing else (that chart, an Excel cell in `ExcelWorkbook`): name it there, with why. The screens' own pixels and timings (a menu's place, the window's opening size) are in Components and Maui, which the scan leaves alone.

### Buffered and unbuffered costs

Everything is costed at the buffered cost. The Products screen's **Unbuffered** switch (`CostBasis.Unbuffered`) prices what it shows and prints from the true cost instead. It changes only which cost is read (`RawMaterialRules.CostPerBaseUnitAt`). Nothing stored changes, and the propagators don't know the switch exists.

The Price Simulator has the same switch (`PriceSimulatorState.CostBasis`, `ToggleCostBasisCommand`). It prices every product as saved at both bases on load (`Data.NowBuffered`, `NowUnbuffered`), so a switch only runs the recalculation again at the other basis and reads nothing. `SavedProductReader.PricedAt` already gives a composite's products both costs, so `Build(product, detail, basis, repriced)` picks the right one. The report's printouts carry the basis, so `PrintDocument` marks them "Unbuffered costs" as it does for Products. The switch is session-only and isn't part of a scenario.

---

## 8. Data access

### Reads through Dapper, writes through EF Core

Every public repository read is plain SQL run by Dapper. It returns the same entity objects, with the same navigation properties filled in, that EF Core's `Include` chains used to return. Everything else goes through EF Core: every write, the schema and every migration, and any read inside a save that loads an entity in order to change it. The exceptions are reads that share an EF Core query with a save's own check, so that what a screen shows and what a save refuses can't disagree: a made ingredient's BOM (`IngredientBoms`, below) and `SubAssemblyRoleRepository.CountOwnershipChangesAsync`, which counts what `SubAssemblyRepository.SingleUse` would have a role change adopt.

A read, from [ProductCategoryRepository](../src/TinapayanRMS.Infrastructure/Repositories/ProductCategoryRepository.cs):

```csharp
public Task<IReadOnlyList<ProductCategory>> GetAllAsync(CancellationToken cancellationToken) =>
    dbContextFactory.QueryAsync<ProductCategory>(
        $"SELECT {Columns.Of<ProductCategory>("c")} FROM product_categories c ORDER BY c.name", null, cancellationToken);
```

The read helpers are in `Infrastructure/Queries`:

- **[DapperReads](../src/TinapayanRMS.Infrastructure/Queries/DapperReads.cs)** borrows a DbContext's connection, so reads use the same connection string as EF Core. `QueryAsync` and `QuerySingleOrDefaultAsync` run either on the factory, in a context of their own, or on a save's context. On a context that holds a transaction, a read joins that transaction (`DapperReads.Command`). `ReadAsync` gives a read that needs several queries, such as a multi-mapped join, a context of its own.
- **[Columns](../src/TinapayanRMS.Infrastructure/Queries/Columns.cs)** lists every entity's columns by their snake_case names, `id` first, because a multi-mapped join splits on `Id` (Dapper matches it without regard to case). Dapper maps `cost_per_base_unit` back to `CostPerBaseUnit`, since `DapperReads` turns on `MatchNamesWithUnderscores`. `Columns.Of<T>(alias)` writes the SELECT list.
- **`RawMaterialReads` and `SubAssemblyReads`** load the shared graphs: materials with their category, base unit and units, alone or as the material of a product's packaging line or a packaging set's (one query reads both kinds of line, `ReadLinesAsync`, so a column or join they need is added once); sub-assemblies with their role and owner.
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
- **A recursive query over links people make stops at a loop.** The app refuses a loop of made and linked ingredients, composites or packaging links, but one made outside it (a hand edit, say) would run until the command timed out. Put `CYCLE <column> SET is_cycle USING path` after the recursive CTE, as [RawMaterialRepository](../src/TinapayanRMS.Infrastructure/Repositories/RawMaterialRepository.cs)'s `DependentsSql` does, and test it with a loop put in past the repository, as `RawMaterialDependentsPersistenceTests` does. `UNION` in place of `UNION ALL` ends a loop too, but PostgreSQL sizes its hash table from the planner's row estimate, which made Dependents about 20 ms slower.
- Dapper maps a `date` column to `DateOnly`, and an enum stored as a string back to the enum, on its own.

Never write through Dapper. A Dapper write would skip the cost propagator and the `CostUpdatedAt` stamp, leaving stored costs stale.

### Writing safely

- **Update by loading, then applying.** An `UpdateAsync` loads the tracked entity, copies across only the fields a user can edit, and saves. Never call `dbContext.Update(incoming)` on an object the caller built: it marks every property as modified, and silently overwrites the stored costs. If an edited field feeds a stored figure (a unit's multiplier feeds the cost), the same method recomputes that figure.
- **Delete with `ExecuteDeleteAsync`.** It runs at once, outside the change tracker, so a foreign key's refusal arrives as a `PostgresException`, not a `DbUpdateException`. `RunSafelyAsync` shows it as "This item is currently in use elsewhere and can't be deleted." A test of it expects `PostgresException`. It also runs inside the context's transaction when there is one, so a set of lines replaced with it commits or rolls back with the rest.
- **Replace a set of lines whole.** The `SetLinesAsync` methods first check every incoming row, and every record the rows refer to. Only then do they delete the old rows with `ExecuteDeleteAsync`, insert the new ones with `SortOrder` set to each row's position, recompute any totals and save. A rejected set leaves the old lines untouched. The set a method replaces must match exactly what the UI edits as one unit.
- **One Save, one transaction.** `RawMaterialRepository.SaveAsync` and `ProductRepository.SaveAsync` write everything a screen's form holds in one context and one transaction. Each step is an `internal static` method that takes the context, so steps from several repositories compose. Each step saves as it goes, so the checks of later steps see what earlier ones wrote. A part passed as `null` means "leave it as saved". The caller's entity is copied, never attached, so a rolled-back save never leaves an id on it. The cost cascade runs once, at the end, on the same context and in the same transaction, so it commits with the save or not at all.
- **Save inputs are immutable records:** `RecipeDraft`, `NewSubAssemblyDraft`, `LinkedUsageDraft`, `PackagingLinkDraft`, `BulkPackagingDraft`, `ProductMove`, `ProductFolderMove`, `StageMove`, `RawMaterialMove` and the `RecipeLine` record struct, all in `ApplicationCore/Interfaces`. Where a section's sub-assembly goes (`NewSubAssemblyDraft.Placement`, a `SectionPlacement`) and what the packaging links (`PackagingLinkDraft`) are closed records, one case for each thing Save can do.
- **Writes take turns.** `BeginOrJoinTransactionAsync` takes the writes' lock (`pg_advisory_xact_lock(Transactions.WritesLockKey)`) as a transaction begins, and holds it until the transaction ends. A unit of work that writes (`SharedTransactionDbContextFactory.RunAsync`) takes it before its transaction begins, as a session lock let go once the transaction ends, since PostgreSQL takes a Serializable transaction's snapshot at its first statement: taken with its first write, after its reads, its snapshot could predate the writer it waited behind (a read-only run, `ReadSnapshotAsync`, takes none). So two writes from two connections (two PCs, or Recalculate Costs beside a save) never cascade side by side, where the one committing last could store a made ingredient's cost worked out from the other's old one. Begin every write that can change a cost through it. A write to a table the cascade never reads (a category, a unit of measure, the costing defaults) may save without it. [CostCascadeConcurrencyPersistenceTests](../tests/TinapayanRMS.Infrastructure.Tests/Persistence/CostCascadeConcurrencyPersistenceTests.cs) holds one save at its commit while another runs.
- **A form's Save says when the record it opened was last saved.** Products, raw materials and sub-assemblies are edited at length on a form, so another PC can save one meanwhile. The form keeps the record's `UpdatedAt` as it took it (`RecordScreenState.SavedVersion`), its Save sends that time as the record's `UpdatedAt`, and the repository's update compares it with what's stored ([SavedVersionRules](../src/TinapayanRMS.ApplicationCore/Rules/SavedVersionRules.cs)), after the writes' lock, so nothing lands between the check and the write. A later save refuses this one with `SavedSinceException`, a `BusinessRuleException` the screen shows as written. `UpdatedAt` is the token because only a save of the record itself moves it: PostgreSQL's `xmin` also moves with every cost the cascade stores, and would refuse a form nobody else had touched. So a new write to one of these records stamps `UpdatedAt` with `dbContext.LocalNow()` (a write to what another record's Save writes whole is one: a variant's Save relining its source's own section stamps the source, `ProductSubAssemblyUsageRepository.StampOwnersOfRelinedAsync`), whole microseconds (`ToStoredPrecision`), what PostgreSQL keeps, so a time stamped is the time read back. A saved record no save has stamped yet is sent as `SavedVersionRules.NeverSaved`, so the first save stamped on it since refuses this one. A rename or a move from a list takes the `SavedVersionCheck`s of the tabs holding its records with unsaved changes (`IRecordListHost.SavedVersionsFor(ids)`, each form's own for its record; with no ids, every tab's, for a folder's move, whose products aren't known before it), and checks each record it writes against its own check, inside the write's own transaction (`SavedVersionRules.EnsureNotSavedSince`); a record no tab holds with changes isn't checked. A save that sends no time isn't checked: a new record, or a caller that isn't a form. A product's Save also writes a linked sub-assembly's lines when they're changed in its recipe: it sends the lines they were changed from (`LinkedUsageDraft.LinesTaken`), and is refused when those stored are others (`SavedVersionRules.EnsureLinesNotSavedSince`). By the lines, as they're all it writes there: a rename since refuses nobody. Nor does a saved time show a role's Product-owned changed in Settings, which moves a sub-assembly between a product and the Sub-Assemblies screen: the recipe says which were the product's own as the form took them (`RecipeDraft.OwnTaken`), and its save is refused when whose one is has changed under it (`ProductSubAssemblyUsageRepository.EnsureOwnershipNotChangedSinceAsync`). The screen's side is in [Editing, undo and Save](#editing-undo-and-save). Tests: [SavedVersionPersistenceTests](../tests/TinapayanRMS.Infrastructure.Tests/Persistence/SavedVersionPersistenceTests.cs), and each record screen's `…StateSavedVersionTests`.
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
- **`IngredientDependencies`**: loads the ingredient dependency graph as stored, BOM edges (`IngredientBoms`: sub-assembly lines and add-ins) and link edges, through `IngredientCostRules.Dependencies`, the cascade's own definition, with every material's name (`IngredientDependencyGraph`), for the cycle checks. `EnsureNoCycleAsync` checks the made ingredients a save changed against one read of it, and a refusal names the two ingredients in the loop.
- **`ProductCloner`** (`IProductCloner`): Clone and Create Variant. A clone links the sub-assemblies the source links, copies the source's own ones and copies the packaging. A variant links every sub-assembly the source uses, the ones it links and its own alike, and has no packaging. It's the only way a product links another product's own sub-assembly. `SetRecipeAsync` takes `linksOthersOwn` for it, and otherwise refuses such a link (`SubAssemblyRules.EnsureLinkable`) unless the product already has it. While a variant links one, the source can't drop it from its recipe or be deleted. On the Products screen, `ProductUsageEdit.LinkedOwner` names the source, and the linked badge (`OpenLinkedSourceCommand`) opens it.
- **`JsonViewStateStore`** (`IViewStateStore`): the view-state file. A missing or broken file reads as empty, and a failed write is dropped.
- **`DatabaseBackups`** (`IDatabaseBackups`): Settings' Database tab and the daily automatic backup, taken by PostgreSQL's own tools through [PostgresTools](../src/TinapayanRMS.Infrastructure/Services/PostgresTools.cs). Each tool runs through .NET 11's `Process.RunAndCaptureTextAsync`, which reads both its pipes as it runs: one that fails throws `PostgresToolException` (ApplicationCore's `Rules`, beside its other exceptions) with what it wrote to standard error, and one that's cancelled is stopped. Every run gets `--no-password`, since with no password available a tool otherwise waits at a prompt on a console nobody can see, and one still running after half an hour (`PostgresTools.DefaultTimeout`) is stopped and throws `TimeoutException`, so no tool can hold up the backups, restores and copies waiting their turn behind it. A backup is `pg_dump --format=custom` into a `.backup.partial` file (`BackupRules.PartialSuffix`), which takes its `.backup` name only once `pg_dump` has finished, so a run cut short is never listed, counted as the day's backup or restored; one left behind is deleted after a day. A copy in or out (`CopyAsync`) goes the same way, so one cut short leaves the file it would have replaced as it was. Backups, restores and copies out take turns (a `SemaphoreSlim`), since the day's automatic backup can start while Settings is taking one, and its copy to the second folder while Settings copies one there: two copies to one file at once fought over its partial file, and the one that lost said the copy hadn't gone through. A copy in needs no turn, as its name (`CopiedIn`) is one nothing else writes. A restore reads the file first with `pg_restore --list` (`BackupRules.TryReadArchiveListing`): a file that isn't a backup, or is from a newer PostgreSQL, is refused before anything changes. It then takes a safety backup and restores into a scratch database, `<database>_restoring` (`--no-owner --no-privileges --exit-on-error --single-transaction`). There it brings an older backup up to date with `MigrateAsync`, and refuses one from a later version, or of another app (`BackupRules.UpdatesNeeded`). Only then does it swap the scratch database in: it closes the database to new connections, ends the app's own, renames the database to `<database>_replaced` (`DatabaseBackups.ReplacedSuffix`) and the scratch database to its name, and drops the one it replaced. So a backup that's refused or fails part-way never touches the data. The window can't be closed while a restore runs (`IBusyWork`), but the app can still end between the two renames (a power cut), leaving nothing under the database's name: `RecoverInterruptedRestoreAsync`, the first thing `StartupTasks` does, renames `<database>_replaced` back and opens it to connections again when the database itself is missing, and does nothing otherwise. The scratch database it leaves is dropped by the next restore, as it starts. Every statement runs on its own unpooled connection to the `postgres` maintenance database. A new backup or a file copied in never reuses a name in the folder: names go to the second, so `BackupRules.FreeFileNameFor` moves the time on a second while the name is taken. The app then restarts, since every singleton screen state holds data from before.

### Schema conventions

- There's one `IEntityTypeConfiguration<T>` per entity in `Configurations/`, and `OnModelCreating` applies them all from the assembly.
- Every table is its `DbSet`'s name in snake_case, and every column its property's.
- Every name a user chooses (materials, categories, products, sub-assemblies, roles, units) has a unique index. Every text column has the `case_insensitive` ICU collation (`TinapayanRMSDbContext.CaseInsensitiveCollation`, set in `ConfigureConventions`), so "flour" is a duplicate of "Flour" and names sort without regard to case, as they did on SQL Server.
- That collation counts a space at the end of a name, so a name goes in without spaces at either end: `ProductRules.StoredName`, `RawMaterialRules.StoredName` and `UnitOfMeasureRules` in their repositories, `.Trim()` in the other forms' states. A new kind of named record needs the same, or `"Flour "` gets past the unique index beside `"Flour"`.
- Enums are stored as strings (`HasConversion<string>()`), so the data reads clearly and Dapper maps them back by name.
- Owned children cascade on delete (lines, units, prices, costing parameters). References are `Restrict`, so a material that a line uses can't be deleted. A product's category is `SetNull`.
- Check constraints back up the rules. They're named `CK_<Table>_<Rule>`. A figure a read divides by or adds up has one (`CK_PackagingLines_AuthoredQuantity`, `CK_RawMaterialUnits_BaseUnitsPerUnit`, `CK_CostingParameters_Percents`, `CK_RawMaterials_WeightLossPercent`, the `_Grams` ones): `SavedProductReader.ReadAllAsync` prices every product together, so one bad row would otherwise fail Reports and the Recipe API for all of them.
- Every line table has a `SortOrder`, because users drag lines into order.
- Where the CLR default is a meaningful value, a column default is paired with `HasSentinel`, as on `Product.QuantityRequired` and `Product.Stage`.
- **An enum stored by name keeps reading a name it's given up.** Renaming a member changes what's stored, so the rename comes with a data upgrade that rewrites the stored name (in SQL, as EF's own queries look for the new one), and a converter that still reads the old name (`ProductStageConverter`), for what a copy of the app or a server built before the rename writes until it's updated. Dapper reads the same columns, so it needs the same: a type handler (`ProductStageTypeHandler`, which `DapperReads` registers with `SqlMapper.Settings.PreferTypeHandlersForEnums` on, as Dapper otherwise parses an enum itself). Anything else kept by the name needs it too: the Products list's folders were kept closed by keys made from it, which `ProductFolderNode.CurrentFolderKey` reads as their new keys. A copy built before the rename can't read the new name, so it fails on a product in Innovation: update every copy on a database together, and take off any that won't be. The model doesn't change, so there's no migration.
- Every `DateTime` column is `timestamp without time zone` (`ConfigureConventions`): the app stamps the shop's local time (`LocalNow()`), and Npgsql's default, `timestamptz`, takes only UTC. PostgreSQL keeps a time to the microsecond.
- On a unique index over a nullable column, PostgreSQL lets null rows repeat. Add `.AreNullsDistinct(false)` if null rows must collide too.

### Migrations

Migrations come only from `dotnet ef migrations add`. Never write or edit one by hand. (The SQL Server migrations, some with hand-added data-conversion SQL from before this rule, were replaced by one `InitialCreate` for PostgreSQL on 2026-10-21.)

**Data a schema change needs goes in a data upgrade, not the migration**, and so does data an older one left to be filled in later (the target buffers, 2026-11-05). [DataUpgrades](../src/TinapayanRMS.Infrastructure/Upgrades/DataUpgrades.cs) lists one-time changes to the data, oldest first, each a name and a method on the DbContext. EF Core's seeding hook (`UseSeeding` and `UseAsyncSeeding`, set in `TinapayanRMSDbContext.Configure`) runs the ones a database hasn't had after the migrations and inside their transaction, whenever a database is migrated: by `dotnet ef database update`, by a restored backup being brought up to date, and by the test fixtures. Each is recorded in `data_upgrades` and never made again. One added after its migration ran on a database (tied to an earlier migration) counts as an update there on its own (`DataUpgrades.OverdueAsync`), so the app's update and a restore migrate for it even with no migration to apply, a backup first: until 2026-11-22 they skipped `MigrateAsync` then, and it never ran. Each names the migration whose schema it needs, and waits until that's applied, so a database migrated back to an earlier migration skips it; it runs against the schema the migrations leave, so it can't read a column a later migration drops. Add one to the end of the list, never change one that has run, and test it in `DataUpgradesTests`.

1. Change the entity, its configuration, and its `Columns` list.
2. `dotnet ef migrations add <Name>`.
3. Read the generated `Up` and `Down` before you accept them.
4. `dotnet ef migrations has-pending-model-changes` should report no changes.
5. Regenerate the compiled model (see [The compiled model](#the-compiled-model)).
6. `dotnet ef database update`. (Running the app would apply it too, after a backup: see below.)
7. Run the tests. The fixtures migrate the test databases themselves.

### The app updates its own database

Updating the app is all an update takes, on this PC or another: the MSI or the install script, with no source folder and no `dotnet ef` on the shop's PC. As it opens, before the sign-in panel reads anything, [DatabaseReadiness](../src/TinapayanRMS.Presentation/App/DatabaseReadiness.cs) (a singleton, one run per start, shared by the panel's load and `StartupTasks`, and run again after a failure by the panel's Try Again) calls [DatabaseUpdater](../src/TinapayanRMS.Infrastructure/Services/DatabaseUpdater.cs) (`IDatabaseUpdater`), through its use case, `IDatabaseUpdates` (UseCases):

1. **A restore cut short** in the middle of its renames is put back (`IDatabaseBackups.RecoverInterruptedRestoreAsync`), or the next step would make a new, empty database where it should be.
2. **No database by the app's name** (a new PC): `MigrateAsync` makes it, with every migration and the seeded roles, and nothing to back up. An empty database (no tables) is given every migration the same way.
3. **Behind**: the migrations it lacks, worked out by `BackupRules.DatabaseUpdatesNeeded`, as a restore's are, are applied after a backup of the new kind `BackupKind.BeforeUpdate` (`before-update` in the file's name). A backup that can't be taken stops the update with nothing applied. EF Core 11 takes its migration lock, so two PCs opening the same database together don't both apply; the data upgrades run as the migration's seeding, as ever. A data upgrade the app gained after its migration ran here counts as behind too (`DataUpgrades.OverdueAsync`), with no migration to apply: the backup, then `MigrateAsync`, whose seeding makes it.
4. **A migration this copy doesn't know** (a newer copy on another PC updated it) or **tables with no migration history** (someone else's database) are refused, with nothing changed: `BackupRules.DatabaseFromLaterVersionText` and `DatabaseNotThisAppsText`, shown in the sign-in panel, which never unlocks over them.

While migrations are being applied, `DatabaseReadiness.IsUpdating` makes the panel say so. It's registered in `ServiceRegistration`, not `DatabaseHost`: the Recipe API and the MCP server never change the database. A Debug build started signed in (`TINAPAYANRMS_SIGNED_IN`) draws the screens without waiting for it; a failure then shows in the banner. Tests: `DatabaseUpdaterTests` (throwaway databases), `DatabaseReadinessTests`, `SignInStateTests`, `SignInTests`.

To discard a migration you've generated but haven't applied anywhere, run `dotnet ef migrations remove`.

**Renaming a column without losing data.** EF Core pairs renamed columns by guesswork. If you rename two columns of the same type on one table in one step, it can pair them crosswise and swap their data, or drop and re-add them. Use two migrations instead:

1. Rename the properties, and pin the old column names with `HasColumnName("OldName")`. Generate any other pending migration now; it carries no column change.
2. Remove the pins and generate again. The only difference is now the column names, so EF Core emits an unambiguous `RenameColumn`.

### The compiled model

The app runs on a compiled EF Core model, the generated code in `Infrastructure/CompiledModels/`, so the first query doesn't spend time building the model from the configurations. EF Core finds it by itself, through the `[assembly: DbContextModel]` attribute in `TinapayanRMSDbContextAssemblyAttributes.cs`, so `TinapayanRMSDbContext.Configure` doesn't call `UseModel`. The app, the tests and the repositories all use it. The `dotnet ef` commands don't: `migrations add`, `has-pending-model-changes` and `dbcontext optimize` itself build the design-time model from `OnModelCreating`.

- **Regenerate it after every model change**: an entity, a configuration, `ConfigureConventions` or `OnModelCreating`. Run `dotnet ef dbcontext optimize --output-dir CompiledModels --namespace TinapayanRMS.Infrastructure.CompiledModels`. It overwrites the folder, and the result is committed with the change. Never edit the generated files by hand.
- **A stale one still builds and runs.** It finds properties by name, so it keeps mapping the model as it was. [CompiledModelTests](../tests/TinapayanRMS.Infrastructure.Tests/CompiledModels/CompiledModelTests.cs) fail until it's regenerated. They compare it with the model built from the configurations: the entities, properties, keys, relationships and indexes, and the tables, columns, types and constraints they map to.
- **If the regeneration won't build**, for example because an entity class it names was deleted, delete `CompiledModels/` and run the command again.
- A compiled model leaves out what only migrations use, such as column collations and seed data. Those come from the design-time model, so they don't need to match.

---

## 9. The UI layer

### The shell

The app is a .NET MAUI window ([MauiProgram](../src/TinapayanRMS.Maui/MauiProgram.cs), [App](../src/TinapayanRMS.Maui/App.cs)) whose one page, [MainPage](../src/TinapayanRMS.Maui/MainPage.cs), is a `BlazorWebView` filling it: `wwwroot/index.html` with the app's `Routes` in it. [MainLayout](../src/TinapayanRMS.Components/Layout/MainLayout.razor) wraps every screen:

- **The title bar** ([TitleBar](../src/TinapayanRMS.Components/Layout/TitleBar.razor)), which the page draws, since the window is chromeless: the app's heading (`MainWindowState.Heading`) on the left; the Vim keys' mode while they're on, Find, Lock, Back, Forward, then Minimize, Maximize (Restore Down while maximized) and Close on the right. While the window is locked (`Locked`) it has only the app's title and the window's three buttons. The window does what the buttons ask through [IAppWindow](../src/TinapayanRMS.Components/Shared/IAppWindow.cs), which [AppWindowChrome](../src/TinapayanRMS.Maui/Platforms/Windows/AppWindowChrome.cs) implements. `app.js` (`watchTitleBar`) reports where the bar and its buttons are, and the window makes the gaps between the buttons its caption, so Windows itself drags it, snaps it, maximizes it on a double-click and opens its system menu on a right-click. Anything clickable added to the bar must be a `button`, `a` or `input`, or it's taken as caption. [WindowsWindow](../src/TinapayanRMS.Maui/Platforms/Windows/WindowsWindow.cs) takes Windows' own title bar away and hides MAUI's (docs/plan/maui-app.md, "The window").
- **The activity bar** ([ActivityBar](../src/TinapayanRMS.Components/Layout/ActivityBar.razor)), as Visual Studio Code's: a 32px column down the window's left edge, always out, beside the screen, and the one way between the screens (there's no strip of screen tabs since 2026-11-20: each record view holds a strip of its own records' tabs instead, "A record view opens each record in a tab of its own", below). An icon for each screen (Raw Materials, Sub-Assemblies, Products, Reports, Price Simulator), then Settings at the foot. Each icon's name shows to its right in a Syncfusion tooltip: one `SfTooltip` for them all (`Target=".activity-item"`), which reads the name from the icon's `data-title` (not `title`, which would bring up the browser's tooltip too). It needs `WindowCollision="true"`: with a `Target` and without it, the tooltip takes its own element, the 32px bar, as the edge it mustn't cross, and moves above the icon. Each is a `NavLink` matched by the address's start, so a record's address (`/products/42`) and Settings' pages (`/settings/roles`) mark their screen's icon. It holds no state; `Locked` makes it `inert` under the sign-in panel, as `.app-screen` is.
- **A warning banner** under the title bar while the database lacks migrations (`AppStatus.DatabaseWarning`), or while automatic backups keep failing (`AppStatus.BackupWarning`).
- **The Print dialog** ([PrintDialog](../src/TinapayanRMS.Components/Printing/PrintDialog.razor)), drawn once signed in, outside `.app-shell` so print.css can put the shell away on paper ([Printing](#printing)). The window locking closes it, as it would otherwise show the printout over the sign-in panel.
- **The hosts** for what any screen can show: `NoticeHost` (toasts), `ConfirmationHost` (a Yes/No dialog) and `PromptHost` (a dialog asking for a name).

**The sign-in gate** is `MainLayout` too, so it's in one place: see [Signing in](#signing-in).

Settings isn't a dialog but a screen, the gear at the activity bar's foot: [SettingsTabs](../src/TinapayanRMS.Components/Pages/Settings/SettingsTabs.razor) is a row of links, and each tab is a page of its own under `/settings/`.

Back and Forward are the web view's history: a screen is an address, and so is each record view's tab (`products/42`, `products/new/3`), and the title bar's arrows, Alt+Left and Alt+Right, and the Vim keys' `H` and `L` step through it. The arrows are enabled from the web view's Navigation API (`navigation.canGoBack`).

Closing the window ends the process there and then ([App](../src/TinapayanRMS.Maui/App.cs)), once the background work has stopped and the log is flushed: left to itself, it stayed 17 to 22 seconds after the window had gone, and a start in that time found it running and exited.

### Pages and their parts

A screen's page inherits [ScreenBase&lt;TState&gt;](../src/TinapayanRMS.Components/Shared/ScreenBase.cs), which injects the screen state as `State`:

```razor
@page "/settings/units-of-measure"
@inherits ScreenBase<UnitsOfMeasureState>

<PageTitle>Units of Measure · TinapayanRMS</PageTitle>

<SettingsTabs />

<div class="list-and-form">
    <section class="card list-card">
        <h2 class="section-header">Units of Measure</h2>
        <PickGrid TItem="UnitOfMeasure" DataSource="State!.Items" @bind-SelectedItem="State!.SelectedItem">
            <Columns>
                <GridColumn Field="@nameof(UnitOfMeasure.Id)" IsPrimaryKey="true" Visible="false" />
                <GridColumn Field="@nameof(UnitOfMeasure.Name)" HeaderText="Name" />
            </Columns>
        </PickGrid>
    </section>

    <section class="card form-card">
        <h2 class="section-header">@State!.FormTitle</h2>
        <FormRow Label="Name" For="unit-name">
            <TextField ID="unit-name" @bind-Value="State!.EditingName" />
        </FormRow>
        <div class="button-row">
            <CommandButton Command="State!.NewCommand" Icon="AppIcon.Add" Label="New" />
            <CommandButton Command="State!.SaveCommand" Icon="AppIcon.Save" Label="Save" Primary="true" />
            <CommandButton Command="State!.DeleteCommand" Icon="AppIcon.Delete" Label="Delete" />
        </div>
    </section>
</div>

@code {
    protected override ScreenCommand LoadCommand => State!.LoadCommand;
}
```

`ScreenBase` is what makes that enough. It:

- **runs `LoadCommand` each time the page opens** (`OnInitializedAsync`);
- **shows `ErrorMessage` and `SuccessMessage` as toasts**, through `Notices`;
- **answers `ConfirmDelete` with a dialog**, through `Confirmations`;
- **redraws the page** when a property of the screen state changes, and also when any of its lists or any of its commands' `CanExecute` changes. A grid's rows or a button's enabled state change without a property of the screen state changing, so `ScreenBase` subscribes to every property declared as an `INotifyCollectionChanged` or an `ICommand` on the screen state and on its parts (the get-only screen states it holds, such as `ProductCosting`), once. It picks them by the type they're declared as, worked out once a type, so a worked-out property (a caption, a filtered list such as `SelectedTreeProducts`) is never read for it: **a list or command a screen should redraw for is declared as one** (`ObservableCollection<T>`, `TableRows<T>`, `ScreenCommand`), not as an `IReadOnlyList<T>` or `object` holding one;
- **draws once for changes that come together.** A load sets hundreds of properties, rows and commands in a row, and a redraw asked for on the dispatcher is drawn and sent to the web view at once. So every request goes to the screen's `RedrawGate`: the first queues one redraw behind the work in hand, and the rest find it queued. The redraw follows the change by one turn of the dispatcher, with no timer. `FolderTree` has a gate of its own for its rows;
- **tells the Vim keys which screen is on show** (`VimKeysState`);
- gives the page `AddSubscription` (to keep an `Interaction` handler, or anything else the page lends its state, until the page goes), `ScreenNotices`, `OnStateChanged(propertyName)` to override, and `RedrawNowAsync` for the few things that have to follow the drawing, such as moving the cursor into a field the redraw fills in.

A component that redraws on a screen state's events asks its own `RedrawGate` (`Request`), and never calls `InvokeAsync(StateHasChanged)` from a handler that runs once per change. One that tells its gate it's drawn (`Drawn`, which drops the redraw queued for what's been asked so far) tells it as the drawing starts, at the top of its markup (`FolderTree`, `MainLayout`, the sign-in panel, `FindHost`, `TitleBar`) or before its `StateHasChanged` (`RedrawNowAsync`), never after: a change on another thread can ask while the component draws, and told afterwards the gate would drop that request though the drawing missed it.

A large screen is split into components in a folder named after it (`Pages/RawMaterials/`, `Pages/Products/`). A part takes the screen's state as a `State` parameter and has no state the screen state should own. The screen's page decides when each part shows. Keep a part's `@code` to view-only work, such as focus.

**A record view opens each record in a tab of its own** (Sub-Assemblies, Raw Materials and Products, since 2026-11-20): its list beside a strip of tabs, as Visual Studio Code's editors are. The pieces, each made for any record view:

- **[RecordViewState&lt;TForm&gt;](../src/TinapayanRMS.Presentation/Shared/RecordViewState.cs)** is the view's state (`SubAssembliesState`, `RawMaterialsState`, `ProductsState`, scoped as any screen's): the list, what every form reads (Sub-Assemblies' roles and materials; Raw Materials' materials, categories, units of measure and Settings' cost buffer; Products' products, folders, categories, roles, materials, linkable sub-assemblies and costing defaults), the load, and the tabs. Each tab holds a form of its own, a `RecordScreenState` that's an `ITabForm` (`SubAssemblyForm`, `RawMaterialForm`, `ProductForm`), made by the view (`NewForm`), never by DI. A form's own lists over the view's (Raw Materials' `LinkTargets`, `AddInOptions` and `CategoryOptions`, which leave out what the form holds) are subscriptions to the view's caches, so the form lets go of them in its `Dispose` (`_viewLists`): a closed tab would otherwise live as long as the view. A record picked in the list opens in the preview tab (`PreviewSlot`): the one there while it holds nothing unsaved, else a new one, the old kept; a record that has a tab shows it (`OpenInTab`). New, and Raw Materials' Copy, start a record in the preview slot. `Keep` keeps a tab (a double-click on it, or on the record's row: `FolderTree.DoubleClick`). `CloseTabAsync` shows the tab and asks about its form's changes first; `Drop` takes it away asking nothing (its record deleted).
- **The view's own work runs as the form showing** (`Runner`): its load (`LoadCommand`, through `ScreenState.RunLoadAsync`), and the list's actions, through `RecordViewState`'s re-implementation of `IScreenPartHost`, `IFormPartHost`, `IUndoRecorder` and `IRecordListHost`, which hand each call to the form showing, so what they say shows on that form and their work counts with it (a test settles on the form). The load merges into the view's lists and into every form, within every form's own refresh (`RefreshingEach`, through `ScreenState.RefreshWith`), each form saying what it showed before (`BeginLoad`), taking the merge (`TakeLoad`) and what follows it (`EndLoadAsync`). As that runs for every open tab on every visit and after every save, a form's merge does only what the load changed: Products' section choices are built again only when the view's linkable sub-assemblies or roles changed (`ProductsState.LinkableVersion`), a linked packaging's rows only when its lines did (`ProductPackaging.RefreshLinkedLines`), a composite's product choices only for a composite, and a line row raises its percentage only when its base or batch weight moved (`IngredientLineRow`); at its end it opens a record an address asked for before the list held it (`RecordToOpen`, `OpenRecordWaiting`). The list's host is the view, which answers for the forms holding a record (`Tabs.Holding`): a rename or move's saved-version checks (`SavedVersionsFor`, every tab's for the records it takes), `HasUnsavedChangesFor`, `Renamed`, `Moved`; a category or role deleted is forgotten by every form's undo steps. **All of that is the base's, shared by the three views**, with drafts (`OpenDraft`), deletes that close the record's tab (`DeleteRecordAsync`), the list's row following the tab showing (`ViewList`, `FormShowsAnother`) and the Vim keys' Save and Delete with no tab open (`IRecordScreen`); a view adds its lists, its load, its list host's own members and its actions, and names its `LoadCommand`, `NewCommand`, `OpenRecord` and `OpenByName`. Raw Materials' load also reads the products using each form's material (`DetailMaterialId`), which `EndLoadAsync` shows if the form still holds it.
- **[RecordTabs&lt;TForm&gt;](../src/TinapayanRMS.Presentation/Shared/RecordTabs.cs)** holds the tabs (`All`, `Active`, `Preview`), each a `RecordTab` with its form, `IsPreview` and `Number`, the tab's own number, which a new record's address carries. `Changed` says anything the strip draws changed: a tab opened, closed, shown or kept, or a form's `TabTitle`, `RecordId` or `HasUnsavedChanges` (the form raises it as its history's `IsDirty` changes).
- **A form's questions while no page shows it** go to the view's: the view answers its forms' `ConfirmDelete` and `ConfirmUnsavedChanges` after the form's own page (registered as it makes the form), and the window's guard answers them itself as it closes (below).
- **The page** inherits [RecordViewBase&lt;TView, TForm&gt;](../src/TinapayanRMS.Components/Shared/RecordViewBase.cs): `/sub-assemblies/{Id:int}` (or `/raw-materials/…`) opens a saved record as a pick would; `/sub-assemblies/new/{Number:int}` shows a tab by its number; the bare `/sub-assemblies` takes the active tab's address in its place. The tab showing goes into the address (`FollowTab`): a tab shown or opened adds to the history, so Back steps through the tabs; a new record saved, or the bare address, is replaced (`MoveAddressTo(…, replace: true)`). While an address waits for the load (`IsOpeningRecord`), the address stays; and once the address is another view's (a link, Back, the activity bar) or the page has gone, a change still coming in (a load running, a change queued before the page went) leaves it alone, or it took the window back to the view. A tab's record (`ITabForm.RecordId`) is the one picked from the pick on (`SelectedRow`, `SelectedProduct`, `SelectedRawMaterial`), falling back to `EditingId`, so a link opening another record in the preview tab never names the one before in the address while its details are read. The page draws the list card, a [RecordTabStrip](../src/TinapayanRMS.Components/Shared/RecordTabStrip.razor) (each tab a link to its address, the preview's name in italics, a dot for unsaved changes in the ✕'s place) and the active form, keyed by it (`@key="form"`), so another tab draws its form afresh.
- **The form's component** inherits [FormBase&lt;TForm&gt;](../src/TinapayanRMS.Components/Shared/FormBase.cs) (`SubAssemblyFormView`, `RawMaterialFormView`): a `ScreenBase` over the form it's given (`ResolveState`), which shows its messages, answers its questions, puts it on show for the Vim keys and draws its undo keys, but loads nothing. A move to another address asks nothing about it: the form stays in its tab, and the window asks about it as it closes, through the view (`IOpenForms`).

**A record view has an address per tab**, through `RecordViewBase` (above): `AddressRoot` ("products") names the view, and the record opens through the view, an `IRecordOpener` (`OpenRecord`, `OpenRecordOnLoad`), in its tab or the preview tab. That gives the web view's Back and Forward something to step through, and lets one screen open a record on another by navigating. (Until 2026-11-20 each record screen showed one record, through `RecordScreenBase`, which followed the record picked into the address.)

**The page acts on an address once, when it arrives** (`_addressOpened`), and not each time it's handed the same one:

- **As the view opens**, `OnInitialized` gives the record to the load that follows (`OpenRecordOnLoad`), which opens it at its end, when the list is fresh.
- **When the address changes** (Back, Forward, another screen's link), `OnParametersSet` opens the record there and then (`OpenRecord`).
- **New drops a record still waiting for the load.** Blazor also calls `OnParametersSet` once `OnInitializedAsync` is back, which is after the load. The pages used to open the record there, so a New clicked while the list was still loading had the record load over the new form. The Name box had the cursor, so it stayed blank, and the name typed into it was saved as the old record's.

### Screen state

The state is mutable and observable, and why is [ADR 0003](adr/0003-mutable-observable-screen-state.md). **[ScreenState](../src/TinapayanRMS.Presentation/Shared/ScreenState.cs)** is the base of every screen's state. It provides:

- `ErrorMessage` and `SuccessMessage`, which the toasts show.
- **`RunSafelyAsync(action)`**, which clears both messages and runs the action. Whatever the action throws becomes `ErrorMessage`, in the words [Failure](../src/TinapayanRMS.ApplicationCore/Rules/Failure.cs) gives it (ApplicationCore, so the screens need no reference to Infrastructure: it reads the database's errors through .NET's `DbException`, its `SqlState` and the fields Npgsql copies into `Data`, and EF Core's update exceptions by their type's name; `FailureTests` checks each case against the real exceptions): a `BusinessRuleException`'s message as written; what the database refused or couldn't do, put plainly by its error code (a duplicate name, something in use, a database that can't be reached); a file that couldn't be written or opened (`UnauthorizedAccessException`, and an `IOException` in its own terms: open in another program only for a sharing or lock violation, a missing file or folder as that, anything else in Windows' words); what `pg_dump` or `pg_restore` said (`PostgresToolException`); and anything else, a bug, as "Something went wrong, and the details went to the log: …". A refusal is logged as a warning, anything else as an error with its stack. Nothing reaches the button or key that ran it. An action that's done when it returns, with nothing to await, is an `Action`: `RunSafelyAsync(() => { … })`, with no `Task.CompletedTask` to return.
- **`LoadSafelyAsync(load)`**, which every `LoadCommand` runs its body in. A load that fails shows why in the same words, without clearing the notices (a screen loads after its own Save, and mustn't take that Save's message away), and a later load that works clears it.
- **`LoadLatest(values, load)`**, for a load driven by a selection: each value's load runs as it comes, a newer value cancels a load still running, and one that fails shows why while the next value loads as usual. Never write `Observable.Switch(...).Subscribe()` for this by hand: an exception that reaches `Switch` ends the subscription, and from then on no pick loads anything. **`LoadLatest(values, load, runAs)`** runs each value's load as another screen's, the one `runAs` gives as the value comes, counted with its work and its failure shown there: a record view's own loads run as the form showing (`RecordViewState.Runner`), as the Products list's flags after each load do.
- **`ShowFailure(exception, context)`**, for a failure that code catches itself, such as a read nothing awaits.
- **`Work`** ([BackgroundWork](../src/TinapayanRMS.Presentation/Shared/BackgroundWork.cs)) and **`WhenIdleAsync()`**. The three runners above count each piece of work while it runs, so `WhenIdleAsync` completes once the screen has settled: nothing it started is still running, and the dispatcher has run what the work queued. A part of a screen that starts work of its own takes the screen's `Work` and runs it through `Work.RunAsync` (`ProductComponents`'s cost reads). Start nothing in the background any other way, or a test waiting for the screen won't wait for it.
- **`RunWithConfirmationAsync(message, action)`**, which asks through the `ConfirmDelete` interaction first.
- **`ExportToExcelAsync(title)`**, which asks the view to write its grids to a workbook through the `ExportToExcel` interaction, and says where the file went. See [Exporting to Excel](#exporting-to-excel).
- **`ApplyRefresh`/`IsRefreshing`** and **`ApplyRestore`/`IsRestoring`**, which mark a load merging data in and an undo putting a form back (`IsBusyApplying` for either), so that nothing written meanwhile is taken for the user's: the undo history records no step (`CanRecordUndo`), Products' section form doesn't move what Save measures against (`ProductRecipe.RebaseSettledForm`), and Raw Materials' unit rows don't reprice the brands. A setter never asks: what only the user's edit sets off isn't in a setter (below), so a load or an undo writes the properties as they are. A figure worked out from the form is read, not pushed (below). What's left to push is a write the form's own figures follow, into the rows or the form: a table's rows pointed at the fresh copies of their ingredients (`ResolveLines`), the formula base applied to each row (`RecomputeFormulaBase`, which sets the grams of a line typed as a percentage), the piece size figure that wasn't typed (`FollowPieceSize`), the brand rows and the buffer that follow the buffered cost (`UpdatePurchaseCostTexts`, which writes `EditingCostBufferPercent`). Those go through [DerivedUpdates](../src/TinapayanRMS.Presentation/Shared/DerivedUpdates.cs) (`_updates.Run(SomeMethod)`): each runs at once, or once at the end of a restore, which writes the form inside `_updates.Batch`, so it never works from a half-restored form and needs no `IsRestoring` check.
- A logger for the concrete class (`logger.ForContext(GetType())`).

A command that deletes, or does anything else that can't be undone, goes through `RunWithConfirmationAsync`. A command that saves runs inside `RunSafelyAsync`, and runs its validator first. A command that only changes the form in memory isn't wrapped.

The smallest complete screens are Settings' list tabs (Units of Measure, Material Categories, Product Categories, Roles): a list, and a form beside it. They share [LookupListState](../src/TinapayanRMS.Presentation/Settings/LookupListState.cs), which holds the list, the pick, the name and the commands, and reads, checks and writes the list through its editor, an [ILookupListEditor](../src/TinapayanRMS.UseCases/Settings/ILookupListEditor.cs) (the tab's use case: `UnitOfMeasureEditor`, or the category's or role's own editor, which the other screens use too); each tab adds what its form holds beyond the name and what Delete asks. Abridged:

```csharp
public abstract class LookupListState<TItem> : ScreenState, IDisposable
    where TItem : class
{
    private readonly SourceCache<TItem, int> _cache;
    private readonly ReadOnlyObservableCollection<TItem> _items;

    protected LookupListState(string noun, Func<TItem, int> idOf, IComparer<TItem> order, ILookupListEditor<TItem> editor, ILogger logger)
        : base(logger)
    {
        Editor = editor;
        _cache = new SourceCache<TItem, int>(idOf);
        _cache.Connect()
            .SortAndBind(out _items, order)
            .Subscribe();

        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(async () =>
        {
            var items = await Editor.GetAllAsync(CancellationToken.None);              // 1. fetch
            var selectedId = SelectedItem is { } selected ? idOf(selected) : (int?)null; // 2. remember picks by id
            _cache.MergeFrom(items);                                                   // 3. merge, and point the
            SelectedItem = Items.FirstOrDefault(i => idOf(i) == selectedId);           //    selection at its fresh copy
        }));

        SaveCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
        {
            var item = FormItem();
            Editor.EnsureValid(item);   // a refusal (BusinessRuleException) shows as ErrorMessage, with why
            await SaveAsync(item);      // the editor's AddAsync or UpdateAsync, then the form cleared and the list loaded again
        }));

        DeleteCommand = new ScreenCommand(
            () => RunWithConfirmationAsync(DeleteQuestion(SelectedItem!), async () =>
            {
                await Editor.DeleteAsync(idOf(SelectedItem!), CancellationToken.None);
                await FinishSaveAsync();
            }),
            () => SelectedItem is not null);
    }

    public ReadOnlyObservableCollection<TItem> Items => _items;

    // What the form is for. A load points it at its fresh copy (above), which leaves the form as it is.
    public TItem? SelectedItem
    {
        get => _selectedItem;
        private set => RaiseAndSetIfChanged(ref _selectedItem, value);
    }

    // The user's pick, which the list calls (SelectedItemChanged="State!.Pick"): the form shows the item.
    public void Pick(TItem? item)
    {
        SelectedItem = item;
        if (item is not null)
        {
            EditingId = idOf(item);
            LoadIntoForm(item);
        }
    }

    // Worked out as it's read: the screen reads it again each time it's drawn.
    public string FormTitle => SelectedItem is null ? $"New {noun}" : $"Edit {noun}";

    protected ILookupListEditor<TItem> Editor { get; }
    protected abstract string DeleteQuestion(TItem item);
    protected abstract void LoadIntoForm(TItem item);
    protected abstract TItem FormItem();
    /* … ClearFields … */

    public void Dispose() => _cache.Dispose();
}
```

A tab is then its own fields and those few answers ([RawMaterialCategoriesState](../src/TinapayanRMS.Presentation/Settings/RawMaterialCategoriesState.cs), abridged); Roles overrides `SaveAsync` too, to ask before a role's product-owned box changes what it moves:

```csharp
public sealed class RawMaterialCategoriesState(IRawMaterialCategoryEditor editor, ILogger logger)
    : LookupListState<RawMaterialCategory>("Category", c => c.Id, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name), editor, logger)
{
    public RawMaterialCategoryType EditingType { get; set; }   // RaiseAndSetIfChanged, as every stored property

    protected override string DeleteQuestion(RawMaterialCategory item) => $"Delete the '{item.Name}' category? This can't be undone.";

    protected override void LoadIntoForm(RawMaterialCategory item)
    {
        EditingName = item.Name;
        EditingType = item.Type;
    }

    protected override RawMaterialCategory FormItem() => new() { Id = EditingId, Name = EditingName.Trim(), Type = EditingType };

    /* … ClearFields … */
}
```

The rules for screen states:

- **There's no MVVM library** (ReactiveUI went on 2026-11-16, the build plan's log): a screen's state (through `ScreenState`), a part of one and a row derive from [ObservableState](../src/TinapayanRMS.Presentation/Shared/ObservableState.cs), a plain `INotifyPropertyChanged`, and their commands are [ScreenCommand](../src/TinapayanRMS.Presentation/Shared/ScreenCommand.cs)s. A screen draws itself again whenever its state or one of its parts says anything changed (`ScreenBase`) and reads every value then, so:
  - **a stored property** sets its field with `RaiseAndSetIfChanged`, which says whether it changed: a setter with more to do then writes `if (RaiseAndSetIfChanged(ref _field, value)) { … }`, with no guard of its own;
  - **a setter only does what follows any write** (what's worked out from the field: `SelectionChanged`, `TypeChanged`, a cost recompute), never what only the user's edit sets off. **The user's edit is a method the page calls** in place of a two-way binding (`Value="State.EditingType" ValueChanged="State.ChangeType"`, a list's `SelectedItemChanged="State!.Pick"`, a tab's `@onclick="() => State.Recipe.PickRecipeTab(tab)"`), named for what the user does: `Pick` (the record screens' and Settings' selection), `ChangeType`, `ChangeSource`, `ChangeBaseUnit`, `PickSubAssembly`, `PickMode`, `PickRole`, `PickSource`, `PickLinkTarget`, `PickRecipeTab`, `TypeWeightPerPiece`, `TypeYield`. It writes the property, then does what the edit adds (`TypeEdited`, a made ingredient's starting weight loss) or feeds a stream of picks (`_picks`, `ModePicks`, `RolePicks`, `SourcePicks`, `LinkTargetPicks`, a packaging source's). Such a property's setter is private, or internal on a part the screen writes, so the compiler keeps a load or an undo off the edit's path: they write the property, and New or a screen's own pick for the user calls the method. A property whose setter only stores, or that only the user ever writes (a search box, a switch), stays bound with `@bind-Value` (since 2026-11-17, the build plan's log);
  - **a derived value is a plain getter** (`IsEditingMade => …`, `FormTitle`, Raw Materials' header costs). It's said to have changed only where something follows it by name (a row's table, a tree's row, a test) or where nothing stored changed with it (`UndoHistory.CanUndo`);
  - **a figure worked out from a table or from many fields is a getter over a [Derived&lt;T&gt;](../src/TinapayanRMS.Presentation/Shared/Derived.cs)** (since 2026-11-18, the build plan's log): Sub-Assemblies' batch totals and set cost, a made ingredient's BOM totals, the section form's figures and captions, and a product's costs (`ProductCosting.Costs`, worked out from what `ProductDraft.CostingInputs` gathers). It's worked out when read, and only when something it reads has changed since (`Invalidate`), so a screen drawn again for anything else reads it for nothing, and an undo that writes thirty fields marks it stale thirty times and works it out once, from the whole form. It's marked stale broadly, never one setter at a time: `RowChanges.OnAnyChange` on each table it reads (any row added, taken out or changed), the part's own `OnPropertyChanged` where it reads the part's fields, and a call where it reads something else (`InvalidateCosts`, `BomInputsChanged`, the cost basis, the shop's defaults). Too often costs one more computation; too rarely shows a stale figure. Invalidating raises a change of the properties that show it, once a read figure goes stale, so the screen draws it again. Nothing may rely on a figure's change event to learn of a change to what it reads: Save's dirty check follows the tables themselves (`RowChanges.OnAnyChange(Batch.Lines, History.RefreshDirty)`), since a figure nothing has read raises nothing;
  - **what follows a change is called by the setter or the method that made it**, in the order it has to run (`SelectionChanged`, `TypeChanged`), never a subscription to the screen state's own properties;
  - **a part tells its screen of a change with a plain event** the screen subscribes to where it wires the part up (`ProductComponentsDraft.IncludesPackagingChanged`, `ProductPackagingDraft.BulkQuantityChanged`), and a part whose property is set from several places, quiet ones included, says so once by overriding `ObservableState.OnPropertyChanged` (`ProductUsageForm.KindChanged`);
  - **a `ScreenCommand` asks whether it can run when it's wanted** (a `Func<bool>`, read as its button is drawn) and can't run while it runs, saying so as a run starts and ends (`CanExecuteChanged`); `ExecuteAsync` runs it whatever `CanExecute` says, so a button or key asks first (`Commands.Run`). One without a parameter is a `ScreenCommand` (`new ScreenCommand(asyncBody, canRun)`, or `ScreenCommand.Of(body, canRun)` for a body that's done when it returns), one with a parameter a `ScreenCommand<T>`;
  - **Rx stays where it does real work**: a load that a newer value cancels (`LoadLatest`, which switches to each value's load in turn, fed by a `BehaviorSubject` the setter pushes to), what one screen asks of another (below), DynamicData's lists and their filters, and `AutoRefresh` on a table's rows. Rx's unit is `System.Reactive.Unit`.
- **Lists** are a DynamicData `SourceCache<T, int>` bound with `Connect().SortAndBind(out collection, comparer).Subscribe()`, or an `ObservableCollection<T>` of editable rows.
- **A table an `SfGrid` draws is a [TableRows](../src/TinapayanRMS.Presentation/Shared/TableRows.cs) of its rows, filled with `ReplaceWith`**, which replaces every row in one change (a single Reset). The grid refreshes and draws all its rows again for each change to its list, and an `ObservableCollection`'s `Clear` and each `Add` are a change apiece: DynamicData's `AddRange` adds a row at a time, and so does `ObservableCollectionExtended`'s. Filled that way, Reports' five tables were drawn some 330 times a visit and held the window for 7 seconds of the 9 a visit took (2026-11-07). Rows that show the same as those it holds, in the same order, are no change: it keeps its own and says nothing, comparing a row class by its public scalar properties (`CollectionMerge.HasSameValues`) and a record or a value by its `Equals`, so a reload that finds nothing new doesn't draw the table again. `ReplaceWith` says whether it changed; what follows the grid's rows, such as the rows picked in it, follows that.
- **Several reads at once** start in parallel with `Task.Run`, are awaited together with `Task.WhenAll`, and are applied in one step, back on the UI thread. Code inside `Task.Run` calls repositories only and never touches screen state. (`ProductsState`'s `LoadCommand` starts about a dozen reads at once this way, and a packaging link for each open product; `RawMaterialsState`'s and `SubAssembliesState`'s each make one call off the UI thread, to their use case's `ReadCatalogAsync` (`IRawMaterialQueries`, `ISubAssemblyQueries`), which starts their five reads the same way, Raw Materials' with the products using each open tab's material, and gives them back together.) Awaited one after another from the UI thread, each read comes back to it before the next starts, and waits its turn behind whatever the screen is drawing.
- **Dialogs, prompts and files** are `Interaction`s that the page handles: `ConfirmDelete` and `ConfirmUnsavedChanges` (`ScreenBase` answers them, through `Confirmations`), `PromptForName` and `PromptForNewCategory` (the page answers through `Prompts`; the lists name their rows in place instead, `TreeNameEdit`, and only the Products list asks, for a clone's or variant's name), `PromptForMadeIngredient` (Move to In-House and Make In-House Ingredient: a name and a Made category, `Prompts.AskChoiceAsync`), `ExportToExcel` (the page asks where with the Save dialog, then builds or exports the workbook there), `ChooseCopyDestination`, `ChooseBackupFile`, `ChooseCopyFolder` and `ShowFolder` (the Database page answers with the Save, Open and folder dialogs and File Explorer, `IFileDialogs`), and `ShowPrint` (the Price Simulator hands its printouts to `PrintPreviews` and navigates to the preview). Tests register handlers of their own.
- **One screen asks another for something** through an `IObservable<T>` backed by a `Subject<T>`: `ProductOpenRequests` (from Raw Materials, Sub-Assemblies, Reports and the Price Simulator), `RawMaterialOpenRequests` (from Reports, and Sub-Assemblies' Dependents for a made ingredient), `NewProductRequests` (from Sub-Assemblies' New Product, handled by `ProductsState.StartNewProductFrom`), `SubAssemblyOpenRequests` (from Products, and a made ingredient's Made from badge on Raw Materials) and `PrintRequests` (from Products). A record view's forms ask through their own, which the view passes on (`RecordViewState.Wire`), so `ScreenLinks` follows the view. [ScreenLinks](../src/TinapayanRMS.Components/Shared/ScreenLinks.cs), one per page the window loads and started by the layout, subscribes to them and navigates to the target's address, so the screen states never know about each other.
- **A record screen's form edits a draft** ([ADR 0002](adr/0002-layers-use-cases-and-drafts.md)): Sub-Assemblies' `SubAssemblyForm` holds a [SubAssemblyDraft](../src/TinapayanRMS.Presentation/SubAssemblies/SubAssemblyDraft.cs), the record as it's edited (the name, the role, and the `Batch` and `PackagingSet` parts with their lines), with no service, no I/O and no screen in it. The draft is filled from what was saved (`Show`) or emptied (`Clear`), captured and put back for undo (`Capture`, `Restore`, which rebuilds the lines and applies the formula base once, at the end), re-pointed at a load's fresh copies (`Rebase`, which changes nothing typed: [ADR 0007](adr/0007-a-load-repoints-a-draft.md)), and read as a Save writes it (`ToSaveRequest`). Every change to anything it holds, its lines' cells included, raises `Changed`, which the form's dirty check follows in one subscription (it moves a `Version` too, which nothing reads and none need keep: [ADR 0009](adr/0009-close-the-migration.md)). The form keeps which record it is, the undo history, the tabs and Save, which goes through the editor: `EnsureValid` (the name and role), the draft's request (a line half filled in is refused), `CheckSaveAsync` (no lines is refused; the linking products' new costs are asked about), then `SaveAsync`. The form still offers the draft's fields under their old names (`EditingName`, `EditingRole`, `Batch`, `PackagingSet`), which the page binds: `ScreenBase` watches a part one level down, so a part the page binds is a property of the form.
- **Raw Materials' form edits a [RawMaterialDraft](../src/TinapayanRMS.Presentation/RawMaterials/RawMaterialDraft.cs) the same way** (phase 2): the material's own fields with the rules that follow them, and its Cost Setup, Alternative Units and recipe parts with their wiring, which it hosts: their host interfaces ask only for the undo recorder and the draft's fields. The draft asks its form only what `IRawMaterialDraftHost` gives (the material picked, whether its details have arrived, whether a load or an undo is writing), and its restore takes the form's `ApplyRestore` as a guard, so the fields go back as no edit of the user's and the one recompute runs after. A new material still on Settings' cost buffer follows a changed one as the draft's own rule (`TakeDefaultCostBuffer`), which hands back what moved (`CostBufferDefaultMoved`) for the form to carry into its undo history (`RewriteAll`). The form reads a made ingredient's recipe lines and hands them to the draft's recipe (`Recipe.ShowLines`), builds the material for Save with the draft (`ToMaterial`, then `IRawMaterialEditor.EnsureValid`, then `ToSaveRequest`, which refuses a half-filled row), and offers the draft's fields under the names the page binds.
- **Products' form gets its draft a part at a time** (phase 3), the Packaging tab's first: a [ProductPackagingDraft](../src/TinapayanRMS.Presentation/Products/ProductPackagingDraft.cs) holds the packaging (what it links, its lines and bulk packaging, a Move to Sub-Assemblies, the packaging last loaded), the rules that follow an edit (a link or a copy taking the lines' place and ending a move, Make a Copy, Remove Link keeping the bulk packaging, a move refused without a complete line, a row pointed at the material picked), its undo share and a load's re-pointing (`ResolveRows`), and what Save writes of it (`ChangedLink`, `ChangedLines`, `ChangedBulk`). It asks only for the cost basis (`IProductPackagingDraftHost`). The tab, `ProductPackaging`, keeps the commands, the Link and Copy picker and the rows picked, and passes the draft's fields on under the names the page binds; the form loads, captures, restores and saves through `Packaging.Draft`. A composite's Products table is the second: a [ProductComponentsDraft](../src/TinapayanRMS.Presentation/Products/ProductComponentsDraft.cs) holds its lines, the products' packaging ticked in or out, what the product cell offers (never the composite nor one holding it) and what each product brings as read; a line wanting a cost not read says so (`CostWanted`), and the tab, `ProductComponents`, reads it through `Work` and hands it over (`TakeCosts`), as it keeps Remove Line, the line picked and the checkbox's undo step. The third, the Costing tab's `ProductCosting`, was a draft already (the percentages, the costing as loaded, Settings' defaults and rates, the figures worked out from what the form gathers, with no reads, commands or host), and keeps a new product still on the defaults following new ones as its own rule (`TakeDefaults`); what only its form may change (its inputs, its cost basis) is internal, which Presentation.Tests sees. The recipe's is the fourth: a [ProductRecipeDraft](../src/TinapayanRMS.Presentation/Products/ProductRecipeDraft.cs) holds the sections (`UsageEdits`) and the recipe as saved, builds the sections from a saved product (`Show`), keeps them in recipe order, told apart and each marked when its role has others, copies a linked one as a new one (`CopyOfLinked`), re-points them at a load's fresh ingredients, roles and sub-assemblies (`RefreshRows`, and an undo step's with `StepAsSavedNow`; the lines a linked one's were changed from stay while they're changed here, `ProductUsageEdit.LinesTaken`; and a section whose role's Product-owned changed turns round only with nothing changed in it, `IsChangedHere`), and works out what Save writes (`CurrentDraft`, `IsAsSaved`), with the ingredients, the roles and a composite's products draft it reads; the part, `ProductRecipe`, keeps the tab strip mirroring the sections, the commands and the section form's flow, and passes `UsageEdits` on. Over the four is the product's own draft, a [ProductDraft](../src/TinapayanRMS.Presentation/Products/ProductDraft.cs): the product's fields (name, description, quantity, current price, category, kind, the Create Variant name), its parts' drafts, which it builds and hosts (their cost basis is its costing's, the name and kind its own), what the costs are worked out from (`CostingInputs`), the product Save writes (`ToProduct`), Print's pages (`ToPrintout`), an undo step's share of the product (`Capture`, `Restore`, the recipe part's share passed through), and its `Show`, `Clear` and `Rebase`: a saved product's fields and parts (`ShowFields`, `ShowParts`, which the form's `LoadIntoForm` and `LoadProductDetailAsync` call, before the recipe part's `ShowSaved` and the costing), a new product's fields (`ClearFields`, the form's `StartNewProduct`), and a load's re-pointing (`Rebase` and `RebaseLinked`, the form's `TakeLoad`, the second returning what changed of what's linked for the form's undo history); it asks the form only which saved product it shows (`IProductDraftHost`), and what changes it is internal, the form's to call. The form builds the parts' tabs over its parts' drafts and passes its fields on under the names the page binds.
- **A record screen's state is one class over its parts, in one file**: `ProductForm`, `RawMaterialForm` and `SubAssemblyForm` (with their views, `ProductsState`, `RawMaterialsState` and `SubAssembliesState`, which keep the list and the load: "A record view opens each record in a tab of its own", above), never a partial class (they were partial classes in files by area until 2026-11-14). The screen keeps which record it shows, loading, Save and the order an undo puts its parts back in; its form's own fields and what spans its parts (what Products' costs are worked out from) are its draft's where it has one (`ProductDraft`); each other piece is a part (below): its list and its tabs and cards. The constructor sets the fields, makes the parts, then **its first calls of what follows each change, and its subscriptions, in the order they were made, which counts**: what follows one change runs in that order (Products' tab-hiding before `ShowLinks`, Raw Materials' before `ShowPriceHistory`), the constructor calls each once for the form's first values (`TypeChanged`), and some subscriptions run as they're made (`LoadLatest` starts its first load, and `_picks` is a `BehaviorSubject`, so subscribing runs `ResetDetails`), so each needs what it reads made first. A part subscribes, in its own constructor, only to what it holds alone; a subscription that reaches from one part to another, or to the form, is the screen's, made in its constructor at its place in that order (Raw Materials' brand rows reaching the purchase texts, Products' section form wired to its recipe in `WireUsageForm`), so moving a piece into a part never moves a subscription ahead of one it ran after. A few groups stay small methods of their own (`WireUsageForm`, `WireCosts`), called in that order. **The commands come after, in any order**: making one runs nothing. A command, like a part, is get-only, set in the constructor, as `ScreenBase` only watches get-only parts.
- **A part owns its tab, card or list, commands and all.** A part is a class of its own that the screen holds as a get-only property (`ProductForm.Packaging`, a [ProductPackaging](../src/TinapayanRMS.Presentation/Products/ProductPackaging.cs)): it holds its piece's state, its commands and the logic only they use, and its component binds to it (`PackagingSection` binds `State.Packaging.RemoveLineCommand`; a list card takes its list part as its parameter, `<ProductsListCard List="State!.List" />`). A command that changes the form is one undo step, which the part records through the screen's `IUndoRecorder` (`host.AsUndoStep(...)`, [UndoRecorderExtensions](../src/TinapayanRMS.Presentation/Shared/UndoRecorderExtensions.cs)). What it needs of its screen comes through one interface of its own, which the screen implements explicitly, as Raw Materials does `IBrandPriceRowHost`: `IProductPackagingHost` gives the other products and `InvalidateCosts` (its draft asks for the cost basis, `IProductPackagingDraftHost`), on top of [IScreenPartHost](../src/TinapayanRMS.Presentation/Shared/IScreenPartHost.cs), which `ScreenState` implements for every screen (`Work`, `RunSafelyAsync`, `LoadLatest`, `ShowError`, `ShowSuccess`, `ShowFailure`), so the part's actions run, fail and count toward `WhenIdleAsync` as the screen's own do. A part of a record screen's form asks through one on top of `IFormPartHost` instead, which adds the undo recorder, `IsRefreshing`, `IsBusyApplying`, `RunWithConfirmationAsync` and `ApplyRefresh` (a part's own read merged as a load's is), all implemented by `RecordScreenState` (`IRawMaterialPriceLogHost`; `IProductRecipeHost`, which also hands the recipe the form's `History`: a section applied as its tab is left is one step, measured by what it did to the recipe); a list's is on top of `IRecordListHost` (below). A part a draft holds asks the draft instead, on top of the undo recorder alone (`IRawMaterialUnitsHost`, `IRawMaterialPurchaseCostHost`, `IRawMaterialRecipeHost`, which `RawMaterialDraft` implements), since a draft does nothing a screen does. Its repositories come through its constructor, and so do the lists the screen loads that it reads, collections each load merges into rather than replaces (Products' parts take the products, ingredients, packaging materials and roles; Raw Materials' recipe the ingredients); what a load replaces (Products' `Linkable`) or the screen decides it asks its host for. The screen builds its parts early in its constructor and passes itself, so a part's constructor makes its commands and may call `LoadLatest`, but reads nothing of the screen's state, which isn't set yet; a subscription it makes runs before the screen's own, so it only moves into a part when nothing else watches what it watches, or it ran first anyway (above). **A part keeps its own share of an undo step**: Products' snapshot holds one record a part (`ProductFormSnapshot.Recipe`, a `RecipeSnapshot` holding the section form's `SectionFormSnapshot`; `.Packaging`, a `PackagingSnapshot`; `.Components`), each saying for itself whether two look the same, and each part captures and puts back its own (`ProductPackagingDraft.Capture` and `Restore`, through `Packaging.Draft`; `ProductRecipe.Capture`, `CloseForRestore` and `Restore`); the screen's `RestoreSnapshot` calls them in the order they must go back in. **A part works out what's only its own**: `ProductCosting` works out, as they're read, from what the product's draft gathers (`ProductDraft.CostingInputs`: the recipe's figures, and the materials it uses), the costs, the captions naming what they're for (`CostUnitCaption`, `MaterialCostTooltip`) and the below-cost warning, and the Costing part holds the cost basis, which the screen switches (`ToggleCostBasisCommand`) as it prices its tables' rows again. A part can be another's input: `NewProductPlace`, where Save files a new product, is the list's too, whose new row starts there (the form showing's, through the view). What spans parts stays with the screen: loading (its reads through the screen's queries, a use case: `IProductQueries.ReadLoadAsync` gives a `ProductsLoad`, as `ReadDetailAsync` gives a `ProductDetail`), Save, and the order an undo puts the parts back in, the product's share of it the draft's (`ProductDraft.Restore`), as gathering what the costs are worked out from is. `ScreenBase` watches a part's properties, lists and commands, one level down, so a button bound to a part's command goes on and off with it (`ScreenBaseTests`).

### Loading and refreshing

- **There's one load trigger: opening the page.** `ScreenBase` runs the page's `LoadCommand` as it initialises, so going to a screen reloads it, and coming back to it reloads it again with whatever another screen changed. A page never loads data anywhere else.
- **Every `LoadCommand` is a fetch-then-merge, never a clear-and-reload.** It awaits all its reads first, then records the ids of the selected row and of every picker's choice, then merges inside `ApplyRefresh` and picks everything again by id.
- **[CollectionMerge](../src/TinapayanRMS.Presentation/Shared/CollectionMerge.cs)** (`MergeFrom`) keeps the existing object for every row whose values haven't changed, because that object is what a list's selection, a row's `@key` and a picker's choice are tied to. An `ObservableCollection` merge takes a key of any type, and rows that share a key are matched in order. `HasSameValues` compares scalar properties, plus those of referenced entities one level deep. A collection that a row owns (a material's `Units`) counts as part of the row.
- **Pointing a selection at its refreshed copy must not reload the form or throw away unsaved edits.** The merge writes the selection (`SelectedProduct = fresh`), whose setter only does what follows any change of it (the list's row, the tabs it hides), and only a pick, the `Pick` method the list calls, loads the form (`_picks` on the record screens, `LookupListState.Pick` on Settings' lists).
- Save and Delete call the same `LoadCommand` afterwards, so the record you saved stays selected.
- **A load that fails says so, and the screen keeps working.** `LoadSafelyAsync` and `LoadLatest` show the failure in a red toast, and the next visit or pick tries again. A selection's load that fails clears what it would have replaced rather than leave another record's details under the new pick.
- **Between a pick and its details arriving**, a record screen's tabs ignore the mouse (`AreDetailsReady`, the `details-pending` class), so nothing is typed into the record that's on its way out.

### Editing, undo and Save

Raw Materials, Sub-Assemblies and Products edit a whole record in memory, and one Save writes it all (see [One Save, one transaction](#writing-safely)). Save is offered only when there's something to save: its `canExecute` is `History.IsDirty`.

Undo and redo follow the Memento pattern:

- **[UndoHistory&lt;TSnapshot&gt;](../src/TinapayanRMS.Presentation/Shared/UndoHistory.cs)** keeps a screen's undo and redo stacks. It knows nothing about what a snapshot holds. The screen gives it a way to take a snapshot, a way to compare two, and a way to tell whether recording is allowed (not while loading or restoring).
- **The three record screens share it through [RecordScreenState&lt;TSnapshot&gt;](../src/TinapayanRMS.Presentation/Shared/RecordScreenState.cs)**, their base class: it makes `History` (first, so every subscription a screen wires up finds it), the Undo and Redo commands, `FormVersion`, the `IUndoRecorder` members, `AsUndoStep` (an extension on `IUndoRecorder`, so a part's command records its step through the screen the same way), `TryUndo`/`TryRedo`, `WatchForSave`, the unsaved-changes question (`HasUnsavedChanges`, `ResolveUnsavedChangesAsync`, `AfterUnsavedChangesAsync`), and opening a record (`OpenRecord`, by id, now or at the end of the next LoadCommand while the list doesn't hold it yet; `OpenRecordOnLoad`, for the address the screen opens on; `OpenRecordAsync`, which asks about unsaved changes first; `OpenByName`, `:e`'s; `PickFromList`, a row picked). A screen gives it `CaptureSnapshot`, `RestoreSnapshot(snapshot, settledThen)`, `UnsavedChangesName`, `ShowingRecordId`, `SaveFormAsync`, `ReadListsAgainAsync` and `RevertToSavedAsync`, and `CanRecordUndo` where a record's details arrive after the pick; for opening, `TrySelectRecord` (picks a record's row as a click would, clearing a search that hides it), `FindRecordByName` and `ReplacesShowing` (Products' leaves out a deleted product, which only its row in the Deleted folder picks), and it calls `OpenRecordAskedFor` at its LoadCommand's end and `ForgetRecordToOpen` on New. The base is the `IRecordOpener` its page opens the address's record through. Its Undo and Redo can run while `History.CanUndo` and `History.CanRedo`.
- **Their lists are parts on one base, [RecordList](../src/TinapayanRMS.Presentation/Trees/RecordList.cs)** (`ProductsList`, `RawMaterialsList` and `SubAssembliesList`, each the screen's `List`, and each the `IListTreeScreen` its tree asks what can be dragged where): the rows picked (`SelectedTreeNodes`, `SelectedTreeNode`, `SyncTreeSelection`, through `ListSelection`), the row named in place (`TreeNameEdit`, `CommitTreeNameAsync`, `CancelTreeName`, through `ListNaming`) and the move notice (`MoveNotice`, `DismissMoveNoticeCommand`). Each list gives it `ShowingRow` (the record shown's row, or the new one's), `PickedRecordsProperty`, `PickRowFromList` (a record's row picked opens it, through the view), `IsNewRow`, `CommitTreeNameToAsync`, `TreeNameCancelled` and `EndNewRow` (the new record's row taken out), and holds its own tree, search, moves, menus and folders. What it needs of the forms it asks through an interface on top of `IRecordListHost` (whether the form showing holds unsaved changes, its error, `AfterUnsavedChangesAsync`, which `RecordViewState` implements for every view), which the view implements explicitly: the form showing, and what the list's actions do to the forms holding the records they act on (`IRawMaterialsListHost.NameNew` and `Renamed`, `IProductsListHost.SaveNewAsync` for a product named in the list). Raw Materials and Products also share their Category picker's naming of a new category, [CategoryPicker&lt;TCategory&gt;](../src/TinapayanRMS.Presentation/Shared/CategoryPicker.cs), composed into each: the screen keeps what the page binds (`EditingCategory`, `CategorySearchText`, `PromptForNewCategory`) and gives it how a category is saved and read back. The base raises its changes by name.
- **Snapshots are immutable records** implementing `IFormSnapshot<T>` (`ShowsSameAs`, and `SavesSameAs`, which is `ShowsSameAs` unless the form holds something no Save writes): `RawMaterialFormSnapshot`, `SubAssemblyFormSnapshot` and `ProductFormSnapshot`, which is made of its parts' shares (`RecipeSnapshot`, `SectionFormSnapshot`, `PackagingSnapshot`). They hold the related records by id, not by object, because a refresh swaps in fresh objects and comparing those by reference would look like an edit.
- **The fields mark where a step starts and ends.** A record screen's page wraps its content in `<CascadingValue Value="(IUndoRecorder)State!">`. `TextField`, `NumberField` and `PickField` take that recorder as a cascading parameter and call `BeginUndoStep` as they take the cursor and `EndUndoStep` as they lose it, so one visit to a field or a cell is one step. A change made in one go (a tick box, a row dragged) wraps itself in the same pair.
- **A change that finishes the step before it joins that step** (`History.EndJoiningLast`): Products' section form applied on leaving its tab, or before Print and Export to PDF (`ProductRecipe.CommitUsageFormOnLeave`, `ApplyOpenForm`), finishes the typing into it, which was a step of its own, so one undo takes back both and shows the tab the edit was made on. With no step to join (typing an undo put back), it's kept as its own. Either way the tab moves only once the form left is applied (`RecipeTabShown`), so a step's snapshot never pairs one section's tab with another's form.
- **The recorder also says which record the form is for**: `IUndoRecorder.FormVersion`, a count each record screen state raises as another record, or a new one, takes the form's place (`LoadIntoForm` for a different id, and New). `TextField` and `NumberField` remember it as they take the cursor. If it has changed, the box is given up (`IsGivenUp`): nothing more is taken from it, and a fresh input, without the cursor, is drawn in its place with the record's own value. The lists' search boxes sit inside a `CascadingValue` of a null recorder, so they keep the cursor when a record opens.
- **[UndoKeys](../src/TinapayanRMS.Components/Shared/UndoKeys.razor)**, placed on a record screen, sends Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z to its `IUndoScreen`. The web view's own undo in a text box is never used. The key first takes the cursor out of the field it's in, so that what's half-typed lands and so the field shows what the step put in it, then puts the cursor back once the screen has been drawn again: in the same control, or in the same cell where the step drew a table's lines afresh. The page passes it `RedrawNowAsync` as `Redraw`, so the step is drawn before the page's script is told the key is done.
- **`History.IsDirty`** compares the form with the snapshot it was last saved or loaded with. It's worked out as it's read (a `Derived<bool>` since 2026-11-18): `RefreshDirty()` only marks it stale, so the screen calls it on every change of every table's rows (`RowChanges.OnAnyChange(…, History.RefreshDirty)`) for one capture and comparison when the Save button is next drawn, and Save never depends on a figure's change event to hear of an edit. It decides whether Save is offered, whether the form may be left without asking, and whether a printout says "Includes unsaved changes".
- **A dirty form is never left without asking.** Products, Raw Materials and Sub-Assemblies are `IUnsavedChangesScreen`s: `ResolveUnsavedChangesAsync(revert)` asks Save / Don't Save / Cancel through `ConfirmUnsavedChanges` (`ScreenState.AskAboutUnsavedChangesAsync`) and says whether the form may be left. A screen asks before its own form shows another record (a list pick, through `RecordScreenState.PickFromList`; New; `:e`, through `OpenRecordAsync`), and the page asks before the screen is left ([Unsaved changes](#unsaved-changes)). With `revert`, Don't Save also puts the form back as saved. What fails while asking, such as reading the record back, shows on the form, which isn't left. A new path that replaces a form's record, or leaves a screen, goes through one of these.
- **A record saved since the form took it** is followed as a load finds it (`RecordScreenState.FollowSavedVersion`, which each screen's load calls once). With nothing typed, the screen loads the record into the form again, whoever saved it: every table and sub-form of it, since a sub-form left as it was is written by the next Save (Products' section form, which `ProductRecipe.ShowSaved` closes for its tab to open again). With something typed, the form takes the new time only when the window itself has just saved that record: a list's rename or move sends the checks of the tabs holding its records (`SavedVersionsFor`) with its write, so it's refused over a save made since, and reloads through `LoadAfterSavingAsync(ids)`, after which each form holding one of `ids` follows it. Otherwise the form keeps the time it took, so its Save is refused rather than write over that save ([Writing safely](#writing-safely)). A write refused this way reads the screen's lists again before it says so (`OnActionFailedAsync`, through the screen's `ReadListsAgainAsync`), so the record opened next is the one saved now, where the list's old copy would be refused in its turn. A list write that doesn't save the record showing reloads with plain `LoadAsync`.
- **An edit the form hasn't seen yet keeps Save offered** (`IUndoRecorder.IsEditPending`): a grid cell being typed into, an add row with something typed into it until the row is added or dropped, and a `NumberField` that `LandsOnLeave` (Raw Materials' Buffered Cost) from the first key typed into it until it's left. Not for having the cursor alone: an undo puts the cursor back where it was, and the form it had just put back read as unsaved. The click on Save is what ends the edit, so a Save that went disabled as the edit ended would lose that click.

### The shared components

The pages are built from the components in [Shared/](../src/TinapayanRMS.Components/Shared/). Use them rather than a raw Syncfusion input or an `<input>` of your own: each exists because of something that went wrong without it.

| Component | Use it for | What it takes care of |
|---|---|---|
| `TextField` | Text bound to a screen state | Each keystroke reaches the screen state. Nothing writes into the box while it has the cursor. One undo step per visit. `OnLeave`, `OnEnter`. |
| `NumberField<TValue>` | A `decimal`, `decimal?`, `int` or `int?` | Shown in its `Format`, right-aligned, selected whole on taking the cursor. Takes only what makes a number: `app.js` refuses any other key before it shows (`beforeinput`), from what the input says it may hold (`data-whole` for an `int`, no point; `data-signed` with no `Min` or one below zero, a leading minus), and takes a paste or a drop only when it's one number the box takes (`oneNumberIn`), at the caret. Passes a number on as it's typed once it reads back the same (`2.5`, but not `2.` or `2.50`), and otherwise on leaving. `Min` and `Max`. A number beyond `NumberField.Largest` (just under a billion, either side of zero) is read as no number at all, as letters are: the costs worked out from a longer one overflow `decimal`. A box left holding text it didn't take is drawn as typed and then as its number, so the web view is sent the number. `LandsOnLeave` holds the value until the box is left. `ReadOnly` greys it. |
| `PickField<TItem>` | A picker (a Category, a Role, a line's ingredient) | A `PickComboBox` (an `SfComboBox` of the app's own) that filters as you type, lists the best match first (`RecordNameMatch.Rank`) and highlights it for Enter or Tab, leaving the box holding just what's typed; bound **by index**. Escape, then leaving, keeps the pick. Typed text that matches nothing puts the pick back, unless the picker is `AllowEmpty`. Only a pick made while the box has the cursor reaches the screen state: the combo box also hands back values of its own as it settles on a list it was given, which once put the base unit back into Raw Materials' Per under a form already settled. `ItemTemplate` for a list with columns. `OnTyped`, `OnArrive` and `OnLeave` for a picker that offers to add what was typed. `OpenOnShow` for one that appears in answer to a button (the Packaging tab's Link and Copy): it takes the cursor and opens its list as it shows. `Deferred` for a picker on every line of a table (every `LineGrid`'s): until it has the cursor it's a look-alike, the item's name and the arrow in the combo box's own classes, and the combo box is made only as the look-alike takes the cursor or its arrow is pressed, taking the cursor over once it's set up (`tinapayan.handOverPick` in `app.js`, with anything typed meanwhile); leaving puts the look-alike back. |
| `LineGrid<TRow>` | A table typed straight into (brand prices, a BOM, a recipe's lines) | A plain table of fields: one keyed row per line, a row header showing the line picked, drag to reorder (`Reorderable`, by the pointer: `app.js`'s `watchLineDrag`, since the window's web view never drops the browser's own drag), and the add row. |
| `IngredientLinesGrid`, `PackagingLinesGrid` | A recipe's ingredient lines; packaging lines | The `LineGrid`s that Sub-Assemblies and Products share. |
| `PickGrid<TItem>` | A list to pick one record from (Settings' lists) | An `SfGrid` with single row selection, kept in step with the screen state's `Selected…` property both ways. Its columns must include the key, with `IsPrimaryKey`. |
| `SelfDrawn` | An `SfGrid` over a `TableRows`, on a page that draws itself again for other things (Reports' tables, the Price Simulator's products) | Draws its content once and never again for the page (`ShouldRender` false), so a button's state, a toast or a grid's event doesn't take every grid through its drawing; the grid still draws itself as its rows change. Only for content whose parameters don't change after it's first drawn. |
| `FolderTree<TNode>` | The Raw Materials, Sub-Assemblies and Products lists | Draws the screen state's folders and rows, and changes its open folders and picked rows on a click (with Ctrl or Shift), the arrow keys, Home, End and Enter. Drag onto a folder, by the pointer (`app.js`'s `watchTreeDrag`, which scrolls the list near its top and bottom, through the `Drags` go-between), not the browser's drag and drop, which the web view doesn't hand back: the screen says what can be dragged (`CanDrag`, Products' one folder of the user's alone or products), where it can land (`CanDrop`) and what the drop does (`OnDrop`). The list cards hand it their screen state's `IListTreeScreen` members, so the rules the screen state tests check are the ones the window runs, and a drop made while a move is still saving does nothing (`IListTreeScreen.DropAsync`). A right-click menu (`TreeMenu`), and F2, Del, Ctrl+X and Ctrl+V for what the menu shows beside them, and Esc for what the screen gives it. `IsEditing` names the row whose name is being typed in place: it isn't dragged, and a click, F2 or Del on it does nothing. `RenamesOnSecondClick` names the rows a slow second click anywhere on the picked row renames, by what F2 does, after `SecondClickWait` (half a second; a test passes a task it ends itself), unless the cursor leaves the tree meanwhile. A double-click renames nothing. |
| `ListCardBase` | The three lists' cards (`ProductsListCard`, `RawMaterialsListCard`, `SubAssembliesListCard`), as their `@inherits` | `RowContent`, what a row of the list's `FolderTree` shows: a folder's icon, then the `TreeNameBox` while the row is named, or its title, what the card adds after it (Products' markup flag) and a folder's count (`ListTreeNode.Count`). A fragment drawn into the tree, not a component per row. Its base is named in `ListCardBase.razor.cs`: Razor's first pass gives a .razor file with markup in its `@code` no base, and the cards on it weren't found as components (RZ10012). |
| `TreeNameBox` | A list's row's name typed in place, as in a file explorer (the three lists, over each screen state's `TreeNameEdit`) | Drawn by the row template in place of the title. Takes the cursor with its text selected, and takes it back if it's taken before the box is used, in its first second (`SettleTime`), with no mouse press since it showed: a closing drop-down or dialog takes it (`tinapayan.showNameBox`, `takeFocusBack`). A click elsewhere ends it at once. Enter, or leaving with the text changed, gives the text to `OnCommit`; Esc, or leaving with the text it started with, calls `OnCancel`; only the first counts. Its keys and clicks stop at it. Key it by the edit (`@key`), so a refused name that comes back gets a new box. |
| `MoveNoticeBar` | Under a tree | What the last drag moved, with Undo. |
| `CommandButton` | A button bound to a command (a `ScreenCommand`) | Enabled while the command can run. |
| `ActionButton` | A button for a command with a parameter, or a page's own action | Pair it with `Commands.Run(command, parameter)`. |
| `MenuButton` | A button that drops a short list of commands | Closes its list once one is clicked. |
| `FormRow` | A label on the left of its field, in a stacked form | Lines the labels up. |
| `DependentTiles` | The products or sub-assemblies using a record | Each tile opens the record on its screen. |
| `UndoKeys` | A record screen | Ctrl+Z and Ctrl+Y (see above). |
| `Icon`, `AppIcons` | Icons | One of the app's icons (`SfIcon`); the Syncfusion icon each `AppIcon` is drawn with. |

How a page runs a command:

- a `CommandButton`, for a command without a parameter;
- `Commands.Run(command, parameter)` from an `ActionButton` or any event, which runs the command only if it can run now;
- never `_ = command.ExecuteAsync()` from markup: the click wouldn't wait for it.

A `ScreenCommand`'s `CanExecute()` is a method, asked when it's wanted (a button's `Disabled`, `!command.CanExecute()`). `Commands.Run` runs one awaited, if it can run now; a screen's load runs its `LoadCommand` whatever it says. A menu's item or a key runs one through `ICommand.Execute`, which nothing awaits: a run that fails that way goes to `UnhandledFailures.Report` (`ScreenCommandBase.UnawaitedRunFailed`).

### Typing: three rules

Each event in the web view goes to the components and comes back as a redraw (the web app this was ported from sent them over the network, Blazor Server). These rules keep that round trip from eating what's typed. Each was learned from a bug.

1. **Never write into a box that has the cursor.** A redraw answering an earlier keystroke would overwrite the keystrokes typed since: "zz web test material" arrived as "zt material". `TextField` and `NumberField` keep their own text while focused and take the screen state's value again once left, and `PickField` its own pick: a pick it refuses while focused (text rubbed out where one is needed) isn't written back into the box until it's left. What has to happen before the first key, such as selecting a number's text as it takes the cursor, is done in the page's script ([app.js](../src/TinapayanRMS.Components/wwwroot/app.js)), because the components can't be asked in time.
   - **The one exception is another record loading under the cursor** (New, then Back; or a box typed into while the screen is still opening its record). The box then holds the record before's text, and a name typed into it was saved as the loaded record's. So the box is given up and drawn afresh, as `FormVersion` above describes.
2. **Key a table's rows, and never draw one object as two rows.** `LineGrid` draws the lines and the add row's draft in one `@key`ed loop, so the draft keeps its row, and the cursor, as it becomes a line. It clears its draft before adding it to the lines, because the add redraws the screen, and a row that is both a line and the draft breaks the page ("more than one sibling has the same key").
3. **Bind a picker by index, not by value.** `SfComboBox` reads the first value of an enum as empty, so a picker bound to `RawMaterialType` showed blank for Ingredient. `PickField` binds an `int?` index into its list.
4. **Make a repeated row's handlers once, not on every drawing.** A handler that closes over its row (`@onclick="() => SelectAsync(row)"`) is a new delegate each time the table is drawn, and Blazor swaps a new handler id in for every row, for every handler, on every redraw of the screen, which follows any change it shows. `FolderTree` (`EventsOf`, `RowEvents`) and `LineGrid` make each row's handlers the first time it's drawn and keep them while it's there; a handler that is a method of the component (`@ondragend="OnDragEnd"`) needs nothing, since its delegates compare equal. ASP.NET Core's rendering guidance has the same rule ("Avoid recreating delegates for many repeated elements or components"). `FolderTreeTests` and `LineGridTests` check that a row keeps its handler id when the table is drawn again.

### The add row

A `LineGrid`'s add row is a draft made by the screen state's factory (`NewBrandPriceRow`, `NewBomLine`, and so on). The moment one of its properties changes, it joins the lines and a new draft takes its place. A draft nobody types into is never a line.

Where the screen itself changes the draft, pass `IsBlank`. A composite's Products table prices its add row when a product's costs arrive, and without `IsBlank` that put a blank line after every pick.

### Vim keys

An optional layer of Vim-style keys over the whole app, in the manner of Vimium. The user turns it on in Settings → Keyboard. `KeyboardState` keeps the choice in the Windows user's view state (`vim-keys`).

- **The keys are read in the web view**, by [vim.js](../src/TinapayanRMS.Components/wwwroot/vim.js), in the capture phase of `keydown`, before any control sees them. The keyboard and the cursor are the web view's, and a round trip to the components per key would be felt.
- **The server side is in [Vim/](../src/TinapayanRMS.Components/Vim/)**, and the [VimKeys](../src/TinapayanRMS.Components/Layout/VimKeys.razor) component in the layout joins the two. As the keys are turned on it hands `vim.js` the key table (`VimKeymap`), the command list (`VimCommandLine.Commands`) and the screens; `vim.js` writes NORMAL or INSERT into the title bar, and calls back for hint labels (`HintLabels`) and to run a command line (`VimKeysState.Run`).
- **Modes.** In NORMAL, a typed character is swallowed and looked up, so none types. INSERT is the app as it always was, and only Esc and Ctrl+[ are the keys'. A field that takes the cursor by a click or Tab goes to INSERT; one reached with `j` or `k` stays in NORMAL (`focusQuietly`). Esc in INSERT stops typing and keeps what was typed, by raising the field's `change`; in a dialog with the `closes-on-first-esc` class (Find's), it goes on to the dialog and closes it instead, as with the keys off. Give a dialog that class when its own keys all work while typing, so that a stop in NORMAL would only cost a key.
- **Where the cursor is** decides what a key does (`contextOf`): a field, a table (a `LineGrid` or an `SfGrid`), a tree (`FolderTree`), or anything else. A box typed into inside a tree (a `TreeNameBox`) is a field, so its letters are text.
- **Keys stand for what a hand would do.** In a tree, `j` is raised as the Down arrow, `dd` as Del and `r` as F2, on the tree itself, so `FolderTree`'s own key handling applies. In a `LineGrid`, the keys move the cursor from field to field by row and column. In an `SfGrid`, `j` and `k` click the next row. In a form, `j` and `k` move to the next and previous control in tab order. Nothing has Vim code of its own.
  - A key the layer raises itself is a synthetic event. Its own listener ignores those (`event.isTrusted`), so they reach the control.
  - `dd` in a `LineGrid` clicks the nearest button with the `remove-line` class after the table. Give a table's Remove button that class.
- **`f` labels what can be clicked** (`hintTargets`): buttons, links, fields, tabs, tree rows, grid rows, an open list's items. Typing a label clicks its target, or starts typing in it if it's a field.
- **The command line** (`:`) is a bar `vim.js` draws. The line goes to `VimKeysState.Run`, which parses it with `VimCommandLine` and runs it on the screen on show, through `IRecordScreen` (New, Save, Delete, `OpenByName`) and `IRevertScreen`. A command goes through the screen's own `ScreenCommand`, as a button does, so it asks, checks and reports as a click would. A screen's address comes back for the page to open; `:print` runs Products' Print, which opens the Print dialog. The screen on show is the latest a `ScreenBase` showed of those still up (`VimKeysState.Show` and `Hide`, a stack): the Print dialog's products pane is one, over the page under it, which is on show again as the dialog closes.
- **While a dialog is open the keys are for it**, wherever the cursor was left: `j` and `k` go round its buttons, and Esc closes it.
- **What the web view changes.** `gt` and `gT` click the next and previous screen on the activity bar (`.activity-item`). In Settings they click its own tabs, so the bar's Settings is passed over too: keys that went in couldn't come back out. `H` and `L` are the web view's Back and Forward. Ctrl+N is the web view's and can't be taken, so Ctrl+J and Ctrl+K move through an open list (Ctrl+P still works for up). Ctrl+R is taken from the web view, for redo, while the keys are on.
- **To add a key**, add a `VimKeyBinding` to `VimKeymap` (keys, action, contexts, description), and a `VimAction` if it's a new one. Then handle the action in `vim.js`'s `run`, or in the function for its place (`lineGridKey`, `pickGridKey`). The cheat sheet picks it up from the table.
- **To add a command**, add a `VimCommandDefinition` to `VimCommandLine.Commands` and handle its kind in `VimKeysState.Run`.
- **Tests** are in `Components.Tests/Vim/`: the key table, the command line, the labels and `VimKeysState`. `vim.js` itself has no automated test: check a change to it in the app's window.

### Theme and visual conventions

- **The theme** is Syncfusion's Fluent 2, its lite file (`_content/Syncfusion.Blazor.Themes/fluent2-lite.css`, linked in `wwwroot/index.html`): `fluent2.css` without the rules for Syncfusion's bigger, touch size (`e-bigger`), which nothing here uses, 4.3 MB rather than 5.5. The Maui project leaves the package's other themes out of the build. Then [app.css](../src/TinapayanRMS.Components/wwwroot/app.css), which recolours it and draws the app's own cards, tiles and forms.
- **Colours** are CSS variables at the top of `app.css`: the Catppuccin Latte palette, the Sky accent, and design tokens named for what they're for (`--page-background`, `--card-background`, `--card-border`, `--heading-foreground`, `--muted-foreground`, `--accent-soft`). Syncfusion's own variables (`--color-sf-primary` and the rest) are set from them. Styles use the tokens, never a hex colour of their own.
- **One stylesheet per screen family**, in `Components/wwwroot/css/`: `record-screens.css` (the three record screens and the shared components), `products.css`, `reports.css` (Reports and the Price Simulator) and `print.css`. They're linked in `wwwroot/index.html`.
- **Icons** are Syncfusion's own, from the Fluent 2 theme's icon font, the same one its components draw their arrows and buttons with, so they look the same on any system. Each is named by what it stands for in [AppIcon](../src/TinapayanRMS.Presentation/Shared/AppIcon.cs) (Presentation, so a screen state can give a menu item, tree row or tab one), and [AppIcons](../src/TinapayanRMS.Components/Shared/AppIcons.cs) picks the Syncfusion icon (`IconName`) for each: change an icon there, everywhere at once. A page draws one with `<Icon Of="AppIcon.Warning" />`, which is Syncfusion's `SfIcon`, and a button takes `Icon="AppIcon.Save"`. A row drawn hundreds of times (a folder tree's) puts `AppIcons.CssOf(...)` on a span instead, which is what `SfIcon` draws, without a component per row. An icon takes the size of where it sits (`.glyph` in `app.css`), not `SfIcon`'s own; a rule that sizes one by a class of its own names `.glyph` with it (`.glyph.confirm-glyph`). `AppIconsTests` checks the theme draws every `AppIcon`, so a Syncfusion upgrade that drops one fails there. The window's Minimize, Maximize, Restore Down and Close are inline SVG in `TitleBar.razor`, as Syncfusion's icons have none.
- **Tooltips** are an element's `data-title`, never its `title`, which would bring up the browser's own tooltip as well. [Tooltips](../src/TinapayanRMS.Components/Shared/Tooltips.razor), one `SfTooltip` with a `Target` of `[data-title]`, shows them in the activity bar's look (Fluent 2's, at most 320px wide, after 300 ms): `MainLayout` holds the whole page in one, and the Print dialog its content in another, as `SfDialog` moves itself out to the body. One whose target a click takes off the page (a pencil replaced by the picker it opens) closes then: Syncfusion's waits for the pointer to leave its target, which a target gone never does, so `Tooltips` has app.js's `watchTooltip` look for its target, by Syncfusion's `data-tooltip-id`, a little after each click or key, and close it once it's gone. The shared components' `Tooltip` parameter writes it, and an icon-only `CommandButton` or `ActionButton` takes it as its `aria-label` too, since nothing else names it. Keep a tooltip to a sentence, two at most: say what the control does, not every case. A component that puts its attributes where the pointer never is (`SfCheckBox`, on its hidden input) goes in a `<span class="tip-host" data-title="…">`.
- **Forms**: an input's label goes on its left in a stacked form (`FormRow`). A row of fields side by side keeps each label beside its field (`header-fields`). A value that can never be typed is text, not a read-only box.
- **Amounts** have no currency symbol, and use the decimals in [Precision and rounding](#precision-and-rounding).
- **Keep it plain.** Use the plainest standard control and the fewest columns. Fold a derived or helper value into a figure that's already shown rather than giving it a column of its own. A real cost component, though, gets its own column, and the line's total includes it.
- **Tables** have grid lines both ways. Quantity cells are `NumberField`s with `Format="#,0.0###"`: at least one decimal, and up to the four the database keeps.
- **Controls are 24px tall**: a button, a text or number box, a picker, and a table's rows and heading. `--control-height` at the top of `app.css` sets them, through its "Control heights" section (the Syncfusion buttons and boxes, the app's own fields, a grid's heading) and `record-screens.css` (a `LineGrid`'s rows). An `SfGrid` takes `RowHeight="24"`, which can't read the variable: give a new grid it too. To size a control, give it the height with `box-sizing: border-box` rather than padding that adds up to it, since a 1px border is drawn thinner at a display scale over 100%. [docs/plan/maui-app.md](plan/maui-app.md) ("How it's built") says what was left out and why.
- **What scrolls one way scrolls the other way too.** `overflow-x: auto` makes `overflow-y` auto as well, so anything reaching past the element's inside gives it a scroll bar, even a pixel: the recipe tab strip had one beside its tabs for the tab showing, hung a pixel over the strip's border to open onto its form. Draw such a line inside the element instead, as the strip now does with its bottom padding (`products.css`).

### Working with Syncfusion

- **Prefer a Syncfusion Blazor component** to a plain HTML control where one fits (`SfButton`, `SfGrid`, `SfTab`, `SfDialog`, `SfToast`, `SfDropDownButton`), so the whole UI picks up the theme. The exceptions are the shared components above.
- **To add a component from a new package**, add its `PackageReference` (with no version) to the Components project, a `PackageVersion` at `$(SyncfusionVersion)` in `Directory.Packages.props`, and its namespace to `_Imports.razor`. Whatever its package, a component new to the screens also goes on the page's list of scripts (next).
- **The page loads the scripts of only the components the screens use**, not Syncfusion's all-components script (`syncfusion-blazor.min.js`, 7 MB, which the Maui project leaves out of its build). [syncfusion.js](../src/TinapayanRMS.Components/wwwroot/syncfusion.js) lists them by Syncfusion's names (`SfGrid` is `sfgrid`), and has Syncfusion's own loader fetch each one's scripts and those they need: `sfBlazor.loadDependencies`, in Syncfusion's base script, which its per-component scripts call too. Nothing in Syncfusion waits for its scripts, and the window starts Blazor once the page is parsed, so the screens' JS initializer ([TinapayanRMS.Components.lib.module.js](../src/TinapayanRMS.Components/wwwroot/TinapayanRMS.Components.lib.module.js), which the host's build lists, under a fingerprinted name, in `_framework/blazor.modules.json`) holds Blazor's start until they're in. **A component new to the screens goes on the list**, unless it has no script (`SfButton`): [SyncfusionScriptsTests](../tests/TinapayanRMS.Components.Tests/SyncfusionScriptsTests.cs) checks the list against the Syncfusion components the Components assembly names, and against the loader in the package at `SyncfusionVersion`, so an upgrade that changes the loader fails there before it fails in the window.
- **To learn how a component is meant to be used**, ask the `syncfusion-blazor` MCP server (Syncfusion's Blazor documentation, started from `.mcp.json`), then read its type with `ilspycmd`: the packages are in `%UserProfile%\.nuget\packages\syncfusion.blazor.*\<version>\lib\net10.0\` (the grid's assembly is `Syncfusion.Blazor.Grids.dll`). Syncfusion ships no `net11.0` build yet, and .NET 11 runs the `net10.0` one.
- **Known traps:**
  - A Syncfusion component left off `syncfusion.js`'s list has no script in the window: it throws as it starts (the "Something went wrong" bar) or quietly does without, and bUnit can't see either. `SyncfusionScriptsTests` fails first.
  - `_Imports.razor` aliases the names Syncfusion's namespaces share with each other and with ASP.NET Core (`SelectionMode`, `SelectionType`, `FilterType`, `ChangeEventArgs`, `DragEventArgs`, `Action`). A new clash shows as CS0104: add an alias there.
  - `SfComboBox` reads an enum's first value as empty. Use `PickField`.
  - A Syncfusion drop-down that filters holds each filter back 300 ms after the key (`DebounceDelay`), so its list trails the typing. `PickField` sets it to 0, and so does the Print dialog's `SfMultiSelect`: the lists are in memory, and a filter takes the window a few milliseconds.
  - `SfMultiSelect` in checkbox mode draws its text (the items ticked, or its placeholder) on the theme's 28px line, lower than a picker's in the app's compact boxes; `app.css` sets its line to the box's (`--control-height`). Its arrow and clear button are hung from the box's bottom at the theme's 32px and 28px heights, so they sat 3 to 5px high; `app.css` makes them fill the box, as a picker's arrow does.
  - `SfComboBox`'s own `Autofill` writes the rest of the first name starting with what's typed into the box under the cursor, and its own highlight goes to the item the box held when that's still listed, not the best match. `PickComboBox` sorts by the match and highlights it instead (`PickField`).
  - A `Query` given to an `SfComboBox` as a parameter is taken in on its first redraw after it's drawn, which for a picker is the first letter typed into it: the combo box reloads its whole list, and that letter's filter is lost. `PickComboBox` adds its sort in `GetQuery`.
  - `SfComboBox.FilterAsync` with a list of the picker's own, from its `Filtering` event, lost to the combo box's own list: the popup kept showing everything.
  - `SfComboBox.FocusAsync` and `ShowPopupAsync` call into its script, which its first drawing sets up; its `Created` event says that's done. `PickField`'s `OpenOnShow` calls them there.
  - Each `SfComboBox` sets up its whole list as it's drawn (a query over every item, a spinner shown and hidden) and its script after: one on every line of a recipe held the window some 50 to 70 ms more for each open or pick of a record. A picker on a table's lines is `PickField`'s `Deferred`, a look-alike until the cursor comes.
  - A key `vim.js` gives a Syncfusion component (`press`) must bubble: the component hears it through Blazor, which listens at the page's root. A list given an Esc that didn't bubble stayed open.
  - `SfDropDownButton` with custom popup content doesn't close when something in it is clicked. Use `MenuButton`.
  - `SfTooltip`'s element takes the theme's 12px type, which everything inside it inherits: `app.css` has the `Tooltips` element inherit everything from its parent instead (`.app-tooltip-host`). A tooltip is placed by its target's box, so a target with none (`display: contents`) gets its tooltip in the window's top left corner: `.tip-host` is `inline-flex`. Wrapping an `SfMultiSelect` that way pushed the Print bar's buttons off its end, so the Print dialog's lists keep their `data-title` on their own input, and show it over their text only.
  - `SfToast`'s `CssClass` doesn't reach the toast. Set it on each `ToastModel`, as `NoticeHost` does.
  - An `SfGrid` in a flex card needs the CSS in `app.css` (`.list-card .e-grid`) to fill the card and scroll inside it.
  - An `SfTab` gets no `Height`. With one, its script reads the tab strip's height as it starts, and now and then the strip isn't in the page yet: the script throws, and Blazor's "Something went wrong" bar ends the screen. The `details-tabs` class sizes the tabs instead (`record-screens.css`). [docs/plan/maui-app.md](plan/maui-app.md) ("How it's built") has the whole story.
  - An `SfGrid` given an `ObservableCollection` draws all its rows again for each change to it, so a row-by-row fill draws a growing table once a row, on the UI thread. Give it a `TableRows<T>` and fill it with `ReplaceWith` (see the screen state rules above).
  - An `SfGrid` is taken through its parameters and its drawing whenever its page draws again, which a page does for a button's state, a toast, or an event of any grid on it. Put a grid over a `TableRows` in a `SelfDrawn`: it draws itself as its rows change, and needs nothing from the page after that. On Reports, with all five tables built, a filter on an 11-row table held the window for 140 ms, and nothing measurable inside one. Syncfusion's per-event `PreventRender` spares only the grid that raised the event.
  - Row virtualization (`EnableVirtualization`) isn't used. With `Height="100%"` the grid draws Syncfusion's default block of 12 rows and leaves the rest of the view empty until it's scrolled, and for a table of a hundred-odd rows it only moves the drawing into the scrolling, which otherwise costs the window nothing (2026-11-07).
  - An `SfTab` with `LoadOn="ContentLoad.Init"` builds every tab's content as the page opens. The record screens need that for their forms; Reports uses `Demand`, which builds a tab's table the first time it's shown and keeps it.
  - A grid's menu items are `MenuItem`s set in code, which trips analyzer BL0005: the tree's menu suppresses it locally.
  - Syncfusion's dialog asks the renderer whether it's interactive: a bUnit test that draws one calls `RenderingAsTheWebView()` (Components.Tests' `AppServices`).

**Make an `SfGrid` when it can be seen.** A grid created while hidden (`offsetWidth` 0) puts off building its modules, polling every 500 ms until it's shown. One that's never shown then crashes as it's let go ("Cannot read properties of undefined (reading 'unwireEvents')", which reaches the log as a task nothing awaited), and its timer runs for as long as the window is open. So a tab whose content is made up front (`LoadOn="ContentLoad.Init"`) makes its grid the first time the tab shows, as [PriceHistory](../src/TinapayanRMS.Components/Pages/RawMaterials/PriceHistory.razor) does; Reports' tabs load on demand for the same reason.

### Printing

The Products screen prints a product, a version from its History tab, or a whole folder of products. The Price Simulator prints a report of its own. The parts:

1. `ProductsState.PrintRequests` (a form's Print, passed on by the view, or the list's) asks for the Print dialog. `ScreenLinks` hands the choice (a `PrintScope`) to `PrintDialogState.Pick` and opens the dialog on the products (`PrintPreviews.ShowProducts`). The dialog's **Print** list holds the products ticked (`ChosenProductIds`), which a folder's, the products' or Print All Products' request ticks as the next preparation reads them; with none ticked it prints the product of the tab showing (`ProductsState.Form.PreparePrintout`), at that tab's cost basis. Its **Categories** list (`ChosenCategoryIds`) narrows the Print list.
2. A **`ProductPrintout`** (immutable records, in `Presentation/Printing/ProductPrintout.cs`) holds what's printed. It's taken from the screen as shown, unsaved edits included, or from `SavedProductReader` for a set of saved products. A **`ProductPrintSet`** is one printout or several, or a Price Simulator report.
3. **[PrintDocument](../src/TinapayanRMS.Components/Printing/PrintDocument.razor)** renders a `ProductPrintSet` as HTML: headings, tables and totals. Each part that starts a page (a set's title and contents, then each of its products) is a `print-sheet` element. Its page is **Settings → Printing's** (`PrintSettings`, one row, through `IPrintSettingsRepository`; `PrintRules` turns the paper, orientation and margins into millimetres), held for the page by the scoped **`PrintPage`** (Presentation/Printing). `PrintDialogState`'s preparation, `PdfExport` and the Price Simulator's `ShowPrint` handler read it again (`RefreshAsync`) before each printout, and `PrintingState`'s Save puts it in use at once (`Use`). `PrintDocument` writes it several ways: its own `<style>@page { size; margin; @bottom-left }</style>` (`PageRule`), which comes after `print.css`'s A4 one and so wins, with the footer note as a CSS string (`CssString`) in the margin box opposite the page number; `--paper-*`, `--margin-*` and `--type-scale` properties on the `print-document`, which size each `print-sheet` on screen, so what's printed is laid out at the page's width there too, and every printed font size (`calc(… * var(--type-scale, 1))`; the page number's stays 8.5 pt); `data-page-height`, the printed area's height in millimetres, for `app.js`; the shop's heading (`ShopHeading`: the logo as a `data:` URL, `PrintPage.LogoSource`, then the name over the address line) at the top of each `print-sheet`; and what's left out (`ShowsUnitCosts`, `ShowsPercentages`, `ShowsFigures`, `ShowsPriceComparison`), cells and headings alike, so the totals rows still line up.
4. **[print.css](../src/TinapayanRMS.Components/wwwroot/css/print.css)** lays that document out for paper. Under `@media print` it hides the app around the document, sets the page's margins and a "Page n of m" footer (`@page`), starts each `print-sheet` after the first on a new page, and keeps a heading with the table under it. A table's `tfoot` is a plain row group (`display: table-row-group`), so its totals print once, after the last row: as a footer group the browser repeated them on every page. On screen it draws each `print-sheet` as a sheet of paper, as long as its part is.
5. **[app.js](../src/TinapayanRMS.Components/wwwroot/app.js) breaks the pages** (`layOutPrintoutPages`), each time `PrintDocument` is drawn (its `OnAfterRenderAsync` calls `tinapayan.layOutPrintoutPages`, so the preview shows them), as the Print dialog has opened (its `Opened` event: the document is drawn as the dialog starts to open, before it has a size, and a printout with no layout is passed over), and again on `beforeprint`, for Print's dialog and Export to PDF's `Page.printToPDF` alike, since a page number is known only in `@page`'s margins (`counter(page)` reads 0 in the page itself). It fills each page to the document's `data-page-height` (182 × 267 mm for A4 with normal margins) from each `print-sheet`'s heights on the screen, which already lays it out at the page's width, by `print.css`'s rules (a row whole, a heading with what follows, the `print-keep` costing whole if it fits a page, the totals with the last row, never a table's first row alone), with 3 mm of slack and 8 mm at the top of each page after a part's first. Then, at each such page, it puts in a running head, the part's `h1` with "(continued)": a `print-running-head` div before its first heading or table, or, when a table goes on, a `print-running-row` in the table with the table's section (the nearest `h2`/`h3` before it, its own words) and a clone of the table's heading row (`print-repeat-head`), as the browser's own repeat of `thead` is turned off once the pages are worked out (`print-paged`), since it would come above the name; marks that head `data-page-break` (`break-before: page` on paper); and puts a `print-continued` row, "Continued on page n", under the last row of a table that goes on. A set's contents' `print-contents-page` cells get each product's first page. Everything put in has the class `print-added` and is cleared before the next working out; Blazor keeps its own list of the nodes it made, so it passes over them, and they stay until the next drawing or printing, since the print preview may print what it holds after its `afterprint`. On screen each running head carries `data-page` ("Page 2") for a dashed line with the number. Printed at a paper size, scale or margins other than Settings', the browser breaks the pages itself as well, and a note can name the wrong page. Check a change with the run-tinapayanrms driver: a script calling `Page.printToPDF` over the debugging port prints as Export to PDF does.
6. **[PrintPane](../src/TinapayanRMS.Components/Printing/PrintPane.razor)** shows the document on screen as a preview, with the **Include** tick boxes (Recipe, Costing and Sub-assembly total) and **Production copy**, all kept for the Windows user in the view state by `PrintIncludes`, which never clears the last of Recipe and Costing and holds Sub-assembly total and Costing apart (`PrintDocument.IncludeSubAssemblyTotal`: the recipe's costs as the costing prints them, unit costs regardless of `OmitUnitCosts`, then the total sub-assembly cost in a `print-sub-assembly-total` band in place of the breakdown); **Export to PDF**, which asks where to save (`IFileDialogs`, the name from `PdfExport.FileNameOf`) and writes the window's page there with `IAppWindow.SavePageAsPdfAsync`, as it shows, saying where or why not through `Notices` itself, as the dialog's tables and the Price Simulator's printouts have no screen state; and **Print**, which opens the computer's print dialog through `IPagePrinter`: WebView2's own (`window.print()`, [WebViewPagePrinter](../src/TinapayanRMS.Components/Shared/IPagePrinter.cs)). The printer is chosen there. A production copy passes `PrintDocument` the recipe alone and `ProductionCopy`, which adds a **Done** column of `print-tick-box`es to the recipe and packaging tables, a "Production copy" line under each title, a `print-signoff` line, and type at least 1.25 times normal (`ProductionTypeScale`). A printout with unsaved changes or never saved (`ProductPrintout.IsDraft`, from its `SaveNote`) gets a `print-draft` banner, and a `print-draft-mark` that `print.css` makes a fixed box on paper, which Chromium prints on every page. The dialog holds the page's script still and draws the printout over the page, so the lock cancels it: `MainLayout` calls `IAppWindow.ClosePrintDialog` as the window locks, and [PrintDialog](../src/TinapayanRMS.Maui/Platforms/Windows/PrintDialog.cs) finds the dialog's target through the web view's DevTools protocol and presses Escape in it, since WebView2 has no call for it.
7. **[PdfExport](../src/TinapayanRMS.Components/Printing/PdfExport.razor)** is Products' **Export to PDF**, which skips the Print dialog. `ProductForm.ExportPdfCommand` asks its `ExportPdf` interaction with the pages, which the form view's `PdfExport` answers. `PdfExport` asks where to save (`IFileDialogs`), draws a `PrintDocument` in a `pdf-export` element out of sight, has `app.js` break its pages (`layOutPrintoutPages`: `Page.printToPDF` fires no `beforeprint`), and calls `IAppWindow.SavePageAsPdfAsync`. The element is drawn through a section (`SectionContent`) in `MainLayout`'s `SectionOutlet` (`PdfExport.OutletName`), outside the `app-shell` as the Print dialog is, so while it's there `print.css` puts the shell away and prints it alone, at the paper's width: drawn in the product's tab, the list beside it printed too and pushed the pages off the paper. `.print-document` sets its own `line-height` (21px, Syncfusion's `.e-dlg-content`'s, which the preview inherits), so its rows, and so its page breaks, are the Print dialog's. [PagePdf](../src/TinapayanRMS.Maui/Platforms/Windows/PagePdf.cs) writes the file through the DevTools protocol's `Page.printToPDF`, which keeps `print.css`'s `@page` paper and margins.

**Every printout opens in one dialog**, [PrintDialog](../src/TinapayanRMS.Components/Printing/PrintDialog.razor) (since 2026-11-20; a Print screen and a preview page until then), an `SfDialog` 48px short of the window each way, over the screen it was opened from, which keeps its form as it was. [PrintPreviews](../src/TinapayanRMS.Components/Printing/PrintPreviews.cs), one per page, says whether it's open and on what (`Showing`): Products' choices (`ShowProducts`, drawn by `ProductPrintPane`, a `ScreenBase<PrintDialogState>`, which runs its load each time the dialog opens, its content being drawn only while it's open), the Price Simulator's printouts (`Show`: its `ShowPrint` interaction hands the choices over, a picker choosing among them) or a table (`ShowTables`). Its ✕ or Esc closes it, and so do a move to another address (`LocationChanged`) and the window locking (`MainLayout`). On paper `print.css` prints its document alone: while a `.print-dialog` is open the `.app-shell` goes, and so do the dialog's heading and the shade, its container, box and content laid out as the page's own (`position: static`, no size or transform).

**Tables print too**, through the same preview, page and `app.js`: a **`TablePrintout`** (Presentation/Printing: a title, lines under it, `PrintedTable`s of text cells with `PrintedColumn`s that are figures or keep to one line, a total row and a note, and a `SaveNote` that makes it a draft) is drawn by `PrintDocument`'s `Tables` parameter as one `print-sheet`, and `PrintPane`'s `Tables` hides the Include ticks and names its PDF after the title and date. `PrintPreviews.ShowTables` puts one in the dialog. Reports' **Print** on each tab builds one from its grid (`GridPrints.PrintoutAsync`: the visible columns, `GetCurrentViewRecordsAsync`'s rows, as the grid's own Excel export takes them, each cell by reflection in its column's `Format`), refreshing `PrintPage` first. Sub-Assemblies' **Print** is `SubAssemblyForm.PrintCommand`, its `ShowPrint` interaction carrying `PrintoutNow`, the form as it shows, which stays under the dialog, unsaved edits and all. A product's costing goes to Excel from the Costing tab: `ProductForm.ExportCostingCommand` asks `ExportCosting` with what Print would print, and the form view answers with `CostingWorkbook.Build` (Components/Printing: Costing, Recipe and Packaging sheets through `ExcelWorkbook`).

- [PriceSimulatorReport](../src/TinapayanRMS.Presentation/PriceSimulator/PriceSimulatorReport.cs) pairs a product's `SavedProduct` priced now with the same product priced at the simulated prices. Both are built from one `ProductDetail`, so their sections, lines, packaging and products pair up by index. It sets `NowCost` on each line, and `NowBatchCost` on each section, whose cost a change moves, and puts `PrintedSimulation` (today's `ProductCostSummary` and the changes reaching the product) on the printout.
- `PriceSimulatorState` keeps the last recalculation's simulated `SavedProduct`s and the changes it tried, so the report always matches the table. `PrintChoices` builds a `PrintChoice` for the rows picked and one for every row shown.
- A `ProductPrintSet` with `PriceChanges` set is a Price Simulator report. With the costing included, its first page has the price changes and a contents with each product's base cost now and simulated. Each product gets a Price Changes Applied table, a Cost Now column on every recipe, packaging and products table, a costing with Now, Simulated and Change columns, and, under a Current Price a change reaches, the price to keep today's markup.
- **The printout's layout**: a section's lines have no Base column, the costing has its Wastage row, and its last row is named for the unit the figures are for (`ProductPrintout.SuggestedSellingPriceLabel`, Suggested Selling Price per Piece).

### Exporting to Excel

A screen state's export command calls `ExportToExcelAsync(title)`, which raises the `ExportToExcel` interaction with the export's title and, once the page answers with a path, sets `SuccessMessage` to where the file went. It runs inside `RunSafelyAsync`. The page asks where with the computer's Save dialog ([IFileDialogs](../src/TinapayanRMS.Components/Shared/IFileDialogs.cs)`.ChooseSaveFileAsync`), suggesting "<title> yyyy-MM-dd.xlsx", and answers null, saving nothing, if it's cancelled. The workbook comes one of two ways:

- **A grid exports itself.** Reports' tables are `SfGrid`s with `AllowExcelExport`, and the page asks the grid for the workbook as a stream (`ExportToExcelAsync(asMemoryStream: true, …)`) and writes its bytes where the dialog said. **The grid closes that stream as it returns it** (Syncfusion disposes it in a `finally`), so it's read with `ToArray()`, which a closed `MemoryStream` still allows (setting its `Position` or copying it throws "Cannot access a closed Stream"): [GridExports](../src/TinapayanRMS.Components/Shared/GridExports.cs)`.WorkbookAsync` does that, and `GridExportsTests` checks it on a drawn grid. The sort, the filters and the columns the user chose are the grid's, so they come with it. Each Reports grid keeps them for the Windows user, as the grid's own state (`GetPersistDataAsync`), restored as it's created, the first time its tab is shown ([GridLayouts](../src/TinapayanRMS.Components/Shared/GridLayouts.cs)).
- **The page builds a workbook.** The Price Simulator describes its sheets with [ExcelWorkbook](../src/TinapayanRMS.Components/Shared/ExcelWorkbook.cs) (`ExcelSheet.Of(name, rows, columns…)`, each `ExcelColumn` a heading, a value and a number format), builds it with `ExcelWorkbook.Build` (Syncfusion XlsIO, licensed by the same key), and saves the bytes with `IFileDialogs.SaveAsync`. Figures are written as numbers at full precision; the format only shows them rounded.

The dialogs are Windows' own ([Platforms/Windows/FileDialogs.cs](../src/TinapayanRMS.Maui/Platforms/Windows/FileDialogs.cs)): WinRT's pickers, given the window's handle, as an unpackaged app must. Its `ShowFolder` starts `explorer.exe` on a folder.

### The Price Simulator

`PriceSimulatorState` prices every product now and with the table's changes (`TriedPriceChange`, `PriceChangeSimulation`), each a `PriceSimulatorProductRow` with its current price and `PriceToKeepMarkup`. Its products table is a `TableRows`, replaced in one change for each change typed and for Only affected, and its grid is in a `SelfDrawn`, as Reports' are: filled a row at a time, each of those held the window for about 1.2 s (2026-11-13). The page, [PriceSimulator.razor](../src/TinapayanRMS.Components/Pages/PriceSimulator.razor), gives the screen state the products as its grid shows them (`ShownProducts`, from `GetCurrentViewRecordsAsync` after each `DataBound`), for Print Report's All products shown and for Export to Excel.

The table can be saved as a **scenario** ([PriceScenario](../src/TinapayanRMS.Domain/Entities/PriceScenario.cs)), kept in the database:

- `SimulatedPriceChange.ToScenarioLine` gives a row's material and whichever of Change % and New Price was typed, and `Open(line)` types it back. So a percentage goes on the price as it is when the scenario is opened.
- `HasUnsavedChanges` compares the table with what was last opened or saved, by value. Picking a scenario over unsaved changes asks first.
- The scenarios list sets `SelectedScenario`, whose setter only asks for the scenario to be opened. Save asks for a name through `PromptForName`, and asks again before replacing another scenario of that name.

### The Production Plan

It moved to TinapayanBMS on 2026-10-31, and its screen, screen state, builder and rules were taken out. TinapayanBMS plans from what the [Recipe API](#the-recipe-api) gives it.

### Backups

Settings → Database is `DatabaseBackupsState` over `IBackupLibrary` (UseCases, over `IDatabaseBackups`), with the computer's own dialogs for its files:

- **Open Folder** shows the backups folder in File Explorer (`ShowFolder`, which the page answers with `IFileDialogs.ShowFolder`). It makes the folder first if no backup has yet, with `IBackupLibrary.EnsureFolder`, as a backup does: a folder made any other way would lack the entry that lets every Windows user in (`BackupFolder`).
- **Copy To…** asks where with the Save dialog (`ChooseCopyDestination`) and copies the picked backup there.
- **Restore from File…** asks for a `.backup` file with the Open dialog (`ChooseBackupFile`). The screen state copies it into the backups folder and restores it from there.
- **After a restore, the window's page loads again.** The screen state's restart callback raises `AppStatus.Restored`, a singleton's event, and `MainLayout` answers with a forced navigation, which builds every screen state afresh in a new scope.
- **Automatic backups are checked every hour** while the app runs, not only as it starts, since it can be left open for days: `StartupTasks` runs [AutomaticBackups](../src/TinapayanRMS.Presentation/App/AutomaticBackups.cs)' `RunAsync` at once and then on a `PeriodicTimer`. It takes the day's backup if it's due and copies it to **Also Copy To**, keeping the newest 14 there. That folder, and what last went wrong with a backup or its copy, are kept in the view state (`backup-copy-folder`, `backup-problem`, `backup-copy-problem`). The Database tab's **Choose…** picks the folder with the folder dialog (`ChooseCopyFolder`). When backups keep failing and the newest is three days old, `AppStatus.WarnOfBackups` puts a banner over the screens until one is taken.
- **An update takes a backup first** (`BackupKind.BeforeUpdate`, shown as Before update, never pruned), as a restore does: see [The app updates its own database](#the-app-updates-its-own-database).
- **pg_dump writes to a `.partial` file**, which takes the backup's name only once it's done, and a tool still running after half an hour is stopped (`PostgresTools`).

### Unsaved changes

A form with unsaved changes (`IUnsavedChangesScreen`: Products, Raw Materials, Sub-Assemblies) is never left with them. **In a record view (Sub-Assemblies, Raw Materials, Products) nothing replaces a form**: a pick or New opens another tab, and going elsewhere leaves the form in its tab, so only closing its tab (`RecordViewState.CloseTabAsync`, revert: false) or the window asks. Products' Clone, Create Variant and Apply Costing Defaults still ask a tab holding the product (shown first, revert: true), as what they make or change is the product as saved. A form asks through `ConfirmUnsavedChanges`, which `ScreenBase` answers with the Yes, No and Cancel dialog (`Confirmations.AskAboutUnsavedChangesAsync`). **A move between addresses asks nothing** (the activity bar, a link, Find, Back and Forward): every form that can hold changes is a record view's tab, which stays as it was, so `ScreenBase` registers no location-changing handler. Until the record views (2026-11-20) it did, for the one form each record screen held. A view moves its own address to the tab it shows through `MoveAddressTo`. The Print dialog is no move at all: the screen under it stays, form and all.

**A Save still running is waited for, not asked about.** The form holds unsaved changes until its Save is back, so a tab clicked (or the window closed) in that moment asked "Save the changes?", and Yes started a second Save of the same form beside the first, since `SaveCommand.Execute()` runs whatever `CanExecute` says: a new record's second insert was refused as a name already in use, after a Save that worked. Each screen's SaveCommand runs its body through `RecordScreenState.RunSaveAsync`, which keeps the Save's task, and `ResolveUnsavedChangesAsync` awaits it while the form is dirty, then asks only if the form is still dirty, as after a Save that was refused. It's the form's question, not the button's, so it's kept with the form rather than read from the command. A Save clears the form's history before it reloads, so nothing it runs on its way can wait on it. Tests: `LeavingWhileSavingTests`.

Closing the window is the page's [UnsavedChangesGuard](../src/TinapayanRMS.Components/Shared/UnsavedChangesGuard.cs) to ask about. The window's `Closing` handler ([WindowsWindow](../src/TinapayanRMS.Maui/Platforms/Windows/WindowsWindow.cs)) holds the window open, asks the same question through the guard on the web view's dispatcher (`BlazorWebView.TryDispatchAsync`, `revert: false`, as for the app closing), and closes it once the forms may be left; Close clicked again meanwhile waits on the first. The forms are asked of the record views, every tab's, whether the view shows or not (`IOpenForms`, registered for each view: `RawMaterialsState`, `SubAssembliesState` and `ProductsState`, which the guard fetches once for its page), so a screen that failed and gave way to its error panel still has its forms asked about; as no page may show the form, the guard answers its questions itself while it asks (`ResolveAsync`, through `Confirmations`), and shows why a Save it asked for was refused. Before the forms, the guard asks every `IBusyWork` (Settings → Database's `DatabaseBackupsState`, through its `BusyText`): while a backup, copy or restore runs, the window stays open and the page's error toast says why, since closing ends the process there and then, and a restore cut short between its renames left the database under another name. Reloading the page (F5) has the web view's own leave warning, which every `ScreenBase` turns on while any form the guard knows holds changes (`UnsavedChangesGuard.HasUnsavedChanges`; `tinapayan.setLeaveWarning`, a `beforeunload` handler).

**Windows asking the app to close** isn't the window's Close either. Before a sign-out, a shutdown or a restart, and for an install or uninstall that replaces the app's files (Windows Installer's Restart Manager, which flags it `ENDSESSION_CLOSEAPP`), Windows sends the window `WM_QUERYENDSESSION`, wanting its answer at once, then `WM_ENDSESSION`. MAUI's `OnPlatformMessage` can't answer a message, so `WindowsWindow` subclasses the window itself (comctl32's `SetWindowSubclass`, `OnSessionEnding`). It reads `AppStatus.KeepsWindowOpen`, which asks the guard of the page loaded last (`UnsavedChangesGuard.KeepsWindowOpen`: what a busy work says, or that a form holds unsaved changes) and asks the user nothing: the window's thread is the page's too, so it's read there and then, with no dispatch to wait on. With a reason, the window answers no, gives Windows the reason to show beside the app in a sign-out's or a shutdown's list (`ShutdownBlockReasonCreate`), and posts its own Close (`SC_CLOSE`, after `SC_RESTORE` when it's minimized), so the usual question follows; without one, it lets the default yes through, and when `WM_ENDSESSION` says the session ends, it ends the app as its closing does (`App.End`: the hourly backups stop and the log is flushed). A close by Restart Manager that isn't forced, Windows Installer's default, then fails at once (`ERROR_FAIL_SHUTDOWN`), and Windows Installer is left with its files in use, which it replaces at a restart if the app is still open when it copies them. An end that's forced (Sign out anyway, an install that forces it) still ends the app.

### Signing in

The app has one user, so one password and no user name ([AccessSettings](../src/TinapayanRMS.Domain/Entities/AccessSettings.cs), one row in `access_settings`, beside the auto-lock's minutes). It's kept as PBKDF2-HMAC-SHA256 with a 16-byte salt of its own, at 600,000 iterations, a third of a second ([PasswordRules](../src/TinapayanRMS.ApplicationCore/Rules/PasswordRules.cs)), and checked in constant time at the iteration count stored with it, so raising `Iterations` leaves older passwords working. [AccessSettingsRepository](../src/TinapayanRMS.Infrastructure/Repositories/AccessSettingsRepository.cs) hashes on `Task.Run`, sets the first password only while there's none (`CreatePasswordAsync`), and changes it only with the current one. It's in the database rather than the Windows user's view state so it outlives a Windows profile and goes with the backups; restoring an older backup brings back its password, or none, which the app then asks to set. The Recipe API's reader role can read the hash, and nothing serves it.

- **[AppLock](../src/TinapayanRMS.Presentation/App/AppLock.cs)** is a singleton, so the page loading again after a restore stays signed in. It starts locked; `Unlock` and `Lock` raise `Changed`; `HasSignedIn` says whether the password has been given since the start; `CheckIdle` locks once nothing has noted activity for `AutoLockMinutes` (0 is never); and the fifth wrong password in a row makes the next try wait 30 seconds (`NoteWrongTry`, `WaitLeft`). Its time is a `TimeProvider` (`TestClock` in the tests).
- **[MainLayout](../src/TinapayanRMS.Components/Layout/MainLayout.razor) is the gate.** Before the first sign-in it draws no activity bar, tabs or `@Body`, so no screen is built and nothing loads: only the title bar and [SignInScreen](../src/TinapayanRMS.Components/Layout/SignInScreen.razor). Locked after that, the screens stay drawn, unsaved edits and all, but the activity bar and `.app-screen` are `inert` (no clicks, no focus), the banners go, and the panel covers them (`.sign-in-screen`, absolutely placed over `.app-body`). Every move to another address is cancelled while locked (`RegisterLocationChangingHandler`). It hosts the auto-lock: `app.js`'s `watchActivity` calls `NoteActivity` at most every 10 seconds on a key, pointer or wheel event in the window, and a 30-second `PeriodicTimer` calls `CheckIdle`.
- **No key reaches the screens while the panel is up.** The title bar drops the Vim keys and Find with their components (`VimKeys` turns `vim.js` off as it goes). `SignInScreen` turns on `app.js`'s `lockKeys` for as long as it's shown: a capture listener on `window`, ahead of every document listener and Blazor's, stops each key whose target is outside the panel and every Ctrl, Alt or Win combination (Ctrl+A, C, V, X, Z and Y still edit the box), and cancels the browser's own (Alt+Left, Ctrl+P, F5).
- **[SignInState](../src/TinapayanRMS.Presentation/App/SignInState.cs)** (the page's) first waits for the database to be made ready ([The app updates its own database](#the-app-updates-its-own-database)), then asks to set a password while there's none, else for the password; on success it reads the auto-lock's minutes into `AppLock` and unlocks. A failure to reach the database shows in the panel with Try Again, and never unlocks. A password cleared or set elsewhere while the panel is up turns it to the other form. What's typed is cleared after each try, so the panel draws its boxes afresh then (`@key`) and puts the cursor back. **A box's text is dropped during a try** (`Typed`): Enter in a box also sends its text as a change, which lands while the password is being checked and would otherwise come back in the fresh box. Settings → Security ([SecurityState](../src/TinapayanRMS.Presentation/Settings/SecurityState.cs)) does the same for its three boxes.
- **A forgotten password** is cleared by [scripts/Reset-Password.ps1](../scripts/Reset-Password.ps1) (hash, salt and date to NULL; the minutes stay), which needs the database's own access, as the Windows user's `pgpass.conf` gives it: the password protects the open app from whoever is at the PC, and can't be stronger than that Windows account.
- **A check in the window** can't know the password: a Debug build started with `TINAPAYANRMS_SIGNED_IN=1` starts unlocked with no auto-lock (`MauiProgram`, `#if DEBUG`), which `driver.ps1 launch` sets unless `TINAPAYANRMS_SIGN_IN=1`. To check the panel itself, run against a copy of the dev database (`ConnectionStrings__Default`), not the user's, where setting a password would lock them out of their own.
- Tests: `PasswordRulesTests`, `AccessSettingsPersistenceTests`, `AccessSettingsEditorTests` (UseCases.Tests), `AppLockTests`, `SignInStateTests`, `SecurityStateTests`, and `SignInTests` (Components.Tests, over `InMemoryAccessSettings`, since the component tests have no database).

### Find

Ctrl+K, or the title bar's magnifier, opens [FindHost](../src/TinapayanRMS.Components/Layout/FindHost.razor), a dialog over `QuickFindState`, which reads every name each time it opens (`IQuickFindQueries`) and matches what's typed (`QuickFindState.Match`). The key is caught in the page's script (`tinapayan.registerFindKey`). Opening a match navigates to its address, so the screen being left asks about its unsaved changes as any link does.

### Logging and errors

- Serilog's static `Log.Logger` is built at the top of `MauiProgram`. It writes one rolling file a day to `%LocalAppData%\TinapayanRMS\Maui\logs\`, keeping 31. It's registered in DI as a `Serilog.ILogger`, and it's the app's only logging provider: Blazor's and EF Core's own logs go to it from Warning up.
- **Each start names the build** in the log (`AppVersion.Current`: the version and, in a published copy, its commit), which Settings → Database shows as **App Version**. An everyday build has no commit to name.
- `RunSafelyAsync`, `LoadSafelyAsync` and `LoadLatest` log a refusal as a warning, and any other failure as an error with its stack. This is where nearly every failure ends: on its screen, as a red toast, with the app carrying on.
- **A red toast goes with what it was about.** The next action clears it (`RunSafelyAsync`), and so do another record taking the form's place (`RecordScreenState.FormVersion`), the answer No about unsaved changes (`AskAboutUnsavedChangesAsync`) and leaving the screen (`ScreenBase.Dispose`). The next screen has no message of its own to clear, so a toast left up would stay until it was closed by hand.
- **An exception that escapes a screen gives the screen up, not the page.** `MainLayout` draws the screens inside a [ScreenErrorBoundary](../src/TinapayanRMS.Components/Shared/ScreenErrorBoundary.razor) (an `ErrorBoundaryBase`), which logs the exception and shows a panel in the screen's place, with **Open It Again** (`Recover`); another address recovers it too. The title bar, the tabs and the other screens go on working, and the screen's state, the page's, keeps what it held, unsaved changes included. Only a failure outside the screens (the title bar, the layout's hosts) still reaches Blazor's bar at the bottom of the window ("Something went wrong, and the page needs reloading", in `index.html`), whose Reload builds every screen state afresh. So a screen state still catches what its actions throw, and a page still shows a failure of its own with `ScreenNotices.ShowError`: the panel is the backstop.
- **What fails with nothing to catch it is logged** ([UnhandledFailures](../src/TinapayanRMS.Components/UnhandledFailures.cs)): an exception that ends the process (`AppDomain.UnhandledException`, fatal), a task that failed with nothing awaiting it (`TaskScheduler.UnobservedTaskException`, a warning, and observed), an exception on the window's thread (WinUI's `UnhandledException`, fatal, hooked by the WinUI `App`), and a command run nothing awaited that failed (`ScreenCommandBase.UnawaitedRunFailed`: error, and shown as a toast by `NoticeHost`). MAUI logs none of them itself.
- **A task a component starts and doesn't wait for goes through `Forget(logger)`** ([TaskForgetting](../src/TinapayanRMS.Components/Shared/TaskForgetting.cs)), not a bare `_ =`: what it throws is logged as it fails (a page that has gone and a cancellation aren't failures), where a discard only reached `UnobservedTaskException` when the task was collected, if ever. In `app.js`, every call into .NET (`invokeMethodAsync`) has a `.catch`.
- **A failure to start** is logged as fatal (`MauiProgram`), Windows' message box says why and where the log is, and the app ends. A database that can't be reached isn't one: the window opens, and each screen's load says so in its error notice.
- An address that doesn't exist goes to `/not-found`.
- In a Debug build, F12 opens the web view's developer tools (`AddBlazorWebViewDeveloperTools`), whose console shows a component's exception.

---

## 10. Testing

### How the tests work

- **Nothing is mocked.** Rules are tested as pure functions. Repositories, services and screen states are tested on a real PostgreSQL database that Respawn resets before every test. The components are rendered with bUnit, which has no web view: what only shows in the app's window (focus, typing, drag and drop, printing, the dialogs, the Vim keys' script and the title bar) is checked there ([Checking a screen in the app's window](#checking-a-screen-in-the-apps-window)).
- **Each database-backed project has its own database**: `tinapayanrms_test_infrastructure`, `tinapayanrms_test_presentation`, `tinapayanrms_test_pricing`, `tinapayanrms_test_mcp`, `tinapayanrms_test_recipeapi` and `tinapayanrms_test_usecases`. `dotnet test --solution` runs the projects side by side, and a shared database would be reset under the other project's running test. `Presentation.Tests` and `Infrastructure.Tests` have four database collections each, `DatabaseCollection.A` to `D`, and each collection has a database of its own (the project's, then `_2` to `_4`), so their database tests run four at a time (2026-11-05). With one collection, those tests ran one after another and took about 7 of the suite's 7½ minutes. It took 4 to 6 on this PC, and about 3 since the reset deletes rows rather than truncating tables (2026-11-23), depending on what else is running: with everything at once its processor is the limit (85% on average, paging hard with 8 GB). `Infrastructure.Tests` is the last to finish, held by `DatabaseBackupsTests`, which make, dump, restore and drop whole databases one at a time (three at once made each about three times slower, as every drop checkpoints the server).
- **Each database is migrated once per test run**, by the fixture's `MigrateAsync`.
- **Respawn clears every table** except `__EFMigrationsHistory` before each test, and tries again if PostgreSQL ends it as a deadlock with a write the last test left running. That includes rows the migrations seeded, so a test creates every row it needs. It deletes each table's rows, in the order their foreign keys allow (`FormatDeleteStatement`), rather than Respawn's one `TRUNCATE` of them all: a test leaves a handful of rows, which a `DELETE` clears in about a millisecond, while a `TRUNCATE` gives every table new files and took about 0.3 s a test, three times the median test. With it, `Presentation.Tests` took 211 s alone and `Infrastructure.Tests` 217 s; with deletes, 85–90 s and 100–140 s (2026-11-23).

| Project | What it tests | Needs |
|---|---|---|
| `ApplicationCore.Tests` | `Rules/` and `Validators/`, as pure functions | Nothing |
| `Infrastructure.Tests` | `Persistence/` (repositories and the cascade), `Queries/` (`ColumnsTests`), `Upgrades/` (`DataUpgradesTests`), `Services/` (`JsonViewStateStoreTests`, and `DatabaseBackupsTests`, which run the real `pg_dump` and `pg_restore`) | PostgreSQL, with its command-line tools |
| `Pricing.Tests` | `SavedProductReaderBulkTests` and `SavedProductSectionNameTests` on real repositories; `ProductCostSummaryTests` and `TriedPriceChangeTests` without a database | PostgreSQL for the first two |
| `Presentation.Tests` | The screen states on real repositories, in Presentation's folders, each screen driven on `UiThread`; the drafts' property tests (`SubAssemblyDraftTests`, `RawMaterialDraftTests`, below) | PostgreSQL, but for the drafts' tests |
| `UseCases.Tests` | `SubAssemblies/` (`SubAssemblyEditorTests`, `SubAssemblyRoleEditorTests`): what each refuses, what a Save writes, what it says a Save does to the linking products; `RawMaterials/` (`RawMaterialEditorTests`, `RawMaterialCategoryEditorTests`, `MaterialCostSharesTests`) | PostgreSQL |
| `Components.Tests` | `Shared/` and `Layout/` (components rendered with bUnit, the title bar on a `FakeAppWindow`), `Vim/`, `ServiceRegistrationTests`, `SyncfusionScriptsTests`, and `Architecture/` (see [The architecture tests](#the-architecture-tests)) | Nothing |
| `Mcp.Tests` | `Tools/` (`CostingToolsTests`): the tools read through the server's read-only connection, priced as the screens price, and the connection refusing a write | PostgreSQL |
| `RecipeApi.Tests` | `Endpoints/` (`RecipeApiTests`): each endpoint served in memory on the API's read-only connection, the key when one is set, and the connection refusing a write | PostgreSQL |
| `Tests.Common` | Nothing: it's the shared fixture library | — |

### Running fewer tests while developing

The whole suite takes about 3 minutes on this PC. While you work, two scripts run less of it (2026-11-05). Before a commit or a merge, run it all: both pick by project or by name, so they can miss a test of something a change reaches some other way.

- **[scripts/Test-Changed.ps1](../scripts/Test-Changed.ps1) runs the test projects your changes can reach.** It takes the files changed since `-Since` (`HEAD` by default, so what isn't committed yet, new files included; `-Since main` for the whole branch) and runs every test project that references, directly or through others, a project one of them is in. It reads the references from the `.csproj` files, so it needs no list of its own. A change to the screens (`Components`) runs `Components.Tests` alone; one to `Presentation` runs `Presentation.Tests` and `Components.Tests`; one to the app (`Maui`) runs `Components.Tests`, which reads its IL, and one to a server its own tests and `Components.Tests`; one to `Pricing` or `Infrastructure` runs every project's but `ApplicationCore.Tests`, since `Tests.Common` references both; one to `Domain`, `ApplicationCore`, a build setting (`Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, the solution) or a file outside the projects runs them all. A file another project links in counts for it too: the app's `appsettings.json` (in `src/TinapayanRMS.Maui`) reaches every database test project, through `Tests.Common`. Docs, `scripts/` and `.claude/` run nothing. Several projects run side by side from one `dotnet test`, through a solution filter it writes to `%TEMP%`. `-List` says what it would run and why, and runs nothing; `-Class "*ProductsState*"` runs only matching classes, a project with none passing with "Zero tests ran" (it passes `--ignore-exit-code 8`, the platform's exit code for that). `-Near` picks the classes itself, by name (2026-11-23): for each changed source file, the classes named after it (`ProductRules.cs`: `ProductRulesTests`, `ProductRulesFoldTests`, any `ProductRules…Tests`), and for a changed test file its own classes; it lists each changed file no class is named after, and runs nothing for it, so it's for editing one thing, never the last check. **A project that passed on the same changes isn't run again** (2026-11-23): after each run, every project that passed is stamped, in `%LocalAppData%\TinapayanRMS\Test-Changed\passed.json`, with a hash of what it ran on (the changed files it can see and their contents, the commit compared with, and the classes asked for), and a run whose stamp matches is skipped. After fixing one project's failure, only that one runs again; any edit a project can see, or a commit, runs it again. `-Rerun` runs them anyway. **`Components.Tests` doesn't rebuild the app when the app's own files haven't changed** (2026-11-23): it builds the app only to read its IL, and the app's WinUI XAML compiler (`MarkupCompilePass1` and `2`, with no inputs or outputs MSBuild could check) runs on every build, about 10 of the 13 seconds a build of `Components.Tests` took with nothing changed. With `-p:UseBuiltApp=true` its project file reads the app's assembly from the app's last Debug build instead, and the script passes it when that file is newer than every file in `src/TinapayanRMS.Maui`, so with nothing changed the build takes about 2 seconds. Everything else (`dotnet test`, CI) builds the app.
- **[scripts/Watch-Tests.ps1](../scripts/Watch-Tests.ps1) runs one class again each time you save**, by Microsoft.Testing.Platform's hot reload (`Microsoft.Testing.Extensions.HotReload`, in every test project; it does nothing unless `TESTINGPLATFORM_HOTRELOAD_ENABLED` is `1`, which only this script sets). It starts the project under `dotnet watch`: a change to a method's body, in a test or the code it tests, is applied to the running tests, which run again; anything hot reload can't apply (a new method, type or parameter) makes `dotnet watch` rebuild and start them again (`DOTNET_WATCH_RESTART_ON_RUDE_EDIT`). Measured on `SubAssemblyRolesStateTests` (8 tests): 94 seconds to start, most of it the build, then a broken assertion failed 17 seconds after the save and passed 7 seconds after it was put back. Give it `-Class`, `-Method` or both, which it joins into one `--treenode-filter`; with neither, the whole project runs after each save. Hot reload runs the tests again in the same test session, and TUnit (1.81.16 to 1.87.12 so far) completes the session's dynamic test queue at the end of every run, so the second run's completion threw and every run after the first reported a failure of TUnit's own, "ChannelClosedException: The channel has been closed." [tests/HotReloadReruns.cs](../tests/HotReloadReruns.cs), compiled into every test project, works around it: under hot reload only (`TESTINGPLATFORM_HOTRELOAD_ENABLED`), a `[BeforeEvery(Test)]` hook gives that queue a new channel when the run before completed it, reaching it through TUnit's internals by name. If the failure comes back after a TUnit upgrade, TUnit has renamed what it reaches for; if TUnit fixes it, delete the file. Stop it with Ctrl+C.
- **Don't run a project two ways at once.** A watcher holds its project's build, so a `dotnet build` of it fails on locked files, and it uses the project's test databases, so a second run would reset them under its tests. `Test-Changed.ps1` and `Watch-Tests.ps1` refuse to start while that project's tests are running already.
- **Both hand `dotnet` the repo's path as your terminal spells it.** The build records each project's folder with its drive letter's case, so `C:\…` after `c:\…` would recompile every project (see "Build configuration").

### The database fixtures

- **[DatabaseFixtureBase](../tests/TinapayanRMS.Tests.Common/Fixtures/DatabaseFixtureBase.cs)** (in Tests.Common) reads the app's connection string, swaps in the project's database name, migrates the database, and resets it with Respawn. A database an earlier run left is emptied before it's migrated: the reset between tests empties `data_upgrades` too (the upgrade tests need it empty), so migrating makes every data upgrade again, and on a run's leftover rows one can fail (a folder named as one `ProductFoldersUpgrade` makes), and with it every test of that collection (2026-11-18).
- **`DatabaseFixture`** and **`DatabaseCollection`** are defined in each project's own `Fixtures/` folder. Each `DatabaseFixture` is one line that names its database; `DatabaseCollection` holds the collections' keys. TUnit makes one fixture for each key a keyed `[ClassDataSource]` names, and initializes it (`IAsyncInitializer`) before its first test. `DatabaseFixtureBase` gives the first one of a run the name as it is and the next ones the name with `_2`, `_3` and so on, so each collection has a database of its own.
- **[PersistenceTestBase](../tests/TinapayanRMS.Tests.Common/Fixtures/PersistenceTestBase.cs)** resets the database before each test, in a `[Before(Test)]` hook. It provides `CreateDbContext()`, `CreateDbContextFactory()` (the app's own `SharedTransactionDbContextFactory`, optionally on a `TestClock`), `CreateUnitOfWork()`, the test's `Cancellation` token, and seeding helpers such as `AddIngredientAsync`, `AddRoleAsync`, `AddProductAsync`, `AddProductWithSubAssemblyAsync`, `SetPriceAsync` and `SeedUnitOfMeasureAsync`. With nothing saved in Settings, every product is priced with the 3% default wastage; a test whose figures are about something else calls `WithoutWastageAsync()` (or `SetWastageAsync`) before the screen or reader reads it.
- **[ThrowawayDatabase](../tests/TinapayanRMS.Tests.Common/Fixtures/ThrowawayDatabase.cs)** is a migrated database of one test's own on the same server (`tinapayanrms_test_backup_` and eight characters of a GUID), dropped when the test is done. The backup tests restore over it, which would put every other test out of the shared database.
- **Every concrete test class must carry `[NotInParallel(DatabaseCollection.Name)]` and `[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]`**, or in `Presentation.Tests` and `Infrastructure.Tests` one of `DatabaseCollection.A` to `D` in both. The first keeps the collection's tests from running at once against its database; the second hands the class that collection's fixture, and without it TUnit can't construct the class. The four are balanced by how long their classes take, so put a new class in the one with least in it (`Infrastructure.Tests`' A is `DatabaseBackupsTests`' alone).
- **TUnit runs every other test in parallel, a class's own tests included.** Each test gets a new instance of its class, so its fields are its own, but a static, a file, a window or a database it shares with another test needs a `[NotInParallel]` key they share.

### Writing tests

A rule test needs no fixture:

```csharp
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class CostingRulesTests
{
    [Test]
    public void CalculateSellingPrice_adds_the_markup_then_the_VAT()
    {
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 20m, packagingCostPerPiece: 2m,
            laborPercent: 25m, overheadPercent: 10m, administrativePercent: 5m, markupPercent: 30m, vatPercent: 12m);

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

[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class ProductCategoryRenamePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
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

A test of a screen's state builds it on real repositories, stands in for the dialogs, and drives its commands. Abridged from [SubAssemblyRolesStateTests](../tests/TinapayanRMS.Presentation.Tests/Settings/SubAssemblyRolesStateTests.cs):

```csharp
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class SubAssemblyRolesStateTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRolesState CreateState(bool confirm)
    {
        var state = new SubAssemblyRolesState(
            new SubAssemblyRoleRepository(CreateDbContextFactory()), new SubAssemblyRoleValidator(), new LoggerConfiguration().CreateLogger());
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(confirm));   // stands in for the dialog
        return state;
    }

    [Test]
    public async Task Declining_the_question_saves_nothing()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var state = CreateState(confirm: false);
        await state.LoadCommand.Execute();
        state.SelectedItem = state.Items.Single(r => r.Id == fillingRoleId);

        state.EditingIsProductOwned = false;
        await state.SaveCommand.Execute();

        // … assert on the State, and on a fresh read from the database …
    }
}
```

Some loads start from a selection, with nothing to await. A test waits for the screen to settle with [Settle](../tests/TinapayanRMS.Presentation.Tests/Fixtures/Settle.cs): `await Settle.UntilAsync(state, () => state.AreDetailsReady)` awaits the screen's `WhenIdleAsync` (everything it started has finished, and its results are on the screen), then requires the condition, failing at once if it doesn't hold. `Settle.PickAsync(state, () => state.SelectedProduct = …, condition)` picks first. A command is done running as its `ExecuteAsync` ends, so it's offered again from then: ask `CanExecute()` straight after. Don't poll for a condition: a test then waits as long as the polling does, and can pass on a moment that doesn't last. Only something on a timer, which isn't the screen's work, polls, with `Settle.EventuallyAsync` (the folders written to the view state half a second after a change).

A data-driven test's `decimal` values with decimals come from a `[MethodDataSource]` method, written as `decimal` literals (`0.0525m`), never from `[Arguments]`. An attribute can't hold a `decimal`, so `[Arguments(0.0525)]` passes a `double`, which since .NET 11 arrives as 0.0524999999999999980571097069. Whole numbers are safe in `[Arguments]`. The method returns one value, or a tuple for several parameters; a row holding something mutable, such as an array, is a `Func<…>` that builds it afresh for its test (TUnit0046). See `StandardUnitRulesTests`:

```csharp
public static IEnumerable<(string, string, decimal)> MassUnits() =>
[
    ("pound", "lb", 453.5924m),
    ("ounce", "oz", 28.3495m),
];

[Test]
[MethodDataSource(nameof(MassUnits))]
public void A_mass_unit_of_an_ingredient_is_fixed_in_grams(string name, string abbreviation, decimal grams)
```

**Every async test of a screen's state runs on `UiThread.RunAsync(async () => …)`** (`public Task X() => UiThread.RunAsync(async () => { … });`, Presentation.Tests' `Fixtures/`): a thread of its own with a synchronization context that runs one thing at a time, in order, as the app's UI thread does. A screen only ever runs on its UI thread, and a test runs it the same way: a read nothing awaits (`_ = FetchComponentCostsAsync(…)`, a composite's Products table) or a recalculation that checks it's still the latest (`PriceSimulatorStateTests`) would otherwise come back on the thread pool and change the screen while the test did: about one run in three lost a line's cost or threw "An item with the same key has already been added".

A component test renders the component in a bUnit context that has the app's own services, and drives it through its markup. Abridged from [LineGridTests](../tests/TinapayanRMS.Components.Tests/Shared/LineGridTests.cs):

```csharp
public class LineGridTests
{
    private readonly ObservableCollection<Line> _lines = [new() { Name = "Flour" }, new() { Name = "Water" }];
    private readonly List<Line> _drafts = [];

    [Test]
    public async Task Typing_in_the_add_row_makes_it_a_line_and_a_new_add_row_takes_its_place()
    {
        await using var context = AppServices.NewContext();
        var grid = context.Render<LineGrid<Line>>(parameters => parameters
            .Add(g => g.Items, _lines)
            .Add(g => g.NewItem, NewDraft)
            .Add(g => g.Header, "<th>Name</th>")
            .Add(g => g.Row, line => $"<td class=\"name\">{line.Name}</td>"));

        await grid.InvokeAsync(() => _drafts[0].Name = "Yeast");

        await grid.WaitForAssertionAsync(() => _lines.Select(line => line.Name).Should().Equal("Flour", "Water", "Yeast"));
        grid.FindAll("tbody tr")[^1].ClassList.Should().Contain("add-row");
    }
}
```

- [AppServices.NewContext()](../tests/TinapayanRMS.Components.Tests/Fixtures/AppServices.cs) makes a bUnit context with `ServiceRegistration.AddTinapayanRMS`, Syncfusion's services, loose JavaScript interop (calls into `app.js` do nothing), an in-memory view state and print settings (`InMemoryPrintSettings`, which Export to PDF reads as it prints), and a `FakeAppWindow` for `IAppWindow`, which records what the title bar asked of the window. Nothing in it opens a database connection.
- A test that draws a Syncfusion dialog calls `RenderingAsTheWebView()` on the context once its services are added: the dialog asks the renderer whether it's interactive, and bUnit's says it isn't.
- bUnit's own `TestContext` is aliased away in the project file: `TestContext` is TUnit's.
- Change a screen state or a row from a test inside `InvokeAsync`, as the app changes it on the UI thread.
- **Await bUnit's waits**: `WaitForAssertionAsync`, never the blocking `WaitForAssertion`. A blocking wait holds a thread-pool thread for as long as it waits, and with tests running side by side those threads are the ones the renderer needs to finish what's being waited for. [WaitsSetup](../tests/TinapayanRMS.Components.Tests/WaitsSetup.cs) gives each wait 30 seconds, where bUnit's default is one: a wait that's met ends at once.
- **A component that hooks into what outlives it has a test that it's let go once it's left.** `context.ShowAndLeave<T>(parameters, whileShown)` ([Visits](../tests/TinapayanRMS.Components.Tests/Fixtures/Visits.cs)) shows the component in a page, leaves it as the router does, and returns a weak reference to it. `IsCollected()` is true once nothing holds it. See `A_screen_left_is_let_go_though_its_viewmodel_lives_on`, and its like for `FolderTree` and `LineGrid`. In `whileShown`, check what's drawn with `Find`, not `WaitForAssertion`: bUnit's waits leave a continuation on its renderer that holds the component.

Other helpers:

- `VersionRecorders.Create(factory)` (in Tests.Common's `Fixtures/`) builds a `ProductVersionRecorder` over real repositories, and `CreateSaves` the `VersionedSaves` over one, for a test that constructs `SubAssembliesState` or tests the saves themselves (`Pricing.Tests/Versions`).
- `FailingDbContextFactory` (in Presentation.Tests' `Fixtures/`) wraps a factory and refuses every context while its `Fail` is set: for a test that a screen survives a database that stops answering, and recovers once it answers. A test that a write is all or nothing fails it part-way with an EF Core save interceptor instead (`CostCascadeTransactionPersistenceTests`).
- `SqlCommandCounter` (in Tests.Common's `Fixtures/`) counts the SQL commands sent from where it's started (the activities Npgsql reports on its `Npgsql` ActivitySource): for a test that a read's round trips don't grow with what it reads. Work started in the background from there counts too, so after a screen state command read `await counter.SettledAsync()` (no new command for 300 ms), not `Commands`: a load's background reads, such as the Products list's below-markup flags, are often still going when the command returns.

### The costing golden master

`CostingGoldenMasterTests` (Mcp.Tests) prices every product of a copy of the shop's data and compares each figure with the approved ones, so a costing change that moves a price fails the suite, and CI.

- **The copy** is `Golden/costing-fixture.backup`: a `pg_dump` of the dev database with its names replaced by ids (materials, products, sub-assemblies, sections, categories, folders and brands), and its product versions, price scenarios and password taken out. Its quantities and prices are the shop's, and role and unit names stay, since a unit's name decides its multiplier. It comes in through Copy In and Restore, so every migration added since it was taken runs on it.
- **The figures** are what `CostingTools` answers at both cost bases: the product list, every product's cost sheet and one simulation of four price changes, to the 6 decimal places the tools write. Text is left out, since a reworded label moves no figure.
- **A change meant to move a figure** approves the new ones, then shows them for reading in the diff of `Golden/costing.approved.txt`:

  ```powershell
  $env:TINAPAYANRMS_APPROVE_COSTING = '1'
  dotnet test --project tests/TinapayanRMS.Mcp.Tests --treenode-filter "/*/*/CostingGoldenMasterTests/*"
  Remove-Item Env:TINAPAYANRMS_APPROVE_COSTING
  ```

### Benchmarks and performance budgets

What a refactor could make slower is measured on the golden master's copy of the shop's data, never the dev database, three ways ([ADR 0008](adr/0008-benchmarks-and-performance-budgets.md)): budgets the suite holds, timings of two builds compared side by side, and the start.

- **The scenarios** ([tests/TinapayanRMS.Benchmarks/Scenarios/](../tests/TinapayanRMS.Benchmarks/Scenarios/), listed by name in `ScenarioCatalog`): the screens' reads through their use cases and `SavedProductReader`; the Price Simulator's costing of the golden master's four price changes; a price save of the fixture's flour, with its cost cascade; Products opened on its heaviest product, a product picked after another, and Raw Materials and Sub-Assemblies opened; and the app's start (`Startup/AppStart`: the services built, the database made ready, the sign-in panel's load, Raw Materials loaded). Each is built from the app's own registrations (`ServiceRegistration.AddTinapayanRMS`), runs a screen on a one-thread synchronization context as the window's UI thread, waits for it to settle, and checks its result, so one that fails shows rather than timing its error.
- **Their databases** are named `tinapayanrms_bench_*` (`BenchServer`; nothing else may be touched), restored from `Golden/costing-fixture.backup` as the golden master restores it, vacuumed and analyzed before anything is timed, with a copy (`CREATE DATABASE … TEMPLATE`) for the cascade's saves, and dropped as the run ends. `ThrowawayDatabase.CreateAsync` takes their names. A database just restored has no planner statistics, so autovacuum analyzes it in the middle of a run, at a moment that differs from run to run: before the statistics were made first, an unchanged tree compared with itself came out a third slower in three reads, every round.
- **The budgets** ([Benchmarks.Tests' `BudgetTests`](../tests/TinapayanRMS.Benchmarks.Tests/BudgetTests.cs)): after two warm runs, each scenario's SQL commands (`SqlCommandCounter`, settled) must equal the number approved in `Budgets/budgets.approved.txt`, and the least it allocates in three runs must be within 10% over and 20% under the kilobytes approved. Counts, not times, so they hold in the suite. The project runs alone in its process with tiered compilation off, so a later optimized recompile can't move what a scenario allocates. A change meant to move one approves the new figures, as the golden master's are, then shows them in the diff: the commit says why a budget went up.

  ```powershell
  $env:TINAPAYANRMS_APPROVE_BUDGETS = '1'
  dotnet test --project tests/TinapayanRMS.Benchmarks.Tests
  Remove-Item Env:TINAPAYANRMS_APPROVE_BUDGETS
  ```
- **The timings**: `dotnet run -c Release --project tests/TinapayanRMS.Benchmarks -- [--quick] [--scenario "Screens.*"] [--artifacts <folder>]` restores the databases and times the scenarios with BenchmarkDotNet, in process (`Suites/`: `ScenarioBenchmarks` for the reads, as many runs an iteration as fit; `SteppedBenchmarks`, one run an iteration, for the scenarios that need something between runs that isn't timed). Its results go to `%TEMP%\trms-bench\artifacts` unless `--artifacts` says where. `list` prints the scenarios' names.
- **The cold start**: `startup prepare --database tinapayanrms_bench_<name>` makes a database the start runs on (the fixture at that build's migrations, its password `bench-start`, the auto-lock off), `startup sample --database …` takes one cold start in its own process and prints a line of JSON (each stage's milliseconds, then SQL commands, methods JIT-compiled, assemblies loaded, bytes allocated and collections), and `startup drop --database …` drops it with its backups; `clean` drops what a killed run left. A sample loads nothing a start of the app doesn't (`BenchServer`, not the fixtures' assembly).
- **Comparing two builds**: [scripts/Compare-Benchmarks.ps1](../scripts/Compare-Benchmarks.ps1) checks the base out in a worktree at `%TEMP%\trms-bench\base` (kept between runs), builds both sides with `-c Bench -p:Optimize=true` into `%TEMP%\trms-bench` (your Debug build and the MCP server's Release one stay as they are), and runs them in alternating processes: `-Rounds` benchmark runs a side (4), `-StartupRuns` cold starts (10, after one each not counted), and with `-Window` `-WindowRuns` starts of each side's published app (10). A time is worse when the median of head ÷ base over the pairs (the table's Change) is more than `-Threshold` (10%) over 1, and chance alone would show head that much slower 1.5% of the time or less: every head round beyond every base round (1 in 70 with 4 a side), or head slower in 9 pairs of 10. A table holds some twenty times, so a looser bar (4 rounds of 4 slower, 6%) would trip on an unchanged tree often. With too few pairs ever to tell (`-Quick`'s 2 rounds) it says `slower?` and fails nothing. A count (SQL commands, assemblies) is worse by one, memory over 5%, methods compiled over 2%. It prints the table, writes it to `summary.md` in the results folder it names, with what each side measured (JSON) and every run's log, and exits 1 when anything got worse; `-Results <folder>` works an earlier run's table out again, with another `-Threshold` say, running nothing. `-Base` takes a commit or a folder holding a copy of the repo, `-Head` likewise (the working tree by default), `-Filter` the scenarios (`'Screens.*,Pricing.ReadAll'`), `-Only Benchmarks|Startup|Window` runs one part, and `-Clean` removes the worktree, the builds, the results and the copies' web view folders, and drops any leftover database.
- **The window's start** (`-Window`) publishes each side as `Install-TinapayanRMS.ps1` does (so, as that script does, it builds Release in each tree, the working tree's `bin` and `obj` included, and the MCP server's next start rebuilds what it changed) and starts it on its prepared database with WebView2's debugging port at 9223 and the web view's background throttling off; a helper thread moves the window past the desktop's right edge as it appears, and notes when it showed, and [scripts/benchmarks/window-start.js](../scripts/benchmarks/window-start.js) marks in the page when the sign-in panel can be typed into and, once it has typed the password and Enter, when Raw Materials' list shows. Marks are DOM changes (a `MutationObserver` it puts in the page), not paint: an off-screen web view may not paint. It types as a person would, once the panel has put the cursor in the box itself and at a person's pace, then waits for the whole password to be in the box before Enter, and types again if the panel refuses it: typing the moment the box showed lost keys to the panel's own focus landing partway. None of that is timed, as the list's time runs from the Enter that signed in. It attaches while the page may still be on its first navigation, so each of its queries is asked again of the new page. The window opens on the mouse's monitor for a moment at each start and can take the keyboard, so run it while you aren't typing. It needs every copy of the app closed (they share your `view-state.json`, which it copies first and puts back after, and meanwhile takes the backups' second folder out of, so no copy's automatic backup of its database lands there), and the starts write to your app log. It deletes the copies' automatic backups as it drops their databases.
- **Reading a comparison**: on this laptop (2026-11-21) an unchanged tree compared with itself, the default run of about ten minutes, came out "same" everywhere, its benchmarks' Change within ±9% and the cold start's stages within ±5%, its counts equal and its memory within 2%. One process can be 30% off another for the same build (a read run side by side especially), which is why the verdict asks for every round, not a median. Run it with nothing else busy, and not straight after the test suite. A change smaller than the noise can't be shown this way: count its queries or allocations instead.

### Checking a screen in the app's window

bUnit renders a component's markup and raises its events; it has no web view. Focus, typing at a person's speed, drag and drop, the print styles, the dialogs, all of `vim.js` and the title bar are only seen in the app's window. Check a change to a page or a shared component there before you call it done.

Run the app (`dotnet run --project src/TinapayanRMS.Maui`) and use the screen as a person would: type quickly, tab through it, undo, drag, print. For a check you can repeat, drive the window from a script with the `run-tinapayanrms` skill's driver ([.claude/skills/run-tinapayanrms/](../.claude/skills/run-tinapayanrms/); its `SKILL.md` lists the commands). `driver.ps1 launch` starts the built app with WebView2's debugging port open (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`), and `driver.js` attaches Node's `playwright-core` to it, with no browser to download:

```js
const { chromium } = require('playwright-core');
const browser = await chromium.connectOverCDP('http://localhost:9222');
const page = browser.contexts()[0].pages()[0];   // the window's page, at https://0.0.0.1/
```

- Its clicks, keys and screenshots go to the page, not the mouse or the screen, so nothing else on the computer is touched. The window opens on the monitor the mouse is on.
- The page's screenshot leaves out the window's frame. `driver.ps1 window` captures the whole window with `PrintWindow`, and `driver.ps1 hit` asks Windows' hit test (`WM_NCHITTEST`) at points on the title bar: caption in the gaps, client on the buttons.
- **`driver.ps1 smoke` is the check before an install**: every address, then a record of each record screen (one that isn't picked already), failing on the page's error bar, a screen's error panel, a red toast or an error in the page's console (the web view's favicon request aside). `TINAPAYANRMS_EXE` points the driver at another copy, so the commit to install is checked as the shop will run it: install it into a trial folder (`Install-TinapayanRMS.ps1 -InstallFolder <scratch folder> -NoDesktopShortcut -NoStartMenuShortcut`), start that exe with the debugging port, and run `smoke`. Opening records writes the lists' open folders to `view-state.json`: copy it first, and put it back with the app closed.
- Wait for what you expect to appear, not a fixed time. Type with `page.keyboard.type`, not `fill`, to send real keystrokes at speed.
- `drag_to` scrolls the target into view between pressing and moving, which makes the web view start the drag from whatever is then under the pointer. Drag between rows that are both on screen, or drive it with `page.mouse` (`move`, `down`, `move` in steps, `up`): the lists and the tables drag by pointer events, which those make as a hand does, and holding near a list's bottom edge scrolls it. Press Escape before `up` to see a drag through without dropping anything.
- Collect the console's errors (`page.on("console", …)`): a broken page says so there. In a Debug build, F12 in the window opens the developer tools.
- The dialogs (Save, Open, folder, print) are the computer's, outside the page, so a script can't answer them: check them by hand.
- Windows PowerShell's web requests look for a proxy first, which can take longer than a short timeout, so the driver tests the debugging port with a TCP connect.
- **It starts signed in**: a Debug build with `TINAPAYANRMS_SIGNED_IN=1`, which `launch` sets, skips the sign-in panel ([Signing in](#signing-in)); `TINAPAYANRMS_SIGN_IN=1` starts at the panel instead. A Release copy (`TINAPAYANRMS_EXE`) always asks for the password.
- **The check runs against the dev database, the user's real data.** Save nothing: leave an edit with No when the window asks, and check afterwards that nothing's `updated_at` moved.

### The architecture tests

[Components.Tests' `Architecture/`](../tests/TinapayanRMS.Components.Tests/Architecture/) checks the rules of [ADR 0002](adr/0002-layers-use-cases-and-drafts.md), [ADR 0004](adr/0004-quality-gates.md) and [ADR 0010](adr/0010-the-gates-read-method-bodies.md) by reflection, and for what's only in a method's body from its IL (`IlReader`), with no database. The app (`TinapayanRMS.Maui`, Windows only) and the two servers aren't referenced, only built first (`ReferenceOutputAssembly="false"`, their files named in the test assembly's `AssemblyMetadata`), and their IL is read from those files without loading them (`UnreferencedAssemblies`, by `System.Reflection.Metadata`), so a change to the app's code runs Components.Tests too:

- **[LayerTests](../tests/TinapayanRMS.Components.Tests/Architecture/LayerTests.cs)**: each project under `src` references only the layers in `MayReference`, read from its `.csproj` (so the app and the two servers are checked too), and the layers below the screens use no UI framework, nor the screens' Rx and DynamicData. A new project fails until it has a place there, decided in an ADR. `StillThere` lists a reference the layers don't allow that's still there, with what takes it away: empty since 2026-11-21, when Presentation's to Infrastructure went (ADR 0002, phase 4), so none may be added.
- **[DataAccessTests](../tests/TinapayanRMS.Components.Tests/Architecture/DataAccessTests.cs)**: no type in Presentation or Components holds a repository, the unit of work, the DbContext, another port Infrastructure implements (but `IViewStateStore`, a file), or a service built on them (`SavedProductReader`, `VersionedSaves`), in a field, its constructor or an `[Inject]` property, except as many as `StillReaching` lists for it; EF Core's, Npgsql's and Dapper's types and any ADO.NET connection, command or data source count too. And no method of theirs outside `ServiceRegistration` reaches one without holding it: resolved from the services, `typeof`, made, cast to or called (a static calculation such as `SavedProductReader.Build` is let through). Nor does one ask the services for anything, or make anything with them, by a `Type` (`GetService(type)`, `ActivatorUtilities.CreateInstance(services, type)`), whose IL shows only a variable: ask by a type argument or an `[Inject]` property, which can be read.
- **[ConstructorDependencyTests](../tests/TinapayanRMS.Components.Tests/Architecture/ConstructorDependencyTests.cs)**: a class deriving from `ObservableState` takes at most six things through its constructor, except as many as `StillOver` lists for it.
- **[FileSizeTests](../tests/TinapayanRMS.Components.Tests/Architecture/FileSizeTests.cs)**: a file of Presentation's is at most 700 lines, except as long as `StillOver` lists it, a length lowered once the file is 25 lines shorter (ADR 0002's amendment of 2026-11-21: what was longer, a screen's coordination, may only shrink).
- **[DecimalNumbersTests](../tests/TinapayanRMS.Components.Tests/Architecture/DecimalNumbersTests.cs)** and **[RoundingModeTests](../tests/TinapayanRMS.Components.Tests/Architecture/RoundingModeTests.cs)**: no `double` below the screens, none turned into a `decimal` there, in Components, in the app or in the two servers, and every rounding names its mode (see [Precision and rounding](#precision-and-rounding)).

**The lists are ratchets.** They hold what was over a rule on 2026-11-20, and they only shrink: a type that holds or takes more than its listed number fails, one not listed may hold none, and one that holds or takes fewer fails too, saying what to lower its number to, or to take it out. So the change that moves a screen's reads to a use case lowers its number in the same commit, and nothing can creep back up to it. Never raise a number or add a type to make a test pass: split the class, or reach the database through a use case.

### Property tests of a draft

A record screen's draft is tested on its own, with no database and no UI thread (its form and view may use the database, [ADR 0009](adr/0009-close-the-migration.md)), by [CsCheck](https://github.com/AnthonyLloyd/CsCheck) ([ADR 0006](adr/0006-testing-strategy.md)). [SubAssemblyDraftTests](../tests/TinapayanRMS.Presentation.Tests/SubAssemblies/SubAssemblyDraftTests.cs) builds a draft over lists of its own (`World`, which is also its undo recorder) and an `UndoHistory` recording only the user's steps, then runs hundreds of random sequences of what a user does to it (a name, a role, lines added, typed into in grams or as a percentage, ticked as the base, taken out, a packaging set's lines), each an undo step, with undos, redos and Saves among them, and loads that bring fresh copies of the roles and materials, as saved elsewhere since: repriced, renamed, both or neither. Beside the real history, [UndoModel](../tests/TinapayanRMS.Presentation.Tests/Fixtures/UndoModel.cs) keeps what it should hold: every state a kept step left, where the history stands among them, and what was last saved. After every step:

- the draft is where its history says it is, and Undo and Redo are offered exactly when there's somewhere to step;
- the dirty check says whether the draft differs from what was last saved;
- a load changes nothing the draft saves, and leaves it referring to the load's copies, by id;
- each figure the draft works out (a `Derived<T>`: the batch's figures, the packaging set's cost) is what its lines give as they stand, worked out afresh, the harness marking nothing but what the form's own code would ([ADR 0012](adr/0012-figures-hear-of-a-change-from-whoever-made-it.md));
- a Save is refused when a rule would refuse it, and else, the sub-assembly shown again as saved, writes what was saved.

Then every step is undone and redone, each checked the same. Putting back what was captured changes nothing. A failure is shrunk to the shortest sequence that shows it, and the message gives the seed that reproduces it (`CsCheck_Seed`). A step is a record, so the sequence prints as it was run. A new kind of edit a screen gains is a new step, generated beside the others; a bug the screen shows in a sequence of edits becomes an invariant here, or a fixed sequence if it's one case.

**A step does only what the screen lets the user do**, and a part's Save (Products' parts are saved together, by the form) is what the form's dirty check sees of one: what was written is what's measured against, with nothing to undo, its reload the form's, tested on the database. The steps only the form has, a recipe tab picked and the section form's figures, are [ProductsStateSequenceTests](../tests/TinapayanRMS.Presentation.Tests/Products/ProductsStateSequenceTests.cs)', on the database: random tab picks, typing into the section form, undos, redos and Saves on a two-section product, after each of which the tab strip and the form show the same section and the form's figures are worked out afresh (`UsageFormFigures.Of`), and with every step undone there's nothing to save. Its sequences are made by CsCheck up front from a seed each message names, and run on the form's `UiThread`, which CsCheck's own sampling doesn't keep to.

[RawMaterialDraftTests](../tests/TinapayanRMS.Presentation.Tests/RawMaterials/RawMaterialDraftTests.cs) does the same for Raw Materials' draft, from a new material: its steps are every field and table (a name, a type, a source, a category, a weight loss, a link and its shares, a base unit, the cost buffer and preferred brand, brand prices, alternative units, a made ingredient's sub-assembly and add-ins), its dirty check is measured as the form's is, by what a Save would write (`SavesSameAs`), and a made ingredient's totals are checked after every step against its sub-assembly's lines (read as the form reads them, on a pick and on every load) and its add-ins, less the weight loss (`IngredientBomRules.CalculateCost`). Its steps do only what the screen lets them: a field the form hides for the material's type or source isn't typed into, and a standard unit's multiplier (a kilogram's, for an ingredient in grams) isn't typed, as the Alternative Units table doesn't let it be. Typed anyway, the restore and the load put the standard back, which the properties first reported as failures of the draft.

[ProductDraftTests](../tests/TinapayanRMS.Presentation.Tests/Products/ProductDraftTests.cs) does it for what the product's draft adds over its parts': its fields, the costing and the packaging under undo and redo, and the costs following the fields after every step (the quantity they're for, the current price, the category's own wastage, the packaging), the harness telling the costing of nothing the fields tell it themselves.

[ProductRecipeDraftTests](../tests/TinapayanRMS.Presentation.Tests/Products/ProductRecipeDraftTests.cs) does it for the recipe's sections, from a saved product's dough of its own and linked topping, its dirty check and a load measured by what Save writes (the sections compare at their ingredients' costs too, so a load that moves them changes what they show), with properties of its own: the sections stay in recipe order, told apart and marked when their role repeats, the recipe's figures (`Priced`) are its sections folded afresh, a recipe that saves as loaded is as saved, and undoing every step gives back where it started at the costs the last load brought, every line at the ingredients as last loaded (`ProductRecipeDraft.Restore`). Its loads also bring what was saved elsewhere (a linked sub-assembly's lines, its name, its role, a role made product-owned), every undo step rewritten as the form rewrites it (`StepAsSavedNow`). Those found, on 2026-11-24, that a step kept a section in its old place when a role moved it, and linked when the product had taken it as its own; a step is now in recipe order again, and takes a section the product owns now as its own, as the draft does.

[ProductCostingTests](../tests/TinapayanRMS.Presentation.Tests/Products/ProductCostingTests.cs) does it for the costing, with what its figures come from changing between steps as the form changes it (a recipe edit on another tab, the cost basis, Settings' rates read again), and a property of its own: **the figures are never stale**, a fresh pricing of the inputs and percentages as they are now after every step, which fails if any of them stops marking the figures stale.

[ProductComponentsDraftTests](../tests/TinapayanRMS.Presentation.Tests/Products/ProductComponentsDraftTests.cs) does it for a composite's Products table, with the costs a line wants arriving between steps, and a property of its own: every line costs its quantity at what its product brings, as read, its packaging in only while it's ticked. Its first runs found that a cost kept for a product taken out of the table outlived the load that would have refreshed it, and priced the product as it was before the load if it was picked again; a load now drops the costs of products no line holds.

[ProductPackagingDraftTests](../tests/TinapayanRMS.Presentation.Tests/Products/ProductPackagingDraftTests.cs) does it for a product's packaging, from a saved product's, its own lines or linked to a set: lines typed, picked, given a unit and taken out, a set linked or a product's packaging copied in, Make a Copy, Remove Link, Move to Sub-Assemblies, bulk lines and how many a pack holds. It adds a property of its own: **packaging that shows as loaded saves nothing** (`ChangedLink`, `ChangedLines` and `ChangedBulk` all null). Its first runs found that a row shown from a save or the picker, its material then picked, went on pricing the material it had until the form resolved it; the draft now resolves a row as its material is picked.

### Naming

Test classes are named `<Subject>Tests`, such as `CostingRulesTests` or `ProductSavePersistenceTests`. Test methods are sentences with underscores: either `Method_scenario_result` (`EnsureValidParameters_rejects_negative_labor_cost`) or a plain statement (`Declining_the_question_saves_nothing`). CA1707 is turned off in `tests/` for this.

### Coverage

Coverage comes from Microsoft's MTP coverage extension (see the [command reference](#command-reference)); `coverlet` doesn't work under Microsoft.Testing.Platform. When you read the raw report, ignore `Infrastructure/Migrations`: its generated code never runs under the reset-a-migrated-database model, and it drags Infrastructure's raw figure down.

---

## 11. Conventions

### Code

- **Use modern C# 15 and .NET 11 idioms**: primary constructors (every repository has one), collection expressions, `record` and `record struct` for immutable shapes (drafts, snapshots, printouts), `required` members, file-scoped namespaces and pattern matching.
  - **A fixed set of cases is a `closed` class or record** (C# 15): the list trees' nodes, `RecipeTab`, `PrintScope`, `RecipeTabChoice`, `PackagingLinkDraft`, `PackagingLinkTarget`, `SectionPlacement`. Each case is a sealed record nested in it, with only its own data, in place of a `Kind` enum, nullable fields that only some cases use, or flags that overlap. A switch over one names every case, and `null` when null can come, never `_`, so a new case is a CS8509 warning wherever it's missed.
  - **A `union`** (a struct holding its case in `Value`) suits a value that's one of some types already there: `PreparedPrintout(ProductPrintout, PrintProblem)`, what `PreparePrintout` gives the Print dialog, empty (`default`, matched by `case null`) while there's nothing to print. In a `?:` whose other branch is a case, write `default(PreparedPrintout)`, since a bare `default` takes the case's type. Tests read the case through `Value`.
  - **A closed record that's shown seals its `ToString`** (`ProductCategoryFilter`), or each case reads "Category { Label = … }".
  - **`BeEquivalentTo` over a closed base type** needs `o => o.ComparingByValue<TBase>()`, since the base has no members to compare.
  - **`[with(comparer), ..]`** for a collection made with a comparer or capacity, and **labeled `break`/`continue`** in place of a flag or `goto` out of nested loops (IDE0410).
  - **.NET 11's library additions in place of hand-written code**: `Enumerable.FullJoin` for a full outer join by a key that comes once on each side (`ProductVersionRules.Paired`), and `Process.RunAndCaptureTextAsync` to run a tool and read what it writes (`PostgresTools.RunAsync`).
- **Nullable reference types are on everywhere.** Treat their warnings as real.
- **Everything that touches the database is `async`** and takes a `CancellationToken`. Screen states pass `CancellationToken.None`.
- **A rule throws `BusinessRuleException` with a message written for the shop's staff**, because `RunSafelyAsync` shows it exactly as written. That goes for a repository's checks and a screen's own form checks too. A plain `InvalidOperationException` reads as a bug: EF Core and Npgsql throw those for failures.
- **Strings**: compare names with `StringComparison.OrdinalIgnoreCase`; sort for display with `StringComparer.CurrentCultureIgnoreCase`; format display dates with `CultureInfo.CurrentCulture`, and log output with `CultureInfo.InvariantCulture`.
- **Correctness and performance come before brevity.** Query only the rows and columns you need, rather than loading more and filtering in memory. Avoid needless intermediate collections in hot paths such as the cascade.
- **Comments explain why.** Types and non-obvious members have XML doc comments that give their domain meaning. Inline comments record a decision and its reason. A comment says what's true now, in the present tense, with no date and no account of who asked for it or how a bug was found: the build plan's change log and git keep that. Match the comment density of the code around yours.
- **Seal every class that isn't a base class** (`ScreenState`, `ObservableState`, `ObservableRow`, `ScreenCommandBase` and `ScreenBase` are the base classes in `src`), so the JIT can devirtualize calls and type checks are cheaper. A fixed lookup table is a `FrozenDictionary`, and a lookup by a trimmed name uses its span-keyed `GetAlternateLookup<ReadOnlySpan<char>>()` rather than allocating the trimmed string (`StandardUnitRules`).
- **Keep the build free of warnings.** To look for more performance problems than the default rule set finds, rebuild with `-p:AnalysisLevelPerformance=latest-all`. On 2026-10-29 that left only CA1849 on FluentValidation's `Validate` (an in-memory check; `ValidateAsync` only wraps it) and on tests calling the synchronous methods deliberately.

### Documentation and process

- **The user manual follows the app.** A change users can see updates [docs/user-manual.md](user-manual.md) in the same change. The manual is written for the shop's staff: it describes current behaviour only, shows labels in bold exactly as the screen does, gives amounts without a currency symbol, and uses no code names.
- **PLAN.md is the living plan.** Record each decision there, with its reason, and keep its sections current; an architecture decision is also a record in `docs/adr/` (below), which PLAN.md links for why. History belongs in the build plan's log; PLAN.md and the manuals describe only what's true now.
- **This manual follows the code.** A change to anything it describes (the setup, commands, solution layout, architecture, a pattern or convention, or a type or file it names) updates it in the same change. Like the user manual, it describes only what's true now. `DocPathsTests` fails on a path a guide names that the repository doesn't have.
- **Decisions about the architecture are records in [docs/adr/](adr/README.md)** ([ADR 0001](adr/0001-record-architecture-decisions.md)): one file each, with the context, the decision, what it costs and the alternatives turned down, its text left as accepted but for its status. A change that adds a project, a layer, a framework or a library the app's code is built around, or that changes a rule a record set, a new kind of gate included ([ADR 0010](adr/0010-the-gates-read-method-bodies.md)), starts with a record. PLAN.md and this manual describe what the records decided.
- **One structural change at a time** ([ADR 0013](adr/0013-ci-on-github-changes-straight-to-main.md)): finished and checked in the window before the next begins, and never in the same commit as a feature. Every change goes straight to `main`, checked by the CI run its push starts; a structural one is reviewed first (`/code-review` of the working tree, its findings fixed or answered), and its row in the build plan's log says what the review found.
- **Dependencies are updated by Dependabot** ([.github/dependabot.yml](../.github/dependabot.yml), [ADR 0013](adr/0013-ci-on-github-changes-straight-to-main.md)): weekly pull requests for the NuGet packages, the .NET SDK and the CI workflow's actions, grouped as Directory.Packages.props says packages must move together, each merged on its own once CI passes on it. No prerelease package goes into a release build unless Directory.Packages.props names it with what it waits for.
- **Verify a UI change in the app's window**, not just in the tests ([Checking a screen in the app's window](#checking-a-screen-in-the-apps-window)). Close any copy you already have open first.
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
7. In the screen state, add the editing property, its load and save mapping, and its place in the form snapshot (so undo and the dirty check include it). Bind it on the page, with the shared field for its kind (`TextField`, `NumberField`, `PickField`).
8. Add tests: rules, persistence, and the screen state.
9. Update the user manual if users can see the field, and PLAN.md's Domain Model section.

### Add an entity and its table

Everything in the previous recipe, plus:

1. A `DbSet` in `TinapayanRMSDbContext`, named as the table should be.
2. An `IEntityTypeConfiguration<T>` in `Configurations/`, with the unique indexes, delete behaviours and check constraints the rules need.
3. A repository interface in `ApplicationCore/Interfaces`, and its implementation in `Infrastructure/Repositories`, taking `IDbContextFactory<TinapayanRMSDbContext>` through its primary constructor. Reads use Dapper and `Columns`; writes use EF Core, in a transaction begun with `BeginOrJoinTransactionAsync` when they need one.
4. Registrations: the repository as a singleton in `DatabaseHost`, and `IValidator<T>` in `ServiceRegistration` if the entity has a Save form.

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
5. Run [the costing golden master](#the-costing-golden-master). If the change is meant to move figures, approve them and read the diff of what moved.
6. Update the user manual's [How costs are worked out](user-manual.md#12-how-costs-are-worked-out), including its worked example, in the same change.
7. Record the decision in PLAN.md.

### Add a screen

1. Write the screen state in a folder for the screen in `Presentation/` (its namespace is the folder's), deriving from `ScreenState`. Make it `sealed`, and `IDisposable` if it owns a `SourceCache`. Give it a fetch-then-merge `LoadCommand`. Register it as **scoped** in `ServiceRegistration`.
2. Write the page in `Components/Pages/`: `@page "/your-screen"`, `@inherits ScreenBase<YourState>`, a `<PageTitle>`, and an override of `LoadCommand`. Build it from the shared components. A record screen also takes `/{Id:int}`, as Products does.
3. Add it to `ActivityBar`'s items, and give it an `AppScreen` value and its address in `VimKeysState.UrlOf`. If the Vim keys should reach it by name, add a command in `VimCommandLine.Commands`.
4. If another screen opens it, expose the request as an observable on that screen's state and subscribe in `ScreenLinks`.
5. Add its state to `ServiceRegistrationTests`, and write tests of its state.
6. Check it in the app's window, and add a section to the user manual.

### Add a Settings tab

1. Write the screen state and register it, as for a screen.
2. Write the page in `Components/Pages/Settings/`, with `@page "/settings/your-tab"` and `<SettingsTabs />` at the top. Name it as its state is, without the `State` (`SubAssemblyRoles.razor` over `SubAssemblyRolesState`).
3. Add its link to `SettingsTabs`.

### Export a table to Excel

1. In the screen state, add a command that runs `ExportToExcelAsync("<title>")`. The title names the file and lets the page tell several exports apart.
2. In the page's `OnInitialized`, after `base.OnInitialized()`, register a handler on `State.ExportToExcel` with `AddSubscription`. Ask where with `IFileDialogs.ChooseSaveFileAsync`, answering null if it's cancelled. For an `SfGrid`, set `AllowExcelExport` and write what `GridExports.WorkbookAsync(grid, properties)` returns there (the grid's `ExportToExcelAsync(asMemoryStream: true, …)`, read with `ToArray()`, since the stream comes back closed). Otherwise build the sheets with `ExcelWorkbook` and save them with `IFileDialogs.SaveAsync`. Answer with the path.
3. Add an **Export to Excel** `CommandButton` with `Icon="AppIcon.ExportToExcel"`.
4. Test the command with a handler of your own on `ExportToExcel`.

### Use a new Syncfusion component

1. Add its package to the Components project, as in [Working with Syncfusion](#working-with-syncfusion).
2. Put its name on the list in `Components/wwwroot/syncfusion.js` (`SfGrid` is `sfgrid`), so the page loads its scripts. `SyncfusionScriptsTests` fails until it's there, and if it's there but has no script.
3. Learn how it's meant to be set up from the `syncfusion-blazor` MCP server (Syncfusion's Blazor documentation), then `ilspycmd`.
4. If the user types into it, check the [three rules](#typing-three-rules) hold: type quickly into it in the app's window while the screen state changes under it.
5. Check it at the app's text size, and that the theme's colours reach it.

### Add a shared component

1. Write it in `Components/Shared/`, with a comment at the top saying what it's for and what it takes care of. Take state as parameters; keep only view state (focus, a drag in progress) in the component.
2. If it's typed into, take `[CascadingParameter] IUndoRecorder? Recorder` and mark the undo step as the fields do. If it keeps its own text while it has the cursor, give the box up when `Recorder.FormVersion` changes, as `TextField` does.
3. Test it in `Components.Tests/Shared` with bUnit, and in the app's window.
4. Add it to the table in [The shared components](#the-shared-components).

### Ask something of the window

1. Add the member to the interface in `Components/Shared/` (`IAppWindow` for the window's frame, `IFileDialogs`, `IPagePrinter`), with a comment saying what it does, not how Windows does it.
2. Implement it in `Maui/Platforms/Windows/` (`AppWindowChrome`, `FileDialogs`), on the window's thread: WinUI's window and pickers only answer there.
3. Teach the fake in `Components.Tests/Fixtures/` (`FakeAppWindow`) to record it, and test the component that asks.
4. Check it in the app's window: `driver.ps1 window` and `hit` see the frame.

---

## 13. Diagnostics and troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| The window shows Syncfusion's licence notice, and the log says no licence key was found | It was built while `SYNCFUSION_LICENSE_KEY` wasn't visible to the build | Set the user environment variable, restart your terminal or IDE, then rebuild |
| `dotnet publish` fails asking for `SYNCFUSION_LICENSE_KEY` | The variable isn't visible to the build, and an app published without it would show the licence notice | The same |
| The app doesn't open, and a message box says it couldn't start | It failed before its window: the message and the log's last lines say why | Read `%LocalAppData%\TinapayanRMS\Maui\logs\`. A missing MAUI workload shows at build time instead (NETSDK1147): install it |
| The app doesn't open, and a message box says it's open in another Windows sign-in | One copy per PC (`OneCopyPerPc`) | Close it there; `Get-Process TinapayanRMS.Maui \| Select Id, SessionId` finds it |
| The app doesn't open, and there's nothing new in the log | A copy from the same folder is still running, and it brought that one forward | Find it with `Get-Process TinapayanRMS.Maui`; one with no window is stuck, so end it |
| A screen shows "This screen ran into a problem it didn't expect" | An exception escaped the screen (`ScreenErrorBoundary`) | Read the log for its stack ("A screen failed"); Open It Again shows the screen as it was |
| Every screen says the database doesn't exist after a restore was cut short | The app ended between the restore's two renames | Start the app again: `StartupTasks` renames `<database>_replaced` back (`RecoverInterruptedRestoreAsync`), and logs that it did |
| A screen says "Couldn't reach the database.", and the log has "Failed to connect" or "password authentication failed" | PostgreSQL isn't running, or the password file has no line for the server and role | Start the `postgresql-x64-18` service, and check `appsettings.json` and `%APPDATA%\postgresql\pgpass.conf` |
| A banner under the title bar says the database is out of date | A migration hasn't been applied to it | Stop the app, run `dotnet ef database update`, start it again |
| A bar at the bottom of the window says something went wrong and the page needs reloading | An exception escaped a component outside the screens (the title bar, the layout) and broke the page | Read the log for its stack; in a Debug build the developer tools' console (F12) has it too |
| "More than one sibling of element 'tr' has the same key value" in the log | One object drawn as two rows in a keyed loop | See [Typing: three rules](#typing-three-rules) |
| Characters go missing when typing quickly | Something wrote into the box while it had the cursor | Use `TextField`, `NumberField` or `PickField`; see the three rules |
| A picker shows blank for the first item of a list | It's bound by value | Use `PickField` |
| A change to a `.css` or `.js` file doesn't show | The app is still the build from before | Close it and run it again |
| A strip across the top of the window, over the page's title bar | MAUI's own title bar row is back: an update renamed what `AppWindowChrome` reaches for | The log has a warning naming it. Find the new name with `ilspycmd` in MAUI's Windows assembly ([docs/plan/maui-app.md](plan/maui-app.md), "The window") |
| The title bar's gaps don't drag the window, or a button doesn't click | The caption region is out of step with the page | `driver.ps1 hit` at the points; a new clickable element on the bar must be a `button`, `a` or `input` |
| Timings vary widely from run to run | Other programs on the PC compete for memory and disk | Compare timings in one process, alternating before and after |
| `dotnet ef` can't connect | The design-time factory's connection string is hard-coded | Check the password file, or edit `TinapayanRMSDbContextFactory.DevelopmentConnectionString` |
| Settings → Database says backups need `pg_dump` and `pg_restore` | PostgreSQL's command-line tools aren't in `C:\Program Files\PostgreSQL\<version>\bin` or on the `PATH` | Install them, or set `Backups:ToolsFolder` in `appsettings.json` |
| The build can't copy `TinapayanRMS.Maui.exe` or a DLL | The app is running | Close it, or `Get-Process TinapayanRMS.Maui \| Stop-Process` |
| CS0104, an ambiguous reference, in a `.razor` file | Two imported namespaces share a name | Add an alias to `Components/_Imports.razor` |
| An AI assistant's TinapayanRMS tools give figures from before your change | The MCP server is still running the build it started with | Restart it (`/mcp` in Claude Code). It builds again as it starts |
| `--filter` isn't recognised, or zero tests ran | VSTest flags under Microsoft.Testing.Platform | Use `--treenode-filter "/*/*/<class>/<test>"` and `--coverage` |
| A test passes alone but fails now and then in a full run | TUnit runs it beside another test, even one of its own class, that shares something with it | Give them a shared `[NotInParallel]` key |
| The window stops responding | Something blocks the UI thread | `dotnet-stack report -p <pid>`. Find the PID with `dotnet-trace ps`. |
| A crash, deadlock or memory growth | | `dotnet-dump collect -p <pid>`, then `dotnet-dump analyze <file>` |
| An async wait that never ends, and `dumpasync` lists nothing | `dotnet-dump`'s `dumpasync` can't read .NET 11's runtime async, which the app's async methods and .NET's own use: in a dump they're continuations named by layout (`Continuation_64_0_7`) | The debugger's Parallel Stacks window, or `dotnet-trace`; `dotnet-dump`'s `dumpheap` and `gcroot` still work |
| Something is slow | | `dotnet-trace collect -p <pid>` |

When you compare the behaviour of two versions of the code, check that the output says "Build succeeded" before each run, and close the app between them: a running copy keeps the old build's files. If it matters, confirm what was built with `ilspycmd -t <Type> <dll>`.

### Checking costs from an AI assistant

A product's cost and selling price aren't stored, so SQL can't show them: they're worked out when they're read (see [Stored and computed costs](#stored-and-computed-costs)). [src/TinapayanRMS.Mcp](../src/TinapayanRMS.Mcp) is a [Model Context Protocol](https://modelcontextprotocol.io) server, built on the official C# SDK (the `ModelContextProtocol` package), that gives an AI coding assistant such as Claude Code these figures. It works them out with the app's own code and reads the dev database `tinapayanrms`. Its tools are in [CostingTools](../src/TinapayanRMS.Mcp/Tools/CostingTools.cs):

| Tool | What it returns | Worked out by |
|---|---|---|
| `list_products` | Every product, or those whose name has some text in it, each with its base cost, suggested and rounded selling price, current price and markup | `SavedProductReader.ReadAllAsync`, as Reports prices them |
| `get_product_costing` | One product as the Products screen shows it once opened: each section's batch, lines, add-ins and cost per piece, a composite's products, its packaging, and the Costing tab down to the selling price | `SavedProductReader.ReadAsync` |
| `simulate_price_changes` | What-if: new prices or percentage changes for purchased materials, the made and linked ingredients each reaches, and every product they reach with its cost, suggested price and markup now and with the changes | The Price Simulator's code: `TriedPriceChange`, `PriceChangeSimulation`, and `PriceSimulatorProductRow` |

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
| [Tech Stack](../PLAN.md#tech-stack) | You touch a package, or the test runner |
| [Solution Structure](../PLAN.md#solution-structure) | You add a project or a folder |
| [Domain Model](../PLAN.md#domain-model) | You change an entity or a rule. Each entity's bullets give its rules and the reasons for them. |
| [Syncfusion UI Framework](../PLAN.md#syncfusion-ui-framework) | You use a Syncfusion component, or change the theme |
| [The app](plan/maui-app.md) | You change the window, the title bar, how a screen is built, printing, or the view state; or want to know where the app came from |
| [Reports](plan/reports-screen.md) | You change the Reports screen |
| [Application Architecture](../PLAN.md#application-architecture) | You change a screen state, a repository pattern or a migration |
| [Products](plan/products-screen.md), [Raw Materials](plan/raw-materials-screen.md), [Sub-Assemblies](plan/sub-assemblies-screen.md) | You change one of the three main screens: each one's design notes |
| [Reactive Costing Cascade](../PLAN.md#reactive-costing-cascade) | You change any cost |
| [Development Environment](../PLAN.md#development-environment) | You set up a machine, or change the tests' infrastructure |
| [Build plan](plan/build-plan.md) | You want the history: every phase and dated change, and the open items |
| [Conventions](../PLAN.md#conventions) | Always |

### Library documentation

- System.Reactive (Rx.NET): <https://github.com/dotnet/reactive>
- DynamicData: <https://github.com/reactivemarbles/DynamicData>
- EF Core: <https://learn.microsoft.com/ef/core/>
- Npgsql and its EF Core provider: <https://www.npgsql.org/efcore/>
- PostgreSQL: <https://www.postgresql.org/docs/18/> (`pg_dump` and `pg_restore` under "Client Applications")
- Dapper: <https://github.com/DapperLib/Dapper>
- FluentValidation: <https://docs.fluentvalidation.net/>
- Syncfusion Blazor: <https://blazor.syncfusion.com/documentation/introduction>
- .NET MAUI Blazor Hybrid: <https://learn.microsoft.com/aspnet/core/blazor/hybrid/>
- bUnit: <https://bunit.dev/>
- Playwright (`connectOverCDP`): <https://playwright.dev/docs/api/class-browsertype#browser-type-connect-over-cdp>
- TUnit: <https://tunit.dev/>
- Respawn: <https://github.com/jbogard/Respawn>
- Serilog: <https://serilog.net/>
- Catppuccin palette: <https://catppuccin.com/palette>
