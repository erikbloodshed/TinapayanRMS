# UI/UX refactor — warm amber accent, card layout, Products sub-tabs

**Status:** proposed, not yet implemented (2026-09-12). This is a working plan for one refactor. PLAN.md remains the project guide and gets the lasting conventions from step 6 once the work lands.

## Context

The `ui-ux-pro-max` design skill was tested against this app and used to plan a refactor of its look. It was run read-only (`python -B`, no `--persist`).

**What the skill test showed.** The useful outputs were the style direction (*Minimalism & Swiss*: grid-based, high contrast, for dashboards and professional tools), a semantic palette with card, muted, border and destructive tokens, a guideline that empty states need a message rather than blank space, WPF guidance to use shared styles instead of duplicated setters, and chart guidance: comparisons use same-hue grouped bars with direct value labels, never colour alone. It also confirmed one existing pattern (confirm before delete). It was wrong about one thing: it matched a marketing **landing-page** pattern and recommended Google-hosted Fira fonts, GSAP scroll animations and mobile breakpoints. None of that applies to this app, and the override note in the skill already covers it.

**Problems in the current UI**, taken from the code and from screenshots of the running app:
- **The main lists hide the costs.** Ingredients, Packaging and Sub-Assemblies each give three fixed side columns (~890px) to editors. The list gets ~440px, so Current Cost and Cost/Gram sit behind a horizontal scrollbar.
- **Products sprawls sideways.** Six fixed-width panels add up to ~2000px, so the Selling Price summary is off-screen.
- **Visual hierarchy is flat.** Panels are bare StackPanels on white, and section headers are just 14pt regular text.
- **Values are unformatted.** Money shows raw decimals with no ₱.
- **There are no empty states.** Detail columns go blank when nothing is selected.
- **Styling is duplicated and hardcoded.** Six copies of the status block use a hardcoded `#C00000`, the linked-tab notices use `#B36B00`, and the dividers use `#CCCCCC`.
- **Framework controls aren't themed.** `SfSkinManager.ApplyThemeAsDefaultStyle` is never set, so the native `CheckBox` and `ProgressBar` don't pick up the theme.
- **The header repeats the title bar**, as a heavy 18pt all-caps line.

**Decisions made:** warm amber accent `#B45309` (5.0:1 contrast with white, which passes WCAG AA); Products becomes sub-tabs with a pinned price summary; the lookup panels go into collapsible expanders.

## Constraints
- **ViewModels, bindings and commands stay untouched.** All changes are XAML and resources, plus the theme setup in `App.xaml.cs`, one converter and one small UserControl. No view code-behind depends on element names (verified), so layouts can be restructured freely.
- **No `Style` is ever set on a Syncfusion control.** That keeps every theme template intact. Syncfusion controls change colour only through the theme's own `ThemeSettings`.
- **App styles are keyed and target native elements only** (`Border`, `TextBlock`). There is no implicit `TextBlock` style, because one would leak into every Syncfusion template.
- **The native `Expander` is acceptable.** The trimmed theme has no Syncfusion expander (`SfAccordion` is excluded), and the theme's `MSControl/Expander.xaml` styles the native one once `ApplyThemeAsDefaultStyle` is on. This fits PLAN.md's rule for falling back to a native control.

## Implementation

### 1. Theme foundation — `TinapayanRMS.UI/App.xaml.cs`
In `OnStartup`, before `GetRequiredService<MainWindow>()` (the window's constructor builds all six views):
- `SfSkinManager.ApplyThemeAsDefaultStyle = true;`
- `SfSkinManager.RegisterThemeSettings("Windows11Light", new Windows11LightThemeSettings { PrimaryBackground = #B45309 brush, PrimaryForeground = White })`. The class lives in `TinapayanRMS.Theme/Windows11LightSkinHelper.cs:1779` (namespace `Syncfusion.Themes.Windows11Light.WPF`). Its `PrimaryBackground` setter already derives the Dark1–3, Light1–3 and Opacity shades, so one property re-colours buttons, grid selection, the tab and drawer indicators, and focus rings.
- Font family and sizes stay at the theme defaults.

### 2. Design tokens and styles — `TinapayanRMS.UI/App.xaml`
- **Brushes** (the app's own surfaces, not Syncfusion's): `PageBackgroundBrush` #F5F5F4, `CardBackgroundBrush` #FFFFFF, `CardBorderBrush` #E7E5E4, `HeadingForegroundBrush` #1C1917, `MutedForegroundBrush` #57534E, `AccentBrush` #B45309, `ErrorBackgroundBrush` #FEF2F2 / `ErrorForegroundBrush` #B91C1C, `WarningBackgroundBrush` #FFFBEB / `WarningForegroundBrush` #92400E, and chart brushes `ChartBaseCostBrush` #D97706 (3.2:1 on white, enough for non-text graphics) and `ChartSellingPriceBrush` #92400E.
- **Keyed styles:**
  - `CardStyle` (Border): card background and border, thickness 1, CornerRadius 8, Padding 16.
  - `SectionHeaderStyle` (TextBlock): 16pt SemiBold, heading colour, bottom margin 12.
  - `FieldLabelStyle` (TextBlock): muted colour, bottom margin 4.
  - `CaptionStyle` (TextBlock): muted, wrapping.
  - `EmptyStateStyle` (TextBlock): muted, centred, wrapping.
  - `ErrorBannerStyle` and `WarningBannerStyle` (Border): tinted background, CornerRadius 6, Padding 10,8.
  - `AppTitleStyle` (TextBlock): 13pt SemiBold, CharacterSpacing 60.
- **Converter:** register `EmptyStateVisibilityConverter`.

### 3. Shared pieces (new)
- **`TinapayanRMS.UI/EmptyStateVisibilityConverter.cs`:** returns Visible when the value is `null` or `false`, and Collapsed otherwise. It covers both `SelectedX == null` and `ShowProductDetail == false`.
- **`TinapayanRMS.UI/StatusBanner.xaml`** (UserControl, inherits the DataContext): an error banner bound to `ErrorMessage`/`HasError` (both from `ViewModelBase`), plus the existing 4px indeterminate `ProgressBar` bound to `IsBusy`. It replaces the six copy-pasted status blocks. ReportsView has no status block today, so it doesn't get one.

### 4. Shell — `TinapayanRMS.UI/MainWindow.xaml`
- The header becomes a slim bar: a card-coloured Border with a bottom border and padding 16,10, holding the existing `Heading` binding in `AppTitleStyle`. `MainWindowViewModel` is untouched.
- The page background goes behind the `TabControlExt`. Drawer wiring is unchanged.

### 5. Screen layouts
**Shared pattern** for Ingredients, Packaging Materials and Sub-Assemblies. The root Grid gets the page background and Margin 16, with `StatusBanner` in row 0. Row 1 has:
- **Left column (`*`):** a list card with a section header and the main `SfDataGrid` (`ColumnSizer="AutoWithLastColumnFill"`).
- **Right column (360, vertical `ScrollViewer`)**, stacked in this order:
  1. A **Details** card holding the existing form and the New/Save/Delete buttons.
  2. The existing detail card (Price History, Bill of Materials, or Lines) shown under its existing `Show*` flag. When no item is selected, an empty-state hint shows in its place.
  3. A collapsed `Expander` ("Manage categories" or "Manage roles") holding the existing lookup grid, field and buttons, with identical bindings.

This gives the main list roughly 900px, so every cost column is visible without horizontal scrolling. Labels use `FieldLabelStyle` and panel titles use `SectionHeaderStyle`. Control margins stay inline.

**Per-screen specifics:**

| Screen | Detail card and empty-state binding | Expander |
|---|---|---|
| `IngredientsView.xaml` | Price History (`ShowPriceHistory`) / BOM (`ShowBomEditor`); hint on `SelectedIngredient` | Manage categories |
| `PackagingMaterialsView.xaml` | Price History (`ShowPriceHistory`); hint on the same flag | Manage categories |
| `SubAssembliesView.xaml` | Lines (`ShowLinesEditor`); hint on the same flag | Manage roles |
| `UnitsOfMeasureView.xaml` | List `*` with a single Details card (360); no expander | — |

**`ProductsView.xaml`** replaces the horizontal ScrollViewer with three columns:
- **Left (300):** a Products list card (the grid fills the height), then a Product card with Name and New/Save/Delete.
- **Centre (`*`):** a `TabControlExt` with `CloseButtonType="Hide"` and both `ShowTabItemContextMenu` and `ShowTabListContextMenu` set to `False`. It holds four `TabItemExt`s with `CanClose="False"`: **Usages**, **Packaging**, **Costing**, **Links & Clone**. Each carries the existing panel's content verbatim. It's visible when `ShowProductDetail`; otherwise an empty-state hint shows.
- **Right (260):** a pinned **Price Summary** card with the existing summary bindings. Selling Price is large and accent-coloured.
- The linked-tab notices move into `WarningBannerStyle`, and the `#CCCCCC` dividers are removed.

**`ReportsView.xaml`:**
- Two cards in a `*`/`*` Grid, replacing the fixed 620px widths inside a horizontal ScrollViewer. The filters sit inside the Price Trend card.
- Charts use `Palette="Custom"` with `ChartColorModel`/`CustomBrushes` on the chart brushes. The margin `ColumnSeries` get `ChartAdornmentInfo ShowLabel="True"`, so the comparison doesn't rely on colour alone. The LineSeries uses the accent.
- "(as of today — not historized)" becomes a `CaptionStyle` subtitle.
- Sorting products descending would need a ViewModel change, so it's out of scope.

**Grid value formatting (all screens):**
- Money totals and prices (CurrentCost, TotalCost, Price) use `GridCurrencyColumn`, `CurrencySymbol="₱"`, 2 decimals.
- Per-unit costs (CostPerGram, CostPerPiece) use 4 decimals.
- Quantities use `GridNumericColumn` with 2 decimals; QtyPerPiece and PiecesPerPackagingUnit use 4, because they're stored as 4-decimal reciprocals.
- The small 2-column secondary grids use `ColumnSizer="Star"`.
- Confirm the exact `GridCurrencyColumn` property names against `.claude/skills/syncfusion-wpf-datagrid` before writing them.

### 6. Documentation — `PLAN.md`
Record the following:
- Accent via `RegisterThemeSettings`, and why `ApplyThemeAsDefaultStyle` is on.
- The "no Style on Syncfusion controls" rule.
- The token and style set in App.xaml.
- `StatusBanner`.
- The card and right-rail screen pattern, which updates the CRUD-screen-pattern note.
- Lookups in expanders, which updates the embedded-panel note.
- Products sub-tabs, which supersedes the "horizontal ScrollViewer of fixed-width StackPanels" note.

## Verification
1. `dotnet build TinapayanRMS.slnx` (0 warnings) and `dotnet test --solution TinapayanRMS.slnx`, all passing. No domain code changes, so this is a regression guard.
2. Launch on the **secondary monitor**, verify each click with `WindowFromPoint`, and screenshot all six screens plus the expanded drawer at 1400×800. Check:
   - Every main-list column, including Current Cost and Cost/Gram, is visible without a horizontal scrollbar, and money shows ₱.
   - The Products price summary is visible without scrolling. The sub-tabs switch, and they can't be closed by button, right-click or the tab-list menu.
   - The empty-state hints show with nothing selected and disappear when an item is selected.
   - The Manage categories/roles expanders open, and their New/Save/Delete still work.
   - The amber accent shows on grid selection, the main tab indicator, the drawer selection bar and ButtonAdv hover/press. Native `CheckBox`, `ProgressBar` and `Expander` are themed.
   - The error banner appears for a forced validation failure (Save with an empty Name).
   - Regression: closing a main tab and reopening it from the drawer still works.
3. If `RegisterThemeSettings` has no visible effect on this custom Theme Studio assembly, keep the layout work and report it, rather than hand-editing theme XAML.
