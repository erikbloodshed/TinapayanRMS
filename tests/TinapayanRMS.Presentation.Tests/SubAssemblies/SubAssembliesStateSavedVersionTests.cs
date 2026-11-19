using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.SubAssemblies;

/// <summary>
/// Another PC on the same database saves the sub-assembly this form has open. The form's Save is refused rather than write
/// over that save, what's typed stays, and opening the sub-assembly again shows what was saved; what this window saves itself
/// never counts as someone else's (SavedVersionRules, RecordScreenState.FollowSavedVersion).
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class SubAssembliesStateSavedVersionTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_save_from_another_pc_refuses_the_forms_Save_each_time_and_what_is_typed_stays() => UiThread.RunAsync(async () =>
    {
        var (id, flourId) = await AddRecipeAsync("Dough");
        var state = await OpenedStateAsync(id);
        TypeGrams(state, 250m);

        await AnotherPcSavesAsync(id, flourId, "Sweet Dough", 200m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Sweet Dough"));
        state.SuccessMessage.Should().BeNull();
        state.EditingName.Should().Be("Dough");
        state.Batch.Lines.Single().Grams.Should().Be(250m);
        state.History.IsDirty.Should().BeTrue();
        (await StoredAsync(id)).Should().Be(("Sweet Dough", 200m));

        // The refusal read the list again: that doesn't make what's on the form the copy that's saved.
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Sweet Dough"));
        (await StoredAsync(id)).Should().Be(("Sweet Dough", 200m));
    });

    [Test]
    public Task Opening_the_sub_assembly_again_without_saving_shows_what_was_saved_and_the_next_Save_goes_through() => UiThread.RunAsync(async () =>
    {
        var (id, flourId) = await AddRecipeAsync("Dough");
        var (otherId, _) = await AddRecipeAsync("Filling");
        var state = await OpenedStateAsync(id);
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        TypeGrams(state, 250m);
        await AnotherPcSavesAsync(id, flourId, "Sweet Dough", 200m);
        await state.SaveCommand.ExecuteAsync();

        (await state.OpenRecordAsync(otherId)).Should().BeTrue();
        await ShowingAsync(state, "Filling", 100m);
        state.ErrorMessage.Should().BeNull("what was said was about the changes thrown away");
        (await state.OpenRecordAsync(id)).Should().BeTrue();
        await ShowingAsync(state, "Sweet Dough", 200m);

        TypeGrams(state, 300m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Sweet Dough", 300m));
    });

    [Test]
    public Task Leaving_the_screen_without_saving_puts_the_form_on_what_was_saved_and_the_next_Save_goes_through() => UiThread.RunAsync(async () =>
    {
        var (id, flourId) = await AddRecipeAsync("Dough");
        var state = await OpenedStateAsync(id);
        state.ConfirmUnsavedChanges.RegisterHandler(context => context.SetOutput(UnsavedChangesAnswer.DontSave));
        TypeGrams(state, 250m);
        await AnotherPcSavesAsync(id, flourId, "Sweet Dough", 200m);
        await state.SaveCommand.ExecuteAsync();

        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();
        await ShowingAsync(state, "Sweet Dough", 200m);

        state.ErrorMessage.Should().BeNull("what was said was about the changes thrown away");
        state.History.IsDirty.Should().BeFalse();

        TypeGrams(state, 300m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Sweet Dough", 300m));
    });

    [Test]
    public Task A_form_with_nothing_typed_takes_up_a_save_made_since_as_the_screen_loads() => UiThread.RunAsync(async () =>
    {
        var (id, flourId) = await AddRecipeAsync("Dough");
        var state = await OpenedStateAsync(id);

        await AnotherPcSavesAsync(id, flourId, "Sweet Dough", 200m);
        await state.LoadCommand.ExecuteAsync();
        await ShowingAsync(state, "Sweet Dough", 200m);

        state.History.IsDirty.Should().BeFalse();

        TypeGrams(state, 300m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Sweet Dough", 300m));
    });

    [Test]
    public Task A_rename_from_the_screens_own_list_doesnt_refuse_what_is_typed() => UiThread.RunAsync(async () =>
    {
        var (id, _) = await AddRecipeAsync("Dough");
        var state = await OpenedStateAsync(id);
        TypeGrams(state, 250m);

        await RenameFromTheListAsync(state, id, "Sweet Dough");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Sweet Dough", 250m));
    });

    [Test]
    public Task A_move_from_the_screens_own_list_doesnt_refuse_what_is_typed() => UiThread.RunAsync(async () =>
    {
        var (id, _) = await AddRecipeAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling Role", sortOrder: 2);
        var state = await OpenedStateAsync(id);
        TypeGrams(state, 250m);

        await state.View.List.MoveSubAssembliesCommand.ExecuteAsync([new SubAssemblyMove(id, fillingRoleId)]);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Dough", 250m));
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.SingleAsync(s => s.Id == id, Cancellation)).RoleId.Should().Be(fillingRoleId);
    });

    [Test]
    public Task A_rename_from_the_list_is_refused_over_a_save_from_another_pc_and_what_is_typed_stays() => UiThread.RunAsync(async () =>
    {
        var (id, flourId) = await AddRecipeAsync("Dough");
        var state = await OpenedStateAsync(id);
        TypeGrams(state, 250m);

        await AnotherPcSavesAsync(id, flourId, "Sweet Dough", 200m);
        await RenameFromTheListAsync(state, id, "Soft Dough");

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Sweet Dough"));
        state.Batch.Lines.Single().Grams.Should().Be(250m);
        (await StoredAsync(id)).Should().Be(("Sweet Dough", 200m));
    });

    [Test]
    public Task A_move_from_the_list_is_refused_over_a_save_from_another_pc_and_nothing_moves() => UiThread.RunAsync(async () =>
    {
        var (id, flourId) = await AddRecipeAsync("Dough");
        var fillingRoleId = await AddRoleAsync("Filling Role", sortOrder: 2);
        var state = await OpenedStateAsync(id);
        TypeGrams(state, 250m);

        await AnotherPcSavesAsync(id, flourId, "Sweet Dough", 200m);
        await state.View.List.MoveSubAssembliesCommand.ExecuteAsync([new SubAssemblyMove(id, fillingRoleId)]);

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Sweet Dough"));
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.SingleAsync(s => s.Id == id, Cancellation)).RoleId.Should().NotBe(fillingRoleId);
    });

    [Test]
    public Task Moving_another_sub_assembly_from_the_list_doesnt_let_what_is_typed_over_a_save_from_another_pc() => UiThread.RunAsync(async () =>
    {
        var (id, flourId) = await AddRecipeAsync("Dough");
        var (otherId, _) = await AddRecipeAsync("Filling");
        var toppingRoleId = await AddRoleAsync("Topping Role", sortOrder: 3);
        var state = await OpenedStateAsync(id);
        TypeGrams(state, 250m);

        await AnotherPcSavesAsync(id, flourId, "Sweet Dough", 200m);
        await state.View.List.MoveSubAssembliesCommand.ExecuteAsync([new SubAssemblyMove(otherId, toppingRoleId)]);
        state.ErrorMessage.Should().BeNull("the move is of another sub-assembly, which nobody has saved");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Sweet Dough"));
        (await StoredAsync(id)).Should().Be(("Sweet Dough", 200m));
    });

    [Test]
    public Task A_sub_assembly_no_save_has_stamped_saves_alone_and_is_refused_over_the_first_save_from_another_pc() => UiThread.RunAsync(async () =>
    {
        var (id, flourId) = await AddRecipeAsync("Dough");
        var (otherId, otherFlourId) = await AddRecipeAsync("Filling");
        await ClearSavedTimeAsync(id);
        await ClearSavedTimeAsync(otherId);

        // Nobody else has saved this one: its first Save goes through.
        var state = await OpenedStateAsync(otherId);
        TypeGrams(state, 150m);
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(otherId)).Should().Be(("Filling", 150m));

        // Another PC got to this one first.
        state = await OpenedStateAsync(id);
        TypeGrams(state, 250m);
        await AnotherPcSavesAsync(id, flourId, "Sweet Dough", 200m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(SavedVersionRules.SavedSinceText("Sweet Dough"));
        (await StoredAsync(id)).Should().Be(("Sweet Dough", 200m));
        otherFlourId.Should().NotBe(flourId);
    });

    [Test]
    public Task What_the_screen_said_stays_while_the_sub_assembly_shows_and_goes_when_another_opens() => UiThread.RunAsync(async () =>
    {
        var (id, _) = await AddRecipeAsync("Dough");
        var (otherId, _) = await AddRecipeAsync("Filling");
        var state = await OpenedStateAsync(id);

        ((IScreenPartHost)state).ShowError("Dough is in use.");
        await state.LoadCommand.ExecuteAsync();
        state.ErrorMessage.Should().Be("Dough is in use.");

        state.OpenRecord(otherId);
        await ShowingAsync(state, "Filling", 100m);

        state.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task The_forms_own_saves_follow_one_another() => UiThread.RunAsync(async () =>
    {
        var (id, _) = await AddRecipeAsync("Dough");
        var state = await OpenedStateAsync(id);

        TypeGrams(state, 250m);
        await state.SaveCommand.ExecuteAsync();
        await ShowingAsync(state, "Dough", 250m);
        TypeGrams(state, 300m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await StoredAsync(id)).Should().Be(("Dough", 300m));
    });

    /// <summary>One cell typed and left, as the line table brackets it in the app.</summary>
    private static void TypeGrams(SubAssemblyForm state, decimal grams)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.Batch.Lines.Single().Grams = grams;
        recorder.EndUndoStep();
    }

    private static Task ShowingAsync(SubAssemblyForm state, string name, decimal grams) =>
        Settle.UntilAsync(state,
            () => state.EditingName == name && state.Batch.Lines.Count == 1 && state.Batch.Lines[0].Grams == grams && !state.History.IsDirty,
            () => $"name={state.EditingName}, lines={state.Batch.Lines.Count}, dirty={state.History.IsDirty}");

    private static async Task RenameFromTheListAsync(SubAssemblyForm state, int id, string name)
    {
        await state.View.List.RenameSubAssemblyCommand.ExecuteAsync((SubAssemblyLeafNode)state.View.List.SubAssemblyTree.Find(id)!);
        await state.View.List.CommitTreeNameAsync(name);
    }

    /// <summary>As every sub-assembly was when the column came: no save has stamped it.</summary>
    private async Task ClearSavedTimeAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.SubAssemblies.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, (DateTime?)null), Cancellation);
    }

    /// <summary>A recipe of 100 g of one ingredient, saved as the form saves one, which stamps when it was saved.</summary>
    private async Task<(int Id, int IngredientId)> AddRecipeAsync(string name)
    {
        var roleId = await AddRoleAsync($"{name} Role");
        var ingredientId = await AddIngredientAsync($"{name} Ingredient", 0.05m);
        var id = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = name, RoleId = roleId }, [new RecipeLine(ingredientId, 100m)], Cancellation);
        return (id, ingredientId);
    }

    /// <summary>A save from another PC on this database: its own read of the sub-assembly, its own Save.</summary>
    private async Task AnotherPcSavesAsync(int id, int ingredientId, string name, decimal grams)
    {
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var theirs = (await subAssemblies.GetByIdsAsync([id], Cancellation)).Single();
        theirs.Name = name;
        await subAssemblies.SaveStandaloneAsync(theirs, [new RecipeLine(ingredientId, grams)], Cancellation);
    }

    private async Task<(string Name, decimal Grams)> StoredAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        var name = await dbContext.SubAssemblies.Where(s => s.Id == id).Select(s => s.Name).SingleAsync(Cancellation);
        var grams = await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == id).Select(l => l.Grams).SingleAsync(Cancellation);
        return (name, grams);
    }

    /// <summary>The screen with <paramref name="id"/> open on the form, its lines in.</summary>
    private async Task<SubAssemblyForm> OpenedStateAsync(int id)
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.OpenRecord(id);
        await Settle.UntilAsync(state,
            () => state.SelectedRow?.Id == id && state.Batch.Lines.Count > 0 && !state.History.IsDirty,
            () => $"lines={state.Batch.Lines.Count}");
        return state;
    }

    private SubAssemblyForm CreateState()
    {
        var factory = CreateDbContextFactory();

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
}
