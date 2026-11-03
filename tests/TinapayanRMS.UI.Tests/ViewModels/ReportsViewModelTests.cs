using System.IO;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The Reports summaries, read from the database. The Product Cost Summary costs every product as it's saved, through
/// SavedProductReader, as the Products screen prices the product it opens; before 2026-09-27 Reports had a copy of its
/// own that read a sub-assembly's stored totals as the usage's batch. The add-in figures were worked out for that copy
/// and are unchanged, so the two pricings agree.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ReportsViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ReportsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ReportsViewModel(
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
                new CostingDefaultsRepository(factory)),
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }

    [Fact]
    public async Task The_product_cost_summary_counts_a_linked_usages_add_ins()
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

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        var plain = viewModel.ProductCosts.Should().ContainSingle(r => r.ProductName == "Cream Loaf Plain").Subject;
        var ube = viewModel.ProductCosts.Should().ContainSingle(r => r.ProductName == "Cream Loaf Ube").Subject;

        plain.BaseCost.Should().Be(5m, "100 g of a ₱50, 1,000 g batch");
        ube.BaseCost.Should().Be(66m * 100m / 1020m, "100 g of the ₱66, 1,020 g batch the ube makes it");
        ube.BaseCost.Should().BeGreaterThan(plain.BaseCost, "the ube is real money and has to reach the summary");
    }

    [Fact]
    public async Task The_product_cost_summary_breaks_the_base_cost_into_its_parts()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);

        // A 1,000 g / ₱50 dough at 50 g a piece, a ₱30 gift box a piece, and 25% labor, 10% overhead and 5% admin, at the
        // 3% wastage nothing saved in Settings gives.
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
                ProductId = ensaymadaId, LaborPercent = 25m, OverheadPercent = 10m, AdministrativePercent = 5m, MarginPercent = 30m, VatPercent = 12m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        var ensaymada = viewModel.ProductCosts.Should().ContainSingle(r => r.ProductName == "Ensaymada").Subject;
        ensaymada.IngredientsCost.Should().Be(2.5m, "50 g of a ₱50, 1,000 g dough");
        ensaymada.WastageCost.Should().Be(0.075m, "3% of the ingredients");
        ensaymada.PackagingCost.Should().Be(30m, "one gift box");
        ensaymada.LaborCost.Should().Be(0.625m, "25% of the ingredients");
        ensaymada.OverheadCost.Should().Be(0.25m, "10% of the ingredients, not the packaging");
        ensaymada.AdministrativeCost.Should().Be(0.125m, "5% of the ingredients");
        ensaymada.BaseCost.Should().Be(33.575m, "the six parts added up");
    }

    [Fact]
    public async Task The_price_summary_shows_each_purchased_materials_latest_price_and_its_change_per_gram()
    {
        var flourId = await AddIngredientAsync("Flour");
        var kilogramId = await AddKilogramUnitAsync(flourId);
        await AddIngredientAsync("Salt");
        await SetPriceAsync(flourId, null, 0.05m);
        await SetPriceAsync(flourId, kilogramId, 60m);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        var flour = viewModel.RawMaterialPrices.Should().ContainSingle(r => r.Name == "Flour").Subject;
        flour.UnitName.Should().Be("kilogram");
        flour.Price.Should().Be(60m);
        flour.ChangePercent.Should().Be(20m, "₱60 a kilogram is ₱0.06 a gram, up from ₱0.05");

        var salt = viewModel.RawMaterialPrices.Should().ContainSingle(r => r.Name == "Salt").Subject;
        salt.Price.Should().BeNull("salt has never been priced");
        salt.ChangePercent.Should().BeNull();
    }

    [Fact]
    public async Task A_product_cost_row_asks_for_its_own_product_to_be_opened()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        await AddProductAsync("Ensaymada");

        using var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.ProductCosts.Select(r => r.ProductName).Should().Equal(["Ensaymada", "Pandesal"], "read several at once, they still come by name");
        var row = viewModel.ProductCosts.Should().ContainSingle(r => r.ProductName == "Pandesal").Subject;

        var opened = new List<int>();
        using (viewModel.ProductOpenRequests.Subscribe(opened.Add))
        {
            await viewModel.OpenProductCommand.Execute(row.ProductId);
        }

        opened.Should().Equal(pandesalId);
    }

    [Fact]
    public async Task A_price_summary_row_asks_for_its_own_raw_material_to_be_opened()
    {
        await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");

        using var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        var row = viewModel.RawMaterialPrices.Should().ContainSingle(r => r.Name == "Sugar").Subject;

        var opened = new List<int>();
        using (viewModel.RawMaterialOpenRequests.Subscribe(opened.Add))
        {
            await viewModel.OpenRawMaterialCommand.Execute(row.RawMaterialId);
        }

        opened.Should().Equal(sugarId);
    }

    [Fact]
    public async Task An_export_says_where_its_workbook_was_saved()
    {
        var viewModel = CreateViewModel();
        string? asked = null;
        viewModel.ExportToExcel.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(@"C:\Reports\Product Cost Summary 2026-10-03.xlsx");
        });

        await viewModel.ExportProductCostsCommand.Execute();

        asked.Should().Be(ReportsViewModel.ProductCostSummaryTitle, "the View picks the grid by the title, and the file is named after it");
        viewModel.SuccessMessage.Should().Be(@"Saved Product Cost Summary 2026-10-03.xlsx in C:\Reports.");
        viewModel.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Each_tab_exports_its_own_table()
    {
        var viewModel = CreateViewModel();
        string? asked = null;
        viewModel.ExportToExcel.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(null);
        });

        await viewModel.ExportRawMaterialPricesCommand.Execute();

        asked.Should().Be(ReportsViewModel.RawMaterialPriceSummaryTitle);
    }

    [Fact]
    public async Task A_cancelled_export_says_nothing()
    {
        var viewModel = CreateViewModel();
        viewModel.ExportToExcel.RegisterHandler(interaction => interaction.SetOutput(null));

        await viewModel.ExportProductCostsCommand.Execute();

        viewModel.SuccessMessage.Should().BeNull();
        viewModel.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task A_workbook_that_cant_be_written_says_why()
    {
        var viewModel = CreateViewModel();
        viewModel.ExportToExcel.RegisterHandler(_ =>
            throw new IOException("The process cannot access the file because it is being used by another process."));

        await viewModel.ExportProductCostsCommand.Execute();

        viewModel.ErrorMessage.Should().Be(Failure.FileInUseText);
        viewModel.SuccessMessage.Should().BeNull();
    }
}
