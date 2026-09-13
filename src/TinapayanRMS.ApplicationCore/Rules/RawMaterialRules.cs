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
            throw new InvalidOperationException("Only an ingredient can be made; packaging is always purchased.");
        }
    }

    /// <summary>
    /// A Purchased material has no weight loss percent; its CostPerBaseUnit comes from price history
    /// (ApplyLatestPrice below). A Made ingredient needs a weight loss percent — its cost comes from
    /// a BOM instead (IngredientBomRules). Either kind can have alternative units (RawMaterialUnit);
    /// only a purchased one is ever priced in them.
    /// </summary>
    public static void EnsureFieldsMatchSource(RawMaterial material)
    {
        if (material.Source == RawMaterialSource.Purchased)
        {
            if (material.WeightLossPercent is not null)
            {
                throw new InvalidOperationException("A purchased material has no weight loss percent.");
            }
        }
        else
        {
            if (material.WeightLossPercent is not { } weightLossPercent)
            {
                throw new InvalidOperationException("A made ingredient needs a weight loss percent.");
            }

            if (weightLossPercent is < 0 or >= 100)
            {
                throw new InvalidOperationException("Weight loss percent must be at least 0 and less than 100.");
            }
        }
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
    /// per base unit). No entry (<paramref name="latestPrice"/> null) reverts the material to
    /// unpriced. Call after every add/update/delete of a price entry, and after the latest entry's
    /// unit changes its multiplier.
    /// </summary>
    public static void ApplyLatestPrice(RawMaterial material, decimal? latestPrice, decimal? baseUnitsPerUnit)
    {
        material.CostPerBaseUnit = latestPrice is { } price
            ? UnitConversionRules.CostPerBaseUnit(price, baseUnitsPerUnit)
            : 0m;
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
