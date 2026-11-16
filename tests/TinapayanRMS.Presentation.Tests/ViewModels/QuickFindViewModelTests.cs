using AwesomeAssertions;
using Serilog;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>Find (Ctrl+K): every raw material, standalone sub-assembly and product by name, best first.</summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class QuickFindViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Every_screens_records_are_found_by_name_and_say_what_they_are() => UiThread.RunAsync(async () =>
    {
        var ubeId = await AddIngredientAsync("Ube Halaya");
        var fillingId = await AddRecipeAsync("Ube Filling", [(ubeId, 500m)]);
        var pandesalId = await AddProductAsync("Ube Cheese Pandesal");
        await AddProductAsync("Cheese Roll");
        var find = Create();
        await find.LoadCommand.ExecuteAsync();

        find.Query = "ube";

        find.Results.Select(r => (r.Screen, r.Id, r.Name, r.Kind)).Should().Equal(
            (AppScreen.Products, pandesalId, "Ube Cheese Pandesal", "Product"),
            (AppScreen.SubAssemblies, fillingId, "Ube Filling", "Sub-Assembly"),
            (AppScreen.RawMaterials, ubeId, "Ube Halaya", "Raw Material"));
        find.Results[1].Group.Should().Be("Base", "a sub-assembly's role");
        find.SelectedResult.Should().Be(find.Results[0]);
        find.ResultsNote.Should().BeNull();
    });

    [Test]
    public Task A_deleted_product_isnt_found() => UiThread.RunAsync(async () =>
    {
        await AddProductAsync("Pandesal");
        var gone = await AddProductAsync("Pandesal Old");
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(gone, Cancellation);
        var find = Create();
        await find.LoadCommand.ExecuteAsync();

        find.Query = "pandesal";

        find.Results.Select(r => (r.Screen, r.Name)).Should().Equal((AppScreen.Products, "Pandesal"));
    });

    [Test]
    public void Every_word_is_matched_ignoring_case_and_accents_with_the_closest_names_first()
    {
        QuickFindResult[] records =
        [
            new(AppScreen.Products, 1, "Cheese Ensaymada", "Product", null),
            new(AppScreen.Products, 2, "Ensaymada", "Product", null),
            new(AppScreen.Products, 3, "Ensaymada Ube", "Product", null),
            new(AppScreen.Products, 4, "Mini-Ensaymada", "Product", null),
            new(AppScreen.Products, 5, "Pansensaymada", "Product", null),
            new(AppScreen.RawMaterials, 6, "Crème Fraîche", "Raw Material", "Dairy"),
        ];

        QuickFindViewModel.Match(records, " ensaymada ").Select(r => r.Id).Should().Equal([2, 3, 1, 4, 5], "exact, starts with, a word starts with, then holds it");
        QuickFindViewModel.Match(records, "ube ENSAY").Select(r => r.Id).Should().Equal(3);
        QuickFindViewModel.Match(records, "creme fraiche").Select(r => r.Id).Should().Equal(6);
        QuickFindViewModel.Match(records, "   ").Should().BeEmpty();
    }

    [Test]
    public Task Nothing_typed_or_nothing_found_says_so() => UiThread.RunAsync(async () =>
    {
        await AddProductAsync("Pandesal");
        var find = Create();
        await find.LoadCommand.ExecuteAsync();

        find.ResultsNote.Should().Be("Type part of a name.");
        find.Query = "zzz";

        find.Results.Should().BeEmpty();
        find.SelectedResult.Should().BeNull();
        find.ResultsNote.Should().Contain("\"zzz\"");
    });

    private QuickFindViewModel Create()
    {
        var factory = CreateDbContextFactory();
        return new QuickFindViewModel(
            new RawMaterialRepository(factory), new SubAssemblyRepository(factory), new ProductRepository(factory), new LoggerConfiguration().CreateLogger());
    }
}
