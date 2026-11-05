using System.Collections.Immutable;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// What the Scaling tab scales: a product's recipe for one sellable unit, as the Products screen shows it (its form) or
/// as it's saved (a product a composite holds).
/// </summary>
/// <param name="Sections">Its sub-assemblies, in recipe order, each per piece.</param>
/// <param name="Packaging">Its packaging lines, each for one whole sellable unit.</param>
/// <param name="Components">A composite's products, each with how many of its sellable units go in; none for any other product.</param>
/// <param name="QuantityRequired">The pieces in one sellable unit.</param>
/// <param name="IncludesComponentPackaging">A composite's products bring their packaging too.</param>
public sealed record ScalingRecipe(
    IReadOnlyList<PricedSection> Sections,
    IReadOnlyList<PricedPackagingLine> Packaging,
    IReadOnlyList<(Product Product, int Quantity)> Components,
    int QuantityRequired,
    bool IncludesComponentPackaging)
{
    /// <summary>A saved product's recipe, as <see cref="SavedProductReader"/> built it.</summary>
    public static ScalingRecipe From(SavedProduct saved) => new(
        saved.Sections,
        saved.PackagingLines,
        [.. saved.Components.Select(c => (c.Component.ComponentProduct!, c.Component.Quantity))],
        saved.Product.QuantityRequired,
        saved.Product.IncludesComponentPackaging);
}

/// <summary>
/// Scales a recipe to a number of sellable units, as the Scaling tab's tree shows it: each section in whole batches
/// (RecipeScalingRules.BatchesFor), with its lines and add-ins under it and each made ingredient opening into its BOM, to
/// any depth; each product a composite holds, scaled to its share; and the packaging. Costs are at <paramref name="basis"/>,
/// the screen's. Nothing is read here: the BOMs, the ingredients and the products a composite holds are handed in.
/// </summary>
/// <param name="boms">Every made ingredient's BOM lines, by made ingredient, in order.</param>
/// <param name="ingredientsById">The screen's ingredients, which a BOM line's component is looked up in.</param>
/// <param name="findProduct">A product a composite holds, as saved and priced at <paramref name="basis"/>; null while it hasn't been read.</param>
public sealed class RecipeScaler(
    ILookup<int, IngredientBomLine> boms,
    IReadOnlyDictionary<int, RawMaterial> ingredientsById,
    Func<int, SavedProduct?> findProduct,
    CostBasis basis)
{
    /// <summary>What the tree shows under a product a composite holds until it's been read.</summary>
    public const string ReadingText = "Reading…";

    /// <summary>
    /// <paramref name="recipe"/> scaled to <paramref name="units"/> sellable units: the products a composite holds, then its
    /// sections, then the packaging. <paramref name="productId"/> is the product's own, or null for one not saved yet.
    /// </summary>
    public IReadOnlyList<ScalingRow> Scale(ScalingRecipe recipe, decimal units, int? productId) =>
        Scale(recipe, units, includesPackaging: true, productId is { } id ? [id] : []);

    private List<ScalingRow> Scale(ScalingRecipe recipe, decimal units, bool includesPackaging, ImmutableHashSet<int> holders)
    {
        // A product's packaging comes into a composite only when the composite takes its products' packaging, and into a
        // composite holding that composite only when that one does too, as the Costing tab adds them up.
        List<ScalingRow> rows =
        [
            .. recipe.Components.Select(c =>
                ScaleComponent(c.Product, c.Quantity, units, includesPackaging && recipe.IncludesComponentPackaging, holders)),
        ];

        var pieces = RecipeScalingRules.PiecesFor(units, recipe.QuantityRequired);
        rows.AddRange(recipe.Sections.Select(s => ScaleSection(s, pieces)));

        List<ScalingRow> packaging = [.. recipe.Packaging.Where(l => l.PackagingMaterial is not null).Select(l => ScalePackagingLine(l, units))];
        if (includesPackaging && packaging.Count > 0)
        {
            rows.Add(Group("Packaging", string.Empty, string.Empty, packaging));
        }

        return rows;
    }

    /// <summary>A product a composite holds: <paramref name="quantity"/> of its sellable units in each of the composite's.</summary>
    private ScalingRow ScaleComponent(Product product, int quantity, decimal units, bool includesPackaging, ImmutableHashSet<int> holders)
    {
        var name = CompositeProductRules.LineName(product.Name, quantity);

        // Save refuses a composite that holds itself; this only guards against data changed outside the app.
        if (holders.Contains(product.Id))
        {
            return Group(name, "Holds the product it's in", string.Empty, []);
        }

        if (findProduct(product.Id) is not { } saved)
        {
            return Group(name, ReadingText, string.Empty, []);
        }

        var componentUnits = units * quantity;
        return Group(
            name,
            UnitsText(componentUnits, saved.Product.QuantityRequired),
            string.Empty,
            Scale(ScalingRecipe.From(saved), componentUnits, includesPackaging, holders.Add(product.Id)));
    }

    /// <summary>A section in the whole batches that make <paramref name="pieces"/>, its lines and add-ins that many times over.</summary>
    private ScalingRow ScaleSection(PricedSection section, decimal pieces)
    {
        var batches = RecipeScalingRules.BatchesFor(pieces, section.AuthoredField, section.AuthoredValue, section.TotalWeight);
        if (batches == 0)
        {
            return Group(section.SectionName, section.TotalWeight > 0 ? "No piece size yet" : "No lines yet", string.Empty, []);
        }

        List<ScalingRow> lines =
        [
            .. section.Lines.Select(l => ScaleIngredient(l.Ingredient, l.Grams * batches, string.Empty, [])),
            .. section.AddIns.Select(a => ScaleIngredient(a.Ingredient, a.Grams * batches, "add-in", [])),
        ];

        var made = batches * section.Yield;
        var extra = RecipeScalingRules.ExtraPieces(batches, section.Yield, pieces);
        var detail = $"{batches:#,0} {(batches == 1 ? "batch" : "batches")} · {made:#,0.##} pcs"
            + (Math.Round(extra, 2) > 0 ? $" ({extra:#,0.##} extra)" : string.Empty);
        return Group(section.SectionName, detail, GramsText(section.TotalWeight * batches), lines);
    }

    /// <summary>
    /// <paramref name="grams"/> of an ingredient, and for a made one, its BOM under it scaled to make them (exactly: only
    /// sections go in whole batches). <paramref name="path"/> is the made ingredients above it, so a loop in the data stops.
    /// </summary>
    private ScalingRow ScaleIngredient(RawMaterial ingredient, decimal grams, string detail, ImmutableHashSet<int> path)
    {
        List<ScalingRow> children = [];
        var bom = boms[ingredient.Id].ToList();
        if (ingredient.Source == RawMaterialSource.Made && bom.Count > 0 && !path.Contains(ingredient.Id))
        {
            var scaled = RecipeScalingRules.ScaleBom(grams, [.. bom.Select(l => l.Grams)], ingredient.WeightLossPercent ?? 0m);
            var inner = path.Add(ingredient.Id);
            foreach (var (line, lineGrams) in bom.Zip(scaled))
            {
                if (ingredientsById.TryGetValue(line.ComponentIngredientId, out var component))
                {
                    children.Add(ScaleIngredient(component, lineGrams, string.Empty, inner));
                }
            }
        }

        var cost = grams * RawMaterialRules.CostPerBaseUnitAt(ingredient, basis);
        return new ScalingRow(ingredient.Name, detail, GramsText(grams), cost, false, children);
    }

    /// <summary>A packaging line for <paramref name="units"/> sellable units, in the unit it's typed in.</summary>
    private static ScalingRow ScalePackagingLine(PricedPackagingLine line, decimal units)
    {
        var quantity = (line.QtyPerUnit ?? 0m) * units;
        var unit = line.UnitAbbreviation ?? string.Empty;
        return ScalingRow.Line(line.PackagingMaterial!.Name, string.Empty, $"{quantity:#,0.##} {unit}".TrimEnd(), line.CostPerUnit * units);
    }

    /// <summary>
    /// The ingredients <paramref name="recipe"/> takes, each once, in the order the tree lists them: its lines and add-ins,
    /// what its made ingredients are made of, and those of the products a composite holds. What the Scaling tab's "from what
    /// you have" picker offers.
    /// </summary>
    public IReadOnlyList<RawMaterial> IngredientsOf(ScalingRecipe recipe, int? productId) =>
        [.. Amounts(recipe, 1m, productId is { } id ? [id] : []).Select(a => a.Ingredient).DistinctBy(i => i.Id)];

    /// <summary>
    /// The grams of <paramref name="ingredientId"/> that <paramref name="units"/> sellable units take, wherever it is in the
    /// recipe, as the tree adds it up: each section in whole batches, a made ingredient's BOM opened exactly.
    /// </summary>
    public decimal GramsOf(ScalingRecipe recipe, decimal units, int? productId, int ingredientId) =>
        Amounts(recipe, units, productId is { } id ? [id] : []).Where(a => a.Ingredient.Id == ingredientId).Sum(a => a.Grams);

    /// <summary>
    /// The most whole sellable units that <paramref name="grams"/> of <paramref name="ingredientId"/> are enough for (the
    /// Scaling tab's "from what you have"): 0 when not even one is, or the recipe doesn't take it. More units never take
    /// less, since a section only ever needs more batches, so the most is found by doubling, then halving back.
    /// </summary>
    public int UnitsFrom(ScalingRecipe recipe, int? productId, int ingredientId, decimal grams)
    {
        bool Fits(int units)
        {
            var needed = GramsOf(recipe, units, productId, ingredientId);
            return needed > 0m && needed <= grams;
        }

        if (!Fits(1))
        {
            return 0;
        }

        const int Most = 1_000_000;
        var low = 1;
        var high = 2;
        while (high < Most && Fits(high))
        {
            low = high;
            high *= 2;
        }

        high = Math.Min(high, Most);
        while (high - low > 1)
        {
            var middle = low + ((high - low) / 2);
            if (Fits(middle))
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return Fits(high) ? high : low;
    }

    /// <summary>Every ingredient the recipe takes for <paramref name="units"/>, as Scale walks it, with its grams there.</summary>
    private IEnumerable<(RawMaterial Ingredient, decimal Grams)> Amounts(ScalingRecipe recipe, decimal units, ImmutableHashSet<int> holders)
    {
        foreach (var (product, quantity) in recipe.Components)
        {
            if (!holders.Contains(product.Id) && findProduct(product.Id) is { } saved)
            {
                foreach (var amount in Amounts(ScalingRecipe.From(saved), units * quantity, holders.Add(product.Id)))
                {
                    yield return amount;
                }
            }
        }

        var pieces = RecipeScalingRules.PiecesFor(units, recipe.QuantityRequired);
        foreach (var section in recipe.Sections)
        {
            var batches = RecipeScalingRules.BatchesFor(pieces, section.AuthoredField, section.AuthoredValue, section.TotalWeight);
            if (batches == 0)
            {
                continue;
            }

            foreach (var (ingredient, grams) in section.Lines.Select(l => (l.Ingredient, l.Grams)).Concat(section.AddIns.Select(a => (a.Ingredient, a.Grams))))
            {
                foreach (var amount in IngredientAmounts(ingredient, grams * batches, []))
                {
                    yield return amount;
                }
            }
        }
    }

    /// <summary><paramref name="grams"/> of an ingredient, then, for a made one, what its BOM takes to make them, as ScaleIngredient opens it.</summary>
    private IEnumerable<(RawMaterial Ingredient, decimal Grams)> IngredientAmounts(RawMaterial ingredient, decimal grams, ImmutableHashSet<int> path)
    {
        yield return (ingredient, grams);
        var bom = boms[ingredient.Id].ToList();
        if (ingredient.Source != RawMaterialSource.Made || bom.Count == 0 || path.Contains(ingredient.Id))
        {
            yield break;
        }

        var scaled = RecipeScalingRules.ScaleBom(grams, [.. bom.Select(l => l.Grams)], ingredient.WeightLossPercent ?? 0m);
        foreach (var (line, componentGrams) in bom.Zip(scaled))
        {
            if (ingredientsById.TryGetValue(line.ComponentIngredientId, out var component))
            {
                foreach (var amount in IngredientAmounts(component, componentGrams, path.Add(ingredient.Id)))
                {
                    yield return amount;
                }
            }
        }
    }

    /// <summary>A row with others under it, costing what they cost.</summary>
    private static ScalingRow Group(string name, string detail, string amountText, IReadOnlyList<ScalingRow> children) =>
        new(name, detail, amountText, children.Sum(c => c.Cost), true, children);

    /// <summary>"2,000 g".</summary>
    public static string GramsText(decimal grams) => $"{grams:#,0.##} g";

    /// <summary>"120 pcs" for a product sold by the piece, "20 units · 160 pcs" for a Box of 8.</summary>
    public static string UnitsText(decimal units, int quantityRequired) => quantityRequired == 1
        ? $"{units:#,0.##} pcs"
        : $"{units:#,0.##} {(units == 1 ? "unit" : "units")} · {RecipeScalingRules.PiecesFor(units, quantityRequired):#,0.##} pcs";
}
