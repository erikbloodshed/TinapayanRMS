using System.Globalization;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>A line of the Production Plan: a product, and how many of its sellable units to make.</summary>
/// <param name="find">A product by id; null for one that isn't there.</param>
public sealed class PlanLineRow(Func<int, Product?> find) : ReactiveObject
{
    private int? _productId;
    /// <summary>The product picked, by id (the picker's value).</summary>
    public int? ProductId
    {
        get => _productId;
        set
        {
            // The row is put right before anyone hears, since the plan is worked out again from it straight away.
            _productId = value;
            Product = value is { } id ? find(id) : null;
            this.RaisePropertyChanged(nameof(PiecesText));
            this.RaisePropertyChanged(nameof(ProductName));
            this.RaisePropertyChanged();
        }
    }

    public Product? Product { get; private set; }

    public string ProductName => Product?.Name ?? string.Empty;

    private decimal? _units;
    /// <summary>Sellable units to make: pieces for a product sold by the piece, boxes for a Box of 8.</summary>
    public decimal? Units
    {
        get => _units;
        set
        {
            _units = value;
            this.RaisePropertyChanged(nameof(PiecesText));
            this.RaisePropertyChanged();
        }
    }

    /// <summary>"160 pcs" for 20 boxes of 8; blank for a composite, which is one of itself, or with no units yet.</summary>
    public string PiecesText => Product is { IsComposite: false } product && Units is > 0m and var units
        ? $"{RecipeScalingRules.PiecesFor(units, product.QuantityRequired):#,0.##} pcs"
        : string.Empty;
}

/// <summary>
/// A row of the Mixing Sheet: a mix to make in whole batches, and the products it's for; or a made ingredient to prepare
/// for them, with no batches.
/// </summary>
/// <param name="Batches">Whole batches to make; null for a made ingredient, prepared to the gram.</param>
/// <param name="Grams">What the batches weigh before baking, or the made ingredient's grams.</param>
public sealed record MixRow(string Name, string For, decimal? Batches, decimal Grams);

/// <summary>
/// A row of the Purchase Requisition: a purchased material's total, in its base unit, the purchase units to buy for it, and what
/// the total costs.
/// </summary>
/// <param name="Needed">In the material's base unit: grams of an ingredient, pieces or metres of packaging.</param>
/// <param name="Buy">Whole purchase units to buy; null for a material with no price, so no purchase unit.</param>
/// <param name="BuyUnit">What it's bought in, "sack"; null with no price.</param>
/// <param name="Cost">What the amount needed costs at the material's buffered cost; null with no cost.</param>
public sealed record PurchaseRequisitionRow(int MaterialId, string Name, string Type, decimal Needed, string Unit, decimal? Buy, string? BuyUnit, decimal? Cost);

/// <summary>A production plan as the screen shows it.</summary>
public sealed record ProductionPlanResult(IReadOnlyList<MixRow> Mixes, IReadOnlyList<PurchaseRequisitionRow> Requisition, decimal TotalCost, IReadOnlyList<string> Notes)
{
    public static ProductionPlanResult Empty { get; } = new([], [], 0m, []);
}

/// <summary>
/// Puts a production plan together from the products as saved (2026-10-03): their recipes handed to
/// ProductionPlanRules.Plan, and its mixes, made ingredients and purchases named and costed for the screen. Nothing is
/// read here.
/// </summary>
public static class ProductionPlanBuilder
{
    /// <param name="orders">Each product to make, by id, and how many of its sellable units.</param>
    /// <param name="products">Every product as saved, priced at its buffered costs, by id.</param>
    /// <param name="materials">Every raw material, by id.</param>
    /// <param name="brandPrices">Every purchased material's brand prices, by material.</param>
    public static ProductionPlanResult Build(
        IReadOnlyList<(int ProductId, decimal Units)> orders,
        IReadOnlyDictionary<int, SavedProduct> products,
        IReadOnlyDictionary<int, RawMaterial> materials,
        IReadOnlyCollection<IngredientBomLine> bomLines,
        ILookup<int, RawMaterialBrandPrice> brandPrices)
    {
        if (orders.Count == 0)
        {
            return ProductionPlanResult.Empty;
        }

        var plan = ProductionPlanRules.Plan(
            orders,
            products.ToDictionary(p => p.Key, p => RecipeOf(p.Value)),
            materials.Values.Where(m => m.Type == RawMaterialType.Ingredient).ToDictionary(m => m.Id, IngredientOf),
            bomLines.ToLookup(l => l.IngredientId, l => (l.ComponentIngredientId, l.Grams)));

        string NameOf(int id) => materials.GetValueOrDefault(id)?.Name ?? $"Raw material {id}";

        List<MixRow> mixes =
        [
            .. plan.Mixes.Select(mix => new MixRow(MixName(mix, products, NameOf), ForText(mix.Uses, products), mix.Batches, mix.BatchWeight * mix.Batches))
                .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            .. plan.Prepared.Select(p => new MixRow(NameOf(p.Key), "Made ingredient, for the mixes above", null, p.Value))
                .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
        ];

        List<PurchaseRequisitionRow> requisition =
        [
            .. plan.Purchases
                .Where(p => p.Value > 0m)
                .Select(p => RequisitionRowOf(p.Key, p.Value, materials.GetValueOrDefault(p.Key), brandPrices[p.Key].ToList()))
                .OrderBy(row => row.Type == "Packaging")
                .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
        ];

        return new ProductionPlanResult(
            mixes,
            requisition,
            requisition.Sum(row => row.Cost ?? 0m),
            [.. plan.Issues.Select(issue => NoteOf(issue, products, NameOf)).Distinct()]);
    }

    /// <summary>What the plan needs of a product: its sections, its packaging in base units, and a composite's products.</summary>
    public static PlanRecipe RecipeOf(SavedProduct saved) => new(
        saved.Product.QuantityRequired,
        [
            .. saved.Sections.Select(s => new PlanSection(
                s.SubAssemblyId,
                [.. s.Lines.Select(l => (l.Ingredient.Id, l.Grams))],
                [.. s.AddIns.Select(a => (a.Ingredient.Id, a.Grams))],
                s.AuthoredField,
                s.AuthoredValue)),
        ],
        [.. saved.PackagingLines.Where(l => l.PackagingMaterialId is not null && l.BaseQty is > 0m).Select(l => (l.PackagingMaterialId!.Value, l.BaseQty!.Value))],
        [.. saved.Components.Select(c => (c.Component.ComponentProductId, c.Component.Quantity))],
        saved.Product.IncludesComponentPackaging);

    private static PlanIngredient IngredientOf(RawMaterial m) => new(m.Id, m.Source, m.WeightLossPercent ?? 0m, m.LinkedToId, m.WeightSharePercent);

    /// <summary>
    /// What the kitchen calls a mix: a linked sub-assembly's own name ("Sweet Dough"), or a product's own section by the
    /// product and its tab ("Ube Roll Filling"); with the add-ins that make it a mix of its own ("Sweet Dough + Ube Flavour").
    /// </summary>
    private static string MixName(PlannedMix mix, IReadOnlyDictionary<int, SavedProduct> products, Func<int, string> nameOf)
    {
        var product = products[mix.Uses[0].ProductId];
        var section = product.Sections.First(s => s.SubAssemblyId == mix.SubAssemblyId);
        var name = section.LinkedName ?? $"{product.Product.Name} {section.SectionName}";
        return mix.AddIns.Count == 0 ? name : $"{name} + {string.Join(", ", mix.AddIns.Select(a => nameOf(a.IngredientId)))}";
    }

    /// <summary>"Ube Roll 40 pcs, Cheese Roll 60 pcs": each product the mix is for, and the pieces it takes from it.</summary>
    private static string ForText(IReadOnlyList<PlannedUse> uses, IReadOnlyDictionary<int, SavedProduct> products) =>
        string.Join(", ", uses
            .GroupBy(u => u.ProductId)
            .Select(g => string.Create(CultureInfo.CurrentCulture, $"{products[g.Key].Product.Name} {g.Sum(u => u.Pieces):#,0.##} pcs")));

    private static PurchaseRequisitionRow RequisitionRowOf(int id, decimal needed, RawMaterial? material, IReadOnlyList<RawMaterialBrandPrice> prices)
    {
        if (material is null)
        {
            return new PurchaseRequisitionRow(id, $"Raw material {id}", "Ingredient", needed, "g", null, null, null);
        }

        var unit = PurchaseUnit.Of(material, prices);
        var cost = needed * material.BufferedCostPerBaseUnit;
        return new PurchaseRequisitionRow(
            id,
            material.Name,
            material.Type == RawMaterialType.Ingredient ? "Ingredient" : "Packaging",
            needed,
            BaseUnitText.AbbreviationOf(material),
            unit.Price is null ? null : RecipeScalingRules.UnitsToBuy(needed, unit.BaseUnitsPerUnit),
            unit.Price is null ? null : unit.Name,
            cost > 0m ? cost : null);
    }

    private static string NoteOf(PlanIssue issue, IReadOnlyDictionary<int, SavedProduct> products, Func<int, string> nameOf)
    {
        var product = issue.ProductId is { } productId && products.TryGetValue(productId, out var saved) ? saved : null;
        var section = product?.Sections.FirstOrDefault(s => s.SubAssemblyId == issue.SubAssemblyId)?.SectionName ?? "a sub-assembly";
        return issue.Kind switch
        {
            PlanIssueKind.NoPieceSize => $"{product?.Product.Name}: its {section} has no weight per piece or yield yet, so it isn't planned.",
            PlanIssueKind.NoLines => $"{product?.Product.Name}: its {section} has no ingredients yet, so it isn't planned.",
            PlanIssueKind.HoldsItself => $"{product?.Product.Name} holds itself through the products in it, so they aren't all planned.",
            PlanIssueKind.MissingProduct => "A product in the plan is no longer there, so it isn't planned.",
            PlanIssueKind.MadeWithoutBom => $"{nameOf(issue.IngredientId!.Value)} is made, but has no ingredients listed, so it's listed to buy as it is.",
            PlanIssueKind.BrokenLink => $"{nameOf(issue.IngredientId!.Value)} isn't linked to what it's cut from, so it's listed to buy as it is.",
            _ => issue.Kind.ToString(),
        };
    }
}
