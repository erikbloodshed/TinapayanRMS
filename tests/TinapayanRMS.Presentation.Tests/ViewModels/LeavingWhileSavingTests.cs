using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// A record screen's form holds unsaved changes until its Save is back, so leaving it meanwhile (Save, then a tab, a link
/// or the window's Close before the Save returned) asked "Save the changes?", and Yes started a second Save of the same form
/// beside the first: a new record's second insert refused as a name already in use, after a Save that worked. Leaving now
/// waits for the Save, and, the form saved, asks nothing.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class LeavingWhileSavingTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Products_leaving_while_a_Save_runs_waits_for_it_and_asks_nothing() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Almond Bread", roleId, [new RecipeLine(flourId, 1000m)]);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateProductsViewModel(factory);
        await viewModel.LoadCommand.Execute();
        await Settle.PickAsync(viewModel, () => viewModel.SelectedProduct = viewModel.Products.Single(), () => viewModel.AreDetailsReady && viewModel.Recipe.UsageEdits.Count == 1);
        var asked = CountQuestions(viewModel);
        viewModel.EditingName = "Almond Loaf";

        var (waited, mayLeave) = await LeaveWhileSavingAsync(factory, viewModel, () => viewModel.SaveCommand.Execute().ToTask());

        mayLeave.Should().BeTrue("the Save went through, so there's nothing left to ask about");
        asked().Should().Be(0, "the Save was waited for and left nothing unsaved (error: {0})", viewModel.ErrorMessage);
        viewModel.History.IsDirty.Should().BeFalse();
        viewModel.ErrorMessage.Should().BeNull();
        waited.Should().BeTrue("it waited for the Save rather than ask while it ran");
        (await new ProductRepository(factory).GetAllAsync(Cancellation)).Should().ContainSingle().Which.Name.Should().Be("Almond Loaf");
    });

    [Test]
    public Task Products_leaving_while_a_new_products_Save_runs_saves_it_once() => UiThread.RunAsync(async () =>
    {
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateProductsViewModel(factory);
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();

        // What New starts on its own (the list's row for the new product) settles first, as it does before anyone types.
        await Settle.UntilAsync(viewModel, () => !viewModel.History.IsDirty, () => $"dirty={viewModel.History.IsDirty}");
        var asked = CountQuestions(viewModel);
        viewModel.EditingName = "Pandesal";

        var (waited, mayLeave) = await LeaveWhileSavingAsync(factory, viewModel, () => viewModel.SaveCommand.Execute().ToTask());

        mayLeave.Should().BeTrue("the Save went through, so there's nothing left to ask about");
        asked().Should().Be(0, "the Save was waited for and left nothing unsaved (error: {0})", viewModel.ErrorMessage);
        waited.Should().BeTrue("it waited for the Save rather than ask while it ran");
        viewModel.ErrorMessage.Should().BeNull("a second Save would have been refused as a name already in use");
        (await new ProductRepository(factory).GetAllAsync(Cancellation)).Should().ContainSingle().Which.Name.Should().Be("Pandesal");
    });

    [Test]
    public Task Raw_materials_leaving_while_a_Save_runs_waits_for_it_and_asks_nothing() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddIngredientAsync("Flour");
        await SetPriceAsync(flourId, unitId: null, price: 0.05m);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateRawMaterialsViewModel(factory);
        await viewModel.LoadCommand.Execute();
        await Settle.PickAsync(viewModel, () => viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(), () => viewModel.AreDetailsReady && viewModel.PurchaseCost.BrandPriceRows.Count == 1);
        var asked = CountQuestions(viewModel);
        viewModel.EditingName = "Strong Flour";

        var (waited, mayLeave) = await LeaveWhileSavingAsync(factory, viewModel, () => viewModel.SaveCommand.Execute().ToTask());

        mayLeave.Should().BeTrue("the Save went through, so there's nothing left to ask about");
        asked().Should().Be(0, "the Save was waited for and left nothing unsaved (error: {0})", viewModel.ErrorMessage);
        waited.Should().BeTrue("it waited for the Save rather than ask while it ran");
        viewModel.ErrorMessage.Should().BeNull();
        (await new RawMaterialRepository(factory).GetByIdAsync(flourId, Cancellation))!.Name.Should().Be("Strong Flour");
    });

    [Test]
    public Task Sub_assemblies_leaving_while_a_Save_runs_waits_for_it_and_asks_nothing() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Almond", roleId, [new RecipeLine(flourId, 1000m)]);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateSubAssembliesViewModel(factory);
        await viewModel.LoadCommand.Execute();
        await Settle.PickAsync(viewModel, () => viewModel.SelectedRow = viewModel.List.Rows.Single(), () => viewModel.EditingName == "Almond Dough" && viewModel.Batch.Lines.Count == 1);
        var asked = CountQuestions(viewModel);
        viewModel.EditingName = "Almond Loaf Dough";

        var (waited, mayLeave) = await LeaveWhileSavingAsync(factory, viewModel, () => viewModel.SaveCommand.Execute().ToTask());

        mayLeave.Should().BeTrue("the Save went through, so there's nothing left to ask about");
        asked().Should().Be(0, "the Save was waited for and left nothing unsaved (error: {0})", viewModel.ErrorMessage);
        waited.Should().BeTrue("it waited for the Save rather than ask while it ran");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.List.Rows.Should().ContainSingle().Which.Name.Should().Be("Almond Loaf Dough");
    });

    // A Save that's refused leaves the form as it was, unsaved: the question still comes, once the Save is back.
    [Test]
    public Task Raw_materials_leaving_while_a_refused_Save_runs_asks_once_its_back() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddIngredientAsync("Flour");
        await SetPriceAsync(flourId, unitId: null, price: 0.05m);
        var factory = new FailingDbContextFactory(CreateDbContextFactory());
        var viewModel = CreateRawMaterialsViewModel(factory);
        await viewModel.LoadCommand.Execute();
        await Settle.PickAsync(viewModel, () => viewModel.SelectedRawMaterial = viewModel.RawMaterials.Single(), () => viewModel.AreDetailsReady && viewModel.PurchaseCost.BrandPriceRows.Count == 1);
        var answers = 0;
        viewModel.ConfirmUnsavedChanges.RegisterHandler(context =>
        {
            answers++;
            context.SetOutput(UnsavedChangesAnswer.Cancel);
        });
        viewModel.EditingName = "Strong Flour";

        // The Save fails at its first read, as a database that stopped answering does.
        factory.Fail = true;
        var saving = viewModel.SaveCommand.Execute().ToTask();
        var leaving = viewModel.ResolveUnsavedChangesAsync(revert: true);
        await saving;

        (await leaving).Should().BeFalse("Cancel keeps the unsaved changes, and the form");
        answers.Should().Be(1);
        viewModel.ErrorMessage.Should().Be(Failure.TimedOutText);
        viewModel.History.IsDirty.Should().BeTrue();
    });

    /// <summary>How many times the screen asks about unsaved changes, answering Save.</summary>
    private static Func<int> CountQuestions(ViewModelBase viewModel)
    {
        var asked = 0;
        viewModel.ConfirmUnsavedChanges.RegisterHandler(context =>
        {
            asked++;
            context.SetOutput(UnsavedChangesAnswer.Save);
        });
        return () => asked;
    }

    /// <summary>
    /// Starts a Save (<paramref name="save"/>) with every read held, as a slow database keeps it on its way, then leaves the
    /// form (ResolveUnsavedChangesAsync) while it's held, as a tab clicked before the Save is back does; then lets the reads
    /// go. Whether leaving was still waiting when they were let go, and its answer.
    /// </summary>
    private static async Task<(bool Waited, bool MayLeave)> LeaveWhileSavingAsync(
        FailingDbContextFactory factory, IUnsavedChangesScreen screen, Func<Task> save)
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Hold = hold;
        var saving = save();
        var leaving = screen.ResolveUnsavedChangesAsync(revert: true);
        var waited = !leaving.IsCompleted;

        factory.Hold = null;
        hold.SetResult();
        await saving;
        return (waited, await leaving);
    }

    private ProductsViewModel CreateProductsViewModel(FailingDbContextFactory factory)
    {
        return new ProductsViewModel(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            new PackagingLineRepository(factory),
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

    private static RawMaterialsViewModel CreateRawMaterialsViewModel(FailingDbContextFactory factory)
    {
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
}
