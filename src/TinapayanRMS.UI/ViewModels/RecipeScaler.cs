using System.Collections.Immutable;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

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
    IReadOnlyList<ProductUsageEdit> Sections,
    IReadOnlyList<PackagingLineRow> Packaging,
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
        var name = ProductComponentRow.LineNameFor(product.Name, quantity);

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
    private ScalingRow ScaleSection(ProductUsageEdit section, decimal pieces)
    {
        var batches = RecipeScalingRules.BatchesFor(pieces, section.AuthoredField, section.AuthoredValue, section.FinalWeight);
        if (batches == 0)
        {
            return Group(section.SectionName, section.FinalWeight > 0 ? "No piece size yet" : "No lines yet", string.Empty, []);
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
            children =
            [
                .. bom.Zip(scaled)
                    .Where(pair => ingredientsById.ContainsKey(pair.First.ComponentIngredientId))
                    .Select(pair => ScaleIngredient(ingredientsById[pair.First.ComponentIngredientId], pair.Second, string.Empty, inner)),
            ];
        }

        var cost = grams * RawMaterialRules.CostPerBaseUnitAt(ingredient, basis);
        return new ScalingRow(ingredient.Name, detail, GramsText(grams), cost, false, children);
    }

    /// <summary>A packaging line for <paramref name="units"/> sellable units, in the unit it's typed in.</summary>
    private static ScalingRow ScalePackagingLine(PackagingLineRow line, decimal units)
    {
        var quantity = (line.QtyPerUnit ?? 0m) * units;
        var unit = line.UnitOptions.FirstOrDefault(o => o.Key == line.UnitKey)?.Abbreviation ?? line.BaseUnitAbbreviation;
        return ScalingRow.Line(line.PackagingMaterial!.Name, string.Empty, $"{quantity:#,0.##} {unit}".TrimEnd(), line.CostPerUnit * units);
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
