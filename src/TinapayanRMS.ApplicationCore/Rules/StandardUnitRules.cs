using System.Collections.Frozen;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// Units whose size is fixed by definition, so a material's multiplier for one is never typed: a kilogram is 1,000 g, an
/// inch 25.4 mm. Units of Measure are the shop owner's own rows, with no seeded ids, so a standard unit is recognised by
/// its name or abbreviation, whatever the case. A multiplier is fixed only between two units of the same kind, the unit
/// and the material's base unit: a kilogram of an ingredient is 1,000 g, but a kilogram of wrappers counted in pieces is
/// however many pieces it holds, which stays typed.
/// </summary>
public static class StandardUnitRules
{
    private enum Kind
    {
        Mass,
        Length,
        Volume,
    }

    /// <summary>A standard unit's size in its kind's smallest reference unit: grams, millimetres or millilitres.</summary>
    private sealed record StandardUnit(Kind Kind, decimal Size);

    private static readonly FrozenDictionary<string, StandardUnit> ByName = Build(
        (new StandardUnit(Kind.Mass, 0.001m), ["milligram", "milligramme", "mg"]),
        (new StandardUnit(Kind.Mass, 1m), ["gram", "gramme", "g"]),
        (new StandardUnit(Kind.Mass, 1000m), ["kilogram", "kilogramme", "kilo", "kg"]),
        (new StandardUnit(Kind.Mass, 28.349523125m), ["ounce", "oz"]),
        (new StandardUnit(Kind.Mass, 453.59237m), ["pound", "lb", "lbs"]),
        (new StandardUnit(Kind.Length, 1m), ["millimeter", "millimetre", "mm"]),
        (new StandardUnit(Kind.Length, 10m), ["centimeter", "centimetre", "cm"]),
        (new StandardUnit(Kind.Length, 1000m), ["meter", "metre", "m"]),
        (new StandardUnit(Kind.Length, 25.4m), ["inch", "in"]),
        (new StandardUnit(Kind.Length, 304.8m), ["foot", "ft"]),
        (new StandardUnit(Kind.Volume, 1m), ["milliliter", "millilitre", "ml"]),
        (new StandardUnit(Kind.Volume, 1000m), ["liter", "litre", "l"]));

    /// <summary>An ingredient's base unit, the gram, which has no Units of Measure row of its own.</summary>
    private static readonly StandardUnit Gram = ByName["gram"];

    /// <summary>
    /// How many of the material's base units one <paramref name="unit"/> holds when both are standard units of the same
    /// kind: 1,000 for a kilogram of an ingredient (whose base unit is always the gram), 25.4 for an inch of ribbon
    /// measured in millimetres. Null when the multiplier is the material's own to type: a bag, or a kilogram of wrappers
    /// counted in pieces, or a packaging material with no base unit picked yet. Rounded to the 4 places a multiplier is
    /// stored with (an ounce is 28.3495 g), so a saved one reads back equal to it.
    /// </summary>
    public static decimal? BaseUnitsPerUnit(UnitOfMeasure unit, RawMaterialType type, UnitOfMeasure? baseUnit)
    {
        var standard = Find(unit);
        var standardBase = type == RawMaterialType.Ingredient ? Gram : baseUnit is null ? null : Find(baseUnit);

        if (standard is null || standardBase is null || standard.Kind != standardBase.Kind)
        {
            return null;
        }

        // A unit too small to show in 4 places of the base unit (a milligram of a kilogram) is left to type.
        var multiplier = Math.Round(standard.Size / standardBase.Size, 4);
        return multiplier > 0 ? multiplier : null;
    }

    /// <summary><see cref="ByName"/> looked up by a trimmed span of a name, so a lookup doesn't allocate the trimmed copy.</summary>
    private static readonly FrozenDictionary<string, StandardUnit>.AlternateLookup<ReadOnlySpan<char>> BySpan =
        ByName.GetAlternateLookup<ReadOnlySpan<char>>();

    private static StandardUnit? Find(UnitOfMeasure unit) =>
        BySpan.TryGetValue(unit.Name.AsSpan().Trim(), out var byName) ? byName
        : BySpan.TryGetValue(unit.Abbreviation.AsSpan().Trim(), out var byAbbreviation) ? byAbbreviation
        : null;

    private static FrozenDictionary<string, StandardUnit> Build(params (StandardUnit Unit, string[] Names)[] units) =>
        units.SelectMany(u => u.Names.Select(name => (name, u.Unit)))
            .ToFrozenDictionary(p => p.name, p => p.Unit, StringComparer.OrdinalIgnoreCase);
}
