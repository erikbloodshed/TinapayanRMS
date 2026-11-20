using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;

namespace TinapayanRMS.Presentation.Tests.SubAssemblies;

/// <summary>
/// The Sub-Assemblies screen, on real repositories: one Save writes a standalone sub-assembly's name, role
/// and lines; coming back to the tab (LoadCommand) keeps unsaved lines and reprices them; Delete is refused
/// while a product links it.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class SubAssembliesStateRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task One_Save_writes_a_new_standalone_sub_assembly_and_selects_it() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.HeaderTitle.Should().Be("New sub-assembly");
        state.EditingRole!.Name.Should().Be("Glaze", "a new form takes the first role");
        state.EditingName = "Chocolate Glaze";
        state.Batch.Lines.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 200m });
        state.Batch.BatchWeight.Should().Be(200m);
        state.Batch.BatchCost.Should().Be(18m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("Chocolate Glaze was saved.");
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.SubAssemblies.SingleAsync(Cancellation);
            saved.Name.Should().Be("Chocolate Glaze");
            (await SubAssemblyTotalsAsync(saved.Id)).TotalCost.Should().Be(18m);
        }

        var row = state.View.List.Rows.Should().ContainSingle().Subject;
        state.SelectedRow.Should().BeSameAs(row);
        state.Batch.BatchWeight.Should().Be(200m);
        state.HeaderTitle.Should().Be("Chocolate Glaze");
        state.Batch.Lines.Should().ContainSingle().Which.Grams.Should().Be(200m);
    });

    [Test]
    public Task Saving_without_lines_is_refused_with_a_message() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Glaze");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.EditingName = "Chocolate Glaze";
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Add the ingredients that go into Chocolate Glaze, in grams.");
        state.View.List.Rows.Should().BeEmpty();
    });

    [Test]
    public Task Reloading_keeps_unsaved_lines_and_reprices_them() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.View.List.Rows.Single());
        await Settle.UntilAsync(state, () => state.Batch.Lines.Count == 1, () => $"lines={state.Batch.Lines.Count}");

        state.Batch.Lines.Single().Grams = 300m;
        await using (var dbContext = CreateDbContext())
        {
            var sugar = await dbContext.RawMaterials.SingleAsync(m => m.Id == sugarId, Cancellation);
            sugar.CostPerBaseUnit = 0.10m;
            sugar.BufferedCostPerBaseUnit = 0.10m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await state.LoadCommand.ExecuteAsync();

        state.Batch.Lines.Should().ContainSingle().Which.Grams.Should().Be(300m, "an unsaved edit survives the refresh");
        state.Batch.BatchCost.Should().Be(30m, "the line is priced at sugar's new cost");
        state.SelectedRow!.Name.Should().Be("Glaze");
    });

    [Test]
    public Task Deleting_a_linked_one_is_refused_and_the_form_shows_who_uses_it() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        await SaveRecipeAsync(await AddProductAsync("Cream Loaf"), new RecipeDraft([], [new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m)]));
        var state = CreateState();
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        await state.LoadCommand.ExecuteAsync();

        state.View.List.Rows.Single().DependentsText.Should().Be("Cream Loaf");
        state.Pick(state.View.List.Rows.Single());
        await Settle.UntilAsync(state, () => state.Batch.Lines.Count == 1, () => $"lines={state.Batch.Lines.Count}");
        var creamLoaf = state.Dependents.Products.Should().ContainSingle().Subject;
        creamLoaf.Name.Should().Be("Cream Loaf");
        int? opened = null;
        using (state.Dependents.ProductOpenRequests.Subscribe(productId => opened = productId))
        {
            await state.Dependents.OpenProductCommand.ExecuteAsync(creamLoaf.Id);
        }

        opened.Should().Be(creamLoaf.Id, "a tile hands its product's id on, for the main window to open it");
        state.Dependents.Text.Should().StartWith("These products follow every change");

        await state.DeleteCommand.ExecuteAsync();

        state.ErrorMessage.Should().StartWith("Cream Loaf links Glaze.");
        state.View.List.Rows.Should().ContainSingle();
    });

    [Test]
    public Task OpenSubAssembly_waits_for_a_list_not_loaded_yet_and_clears_a_search_hiding_it() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var repository = new SubAssemblyRepository(CreateDbContextFactory());
        var chocolateId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Chocolate Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var ubeId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var state = CreateState();

        // As a linked badge on Products does before the Sub-Assemblies screen was ever shown.
        state.OpenRecord(ubeId);
        state.SelectedRow.Should().BeNull("the list is empty until the screen loads");

        await state.LoadCommand.ExecuteAsync();

        state.SelectedRow!.Id.Should().Be(ubeId);
        await Settle.UntilAsync(state, () => state.EditingName == "Ube Glaze", () => $"name={state.EditingName}");

        state.View.List.SearchText = "Chocolate";
        state.OpenRecord(chocolateId);
        state.SelectedRow!.Id.Should().Be(chocolateId);
        state.View.List.SearchText.Should().Be("Chocolate", "a search showing it is left alone");

        state.OpenRecord(ubeId);
        state.SelectedRow!.Id.Should().Be(ubeId);
        state.View.List.SearchText.Should().BeEmpty("a search hiding it is cleared, so the list shows the row picked");
        state.View.List.VisibleRows.Should().Contain(state.SelectedRow);
    });

    [Test]
    public Task OpenByName_opens_the_sub_assembly_a_name_means_best_as_the_Vim_command_line_asks() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var repository = new SubAssemblyRepository(CreateDbContextFactory());
        await repository.SaveStandaloneAsync(new SubAssembly { Name = "Chocolate Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var ubeId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.OpenByName("ube").Should().BeTrue();

        state.SelectedRow!.Id.Should().Be(ubeId);
        state.OpenByName("fondant").Should().BeFalse();
        await Settle.UntilAsync(state, () => state.EditingName == "Ube Glaze", () => $"name={state.EditingName}");
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

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.IsPackaging.Should().BeFalse("a new form takes the first role, Glaze");

        state.EditingRole = state.Roles.Single(r => r.IsPackaging);
        state.IsPackaging.Should().BeTrue();
        state.EditingName = "Gift Box Set";
        state.PackagingSet.Lines.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 2m });
        state.PackagingSet.Cost.Should().Be(66m, "two boxes at their buffered ₱33");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("Gift Box Set was saved.");
        await Settle.UntilAsync(state, () => state.PackagingSet.Lines.Count == 1, () => $"packaging lines={state.PackagingSet.Lines.Count}");
        state.IsPackaging.Should().BeTrue();
        state.Batch.Lines.Should().BeEmpty();
        state.PackagingSet.Lines.Single().BaseUnitAbbreviation.Should().Be("pc");
        state.PackagingSet.Cost.Should().Be(66m);
        state.Dependents.Text.Should().Contain("Add Sub-Assembly");
    });

    // A quantity reaches the line as it's typed, and Save has to be offered by then: the click on it is what leaves the
    // cell, and a Save still disabled under the cursor swallows that click (UndoHistory.IsDirty).
    [Test]
    public Task A_packaging_sets_quantity_being_typed_offers_Save_before_its_cell_is_left() => UiThread.RunAsync(async () =>
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

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.EditingRole = state.Roles.Single(r => r.IsPackaging);
        state.EditingName = "Gift Box Set";
        state.PackagingSet.Lines.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 2m });
        await state.SaveCommand.ExecuteAsync();
        await Settle.UntilAsync(
            state,
            () => state.PackagingSet.Lines.Count == 1 && !state.History.IsDirty,
            () => $"packaging lines={state.PackagingSet.Lines.Count}, dirty={state.History.IsDirty}");

        // The cell takes the cursor, and a key is typed: its step is still open.
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.PackagingSet.Lines.Single().QtyPerUnit = 3m;

        state.PackagingSet.Cost.Should().Be(99m);
        state.History.IsDirty.Should().BeTrue("Save is offered while the quantity is still being typed");
        recorder.EndUndoStep();
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.View.List.Rows.Single());
        await Settle.UntilAsync(state, () => state.Batch.Lines.Count == 2, () => $"lines={state.Batch.Lines.Count}");

        await state.CopyCommand.ExecuteAsync();

        state.SelectedRow.Should().BeNull("the copy isn't in the list until it's saved");
        state.HeaderTitle.Should().Be("Copy of House Glaze");
        state.EditingName.Should().Be("House Glaze Copy");
        state.EditingRole!.Name.Should().Be("Glaze", "the copy keeps the original's role, not a new form's first one");
        state.Batch.Lines.Select(l => (l.IngredientId, l.Grams, l.IsFormulaBase)).Should().Equal((flourId, 100m, true), (sugarId, 50m, false));
        state.Batch.BatchCost.Should().Be(9.5m);
        state.Dependents.Products.Should().BeEmpty();
        state.History.IsDirty.Should().BeTrue("Save is offered straight away");
        state.History.CanUndo.Should().BeFalse();

        state.EditingName = "Ube Glaze";
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.View.List.Rows.Select(r => r.Name).Should().Equal("House Glaze", "Ube Glaze");
        state.SelectedRow!.Name.Should().Be("Ube Glaze");
        state.SelectedRow.Id.Should().NotBe(originalId);
        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == originalId).CountAsync(Cancellation);
        lines.Should().Be(2, "the original is left as it was");
        (await dbContext.SubAssemblyLines.CountAsync(l => l.SubAssemblyId == state.SelectedRow.Id, Cancellation)).Should().Be(2);
    });

    [Test]
    public Task A_copys_name_is_one_no_other_sub_assembly_has() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var repository = new SubAssemblyRepository(CreateDbContextFactory());
        await repository.SaveStandaloneAsync(new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        await repository.SaveStandaloneAsync(new SubAssembly { Name = "Glaze Copy", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.View.List.Rows.Single(r => r.Name == "Glaze"));
        await Settle.UntilAsync(state, () => state.EditingName == "Glaze" && state.Batch.Lines.Count == 1, () => $"name={state.EditingName}");

        await state.CopyCommand.ExecuteAsync();
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.View.List.Rows.Select(r => r.Name).Should().Equal("Glaze", "Glaze Copy", "Glaze Copy 2");
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.View.List.Rows.Single());
        await Settle.UntilAsync(state, () => state.PackagingSet.Lines.Count == 1, () => $"packaging lines={state.PackagingSet.Lines.Count}");

        await state.CopyCommand.ExecuteAsync();

        state.IsPackaging.Should().BeTrue();
        state.EditingName.Should().Be("Gift Set Copy");
        state.PackagingSet.Lines.Should().ContainSingle().Which.QtyPerUnit.Should().Be(2m);
        state.PackagingSet.Cost.Should().Be(66m);
        state.Dependents.Text.Should().Contain("Add Sub-Assembly", "a set's own hint, for nothing uses the copy yet");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.View.List.Rows.Select(r => r.Name).Should().Equal("Gift Set", "Gift Set Copy");
    });

    [Test]
    public Task New_Product_asks_for_a_product_linking_the_picked_sub_assembly_once_its_changes_are_saved() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        var requested = new List<int>();
        using var _ = state.NewProductRequests.Subscribe(requested.Add);

        state.NewProductCommand.CanExecute().Should().BeFalse("nothing is picked");

        state.Pick(state.View.List.Rows.Single());
        await Settle.UntilAsync(state, () => state.Batch.Lines.Count == 1, () => $"lines={state.Batch.Lines.Count}");
        state.NewProductCommand.CanExecute().Should().BeTrue();
        state.EditingName = "Sugar Glaze";
        await state.NewProductCommand.ExecuteAsync();

        requested.Should().BeEmpty("the product would link the glaze as it's saved, not as the form shows it");
        state.ErrorMessage.Should().Be("Save Glaze first, or undo its changes: the new product links it as it's saved.");

        state.EditingName = "Glaze";
        await state.NewProductCommand.ExecuteAsync();

        requested.Should().Equal(glazeId);
        state.ErrorMessage.Should().BeNull();
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.Pick(state.View.List.Rows.Single());

        state.NewProductCommand.CanExecute().Should().BeFalse("a packaging set isn't a recipe");
    });

    private SubAssemblyForm CreateState()
    {
        var factory = CreateDbContextFactory();

        return SubAssembliesViews.Create(factory, CreateUnitOfWork()).Form!;
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.View.List.Rows.Single());
        await Settle.UntilAsync(state, () => state.Batch.Lines.Count == 2, () => $"lines={state.Batch.Lines.Count}");

        state.Batch.PercentHeader.Should().Be("Baker's %");
        state.Batch.Lines.Single(l => l.Ingredient!.Id == waterId).Percent = 62.5m;
        state.Batch.FormulaBaseCaption.Should().Contain("162.5");
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await Settle.UntilAsync(state, () => state.Batch.Lines.Count == 2, () => $"lines={state.Batch.Lines.Count}");
        var water = state.Batch.Lines.Single(l => l.Ingredient!.Id == waterId);
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.View.List.Rows.Single());
        await Settle.UntilAsync(state, () => state.Batch.Lines.Count == 2, () => $"lines={state.Batch.Lines.Count}");
        state.Batch.Lines.Single(l => l.Ingredient!.Id == waterId).Percent.Should().Be(500m / 1500m * 100m);

        state.Batch.Lines.Single(l => l.Ingredient!.Id == flourId).IsFormulaBase = false;

        state.Batch.PercentHeader.Should().Be("Absolute %");
        state.Batch.FormulaBaseCaption.Should().StartWith("Absolute %");
        state.Batch.Lines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(75m, "1,500 g of the 2,000 g batch");
        state.Batch.Lines.Single(l => l.Ingredient!.Id == waterId).Percent.Should().Be(25m);
    });

    [Test]
    public Task The_forms_version_goes_up_as_another_sub_assembly_or_a_new_one_takes_the_form() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var repository = new SubAssemblyRepository(CreateDbContextFactory());
        var chocolateId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Chocolate Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var ubeId = await repository.SaveStandaloneAsync(new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.OpenRecord(ubeId);
        await Settle.UntilAsync(state, () => state.EditingName == "Ube Glaze", () => $"name={state.EditingName}");
        var ube = state.FormVersion;

        // What the boxes hold is still the sub-assembly's: they keep the cursor.
        state.EditingName = "Ube Glaze, thick";
        await state.LoadCommand.ExecuteAsync();
        state.FormVersion.Should().Be(ube, "typing and a reload leave the form the same sub-assembly's");

        // The form follows the pick once its lines are read, and the version with it.
        state.OpenRecord(chocolateId);
        await Settle.UntilAsync(state, () => state.EditingName == "Chocolate Glaze", () => $"name={state.EditingName}");
        var chocolate = state.FormVersion;
        chocolate.Should().BeGreaterThan(ube);

        await state.NewCommand.ExecuteAsync();
        var fresh = state.FormVersion;
        fresh.Should().BeGreaterThan(chocolate);

        // New, then the browser's Back: the Name box had the cursor, and stayed blank over the sub-assembly's name.
        state.OpenRecord(ubeId);
        await Settle.UntilAsync(state, () => state.EditingName == "Ube Glaze", () => $"name={state.EditingName}");
        state.FormVersion.Should().BeGreaterThan(fresh);
    });

    [Test]
    public Task The_sub_assembly_in_the_screens_address_is_opened_once_the_load_is_back() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var ubeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var state = CreateState();

        // As the page does when it opens on /sub-assemblies/42, before the load it then runs.
        state.OpenRecordOnLoad(ubeId);
        state.SelectedRow.Should().BeNull("the list is empty until the screen loads");

        await state.LoadCommand.ExecuteAsync();

        state.SelectedRow!.Id.Should().Be(ubeId);
        await Settle.UntilAsync(state, () => state.EditingName == "Ube Glaze", () => $"name={state.EditingName}");
    });

    [Test]
    public Task New_clicked_while_the_screen_is_still_opening_the_sub_assembly_in_its_address_keeps_the_new_form() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var ubeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Ube Glaze", RoleId = roleId }, [(sugarId, 200m)], Cancellation);
        var state = CreateState();
        state.OpenRecordOnLoad(ubeId);
        var loading = state.LoadCommand.ExecuteAsync();

        // The load is still reading. Ube Glaze opened over the new form at its end, and the name typed was saved as its.
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Chocolate Glaze";
        await loading;
        await state.LoadCommand.ExecuteAsync();

        state.SelectedRow.Should().BeNull("New was the later word");
        state.HeaderTitle.Should().Be("New sub-assembly");
        state.EditingName.Should().Be("Chocolate Glaze");
        state.Batch.Lines.Should().BeEmpty("Ube Glaze's lines never loaded into it");
    });
}
