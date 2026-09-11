# TinapayanRMS — Project Guide & Build Plan

Product recipe management & costing app for Tinapayan Festival Bakeshoppe. Tracks ingredient expenses, recipe/product margins, and cascades ingredient and packaging price changes up through sub-assemblies and finished products automatically.

## Tech Stack

- **Language / Runtime**: C# 14, .NET 10 (`net10.0`; `net10.0-windows10.0.19041.0` for the UI and Tests projects — ReactiveUI.WPF requires at least that Windows SDK version, and the bare `net10.0-windows` moniker resolves to `windows7.0`, which is too low)
- **UI**: WPF, styled with **Syncfusion Essential Studio for WPF 34.2.2** and a custom Windows11Light theme authored in Syncfusion Theme Studio
- **MVVM**: ReactiveUI (24.x). This version requires explicit builder-pattern initialization before any `ReactiveObject` is touched — `RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().WithWpf().BuildApp()` — done once per process by `TinapayanRMS.UI/ReactiveUIBootstrapper.cs`. It also dropped `System.Reactive` as a dependency, so that package is referenced directly wherever Rx LINQ operators (`.Select`, `.Where`, …) are used on an `IObservable<T>`.
- **Reactive collections**: DynamicData
- **Persistence**: EF Core 10.0.12 → SQL Server (db name `TinapayanRMS`)
- **DI / Hosting**: `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.DependencyInjection`, wired via `HostBuilder` in `TinapayanRMS.UI/CompositionRoot.cs`, called from `App.xaml.cs`
- **Validation**: FluentValidation
- **Logging**: Serilog (rolling file sink)
- **Charts**: Syncfusion SfChart (`Syncfusion.SfChart.WPF`) for margin/price-trend reporting — replaces the originally-planned LiveCharts2
- **Testing**: xUnit v3, AwesomeAssertions, NSubstitute, Respawn. Integration tests run against a locally installed SQL Server instance (such as LocalDB or SQL Server Express), and Respawn resets that database between tests. `AwesomeAssertions` replaces `FluentAssertions`: FluentAssertions 8+ requires a paid commercial license for use in a for-profit product, and AwesomeAssertions is a same-API, MIT-licensed fork of the last free FluentAssertions 7 release, created for exactly this situation.
- **Solution format**: `.slnx`
- **`dotnet test`**: the .NET 10 SDK dropped VSTest support for `dotnet test`; running under the new Microsoft.Testing.Platform mode needs a `test.runner` setting in `global.json` at the repo root (already added). Run tests with `dotnet test --solution TinapayanRMS.slnx`.
- **Syncfusion license key**: read from the `SYNCFUSION_LICENSE_KEY` Windows user environment variable and registered in `App`'s static constructor (`TinapayanRMS.UI/App.xaml.cs`), before anything else in the process touches a Syncfusion type. Running the app or its tests on another machine requires that variable to be set there too.

## Solution Structure

```
TinapayanRMS.slnx
├── TinapayanRMS.Domain           — entities, value objects (no external deps)
├── TinapayanRMS.ApplicationCore  — use cases, interfaces, costing/margin logic, DTOs
├── TinapayanRMS.Infrastructure   — EF Core DbContext, migrations, repositories
├── TinapayanRMS.Theme            — custom Syncfusion Windows11Light theme (Theme Studio output), referenced by UI
├── TinapayanRMS.UI               — WPF + Syncfusion controls + ReactiveUI Views/ViewModels, DynamicData-backed collections
└── TinapayanRMS.Tests            — xUnit v3, AwesomeAssertions, NSubstitute
```

## Domain Model

- **UnitOfMeasure**: a flat lookup table with only `Id`, `Name` and `Abbreviation`. It has no base unit, conversion factor, or self-FK. It holds both standard units and purchase units: mass (Gram), count (Piece), length (Millimeter, Centimeter, Inch, Meter), and purchase units (Tin, Pack, Bag, Case, Box, Roll, etc.). Units are defined globally here, but each conversion ratio belongs to a specific material. A material's **standard unit** is the unit its quantities and costs are tracked in. For ingredients it is always the gram; each packaging material declares its own. The table can stay flat because each material has exactly one standard unit. The only conversion between two units in the table is between length units, and it uses fixed physical constants in code rather than table data.
  - **Length units** (Millimeter, Centimeter, Inch, Meter) are seeded rows with fixed Ids, and they can't be edited or deleted. A static `LengthConversion` helper in `ApplicationCore` maps those Ids to their factors (1 cm = 10 mm, 1 in = 25.4 mm, 1 m = 1000 mm); `UnitOfMeasureRules.IsProtected` in `ApplicationCore` is the guard every future edit/delete path must check.
  - **Gram and Piece are also seeded** (Ids 5 and 6), since they're the default standard units for ingredients and for boxes/wrappers respectively, but neither is protected — they can be renamed or removed like any other row. Purchase units (Tin, Pack, Bag, Case, Box, Roll, …) are **not** seeded; those are business-specific and left for the shop owner to add once a Units of Measure management screen exists.
- Materials are split into two separate tables, **Ingredient** and **PackagingMaterial**, each with its own category and price history tables. Nothing is shared between the two sides, so the schema itself stops a packaging material from being put in a recipe, or an ingredient on a packaging line. Packaging materials are always purchased. Ingredients can be purchased or made in-house.
- **IngredientCategory** (Flour, Dairy, …) / **Ingredient**:
  - **The standard unit for every ingredient is the gram.** All ingredient quantities are recorded in grams: sub-assembly lines, BOM lines, sub-assembly total weights, and weight-per-piece. Because of that, `Ingredient` has no standard-unit column.
  - **`Source`**: `Purchased` or `Made`. It is set when the ingredient is created and can't change afterwards, so an ingredient's cost always comes from exactly one place.
  - **`CostPerGram`** (derived): the value every sub-assembly line and BOM line uses, whichever the source.
  - **Purchased** ingredients:
    - `PurchaseUnitId` (FK to UnitOfMeasure) is the unit it's bought in.
    - `PurchaseUnitQty` is the number of grams in one purchase unit (a 25kg flour sack is `25000`). It is the only place an ingredient's purchase unit is converted to grams.
    - Priced through `IngredientPriceHistory`. `CostPerGram = CurrentCost / PurchaseUnitQty`.
  - **Made** ingredients are produced in-house from a bill of materials (BOM):
    - They have no purchase unit, purchase quantity, or price history.
    - **`WeightLossPercent`**: weight lost while making the ingredient (e.g. water boiled off syrup). At least 0 and less than 100.
    - Output grams = `Σ BOM line grams × (1 − WeightLossPercent / 100)`.
    - `CostPerGram = Σ (BOM line grams × component CostPerGram) / output grams`.
    - Example: simple syrup made from 1000g sugar (₱0.09/g, so ₱90) and 500g water (₱0), with 20% weight loss, gives 1500 × 0.8 = 1200g. It costs ₱90 / 1200 = ₱0.075/g.
- **IngredientBomLine**: one component of a made ingredient. It has `IngredientId` (the made ingredient), `ComponentIngredientId` (FK to `Ingredient`, purchased or made), and `Grams`.
  - BOMs can nest to any depth: caramel can be made from syrup, and syrup from sugar.
  - Cycles are blocked: an ingredient can't appear anywhere in its own BOM tree. A made ingredient needs at least one BOM line.
  - BOM lines use ingredients only, never packaging.
- **PackagingCategory** (Boxes, Wrappers, Ribbons, …) / **PackagingMaterial**:
  - `StandardUnitId` (FK to UnitOfMeasure) is its standard unit, set per material: Piece for boxes and wrappers, a length unit (mm, cm, inch or meter) for ribbon.
  - `PurchaseUnitId` (FK to UnitOfMeasure) is the unit it's bought in.
  - `PurchaseUnitQty` is the number of standard units in one purchase unit (a pack of 100 boxes is `100` pieces, a 1000cm ribbon roll is `1000` centimeters).
  - Packaging lines use the material's standard unit, with one exception: a material whose standard unit is a length unit can be entered in **any** length unit (see PackagingLine).
- **IngredientPriceHistory** / **PackagingPriceHistory**: a price timeline for each purchased material. Made ingredients have none. Prices are entered **per purchase unit** (the price of one sack or one pack), as they appear on the supplier's receipt. `CurrentCost` on the `Ingredient` or `PackagingMaterial` is recalculated from the latest entry and is also per purchase unit. Cost per standard unit is `CurrentCost / PurchaseUnitQty`.
- **SubAssemblyRole**: an editable lookup table (`Id`, `Name`, `SortOrder`), seeded with Dough, Filling, Topping, Inclusion and Finishing. More roles, such as Glaze, can be added later. `SortOrder` sets the order of a product's recipe tabs. A role can't be deleted while any sub-assembly uses it.
- **SubAssembly** + **SubAssemblyLine**: a sub-assembly is an in-house recipe that products use directly, such as a dough, a filling or a topping. Its lines are the ingredients that make it up.
  - `RoleId` (FK to `SubAssemblyRole`, required): each sub-assembly has exactly one role, so custard is always a Filling, and the Filling tab only offers filling sub-assemblies. To use the same recipe in another role, create a separate sub-assembly.
  - A sub-assembly's role can only be changed while no product uses it. Changing it would move the sub-assembly to a different tab in every product that uses it, and break that tab's links.
  - Each line has an FK to `Ingredient` (purchased or made) and a quantity in grams. Line cost = grams × the ingredient's `CostPerGram`.
  - A sub-assembly has a total weight in grams (the sum of its line grams) and a total ingredient cost.
- **Product** + **ProductSubAssemblyUsage**: a finished product is built from distinct sub-assemblies. It can use several sub-assemblies in the same role (e.g. an ube filling and a cheese filling), but each sub-assembly only once.
  - Each usage stores **`WeightPerPiece`** (grams). The UI accepts either weight-per-piece or yield (piece count) as input, but only `WeightPerPiece` is stored.
  - Yield is derived per usage: `Yield = SubAssembly.TotalWeight / WeightPerPiece`.
  - Usage cost per piece = `SubAssembly.TotalCost × WeightPerPiece / SubAssembly.TotalWeight`. For example, 100g/₱24 filling used at 10g/piece gives ₱2.40/piece, yield 10.
  - Rescaling a sub-assembly batch changes the yield, but the grams in each piece stay the same.
  - A product has **no yield of its own**. Each usage row has its own yield, and those will usually differ between a product's sub-assemblies. That's expected, not an error.
  - Product material cost per piece = the sum of all its usages' cost-per-piece, plus its packaging lines' cost-per-piece.
  - Sub-assembly and product weights are raw input weights, and baking/moisture loss is not modeled for them. Made ingredients are the only place weight loss is modeled (`WeightLossPercent`).
- **PackagingLine**: one packaging material used by a product.
  - FK to `PackagingMaterial`, so packaging price changes cascade like ingredient price changes.
  - **`EntryUnitId`**: the unit this line's quantities are entered in. It is the material's standard unit. The one exception is a material with a length standard unit, which can use any length unit (a ribbon with a cm standard unit can be entered in inch, cm or mm).
  - **`QtyPerPiece`**: amount of the packaging material used by one finished piece, in the entry unit (5 for 5 inches of ribbon, `0.1667` for a 6-piece box, 1 for a label).
  - **`PiecesPerPackagingUnit`**: finished pieces served by one entry unit of the packaging material (6 for that box).
  - **`AuthoredField`**: records which of the two the user typed. The other is recalculated as its reciprocal on every edit. Ribbon is naturally entered as `QtyPerPiece`, a box as `PiecesPerPackagingUnit`.
  - Quantities are stored exactly as entered, in the entry unit. Converting them to the standard unit when saving could lose precision, because cm-to-inch doesn't give a terminating decimal. Keeping the entered value means the user always sees back exactly what they typed.
  - Cost per piece = `PackagingMaterial.CurrentCost / PurchaseUnitQty × QtyPerPiece`, with `QtyPerPiece` first converted from the entry unit to the standard unit through `LengthConversion` (no conversion when the two units are the same).
  - Example: a ribbon costs ₱45.00 per roll. Its standard unit is cm and a roll holds 1000 cm, so it costs ₱0.045 per cm. One product uses 5 in per piece (12.7 cm → ₱0.5715). Another uses 80 mm per piece (8 cm → ₱0.36).
  - Storing both directions differs from `ProductSubAssemblyUsage` on purpose. People describe packaging quantities both ways, but they describe recipe weights only one way.
- **CostingParameters**: labor, overhead, margin %, VAT. **Always independent per product**, even for derived/linked products.

### Derived products

Some products derive from others (e.g. Special Ensaymada Small from Special Ensaymada Big, differing only in weight; or products sharing a dough sub-assembly but differing in toppings). Deriving supports:

- **Clone**: a one-time, fully independent copy.
- **Live Template**: linked sub-assemblies, kept in sync with the source product.
- A separate **"same sub-assembly, different weight"** state, in addition to fully independent. The two products share a `SubAssemblyId` and differ only in `WeightPerPiece`.
- A **tab** is one role on one product, identified by (`ProductId`, `SubAssemblyRoleId`). It holds all of that product's sub-assemblies in that role. Each product also has one **Packaging** tab for its packaging lines. Because a sub-assembly's role can't change while products use it, a tab's identity stays the same for the life of the product.
- Linking is chosen **per tab** when the product is derived, not all-or-nothing. Linking a tab links everything in it, so linking the Filling tab links both the ube and the cheese filling. Afterwards, any tab's link can be repointed to a different product or made fully independent at any time, separately for each tab.
- Live templating covers recipe tabs and packaging lines, but **never** CostingParameters (labor/overhead/margin/VAT), which always stay independent per product.
- Chains of derived products (a derived product used as the base for a further derived product) are allowed.

## Syncfusion UI Framework

The app is styled with **Syncfusion Essential Studio for WPF 34.2.2**, licensed via the `SYNCFUSION_LICENSE_KEY` environment variable, and a custom **Windows11Light** theme originally authored in Syncfusion Theme Studio.

- **`TinapayanRMS.Theme`** is that Theme Studio output, moved into the repo and converted from local-installer `<Reference HintPath>` entries to NuGet `PackageReference`s pinned to `34.2.2`, so the solution restores from nuget.org rather than depending on Essential Studio being installed on the build machine. **No project in the solution requires the Essential Studio installer anymore** — NuGet is the only source for every Syncfusion assembly.
- **Core control set**: the app adopts `SfDataGrid` (lists/grids), `Syncfusion.SfInput.WPF`'s edit controls (`ComboBoxAdv`, `AutoComplete`, `MaskedTextBox`, `DateTimeEdit`, `CurrencyTextBox`, and others), `ButtonAdv`, `TabControlExt`, `DockingManager`/`Ribbon` for the app shell, and `SfChart` for reporting. `TinapayanRMS.UI` takes its own `PackageReference`s (pinned to `34.2.2`) for exactly these.
- **`TinapayanRMS.Theme` is trimmed to that same core set**, not the ~50 assemblies Theme Studio generated by default (which covered every control, including PDF/DocIO/XlsIO/Gantt/Spreadsheet document engines and specialty visualization controls this app has no use for). Trimming turned up two real problems worth remembering:
  - Four packages (`Syncfusion.PivotAnalysis.WPF`, `Syncfusion.Pdf.Base`, `Syncfusion.DocIO.Base`, `Syncfusion.XlsIO.Base`) don't exist at `34.2.2` on nuget.org at all — Syncfusion renamed them (`Pdf.Wpf`, `DocIO.Wpf`, `XlsIO.Wpf`, and the Pivot WPF package under a name not yet tracked down). None of that chain is needed here, so they were dropped rather than chased.
  - The "SfTabControl" folder Theme Studio generated is **not** a general tab control — its XAML resolves `Syncfusion.Windows.Controls.SfTabControl` from `Syncfusion.SfChromelessWindow.WPF`, i.e. it's `SfChromelessWindow`'s own tab strip. The general-purpose tab control is `TabControlExt` (`Syncfusion.Tools.WPF`), which is what's actually kept.
  - The folders backing dropped controls were deleted from `TinapayanRMS.Theme`, not just excluded from compilation — see Open Items below before reaching for one of them again.
- **Applying the theme**: `App.xaml.cs`'s static constructor registers the license (`SyncfusionLicenseProvider.RegisterLicense`) before anything else touches a Syncfusion type; `OnStartup` then calls `SfSkinManager.SetTheme(mainWindow, new Theme("Windows11Light"))` before showing the window. Verified by actually launching the built app and screenshotting the themed `MainWindow` (custom title bar chrome, styled `ButtonAdv`) — not just a successful build.

## Reactive Costing Cascade

The core design problem is that a price change on any ingredient or packaging material must ripple up through everything that uses it, automatically. The cascade has two starting points, one per material table, and both end at the product:

```
IngredientPriceHistory entry added
  → purchased Ingredient.CostPerGram recomputed
    → every made Ingredient that uses it recomputed, up every level of the BOM tree
      → every SubAssemblyLine using a changed ingredient recomputed
        → SubAssembly.TotalCost recomputed
          → every ProductSubAssemblyUsage of that sub-assembly recomputed ─┐
                                                                           ├→ Product total cost recomputed
PackagingPriceHistory entry added                                          │    → Product margin / selling price recomputed
  → PackagingMaterial.CurrentCost recomputed                               │
    → every PackagingLine using it recomputed ─────────────────────────────┘
```

Model this with DynamicData `SourceCache<T>`s joined via `.Connect()` / `.InnerJoin()` / `.TransformMany()`, so the derived observable collections update themselves instead of the code walking the dependency graph by hand on every edit. EF Core remains the source of truth on disk: the caches load from it at startup, and writes go back through EF Core.

The one exception is made ingredients. Their BOMs nest to any depth, so that part of the cascade can't be a fixed chain of joins. An ingredient's cost can change from a new price entry, a BOM line edit, or a `WeightLossPercent` edit. When it does, recompute the made ingredients that depend on it in dependency order, components before the ingredients built from them. From the sub-assembly lines onward, the joins take over as normal.

## Development Environment

- Development happens directly on a local Windows machine that has the .NET 10 SDK, SQL Server Express, and SQL Server LocalDB installed.
- Running the app (not just building it) requires the `SYNCFUSION_LICENSE_KEY` user environment variable to be set (see Syncfusion UI Framework below). The solution builds fine without it, but `App`'s static constructor throws on startup if it's missing.
- **Docker is not used anywhere in the project**: not for development, testing, or deployment.
- EF Core migrations are generated with `dotnet ef migrations add`, not written by hand, and applied to the local database with `dotnet ef database update`.
- **A phase is done** when the solution builds without errors, all tests pass, and any new migration applies cleanly to a local database. Mark it Done in the Build Plan table; there are no separate per-phase documents.
- **Integration test database**: tests never touch the developer's own `TinapayanRMS` database. `TinapayanRMS.Tests/Infrastructure/DatabaseFixture.cs` runs against a separate `TinapayanRMS_Test` database (same server, connection string's `Initial Catalog` overridden), migrated once per test run and reset with Respawn before every test via `[Collection(DatabaseCollection.Name)]` + `IAsyncLifetime`. Seed/reference data (currently just `UnitsOfMeasure`) is excluded from the Respawn reset, since it's owned by migrations, not by tests — add a table to `DatabaseFixture.TablesExcludedFromReset` whenever a future phase adds more seeded lookup data.

## Build Plan

Build phase by phase. Do not jump ahead to a later phase's concerns while working on an earlier one; add them to Open Items below instead.

| Phase | Scope | Status |
|---|---|---|
| 1 | Solution scaffold, DI host builder, ReactiveUI bootstrap, EF Core DbContext | Done |
| 2 | Units of Measure (incl. seeded, protected length units) + Ingredient & Packaging Categories | Done |
| 3 | Syncfusion UI shell — license bootstrap, custom Windows11Light theme applied, core control set wired into `TinapayanRMS.UI` | Done |
| 4 | Ingredients (Purchased/Made source) + Packaging Materials (CRUD, purchase-unit → standard-unit conversion) | Not started |
| 5 | Ingredient & Packaging Price History + `CurrentCost` rollup | Not started |
| 6 | Made Ingredients — BOM lines, `WeightLossPercent`, nested cost rollup with cycle check | Not started |
| 7 | Sub-Assembly Roles + Sub-Assemblies + Sub-Assembly Lines (composition, cost rollup) | Not started |
| 8 | Products — sub-assembly usages, packaging lines | Not started |
| 9 | Costing Parameters — labor/overhead/margin/VAT → selling price | Not started |
| 10 | Derived Products — Clone vs Live Template, per-tab linking | Not started |
| 11 | End-to-end reactive price cascade, made-ingredient, sub-assembly and packaging branches (wiring Phases 5–10 together) | Not started |
| 12 | Margin/price-trend reporting (SfChart) | Not started |
| 13 | Validation (FluentValidation), logging (Serilog), UX polish | Not started |
| 14 | Full test coverage consolidation | Not started |

### Open items (for later phases)

- **Adding back a dropped Syncfusion control.** `TinapayanRMS.Theme` was trimmed from Theme Studio's default ~50-assembly output to the core set (see Syncfusion UI Framework above), and the XAML folders for dropped controls were **deleted from disk**, not just excluded from compilation — the original untrimmed export no longer exists anywhere (it was moved into the repo, then trimmed, rather than copied). If a later phase genuinely needs one of the dropped controls (e.g. `SfScheduler` for some future calendar view), regenerate that theme's XAML from Syncfusion Theme Studio rather than trying to reconstruct it by hand, add the matching `PackageReference`(s) to `TinapayanRMS.Theme.csproj` (check the package's current dependency list on nuget.org first — see the Pdf.Base/DocIO.Base/XlsIO.Base/PivotAnalysis.WPF rename lesson above — some names have changed since Theme Studio was last run), and add matching `PackageReference`(s) to `TinapayanRMS.UI.csproj` for whatever the control itself lives in.

## Conventions

- The layered architecture is fixed: `Domain` has no external dependencies, business and costing rules live in `ApplicationCore`, and EF Core and other I/O stay in `Infrastructure`.
- The MVVM toolkit is **ReactiveUI**, not CommunityToolkit.Mvvm. Use `ReactiveObject`, `ReactiveCommand`, `WhenAnyValue`, etc.
- **Prefer Syncfusion controls over native WPF controls whenever a Syncfusion equivalent is available** (e.g. `ButtonAdv` over `Button`, `TabControlExt` over `TabControl`, `ComboBoxAdv`/`SfDataGrid`/the `SfInput.WPF` edit controls over their native counterparts), so the whole UI picks up the Windows11Light theme consistently. Fall back to a native control only when no Syncfusion equivalent exists, and add the corresponding package to `TinapayanRMS.UI` and (if needed) `TinapayanRMS.Theme` per the Syncfusion UI Framework section above.
- **Decimal precision**: keep `decimal` values at full precision through the whole cascade, and round only for display and for the final selling price. Rounding each per-piece cost before summing adds up to drift. Phase 1 sets the SQL Server column precision when it defines the `DbContext`.
- **Use modern C# 14 / .NET 10 idioms** rather than older patterns still valid in the language: primary constructors, collection expressions (`[..]`), required members, file-scoped namespaces, pattern matching, and `record`/`record struct` for immutable data shapes, in line with what's already in the codebase (e.g. `CategoryPersistenceTests`'s primary constructor, `WellKnownUnitOfMeasureIds`'s collection expression). Don't reach for an older, more verbose equivalent just because it's more familiar.
- **Correctness and performance take priority over brevity or convenience.** Favor the approach that is provably correct and doesn't waste allocations, database round-trips, or UI-thread work, even if a shorter or simpler alternative exists — e.g. querying only the columns/rows actually needed rather than materializing more and filtering in memory, and avoiding unnecessary intermediate collections in hot paths like the reactive costing cascade.
- This document is a living plan. Update it as phases complete or decisions change, so it stays the single source of truth for the project.
