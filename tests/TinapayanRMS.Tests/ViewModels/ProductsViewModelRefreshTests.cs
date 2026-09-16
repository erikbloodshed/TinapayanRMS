using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.ViewModels;

/// <summary>
/// Coming back to the Products tab re-runs LoadCommand (MainWindow). A cost changed on another tab
/// must reach the selected product's recipe rows and cost summary, while the selection and the
/// costing fields the user was typing into stay as they were. Save writes the whole form at once:
/// the product's own sub-assemblies (made in the form), the ones it links, and the rest. The Recipe
/// Structure shows them in tabs, which keep their place through edits and refreshes.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductsViewModelRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Reloading_updates_usage_costs_changed_on_another_tab_and_keeps_the_selection_and_unsaved_costing()
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ube pandesal", "Ube filling", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(
            () => viewModel.UsageEdits.Count == 1,
            () => $"usage rows={viewModel.UsageEdits.Count}, roles={viewModel.Roles.Count}");
        viewModel.UsageEdits.Single().CostPerPiece.Should().Be(2.4m);
        var selected = viewModel.SelectedProduct;
        viewModel.EditingLaborPercent = 5m;

        // As another tab would: ube's price goes up (the cascade reprices the sub-assembly too).
        await using (var dbContext = CreateDbContext())
        {
            var ube = await dbContext.RawMaterials.SingleAsync(m => m.Id == ubeId, Cancellation);
            ube.CostPerBaseUnit = 0.36m;
            ube.BufferedCostPerBaseUnit = 0.36m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();

        viewModel.Products.Should().ContainSingle().Which.Should().BeSameAs(selected);
        viewModel.SelectedProduct.Should().BeSameAs(selected);
        viewModel.SelectedProduct!.Id.Should().Be(productId);
        viewModel.EditingLaborPercent.Should().Be(5m, "an unsaved costing field must survive the refresh");
        viewModel.UsageEdits.Should().ContainSingle().Which.CostPerPiece.Should().Be(3.6m);
        viewModel.Costs.Material.Should().Be(3.6m);
        var (expectedBaseCost, _, _) = CostingRules.CalculateSellingPrice(3.6m, 5m, 0m, 0m, 0m, 0m);
        viewModel.Costs.BaseCost.Should().Be(expectedBaseCost);
        viewModel.CostShares.Should().ContainSingle().Which.Amount.Should().Be(3.6m);
    }

    [Fact]
    public async Task One_Save_writes_a_new_product_with_a_sub_assembly_made_in_it()
    {
        await AddRoleAsync("Filling");
        await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Ube pandesal";
        viewModel.EditingDescription = "Soft bread with ube filling";
        await viewModel.AddUsageCommand.Execute();
        viewModel.IsFormEditingLines.Should().BeTrue("a new sub-assembly is made in the form");
        viewModel.EditingUsageName = "Ube filling";
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = viewModel.Ingredients.Single().Id, Grams = 100m });
        viewModel.UsageFormTotalWeightText.Should().Be("100 g");
        viewModel.EditingUsageWeightPerPiece = 10m;
        viewModel.EditingUsageYield.Should().Be(10m, "typing the weight per piece works out the yield");
        await viewModel.ApplyUsageCommand.Execute();
        viewModel.EditingLaborPercent = 50m;
        viewModel.EditingAdministrativePercent = 12.5m;

        var usage = viewModel.UsageEdits.Should().ContainSingle().Subject;
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>("Apply shows the new sub-assembly's tab").Which.Usage.Should().BeSameAs(usage);
        usage.IsOwned.Should().BeTrue();
        usage.CostPerPiece.Should().Be(2.4m);
        viewModel.Costs.Labor.Should().Be(1.2m, "labor is 50% of the ₱2.40 material cost");
        viewModel.Costs.Administrative.Should().Be(0.3m, "administrative is 12.5% of the ₱2.40 material cost");
        viewModel.Costs.BaseCost.Should().Be(3.9m, "₱2.40 of filling, ₱1.20 labor and ₱0.30 administrative");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        int productId;
        await using (var dbContext = CreateDbContext())
        {
            var product = await dbContext.Products.SingleAsync(Cancellation);
            productId = product.Id;
            product.Name.Should().Be("Ube pandesal");
            product.Description.Should().Be("Soft bread with ube filling");
            var subAssembly = await dbContext.SubAssemblies.SingleAsync(Cancellation);
            subAssembly.Name.Should().Be("Ube filling");
            subAssembly.OwnerProductId.Should().Be(productId);
            subAssembly.TotalWeight.Should().Be(100m);
            var saved = await dbContext.ProductSubAssemblyUsages.SingleAsync(Cancellation);
            saved.AuthoredField.Should().Be(UsageAuthoredField.WeightPerPiece);
            saved.AuthoredValue.Should().Be(10m);
            var costing = await dbContext.CostingParameters.SingleAsync(Cancellation);
            costing.LaborPercent.Should().Be(50m);
            costing.AdministrativePercent.Should().Be(12.5m);
        }

        // The form now shows the saved product, which can be linked and cloned.
        await WaitUntilSettledAsync(
            () => viewModel.SelectedProduct?.Id == productId && viewModel.UsageEdits.Count == 1,
            () => $"selected={viewModel.SelectedProduct?.Id}, usage rows={viewModel.UsageEdits.Count}");
        viewModel.HeaderTitle.Should().Be("Ube pandesal");
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>("the reload shows the same sub-assembly's tab, now saved")
            .Which.Usage.SubAssemblyId.Should().BeGreaterThan(0);
        viewModel.ShowLinks.Should().BeTrue();
        viewModel.LastUpdatedText.Should().NotBe("—");
    }

    [Fact]
    public async Task A_typed_yield_is_kept_and_the_weight_per_piece_follows_the_batch()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (productId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", "Bread Dough", roleId, [(flourId, 1200m)], weightPerPiece: 50m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        await viewModel.EditUsageCommand.Execute(viewModel.UsageEdits.Single());
        viewModel.EditingUsageYield.Should().Be(24m, "1,200 g at 50 g a piece");
        viewModel.EditingUsageYield = 30m;
        viewModel.EditingUsageWeightPerPiece.Should().Be(40m, "typing the yield works out the weight per piece");

        // The batch grows while the form is open, typed over the line's grams: the typed yield stays.
        viewModel.FormLines.Single().Grams = 1500m;
        viewModel.EditingUsageYield.Should().Be(30m);
        viewModel.EditingUsageWeightPerPiece.Should().Be(50m, "1,500 g over the 30 pieces typed");

        await viewModel.ApplyUsageCommand.Execute();
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.SubAssemblyId.Should().Be(doughId, "the sub-assembly is updated in place, not replaced");
        usage.AuthoredField.Should().Be(UsageAuthoredField.Yield);
        usage.AuthoredValue.Should().Be(30m);
        (await dbContext.SubAssemblies.SingleAsync(s => s.Id == doughId, Cancellation)).TotalWeight.Should().Be(1500m);
    }

    [Fact]
    public async Task A_linked_sub_assembly_follows_its_owner_and_Make_a_Copy_makes_it_the_products_own()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (bigId, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", "Sweet Dough", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        // Link Cream Loaf's dough into the mini, at its own weight.
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == miniId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == miniId, () => $"selected={viewModel.SelectedProduct?.Id}");
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageMode = viewModel.UsageFormModes.Single(m => m.Mode == UsageFormMode.Link);
        viewModel.IsFormEditingLines.Should().BeFalse("a link's lines are its owner's");
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single();
        viewModel.EditingUsageSource!.DisplayName.Should().Be("Sweet Dough — Cream Loaf (Dough)");
        await WaitUntilSettledAsync(() => viewModel.FormLines.Count == 1, () => $"form lines={viewModel.FormLines.Count}");
        viewModel.IsFormShowingLines.Should().BeTrue("the sub-assembly picked to link shows the lines it brings, read-only");
        viewModel.FormLines.Single().Grams.Should().Be(1000m);
        viewModel.EditingUsageWeightPerPiece = 30m;
        await viewModel.ApplyUsageCommand.Execute();
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        var linked = viewModel.UsageEdits.Single();
        linked.IsLinked.Should().BeTrue();
        linked.LinkedFromText.Should().Be("From Cream Loaf");
        linked.CostPerPiece.Should().Be(1.5m, "30 g of a ₱50, 1,000 g batch");

        // Its tab shows its form, with its owner's lines in it to look at but not to change.
        viewModel.EditingUsage.Should().BeSameAs(linked);
        viewModel.IsFormShowingLines.Should().BeTrue();
        viewModel.IsFormEditingLines.Should().BeFalse();
        viewModel.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal(("Flour", 1000m));

        // Cream Loaf adds butter to its dough: the mini's linked section follows on the next refresh.
        await SaveRecipeAsync(bigId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(doughId, "Sweet Dough", roleId, [(flourId, 1000m), (butterId, 250m)], UsageAuthoredField.WeightPerPiece, 60m)], []));
        await viewModel.LoadCommand.Execute();
        viewModel.UsageEdits.Single().TotalWeight.Should().Be(1250m);
        viewModel.UsageEdits.Single().CostPerPiece.Should().Be(3.6m, "30 g of a ₱150, 1,250 g batch");
        viewModel.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should()
            .Equal([("Flour", 1000m), ("Butter", 250m)], "the read-only lines show the owner's, as they now are");

        // Make a Copy: the mini gets its own dough, at the same weight, and stops following.
        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());
        var copy = viewModel.UsageEdits.Single();
        copy.IsOwned.Should().BeTrue();
        copy.Name.Should().Be("Sweet Dough");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var miniUsage = await dbContext.ProductSubAssemblyUsages.Include(u => u.SubAssembly).SingleAsync(u => u.ProductId == miniId, Cancellation);
        miniUsage.SubAssemblyId.Should().NotBe(doughId);
        miniUsage.SubAssembly!.OwnerProductId.Should().Be(miniId);
        miniUsage.SubAssembly.TotalWeight.Should().Be(1250m);
        miniUsage.AuthoredValue.Should().Be(30m);
    }

    [Fact]
    public async Task Removing_a_sub_assembly_another_product_links_is_refused_on_the_spot()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", "Sweet Dough", roleId, [(flourId, 1000m)]);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(miniId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Name == "Cream Loaf");
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        var shared = viewModel.UsageEdits.Single();
        shared.IsShared.Should().BeTrue();
        shared.SharedText.Should().Be("Also used by Cream Loaf Mini");

        await viewModel.RemoveUsageCommand.Execute(shared);

        viewModel.ErrorMessage.Should().StartWith("Cream Loaf Mini links Sweet Dough");
        viewModel.UsageEdits.Should().ContainSingle();
    }

    [Fact]
    public async Task The_recipe_tabs_are_each_sub_assembly_in_recipe_order_then_Add_Sub_Assembly_then_Packaging()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, "Ube Filling", fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
                new OwnedSubAssemblyDraft(0, "Sweet Dough", doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.RecipeTabs.Should().Equal(viewModel.AddSubAssemblyTab, viewModel.PackagingTab);
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab, "a new product starts on Add Sub-Assembly");
        viewModel.IsUsageFormOpen.Should().BeTrue();
        viewModel.EditingUsageRole.Should().NotBeNull("the add form opened before the roles loaded takes the first one");

        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Sweet Dough", "Ube Filling", "Add Sub-Assembly", "Packaging");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.RecipeTabs[0]);
        viewModel.IsAddingUsage.Should().BeFalse("an add form nothing was entered in closes when another tab shows");
        viewModel.EditingUsage.Should().BeSameAs(((SubAssemblyRecipeTab)viewModel.RecipeTabs[0]).Usage, "a sub-assembly's tab is the form that edits it");
    }

    [Fact]
    public async Task Save_leaves_the_Add_Sub_Assembly_form_alone_when_nothing_was_entered_in_it()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.IsUsageFormOpen.Should().BeTrue();
        viewModel.EditingName = "Gift Box Set";

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct is not null, () => "the saved product was never selected");
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.SingleAsync(Cancellation)).Name.Should().Be("Gift Box Set");
        (await dbContext.ProductSubAssemblyUsages.CountAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task Editing_a_sub_assembly_opens_the_form_in_its_tab_which_stays_through_a_refresh_and_Apply()
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", "Ube filling", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        var tab = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single();

        await viewModel.EditUsageCommand.Execute(tab.Usage);
        viewModel.EditingUsage.Should().BeSameAs(tab.Usage);
        viewModel.EditingUsageName = "Ube halaya";

        // As another tab would, while the form is open: ube's price goes up, and the refresh swaps in a repriced copy.
        await using (var dbContext = CreateDbContext())
        {
            var ube = await dbContext.RawMaterials.SingleAsync(m => m.Id == ubeId, Cancellation);
            ube.CostPerBaseUnit = 0.36m;
            ube.BufferedCostPerBaseUnit = 0.36m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();
        tab.Usage.CostPerPiece.Should().Be(3.6m);
        viewModel.EditingUsage.Should().BeSameAs(tab.Usage, "the form follows the repriced copy that took its place");

        await viewModel.ApplyUsageCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.UsageEdits.Should().ContainSingle("Apply replaces the refreshed copy instead of adding a second").Which.Name.Should().Be("Ube halaya");
        viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Should().ContainSingle().Which.Should().BeSameAs(tab);
        viewModel.SelectedRecipeTab.Should().BeSameAs(tab);
        viewModel.EditingUsage.Should().BeSameAs(tab.Usage, "the tab has nothing else to show, so its form stays open on what was applied");
        tab.Header.Should().Be("Ube halaya");
    }

    [Fact]
    public async Task Removing_the_sub_assembly_showing_shows_the_next_tab_and_cancelling_an_add_goes_back()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Twin Rolls");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, "Brioche Dough", roleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new OwnedSubAssemblyDraft(0, "Sweet Dough", roleId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 40m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        // From Packaging, Add Sub-Assembly then Cancel: back on Packaging, with the form closed.
        viewModel.SelectedRecipeTab = viewModel.PackagingTab;
        await viewModel.AddUsageCommand.Execute();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.IsUsageFormOpen.Should().BeTrue();
        await viewModel.CancelUsageCommand.Execute();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.PackagingTab);
        viewModel.IsUsageFormOpen.Should().BeFalse();

        // Removing the first sub-assembly while it shows: the next one's tab shows.
        var brioche = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().First();
        viewModel.SelectedRecipeTab = brioche;
        await viewModel.RemoveUsageCommand.Execute(brioche.Usage);
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Header.Should().Be("Sweet Dough");

        // Removing the last: Add Sub-Assembly shows, with its form open. Packaging stays.
        await viewModel.RemoveUsageCommand.Execute(viewModel.UsageEdits.Single());
        viewModel.RecipeTabs.Should().Equal(viewModel.AddSubAssemblyTab, viewModel.PackagingTab);
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.IsUsageFormOpen.Should().BeTrue();
        viewModel.IsAddingUsage.Should().BeTrue();
    }

    [Fact]
    public async Task A_standalone_sub_assembly_is_offered_to_link_and_shows_where_its_from()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var glazeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new Domain.Entities.SubAssembly { Name = "Chocolate Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var productId = await AddProductAsync("Cream Loaf");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        // A new product, not saved yet, is offered it too.
        viewModel.EditingUsageMode = UsageFormModeOption.All.Single(o => o.Mode == UsageFormMode.Link);
        viewModel.LinkableSubAssemblies.Should().ContainSingle().Which.DisplayName.Should().Be("Chocolate Glaze — Sub-Assemblies (Glaze)");

        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(
            () => viewModel.SelectedProduct?.Id == productId && viewModel.IsUsageFormOpen,
            () => $"selected={viewModel.SelectedProduct?.Id}, form open={viewModel.IsUsageFormOpen}");
        viewModel.EditingUsageMode = UsageFormModeOption.All.Single(o => o.Mode == UsageFormMode.Link);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageFormTotalWeightText == "100 g", () => $"batch={viewModel.UsageFormTotalWeightText}");
        viewModel.EditingUsageWeightPerPiece = 5m;
        await viewModel.ApplyUsageCommand.Execute();

        var usage = viewModel.UsageEdits.Should().ContainSingle().Subject;
        usage.IsLinked.Should().BeTrue();
        usage.LinkedFromText.Should().Be("From Sub-Assemblies");
        usage.CostPerPiece.Should().Be(0.45m, "5 g of a 100 g, ₱9 batch");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var saved = await dbContext.ProductSubAssemblyUsages.SingleAsync(Cancellation);
        saved.ProductId.Should().Be(productId);
        saved.SubAssemblyId.Should().Be(glazeId);
        saved.AuthoredValue.Should().Be(5m);
    }

    [Fact]
    public async Task The_description_link_edits_the_description_in_a_pop_up_and_Cancel_puts_it_back()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.HeaderInitial.Should().Be("N", "a new product's title is \"New product\"");
        viewModel.DescriptionLinkText.Should().Be("Add description");

        await viewModel.EditDescriptionCommand.Execute();
        viewModel.IsDescriptionEditorOpen.Should().BeTrue();
        viewModel.EditingDescription = "Soft bread";
        viewModel.DescriptionLinkText.Should().Be("Soft bread", "the link shows what's typed");
        await viewModel.CloseDescriptionEditorCommand.Execute();
        viewModel.IsDescriptionEditorOpen.Should().BeFalse();
        viewModel.EditingDescription.Should().Be("Soft bread");

        await viewModel.EditDescriptionCommand.Execute();
        viewModel.EditingDescription = "Something else";
        await viewModel.CancelDescriptionEditCommand.Execute();
        viewModel.EditingDescription.Should().Be("Soft bread", "Cancel puts back what was there when the pop-up opened");
        viewModel.IsDescriptionEditorOpen.Should().BeFalse();

        viewModel.EditingName = "Cheese roll";
        viewModel.HeaderInitial.Should().Be("C");
    }

    private ProductsViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ProductsViewModel(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new PackagingLinkRepository(factory, packagingLineRepository),
            new ProductCloner(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory, CreateCostPropagator()),
            new ProductValidator(),
            new CostingParametersValidator(),
            new SubAssemblyValidator(),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>
    /// Selecting a product loads its detail in the background (the ViewModel's selection
    /// subscriptions), with nothing to await. Waits until <paramref name="condition"/> has held
    /// for a moment, so a load still finishing can't change things after the test moves on.
    /// </summary>
    private static async Task WaitUntilSettledAsync(Func<bool> condition, Func<string> describe)
    {
        var deadline = Stopwatch.StartNew();
        var heldFor = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (!condition())
            {
                heldFor.Restart();
            }
            else if (heldFor.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"The product's detail never finished loading: {describe()}.");
    }

    [Fact]
    public async Task Leaving_a_sub_assemblys_tab_applies_its_form_and_a_refused_one_keeps_the_tab()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Twin Rolls");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, "Brioche Dough", roleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new OwnedSubAssemblyDraft(0, "Sweet Dough", roleId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 40m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        var brioche = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Brioche Dough");
        var sweet = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Sweet Dough");

        // Typed into Brioche's form, then away to Sweet Dough's tab: the typing is applied on the way out.
        viewModel.SelectedRecipeTab = brioche;
        viewModel.EditingUsage.Should().BeSameAs(brioche.Usage, "showing a sub-assembly's tab opens its form");
        viewModel.FormLines.Single().Grams = 1500m;
        viewModel.SelectedRecipeTab = sweet;

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SelectedRecipeTab.Should().BeSameAs(sweet);
        viewModel.EditingUsage.Should().BeSameAs(sweet.Usage);
        brioche.Usage.TotalWeight.Should().Be(1500m, "leaving its tab applied what was typed");

        // A form a rule refuses keeps its own tab, with the message shown.
        viewModel.SelectedRecipeTab = brioche;
        viewModel.EditingUsageName = string.Empty;
        viewModel.SelectedRecipeTab = sweet;

        viewModel.SelectedRecipeTab.Should().BeSameAs(brioche, "the form has to be put right before another tab can show");
        viewModel.ErrorMessage.Should().NotBeNull();
        viewModel.EditingUsage.Should().BeSameAs(brioche.Usage);

        // Cancel puts back what was last applied, and the tab can be left again.
        await viewModel.CancelUsageCommand.Execute();
        viewModel.EditingUsageName.Should().Be("Brioche Dough");
        viewModel.SelectedRecipeTab = sweet;
        viewModel.SelectedRecipeTab.Should().BeSameAs(sweet);
    }

    [Fact]
    public async Task Merely_showing_a_sub_assemblys_tab_leaves_the_recipe_as_it_was()
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", "Ube filling", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        var tab = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single();
        var usage = tab.Usage;

        viewModel.SelectedRecipeTab = viewModel.PackagingTab;
        viewModel.SelectedRecipeTab = tab;
        viewModel.SelectedRecipeTab = viewModel.PackagingTab;

        viewModel.ErrorMessage.Should().BeNull();
        tab.Usage.Should().BeSameAs(usage, "a form nothing was typed into is applied to nothing");
        viewModel.IsUsageFormOpen.Should().BeFalse("Packaging has no sub-assembly form");
    }

    /// <summary>Cream Loaf owns a 1,000 g / ₱50 Sweet Dough; a second product links it. Returns the linker, the dough and ube.</summary>
    private async Task<(int LinkerId, int DoughId, int UbeId, int FlourId)> SeedLinkableDoughAsync()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        await AddProductWithSubAssemblyAsync("Cream Loaf", "Sweet Dough", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var linkerId = await AddProductAsync("Cream Loaf Ube");
        var doughId = await OwnedSubAssemblyIdAsync(
            (await new ProductRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Single(p => p.Name == "Cream Loaf").Id,
            "Sweet Dough");
        return (linkerId, doughId, ubeId, flourId);
    }

    /// <summary>Selects <paramref name="productId"/> and links the dough into it with <paramref name="addInGrams"/> of ube.</summary>
    private static async Task LinkWithUbeAsync(ProductsViewModel viewModel, int ubeId, decimal addInGrams, decimal weightPerPiece)
    {
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageMode = viewModel.UsageFormModes.Single(m => m.Mode == UsageFormMode.Link);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single();
        await WaitUntilSettledAsync(() => viewModel.FormLines.Count == 1, () => $"form lines={viewModel.FormLines.Count}");
        viewModel.FormAddIns.Add(new IngredientLineRow(viewModel.Ingredients.Single(m => m.Id == ubeId), addInGrams));
        viewModel.EditingUsageWeightPerPiece = weightPerPiece;
        await viewModel.ApplyUsageCommand.Execute();
    }

    [Fact]
    public async Task Add_ins_go_into_a_linked_usages_batch_and_its_cost_per_piece()
    {
        var (linkerId, _, ubeId, _) = await SeedLinkableDoughAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == linkerId, () => $"selected={viewModel.SelectedProduct?.Id}");

        await LinkWithUbeAsync(viewModel, ubeId, addInGrams: 20m, weightPerPiece: 30m);

        viewModel.ErrorMessage.Should().BeNull();
        var usage = viewModel.UsageEdits.Should().ContainSingle().Subject;
        usage.IsLinked.Should().BeTrue();
        usage.BaseTotalWeight.Should().Be(1000m, "the dough itself is still the dough");
        usage.TotalWeight.Should().Be(1020m, "the 20 g of ube goes into the batch");
        usage.TotalCost.Should().Be(66m, "1000*0.05 + 20*0.80");
        usage.CostPerPiece.Should().Be(66m * 30m / 1020m);

        // The form is showing it, and its two tables add up to the batch the piece is cut from.
        viewModel.IsFormShowingAddIns.Should().BeTrue();
        viewModel.UsageFormTotalWeightText.Should().Be("1,020 g");
        viewModel.FormAddIns.Single().Grams.Should().Be(20m);

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var saved = await dbContext.UsageAddInLines.AsNoTracking().Where(l => l.Usage!.ProductId == linkerId).ToListAsync(Cancellation);
        saved.Select(l => (l.IngredientId, l.Grams)).Should().Equal((ubeId, 20m));
    }

    [Fact]
    public async Task An_add_in_already_in_the_linked_recipe_is_refused_on_the_spot()
    {
        var (linkerId, _, _, flourId) = await SeedLinkableDoughAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == linkerId, () => $"selected={viewModel.SelectedProduct?.Id}");

        await LinkWithUbeAsync(viewModel, flourId, addInGrams: 100m, weightPerPiece: 30m);

        viewModel.ErrorMessage.Should().NotBeNull();
        viewModel.ErrorMessage.Should().Contain("Flour").And.Contain("Sweet Dough");
        viewModel.UsageEdits.Should().BeEmpty("nothing goes into the recipe until the form is put right");
    }

    [Fact]
    public async Task Make_a_Copy_folds_the_add_ins_into_the_products_own_lines()
    {
        var (linkerId, _, ubeId, flourId) = await SeedLinkableDoughAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == linkerId, () => $"selected={viewModel.SelectedProduct?.Id}");
        await LinkWithUbeAsync(viewModel, ubeId, addInGrams: 20m, weightPerPiece: 30m);

        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());

        var copy = viewModel.UsageEdits.Should().ContainSingle().Subject;
        copy.IsOwned.Should().BeTrue();
        copy.AddIns.Should().BeEmpty("a sub-assembly of the product's own has no add-ins; its lines are its own");
        copy.Lines.Select(l => (l.Ingredient.Id, l.Grams)).Should().Equal([(flourId, 1000m), (ubeId, 20m)]);
        copy.TotalWeight.Should().Be(1020m, "the batch it was showing is the batch it keeps");
        copy.CostPerPiece.Should().Be(66m * 30m / 1020m);
    }

    [Fact]
    public async Task Saving_twice_with_no_change_leaves_a_linked_usages_add_ins_alone()
    {
        var (linkerId, _, ubeId, _) = await SeedLinkableDoughAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == linkerId, () => $"selected={viewModel.SelectedProduct?.Id}");
        await LinkWithUbeAsync(viewModel, ubeId, addInGrams: 20m, weightPerPiece: 30m);
        await viewModel.SaveCommand.Execute();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        int AddInIdAsync()
        {
            using var dbContext = CreateDbContext();
            return dbContext.UsageAddInLines.Single(l => l.Usage!.ProductId == linkerId).Id;
        }

        var before = AddInIdAsync();

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        AddInIdAsync().Should().Be(before, "a recipe that didn't change isn't rewritten — the draft is compared field by field, not by record equality");
    }

    [Fact]
    public async Task A_repriced_add_in_ingredient_reaches_the_usage_on_a_refresh()
    {
        var (linkerId, _, ubeId, _) = await SeedLinkableDoughAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == linkerId, () => $"selected={viewModel.SelectedProduct?.Id}");
        await LinkWithUbeAsync(viewModel, ubeId, addInGrams: 20m, weightPerPiece: 30m);
        await viewModel.SaveCommand.Execute();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.UsageEdits.Single().TotalCost.Should().Be(66m);

        // The ube doubles on the Raw Materials screen; coming back to Products re-reads it.
        await using (var dbContext = CreateDbContext())
        {
            var ube = await dbContext.RawMaterials.SingleAsync(m => m.Id == ubeId, Cancellation);
            ube.CostPerBaseUnit = 1.6m;
            ube.BufferedCostPerBaseUnit = 1.6m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();

        var usage = viewModel.UsageEdits.Single();
        usage.TotalCost.Should().Be(82m, "1000*0.05 + 20*1.60 — the add-in is repriced like any other line");
        usage.CostPerPiece.Should().Be(82m * 30m / 1020m);
        viewModel.FormAddIns.Single().Cost.Should().Be(32m, "the form's own table is repriced in place, not refilled");
    }
}
