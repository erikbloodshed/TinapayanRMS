using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
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
