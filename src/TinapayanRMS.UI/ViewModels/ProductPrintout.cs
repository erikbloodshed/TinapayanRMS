using System.Globalization;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// What Print puts on paper for a product (Printing/ProductPrintLayout): its recipe and its costing as the Products screen
/// shows them, unsaved edits included, taken the moment Print was pressed. Nothing in it is WPF, so its figures can be
/// checked without laying out a page.
/// </summary>
/// <param name="ProductName">The header's title: the product's name, or "New product" for one not named yet.</param>
/// <param name="CategoryName">Its category, or "Uncategorized".</param>
/// <param name="StageTitle">"Production" or "Research and Development" (ProductRules.StageTitle).</param>
/// <param name="CostUnitCaption">What the costing is for: "Per piece", or "Per unit of 8 pieces".</param>
/// <param name="LastUpdatedText">When it was last saved, "Sep 14, 2026, 6:27 PM", or "—".</param>
/// <param name="SaveNote"><see cref="NotSavedNote"/>, <see cref="UnsavedChangesNote"/>, or null when it's the product as saved.</param>
/// <param name="PrintedAt">When Print was pressed, for the footer.</param>
/// <param name="Sections">The recipe's sub-assemblies, in recipe order; none for a composite.</param>
/// <param name="Packaging">Its packaging, or null when it has none.</param>
/// <param name="Components">A composite's products, in its Products table's order; null for a product made of sub-assemblies.</param>
public sealed record ProductPrintout(
    string ProductName,
    string CategoryName,
    string StageTitle,
    string CostUnitCaption,
    string LastUpdatedText,
    string? SaveNote,
    DateTime PrintedAt,
    IReadOnlyList<PrintedSection> Sections,
    PrintedPackaging? Packaging,
    ProductCostSummary Costs,
    IReadOnlyList<PrintedComponent>? Components = null)
{
    /// <summary>The footer's note for a product that has never been saved.</summary>
    public const string NotSavedNote = "Not saved yet";

    /// <summary>The footer's note for a saved product with changes Save hasn't written yet.</summary>
    public const string UnsavedChangesNote = "Includes unsaved changes";

    /// <summary>The footer's note for a version from the History tab, which isn't the product as it is now.</summary>
    public const string VersionNote = "From the version history, at its costs then";

    /// <summary>A composite: its recipe is the products it holds (<see cref="Components"/>).</summary>
    public bool IsComposite => Components is not null;

    /// <summary>
    /// Which of the materials' costs its figures are at: buffered, unless the Products screen was switched to unbuffered
    /// when Print was pressed, which the page then says (<see cref="UnbufferedNote"/>).
    /// </summary>
    public CostBasis CostBasis { get; init; }

    /// <summary>What the page says beside the cost caption when its figures leave out the cost buffer.</summary>
    public const string UnbufferedNote = "Unbuffered costs";

    /// <summary>
    /// The Price Simulator report's own: what the product costs at today's prices, and the price changes that reach it, its other
    /// figures being at the simulated prices (PriceSimulatorReport, 2026-10-06). Null for every other printout.
    /// </summary>
    public PrintedSimulation? Simulation { get; init; }

    /// <summary>
    /// The Scaling tab, when Print was pressed with it showing: the pages are then the recipe scaled to its number of
    /// sellable units, in place of the recipe and the costing. Null otherwise.
    /// </summary>
    public PrintedScaling? Scaling { get; init; }

    /// <summary>
    /// What a product's figures are for, as its header says it: "Per piece", or "Per unit of 8 pieces"; a composite's are
    /// "Per unit", the one sellable unit it always is.
    /// </summary>
    public static string CostUnitCaptionFor(int quantityRequired, bool isComposite = false) =>
        isComposite ? "Per unit" : quantityRequired <= 1 ? "Per piece" : $"Per unit of {quantityRequired} pieces";

    /// <summary>When a product was last saved, as its header says it: "Sep 14, 2026, 6:27 PM", or "—" for never since it was added.</summary>
    public static string LastUpdatedTextFor(DateTime? updatedAt) =>
        updatedAt?.ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture) ?? "—";
}

/// <summary>
/// What Print gets from the Products screen (ProductsViewModel.PreparePrintout): the pages of the product it has open, or
/// why the sub-assembly form left open there can't give them (<see cref="PrintProblem"/>). Empty (<c>default</c>) while
/// there's nothing to print yet: the details are still arriving, or the History tab has no version picked.
/// </summary>
public union PreparedPrintout(ProductPrintout, PrintProblem);

/// <summary>A rule refused the sub-assembly form left open, so Print keeps the pages it had: the rule's message.</summary>
public sealed record PrintProblem(string Message);

/// <summary>
/// What one Print puts on paper: one product, as the Products screen shows it, or a set of products as they're saved,
/// under a title ("All Products", "Breads · Production"), which gives the document a contents page and starts each
/// product on a page of its own.
/// </summary>
/// <param name="Title">The set's title; null for a single product, printed as it always was.</param>
/// <param name="PrintedAt">When the pages were prepared: every product's, and the contents page's.</param>
public sealed record ProductPrintSet(string? Title, IReadOnlyList<ProductPrintout> Products, DateTime PrintedAt)
{
    /// <summary>One product on its own: no contents page.</summary>
    public static ProductPrintSet Of(ProductPrintout printout) => new(null, [printout], printout.PrintedAt);

    /// <summary>
    /// The Price Simulator report's price changes, every one tried, which its first page lists over its contents; null for any
    /// other set. Its products' figures are at those prices (<see cref="ProductPrintout.Simulation"/>).
    /// </summary>
    public IReadOnlyList<PrintedPriceChange>? PriceChanges { get; init; }

    /// <summary>The Products screen's Scaling tab: one product's recipe scaled, which a set never is.</summary>
    public bool IsScaling => Products.Any(p => p.Scaling is not null);
}

/// <summary>
/// One of the sets a print dialog offers in its Print dropdown: the Price Simulator report's "Selected products (3)" or "All
/// products shown (24)".
/// </summary>
public sealed record PrintChoice(string Label, ProductPrintSet Set)
{
    /// <summary>Its label, which the dropdown shows and a screen reader reads.</summary>
    public override string ToString() => Label;
}

/// <summary>
/// The Scaling tab on paper:the recipe scaled to a number of sellable units, as the tab's tree shows it, each section in
/// whole batches and each made ingredient opened into what it's made from.
/// </summary>
/// <param name="Units">The sellable units it's scaled to: the tab's Sellable Units.</param>
/// <param name="Pieces">What they come to in pieces, when a unit holds more than one; null for a product sold by the piece.</param>
/// <param name="Rows">The tab's tree (RecipeScaler), as it shows.</param>
/// <param name="TotalCost">What everything in it costs, the extra pieces whole batches make included.</param>
public sealed record PrintedScaling(int Units, decimal? Pieces, IReadOnlyList<ScalingRow> Rows, decimal TotalCost);

/// <summary>
/// One sub-assembly of the recipe on paper: its lines and add-ins, each with its share of the formula, the batch they make
/// and the piece cut from it. The figures are the recipe tab's.
/// </summary>
/// <param name="Name">What the recipe calls it, its tab's header: "Dough", "Filling: Ube".</param>
/// <param name="Source">Where a linked one comes from, "Cream Loaf Dough, from Sub-Assemblies"; null for the product's own or a new one.</param>
/// <param name="PercentHeader">"Baker's %" while a line is ticked as the formula's base, "Absolute %" otherwise.</param>
/// <param name="TotalPercent">The whole formula, as a share of the base (167.7), or 100 with no base; null for an empty batch.</param>
/// <param name="HasPieceSize">A weight per piece or a yield was typed; until one is, the section costs nothing per piece.</param>
public sealed record PrintedSection(
    string Name,
    string? Source,
    string PercentHeader,
    IReadOnlyList<PrintedLine> Lines,
    IReadOnlyList<PrintedLine> AddIns,
    decimal TotalWeight,
    decimal? TotalPercent,
    decimal BatchCost,
    decimal WeightPerPiece,
    decimal Yield,
    decimal CostPerPiece,
    bool HasPieceSize)
{
    /// <summary>On the Price Simulator report, the batch's cost at today's prices, when a price change moves it; null otherwise.</summary>
    public decimal? NowBatchCost { get; init; }

    /// <summary>The section as its recipe tab shows it, at <paramref name="basis"/>.</summary>
    public static PrintedSection From(ProductUsageEdit usage, CostBasis basis)
    {
        // Read as the recipe form reads them (ProductsViewModel.RecomputeUsageForm): against the formula's base while a
        // line is ticked, add-ins included, since an add-in is read against the recipe's base; with nothing ticked, every
        // line of both tables against the whole batch, add-ins included, so they come to 100 between them.
        var formulaBase = SubAssemblyRules.FormulaBaseWeight([.. usage.Lines.Select(l => (l.Grams, l.IsFormulaBase))]);
        decimal? PercentOf(decimal grams) => formulaBase > 0
            ? SubAssemblyRules.CalculateBakersPercent(grams, formulaBase)
            : SubAssemblyRules.CalculateAbsolutePercent(grams, usage.TotalWeight);
        PrintedLine Line(SubAssemblyLineEdit line) =>
            new(line.Ingredient.Name, line.Grams, PercentOf(line.Grams), line.CostPerGramAt(basis), line.CostAt(basis), line.IsFormulaBase);

        return new(
            usage.SectionName,
            usage.IsLinked ? usage.LinkedFromText : null,
            IngredientLineRow.PercentHeader(formulaBase),
            [.. usage.Lines.Select(Line)],
            [.. usage.AddIns.Select(Line)],
            usage.TotalWeight,
            PercentOf(usage.TotalWeight),
            usage.TotalCostAt(basis),
            usage.WeightPerPiece,
            usage.Yield,
            usage.CostPerPieceAt(basis),
            usage.AuthoredValue > 0);
    }
}

/// <summary>One ingredient of a section, or one of the product's add-ins on a linked one.</summary>
/// <param name="Percent">Its share of the formula, in the section's <see cref="PrintedSection.PercentHeader"/>; null for an empty batch.</param>
/// <param name="CostPerGram">The ingredient's cost per gram (buffered, unless the printout isn't), as the recipe form's Cost/g column shows it.</param>
/// <param name="Cost">What it adds to the batch's cost, at <paramref name="CostPerGram"/>.</param>
/// <param name="IsFormulaBase">Part of the formula's 100% base, usually the flour.</param>
public sealed record PrintedLine(string Ingredient, decimal Grams, decimal? Percent, decimal CostPerGram, decimal Cost, bool IsFormulaBase)
{
    /// <summary>On the Price Simulator report, what the line costs at today's prices, when a price change moves it; null otherwise.</summary>
    public decimal? NowCost { get; init; }
}

/// <summary>The product's packaging on paper: what one sellable unit takes of each material, as the Packaging tab shows it.</summary>
/// <param name="LinkedTo">"Linked to Gift Box Set (Sub-Assemblies)" for linked packaging; null for the product's own lines.</param>
/// <param name="Total">What the lines cost together, for one sellable unit.</param>
public sealed record PrintedPackaging(string? LinkedTo, IReadOnlyList<PrintedPackagingLine> Lines, decimal Total)
{
    /// <summary>The Packaging tab's rows, leaving out any without a material, which isn't a line yet.</summary>
    public static PrintedPackaging From(IEnumerable<PackagingLineRow> rows, string? linkedTo)
    {
        IReadOnlyList<PrintedPackagingLine> lines =
        [
            .. rows
                .Where(r => r.PackagingMaterial is not null)
                .Select(r => new PrintedPackagingLine(
                    r.PackagingMaterial!.Name,
                    r.QtyPerUnit,
                    r.UnitOptions.FirstOrDefault(o => o.Key == r.UnitKey)?.Abbreviation ?? r.BaseUnitAbbreviation,
                    r.UnitCost,
                    r.CostPerUnit)),
        ];

        return new(linkedTo, lines, lines.Sum(l => l.Cost));
    }
}

/// <summary>One product in a composite, as its Products table shows it.</summary>
/// <param name="Quantity">How many of the product's sellable units go in; null while nothing is typed for it.</param>
/// <param name="RecipeCost">What one of it brings: its recipe (ingredients) cost for one sellable unit, its packaging left out.</param>
/// <param name="Cost">
/// What the line brings into the composite's cost: <paramref name="Quantity"/> at <paramref name="RecipeCost"/>, and at
/// <paramref name="PackagingCost"/> too when there's one.
/// </param>
/// <param name="PackagingCost">
/// The product's own packaging for one sellable unit, when the composite includes its products' packaging; null when it
/// doesn't, and the table then has no Packaging Cost column.
/// </param>
public sealed record PrintedComponent(string Product, int? Quantity, decimal RecipeCost, decimal Cost, decimal? PackagingCost = null)
{
    /// <summary>On the Price Simulator report, what the line costs at today's prices, when a price change moves it; null otherwise.</summary>
    public decimal? NowCost { get; init; }

    /// <summary>The Products table's rows, leaving out any without a product, which isn't a line yet.</summary>
    public static IReadOnlyList<PrintedComponent> From(IEnumerable<ProductComponentRow> rows) =>
    [
        .. rows
            .Where(r => r.Product is not null)
            .Select(r => new PrintedComponent(
                r.Product!.Name, r.Quantity, r.RecipeCost ?? 0m, r.Cost, r.IncludesPackaging ? r.PackagingCost ?? 0m : null)),
    ];
}

/// <summary>One packaging line: how much of a material one sellable unit takes, in the unit it's typed in.</summary>
/// <param name="Qty">Null while nothing is typed for it.</param>
/// <param name="UnitCost">What one of <paramref name="Unit"/> costs, buffered unless the printout isn't.</param>
/// <param name="Cost">What the line costs for one sellable unit.</param>
public sealed record PrintedPackagingLine(string Material, decimal? Qty, string Unit, decimal UnitCost, decimal Cost)
{
    /// <summary>On the Price Simulator report, what the line costs at today's prices, when a price change moves it; null otherwise.</summary>
    public decimal? NowCost { get; init; }
}

/// <summary>
/// One price change of the Price Simulator screen on paper: a purchased material and the price it's tried at, per the unit it's
/// bought in, as the Price Changes table shows it.
/// </summary>
/// <param name="Per">The unit the prices are per: "sack".</param>
/// <param name="CurrentPrice">What one costs now; null for a material with no price yet.</param>
/// <param name="ChangePercent">How far the price moves; null when there's no current price to move from.</param>
public sealed record PrintedPriceChange(string Material, string? Per, decimal? CurrentPrice, decimal? ChangePercent, decimal NewPrice);

/// <summary>
/// What the Price Simulator report adds to a product's printout, whose other figures are at the simulated prices: its costs at
/// today's prices, for the costing's Now column, and the price changes that reach it.
/// </summary>
/// <param name="Changes">The changes reaching the product, through its own ingredients and packaging or the products it holds; none when no change does.</param>
public sealed record PrintedSimulation(ProductCostSummary Now, IReadOnlyList<PrintedPriceChange> Changes);

/// <summary>What Print puts on paper for a saved product.</summary>
public static class SavedProductPrintout
{
    /// <summary>The product as saved, so with no save note.</summary>
    public static ProductPrintout ToPrintout(this SavedProduct saved, DateTime printedAt) => new(
        saved.Product.Name,
        saved.Product.Category?.Name ?? ProductRules.UncategorizedTitle,
        ProductRules.StageTitle(saved.Product.Stage),
        ProductPrintout.CostUnitCaptionFor(saved.Product.QuantityRequired, saved.Product.IsComposite),
        ProductPrintout.LastUpdatedTextFor(saved.Product.UpdatedAt),
        null,
        printedAt,
        [.. saved.Sections.Select(s => PrintedSection.From(s, saved.CostBasis))],
        saved.HasPackaging ? PrintedPackaging.From(saved.PackagingLines, saved.PackagingLinkedTo) : null,
        saved.Costs,
        saved.Product.IsComposite
            ? [.. saved.Components.Select(c => saved.Product.IncludesComponentPackaging
                ? new PrintedComponent(
                    c.Component.ComponentProduct!.Name,
                    c.Component.Quantity,
                    c.RecipeCostAt(saved.CostBasis),
                    c.CostAt(saved.CostBasis) + c.PackagingCostAt(saved.CostBasis),
                    c.Costs.PackagingAt(saved.CostBasis))
                : new PrintedComponent(c.Component.ComponentProduct!.Name, c.Component.Quantity, c.RecipeCostAt(saved.CostBasis), c.CostAt(saved.CostBasis)))]
            : null)
    {
        CostBasis = saved.CostBasis,
    };
}
