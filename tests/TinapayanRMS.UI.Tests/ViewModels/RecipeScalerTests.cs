using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// RecipeScaler: the Scaling tab's tree, a recipe scaled to a number of sellable units. Each section goes in whole
/// batches, a made ingredient opens into its BOM, a composite's products are scaled to their share, and the packaging
/// goes by the unit.
/// </summary>
public class RecipeScalerTests
{
    public RecipeScalerTests() => ReactiveUIBootstrapper.EnsureInitialized();

    private static readonly SubAssemblyRole Dough = new() { Id = 1, Name = "Dough", SortOrder = 1 };
    private static readonly SubAssemblyRole Filling = new() { Id = 2, Name = "Filling", SortOrder = 2 };
    private static readonly UnitOfMeasure Piece = new() { Id = 1, Name = "piece", Abbreviation = "pc" };

    private static int _nextId = 100;

    private static RawMaterial Ingredient(string name, decimal costPerGram, decimal? unbufferedCostPerGram = null) => new()
    {
        Id = _nextId++,
        Name = name,
        Type = RawMaterialType.Ingredient,
        Source = RawMaterialSource.Purchased,
        BufferedCostPerBaseUnit = costPerGram,
        CostPerBaseUnit = unbufferedCostPerGram ?? costPerGram,
    };

    private static RawMaterial Made(string name, decimal costPerGram, decimal weightLossPercent = 0m)
    {
        var made = Ingredient(name, costPerGram);
        made.Source = RawMaterialSource.Made;
        made.WeightLossPercent = weightLossPercent;
        return made;
    }

    private static RawMaterial PackagingMaterial(string name, decimal costPerPiece) => new()
    {
        Id = _nextId++,
        Name = name,
        Type = RawMaterialType.Packaging,
        BaseUnit = Piece,
        BaseUnitId = Piece.Id,
        BufferedCostPerBaseUnit = costPerPiece,
        CostPerBaseUnit = costPerPiece,
    };

    /// <summary>Ensaymada's dough: 600 g of flour at ₱0.05 and 400 g of water, 1,000 g in all, cut 10 g a piece, so 100 a batch.</summary>
    private static ProductUsageEdit EnsaymadaDough(RawMaterial flour, RawMaterial water) => new(
        1,
        Dough,
        [new SubAssemblyLineEdit(flour, 600m), new SubAssemblyLineEdit(water, 400m)],
        UsageAuthoredField.WeightPerPiece,
        10m,
        linkedName: null);

    private static RecipeScaler Scaler(
        IReadOnlyList<IngredientBomLine>? boms = null,
        IReadOnlyList<RawMaterial>? ingredients = null,
        IReadOnlyDictionary<int, SavedProduct>? products = null,
        CostBasis basis = CostBasis.Buffered) =>
        new(
            (boms ?? []).ToLookup(l => l.IngredientId),
            (ingredients ?? []).ToDictionary(i => i.Id),
            id => products?.GetValueOrDefault(id),
            basis);

    private static ScalingRecipe Recipe(
        IReadOnlyList<ProductUsageEdit> sections,
        IReadOnlyList<PackagingLineRow>? packaging = null,
        int quantityRequired = 1,
        IReadOnlyList<(Product, int)>? components = null,
        bool includesComponentPackaging = false) =>
        new(sections, packaging ?? [], components ?? [], quantityRequired, includesComponentPackaging);

    [Fact]
    public void A_box_of_8_scales_its_sections_in_whole_batches_and_its_packaging_by_the_box()
    {
        // 20 boxes of 8 are 160 pieces: 1.6 batches of a 100-piece dough, so 2, and 40 pieces to spare.
        var flour = Ingredient("Flour", 0.05m);
        var water = Ingredient("Water", 0m);
        var bag = PackagingMaterial("Bag", 0.25m);
        var box = PackagingMaterial("Box", 12m);

        var rows = Scaler().Scale(
            Recipe([EnsaymadaDough(flour, water)], [new PackagingLineRow(bag, 8m), new PackagingLineRow(box, 1m)], quantityRequired: 8),
            20m,
            productId: 1);

        rows.Should().HaveCount(2);
        var dough = rows[0];
        (dough.Name, dough.Detail, dough.AmountText, dough.Cost, dough.IsGroup)
            .Should().Be(("Dough", "2 batches · 200 pcs (40 extra)", "2,000 g", 60m, true));
        dough.Children.Should().Equal(
            ScalingRow.Line("Flour", string.Empty, "1,200 g", 60m),
            ScalingRow.Line("Water", string.Empty, "800 g", 0m));

        var packaging = rows[1];
        (packaging.Name, packaging.Cost).Should().Be(("Packaging", 280m));
        packaging.Children.Should().Equal(
            ScalingRow.Line("Bag", string.Empty, "160 pc", 40m),
            ScalingRow.Line("Box", string.Empty, "20 pc", 240m));
    }

    [Fact]
    public void An_exact_fit_makes_no_extra_pieces()
    {
        var rows = Scaler().Scale(Recipe([EnsaymadaDough(Ingredient("Flour", 0.05m), Ingredient("Water", 0m))]), 300m, productId: null);

        rows.Should().ContainSingle().Which.Detail.Should().Be("3 batches · 300 pcs");
    }

    [Fact]
    public void A_linked_sections_add_ins_go_in_every_batch_after_its_lines()
    {
        var section = new ProductUsageEdit(
            5,
            Dough,
            [new SubAssemblyLineEdit(Ingredient("Flour", 0.05m), 980m)],
            UsageAuthoredField.Yield,
            100m,
            linkedName: "Variety Bread Dough",
            addIns: [new SubAssemblyLineEdit(Ingredient("Ube Flavouring", 0.5m), 20m)]);

        var rows = Scaler().Scale(Recipe([section]), 150m, productId: null);

        var dough = rows.Should().ContainSingle().Subject;
        dough.AmountText.Should().Be("2,000 g");
        dough.Children.Should().Equal(
            ScalingRow.Line("Flour", string.Empty, "1,960 g", 98m),
            ScalingRow.Line("Ube Flavouring", "add-in", "40 g", 20m));
    }

    [Fact]
    public void A_made_ingredient_opens_into_its_bom_scaled_to_the_grams_it_takes_to_any_depth()
    {
        // Caramel is half syrup, half cream; the syrup is 1,000 g of sugar and 500 g of water that lose 20% to make 1,200 g.
        var sugar = Ingredient("Sugar", 0.09m);
        var water = Ingredient("Water", 0m);
        var cream = Ingredient("Cream", 0.2m);
        var syrup = Made("Syrup", 0.075m, weightLossPercent: 20m);
        var caramel = Made("Caramel", 0.1375m);
        IngredientBomLine[] boms =
        [
            new() { IngredientId = caramel.Id, ComponentIngredientId = syrup.Id, Grams = 500m },
            new() { IngredientId = caramel.Id, ComponentIngredientId = cream.Id, Grams = 500m },
            new() { IngredientId = syrup.Id, ComponentIngredientId = sugar.Id, Grams = 1000m },
            new() { IngredientId = syrup.Id, ComponentIngredientId = water.Id, Grams = 500m },
        ];
        var section = new ProductUsageEdit(
            2, Filling, [new SubAssemblyLineEdit(caramel, 100m)], UsageAuthoredField.Yield, 10m, linkedName: null);

        var rows = Scaler(boms, [sugar, water, cream, syrup, caramel]).Scale(Recipe([section]), 10m, productId: null);

        var caramelRow = rows.Should().ContainSingle().Subject.Children.Should().ContainSingle().Subject;
        (caramelRow.Name, caramelRow.AmountText, caramelRow.Cost).Should().Be(("Caramel", "100 g", 13.75m));
        caramelRow.Children.Select(r => (r.Name, r.AmountText)).Should().Equal(("Syrup", "50 g"), ("Cream", "50 g"));
        caramelRow.Children[0].Children.Select(r => (r.Name, r.AmountText)).Should().Equal(("Sugar", "41.67 g"), ("Water", "20.83 g"));
        caramelRow.Children[1].Children.Should().BeEmpty("a purchased ingredient has nothing under it");
    }

    [Fact]
    public void A_made_ingredient_whose_bom_holds_itself_opens_only_once()
    {
        var loop = Made("Starter", 0.01m);
        IngredientBomLine[] boms = [new() { IngredientId = loop.Id, ComponentIngredientId = loop.Id, Grams = 100m }];
        var section = new ProductUsageEdit(
            3, Dough, [new SubAssemblyLineEdit(loop, 100m)], UsageAuthoredField.Yield, 10m, linkedName: null);

        var rows = Scaler(boms, [loop]).Scale(Recipe([section]), 10m, productId: null);

        var starter = rows[0].Children.Should().ContainSingle().Subject;
        starter.Children.Should().ContainSingle().Which.Children.Should().BeEmpty();
    }

    [Fact]
    public void Costs_follow_the_cost_basis()
    {
        var flour = Ingredient("Flour", 0.05m, unbufferedCostPerGram: 0.04m);

        var rows = Scaler(basis: CostBasis.Unbuffered).Scale(Recipe([EnsaymadaDough(flour, Ingredient("Water", 0m))]), 100m, productId: null);

        rows[0].Cost.Should().Be(24m, "600 g of flour at its true ₱0.04");
    }

    [Fact]
    public void A_section_without_a_piece_size_has_nothing_to_scale()
    {
        var section = new ProductUsageEdit(
            4, Dough, [new SubAssemblyLineEdit(Ingredient("Flour", 0.05m), 1000m)], UsageAuthoredField.WeightPerPiece, 0m, linkedName: null);

        var row = Scaler().Scale(Recipe([section]), 10m, productId: null).Should().ContainSingle().Subject;

        (row.Detail, row.AmountText, row.Cost).Should().Be(("No piece size yet", string.Empty, 0m));
        row.Children.Should().BeEmpty();
    }

    [Fact]
    public void A_composite_scales_each_product_to_its_share_with_packaging_only_where_it_takes_its_products()
    {
        // A Pasalubong Box of 6 Ensaymada and a Party Box, which holds 2 Cheese Rolls; the Pasalubong Box takes its
        // products' packaging and the Party Box doesn't. 5 Pasalubong Boxes: 30 Ensaymada and 10 Cheese Rolls.
        var flour = Ingredient("Flour", 0.05m);
        var water = Ingredient("Water", 0m);
        var wrapper = PackagingMaterial("Wrapper", 1m);
        var partyBoxCarton = PackagingMaterial("Party Box Carton", 20m);
        var bigBox = PackagingMaterial("Big Box", 50m);

        var ensaymada = new Product { Id = 1, Name = "Ensaymada" };
        var cheeseRoll = new Product { Id = 2, Name = "Cheese Roll" };
        var partyBox = new Product { Id = 3, Name = "Party Box", IsComposite = true };
        var cheeseDough = new ProductUsageEdit(
            9, Dough, [new SubAssemblyLineEdit(flour, 500m)], UsageAuthoredField.Yield, 4m, linkedName: null);
        Dictionary<int, SavedProduct> products = new()
        {
            [1] = new SavedProduct(ensaymada, [EnsaymadaDough(flour, water)], [new PackagingLineRow(wrapper, 1m)], true, null, ProductCostSummary.Empty),
            [2] = new SavedProduct(cheeseRoll, [cheeseDough], [new PackagingLineRow(wrapper, 1m)], true, null, ProductCostSummary.Empty),
            [3] = new SavedProduct(partyBox, [], [new PackagingLineRow(partyBoxCarton, 1m)], true, null, ProductCostSummary.Empty)
            {
                Components = [new SavedComponent(new ProductComponent { ComponentProductId = 2, ComponentProduct = cheeseRoll, Quantity = 2 }, new ComponentCosts(0m, 0m, 0m, 0m))],
            },
        };

        var rows = Scaler(products: products).Scale(
            Recipe([], [new PackagingLineRow(bigBox, 1m)], components: [(ensaymada, 6), (partyBox, 1)], includesComponentPackaging: true),
            5m,
            productId: 10);

        rows.Select(r => (r.Name, r.Detail)).Should().Equal(("Ensaymada × 6", "30 pcs"), ("Party Box × 1", "5 pcs"), ("Packaging", string.Empty));

        rows[0].Children.Select(r => r.Name).Should().Equal("Dough", "Packaging");
        rows[0].Children[0].Detail.Should().Be("1 batch · 100 pcs (70 extra)");
        rows[0].Children[1].Children.Should().Equal(ScalingRow.Line("Wrapper", string.Empty, "30 pc", 30m));

        var cheeseRolls = rows[1].Children[0];
        (cheeseRolls.Name, cheeseRolls.Detail).Should().Be(("Cheese Roll × 2", "10 pcs"));
        cheeseRolls.Children.Select(r => r.Name).Should().Equal(["Dough"], "the Party Box doesn't take its products' packaging");
        rows[1].Children.Select(r => r.Name).Should().Equal(["Cheese Roll × 2", "Packaging"], "the Pasalubong Box takes the Party Box's own");

        rows[2].Children.Should().Equal(ScalingRow.Line("Big Box", string.Empty, "5 pc", 250m));
        rows.Sum(r => r.Cost).Should().Be(rows[0].Cost + rows[1].Cost + 250m);
    }

    [Fact]
    public void A_product_in_a_composite_not_read_yet_says_so()
    {
        var ensaymada = new Product { Id = 1, Name = "Ensaymada" };

        var rows = Scaler().Scale(Recipe([], components: [(ensaymada, 6)]), 5m, productId: 10);

        var row = rows.Should().ContainSingle().Subject;
        (row.Name, row.Detail, row.Cost).Should().Be(("Ensaymada × 6", RecipeScaler.ReadingText, 0m));
    }
}
