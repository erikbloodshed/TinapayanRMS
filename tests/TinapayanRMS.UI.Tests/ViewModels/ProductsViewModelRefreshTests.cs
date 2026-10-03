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
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

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
        viewModel.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered).Should().Be(2.4m);
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
        viewModel.UsageEdits.Should().ContainSingle().Which.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.6m);
        viewModel.Costs.Material.Should().Be(3.6m);
        var (expectedBaseCost, _, _) = CostingRules.CalculateSellingPrice(3.6m, 0m, 5m, 0m, 0m, 0m, 0m);
        viewModel.Costs.BaseCost.Should().Be(expectedBaseCost);
    }

    [Fact]
    public async Task Picking_another_product_keeps_the_previous_details_until_its_own_arrive_and_Save_waits_for_them()
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.5m);
        var (_, ubeFillingId) = await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)]);
        var (cheeseRollId, cheeseFillingId) = await AddProductWithSubAssemblyAsync("Cheese roll", roleId, [(cheeseId, 100m)]);
        DateTime? cheeseRollUpdatedAt;
        await using (var dbContext = CreateDbContext())
        {
            cheeseRollUpdatedAt = (await dbContext.Products.SingleAsync(p => p.Id == cheeseRollId, Cancellation)).UpdatedAt;
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Name == "Ube pandesal");
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.SingleOrDefault()?.SubAssemblyId == ubeFillingId,
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}");

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == cheeseRollId);

        // Its details are still on their way (read off the UI thread): the ube pandesal's show until they
        // arrive, not an empty recipe, and a Save now would write that recipe under the cheese roll's name.
        viewModel.AreDetailsReady.Should().BeFalse();
        viewModel.EditingName.Should().Be("Cheese roll");
        viewModel.UsageEdits.Should().ContainSingle().Which.SubAssemblyId.Should().Be(ubeFillingId);
        viewModel.HasRecipeTabs.Should().BeTrue();
        await viewModel.SaveCommand.Execute();

        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.SingleOrDefault()?.SubAssemblyId == cheeseFillingId,
            () => $"ready={viewModel.AreDetailsReady}, usages={string.Join(",", viewModel.UsageEdits.Select(u => u.SubAssemblyId))}");
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Usage.SubAssemblyId.Should().Be(cheeseFillingId);
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(p => p.Id == cheeseRollId, Cancellation)).UpdatedAt.Should().Be(cheeseRollUpdatedAt, "the Save did nothing");
            (await dbContext.ProductSubAssemblyUsages.Where(u => u.ProductId == cheeseRollId).Select(u => u.SubAssemblyId).ToListAsync(Cancellation))
                .Should().Equal(cheeseFillingId);
        }
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
        viewModel.EditingLaborPercent = 50m;
        viewModel.EditingAdministrativePercent = 12.5m;

        // The sub-assembly's form is left open: the one Save puts it in the recipe, shows its tab, and writes the lot.
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.SingleOrDefault()?.IsLinked == true,
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}");
        var usage = viewModel.UsageEdits.Should().ContainSingle().Subject;
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>("Save shows the new sub-assembly's tab").Which.Usage.Should().BeSameAs(usage);
        usage.IsLinked.Should().BeTrue("Save keeps it on the Sub-Assemblies screen, and the product links it");
        usage.LinkedFromText.Should().Be("Ube pandesal Filling, from Sub-Assemblies");
        viewModel.IsFormEditingLines.Should().BeFalse("its lines are edited on the Sub-Assemblies screen from now on");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(2.4m);
        viewModel.Costs.Labor.Should().Be(1.2m, "labor is 50% of the ₱2.40 material cost");
        viewModel.Costs.Administrative.Should().Be(0.3m, "administrative is 12.5% of the ₱2.40 material cost");
        viewModel.Costs.BaseCost.Should().Be(3.9m, "₱2.40 of filling, ₱1.20 labor and ₱0.30 administrative");

        int productId;
        await using (var dbContext = CreateDbContext())
        {
            var product = await dbContext.Products.SingleAsync(Cancellation);
            productId = product.Id;
            product.Name.Should().Be("Ube pandesal");
            product.Description.Should().Be("Soft bread with ube filling");
            var subAssembly = await dbContext.SubAssemblies.SingleAsync(Cancellation);
            subAssembly.Name.Should().Be("Ube pandesal Filling");
            subAssembly.RoleId.Should().Be(roleId);
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
        boxLine.BaseUnitAbbreviation.Should().Be("pc");
        boxLine.UnitCost.Should().Be(12m);
        boxLine.CostPerUnit.Should().Be(6m, "half a ₱12 box a piece");
        viewModel.LineEdits.Add(boxLine);

        var ribbonLine = viewModel.NewLineEdit();
        ribbonLine.PackagingMaterialId = ribbonId;
        ribbonLine.QtyPerUnit = 30m;
        ribbonLine.BaseUnitAbbreviation.Should().Be("cm");
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
    public async Task A_packaging_line_saved_in_another_unit_shows_in_that_unit_with_what_it_comes_to_in_base_units()
    {
        var productId = await AddProductAsync("Gift loaf");
        int rollId;
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
            rollId = roll.Id;
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.LineEdits.Count == 1, () => $"lines={viewModel.LineEdits.Count}");

        var line = viewModel.LineEdits.Single();
        line.QtyPerUnit.Should().Be(0.02m, "a line saved as 50 pieces per roll is 0.02 of a roll a piece");
        line.UnitId.Should().Be(rollId, "the line stays in the unit it was saved in");
        line.BaseQty.Should().Be(20m, "0.02 of a 1,000 cm roll is 20 cm");
        line.BaseUnitAbbreviation.Should().Be("cm");
        viewModel.Costs.Packaging.Should().Be(0.4m, "20 cm at ₱0.02, as before");
    }


    [Fact]
    public async Task Saving_again_before_the_reload_arrives_keeps_a_new_section_only_once()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Pandesal");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == productId && viewModel.AreDetailsReady, () => $"selected={viewModel.SelectedProduct?.Id}");
        await viewModel.AddUsageCommand.Execute(UsageFormMode.New);
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == roleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1000m });
        viewModel.EditingUsageWeightPerPiece = 50m;

        // The second Save lands before the first one's reload, while the form still shows the section as new.
        await viewModel.SaveCommand.Execute();
        await viewModel.SaveCommand.Execute();
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.Select(s => s.Name).ToListAsync(Cancellation)).Should().Equal("Pandesal Dough");
    }

    [Fact]
    public async Task Make_a_Copy_of_a_section_saved_from_this_product_is_kept_under_the_next_number()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (productId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1000m)], weightPerPiece: 50m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single().Header.Should().Be("Dough");

        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());
        viewModel.IsFormEditingLines.Should().BeTrue("the copy's lines are the form's to change");
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 100m });
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.SingleOrDefault()?.IsLinked == true,
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}");
        var usage = viewModel.UsageEdits.Single();
        usage.LinkedFromText.Should().Be("Pandesal Dough 2, from Sub-Assemblies", "Pandesal Dough is still kept, unused");
        usage.TotalWeight.Should().Be(1100m);
        usage.WeightPerPiece.Should().Be(50m);
        viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single().Header.Should().Be("Dough");
        (await new SubAssemblyRepository(CreateDbContextFactory()).GetByIdsAsync([doughId], Cancellation))
            .Should().ContainSingle().Which.TotalWeight.Should().Be(1000m, "the sub-assembly it was a copy of is left as it was");
    }

    [Fact]
    public async Task Copy_an_existing_one_takes_its_role_and_lines_but_not_its_name()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var sourceId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m, SectionName: "Cheese")], []));
        var productId = await AddProductAsync("Ube Roll");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == productId && viewModel.AreDetailsReady, () => $"selected={viewModel.SelectedProduct?.Id}");

        await viewModel.AddUsageCommand.Execute(UsageFormMode.Copy);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => !o.IsPackaging);
        await WaitUntilSettledAsync(() => viewModel.FormLines.Count == 1, () => $"form lines={viewModel.FormLines.Count}");

        viewModel.EditingUsageRole!.Id.Should().Be(fillingRoleId);
        viewModel.EditingUsageName.Should().BeNull("\"Cheese Roll Filling: Cheese\" is another product's name; Save names the copy after this one");
        viewModel.EditingUsageWeightPerPiece = 10m;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.OrderBy(s => s.Id).Select(s => s.Name).ToListAsync(Cancellation))
            .Should().Equal("Cheese Roll Filling: Cheese", "Ube Roll Filling");
    }

    [Fact]
    public async Task Sections_in_one_role_saved_from_this_product_read_by_their_own_names_on_its_tabs()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var (_, ubeBunFillingId) = await AddProductWithSubAssemblyAsync("Ube Bun", fillingRoleId, [(ubeId, 100m)]);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
                new NewSubAssemblyDraft(fillingRoleId, [(ubeId, 50m)], UsageAuthoredField.WeightPerPiece, 5m, SectionName: "Ube"),
            ],
            [new LinkedUsageDraft(ubeBunFillingId, UsageAuthoredField.WeightPerPiece, 5m)]));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 3, () => $"usage rows={viewModel.UsageEdits.Count}");

        // "Cheese Roll Filling" goes by its role alone, "Cheese Roll Filling: Ube" by Ube, and Ube Bun's by its whole name.
        viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header)
            .Should().Equal("Filling: Ube Bun Filling", "Filling", "Filling: Ube");
    }

    [Fact]
    public async Task A_products_own_section_is_edited_in_its_recipe_and_Save_rewrites_it_in_place()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var fillingId = await SubAssemblyIdAsync(productId, fillingRoleId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1 && viewModel.EditingUsage is not null, () => $"usage rows={viewModel.UsageEdits.Count}");

        var usage = viewModel.UsageEdits.Single();
        usage.IsOwned.Should().BeTrue();
        usage.IsLinked.Should().BeFalse();
        viewModel.IsFormEditingLines.Should().BeTrue("its lines are the product's to type into");
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = milkId, Grams = 50m });
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.UsageEdits.Single().TotalWeight == 150m, () => $"ready={viewModel.AreDetailsReady}");
        var saved = viewModel.UsageEdits.Single();
        saved.SubAssemblyId.Should().Be(fillingId, "Save rewrites the product's own in place");
        saved.IsOwned.Should().BeTrue();
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Usage.Should().BeSameAs(saved);
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.SingleAsync(Cancellation)).TotalWeight.Should().Be(150m);
    }

    [Fact]
    public async Task Save_has_the_saved_product_showing_with_its_form_open_by_the_time_it_returns()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1 && viewModel.EditingUsage is not null, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = milkId, Grams = 50m });

        // The form closes while the product is picked again and opens on the reloaded section: in one pass, with nothing
        // left to arrive afterwards, or the screen draws the form without its section in between (a flicker on Save).
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.AreDetailsReady.Should().BeTrue();
        viewModel.IsUsageFormOpen.Should().BeTrue();
        var saved = viewModel.UsageEdits.Should().ContainSingle().Subject;
        saved.TotalWeight.Should().Be(150m, "the reloaded section, as saved");
        viewModel.EditingUsage.Should().BeSameAs(saved);
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Usage.Should().BeSameAs(saved);
        viewModel.FormLines.Select(l => l.Grams).Should().Equal(100m, 50m);
        viewModel.History.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task A_new_section_in_a_product_owned_role_is_the_products_own_after_Save()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Cheese Roll");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == productId && viewModel.AreDetailsReady, () => $"selected={viewModel.SelectedProduct?.Id}");
        await viewModel.AddUsageCommand.Execute(UsageFormMode.New);
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == fillingRoleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = cheeseId, Grams = 100m });
        viewModel.EditingUsageWeightPerPiece = 10m;

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.SingleOrDefault()?.IsOwned == true,
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}");
        viewModel.IsFormEditingLines.Should().BeTrue();
        viewModel.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Usage.Should().BeSameAs(viewModel.UsageEdits.Single());
        (await new SubAssemblyRepository(CreateDbContextFactory()).GetStandaloneAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Another_products_own_is_offered_to_copy_but_not_to_link()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var ownerId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(ownerId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m, SectionName: "Cheese")], []));
        var productId = await AddProductAsync("Cheese Bun");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == productId && viewModel.AreDetailsReady, () => $"selected={viewModel.SelectedProduct?.Id}");

        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.LinkableSubAssemblies.Where(o => !o.IsPackaging).Should().BeEmpty("a product's own is copied, never linked");

        await viewModel.AddUsageCommand.Execute(UsageFormMode.Copy);
        viewModel.LinkableSubAssemblies.Where(o => !o.IsPackaging).Select(o => o.DisplayName)
            .Should().Equal("Cheese Roll Filling: Cheese — Cheese Roll (Filling)");
    }

    [Fact]
    public async Task A_role_made_product_owned_in_Settings_turns_a_linked_section_into_the_products_own_on_coming_back()
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var (_, fillingId) = await AddProductWithSubAssemblyAsync("Cheese Roll", fillingRoleId, [(cheeseId, 100m)]);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1 && viewModel.EditingUsage is not null, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.UsageEdits.Single().IsLinked.Should().BeTrue();
        viewModel.IsFormEditingLines.Should().BeFalse();

        var roles = new SubAssemblyRoleRepository(CreateDbContextFactory());
        var role = (await roles.GetAllAsync(Cancellation)).Single(r => r.Id == fillingRoleId);
        role.IsProductOwned = true;
        await roles.UpdateAsync(role, Cancellation);
        await viewModel.LoadCommand.Execute();

        var usage = viewModel.UsageEdits.Single();
        usage.SubAssemblyId.Should().Be(fillingId);
        usage.IsOwned.Should().BeTrue();
        viewModel.EditingUsage.Should().BeSameAs(usage);
        viewModel.IsFormEditingLines.Should().BeTrue("the form opens on it again, its lines the product's now");
        viewModel.History.IsDirty.Should().BeFalse("nothing was typed");
    }

    [Fact]
    public async Task A_typed_yield_is_kept_and_the_weight_per_piece_follows_the_batch()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Pandesal");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == productId && viewModel.AreDetailsReady, () => $"selected={viewModel.SelectedProduct?.Id}");

        await viewModel.AddUsageCommand.Execute(UsageFormMode.New);
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == roleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1200m });
        viewModel.EditingUsageWeightPerPiece = 50m;
        viewModel.EditingUsageYield.Should().Be(24m, "1,200 g at 50 g a piece");
        viewModel.EditingUsageYield = 30m;
        viewModel.EditingUsageWeightPerPiece.Should().Be(40m, "typing the yield works out the weight per piece");

        // The batch grows while the form is open, typed over the line's grams: the typed yield stays.
        viewModel.FormLines.Single().Grams = 1500m;
        viewModel.EditingUsageYield.Should().Be(30m);
        viewModel.EditingUsageWeightPerPiece.Should().Be(50m, "1,500 g over the 30 pieces typed");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.Include(u => u.SubAssembly).SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.AuthoredField.Should().Be(UsageAuthoredField.Yield);
        usage.AuthoredValue.Should().Be(30m);
        usage.SubAssembly!.TotalWeight.Should().Be(1500m);
    }

    [Fact]
    public async Task Shrinkage_cuts_the_yield_and_makes_every_piece_dearer()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1700m)], weightPerPiece: 60m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        await viewModel.EditUsageCommand.Execute(viewModel.UsageEdits.Single());
        viewModel.EditingUsageYield.Should().Be(28.33m, "1,700 g at 60 g a piece, with nothing lost baking it");

        // A tenth of the batch bakes off: the typed weight per piece is the finished piece, so the yield drops.
        viewModel.EditingUsageShrinkagePercent = 10m;

        viewModel.EditingUsageWeightPerPiece.Should().Be(60m, "the typed figure stays put");
        viewModel.EditingUsageYield.Should().Be(25.5m, "the pieces come out of the 1,530 g the batch makes");
        viewModel.UsageFormTotalWeightText.Should().Be("1,700 g", "the lines that go in are unchanged");
        viewModel.UsageFormFinalWeightText.Should().Be("1,530 g");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.ShrinkagePercent.Should().Be(10m);
        usage.AuthoredValue.Should().Be(60m, "shrinkage is its own figure, not a piece size");

        // The batch still costs ₱85 (1,700 g of flour at ₱0.05), now spread over 25.5 pieces instead of 28.33.
        viewModel.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered).Should().BeApproximately(85m / 25.5m, 0.0001m);
    }

    /// <summary>
    /// Add Sub-Assembly is a drop-down of the three ways in (RecipeStructureCard), and the way it carries is what the
    /// form opens in — the form has no picker of its own any more. Another item picked while the add tab shows changes
    /// that form instead of opening a second one, and the whole add is one undo step.
    /// </summary>
    [Fact]
    public async Task Add_Sub_Assembly_opens_the_form_in_the_way_its_drop_down_asked_for_and_changes_it_in_place()
    {
        var roleId = await AddRoleAsync("Dough");
        await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == miniId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == miniId, () => $"selected={viewModel.SelectedProduct?.Id}");

        // Link: the tab opens with the form already linking, and its title says so in place of the picker that used to.
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.EditingUsageMode.Should().Be(UsageFormMode.Link);
        viewModel.UsageFormTitle.Should().Be("Link an existing sub-assembly");
        viewModel.IsFormPickingSource.Should().BeTrue("a link picks the sub-assembly it comes from");
        viewModel.IsFormEditingLines.Should().BeFalse("a link's lines are its owner's");
        viewModel.IsFormNewMovingToSubAssemblies.Should().BeFalse("only a new one carries the badge");

        // Copy, from the same tab: the form changes in place, and nothing else does.
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Copy);
        viewModel.RecipeTabs.OfType<AddSubAssemblyRecipeTab>().Should().ContainSingle("the tab showing is the one that changes");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.UsageFormTitle.Should().Be("Copy an existing sub-assembly");
        viewModel.IsFormPickingSource.Should().BeTrue();
        viewModel.IsFormEditingLines.Should().BeTrue("a copy is the product's own from then on");
        viewModel.UsageEdits.Should().BeEmpty("nothing is in the recipe until the form is applied");

        // New: no source to pick, so the form's picker row isn't there at all, and no title either: in its place the
        // badge says Save keeps it on the Sub-Assemblies screen, which it does unless its role is product-owned.
        await viewModel.AddUsageCommand.Execute(UsageFormMode.New);
        viewModel.UsageFormTitle.Should().BeEmpty();
        viewModel.IsFormPickingSource.Should().BeFalse();
        viewModel.EditingUsageRole!.Name.Should().Be("Dough", "the first role the recipe doesn't have yet");
        viewModel.IsFormNewMovingToSubAssemblies.Should().BeTrue("a Dough isn't product-owned, so Save moves it to Sub-Assemblies");
        viewModel.EditingUsageRole = viewModel.FormRoleOptions.Single(r => r.Name == "Filling");
        viewModel.IsFormNewMovingToSubAssemblies.Should().BeFalse("a Filling is product-owned, so it stays the product's own");
        viewModel.EditingUsageRole = viewModel.FormRoleOptions.Single(r => r.Name == "Dough");

        // Each pick is its own undo step, back to the add that opened the tab, and then the tab itself.
        viewModel.TryUndo().Should().BeTrue();
        viewModel.EditingUsageMode.Should().Be(UsageFormMode.Copy);
        viewModel.TryUndo().Should().BeTrue();
        viewModel.EditingUsageMode.Should().Be(UsageFormMode.Link);
        viewModel.TryUndo().Should().BeTrue();
        viewModel.RecipeTabs.Should().NotContain(viewModel.AddSubAssemblyTab, "the add went with its tab in one step");
        viewModel.IsUsageFormOpen.Should().BeFalse();
    }

    [Fact]
    public async Task A_linked_sub_assembly_follows_its_changes_and_Make_a_Copy_saves_a_new_one_the_product_links()
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
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.IsFormEditingLines.Should().BeFalse("a link's lines are the Sub-Assemblies screen's");
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => !o.IsPackaging);
        viewModel.EditingUsageSource!.DisplayName.Should().Be("Cream Loaf Dough — Sub-Assemblies (Dough)");
        await WaitUntilSettledAsync(() => viewModel.FormLines.Count == 1, () => $"form lines={viewModel.FormLines.Count}");
        viewModel.IsFormShowingLines.Should().BeTrue("the sub-assembly picked to link shows the lines it brings, read-only");
        viewModel.FormLines.Single().Grams.Should().Be(1000m);
        viewModel.EditingUsageWeightPerPiece = 30m;
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        var linked = viewModel.UsageEdits.Single();
        linked.IsLinked.Should().BeTrue();
        linked.LinkedFromText.Should().Be("Cream Loaf Dough, from Sub-Assemblies");
        linked.CostPerPieceAt(CostBasis.Buffered).Should().Be(1.5m, "30 g of a ₱50, 1,000 g batch");

        // Its tab shows its form, with its lines in it to look at but not to change.
        viewModel.EditingUsage.Should().BeSameAs(linked);
        viewModel.IsFormShowingLines.Should().BeTrue();
        viewModel.IsFormEditingLines.Should().BeFalse();
        viewModel.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal(("Flour", 1000m));

        // Butter goes into the dough on the Sub-Assemblies screen: the mini's linked section follows on the next refresh.
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Id = doughId, Name = "Cream Loaf Dough", RoleId = roleId }, [(flourId, 1000m), (butterId, 250m)], Cancellation);
        await viewModel.LoadCommand.Execute();
        viewModel.UsageEdits.Single().TotalWeight.Should().Be(1250m);
        viewModel.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered).Should().Be(3.6m, "30 g of a ₱150, 1,250 g batch");
        viewModel.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should()
            .Equal([("Flour", 1000m), ("Butter", 250m)], "the read-only lines show the sub-assembly's, as they now are");

        // Make a Copy: the mini gets a dough of its own to change, at the same weight, and stops following.
        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());
        var copy = viewModel.UsageEdits.Single();
        copy.IsNew.Should().BeTrue();
        copy.RoleName.Should().Be("Dough");
        viewModel.RecipeTabs[0].Header.Should().Be("Dough");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var miniUsage = await dbContext.ProductSubAssemblyUsages.Include(u => u.SubAssembly).SingleAsync(u => u.ProductId == miniId, Cancellation);
        miniUsage.SubAssemblyId.Should().NotBe(doughId);
        miniUsage.SubAssembly!.Name.Should().Be("Cream Loaf Mini Dough");
        miniUsage.SubAssembly.TotalWeight.Should().Be(1250m);
        miniUsage.AuthoredValue.Should().Be(30m);
    }

    [Fact]
    public async Task A_linked_sections_lines_are_edited_in_the_product_and_Save_writes_them_to_the_sub_assembly_after_asking()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(miniId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));
        var viewModel = CreateViewModel();
        string? confirmation = null;
        var confirm = false;
        viewModel.ConfirmDelete.RegisterHandler(context =>
        {
            confirmation = context.Input;
            context.SetOutput(confirm);
        });
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == miniId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Its form types into its lines as it would a section of the product's own.
        await viewModel.EditUsageCommand.Execute(viewModel.UsageEdits.Single());
        viewModel.IsEditingLinkedUsage.Should().BeTrue();
        viewModel.IsFormEditingLines.Should().BeFalse("its role and name are still the Sub-Assemblies screen's");
        viewModel.IsFormLinesEditable.Should().BeTrue();
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 250m });

        // A refresh while the form is open keeps what was typed into its lines.
        await viewModel.LoadCommand.Execute();
        viewModel.FormLines.Select(l => (l.IngredientId, l.Grams)).Should().Equal((flourId, 1000m), (butterId, 250m));

        // Save asks first, since Cream Loaf changes too: declined, nothing is written and the change stays in the form.
        await viewModel.SaveCommand.Execute();
        confirmation.Should().Be("Save changes the lines of Cream Loaf Dough, also used by Cream Loaf. Their recipes and costs change with it. Save anyway?");
        var edited = viewModel.UsageEdits.Single();
        edited.AreLinkedLinesEdited.Should().BeTrue();
        edited.TotalWeight.Should().Be(1250m);
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.SubAssemblyLines.CountAsync(l => l.SubAssemblyId == doughId, Cancellation)).Should().Be(1);
        }

        confirm = true;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.SingleOrDefault() is { AreLinkedLinesEdited: false, TotalWeight: 1250m },
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}");
        var saved = viewModel.UsageEdits.Single();
        saved.IsLinked.Should().BeTrue("it's still the one kept on the Sub-Assemblies screen, not a copy");
        saved.SubAssemblyId.Should().Be(doughId);
        saved.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.6m, "30 g of a ₱150, 1,250 g batch");
        viewModel.History.IsDirty.Should().BeFalse("what was saved is the new starting point");
        await using (var dbContext = CreateDbContext())
        {
            var dough = await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == doughId, Cancellation);
            (dough.TotalWeight, dough.TotalCost).Should().Be((1250m, 150m));
            (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == doughId).OrderBy(l => l.SortOrder).Select(l => l.IngredientId).ToListAsync(Cancellation))
                .Should().Equal(flourId, butterId);
        }
    }

    [Fact]
    public async Task Changing_a_link_relinks_the_section_keeping_its_piece_size_and_the_add_ins_the_new_recipe_lacks()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var houseId = await subAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "House Dough", RoleId = roleId }, [(flourId, 1000m)], Cancellation);
        var richId = await subAssemblies.SaveStandaloneAsync(
            new SubAssembly { Name = "Rich Dough", RoleId = roleId }, [(flourId, 1000m), (butterId, 200m)], Cancellation);
        var productId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [], [new LinkedUsageDraft(houseId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(butterId, 100m), (ubeId, 50m)], ShrinkagePercent = 5m }]));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        await viewModel.EditUsageCommand.Execute(viewModel.UsageEdits.Single());

        await viewModel.StartChangingLinkCommand.Execute();

        viewModel.IsChangingLink.Should().BeTrue();
        viewModel.IsShowingLinkBadge.Should().BeFalse("the picker takes the badge's place");
        viewModel.ChangeLinkOptions.Select(o => o.SubAssembly!.Id).Should().Equal([richId], "House Dough is the one linked already");

        viewModel.ChangeLinkTarget = viewModel.ChangeLinkOptions.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Single().SubAssemblyId == richId, () => $"linked={viewModel.UsageEdits.Single().SubAssemblyId}");

        var relinked = viewModel.UsageEdits.Single();
        relinked.IsLinked.Should().BeTrue();
        relinked.LinkedName.Should().Be("Rich Dough");
        (relinked.AuthoredField, relinked.AuthoredValue, relinked.ShrinkagePercent).Should().Be((UsageAuthoredField.WeightPerPiece, 30m, 5m));
        relinked.AddIns.Select(a => a.Ingredient.Id).Should().Equal([ubeId], "Rich Dough has butter already, so that add-in goes");
        viewModel.SuccessMessage.Should().Be("Now linked to Rich Dough. Butter left the add-ins: Rich Dough already has it.");
        viewModel.EditingUsage.Should().BeSameAs(relinked, "its tab's form follows it");
        viewModel.IsChangingLink.Should().BeFalse();
        viewModel.IsShowingLinkBadge.Should().BeTrue();

        // One step back is the old link, add-ins and all.
        viewModel.TryUndo().Should().BeTrue();
        viewModel.UsageEdits.Single().SubAssemblyId.Should().Be(houseId);
        viewModel.UsageEdits.Single().AddIns.Should().HaveCount(2);
        viewModel.TryRedo().Should().BeTrue();

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.AsNoTracking().SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.SubAssemblyId.Should().Be(richId);
        usage.AuthoredValue.Should().Be(30m);
        (await dbContext.SubAssemblies.CountAsync(s => s.Id == houseId, Cancellation)).Should().Be(1, "the old one stays on the Sub-Assemblies screen");
    }

    [Fact]
    public async Task Unlink_makes_a_linked_section_the_products_own_in_a_role_that_isnt_product_owned()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var doughId = await subAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "House Dough", RoleId = roleId }, [(flourId, 1000m)], Cancellation);
        var productId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(butterId, 250m)] }]));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.IsEditingLinkedUsage.Should().BeTrue("Unlink shows on a linked section");

        await viewModel.UnlinkCommand.Execute(viewModel.UsageEdits.Single());

        // Its own before Save: the badge shows, its lines are typed into, and its add-in is one of them.
        var copy = viewModel.UsageEdits.Single();
        copy.IsOwned.Should().BeTrue();
        copy.IsLinked.Should().BeFalse();
        copy.Lines.Select(l => (l.Ingredient.Name, l.Grams)).Should().Equal(("Flour", 1000m), ("Butter", 250m));
        viewModel.EditingUsage!.IsOwned.Should().BeTrue();
        viewModel.IsFormEditingLines.Should().BeTrue();
        viewModel.IsEditingLinkedUsage.Should().BeFalse();

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1 && viewModel.UsageEdits[0].SubAssemblyId > 0,
            () => $"usage rows={viewModel.UsageEdits.Count}");

        viewModel.UsageEdits.Single().IsOwned.Should().BeTrue("it's read back as the product's own");
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.Include(u => u.SubAssembly).SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.SubAssemblyId.Should().NotBe(doughId);
        usage.SubAssembly!.Name.Should().Be("Cream Loaf Mini Dough");
        usage.SubAssembly.OwnerProductId.Should().Be(productId);
        usage.SubAssembly.TotalWeight.Should().Be(1250m);
        usage.AuthoredValue.Should().Be(30m);
        (await dbContext.UsageAddInLines.AnyAsync(Cancellation)).Should().BeFalse("the add-in went into the copy's lines");
        (await subAssemblies.GetStandaloneAsync(Cancellation)).Select(s => (s.Name, s.TotalWeight)).Should()
            .Equal([("House Dough", 1000m)], "the one it was linked to stays on the Sub-Assemblies screen as it was, and the copy isn't there");
    }

    [Fact]
    public async Task Move_to_Sub_Assemblies_puts_the_products_own_there_with_what_was_typed_and_links_it()
    {
        var roleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(roleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var fillingId = await SubAssemblyIdAsync(productId, roleId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.FormLines.Count == 1, () => $"form lines={viewModel.FormLines.Count}");
        viewModel.EditingUsage!.IsOwned.Should().BeTrue();
        viewModel.IsEditingOwnedUsage.Should().BeTrue("Move to Sub-Assemblies shows on the product's own");

        // Typed into the form and not applied yet: the move takes it along.
        viewModel.FormLines.Single().Grams = 120m;
        await viewModel.MoveToSubAssembliesCommand.Execute(viewModel.EditingUsage);

        var moving = viewModel.UsageEdits.Single();
        moving.IsReleasing.Should().BeTrue();
        moving.Lines.Single().Grams.Should().Be(120m);
        viewModel.EditingUsage.Should().BeSameAs(moving, "its tab is its form, so the form stays open on it");
        viewModel.IsEditingOwnedUsage.Should().BeFalse("it's on its way already");
        viewModel.IsFormEditingLines.Should().BeTrue("its lines are still edited here until Save");
        (await viewModel.SaveCommand.CanExecute.FirstAsync()).Should().BeTrue();

        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1 && viewModel.UsageEdits[0].IsLinked,
            () => $"linked={viewModel.UsageEdits.FirstOrDefault()?.IsLinked}");

        var linked = viewModel.UsageEdits.Single();
        linked.SubAssemblyId.Should().Be(fillingId, "the same sub-assembly, not a copy");
        linked.LinkedName.Should().Be("Cheese Roll Filling");
        var moved = await new SubAssemblyRepository(CreateDbContextFactory()).GetStandaloneAsync(Cancellation);
        moved.Should().ContainSingle().Which.TotalWeight.Should().Be(120m);
    }

    [Fact]
    public async Task Removing_a_sub_assembly_another_product_links_leaves_it_there()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (creamLoafId, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)]);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(miniId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == creamLoafId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        await viewModel.RemoveUsageCommand.Execute();
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.UsageEdits.Should().BeEmpty();
        (await new ProductSubAssemblyUsageRepository(CreateDbContextFactory()).GetForProductAsync(miniId, Cancellation))
            .Should().ContainSingle(u => u.SubAssemblyId == doughId, "the dough stays on the Sub-Assemblies screen, linked where it was");
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
                new NewSubAssemblyDraft(fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
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
    public async Task The_costing_summary_breaks_the_sub_assemblies_down_one_by_one_per_sellable_unit()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Sold in twos: each sub-assembly's cost is per unit of two pieces, as the Sub-assemblies row is.
        viewModel.EditingQuantityRequired = 2;
        viewModel.EditingLaborPercent = 20m;

        var costs = viewModel.Costs;
        costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should()
            .Equal([("Dough", 5m), ("Filling", 4.8m)], "₱2.50 of dough and ₱2.40 of filling a piece, in recipe order");
        costs.SubAssemblyLines.Sum(l => l.Cost).Should().Be(costs.SubAssemblies);
        costs.SubAssemblyLines.Should().AllSatisfy(l => l.Share.Should().Be(CostingRules.PercentOf(l.Cost, costs.SellingPrice)));
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
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
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

        await viewModel.AddUsageCommand.Execute();
        viewModel.FormRoleOptions.Select(r => r.Name).Should().Equal(["Dough", "Filling"], "a recipe can have more than one in a role");
        viewModel.EditingUsageRole!.Name.Should().Be("Dough", "the first role the recipe doesn't have yet");
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == doughRoleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1000m });
        viewModel.EditingUsageWeightPerPiece = 50m;
        await viewModel.SaveCommand.Execute();

        // Dough sorts before Filling regardless of which was typed in first, and it's the tab showing.
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.RecipeTabs[0]);
    }

    [Fact]
    public async Task OpenProduct_waits_for_a_list_not_loaded_yet_and_clears_a_search_hiding_the_product()
    {
        var creamLoafId = await AddProductAsync("Cream Loaf");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var viewModel = CreateViewModel();

        // As a Sub-Assemblies Used By tile does before the Products screen was ever shown.
        viewModel.OpenProduct(ensaymadaId);
        viewModel.SelectedProduct.Should().BeNull("the list is empty until the screen loads");

        await viewModel.LoadCommand.Execute();

        viewModel.SelectedProduct!.Id.Should().Be(ensaymadaId);
        viewModel.EditingName.Should().Be("Ensaymada", "the product's form is loaded, as picking it in the list would");

        viewModel.SearchText = "Cream";
        viewModel.OpenProduct(creamLoafId);
        viewModel.SelectedProduct!.Id.Should().Be(creamLoafId);
        viewModel.SearchText.Should().Be("Cream", "a search showing the product is left alone");

        viewModel.OpenProduct(ensaymadaId);
        viewModel.SelectedProduct!.Id.Should().Be(ensaymadaId);
        viewModel.SearchText.Should().BeEmpty("a search hiding the product is cleared, so the list shows the row picked");
        viewModel.VisibleProducts.Should().Contain(viewModel.SelectedProduct);
    }

    [Fact]
    public async Task OpenByName_opens_the_product_a_name_means_best_as_the_Vim_command_line_asks()
    {
        await AddProductAsync("Ube Cheese Pandesal");
        var pandesalId = await AddProductAsync("Pandesal");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.OpenByName("pandesal").Should().BeTrue();

        viewModel.SelectedProduct!.Id.Should().Be(pandesalId, "the name typed in full beats one that only holds it");
        viewModel.OpenByName("croissant").Should().BeFalse();
        viewModel.SelectedProduct!.Id.Should().Be(pandesalId, "nothing matching leaves the product open");
    }

    [Fact]
    public async Task A_role_changed_on_the_Sub_Assemblies_screen_moves_its_tab_to_where_the_new_role_sorts()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling");
        var doughId = await SubAssemblyIdAsync(productId, doughRoleId);

        // Re-role the Dough as a Topping (order 3) where it's kept: on coming back it sorts after the Filling (order 2),
        // and is called Topping.
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Id = doughId, Name = "Ube Roll Dough", RoleId = toppingRoleId }, [(flourId, 1000m)], Cancellation);
        await viewModel.LoadCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "Topping");
    }

    /// <summary>
    /// The piece size isn't a requirement (2026-09-20, at the user's request): a section goes into the recipe, and
    /// saves, before how much of it a piece takes is known. It just costs nothing per piece until one is typed.
    /// </summary>
    [Fact]
    public async Task A_sub_assembly_goes_in_and_saves_before_its_piece_size_is_known()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Pan de Sal");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == productId, () => $"selected={viewModel.SelectedProduct?.Id}");

        // A section with its lines but neither figure of its piece size: it applies, with nothing refused.
        await viewModel.AddUsageCommand.Execute(UsageFormMode.New);
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == roleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1000m });
        viewModel.UsageFormCostPerPieceText.Should().Be("\u2014", "a piece takes none of it yet");
        viewModel.UsageFormBatchCostText.Should().Be("50.00", "the batch costs its lines, sized or not: 1,000 g at 0.05");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        var usage = viewModel.UsageEdits.Single();
        usage.TotalWeight.Should().Be(1000m, "the batch is its lines, sized or not");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(0m);
        viewModel.Costs.Material.Should().Be(0m, "an unsized section costs the product nothing per piece");

        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.ProductSubAssemblyUsages.SingleAsync(u => u.ProductId == productId, Cancellation);
            saved.AuthoredValue.Should().Be(0m, "stored as it was left, waiting to be sized");
        }

        // Sized later, from its own tab: it prices at once, and saves over the unsized one.
        viewModel.SelectedRecipeTab = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single();
        viewModel.EditingUsageWeightPerPiece = 50m;
        viewModel.EditingUsageYield.Should().Be(20m, "1,000 g at 50 g a piece");
        viewModel.UsageFormCostPerPieceText.Should().Be("2.50", "the batch's 50.00 over 20 pieces, to two decimals like the tile beside it");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered) == 2.5m, () => $"cost per piece={viewModel.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered)}");
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
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)], []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Linking: Cream Loaf's Dough is offered too, though the recipe has a Dough; it would go by its name. The recipe's
        // own Dough isn't, as it's linked already.
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.LinkableSubAssemblies.Where(o => !o.IsPackaging).Select(o => o.DisplayName).Should().Equal(
            "Cream Loaf Dough — Sub-Assemblies (Dough)", "Ube Bun Filling — Sub-Assemblies (Filling)");

        // A new one in the Dough role with no name, beside the Dough that has none: refused, and nothing goes in.
        // Another way picked on the add tab changes the form open there, rather than opening a second one.
        await viewModel.AddUsageCommand.Execute(UsageFormMode.New);
        viewModel.RecipeTabs.OfType<AddSubAssemblyRecipeTab>().Should().ContainSingle();
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == doughRoleId);
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = ubeId, Grams = 500m });
        viewModel.EditingUsageWeightPerPiece = 20m;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().StartWith("This recipe has more than one Dough without a name");
        viewModel.UsageEdits.Should().ContainSingle();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab, "the form has to be put right first");

        // Named, it goes in, and the second Dough's tab reads by its name.
        viewModel.EditingUsageName = "Ube";
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header).Should().Equal("Dough", "Dough: Ube");

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
    public async Task Revert_reads_the_product_back_as_it_was_saved_and_drops_a_sub_assembly_being_added()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)], []));
        var viewModel = CreateViewModel();
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Rename it, retype the Dough's piece size, then start a Filling that is left sitting in the form.
        viewModel.EditingName = "Ube Roll Mini";
        await viewModel.EditUsageCommand.Execute(viewModel.UsageEdits.Single());
        viewModel.EditingUsageWeightPerPiece = 60m;
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageRole!.Name.Should().Be("Filling");
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = ubeId, Grams = 100m });
        viewModel.EditingUsageWeightPerPiece = 10m;
        viewModel.UsageEdits.Should().ContainSingle("the Dough went in when its tab was left; the Filling is still in the form");

        await viewModel.RevertCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}");
        viewModel.EditingName.Should().Be("Ube Roll", "the typed name goes back to the saved one");
        viewModel.IsAddingUsage.Should().BeFalse("the Filling being added went with the rest");
        viewModel.EditingUsage!.RoleName.Should().Be("Dough", "the reloaded Dough's tab shows, with its own form");
        viewModel.UsageEdits.Should().ContainSingle().Which.RoleName.Should().Be("Dough");
        viewModel.UsageEdits.Single().WeightPerPiece.Should().Be(50m, "the retyped piece size goes back to the saved 50 g");
        viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header).Should().Equal("Dough");

        // Revert only reads: nothing of the typing reached the database.
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.SingleAsync(Cancellation)).Name.Should().Be("Ube Roll");
        (await dbContext.ProductSubAssemblyUsages.CountAsync(Cancellation)).Should().Be(1);
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
            [new NewSubAssemblyDraft(fillingRoleId, [(ubeId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        await GivePackagingAsync(productId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        (await viewModel.RemoveUsageCommand.CanExecute.FirstAsync()).Should().BeTrue("a sub-assembly's tab is showing");

        // Add Sub-Assembly: its tab goes before Packaging and shows the add form, on the first role the recipe doesn't
        // have. Deleting isn't offered there; adding still is, since its drop-down is the only place the way in can be
        // changed once the form is open.
        await viewModel.AddUsageCommand.Execute();
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "New Sub-Assembly", "Packaging");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.IsUsageFormOpen.Should().BeTrue();
        viewModel.IsAddingUsage.Should().BeTrue();
        viewModel.FormRoleOptions.Select(r => r.Name).Should().Equal(["Dough", "Filling", "Topping"]);
        viewModel.EditingUsageRole!.Name.Should().Be("Dough");
        (await viewModel.AddUsageCommand.CanExecute.FirstAsync()).Should().BeTrue();
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

        // Applied on the way out of its tab: the Dough is the first tab, since Dough is the first role.
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageRole!.Name.Should().Be("Dough", "the only role the recipe doesn't have yet");
        viewModel.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 800m });
        viewModel.EditingUsageWeightPerPiece = 40m;
        viewModel.SelectedRecipeTab = viewModel.PackagingTab;

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Topping", "Packaging");

        // Its own tab opens its form again.
        viewModel.SelectedRecipeTab = viewModel.RecipeTabs[0];
        viewModel.EditingUsage!.RoleName.Should().Be("Dough");

        // Opened from a sub-assembly's tab and left with nothing typed in: the form closes and the New Sub-Assembly tab goes.
        await viewModel.AddUsageCommand.Execute();
        viewModel.EditingUsageRole!.Name.Should().Be("Dough", "the recipe has one in every role, so the first is picked");
        viewModel.SelectedRecipeTab = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().First();
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Topping", "Packaging");
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.RecipeTabs[0]);
    }

    [Fact]
    public async Task Editing_a_sub_assembly_opens_the_form_in_its_tab_which_stays_through_a_refresh_and_Save()
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
        viewModel.EditingUsageWeightPerPiece = 15m;

        // As another tab would, while the form is open: ube's price goes up, and the refresh swaps in a repriced copy.
        await using (var dbContext = CreateDbContext())
        {
            var ube = await dbContext.RawMaterials.SingleAsync(m => m.Id == ubeId, Cancellation);
            ube.CostPerBaseUnit = 0.36m;
            ube.BufferedCostPerBaseUnit = 0.36m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();
        tab.Usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.6m);
        viewModel.EditingUsage.Should().BeSameAs(tab.Usage, "the form follows the repriced copy that took its place");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}");

        // Save reloads the product, so its tab and usage are fresh objects; there is still just the one of each.
        viewModel.UsageEdits.Should().ContainSingle("Save replaces the refreshed copy instead of adding a second").Which.WeightPerPiece.Should().Be(15m);
        var savedTab = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Should().ContainSingle().Subject;
        viewModel.SelectedRecipeTab.Should().BeSameAs(savedTab);
        viewModel.EditingUsage.Should().BeSameAs(savedTab.Usage, "the tab has nothing else to show, so its form is open on it");
        savedTab.Usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(5.4m, "15 g a piece of a 100 g, ₱36 batch");
        savedTab.Header.Should().Be("Filling");
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
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 40m),
            ],
            []));
        await GivePackagingAsync(productId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        // From Packaging, Add Sub-Assembly then back to Packaging: nothing was typed in, so the form closes
        // with nothing applied and the add's tab goes.
        viewModel.SelectedRecipeTab = viewModel.PackagingTab;
        await viewModel.AddUsageCommand.Execute();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        viewModel.IsUsageFormOpen.Should().BeTrue();
        viewModel.SelectedRecipeTab = viewModel.PackagingTab;
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
        viewModel.EditingUsageMode = UsageFormMode.Link;
        viewModel.LinkableSubAssemblies.Where(o => !o.IsPackaging).Should().ContainSingle().Which.DisplayName.Should().Be("Chocolate Glaze — Sub-Assemblies (Glaze)");

        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(
            () => viewModel.SelectedProduct?.Id == productId && viewModel.RecipeTabs.Count == 0,
            () => $"selected={viewModel.SelectedProduct?.Id}, tabs={viewModel.RecipeTabs.Count}");
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => !o.IsPackaging);
        await WaitUntilSettledAsync(() => viewModel.UsageFormTotalWeightText == "100 g", () => $"batch={viewModel.UsageFormTotalWeightText}");
        viewModel.EditingUsageWeightPerPiece = 5m;
        await viewModel.SaveCommand.Execute();

        var usage = viewModel.UsageEdits.Should().ContainSingle().Subject;
        usage.IsLinked.Should().BeTrue();
        usage.LinkedFromText.Should().Be("Chocolate Glaze, from Sub-Assemblies", "a standalone one keeps its name, which its tab doesn't show");
        usage.RoleName.Should().Be("Glaze");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(0.45m, "5 g of a 100 g, ₱9 batch");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var saved = await dbContext.ProductSubAssemblyUsages.SingleAsync(Cancellation);
        saved.ProductId.Should().Be(productId);
        saved.SubAssemblyId.Should().Be(glazeId);
        saved.AuthoredValue.Should().Be(5m);
    }

    [Fact]
    public async Task The_header_title_follows_the_name_as_it_is_typed()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.HeaderTitle.Should().Be("New product");

        viewModel.EditingName = "Cheese roll";
        viewModel.HeaderTitle.Should().Be("Cheese roll", "the header follows the name as it's typed");
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
    public async Task The_current_price_shows_the_markup_it_earns_as_it_is_typed_and_Save_keeps_it()
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.25m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.EditingLaborPercent = 100m;
        viewModel.EditingVatPercent = 12m;
        viewModel.Costs.BaseCost.Should().Be(5m, "₱2.50 of filling and as much again in labor");
        viewModel.Costs.CurrentMarkupPercent.Should().BeNull("no current price is entered");

        viewModel.EditingCurrentPrice = 7m;
        viewModel.Costs.CurrentMarkupPercent.Should().Be(25m, "₱7 with 12% VAT is ₱6.25 before it: ₱1.25 on ₱5 of cost");
        viewModel.Costs.IsCurrentPriceBelowCost.Should().BeFalse();

        viewModel.EditingCurrentPrice = 5m;
        viewModel.Costs.IsCurrentPriceBelowCost.Should().BeTrue("₱5 with VAT is under ₱5 before it");

        viewModel.EditingCurrentPrice = 7m;
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(Cancellation)).CurrentPrice.Should().Be(7m);
        }

        await viewModel.NewCommand.Execute();
        viewModel.EditingCurrentPrice.Should().BeNull("a new product has no current price");
        viewModel.Costs.CurrentMarkupPercent.Should().BeNull();

        viewModel.SelectedProduct = viewModel.Products.Single();
        viewModel.EditingCurrentPrice.Should().Be(7m);
    }

    [Fact]
    public async Task Each_metric_s_value_follows_its_percentage_and_a_value_typed_sets_the_percentage_Save_keeps()
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.Costs.SubAssemblies.Should().Be(2m, "10 g of ube at ₱0.20 a gram");
        List<string> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        viewModel.EditingLaborPercent = 25m;
        viewModel.EditingLaborAmount.Should().Be(0.5m, "25% of ₱2 of sub-assemblies");
        changed.Should().Contain(nameof(ProductsViewModel.EditingLaborAmount), "the value box follows the percentage typed");

        viewModel.EditingOverheadAmount = 0.3m;
        viewModel.EditingOverheadPercent.Should().Be(15m, "₱0.30 is 15% of ₱2");
        viewModel.EditingAdministrativeAmount = 0.2m;
        viewModel.EditingAdministrativePercent.Should().Be(10m);
        viewModel.Costs.BaseCost.Should().Be(3m, "₱2 of sub-assemblies, then ₱0.50 + ₱0.30 + ₱0.20");

        viewModel.EditingMarkupAmount = 0.75m;
        viewModel.EditingMarginPercent.Should().Be(25m, "the markup is a percentage of the ₱3 base cost");
        viewModel.Costs.Markup.Should().Be(0.75m);
        viewModel.History.IsDirty.Should().BeTrue();

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var costing = await dbContext.CostingParameters.SingleAsync(Cancellation);
        (costing.LaborPercent, costing.OverheadPercent, costing.AdministrativePercent, costing.MarginPercent)
            .Should().Be((25m, 15m, 10m, 25m));
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
        viewModel.Categories.Select(c => c.Name).Should().Equal("Cakes & Tortes");
    }

    [Fact]
    public async Task Typing_a_name_that_isnt_a_category_asks_to_add_it_and_picks_it_for_the_product()
    {
        var viewModel = CreateViewModel();
        string? promptedWith = null;
        viewModel.PromptForNewCategory.RegisterHandler(interaction =>
        {
            promptedWith = interaction.Input;
            interaction.SetOutput("Pastries");
        });
        await viewModel.LoadCommand.Execute();

        viewModel.BeginCategoryEdit();
        viewModel.CategorySearchText = "Pas";
        await viewModel.CommitCategoryTextAsync();

        promptedWith.Should().Be("Pas", "the prompt starts with what was typed");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Pastries was added.");
        viewModel.EditingCategory!.Name.Should().Be("Pastries");
        viewModel.CategorySearchText.Should().Be("Pastries");
        viewModel.Categories.Select(c => c.Name).Should().Equal("Pastries");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductCategories.SingleAsync(Cancellation)).Name.Should().Be("Pastries");
    }

    [Fact]
    public async Task Typing_a_listed_categorys_name_picks_it_whatever_its_case_without_asking()
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

        viewModel.BeginCategoryEdit();
        viewModel.CategorySearchText = "breads";
        await viewModel.CommitCategoryTextAsync();

        prompted.Should().BeFalse();
        viewModel.EditingCategory!.Name.Should().Be("Breads");
    }

    [Fact]
    public async Task Cancelling_the_new_category_prompt_puts_back_the_category_the_picker_had()
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

        // As ComboBoxAdv's Suggest mode does: a name it doesn't list lets go of its pick.
        viewModel.BeginCategoryEdit();
        viewModel.CategorySearchText = "Cakes";
        viewModel.EditingCategory = null;
        await viewModel.CommitCategoryTextAsync();

        prompted.Should().BeTrue();
        viewModel.EditingCategory!.Name.Should().Be("Breads");
        viewModel.CategorySearchText.Should().Be("Breads");
        viewModel.Categories.Should().ContainSingle("nothing was added");
    }

    [Fact]
    public async Task The_lists_row_and_the_picked_product_follow_each_other_and_a_folder_picked_leaves_the_product()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var ensaymadaId = await SaveProductAsync("Ensaymada", breadsId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        var tree = viewModel.ProductTree;
        var breads = TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Breads");
        breads.Children.Should().Equal(tree.Find(ensaymadaId)!, tree.Find(mamonId)!);

        viewModel.SelectedTreeNode = tree.Find(mamonId);
        viewModel.SelectedProduct!.Id.Should().Be(mamonId, "picking a product's row picks the product");
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");

        viewModel.SelectedTreeNode = breads;
        viewModel.SelectedProduct!.Id.Should().Be(mamonId, "a folder picked leaves the product showing");

        viewModel.OpenProduct(ensaymadaId);
        viewModel.SelectedTreeNode.Should().BeSameAs(tree.Find(ensaymadaId), "a product opened from elsewhere takes the row with it");
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");

        viewModel.SearchText = "mam";
        tree.Find(ensaymadaId).Should().BeNull();
        viewModel.SelectedTreeNode.Should().BeNull("the search hides the product picked");
        viewModel.SearchText = string.Empty;
        viewModel.SelectedTreeNode.Should().BeSameAs(tree.Find(ensaymadaId), "its row comes back with it");

        await viewModel.NewCommand.Execute();
        viewModel.SelectedTreeNode.Should().BeNull();
    }

    [Fact]
    public async Task Dragging_the_product_on_the_form_to_another_folder_saves_the_move_and_leaves_nothing_else_to_save()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var cakesId = await AddProductCategoryAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.OpenProduct(mamonId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");
        TypeName(viewModel, "Mamon Special");

        var move = ProductTree.MoveTo(TreeFolder(viewModel, ProductStage.Production, "Cakes"), viewModel.SelectedProduct!);
        await viewModel.MoveProductsCommand.Execute([move!]);

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.Products.SingleAsync(Cancellation);
            saved.Stage.Should().Be(ProductStage.Production);
            saved.CategoryId.Should().Be(cakesId);
            saved.Name.Should().Be("Mamon", "the move saves nothing typed into the form");
        }

        var leaf = viewModel.ProductTree.Find(mamonId);
        TreeFolder(viewModel, ProductStage.Production, "Cakes").Children.Should().ContainSingle().Which.Should().BeSameAs(leaf);
        TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Breads").Children.Should().BeEmpty();
        viewModel.SelectedTreeNode.Should().BeSameAs(leaf);
        viewModel.SelectedProduct!.Stage.Should().Be(ProductStage.Production, "the selection is the product as it's saved now");
        viewModel.EditingCategory!.Name.Should().Be("Cakes", "the Category picker follows the move");
        viewModel.EditingName.Should().Be("Mamon Special", "what's typed into the form stays");
        viewModel.History.IsDirty.Should().BeTrue("the new name is still to be saved");

        viewModel.TryUndo().Should().BeTrue();
        viewModel.EditingName.Should().Be("Mamon");
        viewModel.EditingCategory!.Name.Should().Be("Cakes", "no undo takes back a move that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Dragging_another_product_leaves_the_form_as_it_is()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var ensaymadaId = await SaveProductAsync("Ensaymada", breadsId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.OpenProduct(mamonId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");
        TypeName(viewModel, "Mamon Special");

        var ensaymada = viewModel.Products.Single(p => p.Id == ensaymadaId);
        var move = ProductTree.MoveTo(viewModel.ProductTree.Roots.Single(r => r.Stage == ProductStage.Production), ensaymada);
        await viewModel.MoveProductsCommand.Execute([move!]);

        viewModel.ErrorMessage.Should().BeNull();
        TreeFolder(viewModel, ProductStage.Production, "Breads").Children.Should().ContainSingle()
            .Which.Should().BeSameAs(viewModel.ProductTree.Find(ensaymadaId), "dropped on the stage's folder, it keeps its category");
        viewModel.SelectedProduct!.Id.Should().Be(mamonId);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.ProductTree.Find(mamonId));
        viewModel.EditingName.Should().Be("Mamon Special");
        viewModel.EditingCategory!.Name.Should().Be("Breads");
        viewModel.History.CanUndo.Should().BeTrue();
        viewModel.History.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Variant_makes_a_product_that_links_the_recipe_and_packaging_and_shows_it()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (sourceId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        await GivePackagingAsync(sourceId);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}");

        viewModel.EditingCloneName = "Ensaymada Small";
        await viewModel.CreateVariantCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SelectedProduct!.Name.Should().Be("Ensaymada Small");
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 1 && viewModel.IsPackagingLinked,
            () => $"ready={viewModel.AreDetailsReady}, usages={viewModel.UsageEdits.Count}, packaging linked={viewModel.IsPackagingLinked}");
        var usage = viewModel.UsageEdits.Single();
        usage.IsLinked.Should().BeTrue();
        usage.SubAssemblyId.Should().Be(doughId);
    }

    [Fact]
    public async Task A_new_product_starts_with_the_costing_defaults_and_its_first_Save_keeps_them()
    {
        await new CostingDefaultsRepository(CreateDbContextFactory()).SetAsync(
            new CostingDefaults { LaborPercent = 25m, OverheadPercent = 10m, AdministrativePercent = 5m, MarginPercent = 40m, VatPercent = 12m },
            Cancellation);
        var viewModel = CreateViewModel();

        await viewModel.LoadCommand.Execute();

        (viewModel.EditingLaborPercent, viewModel.EditingOverheadPercent, viewModel.EditingAdministrativePercent, viewModel.EditingMarginPercent, viewModel.EditingVatPercent)
            .Should().Be((25m, 10m, 5m, 40m, 12m), "the screen opens on a new product");

        viewModel.EditingName = "Pandesal";
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var costing = await dbContext.CostingParameters.SingleAsync(c => c.Product!.Name == "Pandesal", Cancellation);
        (costing.LaborPercent, costing.MarginPercent, costing.VatPercent).Should().Be((25m, 40m, 12m));
    }

    [Fact]
    public async Task Changed_defaults_reach_an_untouched_new_product_but_not_a_typed_one_or_a_saved_product()
    {
        var defaultsRepository = new CostingDefaultsRepository(CreateDbContextFactory());
        var productId = await AddProductAsync("Cheese roll");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        // As Settings would, while the screen shows a new product: it takes them up on the next load.
        await defaultsRepository.SetAsync(new CostingDefaults { LaborPercent = 20m, VatPercent = 12m }, Cancellation);
        await viewModel.LoadCommand.Execute();
        viewModel.EditingLaborPercent.Should().Be(20m);

        // Typed into, it keeps what was typed.
        viewModel.EditingLaborPercent = 7m;
        await defaultsRepository.SetAsync(new CostingDefaults { LaborPercent = 30m, VatPercent = 12m }, Cancellation);
        await viewModel.LoadCommand.Execute();
        viewModel.EditingLaborPercent.Should().Be(7m);

        // A saved product without costing of its own shows none: the defaults are only where a new product starts.
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");
        viewModel.EditingLaborPercent.Should().Be(0m);

        // New starts from the latest defaults.
        await viewModel.NewCommand.Execute();
        (viewModel.EditingLaborPercent, viewModel.EditingVatPercent).Should().Be((30m, 12m));
    }

    private async Task<int> AddProductCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = new ProductCategory { Name = name };
        dbContext.ProductCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    /// <summary>A product with nothing but its name and category, saved as the Products screen saves a new one: in Research and Development.</summary>
    private Task<int> SaveProductAsync(string name, int? categoryId) =>
        new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = name, CategoryId = categoryId }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    /// <summary>A name typed into the header and left, as one undo step, the way the name box records it.</summary>
    private static void TypeName(ProductsViewModel viewModel, string name)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        viewModel.EditingName = name;
        recorder.EndUndoStep();
    }

    private static ProductFolderNode TreeFolder(ProductsViewModel viewModel, ProductStage stage, string title) =>
        viewModel.ProductTree.Roots.Single(r => r.Stage == stage).Children.OfType<ProductFolderNode>().Single(f => f.Title == title);

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

    private ProductsViewModel CreateViewModel(IViewStateStore? viewStateStore = null)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ProductsViewModel(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new CostingDefaultsRepository(factory),
            new PackagingLinkRepository(factory),
            new ProductCloner(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            viewStateStore ?? new InMemoryViewStateStore(),
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
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 40m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Their saved lines are edited on the Sub-Assemblies screen; Make a Copy gives the product sections to type into.
        foreach (var saved in viewModel.UsageEdits.ToList())
        {
            await viewModel.MakeCopyCommand.Execute(saved);
        }

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

        // The form has no discard of its own: it's put right where it stands, and then the tab can be left.
        viewModel.EditingUsageRole = viewModel.Roles.Single(r => r.Id == doughRoleId);
        viewModel.EditingUsageRole!.Name.Should().Be("Dough");
        viewModel.SelectedRecipeTab = filling;
        viewModel.SelectedRecipeTab.Should().BeSameAs(filling);
        viewModel.ErrorMessage.Should().BeNull();
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
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => !o.IsPackaging);
        await WaitUntilSettledAsync(() => viewModel.FormLines.Count == 1, () => $"form lines={viewModel.FormLines.Count}");
        viewModel.FormAddIns.Add(new IngredientLineRow(viewModel.Ingredients.Single(m => m.Id == ubeId), addInGrams));
        viewModel.EditingUsageWeightPerPiece = weightPerPiece;
        await viewModel.SaveCommand.Execute();
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
        usage.TotalCostAt(CostBasis.Buffered).Should().Be(66m, "1000*0.05 + 20*0.80");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(66m * 30m / 1020m);

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
    public async Task A_linked_recipes_add_ins_table_always_shows_and_its_badge_counts_the_add_ins()
    {
        var (linkerId, _, ubeId, flourId) = await SeedLinkableDoughAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == linkerId, () => $"selected={viewModel.SelectedProduct?.Id}");

        // A picked recipe to link: its add-ins table shows under it, empty, and the badge says so.
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => !o.IsPackaging);
        await WaitUntilSettledAsync(() => viewModel.FormLines.Count == 1, () => $"form lines={viewModel.FormLines.Count}");
        viewModel.IsFormShowingAddIns.Should().BeTrue("the add-ins table shows for every linked recipe, empty or not");
        viewModel.HasFormAddIns.Should().BeFalse();
        viewModel.IsFormShowingNoAddIns.Should().BeTrue();
        viewModel.FormAddInsBadgeText.Should().Be("No add-ins");

        // The add row's untouched line isn't an add-in: only one with an ingredient picked counts.
        viewModel.FormAddIns.Add(viewModel.NewFormAddIn());
        viewModel.HasFormAddIns.Should().BeFalse();
        viewModel.FormAddIns.Clear();

        viewModel.FormAddIns.Add(new IngredientLineRow(viewModel.Ingredients.Single(m => m.Id == ubeId), 20m));
        viewModel.HasFormAddIns.Should().BeTrue();
        viewModel.IsFormShowingNoAddIns.Should().BeFalse();
        viewModel.FormAddInsBadgeText.Should().Be("1 add-in");
        viewModel.UsageFormTotalWeightText.Should().Be("1,020 g");

        viewModel.EditingUsageWeightPerPiece = 30m;
        await viewModel.SaveCommand.Execute();

        // Its tab reopens with the badge counting them. Save reloads the product, so the form reopens on the
        // reloaded copy rather than the one typed into.
        viewModel.ErrorMessage.Should().BeNull();
        await WaitUntilSettledAsync(
            () => viewModel.AreDetailsReady && viewModel.EditingUsage?.AddIns.Count == 1,
            () => $"ready={viewModel.AreDetailsReady}, add-ins={viewModel.EditingUsage?.AddIns.Count}");
        viewModel.IsFormShowingAddIns.Should().BeTrue();
        viewModel.FormAddInsBadgeText.Should().Be("1 add-in");

        // An add-in the linked recipe already has: refused, and the badge counts both lines still in the table.
        viewModel.FormAddIns.Add(new IngredientLineRow(viewModel.Ingredients.Single(m => m.Id == flourId), 50m));
        viewModel.FormAddInsBadgeText.Should().Be("2 add-ins");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Contain("Flour");
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
                new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 200m)], UsageAuthoredField.WeightPerPiece, 20m),
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 40m),
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
    public async Task Picking_another_product_keeps_the_recipe_tabs_and_opens_the_form_on_its_first_section()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var ubeBreadId = await AddProductAsync("Ube Bread");
        await SaveRecipeAsync(ubeBreadId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(ubeId, 300m)], UsageAuthoredField.WeightPerPiece, 15m),
            ],
            []));
        var cheeseBreadId = await AddProductAsync("Cheese Bread");
        await SaveRecipeAsync(cheeseBreadId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 800m)], UsageAuthoredField.WeightPerPiece, 40m),
                new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 200m)], UsageAuthoredField.WeightPerPiece, 10m),
                new NewSubAssemblyDraft(toppingRoleId, [(cheeseId, 50m)], UsageAuthoredField.WeightPerPiece, 2m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == ubeBreadId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");
        viewModel.SelectedRecipeTab = viewModel.RecipeTabs[1];
        List<RecipeTab> ubeBreadTabs = [.. viewModel.RecipeTabs];

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == cheeseBreadId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 3, () => $"usage rows={viewModel.UsageEdits.Count}");

        // The strip keeps its tabs, which show the cheese bread's sections; only its Topping gets a tab of its own.
        viewModel.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Topping");
        viewModel.RecipeTabs[0].Should().BeSameAs(ubeBreadTabs[0]);
        viewModel.RecipeTabs[1].Should().BeSameAs(ubeBreadTabs[1]);

        // Another product's recipe opens on its first tab, the form on its own section, not the one the tab showed before.
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.RecipeTabs[0]);
        viewModel.EditingUsage.Should().BeSameAs(viewModel.UsageEdits[0]);
        viewModel.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal([("Flour", 800m)]);
        viewModel.EditingUsageWeightPerPiece.Should().Be(40m);

        // Back to the ube bread: the Topping's tab goes, and the first two show its sections again.
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == ubeBreadId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.UsageEdits.Count == 2, () => $"usage rows={viewModel.UsageEdits.Count}");

        viewModel.RecipeTabs.Should().Equal(ubeBreadTabs);
        viewModel.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal([("Flour", 1000m)]);
        viewModel.EditingUsageWeightPerPiece.Should().Be(50m);
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
        viewModel.ErrorMessage.Should().Contain("Flour").And.Contain("Cream Loaf Dough");
        viewModel.UsageEdits.Should().BeEmpty("nothing goes into the recipe until the form is put right");
    }

    [Fact]
    public async Task Make_a_Copy_folds_the_add_ins_into_the_new_sections_lines()
    {
        var (linkerId, _, ubeId, flourId) = await SeedLinkableDoughAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == linkerId, () => $"selected={viewModel.SelectedProduct?.Id}");
        await LinkWithUbeAsync(viewModel, ubeId, addInGrams: 20m, weightPerPiece: 30m);

        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());

        var copy = viewModel.UsageEdits.Should().ContainSingle().Subject;
        copy.IsNew.Should().BeTrue();
        copy.AddIns.Should().BeEmpty("a new section has no add-ins; its lines are its own");
        copy.Lines.Select(l => (l.Ingredient.Id, l.Grams)).Should().Equal([(flourId, 1000m), (ubeId, 20m)]);
        copy.TotalWeight.Should().Be(1020m, "the batch it was showing is the batch it keeps");
        copy.CostPerPieceAt(CostBasis.Buffered).Should().Be(66m * 30m / 1020m);
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
        viewModel.UsageEdits.Single().TotalCostAt(CostBasis.Buffered).Should().Be(66m);

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
        usage.TotalCostAt(CostBasis.Buffered).Should().Be(82m, "1000*0.05 + 20*1.60 — the add-in is repriced like any other line");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(82m * 30m / 1020m);
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
            [new NewSubAssemblyDraft(roleId, [(flourId, 1000m), (waterId, 650m)], UsageAuthoredField.WeightPerPiece, 50m)],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Its saved lines are edited on the Sub-Assemblies screen; Make a Copy gives the product a section to type into.
        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());
        viewModel.IsFormEditingLines.Should().BeTrue();

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
            .Where(l => dbContext.ProductSubAssemblyUsages.Any(u => u.ProductId == productId && u.SubAssemblyId == l.SubAssemblyId))
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
                new NewSubAssemblyDraft(roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true), new RecipeLine(waterId, 650m)],
                    UsageAuthoredField.WeightPerPiece, 60m),
            ],
            []));
        var doughId = await SubAssemblyIdAsync(ownerId, roleId);
        var linkerId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(linkerId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == linkerId);
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // The link shows the owner's base, read-only, and the percentages that follow from it.
        viewModel.FormLines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(100m);
        viewModel.IsFormEditingLines.Should().BeFalse("a linked recipe's base is the Sub-Assemblies screen's to set");

        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());

        var copy = viewModel.UsageEdits.Should().ContainSingle().Subject;
        copy.IsNew.Should().BeTrue();
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
                new NewSubAssemblyDraft(roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true)], UsageAuthoredField.WeightPerPiece, 60m),
            ],
            []));
        var doughId = await SubAssemblyIdAsync(ownerId, roleId);
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
            [new NewSubAssemblyDraft(roleId, [(sugarId, 900m)], UsageAuthoredField.WeightPerPiece, 10m)],
            []));
        var glazeId = await SubAssemblyIdAsync(ownerId, roleId);
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
                new NewSubAssemblyDraft(roleId,
                    [new RecipeLine(flourId, 1000m, IsFormulaBase: true), (waterId, 600m), (eggsId, 100m)],
                    UsageAuthoredField.WeightPerPiece, 50m),
            ],
            []));
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.UsageEdits.Count == 1, () => $"usage rows={viewModel.UsageEdits.Count}");

        // Its saved lines are edited on the Sub-Assemblies screen; Make a Copy gives the product a section to type into.
        await viewModel.MakeCopyCommand.Execute(viewModel.UsageEdits.Single());

        var water = viewModel.FormLines.Single(l => l.Ingredient!.Id == waterId);
        water.CanTypeBakersPercent.Should().BeTrue();
        water.Percent = 65m;
        water.Grams.Should().Be(650m);

        // The flour doubles: the typed 65% follows it, the typed 100 g of eggs doesn't.
        viewModel.FormLines.Single(l => l.Ingredient!.Id == flourId).Grams = 2000m;

        viewModel.FormLines.Single(l => l.Ingredient!.Id == waterId).Grams.Should().Be(1300m);
        viewModel.FormLines.Single(l => l.Ingredient!.Id == waterId).IsPercentTyped.Should().BeTrue();
        viewModel.FormLines.Single(l => l.Ingredient!.Id == eggsId).Grams.Should().Be(100m);

        // The trap: Save only writes a recipe it sees as changed, so the typed percentage has to reach the draft.
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.AsNoTracking()
            .Where(l => dbContext.ProductSubAssemblyUsages.Any(u => u.ProductId == productId && u.SubAssemblyId == l.SubAssemblyId))
            .ToListAsync(Cancellation);
        lines.Single(l => l.IngredientId == waterId).Grams.Should().Be(1300m);
        lines.Single(l => l.IngredientId == waterId).AuthoredBakersPercent.Should().Be(65m);
        lines.Single(l => l.IngredientId == eggsId).Grams.Should().Be(100m);
        lines.Single(l => l.IngredientId == eggsId).AuthoredBakersPercent.Should().BeNull();
    }
}
