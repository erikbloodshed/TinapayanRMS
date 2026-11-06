using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// ProductPrintout: a recipe section and the packaging as Print puts them on paper, with the recipe tab's own figures:
/// each line's share of the formula, the batch, and the piece cut from it.
/// </summary>
public class ProductPrintoutTests
{
    public ProductPrintoutTests() => ReactiveUIBootstrapper.EnsureInitialized();

    private static readonly SubAssemblyRole Dough = new() { Id = 1, Name = "Dough", SortOrder = 1 };
    private static readonly SubAssemblyRole Glaze = new() { Id = 2, Name = "Glaze", SortOrder = 2 };

    private static RawMaterial Ingredient(string name, decimal costPerGram) => new() { Name = name, BufferedCostPerBaseUnit = costPerGram };

    [Test]
    public void Each_line_is_a_bakers_percent_of_the_base_which_two_flours_share()
    {
        var usage = new ProductUsageEdit(
            1,
            Dough,
            [
                new SectionLine(Ingredient("Bread Flour", 0.05m), 700m, isFormulaBase: true),
                new SectionLine(Ingredient("Cake Flour", 0.06m), 300m, isFormulaBase: true),
                new SectionLine(Ingredient("Water", 0m), 650m),
                new SectionLine(Ingredient("Salt", 0.02m), 20m),
            ],
            UsageAuthoredField.WeightPerPiece,
            60m,
            linkedName: null);

        var section = PrintedSection.From(usage.ToPriced(), CostBasis.Buffered);

        section.Name.Should().Be("Dough");
        section.Source.Should().BeNull("the section is the product's own");
        section.PercentHeader.Should().Be("Baker's %");
        section.Lines.Should().Equal(
            new PrintedLine("Bread Flour", 700m, 70m, CostPerGram: 0.05m, 35m),
            new PrintedLine("Cake Flour", 300m, 30m, CostPerGram: 0.06m, 18m),
            new PrintedLine("Water", 650m, 65m, CostPerGram: 0m, 0m),
            new PrintedLine("Salt", 20m, 2m, CostPerGram: 0.02m, 0.4m));
        section.AddIns.Should().BeEmpty();
        (section.TotalWeight, section.TotalPercent, section.BatchCost).Should().Be((1670m, (decimal?)167m, 53.4m));
    }

    [Test]
    public void A_linked_sections_add_ins_are_read_against_its_recipes_base_and_it_says_where_its_from()
    {
        var usage = new ProductUsageEdit(
            5,
            Dough,
            [
                new SectionLine(Ingredient("Flour", 0.05m), 1000m, isFormulaBase: true),
                new SectionLine(Ingredient("Water", 0m), 600m),
            ],
            UsageAuthoredField.WeightPerPiece,
            50m,
            linkedName: "Variety Bread Dough",
            addIns: [new SectionLine(Ingredient("Ube Flavouring", 0.5m), 20m)]);

        var section = PrintedSection.From(usage.ToPriced(), CostBasis.Buffered);

        section.Source.Should().Be("Variety Bread Dough, from Sub-Assemblies");
        section.AddIns.Should().Equal(new PrintedLine("Ube Flavouring", 20m, 2m, CostPerGram: 0.5m, 10m));
        (section.TotalWeight, section.TotalPercent, section.BatchCost).Should().Be((1620m, (decimal?)162m, 60m), "the add-ins are part of the batch");
    }

    [Test]
    public void With_no_base_every_line_and_add_in_is_a_share_of_the_whole_batch()
    {
        var usage = new ProductUsageEdit(
            7,
            Glaze,
            [
                new SectionLine(Ingredient("Butter", 0.4m), 300m),
                new SectionLine(Ingredient("Sugar", 0.09m), 200m),
            ],
            UsageAuthoredField.Yield,
            40m,
            linkedName: "Butter Glaze",
            addIns: [new SectionLine(Ingredient("Cream", 0.3m), 500m)]);

        var section = PrintedSection.From(usage.ToPriced(), CostBasis.Buffered);

        section.PercentHeader.Should().Be("Absolute %");
        section.Lines.Select(l => l.Percent).Should().Equal(30m, 20m);
        section.AddIns.Select(l => l.Percent).Should().Equal(50m);
        section.TotalPercent.Should().Be(100m);
    }

    [Test]
    public void A_section_carries_its_batch_and_the_pieces_that_share_it_out()
    {
        var usage = new ProductUsageEdit(
            1,
            Dough,
            [
                new SectionLine(Ingredient("Flour", 0.05m), 1000m, isFormulaBase: true),
                new SectionLine(Ingredient("Water", 0m), 700m),
            ],
            UsageAuthoredField.WeightPerPiece,
            60m,
            linkedName: null);

        var section = PrintedSection.From(usage.ToPriced(), CostBasis.Buffered);

        section.HasPieceSize.Should().BeTrue();
        (section.TotalWeight, section.BatchCost).Should().Be((1700m, 50m));
        section.WeightPerPiece.Should().Be(60m);
        section.Yield.Should().BeApproximately(28.3333m, 0.0001m, "60 g pieces share out the whole 1,700 g batch");
        section.CostPerPiece.Should().Be(usage.CostPerPieceAt(CostBasis.Buffered)).And.BeGreaterThan(0m);
    }

    [Test]
    public void A_section_with_no_piece_size_says_so_and_costs_nothing_a_piece()
    {
        var usage = new ProductUsageEdit(
            1,
            Dough,
            [new SectionLine(Ingredient("Flour", 0.05m), 1000m, isFormulaBase: true)],
            UsageAuthoredField.WeightPerPiece,
            0m,
            linkedName: null);

        var section = PrintedSection.From(usage.ToPriced(), CostBasis.Buffered);

        section.HasPieceSize.Should().BeFalse();
        section.CostPerPiece.Should().Be(0m);
    }

    [Test]
    public void Packaging_lists_each_line_in_its_unit_and_leaves_out_a_row_with_no_material()
    {
        var box = new RawMaterial
        {
            Name = "Gift Box",
            Type = RawMaterialType.Packaging,
            BaseUnit = new UnitOfMeasure { Name = "piece", Abbreviation = "pc" },
            BufferedCostPerBaseUnit = 30m,
        };
        var ribbon = new RawMaterial
        {
            Name = "Ribbon",
            Type = RawMaterialType.Packaging,
            BaseUnit = new UnitOfMeasure { Name = "centimetre", Abbreviation = "cm" },
            BufferedCostPerBaseUnit = 0.05m,
            Units = [new RawMaterialUnit { Id = 7, UnitOfMeasure = new UnitOfMeasure { Name = "roll", Abbreviation = "roll" }, BaseUnitsPerUnit = 1000m }],
        };

        var packaging = PrintedPackaging.From(
            [new PackagingLineRow(box, 1m).ToPriced(), new PackagingLineRow(ribbon, 0.5m, unitId: 7).ToPriced(), new PackagingLineRow().ToPriced()],
            linkedTo: "Linked to Gift Box Set (Sub-Assemblies)");

        packaging.LinkedTo.Should().Be("Linked to Gift Box Set (Sub-Assemblies)");
        packaging.Lines.Should().Equal(
            new PrintedPackagingLine("Gift Box", 1m, "pc", 30m, 30m),
            new PrintedPackagingLine("Ribbon", 0.5m, "roll", 50m, 25m));
        packaging.Total.Should().Be(55m);
    }
}
