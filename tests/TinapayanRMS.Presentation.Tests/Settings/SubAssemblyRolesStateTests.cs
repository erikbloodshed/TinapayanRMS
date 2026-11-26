using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Settings;
using TinapayanRMS.UseCases.SubAssemblies;

namespace TinapayanRMS.Presentation.Tests.Settings;

/// <summary>
/// Settings' Roles tab and its Product-owned option: shown for the role picked, saved with it, asked about first when
/// it moves sub-assemblies (saying how many), and never offered for the Packaging role; and its Can go into another
/// section option, kept while a product has a section in the role on another's lines.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class SubAssemblyRolesStateTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRolesState CreateState(bool confirm, List<string>? asked = null)
    {
        var state = new SubAssemblyRolesState(
            new SubAssemblyRoleEditor(new SubAssemblyRoleRepository(CreateDbContextFactory()), new SubAssemblyRoleValidator()),
            new LoggerConfiguration().CreateLogger());
        state.ConfirmDelete.RegisterHandler(context =>
        {
            asked?.Add(context.Input);
            context.SetOutput(confirm);
        });
        return state;
    }

    private async Task<SubAssemblyRole> RoleAsync(int id) =>
        (await new SubAssemblyRoleRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Single(r => r.Id == id);

    [Test]
    public Task Ticking_Product_owned_asks_first_saying_how_many_move_and_then_saves_it() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var cheeseId = await AddIngredientAsync("Cheese");
        var (productId, fillingId) = await AddProductWithSubAssemblyAsync("Cheese Roll", fillingRoleId, [(cheeseId, 100m)]);
        List<string> asked = [];
        var state = CreateState(confirm: true, asked);
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Items.Single(r => r.Id == fillingRoleId));
        state.EditingIsProductOwned.Should().BeFalse();
        state.CanEditProductOwned.Should().BeTrue();

        state.EditingIsProductOwned = true;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        asked.Should().ContainSingle().Which.Should().StartWith("Make Filling product-owned? 1 sub-assembly on the Sub-Assemblies screen used by just one product");
        (await RoleAsync(fillingRoleId)).IsProductOwned.Should().BeTrue();
        state.Items.Single(r => r.Id == fillingRoleId).IsProductOwned.Should().BeTrue("the grid shows it");
        (await new SubAssemblyRepository(CreateDbContextFactory()).GetByIdsAsync([fillingId], Cancellation)).Single().OwnerProductId.Should().Be(productId);
    });

    [Test]
    public Task Declining_the_question_saves_nothing() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var state = CreateState(confirm: false);
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Items.Single(r => r.Id == fillingRoleId));
        state.EditingIsProductOwned.Should().BeTrue();

        state.EditingIsProductOwned = false;
        await state.SaveCommand.ExecuteAsync();

        (await RoleAsync(fillingRoleId)).IsProductOwned.Should().BeTrue();
    });

    [Test]
    public Task A_rename_alone_isnt_asked_about() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        List<string> asked = [];
        var state = CreateState(confirm: false, asked);
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Items.Single(r => r.Id == fillingRoleId));

        state.EditingName = "Stuffing";
        await state.SaveCommand.ExecuteAsync();

        asked.Should().BeEmpty();
        var saved = await RoleAsync(fillingRoleId);
        (saved.Name, saved.IsProductOwned).Should().Be(("Stuffing", true));
    });

    [Test]
    public Task The_Packaging_role_cant_be_ticked_and_a_new_role_can() => UiThread.RunAsync(async () =>
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var state = CreateState(confirm: true);
        await state.LoadCommand.ExecuteAsync();

        state.Pick(state.Items.Single(r => r.Id == packagingRoleId));
        state.CanEditProductOwned.Should().BeFalse();

        await state.NewCommand.ExecuteAsync();
        state.CanEditProductOwned.Should().BeTrue();
        state.EditingName = "Glaze";
        state.EditingIsProductOwned = true;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.Items.Single(r => r.Name == "Glaze").IsProductOwned.Should().BeTrue();
    });

    [Test]
    public Task Can_go_into_another_section_is_shown_saved_and_kept_while_a_product_uses_it() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true);
        var state = CreateState(confirm: true);
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Items.Single(r => r.Id == spongeRoleId));
        state.EditingCanGoIntoOtherSections.Should().BeFalse();

        state.EditingCanGoIntoOtherSections = true;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await RoleAsync(spongeRoleId)).CanGoIntoOtherSections.Should().BeTrue();
        state.Items.Single(r => r.Id == spongeRoleId).CanGoIntoOtherSections.Should().BeTrue("the grid shows it");

        // A product puts the sponge on its dough's lines: now it stays ticked.
        var flourId = await AddIngredientAsync("Flour");
        var productId = await AddProductAsync("Spanish Bread");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m)
                {
                    SectionLines = [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 1), 300m, null, 1)],
                },
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m),
            ],
            []));
        state.Pick(state.Items.Single(r => r.Id == spongeRoleId));
        state.EditingCanGoIntoOtherSections = false;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().StartWith("Spanish Bread has a Sponge section on another section's lines.");
        (await RoleAsync(spongeRoleId)).CanGoIntoOtherSections.Should().BeTrue();
    });

    [Test]
    public Task A_new_role_goes_last() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 2);
        var state = CreateState(confirm: true);
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Glaze";
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.Items.Select(r => (r.Name, r.SortOrder)).Should().Equal(("Dough", 1), ("Filling", 2), ("Glaze", 3));
    });

    [Test]
    public Task Move_Up_and_Move_Down_move_the_picked_role_and_keep_it_picked() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var fillingId = await AddRoleAsync("Filling", sortOrder: 2);
        await AddRoleAsync("Topping", sortOrder: 3);
        var state = CreateState(confirm: true);
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Items.Single(r => r.Id == fillingId));
        state.EditingName = "Stuffing";

        await state.MoveUpCommand.ExecuteAsync();

        state.Items.Select(r => r.Name).Should().Equal("Filling", "Dough", "Topping");
        state.SelectedItem!.Id.Should().Be(fillingId);
        state.EditingName.Should().Be("Stuffing", "a move leaves the form's unsaved edits alone");
        state.MoveUpCommand.CanExecute().Should().BeFalse("it's first now");

        state.MoveDownCommand.CanExecute().Should().BeTrue("the move is done");

        await state.MoveDownCommand.ExecuteAsync();
        await state.MoveDownCommand.ExecuteAsync();

        state.Items.Select(r => (r.Name, r.SortOrder)).Should().Equal(("Dough", 1), ("Topping", 2), ("Filling", 3));
        state.MoveDownCommand.CanExecute().Should().BeFalse("it's last now");
        state.MoveUpCommand.CanExecute().Should().BeTrue();
    });

    [Test]
    public Task Nothing_moves_with_no_role_picked() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 2);
        var state = CreateState(confirm: true);
        await state.LoadCommand.ExecuteAsync();

        state.MoveUpCommand.CanExecute().Should().BeFalse();
        state.MoveDownCommand.CanExecute().Should().BeFalse();
    });

    [Test]
    public void The_question_says_what_turning_it_off_does()
    {
        SubAssemblyRolesState.OwnershipChangeMessage(new SubAssemblyRole { Name = "Topping", IsProductOwned = false }, 3)
            .Should().Be("Stop making Topping product-owned? The 3 sub-assemblies products own in it will move to the Sub-Assemblies screen, still used by the same products, which will link them. Add-ins merged into their lines stay merged.");
    }
}
