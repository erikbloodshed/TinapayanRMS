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
    /// A Purchased material has no weight loss percent; its CostPerBaseUnit comes from price history
    /// (ApplyLatestPrice below). A Made ingredient needs a weight loss percent — its cost comes from
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
    /// weight, 30% of the cost) ₱0.0386 a gram and egg yolk (30%, 70%) ₱0.21. The share is always of the
    /// linked-to ingredient's true (unbuffered) cost; the part inherits the linked-to ingredient's buffer
    /// percent, and its buffered cost is BufferedCost of the result with that percent.
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
    /// Recomputes CostPerBaseUnit from the latest price history entry: its price divided by the
    /// base units in the unit it was priced in (<paramref name="baseUnitsPerUnit"/>, null for a price
    /// per base unit), and BufferedCostPerBaseUnit from that with the material's buffer. No entry
    /// (<paramref name="latestPrice"/> null) reverts the material to unpriced. Call after every
    /// add/update/delete of a price entry, and after the latest entry's unit changes its multiplier.
    /// </summary>
    public static void ApplyLatestPrice(RawMaterial material, decimal? latestPrice, decimal? baseUnitsPerUnit)
    {
        material.CostPerBaseUnit = latestPrice is { } price
            ? UnitConversionRules.CostPerBaseUnit(price, baseUnitsPerUnit)
            : 0m;
        material.BufferedCostPerBaseUnit = BufferedCost(material.CostPerBaseUnit, material.CostBufferPercent);
    }

    /// <summary>A cost with a buffer on top: ₱0.05 with a 5% buffer is ₱0.0525.</summary>
    public static decimal BufferedCost(decimal cost, decimal bufferPercent) =>
        cost * (1 + bufferPercent / 100m);

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
}
