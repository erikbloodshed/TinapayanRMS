using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.RawMaterials;

/// <summary>
/// Another PC on the same database saves the raw material this form has open. The form's Save is refused rather than write
/// over that save, what's typed stays, and opening the material again shows what was saved; what this window saves itself
/// never counts as someone else's (SavedVersionRules, RecordScreenState.FollowSavedVersion).
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class RawMaterialsStateSavedVersionTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_save_from_another_pc_refuses_the_forms_Save_each_time_and_what_is_typed_stays() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var state = await OpenedStateAsync(id);
        TypeBuffer(state, 5m);

        await AnotherPcRenamesAsync(id, "Strong Flour");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Strong Flour"));
        state.SuccessMessage.Should().BeNull();
        state.EditingName.Should().Be("Bread Flour");
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(5m);
        state.History.IsDirty.Should().BeTrue();
        (await StoredAsync(id)).Should().Be(("Strong Flour", 0m));

        // The refusal read the list again: that doesn't make what's on the form the copy that's saved.
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Strong Flour"));
        (await StoredAsync(id)).Should().Be(("Strong Flour", 0m));
    });

    [Test]
    public Task Opening_the_material_again_without_saving_shows_what_was_saved_and_the_next_Save_goes_through() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var otherId = await SaveMaterialAsync("Cake Flour");
        var state = await OpenedStateAsync(id);
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        TypeBuffer(state, 5m);
        await AnotherPcRenamesAsync(id, "Strong Flour");
        await state.SaveCommand.ExecuteAsync();

        (await state.OpenRecordAsync(otherId)).Should().BeTrue();
        await ShowingAsync(state, otherId);
        (await state.OpenRecordAsync(id)).Should().BeTrue();
        await ShowingAsync(state, id);

        state.EditingName.Should().Be("Strong Flour");
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(0m);

        TypeBuffer(state, 7m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Strong Flour", 7m));
    });

    [Test]
    public Task Leaving_the_screen_without_saving_puts_the_form_on_what_was_saved_and_the_next_Save_goes_through() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var state = await OpenedStateAsync(id);
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        TypeBuffer(state, 5m);
        await AnotherPcRenamesAsync(id, "Strong Flour");
        await state.SaveCommand.ExecuteAsync();

        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.EditingName == "Strong Flour", () => $"name={state.EditingName}");

        state.History.IsDirty.Should().BeFalse();

        TypeBuffer(state, 7m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Strong Flour", 7m));
    });

    [Test]
    public Task A_form_with_nothing_typed_takes_up_a_save_made_since_as_the_screen_loads() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var state = await OpenedStateAsync(id);

        await AnotherPcRenamesAsync(id, "Strong Flour");
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.EditingName == "Strong Flour", () => $"name={state.EditingName}");

        state.History.IsDirty.Should().BeFalse();

        TypeBuffer(state, 5m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Strong Flour", 5m));
    });

    [Test]
    public Task A_rename_from_the_screens_own_list_doesnt_refuse_what_is_typed() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var state = await OpenedStateAsync(id);
        TypeBuffer(state, 5m);

        await RenameFromTheListAsync(state, id, "Strong Flour");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Strong Flour", 5m));
    });

    [Test]
    public Task A_move_from_the_screens_own_list_doesnt_refuse_what_is_typed() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var flourId = await AddCategoryAsync("Flours");
        var state = await OpenedStateAsync(id);
        TypeBuffer(state, 5m);

        await state.List.MoveMaterialsCommand.ExecuteAsync([new RawMaterialMove(id, flourId)]);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Bread Flour", 5m));
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == id, Cancellation)).CategoryId.Should().Be(flourId);
    });

    [Test]
    public Task A_rename_from_the_list_is_refused_over_a_save_from_another_pc_and_what_is_typed_stays() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var state = await OpenedStateAsync(id);
        TypeBuffer(state, 5m);

        await AnotherPcRenamesAsync(id, "Strong Flour");
        await RenameFromTheListAsync(state, id, "Cake Flour");

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Strong Flour"));
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(5m);
        (await StoredAsync(id)).Should().Be(("Strong Flour", 0m));
    });

    [Test]
    public Task A_move_from_the_list_is_refused_over_a_save_from_another_pc_and_nothing_moves() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var flourId = await AddCategoryAsync("Flours");
        var state = await OpenedStateAsync(id);
        TypeBuffer(state, 5m);

        await AnotherPcRenamesAsync(id, "Strong Flour");
        await state.List.MoveMaterialsCommand.ExecuteAsync([new RawMaterialMove(id, flourId)]);

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Strong Flour"));
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(m => m.Id == id, Cancellation)).CategoryId.Should().NotBe(flourId);
    });

    [Test]
    public Task Moving_another_material_from_the_list_doesnt_let_what_is_typed_over_a_save_from_another_pc() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var otherId = await SaveMaterialAsync("Cake Flour");
        var flourId = await AddCategoryAsync("Flours");
        var state = await OpenedStateAsync(id);
        TypeBuffer(state, 5m);

        await AnotherPcRenamesAsync(id, "Strong Flour");
        await state.List.MoveMaterialsCommand.ExecuteAsync([new RawMaterialMove(otherId, flourId)]);
        state.ErrorMessage.Should().BeNull("the move is of another material, which nobody has saved");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Strong Flour"));
        (await StoredAsync(id)).Should().Be(("Strong Flour", 0m));
    });

    [Test]
    public Task A_material_no_save_has_stamped_is_refused_over_the_first_save_from_another_pc() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.UpdatedAt, (DateTime?)null), Cancellation);
        }

        var state = await OpenedStateAsync(id);
        TypeBuffer(state, 5m);

        await AnotherPcRenamesAsync(id, "Strong Flour");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Strong Flour"));
        (await StoredAsync(id)).Should().Be(("Strong Flour", 0m));
    });

    [Test]
    public Task The_forms_own_saves_follow_one_another() => UiThread.RunAsync(async () =>
    {
        var id = await SaveMaterialAsync("Bread Flour");
        var state = await OpenedStateAsync(id);

        TypeBuffer(state, 5m);
        await state.SaveCommand.ExecuteAsync();
        await ShowingAsync(state, id);
        TypeBuffer(state, 7m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Bread Flour", 7m));
    });

    /// <summary>One field typed and left, as the form brackets it in the app.</summary>
    private static void TypeBuffer(RawMaterialsState state, decimal percent)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.PurchaseCost.EditingCostBufferPercent = percent;
        recorder.EndUndoStep();
    }

    private static Task ShowingAsync(RawMaterialsState state, int id) =>
        Settle.UntilAsync(state, () => state.AreDetailsReady && state.SelectedRawMaterial?.Id == id, () => $"ready={state.AreDetailsReady}");

    private static async Task RenameFromTheListAsync(RawMaterialsState state, int id, string name)
    {
        await state.List.RenameMaterialCommand.ExecuteAsync((RawMaterialLeafNode)state.List.RawMaterialTree.Find(id)!);
        await state.List.CommitTreeNameAsync(name);
    }

    /// <summary>A purchased ingredient saved as a form saves one, which stamps when it was saved.</summary>
    private async Task<int> SaveMaterialAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Dry Goods", Cancellation)
            ?? new RawMaterialCategory { Name = "Dry Goods", Type = RawMaterialCategoryType.Ingredient };
        if (category.Id == 0)
        {
            dbContext.RawMaterialCategories.Add(category);
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var material = new RawMaterial { Name = name, Type = RawMaterialType.Ingredient, CategoryId = category.Id, Source = RawMaterialSource.Purchased };
        return await new RawMaterialRepository(CreateDbContextFactory()).SaveAsync(material, [], brandPrices: null, addIns: null, Cancellation);
    }

    private async Task<int> AddCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = name, Type = RawMaterialCategoryType.Ingredient };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    /// <summary>A save from another PC on this database: its own read of the material, its own Save.</summary>
    private async Task AnotherPcRenamesAsync(int id, string name)
    {
        var materials = new RawMaterialRepository(CreateDbContextFactory());
        var theirs = (await materials.GetByIdAsync(id, Cancellation))!;
        theirs.Name = name;
        await materials.SaveAsync(theirs, [], brandPrices: null, addIns: null, Cancellation);
    }

    private async Task<(string Name, decimal BufferPercent)> StoredAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        var material = await dbContext.RawMaterials.SingleAsync(m => m.Id == id, Cancellation);
        return (material.Name, material.CostBufferPercent);
    }

    /// <summary>The screen with <paramref name="id"/> open on the form, its details in.</summary>
    private async Task<RawMaterialsState> OpenedStateAsync(int id)
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.OpenRecord(id);
        await ShowingAsync(state, id);
        return state;
    }

    private RawMaterialsState CreateState()
    {
        var factory = CreateDbContextFactory();

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
            new LoggerConfiguration().CreateLogger());
    }
}
