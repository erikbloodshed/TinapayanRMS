using System.Collections.Specialized;
using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
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
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
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
        var roleId = await AddRoleAsync("Filling");
        await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Ube pandesal";
        viewModel.EditingDescription = "Soft bread with ube filling";
        await viewModel.AddUsageCommand.Execute();
        viewModel.IsFormEditingLines.Should().BeTrue("a new sub-assembly is made in the form");
        viewModel.EditingUsageRole!.Name.Should().Be("Filling", "the add form starts on the first role the recipe doesn't have yet");
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
            subAssembly.Name.Should().BeNull("a product's own sub-assembly goes by its role");
            subAssembly.RoleId.Should().Be(roleId);
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
    public async Task Packaging_lines_are_typed_into_the_table_in_each_materials_base_unit_and_saved()
    {
        int boxId, ribbonId;
        await using (var dbContext = CreateDbContext())
        {
            var category = new RawMaterialCategory { Name = "Packaging" };
            var box = new RawMaterial
            {
                Name = "Cake Box", Type = RawMaterialType.Packaging, Category = category, BaseUnit = new UnitOfMeasure { Name = "piece", Abbreviation = "pc" },
                CostPerBaseUnit = 12m, BufferedCostPerBaseUnit = 12m,
            };
            var ribbon = new RawMaterial
            {
                Name = "Ribbon", Type = RawMaterialType.Packaging, Category = category, BaseUnit = new UnitOfMeasure { Name = "centimeter", Abbreviation = "cm" },
                CostPerBaseUnit = 0.02m, BufferedCostPerBaseUnit = 0.02m,
            };
            dbContext.RawMaterials.AddRange(box, ribbon);
            await dbContext.SaveChangesAsync(Cancellation);
            (boxId, ribbonId) = (box.Id, ribbon.Id);
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Gift loaf";
        viewModel.IsPackagingEditable.Should().BeTrue();

        // The row the add row edits shows the material's base unit and prices itself as soon as the material is picked.
        var boxLine = viewModel.NewLineEdit();
        boxLine.PackagingMaterialId = boxId;
        boxLine.QtyPerUnit = 0.5m;
        boxLine.Unit.Should().Be("pc");
        boxLine.UnitCost.Should().Be(12m);
        boxLine.CostPerUnit.Should().Be(6m, "half a ₱12 box a piece");
        viewModel.LineEdits.Add(boxLine);

        var ribbonLine = viewModel.NewLineEdit();
        ribbonLine.PackagingMaterialId = ribbonId;
        ribbonLine.QtyPerUnit = 30m;
        ribbonLine.Unit.Should().Be("cm");
        viewModel.LineEdits.Add(ribbonLine);
        viewModel.Costs.Packaging.Should().Be(6.6m, "₱6 of box and 30 cm of ribbon at ₱0.02 a cm");

        // A line with a material but no quantity is refused, and deleting it lets Save through.
        var halfLine = viewModel.NewLineEdit();
        halfLine.PackagingMaterialId = boxId;
        viewModel.LineEdits.Add(halfLine);
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().Contain("Cake Box");
        viewModel.SelectedLineEdit = halfLine;
        await viewModel.RemoveLineCommand.Execute();

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var lines = await dbContext.PackagingLines.OrderBy(l => l.PackagingMaterialId).ToListAsync(Cancellation);
            lines.Should().HaveCount(2);
            lines.Should().AllSatisfy(l =>
            {
                l.UnitId.Should().BeNull("a line is always in its material's base unit");
                l.AuthoredField.Should().Be(PackagingLineAuthoredField.QtyPerUnit);
            });
            lines[0].PackagingMaterialId.Should().Be(boxId);
            lines[0].QtyPerUnit.Should().Be(0.5m);
            lines[1].QtyPerUnit.Should().Be(30m);
        }

        await WaitUntilSettledAsync(
            () => viewModel.SelectedProduct is not null && viewModel.LineEdits.Count == 2 && !viewModel.LineEdits.Contains(boxLine),
            () => $"selected={viewModel.SelectedProduct?.Id}, lines={viewModel.LineEdits.Count}");
        viewModel.LineEdits.Single(l => l.PackagingMaterialId == ribbonId).QtyPerUnit.Should().Be(30m);
        viewModel.Costs.Packaging.Should().Be(6.6m);
    }

    [Fact]
    public async Task A_packaging_line_saved_in_another_unit_shows_in_the_materials_base_unit_at_the_same_cost()
    {
        var productId = await AddProductAsync("Gift loaf");
        await using (var dbContext = CreateDbContext())
        {
            var roll = new RawMaterialUnit { UnitOfMeasure = new UnitOfMeasure { Name = "roll", Abbreviation = "roll" }, BaseUnitsPerUnit = 1000m };
            var ribbon = new RawMaterial
            {
                Name = "Ribbon", Type = RawMaterialType.Packaging, Category = new RawMaterialCategory { Name = "Packaging" },
                BaseUnit = new UnitOfMeasure { Name = "centimeter", Abbreviation = "cm" },
                CostPerBaseUnit = 0.02m, BufferedCostPerBaseUnit = 0.02m, Units = [roll],
            };
            dbContext.RawMaterials.Add(ribbon);
            await dbContext.SaveChangesAsync(Cancellation);

            // One roll serves 50 pieces: 20 cm a piece.
            dbContext.PackagingLines.Add(new PackagingLine
            {
                ProductId = productId, PackagingMaterialId = ribbon.Id, UnitId = roll.Id,
                AuthoredField = PackagingLineAuthoredField.UnitsPerPackagingUnit, UnitsPerPackagingUnit = 50m, QtyPerUnit = 0.02m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.LineEdits.Count == 1, () => $"lines={viewModel.LineEdits.Count}");

        var line = viewModel.LineEdits.Single();
        line.QtyPerUnit.Should().Be(20m);
        line.Unit.Should().Be("cm");
        viewModel.Costs.Packaging.Should().Be(0.4m);
    }


    [Fact]
    public async Task A_typed_yield_is_kept_and_the_weight_per_piece_follows_the_batch()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (productId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1200m)], weightPerPiece: 50m);
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
        var (bigId, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        // Link Cream Loaf's dough into the mini, at its own weight.
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == miniId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == miniId, () => $"selected={viewModel.SelectedProduct?.Id}");
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageMode = viewModel.UsageFormModes.Single(m => m.Mode == UsageFormMode.Link);
        viewModel.IsFormEditingLines.Should().BeFalse("a link's lines are its owner's");
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => !o.IsPackaging);
        viewModel.EditingUsageSource!.DisplayName.Should().Be("Dough — Cream Loaf", "another product's sub-assembly goes by its role");
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
            [new OwnedSubAssemblyDraft(doughId, roleId, [(flourId, 1000m), (butterId, 250m)], UsageAuthoredField.WeightPerPiece, 60m)], []));
        await viewModel.LoadCommand.Execute();
        viewModel.UsageEdits.Single().TotalWeight.Should().Be(1250m);
        viewModel.UsageEdits.Single().CostPerPiece.Should().Be(3.6m, "30 g of a ₱150, 1,250 g batch");
        viewModel.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should()
            .Equal([("Flour", 1000m), ("Butter", 250m)], "the read-only lines show the owner's, as they now are");

        // Make a Copy: the mini gets its own dough, at the same weight, and stops following.
        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());
        var copy = viewModel.UsageEdits.Single();
        copy.IsOwned.Should().BeTrue();
        copy.RoleName.Should().Be("Dough");
        viewModel.RecipeTabs[0].Header.Should().Be("Dough");
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
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)]);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(miniId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Name == "Cream Loaf");
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        var shared = viewModel.UsageEdits.Single();
        shared.IsShared.Should().BeTrue();
        shared.SharedText.Should().Be("Also used by Cream Loaf Mini");
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Usage.Should().BeSameAs(shared);

        await viewModel.RemoveUsageCommand.Execute();

        viewModel.ErrorMessage.Should().StartWith("Cream Loaf Mini links Cream Loaf's Dough");
        viewModel.UsageEdits.Should().ContainSingle();
    }

    [Fact]
    public async Task The_recipe_tabs_are_each_sub_assembly_in_recipe_order_then_Packaging()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
                new OwnedSubAssemblyDraft(0, doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.RecipeTabs.Should().BeEmpty("a new product starts with nothing in its recipe, packaging included");
        viewModel.HasRecipeTabs.Should().BeFalse();
        viewModel.SelectedRecipeTab.Should().BeNull();
        viewModel.IsUsageFormOpen.Should().BeFalse("nothing is being added until Add Sub-Assembly is pressed");

        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.RecipeTabs[0]);
        viewModel.EditingUsage.Should().BeSameAs(((SubAssemblyRecipeTab)viewModel.RecipeTabs[0]).Usage, "a sub-assembly's tab is the form that edits it");
    }

    [Fact]
    public async Task A_role_reordered_in_Settings_moves_its_tab_on_the_next_refresh()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new OwnedSubAssemblyDraft(0, fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling");

        // Filling now sorts before Dough, the way Settings' Roles screen would save it.
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.SubAssemblyRoles.FindAsync([doughRoleId], Cancellation))!.SortOrder = 2;
            (await dbContext.SubAssemblyRoles.FindAsync([fillingRoleId], Cancellation))!.SortOrder = 1;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();

        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "Dough");
    }

    [Fact]
    public async Task Adding_sub_assemblies_out_of_role_order_still_sorts_their_tabs_by_role()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Ube Roll";

        // Filling (role order 2) typed first, then Dough (role order 1) — the reverse of role order.
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == fillingRoleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = ubeId, Grams = 100m });
        viewModel.EditingUsageWeightPerPiece = 10m;
        await viewModel.ApplyUsageCommand.Execute();

        await viewModel.AddUsageCommand.Execute();
        viewModel.FormRoleOptions.Select(r => r.Name).Should().Equal(["Dough", "Filling"], "a recipe can have more than one in a role");
        viewModel.EditingUsageRole!.Name.Should().Be("Dough", "the first role the recipe doesn't have yet");
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == doughRoleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1000m });
        viewModel.EditingUsageWeightPerPiece = 50m;
        await viewModel.ApplyUsageCommand.Execute();

        // Dough sorts before Filling regardless of which was typed in first, and it's the tab showing.
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.RecipeTabs[0]);
    }

    [Fact]
    public async Task Changing_a_sub_assemblys_role_on_Apply_moves_its_tab_to_where_the_new_role_sorts()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new OwnedSubAssemblyDraft(0, fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling");
        var doughTab = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single(t => t.Usage.RoleName == "Dough");

        // Re-role the Dough as a Topping (order 3) — it should now sort after the Filling (order 2), and be called Topping.
        await viewModel.EditUsageCommand.Execute(doughTab.Usage);
        viewModel.FormRoleOptions.Select(r => r.Name).Should().Equal(["Dough", "Filling", "Topping"], "every role, the recipe's others too");
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == toppingRoleId);
        await viewModel.ApplyUsageCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "Topping");
    }

    [Fact]
    public async Task A_second_sub_assembly_in_a_role_needs_a_name_and_the_tabs_then_read_by_their_names()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        await AddProductWithSubAssemblyAsync("Cream Loaf", doughRoleId, [(flourId, 1000m)]);
        await AddProductWithSubAssemblyAsync("Ube Bun", fillingRoleId, [(ubeId, 100m)]);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(0, doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)], []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Linking: Cream Loaf's Dough is offered too, though the recipe has a Dough; it would go by its owner's name.
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageMode = viewModel.UsageFormModes.Single(m => m.Mode == UsageFormMode.Link);
        viewModel.LinkableSubAssemblies.Where(o => !o.IsPackaging).Select(o => o.DisplayName).Should().Equal("Dough — Cream Loaf", "Filling — Ube Bun");

        // A new one in the Dough role with no name, beside the Dough that has none: refused, and nothing goes in.
        viewModel.EditingUsageMode = viewModel.UsageFormModes.Single(m => m.Mode == UsageFormMode.New);
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == doughRoleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = ubeId, Grams = 500m });
        viewModel.EditingUsageWeightPerPiece = 20m;
        await viewModel.ApplyUsageCommand.Execute();

        viewModel.ErrorMessage.Should().StartWith("This recipe has more than one Dough without a name");
        viewModel.UsageEdits.Should().ContainSingle();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab, "the form has to be put right first");

        // Named, it goes in, and the second Dough's tab reads by its name; so does the Cost Overview's slice.
        viewModel.EditingUsageName = "Ube";
        await viewModel.ApplyUsageCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header).Should().Equal("Dough", "Dough: Ube");
        viewModel.CostShares.Select(c => c.Name).Take(2).Should().Equal("Dough", "Dough: Ube");

        // Saved and loaded again, both are there, by the same names.
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        var reloaded = CreateViewModel();
        await reloaded.LoadCommand.Execute();
        reloaded.SelectedProduct = reloaded.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => reloaded.UsageEdits.Count == 2, () => $"usage rows={reloaded.UsageEdits.Count}");
        reloaded.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header).Should().Equal("Dough", "Dough: Ube");

        // Taking the other one out leaves the named one alone in its role: it's plain "Dough" again.
        reloaded.SelectedRecipeTab = reloaded.RecipeTabs[0];
        await reloaded.RemoveUsageCommand.Execute();
        reloaded.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header).Should().Equal("Dough");
        reloaded.UsageEdits.Single().Name.Should().Be("Ube", "it keeps its name, for when another joins it");
    }
    [Fact]
    public async Task Save_leaves_the_Add_Sub_Assembly_form_alone_when_nothing_was_entered_in_it()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();
        await viewModel.AddUsageCommand.Execute();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.IsUsageFormOpen.Should().BeTrue();
        viewModel.EditingName = "Gift Box Set";

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.SelectedProduct is not null && !viewModel.RecipeTabs.Contains(viewModel.AddSubAssemblyTab),
            () => $"selected={viewModel.SelectedProduct?.Id}, tabs={viewModel.RecipeTabs.Count}");
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(Cancellation)).Name.Should().Be("Gift Box Set");
            (await dbContext.ProductSubAssemblyUsages.CountAsync(Cancellation)).Should().Be(0);
        }

        // The reload shows the saved product's tabs — none — and the New Sub-Assembly tab, with nothing in it, goes.
        viewModel.RecipeTabs.Should().BeEmpty();
        viewModel.SelectedRecipeTab.Should().BeNull();
    }

    [Fact]
    public async Task Add_Sub_Assembly_opens_a_New_Sub_Assembly_tab_that_goes_once_another_tab_shows()
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(0, fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        await GivePackagingAsync(productId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        (await viewModel.RemoveUsageCommand.CanExecute.FirstAsync()).Should().BeTrue("a sub-assembly's tab is showing");

        // Add Sub-Assembly: its tab goes before Packaging and shows the add form, on the first role the recipe doesn't
        // have. Adding again, or deleting, isn't offered there.
        await viewModel.AddUsageCommand.Execute();
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "New Sub-Assembly", "Packaging");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.IsUsageFormOpen.Should().BeTrue();
        viewModel.IsAddingUsage.Should().BeTrue();
        viewModel.FormRoleOptions.Select(r => r.Name).Should().Equal(["Dough", "Filling", "Topping"]);
        viewModel.EditingUsageRole!.Name.Should().Be("Dough");
        (await viewModel.AddUsageCommand.CanExecute.FirstAsync()).Should().BeFalse();
        (await viewModel.RemoveUsageCommand.CanExecute.FirstAsync()).Should().BeFalse();

        // Left with nothing entered, as a click on another tab leaves it: the form closes and the tab goes.
        viewModel.SelectedRecipeTab = viewModel.PackagingTab;
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "Packaging");
        viewModel.IsUsageFormOpen.Should().BeFalse();
        (await viewModel.RemoveUsageCommand.CanExecute.FirstAsync()).Should().BeTrue("Delete Sub-Assembly takes the packaging out too");

        // Left with a sub-assembly typed in: it's applied, in role order, and its tab goes.
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == toppingRoleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = cheeseId, Grams = 200m });
        viewModel.EditingUsageWeightPerPiece = 20m;
        viewModel.SelectedRecipeTab = viewModel.PackagingTab;

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "Topping", "Packaging");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.PackagingTab);

        // Applied with Apply: the Dough is the first tab, since Dough is the first role, and it's the tab showing.
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageRole!.Name.Should().Be("Dough", "the only role the recipe doesn't have yet");
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 800m });
        viewModel.EditingUsageWeightPerPiece = 40m;
        await viewModel.ApplyUsageCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Topping", "Packaging");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.RecipeTabs[0]);
        viewModel.EditingUsage!.RoleName.Should().Be("Dough");

        // Opened from a sub-assembly's tab and cancelled: back on that tab, and the New Sub-Assembly tab goes.
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageRole!.Name.Should().Be("Dough", "the recipe has one in every role, so the first is picked");
        await viewModel.CancelUsageCommand.Execute();
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Topping", "Packaging");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.RecipeTabs[0]);
    }

    [Fact]
    public async Task Editing_a_sub_assembly_opens_the_form_in_its_tab_which_stays_through_a_refresh_and_Apply()
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        var tab = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single();

        await viewModel.EditUsageCommand.Execute(tab.Usage);
        viewModel.EditingUsage.Should().BeSameAs(tab.Usage);
        viewModel.FormLines.Single().Grams = 150m;

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
        viewModel.UsageEdits.Should().ContainSingle("Apply replaces the refreshed copy instead of adding a second").Which.TotalWeight.Should().Be(150m);
        viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Should().ContainSingle().Which.Should().BeSameAs(tab);
        viewModel.SelectedRecipeTab.Should().BeSameAs(tab);
        viewModel.EditingUsage.Should().BeSameAs(tab.Usage, "the tab has nothing else to show, so its form stays open on what was applied");
        tab.Usage.CostPerPiece.Should().Be(3.6m, "10 g a piece of a 150 g, ₱54 batch");
        tab.Header.Should().Be("Filling");
    }

    [Fact]
    public async Task Deleting_the_sub_assembly_showing_shows_the_next_tab_and_cancelling_an_add_goes_back()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Twin Rolls");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new OwnedSubAssemblyDraft(0, fillingRoleId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 40m),
            ],
            []));
        await GivePackagingAsync(productId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        // From Packaging, Add Sub-Assembly then Cancel: back on Packaging, with the form closed and the add's tab gone.
        viewModel.SelectedRecipeTab = viewModel.PackagingTab;
        await viewModel.AddUsageCommand.Execute();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.IsUsageFormOpen.Should().BeTrue();
        await viewModel.CancelUsageCommand.Execute();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.PackagingTab);
        viewModel.IsUsageFormOpen.Should().BeFalse();
        viewModel.RecipeTabs.Should().NotContain(viewModel.AddSubAssemblyTab);

        // Deleting the first sub-assembly while it shows: the next one's tab shows.
        var dough = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().First();
        viewModel.SelectedRecipeTab = dough;
        await viewModel.RemoveUsageCommand.Execute();
        viewModel.UsageEdits.Select(u => u.RoleName).Should().Equal("Filling");
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Header.Should().Be("Filling");

        // Deleting the last: Packaging, the only tab left, shows.
        await viewModel.RemoveUsageCommand.Execute();
        viewModel.RecipeTabs.Should().Equal(viewModel.PackagingTab);
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.PackagingTab);
        viewModel.IsUsageFormOpen.Should().BeFalse();
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
        viewModel.LinkableSubAssemblies.Where(o => !o.IsPackaging).Should().ContainSingle().Which.DisplayName.Should().Be("Chocolate Glaze — Sub-Assemblies (Glaze)");

        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(
            () => viewModel.SelectedProduct?.Id == productId && viewModel.RecipeTabs.Count == 0,
            () => $"selected={viewModel.SelectedProduct?.Id}, tabs={viewModel.RecipeTabs.Count}");
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageMode = UsageFormModeOption.All.Single(o => o.Mode == UsageFormMode.Link);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => !o.IsPackaging);
        await WaitUntilSettledAsync(() => viewModel.UsageFormTotalWeightText == "100 g", () => $"batch={viewModel.UsageFormTotalWeightText}");
        viewModel.EditingUsageWeightPerPiece = 5m;
        await viewModel.ApplyUsageCommand.Execute();

        var usage = viewModel.UsageEdits.Should().ContainSingle().Subject;
        usage.IsLinked.Should().BeTrue();
        usage.LinkedFromText.Should().Be("Chocolate Glaze, from Sub-Assemblies", "a standalone one keeps its name, which its tab doesn't show");
        usage.RoleName.Should().Be("Glaze");
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

    [Fact]
    public async Task Save_puts_the_product_in_the_picked_category_which_shows_again_when_it_is_selected_and_New_clears_it()
    {
        await AddProductCategoryAsync("Breads");
        var cakesId = await AddProductCategoryAsync("Cakes");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.Categories.Select(c => c.Name).Should().Equal("Breads", "Cakes");

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Chocolate Cake";
        viewModel.EditingCategory = viewModel.Categories.Single(c => c.Name == "Cakes");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(Cancellation)).CategoryId.Should().Be(cakesId);
        }

        viewModel.Products.Should().ContainSingle().Which.Category!.Name.Should().Be("Cakes", "the list groups the product under it");

        await viewModel.NewCommand.Execute();
        viewModel.EditingCategory.Should().BeNull("a new product starts uncategorized");

        viewModel.SelectedProduct = viewModel.Products.Single();
        viewModel.EditingCategory!.Name.Should().Be("Cakes");
    }

    [Fact]
    public async Task A_product_saved_without_a_category_is_uncategorized()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        await viewModel.NewCommand.Execute();
        viewModel.EditingName = "Pandesal";
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull("a category is optional");
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.SingleAsync(Cancellation)).CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task Reloading_keeps_the_picked_category_and_shows_a_rename_made_in_Settings()
    {
        var cakesId = await AddProductCategoryAsync("Cakes");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        await viewModel.NewCommand.Execute();
        viewModel.EditingCategory = viewModel.Categories.Single();

        await new ProductCategoryRepository(CreateDbContextFactory()).UpdateAsync(new ProductCategory { Id = cakesId, Name = "Cakes & Tortes" }, Cancellation);
        await viewModel.LoadCommand.Execute();

        viewModel.EditingCategory!.Name.Should().Be("Cakes & Tortes", "the unsaved pick points at the refreshed category");
        viewModel.CategoryOptions.Select(c => c.Name).Should().Equal("< Add New .. >", "Cakes & Tortes");
    }

    [Fact]
    public async Task Picking_Add_New_saves_the_category_it_asks_for_and_picks_it_for_the_product()
    {
        var viewModel = CreateViewModel();
        (string Label, string InitialValue) promptedWith = default;
        viewModel.PromptForNewCategory.RegisterHandler(interaction =>
        {
            promptedWith = interaction.Input;
            interaction.SetOutput("Pastries");
        });
        await viewModel.LoadCommand.Execute();
        viewModel.CategorySearchText = "Pas";

        viewModel.CategoryOptions.Should().ContainSingle(c => c.Id == ProductsViewModel.AddNewCategoryId)
            .Which.Name.Should().Be("< Add New \"Pas\" .. >", "the row keeps matching what's typed");
        viewModel.EditingCategory = viewModel.CategoryOptions.Single(c => c.Id == ProductsViewModel.AddNewCategoryId);

        await WaitUntilSettledAsync(() => viewModel.EditingCategory is not null,
            () => $"picked={viewModel.EditingCategory?.Name ?? "none"}, error={viewModel.ErrorMessage ?? "none"}");
        promptedWith.InitialValue.Should().Be("Pas", "the prompt starts with what was typed");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Pastries was added.");
        viewModel.EditingCategory!.Name.Should().Be("Pastries");
        viewModel.Categories.Select(c => c.Name).Should().Equal("Pastries");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductCategories.SingleAsync(Cancellation)).Name.Should().Be("Pastries");
    }

    [Fact]
    public async Task Cancelling_the_new_category_prompt_leaves_the_category_that_was_picked()
    {
        await AddProductCategoryAsync("Breads");
        var viewModel = CreateViewModel();
        var prompted = false;
        viewModel.PromptForNewCategory.RegisterHandler(interaction =>
        {
            prompted = true;
            interaction.SetOutput(null);
        });
        await viewModel.LoadCommand.Execute();
        viewModel.EditingCategory = viewModel.Categories.Single();

        viewModel.EditingCategory = ProductsViewModel.AddNewCategory;

        await WaitUntilSettledAsync(() => prompted, () => "the prompt was never asked for");
        viewModel.EditingCategory!.Name.Should().Be("Breads");
        viewModel.Categories.Should().ContainSingle("nothing was added");
    }

    private async Task<int> AddProductCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = new ProductCategory { Name = name };
        dbContext.ProductCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    /// <summary>Gives <paramref name="productId"/> a per-piece packaging line of a box, so it has a Packaging tab.</summary>
    private async Task<int> GivePackagingAsync(int productId, string boxName = "Box")
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        int boxId;
        await using (var dbContext = CreateDbContext())
        {
            var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Boxes", Cancellation)
                ?? new RawMaterialCategory { Name = "Boxes" };
            var box = new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = boxName,
                Category = category,
                BaseUnitId = pieceId,
                CostPerBaseUnit = 5m,
                BufferedCostPerBaseUnit = 5m,
            };
            dbContext.RawMaterials.Add(box);
            await dbContext.SaveChangesAsync(Cancellation);
            boxId = box.Id;
        }

        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(productId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
        return boxId;
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
            new ProductPackRepository(factory),
            new CostingParametersRepository(factory),
            new PackagingLinkRepository(factory, packagingLineRepository),
            new ProductCloner(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory, CreateCostPropagator()),
            new ProductCategoryRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
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
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Twin Rolls");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new OwnedSubAssemblyDraft(0, fillingRoleId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 40m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        var dough = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        // Typed into the Dough's form, then away to the Filling's tab: the typing is applied on the way out.
        viewModel.SelectedRecipeTab = dough;
        viewModel.EditingUsage.Should().BeSameAs(dough.Usage, "showing a sub-assembly's tab opens its form");
        viewModel.FormLines.Single().Grams = 1500m;
        viewModel.SelectedRecipeTab = filling;

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SelectedRecipeTab.Should().BeSameAs(filling);
        viewModel.EditingUsage.Should().BeSameAs(filling.Usage);
        dough.Usage.TotalWeight.Should().Be(1500m, "leaving its tab applied what was typed");

        // A form a rule refuses keeps its own tab, with the message shown: here, the other one's role, where neither has a name.
        viewModel.SelectedRecipeTab = dough;
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == fillingRoleId);
        viewModel.SelectedRecipeTab = filling;

        viewModel.SelectedRecipeTab.Should().BeSameAs(dough, "the form has to be put right before another tab can show");
        viewModel.ErrorMessage.Should().StartWith("This recipe has more than one Filling without a name");
        viewModel.EditingUsage.Should().BeSameAs(dough.Usage);

        // Cancel puts back what was last applied, and the tab can be left again.
        await viewModel.CancelUsageCommand.Execute();
        viewModel.EditingUsageRole!.Name.Should().Be("Dough");
        viewModel.SelectedRecipeTab = filling;
        viewModel.SelectedRecipeTab.Should().BeSameAs(filling);
    }

    [Fact]
    public async Task Merely_showing_a_sub_assemblys_tab_leaves_the_recipe_as_it_was()
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
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

    /// <summary>Cream Loaf owns a 1,000 g / ₱50 Dough; a second product links it. Returns the linker, the dough and ube.</summary>
    private async Task<(int LinkerId, int DoughId, int UbeId, int FlourId)> SeedLinkableDoughAsync()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var linkerId = await AddProductAsync("Cream Loaf Ube");
        return (linkerId, doughId, ubeId, flourId);
    }

    /// <summary>Selects <paramref name="productId"/> and links the dough into it with <paramref name="addInGrams"/> of ube.</summary>
    private static async Task LinkWithUbeAsync(ProductsViewModel viewModel, int ubeId, decimal addInGrams, decimal weightPerPiece)
    {
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageMode = viewModel.UsageFormModes.Single(m => m.Mode == UsageFormMode.Link);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => !o.IsPackaging);
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
    public async Task A_linked_recipes_add_ins_show_with_Show_Add_Ins_and_open_by_themselves_when_there_are_some()
    {
        var (linkerId, _, ubeId, flourId) = await SeedLinkableDoughAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == linkerId, () => $"selected={viewModel.SelectedProduct?.Id}");

        // A picked recipe to link: Show Add-Ins is offered under it, with the table shut.
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageMode = viewModel.UsageFormModes.Single(m => m.Mode == UsageFormMode.Link);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => !o.IsPackaging);
        await WaitUntilSettledAsync(() => viewModel.FormLines.Count == 1, () => $"form lines={viewModel.FormLines.Count}");
        viewModel.IsFormOfferingAddIns.Should().BeTrue();
        viewModel.IsFormShowingAddIns.Should().BeFalse("most links have no add-ins, so the table starts shut");
        viewModel.FormAddInsToggleText.Should().Be("Show Add-Ins");

        await viewModel.ToggleFormAddInsCommand.Execute();
        viewModel.IsFormShowingAddIns.Should().BeTrue();
        viewModel.FormAddInsToggleText.Should().Be("Hide Add-Ins");

        // Shut again with one typed in: the button counts it, and it still goes into the batch.
        viewModel.FormAddIns.Add(new IngredientLineRow(viewModel.Ingredients.Single(m => m.Id == ubeId), 20m));
        await viewModel.ToggleFormAddInsCommand.Execute();
        viewModel.IsFormShowingAddIns.Should().BeFalse();
        viewModel.FormAddInsToggleText.Should().Be("Show Add-Ins (1)");
        viewModel.UsageFormTotalWeightText.Should().Be("1,020 g");

        viewModel.EditingUsageWeightPerPiece = 30m;
        await viewModel.ApplyUsageCommand.Execute();

        // Its tab opens with them showing, since it has some.
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.EditingUsage!.AddIns.Should().ContainSingle();
        viewModel.IsFormShowingAddIns.Should().BeTrue();

        // Shut, then an add-in the linked recipe already has: refused, with the table opened on it.
        await viewModel.ToggleFormAddInsCommand.Execute();
        viewModel.FormAddIns.Add(new IngredientLineRow(viewModel.Ingredients.Single(m => m.Id == flourId), 50m));
        await viewModel.ApplyUsageCommand.Execute();

        viewModel.ErrorMessage.Should().Contain("Flour");
        viewModel.IsFormShowingAddIns.Should().BeTrue("the line the message is about has to be in sight to be put right");
    }

    [Fact]
    public async Task Picking_another_product_from_a_sub_assemblys_tab_opens_on_its_first_sub_assembly()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        await AddProductWithSubAssemblyAsync("Cream Loaf", fillingRoleId, [(cheeseId, 100m)]);
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(cheeseRollId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, fillingRoleId, [(cheeseId, 200m)], UsageAuthoredField.WeightPerPiece, 20m),
                new OwnedSubAssemblyDraft(0, doughRoleId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 40m),
            ],
            []));
        var viewModel = CreateViewModel();

        // As the tab strip does: the tab showing taken away, it shows the one that took its place, so the last
        // sub-assembly's to go leaves it on Packaging.
        viewModel.RecipeTabs.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems!.Contains(viewModel.SelectedRecipeTab))
            {
                viewModel.SelectedRecipeTab = viewModel.RecipeTabs.Count == 0
                    ? null
                    : viewModel.RecipeTabs[Math.Min(e.OldStartingIndex, viewModel.RecipeTabs.Count - 1)];
            }
        };

        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Name == "Cream Loaf");
        await WaitUntilSettledAsync(() => viewModel.SelectedRecipeTab?.Header == "Filling", () => $"tab={viewModel.SelectedRecipeTab?.Header}");

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == cheeseRollId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.RecipeTabs[0], "the Dough's tab comes first and shows, not the Packaging the strip was left on");
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
        viewModel.ErrorMessage.Should().Contain("Flour").And.Contain("Cream Loaf's Dough");
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

    [Fact]
    public async Task Ticking_a_line_as_the_base_is_saved_and_reads_as_a_hundred_percent()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        var productId = await AddProductAsync("Pandesal");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(0, roleId, [(flourId, 1000m), (waterId, 650m)], UsageAuthoredField.WeightPerPiece, 50m)],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Nothing ticked yet, so the lines read as absolute percentages: their shares of the 1,650 g batch.
        viewModel.UsageFormPercentHeader.Should().Be("Absolute %");
        viewModel.FormLines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(1000m / 1650m * 100m);
        viewModel.FormLines.Sum(l => l.Percent).Should().BeApproximately(100m, 0.0001m);
        viewModel.FormLines.Should().OnlyContain(l => !l.CanTypeBakersPercent, "an absolute percentage isn't typed");

        viewModel.FormLines.Single(l => l.Ingredient!.Id == flourId).IsFormulaBase = true;

        viewModel.UsageFormPercentHeader.Should().Be("Baker's %");

        viewModel.FormLines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(100m);
        viewModel.FormLines.Single(l => l.Ingredient!.Id == waterId).Percent.Should().Be(65m, "650 g of water on 1,000 g of flour");
        viewModel.UsageFormBaseCaption.Should().Contain("1,000 g").And.Contain("165");

        // This is the trap: without CurrentRecipe() carrying the tick, Save treats the recipe as unchanged.
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.AsNoTracking()
            .Where(l => l.SubAssembly!.OwnerProductId == productId)
            .ToListAsync(Cancellation);
        lines.Single(l => l.IngredientId == flourId).IsFormulaBase.Should().BeTrue();
        lines.Single(l => l.IngredientId == waterId).IsFormulaBase.Should().BeFalse();
    }

    [Fact]
    public async Task Make_a_Copy_keeps_the_linked_recipes_base()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        var ownerId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(ownerId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true), new RecipeLine(waterId, 650m)],
                    UsageAuthoredField.WeightPerPiece, 60m),
            ],
            []));
        var doughId = await OwnedSubAssemblyIdAsync(ownerId, roleId);
        var linkerId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(linkerId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // The link shows the owner's base, read-only, and the percentages that follow from it.
        viewModel.FormLines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(100m);
        viewModel.IsFormEditingLines.Should().BeFalse("a linked recipe's base is its owner's to set");

        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());

        var copy = viewModel.UsageEdits.Should().ContainSingle().Subject;
        copy.IsOwned.Should().BeTrue();
        copy.Lines.Single(l => l.Ingredient.Id == flourId).IsFormulaBase.Should().BeTrue("the copy is the same formula");
        copy.Lines.Single(l => l.Ingredient.Id == waterId).IsFormulaBase.Should().BeFalse();
    }

    [Fact]
    public async Task Add_ins_take_their_percentage_from_the_linked_recipes_base()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        var ownerId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(ownerId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true)], UsageAuthoredField.WeightPerPiece, 60m),
            ],
            []));
        var doughId = await OwnedSubAssemblyIdAsync(ownerId, roleId);
        var linkerId = await AddProductAsync("Cream Loaf Ube");
        await SaveRecipeAsync(linkerId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(ubeId, 20m)] }]));

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // 20 g of ube on the recipe's 1,000 g of flour is 2% — not 100% of its own 20 g.
        viewModel.FormAddIns.Single().Percent.Should().Be(2m);
    }

    [Fact]
    public async Task With_no_base_a_linked_recipe_and_its_add_ins_read_as_shares_of_the_whole_batch()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        var ownerId = await AddProductAsync("Glazed Bun");
        await SaveRecipeAsync(ownerId, new RecipeDraft(
            [new OwnedSubAssemblyDraft(0, roleId, [(sugarId, 900m)], UsageAuthoredField.WeightPerPiece, 10m)],
            []));
        var glazeId = await OwnedSubAssemblyIdAsync(ownerId, roleId);
        var linkerId = await AddProductAsync("Ube Glazed Bun");
        await SaveRecipeAsync(linkerId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 10m) { AddIns = [(ubeId, 100m)] }]));

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // The batch is the recipe's 900 g and the 100 g add-in: 90% and 10%, together 100%.
        viewModel.UsageFormPercentHeader.Should().Be("Absolute %");
        viewModel.UsageFormBaseCaption.Should().StartWith("Absolute %");
        viewModel.FormLines.Single().Percent.Should().Be(90m);
        viewModel.FormAddIns.Single().Percent.Should().Be(10m);
    }

    [Fact]
    public async Task A_typed_percentage_is_saved_and_follows_the_flour_while_typed_grams_stay_put()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        var eggsId = await AddIngredientAsync("Eggs", costPerGram: 0.1m);
        var productId = await AddProductAsync("Pandesal");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new OwnedSubAssemblyDraft(0, roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true), (waterId, 600m), (eggsId, 100m)],
                    UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        var water = viewModel.FormLines.Single(l => l.Ingredient!.Id == waterId);
        water.CanTypeBakersPercent.Should().BeTrue();
        water.Percent = 65m;
        water.Grams.Should().Be(650m);

        // The trap: Save only writes a recipe it sees as changed, so the typed percentage has to reach the draft.
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Reopened from what was saved, the flour doubles: the typed 65% follows it, the typed 100 g of eggs doesn't.
        viewModel.FormLines.Single(l => l.Ingredient!.Id == flourId).Grams = 2000m;

        viewModel.FormLines.Single(l => l.Ingredient!.Id == waterId).Grams.Should().Be(1300m);
        viewModel.FormLines.Single(l => l.Ingredient!.Id == waterId).IsPercentTyped.Should().BeTrue();
        viewModel.FormLines.Single(l => l.Ingredient!.Id == eggsId).Grams.Should().Be(100m);

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.AsNoTracking()
            .Where(l => l.SubAssembly!.OwnerProductId == productId)
            .ToListAsync(Cancellation);
        lines.Single(l => l.IngredientId == waterId).Grams.Should().Be(1300m);
        lines.Single(l => l.IngredientId == waterId).AuthoredBakersPercent.Should().Be(65m);
        lines.Single(l => l.IngredientId == eggsId).Grams.Should().Be(100m);
        lines.Single(l => l.IngredientId == eggsId).AuthoredBakersPercent.Should().BeNull();
    }
}
