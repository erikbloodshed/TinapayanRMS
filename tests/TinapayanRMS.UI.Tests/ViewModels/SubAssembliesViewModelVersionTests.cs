using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// A sub-assembly saved on the Sub-Assemblies screen records a version of each product it changes (ProductVersionRecorder):
/// every product linking it whose recipe it changed, with how it stood first for one with no versions yet, noted with where
/// the change came from. A product it doesn't change gets none.
/// </summary>
[Collection(DatabaseCollection.Name)]
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
            productRepository);
        var product = (await productRepository.GetByIdAsync(productId, Cancellation))!;
        var content = ProductVersionSnapshot.From(product, await reader.ReadDetailAsync(productId, Cancellation));
        await new ProductVersionRepository(factory).AddAsync(productId, content, DateTime.Now, null, Cancellation);
    }

    private async Task<SubAssembliesViewModel> OpenAsync(int subAssemblyId)
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single(r => r.Id == subAssemblyId);
        await WaitUntilAsync(() => viewModel.Lines.Count == 1, () => $"lines={viewModel.Lines.Count}, error={viewModel.ErrorMessage}");
        return viewModel;
    }

    private SubAssembliesViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();

        return new SubAssembliesViewModel(
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new SubAssemblyValidator(),
            new SubAssemblyRoleValidator(),
            new InMemoryViewStateStore(),
            VersionRecorders.Create(factory),
            new LoggerConfiguration().CreateLogger());
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
