using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing.Versions;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// A sub-assembly saved on the Sub-Assemblies screen records a version of each product it changes (ProductVersionRecorder):
/// every product linking it whose recipe it changed, with how it stood first for one with no versions yet, noted with where
/// the change came from. A product it doesn't change gets none.
/// </summary>
[Collection(DatabaseCollection.C)]
public class SubAssembliesViewModelVersionTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Saving_a_sub_assemblys_lines_records_a_version_of_each_product_using_it()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var packId = await AddProductAsync("Pandesal x 8");
        await SaveRecipeAsync(packId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 60m)]));
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [new RecipeLine(flourId, 300m)]);

        // Pandesal x 8 has a history already; Pandesal hasn't.
        var versions = new ProductVersionRepository(CreateDbContextFactory());
        await RecordAsItStandsAsync(packId);

        var viewModel = await OpenAsync(doughId);
        viewModel.Lines.Single().Grams = 450m;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Pandesal Dough was saved. A version was recorded for Pandesal and Pandesal x 8.");

        var pandesal = await versions.GetForProductAsync(pandesalId, Cancellation);
        pandesal.Select(v => v.Note).Should().Equal("Pandesal Dough changed on Sub-Assemblies", ProductVersionRules.BaselineNote);
        pandesal.Select(GramsOf).Should().Equal(450m, 500m);

        var pack = await versions.GetForProductAsync(packId, Cancellation);
        pack.Select(v => v.Number).Should().Equal(2, 1);
        pack[0].Note.Should().Be("Pandesal Dough changed on Sub-Assemblies");
        GramsOf(pack[0]).Should().Be(450m);

        (await versions.GetForProductAsync(ensaymadaId, Cancellation)).Should().BeEmpty("Ensaymada's dough is its own");
    }

    [Fact]
    public async Task Saving_new_lines_asks_first_with_what_they_do_to_each_linking_products_base_cost()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);

        // Pandesal takes 10 g a piece, which costs the same from a smaller batch; Pandesal x 8 takes a tenth of the batch,
        // 25.00 of flour, so 2.50 and the shop's 3% wastage, 2.575, which 450 g makes 2.3175.
        var packId = await AddProductAsync("Pandesal x 8");
        await SaveRecipeAsync(packId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.Yield, 10m)]));

        List<string> asked = [];
        var viewModel = await OpenAsync(doughId, asksAboutCosts: true);
        viewModel.ConfirmDelete.RegisterHandler(context => { asked.Add(context.Input); context.SetOutput(false); });
        viewModel.Lines.Single().Grams = 450m;
        await viewModel.SaveCommand.Execute();

        asked.Should().ContainSingle().Which.Should().Be(
            "Saving Pandesal Dough changes the base cost of 1 product, for one sellable unit:\n\nPandesal x 8: 2.58 → 2.32 (-0.26)\n\nSave it?");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.SubAssemblyLines.Single(l => l.SubAssemblyId == doughId).Grams.Should().Be(500m, "No keeps it unsaved");
        }
    }

    [Fact]
    public async Task A_rename_records_no_version()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (pandesalId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);

        var viewModel = await OpenAsync(doughId);
        viewModel.EditingName = "House Dough";
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("House Dough was saved.");
        (await new ProductVersionRepository(CreateDbContextFactory()).GetForProductAsync(pandesalId, Cancellation))
            .Should().BeEmpty("a sub-assembly's name isn't part of a product's recipe");
    }

    [Fact]
    public async Task Recording_the_products_a_sub_assembly_changes_takes_as_many_commands_for_six_as_for_two()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Bread Flour", 0.05m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [new RecipeLine(flourId, 500m)]);
        var (_, rollDoughId) = await AddProductWithSubAssemblyAsync("Spanish Bread", doughRoleId, [new RecipeLine(flourId, 400m)]);
        await LinkProductsAsync(doughId, "Pandesal x", count: 1);
        await LinkProductsAsync(rollDoughId, "Spanish Bread x", count: 5);

        var forTwo = await CommandsRecordingAsync(doughId);
        var forSix = await CommandsRecordingAsync(rollDoughId);

        forTwo.Recorded.Should().HaveCount(2);
        forSix.Recorded.Should().HaveCount(6);
        forSix.Commands.Should().Be(forTwo.Commands, "the versions are written together, not a product at a time");
    }

    private async Task LinkProductsAsync(int subAssemblyId, string name, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            var productId = await AddProductAsync($"{name} {i}");
            await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(subAssemblyId, UsageAuthoredField.WeightPerPiece, 60m)]));
        }
    }

    /// <summary>The commands recording the products takes once the sub-assembly's lines change, and whose versions it recorded.</summary>
    private async Task<(int Commands, IReadOnlyList<string> Recorded)> CommandsRecordingAsync(int subAssemblyId)
    {
        var recorder = VersionRecorders.Create(CreateDbContextFactory());
        var before = await recorder.CaptureAsync([subAssemblyId], [], Cancellation);
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == subAssemblyId)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Grams, l => l.Grams - 50m), Cancellation);
        }

        using var counter = SqlCommandCounter.Start();
        var recorded = await recorder.RecordChangesAsync(before, "Dough changed", DateTime.Now, Cancellation);
        return (counter.Commands, recorded);
    }

    private static decimal GramsOf(ProductVersion version) =>
        ProductVersionContent.FromJson(version.Content).Sections.Single().Lines.Single().Grams;

    private async Task RecordAsItStandsAsync(int productId)
    {
        var factory = CreateDbContextFactory();
        var productRepository = new ProductRepository(factory);
        var reader = new SavedProductReader(
            new PackagingLinkRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            new PackagingLineRepository(factory),
            new CostingParametersRepository(factory),
            new SubAssemblyLineRepository(factory),
            productRepository,
            new CostingDefaultsRepository(factory));
        var product = (await productRepository.GetByIdAsync(productId, Cancellation))!;
        var content = ProductVersionSnapshot.From(product, await reader.ReadDetailAsync(productId, Cancellation));
        await new ProductVersionRepository(factory).AddAsync(productId, content, DateTime.Now, null, Cancellation);
    }

    private async Task<SubAssembliesViewModel> OpenAsync(int subAssemblyId, bool asksAboutCosts = false)
    {
        var viewModel = CreateViewModel(asksAboutCosts);
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single(r => r.Id == subAssemblyId);
        await WaitUntilAsync(() => viewModel.Lines.Count == 1, () => $"lines={viewModel.Lines.Count}, error={viewModel.ErrorMessage}");
        return viewModel;
    }

    private SubAssembliesViewModel CreateViewModel(bool asksAboutCosts = false)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var productRepository = new ProductRepository(factory);

        return new SubAssembliesViewModel(
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new SubAssemblyValidator(),
            new SubAssemblyRoleValidator(),
            new InMemoryViewStateStore(),
            VersionRecorders.CreateSaves(factory, CreateUnitOfWork()),
            new LoggerConfiguration().CreateLogger(),
            asksAboutCosts
                ? new SharedRecipeImpact(
                    new SubAssemblyRepository(factory),
                    productRepository,
                    new SavedProductReader(
                        new PackagingLinkRepository(factory),
                        new ProductSubAssemblyUsageRepository(factory),
                        new PackagingLineRepository(factory),
                        new CostingParametersRepository(factory),
                        new SubAssemblyLineRepository(factory),
                        productRepository,
                        new CostingDefaultsRepository(factory)))
                : null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Func<string> describe)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(15))
            {
                throw new TimeoutException($"Never settled: {describe()}.");
            }

            await Task.Delay(25);
        }
    }
}
