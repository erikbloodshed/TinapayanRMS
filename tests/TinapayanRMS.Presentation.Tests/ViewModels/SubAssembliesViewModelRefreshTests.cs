using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// The Sub-Assemblies screen, on real repositories: one Save writes a standalone sub-assembly's name, role
/// and lines; coming back to the tab (LoadCommand) keeps unsaved lines and reprices them; Delete is refused
/// while a product links it.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class SubAssembliesViewModelRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task One_Save_writes_a_new_standalone_sub_assembly_and_selects_it() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.HeaderTitle.Should().Be("New sub-assembly");
        viewModel.EditingRole!.Name.Should().Be("Glaze", "a new form takes the first role");
        viewModel.EditingName = "Chocolate Glaze";
        viewModel.Lines.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 200m });
        viewModel.BatchWeight.Should().Be(200m);
        viewModel.BatchCost.Should().Be(18m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Chocolate Glaze was saved.");
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.SubAssemblies.SingleAsync(Cancellation);
            saved.Name.Should().Be("Chocolate Glaze");
            (await SubAssemblyTotalsAsync(saved.Id)).TotalCost.Should().Be(18m);
        }

        var row = viewModel.Rows.Should().ContainSingle().Subject;
        viewModel.SelectedRow.Should().BeSameAs(row);
        viewModel.BatchWeight.Should().Be(200m);
        viewModel.HeaderTitle.Should().Be("Chocolate Glaze");
        viewModel.Lines.Should().ContainSingle().Which.Grams.Should().Be(200m);
    });

    [Test]
    public Task Saving_without_lines_is_refused_with_a_message() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Glaze");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.EditingName = "Chocolate Glaze";
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Add the ingredients that go into Chocolate Glaze, in grams.");
        viewModel.Rows.Should().BeEmpty();
    });

    [Test]
    public Task Reloading_keeps_unsaved_lines_and_reprices_them() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single();
        await Settle.UntilAsync(viewModel, () => viewModel.Lines.Count == 1, () => $"lines={viewModel.Lines.Count}");

        viewModel.Lines.Single().Grams = 300m;
        await using (var dbContext = CreateDbContext())
        {
            var sugar = await dbContext.RawMaterials.SingleAsync(m => m.Id == sugarId, Cancellation);
            sugar.CostPerBaseUnit = 0.10m;
            sugar.BufferedCostPerBaseUnit = 0.10m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();

        viewModel.Lines.Should().ContainSingle().Which.Grams.Should().Be(300m, "an unsaved edit survives the refresh");
        viewModel.BatchCost.Should().Be(30m, "the line is priced at sugar's new cost");
        viewModel.SelectedRow!.Name.Should().Be("Glaze");
    });

    [Test]
    public Task Deleting_a_linked_one_is_refused_and_the_form_shows_who_uses_it() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        await SaveRecipeAsync(await AddProductAsync("Cream Loaf"), new RecipeDraft([], [new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m)]));
        var viewModel = CreateViewModel();
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        await viewModel.LoadCommand.Execute();

        viewModel.Rows.Single().DependentsText.Should().Be("Cream Loaf");
        viewModel.SelectedRow = viewModel.Rows.Single();
        await Settle.UntilAsync(viewModel, () => viewModel.Lines.Count == 1, () => $"lines={viewModel.Lines.Count}");
        var creamLoaf = viewModel.Dependents.Should().ContainSingle().Subject;
        creamLoaf.Name.Should().Be("Cream Loaf");
        int? opened = null;
        using (viewModel.ProductOpenRequests.Subscribe(productId => opened = productId))
        {
            await viewModel.OpenProductCommand.Execute(creamLoaf.Id);
        }

        opened.Should().Be(creamLoaf.Id, "a tile hands its product's id on, for the main window to open it");
        viewModel.DependentsText.Should().StartWith("These products follow every change");

        await viewModel.DeleteCommand.Execute();

        viewModel.ErrorMessage.Should().StartWith("Cream Loaf links Glaze.");
        viewModel.Rows.Should().ContainSingle();
    });

    [Test]
    public Task OpenSubAssembly_waits_for_a_list_not_loaded_yet_and_clears_a_search_hiding_it() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var repository = new SubAssemblyRepository(CreateDbContextFactory());
        var chocolateId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Chocolate Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var ubeId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var viewModel = CreateViewModel();

        // As a linked badge on Products does before the Sub-Assemblies screen was ever shown.
        viewModel.OpenSubAssembly(ubeId);
        viewModel.SelectedRow.Should().BeNull("the list is empty until the screen loads");

        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRow!.Id.Should().Be(ubeId);
        await Settle.UntilAsync(viewModel, () => viewModel.EditingName == "Ube Glaze", () => $"name={viewModel.EditingName}");

        viewModel.SearchText = "Chocolate";
        viewModel.OpenSubAssembly(chocolateId);
        viewModel.SelectedRow!.Id.Should().Be(chocolateId);
        viewModel.SearchText.Should().Be("Chocolate", "a search showing it is left alone");

        viewModel.OpenSubAssembly(ubeId);
        viewModel.SelectedRow!.Id.Should().Be(ubeId);
        viewModel.SearchText.Should().BeEmpty("a search hiding it is cleared, so the list shows the row picked");
        viewModel.VisibleRows.Should().Contain(viewModel.SelectedRow);
    });

    [Test]
    public Task OpenByName_opens_the_sub_assembly_a_name_means_best_as_the_Vim_command_line_asks() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var repository = new SubAssemblyRepository(CreateDbContextFactory());
        await repository.SaveStandaloneAsync(new SubAssembly { Name = "Chocolate Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var ubeId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.OpenByName("ube").Should().BeTrue();

        viewModel.SelectedRow!.Id.Should().Be(ubeId);
        viewModel.OpenByName("fondant").Should().BeFalse();
        await Settle.UntilAsync(viewModel, () => viewModel.EditingName == "Ube Glaze", () => $"name={viewModel.EditingName}");
    });

    [Test]
    public Task In_the_packaging_role_the_form_saves_and_reloads_a_packaging_set() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Glaze");
        await AddRoleAsync("Packaging", sortOrder: 2, isPackaging: true);
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        int boxId;
        await using (var dbContext = CreateDbContext())
        {
            var box = new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = "Gift Box",
                Category = new RawMaterialCategory { Name = "Boxes" },
                BaseUnitId = pieceId,
                CostPerBaseUnit = 30m,
                BufferedCostPerBaseUnit = 33m,
            };
            dbContext.RawMaterials.Add(box);
            await dbContext.SaveChangesAsync(Cancellation);
            boxId = box.Id;
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.IsPackaging.Should().BeFalse("a new form takes the first role, Glaze");

        viewModel.EditingRole = viewModel.Roles.Single(r => r.IsPackaging);
        viewModel.IsPackaging.Should().BeTrue();
        viewModel.EditingName = "Gift Box Set";
        viewModel.PackagingLines.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 2m });
        viewModel.SetCost.Should().Be(66m, "two boxes at their buffered ₱33");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Gift Box Set was saved.");
        await Settle.UntilAsync(viewModel, () => viewModel.PackagingLines.Count == 1, () => $"packaging lines={viewModel.PackagingLines.Count}");
        viewModel.IsPackaging.Should().BeTrue();
        viewModel.Lines.Should().BeEmpty();
        viewModel.PackagingLines.Single().BaseUnitAbbreviation.Should().Be("pc");
        viewModel.SetCost.Should().Be(66m);
        viewModel.DependentsText.Should().Contain("Add Sub-Assembly");
    });

    [Test]
    public Task Copy_starts_a_new_one_with_the_same_role_and_lines_and_Save_adds_it_beside_the_original() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough");
        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var repository = new SubAssemblyRepository(CreateDbContextFactory());
        var originalId = await repository.SaveStandaloneAsync(
            new SubAssembly { Name = "House Glaze", RoleId = glazeRoleId },
            [new RecipeLine(flourId, 100m, IsFormulaBase: true), (sugarId, 50m)],
            Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single();
        await Settle.UntilAsync(viewModel, () => viewModel.Lines.Count == 2, () => $"lines={viewModel.Lines.Count}");

        await viewModel.CopyCommand.Execute();

        viewModel.SelectedRow.Should().BeNull("the copy isn't in the list until it's saved");
        viewModel.HeaderTitle.Should().Be("Copy of House Glaze");
        viewModel.EditingName.Should().Be("House Glaze Copy");
        viewModel.EditingRole!.Name.Should().Be("Glaze", "the copy keeps the original's role, not a new form's first one");
        viewModel.Lines.Select(l => (l.IngredientId, l.Grams, l.IsFormulaBase)).Should().Equal((flourId, 100m, true), (sugarId, 50m, false));
        viewModel.BatchCost.Should().Be(9.5m);
        viewModel.Dependents.Should().BeEmpty();
        viewModel.History.IsDirty.Should().BeTrue("Save is offered straight away");
        viewModel.History.CanUndo.Should().BeFalse();

        viewModel.EditingName = "Ube Glaze";
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Rows.Select(r => r.Name).Should().Equal("House Glaze", "Ube Glaze");
        viewModel.SelectedRow!.Name.Should().Be("Ube Glaze");
        viewModel.SelectedRow.Id.Should().NotBe(originalId);
        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == originalId).CountAsync(Cancellation);
        lines.Should().Be(2, "the original is left as it was");
        (await dbContext.SubAssemblyLines.CountAsync(l => l.SubAssemblyId == viewModel.SelectedRow.Id, Cancellation)).Should().Be(2);
    });

    [Test]
    public Task A_copys_name_is_one_no_other_sub_assembly_has() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var repository = new SubAssemblyRepository(CreateDbContextFactory());
        await repository.SaveStandaloneAsync(new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        await repository.SaveStandaloneAsync(new SubAssembly { Name = "Glaze Copy", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single(r => r.Name == "Glaze");
        await Settle.UntilAsync(viewModel, () => viewModel.EditingName == "Glaze" && viewModel.Lines.Count == 1, () => $"name={viewModel.EditingName}");

        await viewModel.CopyCommand.Execute();
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Rows.Select(r => r.Name).Should().Equal("Glaze", "Glaze Copy", "Glaze Copy 2");
    });

    [Test]
    public Task Copying_a_packaging_set_copies_its_materials() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Glaze");
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 2, isPackaging: true);
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        int boxId;
        await using (var dbContext = CreateDbContext())
        {
            var box = new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = "Gift Box",
                Category = new RawMaterialCategory { Name = "Boxes" },
                BaseUnitId = pieceId,
                CostPerBaseUnit = 30m,
                BufferedCostPerBaseUnit = 33m,
            };
            dbContext.RawMaterials.Add(box);
            await dbContext.SaveChangesAsync(Cancellation);
            boxId = box.Id;
        }

        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Set", RoleId = packagingRoleId }, [(boxId, null, 2m)], Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single();
        await Settle.UntilAsync(viewModel, () => viewModel.PackagingLines.Count == 1, () => $"packaging lines={viewModel.PackagingLines.Count}");

        await viewModel.CopyCommand.Execute();

        viewModel.IsPackaging.Should().BeTrue();
        viewModel.EditingName.Should().Be("Gift Set Copy");
        viewModel.PackagingLines.Should().ContainSingle().Which.QtyPerUnit.Should().Be(2m);
        viewModel.SetCost.Should().Be(66m);
        viewModel.DependentsText.Should().Contain("Add Sub-Assembly", "a set's own hint, for nothing uses the copy yet");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Rows.Select(r => r.Name).Should().Equal("Gift Set", "Gift Set Copy");
    });

    [Test]
    public Task New_Product_asks_for_a_product_linking_the_picked_sub_assembly_once_its_changes_are_saved() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        var requested = new List<int>();
        using var _ = viewModel.NewProductRequests.Subscribe(requested.Add);

        (await viewModel.NewProductCommand.CanExecute.FirstAsync()).Should().BeFalse("nothing is picked");

        viewModel.SelectedRow = viewModel.Rows.Single();
        await Settle.UntilAsync(viewModel, () => viewModel.Lines.Count == 1, () => $"lines={viewModel.Lines.Count}");
        (await viewModel.NewProductCommand.CanExecute.FirstAsync()).Should().BeTrue();
        viewModel.EditingName = "Sugar Glaze";
        await viewModel.NewProductCommand.Execute();

        requested.Should().BeEmpty("the product would link the glaze as it's saved, not as the form shows it");
        viewModel.ErrorMessage.Should().Be("Save Glaze first, or undo its changes: the new product links it as it's saved.");

        viewModel.EditingName = "Glaze";
        await viewModel.NewProductCommand.Execute();

        requested.Should().Equal(glazeId);
        viewModel.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task New_Product_is_not_offered_for_a_packaging_set() => UiThread.RunAsync(async () =>
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        int boxId;
        await using (var dbContext = CreateDbContext())
        {
            var box = new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = "Gift Box",
                Category = new RawMaterialCategory { Name = "Boxes" },
                BaseUnitId = pieceId,
                CostPerBaseUnit = 30m,
                BufferedCostPerBaseUnit = 30m,
            };
            dbContext.RawMaterials.Add(box);
            await dbContext.SaveChangesAsync(Cancellation);
            boxId = box.Id;
        }

        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRow = viewModel.Rows.Single();

        (await viewModel.NewProductCommand.CanExecute.FirstAsync()).Should().BeFalse("a packaging set isn't a recipe");
    });

    private SubAssembliesViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();

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

    [Test]
    public Task A_typed_percentage_is_saved_and_reopens_as_typed() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = roleId },
            [new RecipeLine(flourId, 1000m, IsFormulaBase: true), (waterId, 600m)],
            Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single();
        await Settle.UntilAsync(viewModel, () => viewModel.Lines.Count == 2, () => $"lines={viewModel.Lines.Count}");

        viewModel.PercentHeader.Should().Be("Baker's %");
        viewModel.Lines.Single(l => l.Ingredient!.Id == waterId).Percent = 62.5m;
        viewModel.FormulaBaseCaption.Should().Contain("162.5");
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await Settle.UntilAsync(viewModel, () => viewModel.Lines.Count == 2, () => $"lines={viewModel.Lines.Count}");
        var water = viewModel.Lines.Single(l => l.Ingredient!.Id == waterId);
        water.Grams.Should().Be(625m);
        water.IsPercentTyped.Should().BeTrue("the form reopens with the percentage as the figure that was typed");
    });

    [Test]
    public Task Unticking_the_base_switches_the_lines_to_absolute_percentages() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = roleId },
            [new RecipeLine(flourId, 1500m, IsFormulaBase: true), (waterId, 500m)],
            Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single();
        await Settle.UntilAsync(viewModel, () => viewModel.Lines.Count == 2, () => $"lines={viewModel.Lines.Count}");
        viewModel.Lines.Single(l => l.Ingredient!.Id == waterId).Percent.Should().Be(500m / 1500m * 100m);

        viewModel.Lines.Single(l => l.Ingredient!.Id == flourId).IsFormulaBase = false;

        viewModel.PercentHeader.Should().Be("Absolute %");
        viewModel.FormulaBaseCaption.Should().StartWith("Absolute %");
        viewModel.Lines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(75m, "1,500 g of the 2,000 g batch");
        viewModel.Lines.Single(l => l.Ingredient!.Id == waterId).Percent.Should().Be(25m);
    });

    [Test]
    public Task The_forms_version_goes_up_as_another_sub_assembly_or_a_new_one_takes_the_form() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var repository = new SubAssemblyRepository(CreateDbContextFactory());
        var chocolateId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Chocolate Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var ubeId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.OpenSubAssembly(ubeId);
        await Settle.UntilAsync(viewModel, () => viewModel.EditingName == "Ube Glaze", () => $"name={viewModel.EditingName}");
        var ube = viewModel.FormVersion;

        // What the boxes hold is still the sub-assembly's: they keep the cursor.
        viewModel.EditingName = "Ube Glaze, thick";
        await viewModel.LoadCommand.Execute();
        viewModel.FormVersion.Should().Be(ube, "typing and a reload leave the form the same sub-assembly's");

        // The form follows the pick once its lines are read, and the version with it.
        viewModel.OpenSubAssembly(chocolateId);
        await Settle.UntilAsync(viewModel, () => viewModel.EditingName == "Chocolate Glaze", () => $"name={viewModel.EditingName}");
        var chocolate = viewModel.FormVersion;
        chocolate.Should().BeGreaterThan(ube);

        await viewModel.NewCommand.Execute();
        var fresh = viewModel.FormVersion;
        fresh.Should().BeGreaterThan(chocolate);

        // New, then the browser's Back: the Name box had the cursor, and stayed blank over the sub-assembly's name.
        viewModel.OpenSubAssembly(ubeId);
        await Settle.UntilAsync(viewModel, () => viewModel.EditingName == "Ube Glaze", () => $"name={viewModel.EditingName}");
        viewModel.FormVersion.Should().BeGreaterThan(fresh);
    });

    [Test]
    public Task The_sub_assembly_in_the_screens_address_is_opened_once_the_load_is_back() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var ubeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var viewModel = CreateViewModel();

        // As the page does when it opens on /sub-assemblies/42, before the load it then runs.
        viewModel.OpenSubAssemblyOnLoad(ubeId);
        viewModel.SelectedRow.Should().BeNull("the list is empty until the screen loads");

        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRow!.Id.Should().Be(ubeId);
        await Settle.UntilAsync(viewModel, () => viewModel.EditingName == "Ube Glaze", () => $"name={viewModel.EditingName}");
    });

    [Test]
    public Task New_clicked_while_the_screen_is_still_opening_the_sub_assembly_in_its_address_keeps_the_new_form() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var ubeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var viewModel = CreateViewModel();
        viewModel.OpenSubAssemblyOnLoad(ubeId);
        var loading = viewModel.LoadCommand.Execute().ToTask(Cancellation);

        // The load is still reading. Ube Glaze opened over the new form at its end, and the name typed was saved as its.
        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Chocolate Glaze";
        await loading;
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRow.Should().BeNull("New was the later word");
        viewModel.HeaderTitle.Should().Be("New sub-assembly");
        viewModel.EditingName.Should().Be("Chocolate Glaze");
        viewModel.Lines.Should().BeEmpty("Ube Glaze's lines never loaded into it");
    });
}
