using System.Collections.ObjectModel;
using AwesomeAssertions;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// IngredientLinesGrid on a product's section: a line can be another of the recipe's sections, named as one, in grams at
/// its cost per gram, and never part of the formula's base, so it has no Base tick.
/// </summary>
public class IngredientLinesGridTests
{
    private static readonly RawMaterial Flour = new()
    {
        Id = 1,
        Name = "Flour",
        Type = RawMaterialType.Ingredient,
        Source = RawMaterialSource.Purchased,
        CostPerBaseUnit = 0.05m,
        BufferedCostPerBaseUnit = 0.05m,
    };

    private static readonly LineSection Sponge = new(-7, "Sponge", 0.06m, 0.05m);

    [Test]
    public async Task A_section_line_reads_as_one_costs_its_grams_and_has_no_Base_tick()
    {
        await using var context = AppServices.NewContext();
        ObservableCollection<IngredientLineRow> lines = [new(Flour, 1000m, isFormulaBase: true), new(Sponge, 300m)];

        var grid = context.Render<IngredientLinesGrid>(parameters => parameters
            .Add(g => g.Items, lines)
            .Add(g => g.Ingredients, [Flour])
            .Add(g => g.Picks, [LinePick.Of(Sponge), LinePick.Of(Flour)])
            .Add(g => g.ReadOnly, true));

        var rows = grid.FindAll("tbody tr").Where(r => r.QuerySelector(".cell-text") is not null).ToList();
        rows.Select(r => r.QuerySelector(".cell-text")!.TextContent).Should().Equal("Flour", "Sponge (section)");
        rows[1].QuerySelectorAll(".cell-number").Select(c => c.TextContent).Should().Equal("0.0600", "18.00");
        rows[0].QuerySelector("input[type=checkbox]").Should().NotBeNull();
        rows[1].QuerySelector("input[type=checkbox]").Should().BeNull("a section is never part of the formula's base");
    }

    [Test]
    public void Picking_a_section_makes_the_line_one_and_takes_it_out_of_the_base()
    {
        var row = new IngredientLineRow(Flour, 1000m, isFormulaBase: true);

        row.Pick(LinePick.Of(Sponge));

        (row.IsSection, row.IngredientId, row.Ingredient, row.IsFormulaBase).Should().Be((true, (int?)null, (RawMaterial?)null, false));
        row.Picked.Should().Be(LinePick.Of(Sponge));
        row.Cost.Should().Be(60m, "1,000 g at the sponge's 0.06 a gram");

        row.Pick(new LinePick("Flour", Flour, null));
        (row.IsSection, row.IngredientId).Should().Be((false, (int?)1));
    }
}
