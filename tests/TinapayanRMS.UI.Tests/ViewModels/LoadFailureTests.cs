using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// A read or save that fails, as when the database stops answering, says why in the screen's error notice, and the screen
/// carries on: the next pick, load or save works as usual once the database answers again (2026-10-03). Until then a failed
/// pick ended the screen's selection subscription for good (no later pick loaded anything, and Save did nothing, with no
/// word of why), and a failed load or an unexpected failure in a save closed the app.
/// </summary>
[Collection(DatabaseCollection.B)]
public class LoadFailureTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public Task Products_a_pick_that_fails_to_load_says_so_and_the_next_pick_loads() => StaThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        foreach (var name in (string[])["Almond Bread", "Banana Bread", "Cheese Bread"])
        {
            await AddProductWithSubAssemblyAsync(name, roleId, [new RecipeLine(flourId, 1000m)]);
        }

        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateProductsViewModel(factory);
        await viewModel.LoadCommand.Execute();
        Product Named(string name) => viewModel.Products.Single(p => p.Name == name);

        await PickAsync(() => viewModel.SelectedProduct = Named("Almond Bread"), () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1);

        factory.Fail = true;
        await PickAsync(() => viewModel.SelectedProduct = Named("Banana Bread"), () => viewModel.ErrorMessage is not null);
        viewModel.ErrorMessage.Should().Be(Failure.TimedOutText);
        viewModel.UsageEdits.Should().BeEmpty("nothing of Banana Bread's came, and Almond Bread's mustn't show under its name");

        factory.Fail = false;
        await PickAsync(
            () => viewModel.SelectedProduct = Named("Cheese Bread"),
            () => viewModel.AreDetailsReady && viewModel.EditingName == "Cheese Bread" && viewModel.UsageEdits.Count == 1);
        viewModel.ErrorMessage.Should().BeNull("a load that works takes away the error a failed one left");
    });

    // While a picked product's details are on their way the form still holds the product before's, which a Save would
    // write under the new one's name. Typing then isn't an unsaved change (the history records nothing until they come),
    // so Save stays off, rather than on and doing nothing as a review of the code had read it (2026-11-05).
    [Fact]
    public Task Products_Save_stays_off_while_the_picked_products_details_are_on_their_way() => StaThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        foreach (var name in (string[])["Almond Bread", "Banana Bread"])
        {
            await AddProductWithSubAssemblyAsync(name, roleId, [new RecipeLine(flourId, 1000m)]);
        }

        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateProductsViewModel(factory);
        await viewModel.LoadCommand.Execute();
        Product Named(string name) => viewModel.Products.Single(p => p.Name == name);
        await PickAsync(() => viewModel.SelectedProduct = Named("Almond Bread"), () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1);

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Hold = hold;
        viewModel.SelectedProduct = Named("Banana Bread");
        viewModel.AreDetailsReady.Should().BeFalse("Banana Bread's details are still on their way");
        viewModel.EditingName = "Banana Loaf";

        viewModel.History.IsDirty.Should().BeFalse("the history records nothing until the details come");
        (await viewModel.SaveCommand.CanExecute.FirstAsync()).Should().BeFalse("what the form holds isn't Banana Bread's to save");

        factory.Hold = null;
        hold.SetResult();
        await PickAsync(() => { }, () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1);
    });

    [Fact]
    public Task Raw_materials_a_pick_that_fails_to_load_says_so_and_the_next_pick_loads() => StaThread.RunAsync(async () =>
    {
        var ids = new List<int>();
        foreach (var name in (string[])["Butter", "Flour", "Sugar"])
        {
            var id = await AddIngredientAsync(name);
            await SetPriceAsync(id, unitId: null, price: 0.05m);
            ids.Add(id);
        }

        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateRawMaterialsViewModel(factory);
        await viewModel.LoadCommand.Execute();
        RawMaterial WithId(int id) => viewModel.RawMaterials.Single(m => m.Id == id);

        await PickAsync(() => viewModel.SelectedRawMaterial = WithId(ids[0]), () => viewModel.AreDetailsReady && viewModel.BrandPriceRows.Count == 1);

        factory.Fail = true;
        await PickAsync(() => viewModel.SelectedRawMaterial = WithId(ids[1]), () => viewModel.ErrorMessage is not null);
        viewModel.ErrorMessage.Should().Be(Failure.TimedOutText);

        factory.Fail = false;
        await PickAsync(() => viewModel.SelectedRawMaterial = WithId(ids[2]), () => viewModel.AreDetailsReady && viewModel.BrandPriceRows.Count == 1);
        viewModel.ErrorMessage.Should().BeNull();
    });

    [Fact]
    public Task Sub_assemblies_a_pick_that_fails_to_load_leaves_a_blank_form_and_the_next_pick_loads() => StaThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        foreach (var name in (string[])["Almond", "Banana", "Cheese"])
        {
            await AddProductWithSubAssemblyAsync(name, roleId, [new RecipeLine(flourId, 1000m)]);
        }

        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateSubAssembliesViewModel(factory);
        await viewModel.LoadCommand.Execute();
        StandaloneSubAssemblyRow Named(string name) => viewModel.Rows.Single(r => r.Name == name);

        await PickAsync(() => viewModel.SelectedRow = Named("Almond Dough"), () => viewModel.EditingName == "Almond Dough" && viewModel.Lines.Count == 1,
            () => $"name={viewModel.EditingName} lines={viewModel.Lines.Count} error={viewModel.ErrorMessage}");

        // The failure clears the pick, and the pick of nothing that follows mustn't take the error away.
        factory.Fail = true;
        await PickAsync(() => viewModel.SelectedRow = Named("Banana Dough"), () => viewModel.ErrorMessage is not null,
            () => $"name={viewModel.EditingName} lines={viewModel.Lines.Count} row={viewModel.SelectedRow?.Name}");
        viewModel.ErrorMessage.Should().Be(Failure.TimedOutText);
        viewModel.SelectedRow.Should().BeNull("the form went blank rather than show Almond Dough under Banana Dough's row");
        viewModel.Lines.Should().BeEmpty();

        factory.Fail = false;
        await PickAsync(() => viewModel.SelectedRow = Named("Cheese Dough"), () => viewModel.EditingName == "Cheese Dough" && viewModel.Lines.Count == 1,
            () => $"name={viewModel.EditingName} lines={viewModel.Lines.Count} row={viewModel.SelectedRow?.Name} error={viewModel.ErrorMessage}");
        viewModel.ErrorMessage.Should().BeNull();
    });

    [Fact]
    public Task Reports_a_load_that_fails_says_so_and_the_next_one_reads_the_reports() => StaThread.RunAsync(async () =>
    {
        await AddProductAsync("Pandesal");
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateReportsViewModel(factory);

        factory.Fail = true;
        await viewModel.LoadCommand.Execute();

        viewModel.ErrorMessage.Should().Be(Failure.TimedOutText, "the load said why, and didn't throw");

        factory.Fail = false;
        await viewModel.LoadCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.ProductCosts.Should().ContainSingle().Which.ProductName.Should().Be("Pandesal");
    });

    [Fact]
    public Task A_save_that_fails_says_why_and_doesnt_throw() => StaThread.RunAsync(async () =>
    {
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        ReactiveUIBootstrapper.EnsureInitialized();
        var viewModel = new CategoriesViewModel(new RawMaterialCategoryRepository(factory), new RawMaterialCategoryValidator(), new LoggerConfiguration().CreateLogger());
        await viewModel.LoadCommand.Execute();
        viewModel.EditingName = "Breads";

        factory.Fail = true;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be(Failure.TimedOutText);

        factory.Fail = false;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Categories.Should().ContainSingle().Which.Name.Should().Be("Breads");
    });

    // No to "Save the changes?" on leaving a screen reads the record again to put the form back. Nothing awaited the move
    // to another tab, so a read that failed there was lost: the click did nothing, with no word of why (2026-11-04).
    [Fact]
    public Task Products_a_form_that_cant_be_read_back_on_leaving_says_so_and_stays() => StaThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Almond Bread", roleId, [new RecipeLine(flourId, 1000m)]);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateProductsViewModel(factory);
        await viewModel.LoadCommand.Execute();
        await PickAsync(() => viewModel.SelectedProduct = viewModel.Products.Single(), () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1);
        viewModel.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        viewModel.EditingName = "Almond Loaf";

        factory.Fail = true;
        var mayLeave = await viewModel.ResolveUnsavedChangesAsync(revert: true);

        mayLeave.Should().BeFalse("the form couldn't be put back, so it isn't left");
        viewModel.ErrorMessage.Should().Be(Failure.TimedOutText);

        factory.Fail = false;
        await viewModel.LoadCommand.Execute();
        await PickAsync(() => { }, () => viewModel.AreDetailsReady);
        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("it can be left once the database answers again");
    });

    [Fact]
    public Task Raw_materials_a_form_that_cant_be_read_back_on_leaving_says_so_and_stays() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddIngredientAsync("Flour");
        await SetPriceAsync(flourId, unitId: null, price: 0.05m);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateRawMaterialsViewModel(factory);
        await viewModel.LoadCommand.Execute();
        await PickAsync(() => viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(), () => viewModel.AreDetailsReady && viewModel.BrandPriceRows.Count == 1);
        viewModel.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        viewModel.EditingName = "Strong Flour";

        factory.Fail = true;
        var mayLeave = await viewModel.ResolveUnsavedChangesAsync(revert: true);

        mayLeave.Should().BeFalse("the form couldn't be put back, so it isn't left");
        viewModel.ErrorMessage.Should().Be(Failure.TimedOutText);
        viewModel.EditingName.Should().Be("Strong Flour", "the changes are still there");
        viewModel.History.IsDirty.Should().BeTrue();

        factory.Fail = false;
        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("it can be left once the database answers again");
    });

    [Fact]
    public Task Sub_assemblies_a_form_that_cant_be_read_back_on_leaving_says_so_and_stays() => StaThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Almond", roleId, [new RecipeLine(flourId, 1000m)]);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateSubAssembliesViewModel(factory);
        await viewModel.LoadCommand.Execute();
        await PickAsync(() => viewModel.SelectedRow = viewModel.Rows.Single(), () => viewModel.EditingName == "Almond Dough" && viewModel.Lines.Count == 1);
        viewModel.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        viewModel.EditingName = "Almond Loaf Dough";

        factory.Fail = true;
        var mayLeave = await viewModel.ResolveUnsavedChangesAsync(revert: true);

        mayLeave.Should().BeFalse("the form couldn't be put back, so it isn't left");
        viewModel.ErrorMessage.Should().Be(Failure.TimedOutText);

        factory.Fail = false;
        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("it can be left once the database answers again");
    });

    // A record opened by the Vim command line's :e name, which nothing awaits.
    [Fact]
    public Task A_question_that_cant_be_asked_says_so_and_stays() => StaThread.RunAsync(async () =>
    {
        var ids = new List<int>();
        foreach (var name in (string[])["Butter", "Flour"])
        {
            var id = await AddIngredientAsync(name);
            await SetPriceAsync(id, unitId: null, price: 0.05m);
            ids.Add(id);
        }

        var viewModel = CreateRawMaterialsViewModel(new FailingDbContextFactory(CreateDbContextFactory()));
        await viewModel.LoadCommand.Execute();
        await PickAsync(() => viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(m => m.Id == ids[0]), () => viewModel.AreDetailsReady && viewModel.BrandPriceRows.Count == 1);
        viewModel.ConfirmUnsavedChanges.RegisterHandler(_ => throw new InvalidOperationException("The question couldn't be asked."));
        viewModel.EditingName = "Salted Butter";

        viewModel.OpenByName("Flour").Should().BeTrue();

        await PickAsync(() => { }, () => viewModel.ErrorMessage is not null);
        viewModel.ErrorMessage.Should().Contain("The question couldn't be asked.");
        viewModel.SelectedRawMaterial!.Id.Should().Be(ids[0], "it stays on the form it was asked to leave");
        viewModel.EditingName.Should().Be("Salted Butter");
    });

    private static async Task PickAsync(Action pick, Func<bool> settled, Func<string>? describe = null)
    {
        pick();
        var deadline = Stopwatch.StartNew();
        var heldFor = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (!settled())
            {
                heldFor.Restart();
            }
            else if (heldFor.Elapsed > TimeSpan.FromMilliseconds(300))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"The pick never settled: {describe?.Invoke()}");
    }

    private ProductsViewModel CreateProductsViewModel(FailingDbContextFactory factory)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var packagingLineRepository = new PackagingLineRepository(factory);
        return new ProductsViewModel(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new CostingDefaultsRepository(factory),
            new PackagingLinkRepository(factory),
            new ProductCloner(factory),
            CreateUnitOfWork(),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductVersionRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }

    private static RawMaterialsViewModel CreateRawMaterialsViewModel(FailingDbContextFactory factory)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        return new RawMaterialsViewModel(
            new RawMaterialRepository(factory),
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            new IngredientBomLineRepository(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new RawMaterialCategoryValidator(),
            new RawMaterialValidator(),
            new RawMaterialUnitValidator(),
            new RawMaterialBrandPriceValidator(),
            new UnitOfMeasureRepository(factory),
            new InMemoryViewStateStore(),
            new CostingDefaultsRepository(factory),
            new LoggerConfiguration().CreateLogger());
    }

    private SubAssembliesViewModel CreateSubAssembliesViewModel(FailingDbContextFactory factory)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
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
            new LoggerConfiguration().CreateLogger());
    }

    private static ReportsViewModel CreateReportsViewModel(FailingDbContextFactory factory)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var productRepository = new ProductRepository(factory);
        return new ReportsViewModel(
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialRepository(factory),
            productRepository,
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                new PackagingLineRepository(factory),
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                productRepository,
                new CostingDefaultsRepository(factory)),
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }
}
