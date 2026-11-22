using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// Another PC on the same database saves the product this form has open. The form's Save is refused rather than write over
/// that save, what's typed stays, and there's a way back to what was saved; what this window saves itself never counts as
/// someone else's (SavedVersionRules, RecordScreenState.FollowSavedVersion).
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class ProductsStateSavedVersionTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_save_from_another_pc_refuses_the_forms_Save_each_time_and_what_is_typed_stays() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);
        TypeDescription(state, "Toasted and sugared");

        await AnotherPcRenamesAsync(id, "Mamon Classic");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Mamon Classic"));
        state.SuccessMessage.Should().BeNull();
        state.EditingName.Should().Be("Mamon");
        state.EditingDescription.Should().Be("Toasted and sugared");
        state.History.IsDirty.Should().BeTrue();
        (await StoredAsync(id)).Should().Be(("Mamon Classic", (string?)null));

        // The refusal read the list again: that doesn't make what's on the form the copy that's saved.
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Mamon Classic"));
        (await StoredAsync(id)).Should().Be(("Mamon Classic", (string?)null));
    });

    [Test]
    public Task A_load_leaves_what_is_typed_and_doesnt_let_it_save_over_a_save_made_since() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);
        TypeDescription(state, "Toasted and sugared");

        await AnotherPcRenamesAsync(id, "Mamon Classic");
        await state.LoadCommand.ExecuteAsync();

        state.EditingName.Should().Be("Mamon");
        state.EditingDescription.Should().Be("Toasted and sugared");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Mamon Classic"));
    });

    [Test]
    public Task Revert_after_a_refused_Save_shows_what_was_saved_and_the_next_Save_goes_through() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        TypeDescription(state, "Toasted and sugared");
        await AnotherPcRenamesAsync(id, "Mamon Classic");
        await state.SaveCommand.ExecuteAsync();

        await state.RevertCommand.ExecuteAsync();
        await ShowingAsync(state, id);

        state.ErrorMessage.Should().BeNull();
        state.EditingName.Should().Be("Mamon Classic");
        state.EditingDescription.Should().BeEmpty();
        state.History.IsDirty.Should().BeFalse();

        TypeDescription(state, "Toasted");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Mamon Classic", "Toasted"));
    });

    [Test]
    public Task Opening_the_product_again_without_saving_shows_what_was_saved_and_the_next_Save_goes_through() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var otherId = await SaveProductAsync("Ensaymada");
        var state = await OpenedStateAsync(id);
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        TypeDescription(state, "Toasted and sugared");
        await AnotherPcRenamesAsync(id, "Mamon Classic");
        await state.SaveCommand.ExecuteAsync();

        (await state.OpenRecordAsync(otherId)).Should().BeTrue();
        await ShowingAsync(state, otherId);
        state.ErrorMessage.Should().BeNull("what was said was about the changes thrown away");
        (await state.OpenRecordAsync(id)).Should().BeTrue();
        await ShowingAsync(state, id);

        state.EditingName.Should().Be("Mamon Classic");
        state.EditingDescription.Should().BeEmpty();

        TypeDescription(state, "Toasted");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Mamon Classic", "Toasted"));
    });

    [Test]
    public Task A_form_with_nothing_typed_takes_up_a_save_made_since_as_the_screen_loads() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);

        await AnotherPcRenamesAsync(id, "Mamon Classic");
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.EditingName == "Mamon Classic", () => $"name={state.EditingName}");

        state.History.IsDirty.Should().BeFalse();

        TypeDescription(state, "Toasted");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Mamon Classic", "Toasted"));
    });

    [Test]
    public Task A_form_with_nothing_typed_shows_its_open_section_as_saved_since_and_a_Save_keeps_it() => UiThread.RunAsync(async () =>
    {
        var toppingId = await AddRoleAsync("Topping", isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (id, sectionId) = await AddProductWithSubAssemblyAsync("Loaf", toppingId, [(flourId, 1000m)], weightPerPiece: 60m);
        var state = await OpenedStateAsync(id);
        state.UsageForm.IsUsageFormOpen.Should().BeTrue("a product opens on its first section");

        await AnotherPcSavesRecipeAsync(id, new RecipeDraft(
            [new NewSubAssemblyDraft(toppingId, [(flourId, 1000m), (butterId, 250m)], UsageAuthoredField.WeightPerPiece, 60m) { Id = sectionId }],
            []));
        await state.LoadCommand.ExecuteAsync();

        state.UsageForm.FormLines.Select(l => (l.IngredientId, l.Grams)).Should().Equal((flourId, 1000m), (butterId, 250m));
        state.History.IsDirty.Should().BeFalse("nothing was typed");

        // Save applies the section form left open, whatever on the product was changed.
        TypeDescription(state, "Toasted");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredLinesAsync(sectionId)).Should().Equal((flourId, 1000m), (butterId, 250m));
    });

    [Test]
    public Task Leaving_a_section_taken_up_as_saved_since_changes_nothing() => UiThread.RunAsync(async () =>
    {
        var toppingId = await AddRoleAsync("Topping", isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (id, sectionId) = await AddProductWithSubAssemblyAsync("Loaf", toppingId, [(flourId, 1000m)], weightPerPiece: 60m);
        var state = await OpenedStateAsync(id);

        await AnotherPcSavesRecipeAsync(id, new RecipeDraft(
            [new NewSubAssemblyDraft(toppingId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 60m) { Id = sectionId }],
            []));
        await state.LoadCommand.ExecuteAsync();
        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);

        state.Recipe.UsageEdits.Single().Lines.Select(l => l.Grams).Should().Equal(800m);
        state.History.IsDirty.Should().BeFalse("looking at another tab is no edit");
        state.History.CanUndo.Should().BeFalse();
    });

    [Test]
    public Task A_form_with_nothing_typed_shows_a_linked_sections_piece_size_and_add_ins_as_saved_since() => UiThread.RunAsync(async () =>
    {
        var doughId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        var (id, linkedId) = await AddProductWithSubAssemblyAsync("Loaf", doughId, [(flourId, 1000m)], weightPerPiece: 60m);
        var state = await OpenedStateAsync(id);

        await AnotherPcSavesRecipeAsync(id, new RecipeDraft(
            [], [new LinkedUsageDraft(linkedId, UsageAuthoredField.WeightPerPiece, 75m) { AddIns = [(ubeId, 50m)] }]));
        await state.LoadCommand.ExecuteAsync();

        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(75m);
        state.UsageForm.FormAddIns.Select(l => (l.IngredientId, l.Grams)).Should().Equal((ubeId, 50m));

        TypeDescription(state, "Toasted");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.SingleAsync(u => u.ProductId == id, Cancellation);
        usage.AuthoredValue.Should().Be(75m);
        (await dbContext.UsageAddInLines.Where(l => l.UsageId == usage.Id).Select(l => l.Grams).ToListAsync(Cancellation)).Should().Equal(50m);
    });

    [Test]
    public Task A_section_taken_out_since_stays_out_of_a_form_with_nothing_typed() => UiThread.RunAsync(async () =>
    {
        var toppingId = await AddRoleAsync("Topping", sortOrder: 1, isProductOwned: true);
        var fillingId = await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var id = await AddProductAsync("Loaf");
        await SaveRecipeAsync(id, new RecipeDraft(
            [
                new NewSubAssemblyDraft(toppingId, [(flourId, 100m)], UsageAuthoredField.Yield, 1m),
                new NewSubAssemblyDraft(fillingId, [(flourId, 200m)], UsageAuthoredField.Yield, 1m),
            ],
            []));
        var keptId = await SubAssemblyIdAsync(id, fillingId);
        var state = await OpenedStateAsync(id);
        state.UsageForm.EditingUsage!.RoleId.Should().Be(toppingId, "the topping's tab shows");

        await AnotherPcSavesRecipeAsync(id, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingId, [(flourId, 200m)], UsageAuthoredField.Yield, 1m) { Id = keptId }],
            []));
        await state.LoadCommand.ExecuteAsync();

        state.Recipe.UsageEdits.Select(u => u.RoleId).Should().Equal(fillingId);
        state.UsageForm.FormLines.Select(l => l.Grams).Should().Equal(200m);
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task The_sources_tab_takes_up_what_its_variant_saved_into_its_own_section_and_a_Save_keeps_it() => UiThread.RunAsync(async () =>
    {
        var (source, variant, sectionId, flourId, butterId) = await SourceAndVariantTabsAsync();

        await SaveButterIntoTheSectionAsync(variant, butterId);

        source.View.Show(source.View.Tabs.Holding(source.SelectedProduct!.Id)!);
        source.UsageForm.FormLines.Select(l => (l.IngredientId, l.Grams)).Should().Equal((flourId, 1000m), (butterId, 250m));
        source.History.IsDirty.Should().BeFalse("nothing was typed");

        source.UsageForm.TypeWeightPerPiece(70m);
        await source.SaveCommand.ExecuteAsync();

        source.ErrorMessage.Should().BeNull();
        (await StoredLinesAsync(sectionId)).Should().Equal((flourId, 1000m), (butterId, 250m));
    });

    [Test]
    public Task The_sources_tab_with_changes_is_refused_over_what_its_variant_saved_into_its_own_section() => UiThread.RunAsync(async () =>
    {
        var (source, variant, sectionId, flourId, butterId) = await SourceAndVariantTabsAsync();
        TypeDescription(source, "Toasted");

        await SaveButterIntoTheSectionAsync(variant, butterId);
        await source.SaveCommand.ExecuteAsync();

        source.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Loaf"));
        source.EditingDescription.Should().Be("Toasted", "what's typed stays");
        (await StoredLinesAsync(sectionId)).Should().Equal((flourId, 1000m), (butterId, 250m));
    });

    [Test]
    public Task A_rename_from_the_screens_own_list_doesnt_refuse_what_is_typed() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);
        TypeDescription(state, "Toasted and sugared");

        await RenameFromTheListAsync(state, id, "Mamon Tostado");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Mamon Tostado", "Toasted and sugared"));
    });

    [Test]
    public Task A_move_from_the_screens_own_list_doesnt_refuse_what_is_typed() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);
        TypeDescription(state, "Toasted and sugared");

        // Promoting it, then moving it back: the two ways a product moves, neither of which loads the screen again.
        await state.View.List.MoveToStageCommand.ExecuteAsync(new StageMoveRequest([state.SelectedProduct!], ProductStage.Production));
        (await StoredStageAsync(id)).Should().Be(ProductStage.Production);
        await state.View.List.MoveProductsCommand.ExecuteAsync([new ProductMove(id, ProductStage.Innovation, null)]);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Mamon", "Toasted and sugared"));
        (await StoredStageAsync(id)).Should().Be(ProductStage.Innovation);
    });

    [Test]
    public Task What_is_typed_after_a_move_from_the_screens_own_list_saves() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);

        await state.View.List.MoveToStageCommand.ExecuteAsync(new StageMoveRequest([state.SelectedProduct!], ProductStage.Production));
        await ShowingAsync(state, id);
        TypeDescription(state, "Toasted");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Mamon", "Toasted"));
    });

    [Test]
    public Task A_rename_from_the_list_is_refused_over_a_save_from_another_pc_and_what_is_typed_stays() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);
        TypeDescription(state, "Toasted and sugared");

        await AnotherPcRenamesAsync(id, "Mamon Classic");
        await RenameFromTheListAsync(state, id, "Mamon Tostado");

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Mamon Classic"));
        state.EditingDescription.Should().Be("Toasted and sugared");
        (await StoredAsync(id)).Should().Be(("Mamon Classic", (string?)null));

        // The form's Save is refused in its turn: the refused rename took up nothing.
        state.View.List.CancelTreeName();
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Mamon Classic"));
    });

    [Test]
    public Task A_move_from_the_list_is_refused_over_a_save_from_another_pc_and_nothing_moves() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);
        TypeDescription(state, "Toasted and sugared");

        await AnotherPcRenamesAsync(id, "Mamon Classic");
        await state.View.List.MoveToStageCommand.ExecuteAsync(new StageMoveRequest([state.SelectedProduct!], ProductStage.Production));

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Mamon Classic"));
        state.EditingDescription.Should().Be("Toasted and sugared");
        (await StoredStageAsync(id)).Should().Be(ProductStage.Innovation);
    });

    [Test]
    public Task What_the_list_does_elsewhere_doesnt_let_what_is_typed_over_a_save_from_another_pc() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var otherId = await SaveProductAsync("Ensaymada");
        var folder = new ProductFolder { Name = "Breads", Stage = ProductStage.Innovation };
        await new ProductFolderRepository(CreateDbContextFactory()).AddAsync(folder, Cancellation);
        var state = await OpenedStateAsync(id);
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        TypeDescription(state, "Toasted and sugared");

        await AnotherPcRenamesAsync(id, "Mamon Classic");

        // Another product moved, and a folder deleted, which reads the screen's lists again.
        await state.View.List.MoveProductsCommand.ExecuteAsync([new ProductMove(otherId, ProductStage.Production, null)]);
        state.ErrorMessage.Should().BeNull("the move is of another product, which nobody has saved");
        await state.View.List.DeleteFolderCommand.ExecuteAsync(state.View.List.ProductTree.Folder(folder.Id)!);
        state.ErrorMessage.Should().BeNull();

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Mamon Classic"));
        (await StoredAsync(id)).Should().Be(("Mamon Classic", (string?)null));
    });

    [Test]
    public Task A_rename_with_nothing_typed_puts_what_another_pc_saved_on_the_form() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);

        await AnotherPcDescribesAsync(id, "Saved on the other PC");
        await RenameFromTheListAsync(state, id, "Mamon Tostado");
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.EditingDescription == "Saved on the other PC", () => $"description={state.EditingDescription}");

        state.ErrorMessage.Should().BeNull();
        state.EditingName.Should().Be("Mamon Tostado");
        state.History.IsDirty.Should().BeFalse();

        TypeDescription(state, "Saved on this one");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Mamon Tostado", "Saved on this one"));
    });

    [Test]
    public Task A_product_no_save_has_stamped_is_refused_over_the_first_save_from_another_pc() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, (DateTime?)null), Cancellation);
        }

        var state = await OpenedStateAsync(id);
        TypeDescription(state, "Toasted and sugared");

        await AnotherPcRenamesAsync(id, "Mamon Classic");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Mamon Classic"));
        (await StoredAsync(id)).Should().Be(("Mamon Classic", (string?)null));
    });

    [Test]
    public Task What_the_screen_said_stays_while_the_product_shows_and_goes_when_another_opens() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var otherId = await SaveProductAsync("Ensaymada");
        var state = await OpenedStateAsync(id);

        ((IScreenPartHost)state).ShowError("Mamon is in use.");
        await state.LoadCommand.ExecuteAsync();
        state.ErrorMessage.Should().Be("Mamon is in use.");

        state.OpenRecord(otherId);
        await ShowingAsync(state, otherId);

        state.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task The_forms_own_saves_follow_one_another() => UiThread.RunAsync(async () =>
    {
        var id = await SaveProductAsync("Mamon");
        var state = await OpenedStateAsync(id);

        TypeDescription(state, "Toasted");
        await state.SaveCommand.ExecuteAsync();
        await ShowingAsync(state, id);
        TypeDescription(state, "Toasted and sugared");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Mamon", "Toasted and sugared"));
    });

    /// <summary>One field typed and left, as the form brackets it in the app.</summary>
    private static void TypeDescription(ProductForm state, string description)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.EditingDescription = description;
        recorder.EndUndoStep();
    }

    private static Task ShowingAsync(ProductForm state, int id) =>
        Settle.UntilAsync(state, () => state.AreDetailsReady && state.SelectedProduct?.Id == id, () => $"ready={state.AreDetailsReady}");

    private static async Task RenameFromTheListAsync(ProductForm state, int id, string name)
    {
        await state.View.List.RenameProductCommand.ExecuteAsync((ProductLeafNode)state.View.List.ProductTree.Find(id)!);
        await state.View.List.CommitTreeNameAsync(name);
    }

    private Task<int> SaveProductAsync(string name) =>
        new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = name, Stage = ProductStage.Innovation }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    private async Task<ProductStage> StoredStageAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Products.Where(p => p.Id == id).Select(p => p.Stage).SingleAsync(Cancellation);
    }

    /// <summary>A save from another PC on this database: its own read of the product, its own Save.</summary>
    private async Task AnotherPcRenamesAsync(int id, string name)
    {
        var products = new ProductRepository(CreateDbContextFactory());
        var theirs = (await products.GetByIdAsync(id, Cancellation))!;
        theirs.Name = name;
        await products.SaveAsync(theirs, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
    }

    private async Task AnotherPcDescribesAsync(int id, string description)
    {
        var products = new ProductRepository(CreateDbContextFactory());
        var theirs = (await products.GetByIdAsync(id, Cancellation))!;
        theirs.Description = description;
        await products.SaveAsync(theirs, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
    }

    /// <summary>The product's recipe saved from another PC on this database, over the product as saved now.</summary>
    private async Task AnotherPcSavesRecipeAsync(int id, RecipeDraft recipe)
    {
        var products = new ProductRepository(CreateDbContextFactory());
        var theirs = (await products.GetByIdAsync(id, Cancellation))!;
        await products.SaveAsync(theirs, recipe, packagingLink: null, packagingLines: null, costing: null, Cancellation);
    }

    private async Task<IReadOnlyList<(int? IngredientId, decimal? Grams)>> StoredLinesAsync(int subAssemblyId)
    {
        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == subAssemblyId).OrderBy(l => l.SortOrder).ToListAsync(Cancellation);
        return [.. lines.Select(l => ((int?)l.IngredientId, (decimal?)l.Grams))];
    }

    /// <summary>A product with a section of its own, and its variant, each in a tab of the one window.</summary>
    private async Task<(ProductForm Source, ProductForm Variant, int SectionId, int FlourId, int ButterId)> SourceAndVariantTabsAsync()
    {
        var toppingId = await AddRoleAsync("Topping", isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (sourceId, sectionId) = await AddProductWithSubAssemblyAsync("Loaf", toppingId, [(flourId, 1000m)], weightPerPiece: 60m);
        var variantId = await new ProductCloner(CreateDbContextFactory()).CreateVariantAsync(sourceId, "Loaf Mini", Cancellation);

        var source = await OpenedStateAsync(sourceId);
        var view = source.View;
        view.Keep(view.Tabs.Active!);
        view.OpenRecord(variantId);
        var variant = view.Form!;
        await ShowingAsync(variant, variantId);
        return (source, variant, sectionId, flourId, butterId);
    }

    private static async Task SaveButterIntoTheSectionAsync(ProductForm variant, int butterId)
    {
        variant.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        variant.UsageForm.IsEditingLinkedUsage.Should().BeTrue("the variant links its source's section");
        variant.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 250m });
        await variant.SaveCommand.ExecuteAsync();
        variant.ErrorMessage.Should().BeNull();
    }

    private async Task<(string Name, string? Description)> StoredAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        var product = await dbContext.Products.SingleAsync(p => p.Id == id, Cancellation);
        return (product.Name, product.Description);
    }

    /// <summary>The screen with <paramref name="id"/> open on the form, its details in.</summary>
    private async Task<ProductForm> OpenedStateAsync(int id)
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.OpenRecord(id);
        await ShowingAsync(state, id);
        return state;
    }

    private ProductForm CreateState()
    {
        var factory = CreateDbContextFactory();

        return ProductsViews.Create(factory, CreateUnitOfWork()).Form!;
    }
}
