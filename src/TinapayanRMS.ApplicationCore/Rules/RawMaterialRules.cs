using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class RawMaterialRules
{
    /// <summary>
    /// An ingredient's base unit is always the gram, so it has no BaseUnitId: recipes, BOMs and
    /// sub-assembly weights all add up grams. A packaging material picks its own base unit, and is
    /// always purchased — only an ingredient can be made from a BOM, whose math is in grams.
    /// </summary>
    public static void EnsureFieldsMatchType(RawMaterial material)
    {
        if (material.Type == RawMaterialType.Ingredient)
        {
            if (material.BaseUnitId is not null)
            {
                throw new InvalidOperationException("An ingredient's base unit is always the gram.");
            }

            return;
        }

        if (material.BaseUnitId is not > 0)
        {
            throw new InvalidOperationException("A packaging material needs a base unit.");
        }

        if (material.Source != RawMaterialSource.Purchased)
        {
            throw new InvalidOperationException("Only an ingredient can be made or linked; packaging is always purchased.");
        }
    }

    /// <summary>
    /// A Purchased material has no weight loss percent; its CostPerBaseUnit comes from its brand prices
    /// (ApplyBrandPrices below). A Made ingredient needs a weight loss percent — its cost comes from
    /// a BOM instead (IngredientBomRules). A Linked ingredient needs the ingredient it's linked to and
    /// both shares — its cost is a share of that one's (LinkedCost). Only a linked one has link fields.
    /// Every kind can have alternative units (RawMaterialUnit); only a purchased one is ever priced in them.
    /// </summary>
    public static void EnsureFieldsMatchSource(RawMaterial material)
    {
        if (material.Source != RawMaterialSource.Linked
            && (material.LinkedToId is not null || material.WeightSharePercent is not null || material.CostSharePercent is not null))
        {
            throw new InvalidOperationException("Only a linked ingredient is linked to another.");
        }

        switch (material.Source)
        {
            case RawMaterialSource.Purchased:
                if (material.WeightLossPercent is not null)
                {
                    throw new InvalidOperationException("A purchased material has no weight loss percent.");
                }

                break;

            case RawMaterialSource.Made:
                if (material.WeightLossPercent is not { } weightLossPercent)
                {
                    throw new InvalidOperationException("A made ingredient needs a weight loss percent.");
                }

                if (weightLossPercent is < 0 or >= 100)
                {
                    throw new InvalidOperationException("Weight loss percent must be at least 0 and less than 100.");
                }

                break;

            case RawMaterialSource.Linked:
                if (material.WeightLossPercent is not null)
                {
                    throw new InvalidOperationException("A linked ingredient has no weight loss percent.");
                }

                if (material.LinkedToId is not > 0)
                {
                    throw new InvalidOperationException("Pick the ingredient it's linked to.");
                }

                EnsureValidShare(material.WeightSharePercent, "Weight share");
                EnsureValidShare(material.CostSharePercent, "Cost share");
                break;

            default:
                throw new InvalidOperationException($"Unknown source {material.Source}.");
        }
    }

    private static void EnsureValidShare(decimal? sharePercent, string label)
    {
        if (sharePercent is not (> 0 and <= 100))
        {
            throw new InvalidOperationException($"{label} must be above 0 and at most 100 percent.");
        }
    }

    /// <summary>
    /// A linked part's cost per base unit: the linked-to ingredient's, times the share of its cost the part
    /// carries, over the share of its weight the part is. An egg at ₱0.09 a gram gives egg white (70% of the
    /// weight, 30% of the cost) ₱0.0386 a gram and egg yolk (30%, 70%) ₱0.21. The part's true cost is the
    /// share of the linked-to ingredient's true cost, and its buffered cost the same share of the linked-to
    /// ingredient's (fixed) buffered cost.
    /// </summary>
    public static decimal LinkedCost(decimal linkedToCost, decimal weightSharePercent, decimal costSharePercent) =>
        linkedToCost * costSharePercent / weightSharePercent;

    /// <summary>A linked ingredient is a part of another ingredient: never of itself, and never of packaging.</summary>
    public static void EnsureValidLinkTarget(int id, int linkedToId, RawMaterialType linkedToType, string linkedToName)
    {
        if (id != 0 && id == linkedToId)
        {
            throw new InvalidOperationException("An ingredient can't be linked to itself.");
        }

        if (linkedToType != RawMaterialType.Ingredient)
        {
            throw new InvalidOperationException($"'{linkedToName}' is packaging, and an ingredient can only be linked to another ingredient.");
        }
    }

    /// <summary>
    /// The parts linked to one ingredient can't add up to more than all of it: together they take at most 100%
    /// of its weight and at most 100% of its cost. Less is fine — an egg's shell is neither white nor yolk.
    /// <paramref name="otherParts"/> are the ingredient's other linked parts, without the one being saved.
    /// </summary>
    public static void EnsureLinkSharesFit(
        string name,
        string linkedToName,
        decimal weightSharePercent,
        decimal costSharePercent,
        IReadOnlyList<(string Name, decimal WeightSharePercent, decimal CostSharePercent)> otherParts)
    {
        EnsureShareFits(name, linkedToName, "weight", weightSharePercent, [.. otherParts.Select(p => (p.Name, p.WeightSharePercent))]);
        EnsureShareFits(name, linkedToName, "cost", costSharePercent, [.. otherParts.Select(p => (p.Name, p.CostSharePercent))]);
    }

    private static void EnsureShareFits(
        string name, string linkedToName, string what, decimal sharePercent, IReadOnlyList<(string Name, decimal SharePercent)> otherParts)
    {
        var taken = otherParts.Sum(p => p.SharePercent);
        if (taken + sharePercent <= 100m)
        {
            return;
        }

        var takers = string.Join(", ", otherParts.Select(p => $"{p.Name} ({p.SharePercent:0.##}%)"));
        var verb = otherParts.Count == 1 ? "takes" : "take";
        throw new InvalidOperationException(
            $"{takers} already {verb} {taken:0.##}% of {linkedToName}'s {what}, so {name} can take at most {100m - taken:0.##}%.");
    }

    /// <summary>A material's type is set at creation and can never change afterwards: its recipe or packaging lines depend on it.</summary>
    public static void EnsureTypeUnchanged(RawMaterialType original, RawMaterialType updated)
    {
        if (original != updated)
        {
            throw new InvalidOperationException("A raw material's type can't change after it's created.");
        }
    }

    /// <summary>A material's source is set at creation and can never change afterwards.</summary>
    public static void EnsureSourceUnchanged(RawMaterialSource original, RawMaterialSource updated)
    {
        if (original != updated)
        {
            throw new InvalidOperationException("A raw material's source can't change after it's created.");
        }
    }

    /// <summary>Recipes — sub-assembly lines and a made ingredient's BOM — are made of ingredients only, since they add up grams.</summary>
    public static void EnsureUsableInRecipes(RawMaterialType type, string name)
    {
        if (type != RawMaterialType.Ingredient)
        {
            throw new InvalidOperationException($"'{name}' is packaging, and only ingredients can go in a recipe.");
        }
    }

    /// <summary>A product's packaging lines take packaging materials only.</summary>
    public static void EnsureUsableAsPackaging(RawMaterialType type, string name)
    {
        if (type != RawMaterialType.Packaging)
        {
            throw new InvalidOperationException($"'{name}' is an ingredient, and only packaging can go on a packaging line.");
        }
    }

    /// <summary>
    /// Recomputes CostPerBaseUnit from the brand prices: the dearest of them per base unit, each price
    /// divided by the base units in the unit it's for (null for a price per base unit), so the material
    /// is costed at the most it's bought for. BufferedCostPerBaseUnit is a fixed amount the prices don't
    /// move: it's only worked out here (the cost with the material's buffer on top) the first time the
    /// material is priced, while it's still 0, and it goes back to 0 when no prices are left. After that
    /// it changes only when it's typed or recalculated on the screen. Call after the brand prices change,
    /// and after one of their units changes its multiplier. Returns the index of the brand price the cost
    /// came from.
    /// </summary>
    public static int? ApplyBrandPrices(RawMaterial material, IReadOnlyList<(decimal Price, decimal? BaseUnitsPerUnit)> brandPrices)
    {
        var dearest = FindDearestBrandPrice(brandPrices);
        material.CostPerBaseUnit = dearest is { } index
            ? UnitConversionRules.CostPerBaseUnit(brandPrices[index].Price, brandPrices[index].BaseUnitsPerUnit)
            : 0m;
        if (dearest is null)
        {
            material.BufferedCostPerBaseUnit = 0m;
        }
        else if (material.BufferedCostPerBaseUnit == 0m)
        {
            material.BufferedCostPerBaseUnit = BufferedCost(material.CostPerBaseUnit, material.CostBufferPercent);
        }

        return dearest;
    }

    /// <summary>
    /// The index of the brand price that costs the most per base unit (the first, on a tie), or null for
    /// none. ₱930 a 25 kg sack (₱0.0372/g) is dearer than ₱35 a kilo pack (₱0.035/g).
    /// </summary>
    public static int? FindDearestBrandPrice(IReadOnlyList<(decimal Price, decimal? BaseUnitsPerUnit)> brandPrices)
    {
        int? dearest = null;
        var highest = 0m;
        for (var i = 0; i < brandPrices.Count; i++)
        {
            var cost = UnitConversionRules.CostPerBaseUnit(brandPrices[i].Price, brandPrices[i].BaseUnitsPerUnit);
            if (dearest is null || cost > highest)
            {
                dearest = i;
                highest = cost;
            }
        }

        return dearest;
    }

    /// <summary>A brand name as it's saved: trimmed, and null when there's none.</summary>
    public static string? NormalizeBrand(string? brand) =>
        string.IsNullOrWhiteSpace(brand) ? null : brand.Trim();

    /// <summary>
    /// A brand is priced once per unit: the same brand can be bought by the sack and by the kilo, but two
    /// prices for the same brand in the same unit would leave it unclear which is current. Brands match
    /// whatever their case, and an unnamed brand is a brand too. Units are compared as given (unit of
    /// measure ids, or unit ids, null for the base unit).
    /// </summary>
    public static void EnsureDistinctBrandPrices(IEnumerable<(string? Brand, int? UnitKey)> brandPrices)
    {
        var duplicate = brandPrices
            .GroupBy(p => (Brand: NormalizeBrand(p.Brand)?.ToUpperInvariant(), p.UnitKey))
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            var brand = NormalizeBrand(duplicate.First().Brand);
            throw new InvalidOperationException(brand is null
                ? "Two prices without a brand are in the same unit. Name their brands, or remove one."
                : $"'{brand}' is priced twice in the same unit. Remove one.");
        }
    }

    /// <summary>A brand price is always positive.</summary>
    public static void EnsureValidBrandPrice(decimal price)
    {
        if (price <= 0)
        {
            throw new InvalidOperationException("A brand's purchase price must be positive.");
        }
    }

    /// <summary>What one base unit of <paramref name="material"/> costs at <paramref name="basis"/>: its buffered cost, or its true cost.</summary>
    public static decimal CostPerBaseUnitAt(RawMaterial material, CostBasis basis) =>
        basis == CostBasis.Unbuffered ? material.CostPerBaseUnit : material.BufferedCostPerBaseUnit;

    /// <summary>A cost with a buffer on top: ₱0.05 with a 5% buffer is ₱0.0525.</summary>
    public static decimal BufferedCost(decimal cost, decimal bufferPercent) =>
        cost * (1 + bufferPercent / 100m);

    /// <summary>
    /// The buffer percent a material is stored with. A made ingredient's is always 0 (2026-09-21, at the user's
    /// request): its components' buffers are already inside its BOM rollup, so it adds none of its own, and since
    /// it has no buffer box on the screen a stale stored percent is normalised away by any save rather than
    /// rejected — which would leave it unsaveable. A linked ingredient takes the one it's linked to
    /// (<paramref name="inheritedBuffer"/>); anything else keeps what the form sent.
    /// </summary>
    public static decimal StoredCostBufferPercent(RawMaterialSource source, decimal? inheritedBuffer, decimal sent) =>
        source == RawMaterialSource.Made ? 0m : inheritedBuffer ?? sent;

    /// <summary>
    /// BufferedCost the other way round: the buffer that takes <paramref name="cost"/> to
    /// <paramref name="bufferedCost"/>. ₱930 buffered to ₱976.50 is a 5% buffer. The cost must be above 0.
    /// </summary>
    public static decimal CostBufferPercentOf(decimal cost, decimal bufferedCost) =>
        (bufferedCost / cost - 1) * 100m;

    /// <summary>A cost buffer is at least 0% (none) and at most 100% (double the cost).</summary>
    public static void EnsureValidCostBuffer(decimal bufferPercent)
    {
        if (bufferPercent is < 0 or > 100)
        {
            throw new InvalidOperationException("Cost buffer must be at least 0 and at most 100 percent.");
        }
    }

    /// <summary>A purchased material's fixed buffered cost can't be negative. It may be below the cost, once prices have risen past it.</summary>
    public static void EnsureValidBufferedCost(decimal bufferedCost)
    {
        if (bufferedCost < 0)
        {
            throw new InvalidOperationException("Buffered cost can't be negative.");
        }
    }

    /// <summary>
    /// A packaging material's base unit is the one unit it has without an alternative-unit row, so it
    /// can't also be one. An ingredient (<paramref name="baseUnitId"/> null) has the gram, which isn't
    /// a unit of measure row at all.
    /// </summary>
    public static void EnsureNotTheBaseUnit(int? baseUnitId, int unitOfMeasureId)
    {
        if (baseUnitId == unitOfMeasureId)
        {
            throw new InvalidOperationException("That's already the material's base unit.");
        }
    }

    /// <summary>A material has each unit of measure at most once: two of its alternative units can't share one.</summary>
    public static void EnsureUnitNotAlreadyAdded(bool alreadyAdded)
    {
        if (alreadyAdded)
        {
            throw new InvalidOperationException("This material already has that unit.");
        }
    }

    /// <summary>
    /// A packaging material's alternative units, prices and packaging lines are all measured against
    /// its base unit, so it can only change while it has none of them.
    /// </summary>
    public static void EnsureBaseUnitCanChange(bool hasUnits, bool hasPrices, bool hasLines)
    {
        if (hasUnits || hasPrices || hasLines)
        {
            throw new InvalidOperationException(
                "The base unit can't change once the material has alternative units, prices or packaging lines, which are all measured in it.");
        }
    }

    /// <summary>
    /// A material goes in a category of its own type: an ingredient in an ingredient category (Flour, Dairy), packaging
    /// in a packaging one (Boxes, Labels).
    /// </summary>
    public static void EnsureCategoryMatchesType(RawMaterialType materialType, RawMaterialType categoryType, string categoryName)
    {
        if (materialType != categoryType)
        {
            throw new InvalidOperationException(
                $"{categoryName} is a{(categoryType == RawMaterialType.Ingredient ? "n ingredient" : " packaging")} category. "
                + $"Put this {(materialType == RawMaterialType.Ingredient ? "ingredient" : "packaging material")} in a category for {TypePlural(materialType)}.");
        }
    }

    /// <summary>A category's type can change only while no material of its present type is in it, since each would be left in a category of the other type.</summary>
    public static void EnsureCategoryTypeCanChange(string categoryName, int materialsInIt)
    {
        if (materialsInIt > 0)
        {
            throw new InvalidOperationException(
                $"{categoryName} has {materialsInIt} material{(materialsInIt == 1 ? "" : "s")} in it, so its type can't change. "
                + $"Move {(materialsInIt == 1 ? "it" : "them")} to another category first.");
        }
    }

    private static string TypePlural(RawMaterialType type) => type == RawMaterialType.Ingredient ? "ingredients" : "packaging";
}
