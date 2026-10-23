using System.Collections.Immutable;
using System.Globalization;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>One section of a product's recipe as the plan needs it: what a batch is made of, and how big a piece is.</summary>
/// <param name="Lines">The sub-assembly's lines, grams per batch, by ingredient.</param>
/// <param name="AddIns">This product's own add-ins on top of the batch, grams per batch.</param>
public sealed record PlanSection(
    int SubAssemblyId,
    IReadOnlyList<(int IngredientId, decimal Grams)> Lines,
    IReadOnlyList<(int IngredientId, decimal Grams)> AddIns,
    UsageAuthoredField AuthoredField,
    decimal AuthoredValue);

/// <summary>A product's recipe for one sellable unit, as the plan needs it.</summary>
/// <param name="PackagingPerUnit">Its packaging for one sellable unit, in each material's base unit.</param>
/// <param name="Components">A composite's products and how many of their sellable units go in one of it; none for any other.</param>
public sealed record PlanRecipe(
    int QuantityRequired,
    IReadOnlyList<PlanSection> Sections,
    IReadOnlyList<(int MaterialId, decimal BaseQuantity)> PackagingPerUnit,
    IReadOnlyList<(int ProductId, int Quantity)> Components,
    bool IncludesComponentPackaging);

/// <summary>An ingredient as the plan opens it: what it's made of or cut from.</summary>
public sealed record PlanIngredient(int Id, RawMaterialSource Source, decimal WeightLossPercent, int? LinkedToId, decimal? WeightSharePercent);

/// <summary>A product a mix is made for, and the pieces it takes from it.</summary>
public sealed record PlannedUse(int ProductId, decimal Pieces);

/// <summary>
/// One mix to make: a sub-assembly, with the add-ins every product it's for puts in it (none for most), in whole batches.
/// </summary>
/// <param name="BatchesNeeded">The batches its products need between them, before rounding up.</param>
/// <param name="Batches">The whole batches to make.</param>
/// <param name="BatchWeight">What a batch weighs before it's baked: its lines and add-ins.</param>
public sealed record PlannedMix(
    int SubAssemblyId,
    IReadOnlyList<(int IngredientId, decimal Grams)> AddIns,
    decimal BatchesNeeded,
    decimal Batches,
    decimal BatchWeight,
    IReadOnlyList<PlannedUse> Uses);

public enum PlanIssueKind
{
    /// <summary>A section with no weight per piece or yield typed yet: nothing of it is planned.</summary>
    NoPieceSize,

    /// <summary>A section with no lines: nothing of it is planned.</summary>
    NoLines,

    /// <summary>A composite holding itself, through the products in it.</summary>
    HoldsItself,

    /// <summary>A product in the plan, or held by a composite in it, that isn't there any more.</summary>
    MissingProduct,

    /// <summary>A made ingredient with no BOM lines: it's listed to buy as it is.</summary>
    MadeWithoutBom,

    /// <summary>A linked ingredient without what it's cut from: it's listed to buy as it is.</summary>
    BrokenLink,
}

/// <summary>Something the plan couldn't work out, with the product, sub-assembly or ingredient it's about.</summary>
public sealed record PlanIssue(PlanIssueKind Kind, int? ProductId = null, int? SubAssemblyId = null, int? IngredientId = null);

/// <summary>A production plan worked out: the mixes to make, the made ingredients to prepare, and what to buy.</summary>
/// <param name="Prepared">Grams of each made ingredient the mixes take, which are made from what's bought.</param>
/// <param name="Purchases">Each purchased material's total in its base unit: grams of an ingredient, pieces or metres of packaging.</param>
public sealed record ProductionPlan(
    IReadOnlyList<PlannedMix> Mixes,
    IReadOnlyDictionary<int, decimal> Prepared,
    IReadOnlyDictionary<int, decimal> Purchases,
    IReadOnlyList<PlanIssue> Issues);

/// <summary>
/// The Production Plan screen's arithmetic (2026-10-03): from the products to make, the whole batches of each mix and
/// everything to buy for them. A plan of one product gives what its Scaling tab shows, and several products sharing a
/// mix share its batches:
/// <list type="bullet">
/// <item>Each section's batches are worked out as the Scaling tab works them out (RecipeScalingRules.BatchRatio), but
/// added up across the products using the same sub-assembly with the same add-ins before being rounded up, once: two
/// products needing 0.4 of a dough each make 1 batch, not 2. A product's own add-ins go into every batch, so a use with
/// add-ins of its own is a mix of its own.</item>
/// <item>A made ingredient is opened into its BOM (RecipeScalingRules.ScaleBom), to any depth, and only what it's made of
/// is bought.</item>
/// <item>A linked ingredient is bought as what it's cut from: grams ÷ its weight share. The parts of one ingredient are
/// cut from the same pieces of it, so the largest share decides (300 g of egg white and 100 g of yolk take 500 g of eggs,
/// not 833 g), and any use of the ingredient itself comes on top.</item>
/// <item>Packaging is its quantity per sellable unit × the units, and a composite's products bring theirs only when it
/// includes its products' packaging, at every level.</item>
/// </list>
/// </summary>
public static class ProductionPlanRules
{
    /// <param name="orders">Each product to make, by id, and how many of its sellable units.</param>
    /// <param name="recipes">Every product's recipe, the ones composites hold included.</param>
    /// <param name="ingredients">Every ingredient, by id.</param>
    /// <param name="boms">Every made ingredient's BOM lines, by made ingredient: each component and its grams.</param>
    public static ProductionPlan Plan(
        IReadOnlyList<(int ProductId, decimal Units)> orders,
        IReadOnlyDictionary<int, PlanRecipe> recipes,
        IReadOnlyDictionary<int, PlanIngredient> ingredients,
        ILookup<int, (int ComponentId, decimal Grams)> boms)
    {
        var mixes = new Dictionary<string, MixTotal>();
        var purchases = new Dictionary<int, decimal>();
        var issues = new List<PlanIssue>();

        void Visit(int productId, decimal units, bool includesPackaging, ImmutableHashSet<int> holders)
        {
            if (holders.Contains(productId))
            {
                issues.Add(new PlanIssue(PlanIssueKind.HoldsItself, productId));
                return;
            }

            if (!recipes.TryGetValue(productId, out var recipe))
            {
                issues.Add(new PlanIssue(PlanIssueKind.MissingProduct, productId));
                return;
            }

            foreach (var (heldId, quantity) in recipe.Components)
            {
                Visit(heldId, units * quantity, includesPackaging && recipe.IncludesComponentPackaging, holders.Add(productId));
            }

            var pieces = RecipeScalingRules.PiecesFor(units, recipe.QuantityRequired);
            foreach (var section in recipe.Sections)
            {
                var batchWeight = section.Lines.Sum(l => l.Grams) + section.AddIns.Sum(a => a.Grams);
                var ratio = RecipeScalingRules.BatchRatio(pieces, section.AuthoredField, section.AuthoredValue, batchWeight);
                if (ratio == 0m)
                {
                    if (pieces > 0)
                    {
                        issues.Add(new PlanIssue(batchWeight > 0 ? PlanIssueKind.NoPieceSize : PlanIssueKind.NoLines, productId, section.SubAssemblyId));
                    }

                    continue;
                }

                var key = MixKey(section);
                if (!mixes.TryGetValue(key, out var mix))
                {
                    mix = new MixTotal(section, batchWeight);
                    mixes[key] = mix;
                }

                mix.Ratio += ratio;
                mix.Uses.Add(new PlannedUse(productId, pieces));
            }

            if (includesPackaging)
            {
                foreach (var (materialId, baseQuantity) in recipe.PackagingPerUnit)
                {
                    Add(purchases, materialId, baseQuantity * units);
                }
            }
        }

        foreach (var (productId, units) in orders)
        {
            Visit(productId, units, includesPackaging: true, []);
        }

        // Each mix in whole batches, its lines and add-ins that many times over.
        var direct = new Dictionary<int, decimal>();
        List<PlannedMix> planned = [];
        foreach (var mix in mixes.Values)
        {
            var batches = RecipeScalingRules.WholeBatches(mix.Ratio);
            foreach (var (ingredientId, grams) in mix.Section.Lines.Concat(mix.Section.AddIns))
            {
                Add(direct, ingredientId, grams * batches);
            }

            planned.Add(new PlannedMix(mix.Section.SubAssemblyId, mix.Section.AddIns, mix.Ratio, batches, mix.BatchWeight, mix.Uses));
        }

        var prepared = OpenIngredients(direct, ingredients, boms, purchases, issues);
        return new ProductionPlan(planned, prepared, purchases, issues);
    }

    /// <summary>
    /// Turns the grams of each ingredient the mixes take into what's bought: made ingredients opened into their BOMs and
    /// linked ones into what they're cut from, each ingredient visited after everything using it (so its total is complete).
    /// Returns the made ingredients' grams.
    /// </summary>
    private static Dictionary<int, decimal> OpenIngredients(
        Dictionary<int, decimal> direct,
        IReadOnlyDictionary<int, PlanIngredient> ingredients,
        ILookup<int, (int ComponentId, decimal Grams)> boms,
        Dictionary<int, decimal> purchases,
        List<PlanIssue> issues)
    {
        IReadOnlyList<int> DependenciesOf(int id) => ingredients.GetValueOrDefault(id) switch
        {
            { Source: RawMaterialSource.Made } => [.. boms[id].Select(l => l.ComponentId)],
            { Source: RawMaterialSource.Linked, LinkedToId: { } linkedToId } => [linkedToId],
            _ => [],
        };

        // Everything the mixes' ingredients come from, however deep.
        var reachable = new HashSet<int>();
        var waiting = new Stack<int>(direct.Keys);
        while (waiting.Count > 0)
        {
            var id = waiting.Pop();
            if (reachable.Add(id))
            {
                foreach (var dependency in DependenciesOf(id))
                {
                    waiting.Push(dependency);
                }
            }
        }

        var dependencies = reachable.ToDictionary(id => id, DependenciesOf);
        var parts = new Dictionary<int, Dictionary<int, decimal>>();
        var prepared = new Dictionary<int, decimal>();
        foreach (var id in IngredientCostGraph.InDependencyOrder(reachable, dependencies).Reverse())
        {
            var total = direct.GetValueOrDefault(id) + (parts.TryGetValue(id, out var cut) ? cut.Values.Max() : 0m);
            if (total <= 0m)
            {
                continue;
            }

            var ingredient = ingredients.GetValueOrDefault(id);
            switch (ingredient)
            {
                case { Source: RawMaterialSource.Made } when boms[id].Any():
                    var lines = boms[id].ToList();
                    var scaled = RecipeScalingRules.ScaleBom(total, [.. lines.Select(l => l.Grams)], ingredient.WeightLossPercent);
                    for (var i = 0; i < lines.Count; i++)
                    {
                        Add(direct, lines[i].ComponentId, scaled[i]);
                    }

                    Add(prepared, id, total);
                    break;

                case { Source: RawMaterialSource.Made }:
                    issues.Add(new PlanIssue(PlanIssueKind.MadeWithoutBom, IngredientId: id));
                    Add(purchases, id, total);
                    break;

                case { Source: RawMaterialSource.Linked, LinkedToId: { } linkedToId, WeightSharePercent: { } share } when share > 0m:
                    if (!parts.TryGetValue(linkedToId, out var ofParent))
                    {
                        ofParent = [];
                        parts[linkedToId] = ofParent;
                    }

                    ofParent[id] = total * 100m / share;
                    break;

                case { Source: RawMaterialSource.Linked }:
                    issues.Add(new PlanIssue(PlanIssueKind.BrokenLink, IngredientId: id));
                    Add(purchases, id, total);
                    break;

                default:
                    Add(purchases, id, total);
                    break;
            }
        }

        return prepared;
    }

    /// <summary>A sub-assembly and its add-ins, in a fixed order: the uses that can share batches.</summary>
    private static string MixKey(PlanSection section) =>
        $"{section.SubAssemblyId}|{string.Join(';', section.AddIns.OrderBy(a => a.IngredientId).ThenBy(a => a.Grams).Select(a => string.Create(CultureInfo.InvariantCulture, $"{a.IngredientId}:{a.Grams}")))}";

    private static void Add(Dictionary<int, decimal> totals, int id, decimal amount) => totals[id] = totals.GetValueOrDefault(id) + amount;

    private sealed class MixTotal(PlanSection section, decimal batchWeight)
    {
        public PlanSection Section => section;

        public decimal BatchWeight => batchWeight;

        public decimal Ratio { get; set; }

        public List<PlannedUse> Uses { get; } = [];
    }
}
