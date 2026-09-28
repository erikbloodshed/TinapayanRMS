using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// Settings' Roles tab and its Product-owned option: shown for the role picked, saved with it, asked about first when
/// it moves sub-assemblies (saying how many), and never offered for the Packaging role.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SubAssemblyRolesViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRolesViewModel CreateViewModel(bool confirm, List<string>? asked = null)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var viewModel = new SubAssemblyRolesViewModel(
            new SubAssemblyRoleRepository(CreateDbContextFactory()), new SubAssemblyRoleValidator(), new LoggerConfiguration().CreateLogger());
        viewModel.ConfirmDelete.RegisterHandler(context =>
        {
            asked?.Add(context.Input);
            context.SetOutput(confirm);
        });
        return viewModel;
    }

    private async Task<SubAssemblyRole> RoleAsync(int id) =>
        (await new SubAssemblyRoleRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Single(r => r.Id == id);

    [Fact]
    public async Task Ticking_Product_owned_asks_first_saying_how_many_move_and_then_saves_it()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var cheeseId = await AddIngredientAsync("Cheese");
        var (productId, fillingId) = await AddProductWithSubAssemblyAsync("Cheese Roll", fillingRoleId, [(cheeseId, 100m)]);
        List<string> asked = [];
        var viewModel = CreateViewModel(confirm: true, asked);
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRole = viewModel.Roles.Single(r => r.Id == fillingRoleId);
        viewModel.EditingIsProductOwned.Should().BeFalse();
        viewModel.CanEditProductOwned.Should().BeTrue();

        viewModel.EditingIsProductOwned = true;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        asked.Should().ContainSingle().Which.Should().StartWith("Make Filling product-owned? 1 sub-assembly on the Sub-Assemblies screen used by just one product");
        (await RoleAsync(fillingRoleId)).IsProductOwned.Should().BeTrue();
        viewModel.Roles.Single(r => r.Id == fillingRoleId).IsProductOwned.Should().BeTrue("the grid shows it");
        (await new SubAssemblyRepository(CreateDbContextFactory()).GetByIdsAsync([fillingId], Cancellation)).Single().OwnerProductId.Should().Be(productId);
    }

    [Fact]
    public async Task Declining_the_question_saves_nothing()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var viewModel = CreateViewModel(confirm: false);
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRole = viewModel.Roles.Single(r => r.Id == fillingRoleId);
        viewModel.EditingIsProductOwned.Should().BeTrue();

        viewModel.EditingIsProductOwned = false;
        await viewModel.SaveCommand.Execute();

        (await RoleAsync(fillingRoleId)).IsProductOwned.Should().BeTrue();
    }

    [Fact]
    public async Task A_rename_alone_isnt_asked_about()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        List<string> asked = [];
        var viewModel = CreateViewModel(confirm: false, asked);
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRole = viewModel.Roles.Single(r => r.Id == fillingRoleId);

        viewModel.EditingName = "Stuffing";
        await viewModel.SaveCommand.Execute();

        asked.Should().BeEmpty();
        var saved = await RoleAsync(fillingRoleId);
        (saved.Name, saved.IsProductOwned).Should().Be(("Stuffing", true));
    }

    [Fact]
    public async Task The_Packaging_role_cant_be_ticked_and_a_new_role_can()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var viewModel = CreateViewModel(confirm: true);
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedRole = viewModel.Roles.Single(r => r.Id == packagingRoleId);
        viewModel.CanEditProductOwned.Should().BeFalse();

        await viewModel.NewCommand.Execute();
        viewModel.CanEditProductOwned.Should().BeTrue();
        viewModel.EditingName = "Glaze";
        viewModel.EditingIsProductOwned = true;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Roles.Single(r => r.Name == "Glaze").IsProductOwned.Should().BeTrue();
    }

    [Fact]
    public void The_question_says_what_turning_it_off_does()
    {
        SubAssemblyRolesViewModel.OwnershipChangeMessage(new SubAssemblyRole { Name = "Topping", IsProductOwned = false }, 3)
            .Should().Be("Stop making Topping product-owned? The 3 sub-assemblies products own in it will move to the Sub-Assemblies screen, still used by the same products, which will link them. Add-ins merged into their lines stay merged.");
    }
}
