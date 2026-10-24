using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class ProductionPlanRulesTests
{
    private const int Flour = 1, Sugar = 2, Ube = 3, Egg = 4, EggWhite = 5, EggYolk = 6, Syrup = 7, Glaze = 8, Box = 50, Bag = 51;
    private const int Dough = 100, Filling = 101;

    private static readonly Dictionary<int, PlanIngredient> Ingredients = new()
    {
        [Flour] = new(Flour, RawMaterialSource.Purchased, 0m, null, null),
        [Sugar] = new(Sugar, RawMaterialSource.Purchased, 0m, null, null),
        [Ube] = new(Ube, RawMaterialSource.Purchased, 0m, null, null),
        [Egg] = new(Egg, RawMaterialSource.Purchased, 0m, null, null),
        [EggWhite] = new(EggWhite, RawMaterialSource.Linked, 0m, Egg, 60m),
        [EggYolk] = new(EggYolk, RawMaterialSource.Linked, 0m, Egg, 30m),
        [Syrup] = new(Syrup, RawMaterialSource.Made, 20m, null, null),
        [Glaze] = new(Glaze, RawMaterialSource.Made, 0m, null, null),
    };

    // Syrup: 1,000 g sugar boiled down by 20% to 800 g. Glaze: 500 g of syrup and 500 g of egg white.
    private static readonly ILookup<int, (int ComponentId, decimal Grams)> Boms = new[]
    {
        (Made: Syrup, Line: (Sugar, 1000m)),
        (Made: Glaze, Line: (Syrup, 500m)),
        (Made: Glaze, Line: (EggWhite, 500m)),
    }.ToLookup(b => b.Made, b => b.Line);

    [Fact]
    public void One_product_needs_the_whole_batches_its_pieces_take()
    {
        // 85 pieces at 60 g from a 1,700 g dough are exactly 3 batches, not a hair over and so 4.
        var plan = Plan([(1, 85m)], new() { [1] = Recipe(Section(Dough, [(Flour, 1700m)], weightPerPiece: 60m)) });

        var mix = plan.Mixes.Should().ContainSingle().Subject;
        mix.Batches.Should().Be(3m);
        mix.BatchWeight.Should().Be(1700m);
        mix.Uses.Should().Equal(new PlannedUse(1, 85m, 3m));
        plan.Purchases.Should().Equal(new Dictionary<int, decimal> { [Flour] = 5100m });
        plan.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Products_sharing_a_dough_share_its_batches_before_they_are_rounded_up()
    {
        // Each takes 40 pieces of 10 g from a 1,000 g dough: 0.4 of a batch each, so 1 batch between them, not 2.
        var plan = Plan(
            [(1, 40m), (2, 40m)],
            new()
            {
                [1] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 10m)),
                [2] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 10m)),
            });

        var mix = plan.Mixes.Should().ContainSingle().Subject;
        mix.BatchesNeeded.Should().Be(0.8m);
        mix.Batches.Should().Be(1m);
        mix.Uses.Select(u => u.ProductId).Should().Equal(1, 2);
        plan.Purchases[Flour].Should().Be(1000m);
    }

    [Fact]
    public void A_use_with_add_ins_of_its_own_is_a_mix_of_its_own_and_its_add_ins_go_in_every_batch()
    {
        // The ube loaf adds 100 g of ube to the 1,000 g dough: 1,100 g a batch, 110 g a piece from its 11 pieces.
        var plan = Plan(
            [(1, 4m), (2, 5m)],
            new()
            {
                [1] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 250m)),
                [2] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 275m, addIns: [(Ube, 100m)])),
            });

        plan.Mixes.Should().HaveCount(2);
        var ube = plan.Mixes.Single(m => m.AddIns.Count > 0);
        ube.BatchWeight.Should().Be(1100m);
        ube.Batches.Should().Be(2m, "5 pieces at 275 g from 1,100 g are 1.25 batches");
        plan.Mixes.Single(m => m.AddIns.Count == 0).Batches.Should().Be(1m);
        plan.Purchases[Ube].Should().Be(200m);
        plan.Purchases[Flour].Should().Be(3000m);
    }

    [Fact]
    public void A_typed_weight_and_a_typed_yield_count_as_the_Scaling_tab_counts_them()
    {
        var plan = Plan(
            [(1, 50m), (2, 50m)],
            new()
            {
                // 50 pieces of 20 g from 1,000 g are exactly 1 batch.
                [1] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 20m)),

                // A filling yielding 24 pieces a batch: 50 pieces take 3.
                [2] = Recipe(Section(Filling, [(Sugar, 600m)], yield: 24m)),
            });

        plan.Mixes.Single(m => m.SubAssemblyId == Dough).Batches.Should().Be(1m);
        plan.Mixes.Single(m => m.SubAssemblyId == Filling).Batches.Should().Be(3m);
    }

    [Fact]
    public void A_section_with_no_piece_size_or_no_lines_is_left_out_and_said()
    {
        var plan = Plan(
            [(1, 10m)],
            new() { [1] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 0m), Section(Filling, [], weightPerPiece: 10m)) });

        plan.Mixes.Should().BeEmpty();
        plan.Issues.Should().Equal(
            new PlanIssue.NoPieceSize(1, Dough),
            new PlanIssue.NoLines(1, Filling));
    }

    [Fact]
    public void A_composite_makes_its_products_times_their_quantity_and_brings_their_packaging_only_when_it_includes_it()
    {
        var single = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 10m)) with { PackagingPerUnit = [(Bag, 1m)] };
        var box = new PlanRecipe(1, [], [(Box, 1m)], [(1, 6)], IncludesComponentPackaging: false);
        var boxWithBags = box with { IncludesComponentPackaging = true };

        var plain = Plan([(2, 10m)], new() { [1] = single, [2] = box });
        var bagged = Plan([(2, 10m)], new() { [1] = single, [2] = boxWithBags });

        plain.Mixes.Single().Uses.Should().Equal(new PlannedUse(1, 60m, 0.6m));
        plain.Purchases.Should().ContainKey(Box).WhoseValue.Should().Be(10m);
        plain.Purchases.Should().NotContainKey(Bag);
        bagged.Purchases[Bag].Should().Be(60m);
    }

    [Fact]
    public void Packaging_is_its_quantity_per_unit_times_the_units()
    {
        var pack = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 10m)) with { QuantityRequired = 8, PackagingPerUnit = [(Box, 1m), (Bag, 8m)] };

        var plan = Plan([(1, 20m)], new() { [1] = pack });

        plan.Purchases[Box].Should().Be(20m);
        plan.Purchases[Bag].Should().Be(160m);
        plan.Mixes.Single().Uses.Single().Pieces.Should().Be(160m);
    }

    [Fact]
    public void A_made_ingredient_is_bought_as_what_it_is_made_of_to_any_depth()
    {
        // 400 g of glaze takes 200 g of syrup and 200 g of egg white; 200 g of syrup takes 250 g of sugar (20% boils off).
        var plan = Plan([(1, 1m)], new() { [1] = Recipe(Section(Filling, [(Glaze, 400m)], yield: 1m)) });

        plan.Prepared.Should().Equal(new Dictionary<int, decimal> { [Glaze] = 400m, [Syrup] = 200m });
        plan.Purchases[Sugar].Should().Be(250m);
        plan.Purchases[Egg].Should().BeApproximately(200m * 100m / 60m, 0.0000001m);
        plan.Purchases.Should().NotContainKeys(Glaze, Syrup, EggWhite);
    }

    [Fact]
    public void Parts_of_one_ingredient_take_the_largest_share_with_its_own_use_on_top()
    {
        // 300 g of white (60%) is 500 g of eggs and 120 g of yolk (30%) is 400 g: the same 500 g give both. 50 g whole on top.
        var plan = Plan(
            [(1, 1m)],
            new() { [1] = Recipe(Section(Filling, [(EggWhite, 300m), (EggYolk, 120m), (Egg, 50m)], yield: 1m)) });

        plan.Purchases.Should().Equal(new Dictionary<int, decimal> { [Egg] = 550m });
    }

    [Fact]
    public void A_made_ingredient_with_no_bom_and_a_broken_link_are_bought_as_they_are_and_said()
    {
        var ingredients = new Dictionary<int, PlanIngredient>(Ingredients)
        {
            [Syrup] = new(Syrup, RawMaterialSource.Made, 0m, null, null),
            [EggWhite] = new(EggWhite, RawMaterialSource.Linked, 0m, null, null),
        };

        var plan = ProductionPlanRules.Plan(
            [(1, 1m)],
            new Dictionary<int, PlanRecipe> { [1] = Recipe(Section(Filling, [(Syrup, 100m), (EggWhite, 50m)], yield: 1m)) },
            ingredients,
            Array.Empty<(int Made, (int, decimal) Line)>().ToLookup(b => b.Made, b => b.Line));

        plan.Purchases.Should().Equal(new Dictionary<int, decimal> { [Syrup] = 100m, [EggWhite] = 50m });
        plan.Issues.Should().BeEquivalentTo<PlanIssue>(
            [new PlanIssue.MadeWithoutBom(Syrup), new PlanIssue.BrokenLink(EggWhite)], o => o.ComparingByValue<PlanIssue>());
    }

    [Fact]
    public void A_product_gone_and_a_composite_holding_itself_are_said_not_planned()
    {
        var plan = Plan([(9, 1m), (2, 1m)], new() { [2] = new PlanRecipe(1, [], [], [(2, 1)], false) });

        plan.Issues.Should().Equal(new PlanIssue.MissingProduct(9), new PlanIssue.HoldsItself(2));
        plan.Mixes.Should().BeEmpty();
    }

    [Fact]
    public void A_mix_lists_what_goes_into_a_batch_its_lines_then_its_add_ins()
    {
        var plan = Plan(
            [(1, 5m)],
            new() { [1] = Recipe(Section(Dough, [(Flour, 1000m), (Sugar, 100m)], weightPerPiece: 240m, addIns: [(Ube, 100m)])) });

        plan.Mixes.Single().Lines.Should().Equal((Flour, 1000m), (Sugar, 100m), (Ube, 100m));
    }

    [Fact]
    public void With_half_batches_a_mix_is_rounded_up_to_the_next_half_batch()
    {
        // 120 pieces of 10 g from a 1,000 g dough are 1.2 batches: 1.5 in half batches, where whole ones make 2.
        var recipes = new Dictionary<int, PlanRecipe> { [1] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 10m)) };

        var whole = ProductionPlanRules.Plan([(1, 120m)], recipes, Ingredients, Boms);
        var half = ProductionPlanRules.Plan([(1, 120m)], recipes, Ingredients, Boms, halfBatches: true);

        whole.Mixes.Single().Batches.Should().Be(2m);
        half.Mixes.Single().Batches.Should().Be(1.5m);
        half.Purchases[Flour].Should().Be(1500m);
    }

    [Fact]
    public void The_batches_rounded_up_leave_spare_pieces_for_each_product_the_mix_is_for()
    {
        // 40 pieces of 10 g and 40 of 20 g from a 1,000 g dough are 1.2 batches between them: 2 are made, and the 800 g
        // to spare are 80 more of the first or 40 more of the second.
        var plan = Plan(
            [(1, 40m), (2, 40m)],
            new()
            {
                [1] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 10m)),
                [2] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 20m)),
            });

        var mix = plan.Mixes.Single();
        mix.Batches.Should().Be(2m);
        ProductionPlanRules.SparePieces(mix, 1).Should().Be(80m);
        ProductionPlanRules.SparePieces(mix, 2).Should().Be(40m);
        ProductionPlanRules.SparePieces(mix, 3).Should().Be(0m, "the mix isn't for it");
    }

    [Fact]
    public void Exact_batches_leave_nothing_spare_and_a_part_of_a_piece_isnt_one()
    {
        // 85 pieces at 60 g from 1,700 g are exactly 3 batches; 7 pieces at 350 g are 2.45 batches of 1,000 g, so 3 are made
        // and the 550 g left are 1.57 pieces' worth: 1 piece.
        var exact = Plan([(1, 85m)], new() { [1] = Recipe(Section(Dough, [(Flour, 1700m)], weightPerPiece: 60m)) });
        var rounded = Plan([(1, 7m)], new() { [1] = Recipe(Section(Dough, [(Flour, 1000m)], weightPerPiece: 350m)) });

        ProductionPlanRules.SparePieces(exact.Mixes.Single(), 1).Should().Be(0m);
        ProductionPlanRules.SparePieces(rounded.Mixes.Single(), 1).Should().Be(1m);
    }

    [Fact]
    public void A_made_ingredient_to_prepare_lists_its_bom_scaled_to_the_grams_to_prepare()
    {
        // 400 g of glaze takes 200 g of syrup and 200 g of egg white; 200 g of syrup takes 250 g of sugar.
        var plan = Plan([(1, 1m)], new() { [1] = Recipe(Section(Filling, [(Glaze, 400m)], yield: 1m)) });

        plan.PreparedLines[Glaze].Should().Equal((Syrup, 200m), (EggWhite, 200m));
        plan.PreparedLines[Syrup].Should().Equal((Sugar, 250m));
    }

    [Fact]
    public void A_lines_sales_are_its_units_at_the_price_and_its_profit_is_over_the_base_cost_once_vat_is_out()
    {
        // 10 at 56 with 12% VAT sell for 560, 500 before VAT; at a base cost of 40 they cost 400, so 100 is profit.
        var sales = ProductionPlanRules.SalesOf(10m, 56m, 12m, 40m);

        sales.Sales.Should().Be(560m);
        sales.SalesBeforeVat.Should().Be(500m);
        sales.Profit.Should().Be(100m);
    }

    private static ProductionPlan Plan(IReadOnlyList<(int, decimal)> orders, Dictionary<int, PlanRecipe> recipes) =>
        ProductionPlanRules.Plan(orders, recipes, Ingredients, Boms);

    private static PlanRecipe Recipe(params PlanSection[] sections) => new(1, sections, [], [], false);

    private static PlanSection Section(
        int subAssemblyId,
        IReadOnlyList<(int, decimal)> lines,
        decimal? weightPerPiece = null,
        decimal? yield = null,
        IReadOnlyList<(int, decimal)>? addIns = null) =>
        new(
            subAssemblyId,
            lines,
            addIns ?? [],
            yield is null ? UsageAuthoredField.WeightPerPiece : UsageAuthoredField.Yield,
            yield ?? weightPerPiece ?? 0m);
}
