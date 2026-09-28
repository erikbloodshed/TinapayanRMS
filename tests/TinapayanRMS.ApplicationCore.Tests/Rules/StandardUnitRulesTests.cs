using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class StandardUnitRulesTests
{
    private static UnitOfMeasure Unit(string name, string abbreviation) => new() { Name = name, Abbreviation = abbreviation };

    [Theory]
    [InlineData("kilogram", "kg", 1000)]
    [InlineData("kilo", "kls", 1000)]
    [InlineData("gram", "g", 1)]
    [InlineData("pound", "lb", 453.5924)]
    [InlineData("ounce", "oz", 28.3495)]
    public void A_mass_unit_of_an_ingredient_is_fixed_in_grams(string name, string abbreviation, decimal grams)
    {
        StandardUnitRules.BaseUnitsPerUnit(Unit(name, abbreviation), RawMaterialType.Ingredient, baseUnit: null).Should().Be(grams);
    }

    [Fact]
    public void A_unit_is_recognised_by_its_abbreviation_whatever_its_case()
    {
        StandardUnitRules.BaseUnitsPerUnit(Unit("kilos", "KG"), RawMaterialType.Ingredient, baseUnit: null).Should().Be(1000m);
    }

    [Theory]
    [InlineData("bag", "bag")]
    [InlineData("tin", "tin")]
    [InlineData("liter", "l")]
    public void A_unit_that_isnt_mass_is_typed_for_an_ingredient(string name, string abbreviation)
    {
        // A litre of vanilla weighs what it weighs: only its own density says how many grams it is.
        StandardUnitRules.BaseUnitsPerUnit(Unit(name, abbreviation), RawMaterialType.Ingredient, baseUnit: null).Should().BeNull();
    }

    [Theory]
    [InlineData("inch", "in", "millimeter", "mm", 25.4)]
    [InlineData("inch", "in", "centimeter", "cm", 2.54)]
    [InlineData("meter", "m", "centimeter", "cm", 100)]
    [InlineData("liter", "l", "milliliter", "ml", 1000)]
    [InlineData("kilogram", "kg", "gram", "g", 1000)]
    public void A_packaging_unit_is_fixed_in_a_base_unit_of_the_same_kind(
        string name, string abbreviation, string baseName, string baseAbbreviation, decimal expected)
    {
        StandardUnitRules.BaseUnitsPerUnit(Unit(name, abbreviation), RawMaterialType.Packaging, Unit(baseName, baseAbbreviation))
            .Should().Be(expected);
    }

    [Fact]
    public void A_kilogram_of_packaging_counted_in_pieces_is_typed()
    {
        // Cello sheet wrappers bought by the kilogram: how many pieces a kilogram holds is the wrapper's own.
        StandardUnitRules.BaseUnitsPerUnit(Unit("kilogram", "kg"), RawMaterialType.Packaging, Unit("piece", "pc")).Should().BeNull();
    }

    [Fact]
    public void A_packaging_material_with_no_base_unit_yet_has_nothing_fixed()
    {
        StandardUnitRules.BaseUnitsPerUnit(Unit("kilogram", "kg"), RawMaterialType.Packaging, baseUnit: null).Should().BeNull();
    }

    [Fact]
    public void A_unit_too_small_for_four_places_of_the_base_unit_is_typed()
    {
        // A milligram is 0.000001 kg, which a multiplier kept to 4 places would store as 0.
        StandardUnitRules.BaseUnitsPerUnit(Unit("milligram", "mg"), RawMaterialType.Packaging, Unit("kilogram", "kg")).Should().BeNull();
    }
}
