using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Mcp.Results;

/// <summary>
/// What the tools return, serialized to JSON for the model. Costs are worked out at full precision and written to
/// 6 decimal places (ToolJson). Every product figure is for one sellable unit (QuantityRequired pieces).
/// </summary>
public sealed record ProductListing(
    string Name,
    string Category,
    string Stage,
    bool IsComposite,
    int QuantityRequired,
    decimal BaseCost,
    decimal SellingPrice,
    decimal RoundedSellingPrice,
    decimal? CurrentPrice,
    decimal? CurrentMarkupPercent,
    bool IsBelowTargetMarkup)
{
    public static ProductListing From(SavedProduct saved) => new(
        saved.Product.Name,
        CategoryOf(saved.Product),
        ProductRules.StageTitle(saved.Product.Stage),
        saved.Product.IsComposite,
        saved.Product.QuantityRequired,
        saved.Costs.BaseCost,
        saved.Costs.SellingPrice,
        saved.Costs.RoundedSellingPrice,
        saved.Costs.CurrentPrice,
        saved.Costs.CurrentMarkupPercent,
        saved.Costs.IsBelowTargetMarkup);

    internal static string CategoryOf(Product product) => product.Category?.Name ?? ProductRules.UncategorizedTitle;
}

/// <summary>A product as the Products screen shows it once opened: its recipe, packaging and costing.</summary>
public sealed record ProductCostSheet(
    string Name,
    string Category,
    string Stage,
    bool IsComposite,
    int QuantityRequired,
    string CostBasis,
    IReadOnlyList<SectionCosting> Sections,
    IReadOnlyList<ComponentCosting> Components,
    IReadOnlyList<PackagingCosting> Packaging,
    string? PackagingLinkedTo,
    CostSummary Costs)
{
    /// <summary>Its bulk packaging: what one bulk pack takes, how many products it holds, and each one's share; null with none.</summary>
    public BulkPackagingCosting? BulkPackaging { get; init; }

    public static ProductCostSheet From(SavedProduct saved) => new(
        saved.Product.Name,
        ProductListing.CategoryOf(saved.Product),
        ProductRules.StageTitle(saved.Product.Stage),
        saved.Product.IsComposite,
        saved.Product.QuantityRequired,
        saved.CostBasis.ToString(),
        [.. saved.Sections.Select(s => SectionCosting.From(s, saved.CostBasis))],
        [.. saved.Components.Select(c => ComponentCosting.From(c, saved.CostBasis, saved.Product.IncludesComponentPackaging))],
        [.. saved.PackagingLines.Select(PackagingCosting.From)],
        saved.PackagingLinkedTo,
        CostSummary.From(saved.Costs))
    {
        BulkPackaging = saved.BulkPackaging is { } bulk ? BulkPackagingCosting.From(bulk) : null,
    };
}

/// <summary>
/// One section of a recipe (a sub-assembly it uses): its batch, how much of it a piece takes, and its lines. Line and
/// batch costs are for the whole batch; CostPerPiece is what one piece takes of it, add-ins included. Its lines that are the
/// recipe's other sections (SectionLines, each named by the section and costed at its cost per gram; null when none) are in
/// its batch; what the others use of it (UsedElsewhereGrams; null when nothing) leaves the rest for its own pieces, unless it's
/// locked as their ingredient (IsLocked; null when not), with no pieces of its own.
/// </summary>
public sealed record SectionCosting(
    string Name,
    string? LinkedFrom,
    decimal BatchWeightGrams,
    decimal BatchCost,
    decimal WeightPerPieceGrams,
    decimal Yield,
    decimal CostPerPiece,
    IReadOnlyList<LineCosting> Lines,
    IReadOnlyList<LineCosting> AddIns,
    IReadOnlyList<LineCosting>? SectionLines = null,
    decimal? UsedElsewhereGrams = null,
    bool? IsLocked = null)
{
    public static SectionCosting From(PricedSection section, CostBasis basis) => new(
        section.SectionName,
        section.LinkedFrom,
        section.TotalWeight,
        section.TotalCostAt(basis),
        section.WeightPerPiece,
        section.Yield,
        section.CostPerPieceAt(basis),
        [.. section.Lines.Select(l => LineCosting.From(l, basis))],
        [.. section.AddIns.Select(l => LineCosting.From(l, basis))],
        section.SectionLines.Count > 0
            ? [.. section.SectionLines.Select(l => new LineCosting(l.SectionName, l.Grams, l.CostPerGramAt(basis), l.CostAt(basis)))]
            : null,
        section.IsUsedElsewhere ? section.Figures.UsedElsewhereWeight : null,
        section.IsLocked ? true : null);
}

public sealed record LineCosting(string Ingredient, decimal Grams, decimal CostPerGram, decimal Cost)
{
    public static LineCosting From(SectionLine line, CostBasis basis) =>
        new(line.Ingredient.Name, line.Grams, line.CostPerGramAt(basis), line.CostAt(basis));
}

/// <summary>A composite's product line: what one of the product brings, and what the line brings for its quantity.</summary>
public sealed record ComponentCosting(string Product, int Quantity, decimal RecipeCostEach, decimal Cost, decimal? PackagingCost)
{
    public static ComponentCosting From(SavedComponent component, CostBasis basis, bool includesPackaging) => new(
        component.Component.ComponentProduct!.Name,
        component.Component.Quantity,
        component.RecipeCostAt(basis),
        component.CostAt(basis),
        includesPackaging ? component.PackagingCostAt(basis) : null);
}

public sealed record PackagingCosting(string? Material, decimal? Quantity, string? Unit, decimal Cost)
{
    public static PackagingCosting From(PricedPackagingLine line) => new(
        line.PackagingMaterial?.Name,
        line.QtyPerUnit,
        line.UnitAbbreviation,
        line.CostPerUnit);
}

/// <summary>
/// A product's bulk packaging: each line's quantity and cost for one bulk pack, how many products the pack holds, what it
/// costs, and each product's share (in the Packaging cost).
/// </summary>
public sealed record BulkPackagingCosting(IReadOnlyList<PackagingCosting> LinesPerPack, int? ProductsPerPack, decimal PackCost, decimal CostPerUnit)
{
    public static BulkPackagingCosting From(PricedBulkPackaging bulk) =>
        new([.. bulk.Lines.Select(PackagingCosting.From)], bulk.Quantity, bulk.PackCost, bulk.CostPerUnit);
}

/// <summary>The Costing tab, from the materials down to the selling price.</summary>
public sealed record CostSummary(
    decimal SubAssemblies,
    decimal Packaging,
    decimal Material,
    decimal WastagePercent,
    decimal Wastage,
    decimal LaborPercent,
    decimal Labor,
    decimal OverheadPercent,
    decimal Overhead,
    decimal AdministrativePercent,
    decimal Administrative,
    decimal BaseCost,
    decimal MarkupPercent,
    decimal PreVatPrice,
    decimal VatPercent,
    decimal SellingPrice,
    decimal RoundedSellingPrice,
    decimal? CurrentPrice,
    decimal? CurrentMarkupPercent,
    bool IsBelowTargetMarkup)
{
    public static CostSummary From(ProductCostSummary costs) => new(
        costs.SubAssemblies,
        costs.Packaging,
        costs.Material,
        costs.WastagePercent,
        costs.Wastage,
        costs.LaborPercent,
        costs.Labor,
        costs.OverheadPercent,
        costs.Overhead,
        costs.AdministrativePercent,
        costs.Administrative,
        costs.BaseCost,
        costs.MarkupPercent,
        costs.PreVatPrice,
        costs.VatPercent,
        costs.SellingPrice,
        costs.RoundedSellingPrice,
        costs.CurrentPrice,
        costs.CurrentMarkupPercent,
        costs.IsBelowTargetMarkup);
}

/// <summary>A price change tried: one of the inputs, worked out as the Price Simulator screen's Price Changes row would be.</summary>
/// <param name="Reaches">The made and linked ingredients it reprices, however deeply.</param>
public sealed record PriceChangeTried(
    string Material,
    string Unit,
    decimal? CurrentPrice,
    decimal NewPrice,
    decimal? ChangePercent,
    IReadOnlyList<string> Reaches);

/// <summary>A product with the changes beside it now, as a row of the Price Simulator screen's table.</summary>
public sealed record SimulatedProduct(
    string Name,
    string Category,
    decimal BaseCost,
    decimal SimulatedBaseCost,
    decimal ChangePercent,
    decimal SellingPrice,
    decimal SimulatedSellingPrice,
    decimal? CurrentPrice,
    decimal? MarkupPercent,
    decimal? SimulatedMarkupPercent,
    decimal? PriceToKeepMarkup)
{
    public static SimulatedProduct From(PriceSimulatorProductRow row) => new(
        row.ProductName,
        row.Category,
        row.BaseCost,
        row.SimulatedBaseCost,
        row.ChangePercent,
        row.SuggestedPrice,
        row.SimulatedSuggestedPrice,
        row.CurrentPrice,
        row.MarkupPercent,
        row.SimulatedMarkupPercent,
        row.PriceToKeepMarkup);
}

/// <param name="Products">The products the changes reach, the most moved first; every product when asked for.</param>
public sealed record PriceSimulation(string CostBasis, IReadOnlyList<PriceChangeTried> Changes, int ProductsReached, IReadOnlyList<SimulatedProduct> Products);
