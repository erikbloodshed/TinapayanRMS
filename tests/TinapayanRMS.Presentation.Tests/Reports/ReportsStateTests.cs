using System.Collections.Specialized;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Reports;

namespace TinapayanRMS.Presentation.Tests.Reports;

/// <summary>
/// The Reports summaries, read from the database. The Product Cost Summary costs every product as it's saved, through
/// SavedProductReader, as the Products screen prices the product it opens, so the two pricings agree.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ReportsStateTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public void A_row_says_by_how_much_a_current_price_is_short_of_the_target_markup()
    {
        var row = new ProductCostRow(new Product { Name = "Pandesal" }, new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 20m)], MarginPercent = 30m, CurrentPrice = 25m }));
        row.TargetMarkupPercent.Should().Be(30m);
        row.ShortOfTargetMarkup.Should().Be(5m, "25.00 on 20.00 earns 25%");
        new ProductCostRow(new Product { Name = "Pandesal" }, new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 20m)], MarginPercent = 30m, CurrentPrice = 27m }))
            .ShortOfTargetMarkup.Should().BeNull();
    }

    private ReportsState CreateState()
    {
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ReportsState(
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialRepository(factory),
            new ProductRepository(factory),
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                packagingLineRepository,
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                new ProductRepository(factory),
                new CostingDefaultsRepository(factory),
                CreateUnitOfWork()),
            new LoggerConfiguration().CreateLogger());
    }

    [Test]
    public Task The_product_cost_summary_counts_a_linked_usages_add_ins() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);

        // Cream Loaf owns a 1,000 g / ₱50 dough. Both linkers use 100 g a piece, but only one adds ube.
        var (_, doughId) = await AddProductWithSubAssemblyAsync(
            "Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var plainId = await AddProductAsync("Cream Loaf Plain");
        var ubeLoafId = await AddProductAsync("Cream Loaf Ube");
        await SaveRecipeAsync(plainId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 100m)]));
        await SaveRecipeAsync(ubeLoafId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 100m) { AddIns = [(ubeId, 20m)] }]));

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        var plain = state.ProductCosts.Should().ContainSingle(r => r.ProductName == "Cream Loaf Plain").Subject;
        var ube = state.ProductCosts.Should().ContainSingle(r => r.ProductName == "Cream Loaf Ube").Subject;

        plain.BaseCost.Should().Be(5m, "100 g of a ₱50, 1,000 g batch");
        ube.BaseCost.Should().Be(66m * 100m / 1020m, "100 g of the ₱66, 1,020 g batch the ube makes it");
        ube.BaseCost.Should().BeGreaterThan(plain.BaseCost, "the ube is real money and has to reach the summary");
    });

    [Test]
    public Task The_product_cost_summary_breaks_the_base_cost_into_its_parts() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);

        // A 1,000 g / ₱50 dough at 50 g a piece, a ₱30 gift box a piece, and 25% labor, 10% overhead and 5% admin, at the
        // 3% wastage nothing saved in Settings gives, and the shop's 12% VAT.
        await SetVatAsync(12m);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", roleId, [(flourId, 1000m)], weightPerPiece: 50m);
        await using (var dbContext = CreateDbContext())
        {
            var box = new RawMaterial
            {
                Name = "Gift Box", Type = RawMaterialType.Packaging, Category = new RawMaterialCategory { Name = "Boxes" },
                BaseUnit = new UnitOfMeasure { Name = "piece", Abbreviation = "pc" },
                CostPerBaseUnit = 30m, BufferedCostPerBaseUnit = 30m,
            };
            dbContext.RawMaterials.Add(box);
            await dbContext.SaveChangesAsync(Cancellation);
            dbContext.PackagingLines.Add(new PackagingLine
            {
                ProductId = ensaymadaId, PackagingMaterialId = box.Id, AuthoredField = PackagingLineAuthoredField.QtyPerUnit, QtyPerUnit = 1m,
            });
            dbContext.CostingParameters.Add(new CostingParameters
            {
                ProductId = ensaymadaId, LaborPercent = 25m, OverheadPercent = 10m, AdministrativePercent = 5m, MarginPercent = 30m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        var ensaymada = state.ProductCosts.Should().ContainSingle(r => r.ProductName == "Ensaymada").Subject;
        ensaymada.IngredientsCost.Should().Be(2.5m, "50 g of a ₱50, 1,000 g dough");
        ensaymada.WastageCost.Should().Be(0.075m, "3% of the ingredients");
        ensaymada.PackagingCost.Should().Be(30m, "one gift box");
        ensaymada.LaborCost.Should().Be(0.625m, "25% of the ingredients");
        ensaymada.OverheadCost.Should().Be(0.25m, "10% of the ingredients, not the packaging");
        ensaymada.AdministrativeCost.Should().Be(0.125m, "5% of the ingredients");
        ensaymada.BaseCost.Should().Be(33.575m, "the six parts added up");
    });

    [Test]
    public Task The_price_summary_shows_each_purchased_materials_latest_price_and_its_change_per_gram() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddIngredientAsync("Flour");
        var kilogramId = await AddKilogramUnitAsync(flourId);
        await AddIngredientAsync("Salt");
        await SetPriceAsync(flourId, null, 0.05m);
        await SetPriceAsync(flourId, kilogramId, 60m);

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        var flour = state.RawMaterialPrices.Should().ContainSingle(r => r.Name == "Flour").Subject;
        flour.UnitName.Should().Be("kilogram");
        flour.Price.Should().Be(60m);
        flour.ChangePercent.Should().Be(20m, "₱60 a kilogram is ₱0.06 a gram, up from ₱0.05");

        var salt = state.RawMaterialPrices.Should().ContainSingle(r => r.Name == "Salt").Subject;
        salt.Price.Should().BeNull("salt has never been priced");
        salt.ChangePercent.Should().BeNull();
    });

    [Test]
    public Task A_load_fills_each_table_in_one_change() => UiThread.RunAsync(async () =>
    {
        // A grid draws all its rows again for each change to its list: filled a row at a time, the tables were drawn some
        // 330 times a visit and held the window for seconds.
        await AddProductAsync("Pandesal");
        await AddProductAsync("Ensaymada");
        await AddIngredientAsync("Flour", costPerGram: 0.05m, bufferedCostPerGram: 0.04m);
        await AddIngredientAsync("Salt");

        using var state = CreateState();
        List<NotifyCollectionChangedAction> productCosts = [];
        List<NotifyCollectionChangedAction> rawMaterialPrices = [];
        List<NotifyCollectionChangedAction> costBuffers = [];
        state.ProductCosts.CollectionChanged += (_, e) => productCosts.Add(e.Action);
        state.RawMaterialPrices.CollectionChanged += (_, e) => rawMaterialPrices.Add(e.Action);
        state.CostBuffers.CollectionChanged += (_, e) => costBuffers.Add(e.Action);

        await state.LoadCommand.ExecuteAsync();

        state.ProductCosts.Should().HaveCount(2);
        productCosts.Should().Equal(NotifyCollectionChangedAction.Reset);
        state.RawMaterialPrices.Should().HaveCount(2);
        rawMaterialPrices.Should().Equal(NotifyCollectionChangedAction.Reset);
        state.CostBuffers.Should().ContainSingle(r => r.Name == "Flour", "its buffer has worn below the target");
        costBuffers.Should().Equal(NotifyCollectionChangedAction.Reset);
    });

    [Test]
    public Task A_load_that_finds_nothing_new_leaves_the_tables_alone() => UiThread.RunAsync(async () =>
    {
        // What most visits to Reports find: the grids would draw every row again for the same figures.
        await AddProductAsync("Pandesal");
        await AddIngredientAsync("Flour", costPerGram: 0.05m, bufferedCostPerGram: 0.04m);
        using var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        var pandesal = state.ProductCosts.Single();
        state.SelectedCostBuffers.Add(state.CostBuffers.Single());
        List<NotifyCollectionChangedAction> changes = [];
        state.ProductCosts.CollectionChanged += (_, e) => changes.Add(e.Action);
        state.RawMaterialPrices.CollectionChanged += (_, e) => changes.Add(e.Action);
        state.CostBuffers.CollectionChanged += (_, e) => changes.Add(e.Action);

        await state.LoadCommand.ExecuteAsync();

        changes.Should().BeEmpty();
        state.ProductCosts.Single().Should().BeSameAs(pandesal);
        state.SelectedCostBuffers.Should().ContainSingle("the grid keeps the row picked, as it keeps the row");
    });

    [Test]
    public Task A_load_that_finds_a_change_replaces_its_table() => UiThread.RunAsync(async () =>
    {
        await AddProductAsync("Pandesal");
        await AddIngredientAsync("Flour", costPerGram: 0.05m, bufferedCostPerGram: 0.04m);
        using var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.SelectedCostBuffers.Add(state.CostBuffers.Single());
        List<NotifyCollectionChangedAction> productCosts = [];
        List<NotifyCollectionChangedAction> costBuffers = [];
        state.ProductCosts.CollectionChanged += (_, e) => productCosts.Add(e.Action);
        state.CostBuffers.CollectionChanged += (_, e) => costBuffers.Add(e.Action);

        await AddProductAsync("Ensaymada");
        await AddIngredientAsync("Sugar", costPerGram: 0.08m, bufferedCostPerGram: 0.07m);
        await state.LoadCommand.ExecuteAsync();

        state.ProductCosts.Select(r => r.ProductName).Should().Equal("Ensaymada", "Pandesal");
        productCosts.Should().Equal(NotifyCollectionChangedAction.Reset);
        costBuffers.Should().Equal(NotifyCollectionChangedAction.Reset);
        state.SelectedCostBuffers.Should().BeEmpty("the grid unpicks its rows as it takes new ones");
    });

    [Test]
    public Task A_product_cost_row_asks_for_its_own_product_to_be_opened() => UiThread.RunAsync(async () =>
    {
        var pandesalId = await AddProductAsync("Pandesal");
        await AddProductAsync("Ensaymada");

        using var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.ProductCosts.Select(r => r.ProductName).Should().Equal(["Ensaymada", "Pandesal"], "read several at once, they still come by name");
        var row = state.ProductCosts.Should().ContainSingle(r => r.ProductName == "Pandesal").Subject;

        var opened = new List<int>();
        using (state.ProductOpenRequests.Subscribe(opened.Add))
        {
            await state.OpenProductCommand.ExecuteAsync(row.ProductId);
        }

        opened.Should().Equal(pandesalId);
    });

    [Test]
    public Task A_price_summary_row_asks_for_its_own_raw_material_to_be_opened() => UiThread.RunAsync(async () =>
    {
        await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");

        using var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        var row = state.RawMaterialPrices.Should().ContainSingle(r => r.Name == "Sugar").Subject;

        var opened = new List<int>();
        using (state.RawMaterialOpenRequests.Subscribe(opened.Add))
        {
            await state.OpenRawMaterialCommand.ExecuteAsync(row.RawMaterialId);
        }

        opened.Should().Equal(sugarId);
    });

    [Test]
    public Task An_export_says_where_its_workbook_was_saved() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        string? asked = null;
        state.ExportToExcel.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(@"C:\Reports\Product Cost Summary 2026-10-03.xlsx");
        });

        await state.ExportProductCostsCommand.ExecuteAsync();

        asked.Should().Be(ReportsState.ProductCostSummaryTitle, "the page picks the grid by the title, and the file is named after it");
        state.SuccessMessage.Should().Be(@"Saved Product Cost Summary 2026-10-03.xlsx in C:\Reports.");
        state.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task Each_tab_exports_its_own_table() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        string? asked = null;
        state.ExportToExcel.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(null);
        });

        await state.ExportRawMaterialPricesCommand.ExecuteAsync();

        asked.Should().Be(ReportsState.RawMaterialPriceSummaryTitle);
    });

    [Test]
    public Task A_cancelled_export_says_nothing() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        state.ExportToExcel.RegisterHandler(interaction => interaction.SetOutput(null));

        await state.ExportProductCostsCommand.ExecuteAsync();

        state.SuccessMessage.Should().BeNull();
        state.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task A_workbook_that_cant_be_written_says_why() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        state.ExportToExcel.RegisterHandler(_ =>
            throw new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020)));

        await state.ExportProductCostsCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(Failure.FileInUseText);
        state.SuccessMessage.Should().BeNull();
    });
}
