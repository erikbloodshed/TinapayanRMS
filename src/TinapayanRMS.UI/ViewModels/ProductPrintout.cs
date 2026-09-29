using System.Globalization;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// What Print puts on paper for a product (Printing/ProductPrintLayout): its recipe and its costing as the Products screen
/// shows them, unsaved edits included, taken the moment Print was pressed. Nothing in it is WPF, so its figures can be
/// checked without laying out a page.
/// </summary>
/// <param name="ProductName">The header's title: the product's name, or "New product" for one not named yet.</param>
/// <param name="CategoryName">Its category, or "Uncategorized".</param>
/// <param name="StageTitle">"Production" or "Research and Development" (ProductFolderNode.StageTitle).</param>
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

    /// <summary>A composite: its recipe is the products it holds (<see cref="Components"/>).</summary>
    public bool IsComposite => Components is not null;

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
}

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
    decimal ShrinkagePercent,
    decimal FinalWeight,
    decimal WeightPerPiece,
    decimal Yield,
    decimal CostPerPiece,
    bool HasPieceSize)
{
    /// <summary>The section as its recipe tab shows it.</summary>
    public static PrintedSection From(ProductUsageEdit usage)
    {
        // Read as the recipe form reads them (ProductsViewModel.RecomputeUsageForm): against the formula's base while a
        // line is ticked, add-ins included, since an add-in is read against the recipe's base; with nothing ticked, every
        // line of both tables against the whole batch, add-ins included, so they come to 100 between them.
        var formulaBase = SubAssemblyRules.FormulaBaseWeight([.. usage.Lines.Select(l => (l.Grams, l.IsFormulaBase))]);
        decimal? PercentOf(decimal grams) => formulaBase > 0
            ? SubAssemblyRules.CalculateBakersPercent(grams, formulaBase)
            : SubAssemblyRules.CalculateAbsolutePercent(grams, usage.TotalWeight);
        PrintedLine Line(SubAssemblyLineEdit line) =>
            new(line.Ingredient.Name, line.Grams, PercentOf(line.Grams), line.Ingredient.BufferedCostPerBaseUnit, line.Cost, line.IsFormulaBase);

        return new(
            usage.SectionName,
            usage.IsLinked ? usage.LinkedFromText : null,
            IngredientLineRow.PercentHeader(formulaBase),
            [.. usage.Lines.Select(Line)],
            [.. usage.AddIns.Select(Line)],
            usage.TotalWeight,
            PercentOf(usage.TotalWeight),
            usage.TotalCost,
            usage.ShrinkagePercent,
            usage.FinalWeight,
            usage.WeightPerPiece,
            usage.Yield,
            usage.CostPerPiece,
            usage.AuthoredValue > 0);
    }
}

/// <summary>One ingredient of a section, or one of the product's add-ins on a linked one.</summary>
/// <param name="Percent">Its share of the formula, in the section's <see cref="PrintedSection.PercentHeader"/>; null for an empty batch.</param>
/// <param name="CostPerGram">The ingredient's buffered cost per gram, as the recipe form's Cost/g column shows it.</param>
/// <param name="Cost">What it adds to the batch's cost, at <paramref name="CostPerGram"/>.</param>
/// <param name="IsFormulaBase">Part of the formula's 100% base, usually the flour.</param>
public sealed record PrintedLine(string Ingredient, decimal Grams, decimal? Percent, decimal CostPerGram, decimal Cost, bool IsFormulaBase);

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
/// <param name="Cost">What the line brings into the composite's cost: <paramref name="Quantity"/> at <paramref name="RecipeCost"/>.</param>
public sealed record PrintedComponent(string Product, int? Quantity, decimal RecipeCost, decimal Cost)
{
    /// <summary>The Products table's rows, leaving out any without a product, which isn't a line yet.</summary>
    public static IReadOnlyList<PrintedComponent> From(IEnumerable<ProductComponentRow> rows) =>
    [
        .. rows
            .Where(r => r.Product is not null)
            .Select(r => new PrintedComponent(r.Product!.Name, r.Quantity, r.RecipeCost ?? 0m, r.Cost)),
    ];
}

/// <summary>One packaging line: how much of a material one sellable unit takes, in the unit it's typed in.</summary>
/// <param name="Qty">Null while nothing is typed for it.</param>
/// <param name="UnitCost">What one of <paramref name="Unit"/> costs, buffered.</param>
/// <param name="Cost">What the line costs for one sellable unit.</param>
public sealed record PrintedPackagingLine(string Material, decimal? Qty, string Unit, decimal UnitCost, decimal Cost);
