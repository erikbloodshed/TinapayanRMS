using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.Presentation.Reports;
using TinapayanRMS.Presentation.Settings;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;

namespace TinapayanRMS.Presentation.Tests.Shared;

/// <summary>
/// A read or save that fails, as when the database stops answering, says why in the screen's error notice, and the screen
/// carries on: the next pick, load or save works as usual once the database answers again. A failed pick
/// mustn't end the screen's selection subscription for good (no later pick would load anything, and Save would do
/// nothing, with no word of why), and a failed load or an unexpected failure in a save mustn't close the app.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class LoadFailureTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Products_a_pick_that_fails_to_load_says_so_and_the_next_pick_loads() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        foreach (var name in (string[])["Almond Bread", "Banana Bread", "Cheese Bread"])
        {
            await AddProductWithSubAssemblyAsync(name, roleId, [new RecipeLine(flourId, 1000m)]);
        }

        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var state = CreateProductsState(factory);
        await state.LoadCommand.ExecuteAsync();
        Product Named(string name) => state.Products.Single(p => p.Name == name);

        await Settle.PickAsync(state, () => state.Pick(Named("Almond Bread")), () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1);

        factory.Fail = true;
        await Settle.PickAsync(state, () => state.Pick(Named("Banana Bread")), () => state.ErrorMessage is not null);
        state.ErrorMessage.Should().Be(Failure.TimedOutText);
        state.Recipe.UsageEdits.Should().BeEmpty("nothing of Banana Bread's came, and Almond Bread's mustn't show under its name");

        factory.Fail = false;
        await Settle.PickAsync(state,
            () => state.Pick(Named("Cheese Bread")),
            () => state.AreDetailsReady && state.EditingName == "Cheese Bread" && state.Recipe.UsageEdits.Count == 1);
        state.ErrorMessage.Should().BeNull("a load that works takes away the error a failed one left");
    });

    // While a picked product's details are on their way the form still holds the product before's, which a Save would
    // write under the new one's name. Typing then isn't an unsaved change (the history records nothing until they come),
    // so Save stays off, rather than on and doing nothing.
    [Test]
    public Task Products_Save_stays_off_while_the_picked_products_details_are_on_their_way() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        foreach (var name in (string[])["Almond Bread", "Banana Bread"])
        {
            await AddProductWithSubAssemblyAsync(name, roleId, [new RecipeLine(flourId, 1000m)]);
        }

        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var state = CreateProductsState(factory);
        await state.LoadCommand.ExecuteAsync();
        Product Named(string name) => state.Products.Single(p => p.Name == name);
        await Settle.PickAsync(state, () => state.Pick(Named("Almond Bread")), () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1);

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Hold = hold;
        state.Pick(Named("Banana Bread"));
        state.AreDetailsReady.Should().BeFalse("Banana Bread's details are still on their way");
        state.EditingName = "Banana Loaf";

        state.History.IsDirty.Should().BeFalse("the history records nothing until the details come");
        state.SaveCommand.CanExecute().Should().BeFalse("what the form holds isn't Banana Bread's to save");

        factory.Hold = null;
        hold.SetResult();
        await Settle.PickAsync(state, () => { }, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1);
    });

    [Test]
    public Task Raw_materials_a_pick_that_fails_to_load_says_so_and_the_next_pick_loads() => UiThread.RunAsync(async () =>
    {
        var ids = new List<int>();
        foreach (var name in (string[])["Butter", "Flour", "Sugar"])
        {
            var id = await AddIngredientAsync(name);
            await SetPriceAsync(id, unitId: null, price: 0.05m);
            ids.Add(id);
        }

        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var state = CreateRawMaterialsState(factory);
        await state.LoadCommand.ExecuteAsync();
        RawMaterial WithId(int id) => state.RawMaterials.Single(m => m.Id == id);

        await Settle.PickAsync(state, () => state.Pick(WithId(ids[0])), () => state.AreDetailsReady && state.PurchaseCost.BrandPriceRows.Count == 1);

        factory.Fail = true;
        await Settle.PickAsync(state, () => state.Pick(WithId(ids[1])), () => state.ErrorMessage is not null);
        state.ErrorMessage.Should().Be(Failure.TimedOutText);

        factory.Fail = false;
        await Settle.PickAsync(state, () => state.Pick(WithId(ids[2])), () => state.AreDetailsReady && state.PurchaseCost.BrandPriceRows.Count == 1);
        state.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task Sub_assemblies_a_pick_that_fails_to_load_leaves_a_blank_form_and_the_next_pick_loads() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        foreach (var name in (string[])["Almond", "Banana", "Cheese"])
        {
            await AddProductWithSubAssemblyAsync(name, roleId, [new RecipeLine(flourId, 1000m)]);
        }

        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var state = CreateSubAssembliesState(factory);
        await state.LoadCommand.ExecuteAsync();
        StandaloneSubAssemblyRow Named(string name) => state.View.List.Rows.Single(r => r.Name == name);

        await Settle.PickAsync(state, () => state.Pick(Named("Almond Dough")), () => state.EditingName == "Almond Dough" && state.Batch.Lines.Count == 1,
            () => $"name={state.EditingName} lines={state.Batch.Lines.Count} error={state.ErrorMessage}");

        // The failure clears the pick, and the pick of nothing that follows mustn't take the error away.
        factory.Fail = true;
        await Settle.PickAsync(state, () => state.Pick(Named("Banana Dough")), () => state.ErrorMessage is not null,
            () => $"name={state.EditingName} lines={state.Batch.Lines.Count} row={state.SelectedRow?.Name}");
        state.ErrorMessage.Should().Be(Failure.TimedOutText);
        state.SelectedRow.Should().BeNull("the form went blank rather than show Almond Dough under Banana Dough's row");
        state.Batch.Lines.Should().BeEmpty();

        factory.Fail = false;
        await Settle.PickAsync(state, () => state.Pick(Named("Cheese Dough")), () => state.EditingName == "Cheese Dough" && state.Batch.Lines.Count == 1,
            () => $"name={state.EditingName} lines={state.Batch.Lines.Count} row={state.SelectedRow?.Name} error={state.ErrorMessage}");
        state.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task Reports_a_load_that_fails_says_so_and_the_next_one_reads_the_reports() => UiThread.RunAsync(async () =>
    {
        await AddProductAsync("Pandesal");
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var state = CreateReportsState(factory);

        factory.Fail = true;
        await state.LoadCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(Failure.TimedOutText, "the load said why, and didn't throw");

        factory.Fail = false;
        await state.LoadCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.ProductCosts.Should().ContainSingle().Which.ProductName.Should().Be("Pandesal");
    });

    [Test]
    public Task A_save_that_fails_says_why_and_doesnt_throw() => UiThread.RunAsync(async () =>
    {
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var state = new RawMaterialCategoriesState(new RawMaterialCategoryRepository(factory), new RawMaterialCategoryValidator(), new LoggerConfiguration().CreateLogger());
        await state.LoadCommand.ExecuteAsync();
        state.EditingName = "Breads";

        factory.Fail = true;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(Failure.TimedOutText);

        factory.Fail = false;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.Items.Should().ContainSingle().Which.Name.Should().Be("Breads");
    });

    // No to "Save the changes?" on leaving a screen reads the record again to put the form back. Nothing awaits the move
    // to another tab, so a read that failed there would be lost: the click would do nothing, with no word of why.
    [Test]
    public Task Products_a_form_that_cant_be_read_back_on_leaving_says_so_and_stays() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Almond Bread", roleId, [new RecipeLine(flourId, 1000m)]);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var state = CreateProductsState(factory);
        await state.LoadCommand.ExecuteAsync();
        await Settle.PickAsync(state, () => state.Pick(state.Products.Single()), () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1);
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        state.EditingName = "Almond Loaf";

        factory.Fail = true;
        var mayLeave = await state.ResolveUnsavedChangesAsync(revert: true);

        mayLeave.Should().BeFalse("the form couldn't be put back, so it isn't left");
        state.ErrorMessage.Should().Be(Failure.TimedOutText);

        factory.Fail = false;
        await state.LoadCommand.ExecuteAsync();
        await Settle.PickAsync(state, () => { }, () => state.AreDetailsReady);
        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("it can be left once the database answers again");
    });

    [Test]
    public Task Raw_materials_a_form_that_cant_be_read_back_on_leaving_says_so_and_stays() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddIngredientAsync("Flour");
        await SetPriceAsync(flourId, unitId: null, price: 0.05m);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var state = CreateRawMaterialsState(factory);
        await state.LoadCommand.ExecuteAsync();
        await Settle.PickAsync(state, () => state.Pick(state.RawMaterials.Single()), () => state.AreDetailsReady && state.PurchaseCost.BrandPriceRows.Count == 1);
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        state.EditingName = "Strong Flour";

        factory.Fail = true;
        var mayLeave = await state.ResolveUnsavedChangesAsync(revert: true);

        mayLeave.Should().BeFalse("the form couldn't be put back, so it isn't left");
        state.ErrorMessage.Should().Be(Failure.TimedOutText);
        state.EditingName.Should().Be("Strong Flour", "the changes are still there");
        state.History.IsDirty.Should().BeTrue();

        factory.Fail = false;
        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("it can be left once the database answers again");
    });

    [Test]
    public Task Sub_assemblies_a_form_that_cant_be_read_back_on_leaving_says_so_and_stays() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Almond", roleId, [new RecipeLine(flourId, 1000m)]);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var state = CreateSubAssembliesState(factory);
        await state.LoadCommand.ExecuteAsync();
        await Settle.PickAsync(state, () => state.Pick(state.View.List.Rows.Single()), () => state.EditingName == "Almond Dough" && state.Batch.Lines.Count == 1);
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        state.EditingName = "Almond Loaf Dough";

        factory.Fail = true;
        var mayLeave = await state.ResolveUnsavedChangesAsync(revert: true);

        mayLeave.Should().BeFalse("the form couldn't be put back, so it isn't left");
        state.ErrorMessage.Should().Be(Failure.TimedOutText);

        factory.Fail = false;
        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("it can be left once the database answers again");
    });

    // A record opened by the Vim command line's :e name, which nothing awaits.
    [Test]
    public Task A_question_that_cant_be_asked_says_so_and_stays() => UiThread.RunAsync(async () =>
    {
        var ids = new List<int>();
        foreach (var name in (string[])["Butter", "Flour"])
        {
            var id = await AddIngredientAsync(name);
            await SetPriceAsync(id, unitId: null, price: 0.05m);
            ids.Add(id);
        }

        var state = CreateRawMaterialsState(new FailingDbContextFactory(CreateDbContextFactory()));
        await state.LoadCommand.ExecuteAsync();
        await Settle.PickAsync(state, () => state.Pick(state.RawMaterials.Single(m => m.Id == ids[0])), () => state.AreDetailsReady && state.PurchaseCost.BrandPriceRows.Count == 1);
        state.ConfirmUnsavedChanges.RegisterHandler(_ => throw new InvalidOperationException("The question couldn't be asked."));
        state.EditingName = "Salted Butter";

        state.OpenByName("Flour").Should().BeTrue();

        await Settle.PickAsync(state, () => { }, () => state.ErrorMessage is not null);
        state.ErrorMessage.Should().Contain("The question couldn't be asked.");
        state.SelectedRawMaterial!.Id.Should().Be(ids[0], "it stays on the form it was asked to leave");
        state.EditingName.Should().Be("Salted Butter");
    });

    private ProductsState CreateProductsState(FailingDbContextFactory factory)
    {
        var packagingLineRepository = new PackagingLineRepository(factory);
        return new ProductsState(
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
            new RawMaterialCategoryRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductFolderRepository(factory),
            new ProductVersionRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }

    private static RawMaterialForm CreateRawMaterialsState(FailingDbContextFactory factory)
    {
        return new RawMaterialsState(
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
            new LoggerConfiguration().CreateLogger()).Form!;
    }

    private SubAssemblyForm CreateSubAssembliesState(FailingDbContextFactory factory)
    {
        return new SubAssembliesState(
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new SubAssemblyValidator(),
            new SubAssemblyRoleValidator(),
            new InMemoryViewStateStore(),
            VersionRecorders.CreateSaves(factory, CreateUnitOfWork()),
            new LoggerConfiguration().CreateLogger()).Form!;
    }

    private ReportsState CreateReportsState(FailingDbContextFactory factory)
    {
        var productRepository = new ProductRepository(factory);
        return new ReportsState(
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
                new CostingDefaultsRepository(factory),
                CreateUnitOfWork()),
            new LoggerConfiguration().CreateLogger());
    }
}
