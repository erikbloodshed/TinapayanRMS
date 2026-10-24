using System.Globalization;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>A line of the Production Plan: a product, and how many of its sellable units to make.</summary>
/// <param name="find">A product by id; null for one that isn't there.</param>
/// <param name="costs">
/// A product's costing as saved, at buffered costs, by id: what the line sells for and earns (2026-10-24). Null, or a
/// product it has none for, gives the line no sales.
/// </param>
public sealed class PlanLineRow(Func<int, Product?> find, Func<int, ProductCostSummary?>? costs = null) : ReactiveObject
{
    /// <summary>
    /// An empty row, for the grid to compare an add row against (controls:GridAddNewRow.DropWhenUntouched). Without it
    /// every add row counted as typed into, so one left empty was added as a blank line (2026-10-24).
    /// </summary>
    public PlanLineRow()
        : this(_ => null)
    {
    }

    private ProductCostSummary? _costs;

    private int? _productId;
    /// <summary>The product picked, by id (the picker's value).</summary>
    public int? ProductId
    {
        get => _productId;
        set
        {
            // The row is put right before anyone hears, since the plan is worked out again from it straight away.
            _productId = value;
            FindProduct();
            this.RaisePropertyChanged();
        }
    }

    public Product? Product { get; private set; }

    /// <summary>
    /// Points the row at the product's copy from a fresh load. The pick hasn't changed, so ProductId stays quiet and the
    /// plan isn't worked out again for this line alone.
    /// </summary>
    public void FindProduct()
    {
        Product = _productId is { } id ? find(id) : null;
        _costs = _productId is { } costed ? costs?.Invoke(costed) : null;
        this.RaisePropertyChanged(nameof(PiecesText));
        this.RaisePropertyChanged(nameof(ProductName));
        RaiseSalesChanged();
    }

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
            RaiseSalesChanged();
            this.RaisePropertyChanged();
        }
    }

    /// <summary>"160 pcs" for 20 boxes of 8; blank for a composite, which is one of itself, or with no units yet.</summary>
    public string PiecesText => Product is { IsComposite: false } product && Units is > 0m and var units
        ? $"{RecipeScalingRules.PiecesFor(units, product.QuantityRequired):#,0.##} pcs"
        : string.Empty;

    /// <summary>
    /// What the units sell for and earn: at the product's Current Price, or its worked-out selling price when it has
    /// none (<see cref="HasNoCurrentPrice"/>), the profit over its base cost once VAT is out. Null with no units yet.
    /// </summary>
    public PlanSales? SalesFigures => _costs is { } summary && Units is > 0m and var units
        ? ProductionPlanRules.SalesOf(units, summary.CurrentPrice ?? summary.SellingPrice, summary.VatPercent, summary.BaseCost)
        : null;

    /// <summary>What the units sell for, with VAT.</summary>
    public decimal? Sales => SalesFigures?.Sales;

    /// <summary>What they earn over their base cost, VAT out.</summary>
    public decimal? Profit => SalesFigures?.Profit;

    /// <summary>The product has no Current Price, so its sales are at the selling price its costing works out.</summary>
    public bool HasNoCurrentPrice => _costs is { CurrentPrice: null };

    private void RaiseSalesChanged()
    {
        this.RaisePropertyChanged(nameof(SalesFigures));
        this.RaisePropertyChanged(nameof(Sales));
        this.RaisePropertyChanged(nameof(Profit));
        this.RaisePropertyChanged(nameof(HasNoCurrentPrice));
    }
}

/// <summary>
/// A row of the Mixing Sheet: a mix to make in whole (or half) batches, and the products it's for; or a made ingredient
/// to prepare for them, with no batches.
/// </summary>
/// <param name="Batches">Batches to make; null for a made ingredient, prepared to the gram.</param>
/// <param name="Grams">What the batches weigh before baking, or the made ingredient's grams.</param>
public sealed record MixRow(string Name, string For, decimal? Batches, decimal Grams)
{
    /// <summary>
    /// The pieces the batches rounded up make on top of what's planned (2026-10-24): "30 pcs" for a mix for one product,
    /// "Ube Roll 30 pcs or Cheese Roll 25 pcs" for one shared, since it's the same dough either way. Blank with none.
    /// </summary>
    public string Spare { get; init; } = string.Empty;

    /// <summary>
    /// What goes into it (2026-10-24), shown under the row: a mix's lines and add-ins, a batch's grams and all its
    /// batches'; a made ingredient's BOM, scaled to the grams to prepare.
    /// </summary>
    public IReadOnlyList<MixIngredientRow> Ingredients { get; init; } = [];
}

/// <summary>An ingredient of a mix, or of a made ingredient to prepare.</summary>
/// <param name="PerBatch">Its grams in one batch; null for a made ingredient's, which is prepared to the gram.</param>
/// <param name="Grams">Its grams in all the batches, or in the made ingredient to prepare.</param>
public sealed record MixIngredientRow(string Name, decimal? PerBatch, decimal Grams);

/// <summary>
/// A row of the Purchase Requisition: a purchased material's total, in its base unit, the purchase units to buy for it, and what
/// the total costs.
/// </summary>
/// <param name="Needed">In the material's base unit: grams of an ingredient, pieces or metres of packaging.</param>
/// <param name="Buy">Whole purchase units to buy; null for a material with no price, so no purchase unit.</param>
/// <param name="BuyUnit">What it's bought in, "sack"; null with no price.</param>
/// <param name="Cost">What the amount needed costs at the material's buffered cost; null with no cost.</param>
public sealed record PurchaseRequisitionRow(int MaterialId, string Name, string Type, decimal Needed, string Unit, decimal? Buy, string? BuyUnit, decimal? Cost)
{
    /// <summary>
    /// What the whole purchase units cost at the price of the brand they're bought as (2026-10-24): the money to take to the
    /// market, where Cost is only what's used. Null with no price.
    /// </summary>
    public decimal? BuyCost { get; init; }
}

/// <summary>A production plan as the screen shows it.</summary>
/// <param name="TotalCost">What the materials needed cost, at buffered costs.</param>
/// <param name="TotalBuyCost">What the whole purchase units to buy cost, at their brands' prices.</param>
public sealed record ProductionPlanResult(
    IReadOnlyList<MixRow> Mixes,
    IReadOnlyList<PurchaseRequisitionRow> Requisition,
    decimal TotalCost,
    decimal TotalBuyCost,
    IReadOnlyList<string> Notes)
{
    public static ProductionPlanResult Empty { get; } = new([], [], 0m, 0m, []);
}

/// <summary>
/// Everything a production plan is worked out from, with the products, ingredients and BOMs already in
/// ProductionPlanRules' terms. It's made once per load, so an edit to the plan only does the plan's own arithmetic.
/// </summary>
/// <param name="Products">Every product as saved, priced at its buffered costs, by id.</param>
/// <param name="Materials">Every raw material, by id.</param>
/// <param name="BrandPrices">Every purchased material's brand prices, by material.</param>
public sealed record ProductionPlanSource(
    IReadOnlyDictionary<int, SavedProduct> Products,
    IReadOnlyDictionary<int, RawMaterial> Materials,
    ILookup<int, RawMaterialBrandPrice> BrandPrices,
    IReadOnlyDictionary<int, PlanRecipe> Recipes,
    IReadOnlyDictionary<int, PlanIngredient> Ingredients,
    ILookup<int, (int ComponentId, decimal Grams)> Boms)
{
    public static ProductionPlanSource From(
        IReadOnlyDictionary<int, SavedProduct> products,
        IReadOnlyDictionary<int, RawMaterial> materials,
        IReadOnlyCollection<IngredientBomLine> bomLines,
        ILookup<int, RawMaterialBrandPrice> brandPrices) => new(
        products,
        materials,
        brandPrices,
        products.ToDictionary(p => p.Key, p => ProductionPlanBuilder.RecipeOf(p.Value)),
        materials.Values.Where(m => m.Type == RawMaterialType.Ingredient).ToDictionary(m => m.Id, ProductionPlanBuilder.IngredientOf),
        bomLines.ToLookup(l => l.IngredientId, l => (l.ComponentIngredientId, l.Grams)));
}

/// <summary>
/// Puts a production plan together from the products as saved (2026-10-03): their recipes handed to
/// ProductionPlanRules.Plan, and its mixes, made ingredients and purchases named and costed for the screen. Nothing is
/// read here.
/// </summary>
public static class ProductionPlanBuilder
{
    /// <param name="orders">Each product to make, by id, and how many of its sellable units.</param>
    /// <param name="halfBatches">Each mix is rounded up to half batches rather than whole ones.</param>
    public static ProductionPlanResult Build(IReadOnlyList<(int ProductId, decimal Units)> orders, ProductionPlanSource source, bool halfBatches = false)
    {
        if (orders.Count == 0)
        {
            return ProductionPlanResult.Empty;
        }

        var (products, materials, brandPrices) = (source.Products, source.Materials, source.BrandPrices);
        var plan = ProductionPlanRules.Plan(orders, source.Recipes, source.Ingredients, source.Boms, halfBatches);

        string NameOf(int id) => materials.GetValueOrDefault(id)?.Name ?? $"Raw material {id}";

        List<MixRow> mixes =
        [
            .. plan.Mixes.Select(mix => new MixRow(MixName(mix, products, NameOf), ForText(mix.Uses, products), mix.Batches, mix.BatchWeight * mix.Batches)
                {
                    Spare = SpareText(mix, products),
                    Ingredients = [.. mix.Lines.Select(l => new MixIngredientRow(NameOf(l.IngredientId), l.Grams, l.Grams * mix.Batches))],
                })
                .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase),
            .. plan.Prepared.Select(p => new MixRow(NameOf(p.Key), "Made ingredient, for the mixes above", null, p.Value)
                {
                    Ingredients = [.. plan.PreparedLines.GetValueOrDefault(p.Key, []).Select(l => new MixIngredientRow(NameOf(l.ComponentId), null, l.Grams))],
                })
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
            requisition.Sum(row => row.BuyCost ?? 0m),
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

    public static PlanIngredient IngredientOf(RawMaterial m) => new(m.Id, m.Source, m.WeightLossPercent ?? 0m, m.LinkedToId, m.WeightSharePercent);

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

    /// <summary>"30 pcs" for a mix for one product, "Ube Roll 30 pcs or Cheese Roll 25 pcs" for one shared; blank with nothing spare.</summary>
    private static string SpareText(PlannedMix mix, IReadOnlyDictionary<int, SavedProduct> products)
    {
        var spare = mix.Uses
            .Select(u => u.ProductId)
            .Distinct()
            .Select(id => (Id: id, Pieces: ProductionPlanRules.SparePieces(mix, id)))
            .Where(s => s.Pieces > 0m)
            .ToList();
        return spare.Count switch
        {
            0 => string.Empty,
            _ when mix.Uses.Select(u => u.ProductId).Distinct().Count() == 1 => string.Create(CultureInfo.CurrentCulture, $"{spare[0].Pieces:#,0} pcs"),
            _ => string.Join(" or ", spare.Select(s => string.Create(CultureInfo.CurrentCulture, $"{products[s.Id].Product.Name} {s.Pieces:#,0} pcs"))),
        };
    }

    private static PurchaseRequisitionRow RequisitionRowOf(int id, decimal needed, RawMaterial? material, IReadOnlyList<RawMaterialBrandPrice> prices)
    {
        if (material is null)
        {
            return new PurchaseRequisitionRow(id, $"Raw material {id}", "Ingredient", needed, "g", null, null, null);
        }

        var unit = PurchaseUnit.Of(material, prices);
        var cost = needed * material.BufferedCostPerBaseUnit;
        var buy = unit.Price is null ? (decimal?)null : RecipeScalingRules.UnitsToBuy(needed, unit.BaseUnitsPerUnit);
        return new PurchaseRequisitionRow(
            id,
            material.Name,
            material.Type == RawMaterialType.Ingredient ? "Ingredient" : "Packaging",
            needed,
            BaseUnitText.AbbreviationOf(material),
            buy,
            unit.Price is null ? null : unit.Name,
            cost > 0m ? cost : null)
        {
            BuyCost = buy * unit.Price,
        };
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
