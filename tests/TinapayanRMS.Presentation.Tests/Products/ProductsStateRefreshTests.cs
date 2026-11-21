using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// Coming back to the Products tab re-runs LoadCommand (ScreenBase). A cost changed on another tab
/// must reach the selected product's recipe rows and cost summary, while the selection and the
/// costing fields the user was typing into stay as they were. Save writes the whole form at once:
/// the product's own sub-assemblies (made in the form), the ones it links, and the rest. The Recipe
/// Structure shows them in tabs, which keep their place through edits and refreshes.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class ProductsStateRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Reloading_updates_usage_costs_changed_on_another_tab_and_keeps_the_selection_and_unsaved_costing() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state,
            () => state.Recipe.UsageEdits.Count == 1,
            () => $"usage rows={state.Recipe.UsageEdits.Count}, roles={state.Roles.Count}");
        state.Recipe.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered).Should().Be(2.4m);
        var selected = state.SelectedProduct;
        state.Costing.EditingLaborPercent = 5m;

        // As another tab would: ube's price goes up (the cascade reprices the sub-assembly too).
        await using (var dbContext = CreateDbContext())
        {
            var ube = await dbContext.RawMaterials.SingleAsync(m => m.Id == ubeId, Cancellation);
            ube.CostPerBaseUnit = 0.36m;
            ube.BufferedCostPerBaseUnit = 0.36m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await state.LoadCommand.ExecuteAsync();

        state.Products.Should().ContainSingle().Which.Should().BeSameAs(selected);
        state.SelectedProduct.Should().BeSameAs(selected);
        state.SelectedProduct!.Id.Should().Be(productId);
        state.Costing.EditingLaborPercent.Should().Be(5m, "an unsaved costing field must survive the refresh");
        state.Recipe.UsageEdits.Should().ContainSingle().Which.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.6m);
        state.Costing.Costs.Material.Should().Be(3.6m);
        var (expectedBaseCost, _, _) = CostingRules.CalculateSellingPrice(3.6m, 0m, 5m, 0m, 0m, 0m, 0m);
        state.Costing.Costs.BaseCost.Should().Be(expectedBaseCost);
    });

    [Test]
    public Task Picking_another_product_keeps_the_previous_details_until_its_own_arrive_and_Save_waits_for_them() => UiThread.RunAsync(async () =>
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

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Name == "Ube pandesal"));
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.SingleOrDefault()?.SubAssemblyId == ubeFillingId,
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");

        state.Pick(state.Products.Single(p => p.Id == cheeseRollId));

        // Its details are still on their way (read off the UI thread): the ube pandesal's show until they
        // arrive, not an empty recipe, and a Save now would write that recipe under the cheese roll's name.
        state.AreDetailsReady.Should().BeFalse();
        state.EditingName.Should().Be("Cheese roll");
        state.Recipe.UsageEdits.Should().ContainSingle().Which.SubAssemblyId.Should().Be(ubeFillingId);
        state.Recipe.HasRecipeSections.Should().BeTrue();
        await state.SaveCommand.ExecuteAsync();

        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.SingleOrDefault()?.SubAssemblyId == cheeseFillingId,
            () => $"ready={state.AreDetailsReady}, usages={string.Join(",", state.Recipe.UsageEdits.Select(u => u.SubAssemblyId))}");
        state.Recipe.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Usage.SubAssemblyId.Should().Be(cheeseFillingId);
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(p => p.Id == cheeseRollId, Cancellation)).UpdatedAt.Should().Be(cheeseRollUpdatedAt, "the Save did nothing");
            (await dbContext.ProductSubAssemblyUsages.Where(u => u.ProductId == cheeseRollId).Select(u => u.SubAssemblyId).ToListAsync(Cancellation))
                .Should().Equal(cheeseFillingId);
        }
    });

    [Test]
    public Task One_Save_writes_a_new_product_with_a_sub_assembly_made_in_it() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Filling");
        await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Ube pandesal";
        state.EditingDescription = "Soft bread with ube filling";
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.IsFormEditingLines.Should().BeTrue("a new sub-assembly is made in the form");
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Filling", "the add form starts on the first role the recipe doesn't have yet");
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = state.Ingredients.Single().Id, Grams = 100m });
        state.UsageForm.UsageFormTotalWeightText.Should().Be("100 g");
        state.UsageForm.TypeWeightPerPiece(10m);
        state.UsageForm.EditingUsageYield.Should().Be(10m, "typing the weight per piece works out the yield");
        state.Costing.EditingLaborPercent = 50m;
        state.Costing.EditingAdministrativePercent = 12.5m;

        // The sub-assembly's form is left open: the one Save puts it in the recipe, shows its tab, and writes the lot.
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.SingleOrDefault()?.IsLinked == true,
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");
        var usage = state.Recipe.UsageEdits.Should().ContainSingle().Subject;
        state.Recipe.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>("Save shows the new sub-assembly's tab").Which.Usage.Should().BeSameAs(usage);
        usage.IsLinked.Should().BeTrue("Save keeps it on the Sub-Assemblies screen, and the product links it");
        usage.LinkedFromText.Should().Be("Ube pandesal Filling, from Sub-Assemblies");
        state.UsageForm.IsFormEditingLines.Should().BeFalse("its lines are edited on the Sub-Assemblies screen from now on");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(2.4m);
        state.Costing.Costs.Labor.Should().Be(1.2m, "labor is 50% of the ₱2.40 material cost");
        state.Costing.Costs.Administrative.Should().Be(0.3m, "administrative is 12.5% of the ₱2.40 material cost");
        state.Costing.Costs.BaseCost.Should().Be(3.9m, "₱2.40 of filling, ₱1.20 labor and ₱0.30 administrative");

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
            (await SubAssemblyTotalsAsync(subAssembly.Id)).TotalWeight.Should().Be(100m);
            var saved = await dbContext.ProductSubAssemblyUsages.SingleAsync(Cancellation);
            saved.AuthoredField.Should().Be(UsageAuthoredField.WeightPerPiece);
            saved.AuthoredValue.Should().Be(10m);
            var costing = await dbContext.CostingParameters.SingleAsync(Cancellation);
            costing.LaborPercent.Should().Be(50m);
            costing.AdministrativePercent.Should().Be(12.5m);
        }

        // The form now shows the saved product, which can be linked and cloned.
        await Settle.UntilAsync(state,
            () => state.SelectedProduct?.Id == productId && state.Recipe.UsageEdits.Count == 1,
            () => $"selected={state.SelectedProduct?.Id}, usage rows={state.Recipe.UsageEdits.Count}");
        state.HeaderTitle.Should().Be("Ube pandesal");
        state.Recipe.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>("the reload shows the same sub-assembly's tab, now saved")
            .Which.Usage.SubAssemblyId.Should().BeGreaterThan(0);
        state.ShowLinks.Should().BeTrue();
        state.LastUpdatedText.Should().NotBe("—");
    });

    [Test]
    public Task Packaging_lines_are_typed_into_the_table_in_each_materials_base_unit_and_saved() => UiThread.RunAsync(async () =>
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

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Gift loaf";
        state.Packaging.IsPackagingEditable.Should().BeTrue();

        // The row the add row edits shows the material's base unit and prices itself as soon as the material is picked.
        var boxLine = state.Packaging.NewLineEdit();
        boxLine.PackagingMaterialId = boxId;
        boxLine.QtyPerUnit = 0.5m;
        boxLine.BaseUnitAbbreviation.Should().Be("pc");
        boxLine.UnitCost.Should().Be(12m);
        boxLine.CostPerUnit.Should().Be(6m, "half a ₱12 box a piece");
        state.Packaging.LineEdits.Add(boxLine);

        var ribbonLine = state.Packaging.NewLineEdit();
        ribbonLine.PackagingMaterialId = ribbonId;
        ribbonLine.QtyPerUnit = 30m;
        ribbonLine.BaseUnitAbbreviation.Should().Be("cm");
        state.Packaging.LineEdits.Add(ribbonLine);
        state.Costing.Costs.Packaging.Should().Be(6.6m, "₱6 of box and 30 cm of ribbon at ₱0.02 a cm");

        // A line with a material but no quantity is refused, and deleting it lets Save through.
        var halfLine = state.Packaging.NewLineEdit();
        halfLine.PackagingMaterialId = boxId;
        state.Packaging.LineEdits.Add(halfLine);
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().Contain("Cake Box");
        state.Packaging.SelectedLineEdit = halfLine;
        await state.Packaging.RemoveLineCommand.ExecuteAsync();

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
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

        await Settle.UntilAsync(state,
            () => state.SelectedProduct is not null && state.Packaging.LineEdits.Count == 2 && !state.Packaging.LineEdits.Contains(boxLine),
            () => $"selected={state.SelectedProduct?.Id}, lines={state.Packaging.LineEdits.Count}");
        state.Packaging.LineEdits.Single(l => l.PackagingMaterialId == ribbonId).QtyPerUnit.Should().Be(30m);
        state.Costing.Costs.Packaging.Should().Be(6.6m);
    });

    [Test]
    public Task A_packaging_line_saved_in_another_unit_shows_in_that_unit_with_what_it_comes_to_in_base_units() => UiThread.RunAsync(async () =>
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

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Packaging.LineEdits.Count == 1, () => $"lines={state.Packaging.LineEdits.Count}");

        var line = state.Packaging.LineEdits.Single();
        line.QtyPerUnit.Should().Be(0.02m, "a line saved as 50 pieces per roll is 0.02 of a roll a piece");
        line.UnitId.Should().Be(rollId, "the line stays in the unit it was saved in");
        line.BaseQty.Should().Be(20m, "0.02 of a 1,000 cm roll is 20 cm");
        line.BaseUnitAbbreviation.Should().Be("cm");
        state.Costing.Costs.Packaging.Should().Be(0.4m, "20 cm at ₱0.02, as before");
    });


    [Test]
    public Task Saving_again_before_the_reload_arrives_keeps_a_new_section_only_once() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Pandesal");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}");
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == roleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1000m });
        state.UsageForm.TypeWeightPerPiece(50m);

        // The second Save lands before the first one's reload, while the form still shows the section as new.
        await state.SaveCommand.ExecuteAsync();
        await state.SaveCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.Select(s => s.Name).ToListAsync(Cancellation)).Should().Equal("Pandesal Dough");
    });

    [Test]
    public Task Make_a_Copy_of_a_section_saved_from_this_product_is_kept_under_the_next_number() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (productId, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1000m)], weightPerPiece: 50m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single().Header.Should().Be("Dough");

        await state.Recipe.MakeCopyCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        state.UsageForm.IsFormEditingLines.Should().BeTrue("the copy's lines are the form's to change");
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 100m });
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.SingleOrDefault()?.IsLinked == true,
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");
        var usage = state.Recipe.UsageEdits.Single();
        usage.LinkedFromText.Should().Be("Pandesal Dough 2, from Sub-Assemblies", "Pandesal Dough is still kept, unused");
        usage.TotalWeight.Should().Be(1100m);
        usage.WeightPerPiece.Should().Be(50m);
        state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single().Header.Should().Be("Dough");
        (await SubAssemblyTotalsAsync(doughId)).TotalWeight.Should().Be(1000m, "the sub-assembly it was a copy of is left as it was");
    });

    [Test]
    public Task Copy_an_existing_one_takes_its_role_and_lines_but_not_its_name() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var sourceId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(sourceId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m, SectionName: "Cheese")], []));
        var productId = await AddProductAsync("Ube Roll");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}");

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Copy);
        state.UsageForm.PickSource(state.UsageForm.LinkableSubAssemblies.Single());
        await Settle.UntilAsync(state, () => state.UsageForm.FormLines.Count == 1, () => $"form lines={state.UsageForm.FormLines.Count}");

        state.UsageForm.EditingUsageRole!.Id.Should().Be(fillingRoleId);
        state.UsageForm.EditingUsageName.Should().BeNull("\"Cheese Roll Filling: Cheese\" is another product's name; Save names the copy after this one");
        state.UsageForm.TypeWeightPerPiece(10m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.OrderBy(s => s.Id).Select(s => s.Name).ToListAsync(Cancellation))
            .Should().Equal("Cheese Roll Filling: Cheese", "Ube Roll Filling");
    });

    [Test]
    public Task Sections_in_one_role_saved_from_this_product_read_by_their_own_names_on_its_tabs() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 3, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // "Cheese Roll Filling" goes by its role alone, "Cheese Roll Filling: Ube" by Ube, and Ube Bun's by its whole name.
        state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header)
            .Should().Equal("Filling: Ube Bun Filling", "Filling", "Filling: Ube");
    });

    [Test]
    public Task A_products_own_section_is_edited_in_its_recipe_and_Save_rewrites_it_in_place() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var fillingId = await SubAssemblyIdAsync(productId, fillingRoleId);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1 && state.UsageForm.EditingUsage is not null, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        var usage = state.Recipe.UsageEdits.Single();
        usage.IsOwned.Should().BeTrue();
        usage.IsLinked.Should().BeFalse();
        state.UsageForm.IsFormEditingLines.Should().BeTrue("its lines are the product's to type into");
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = milkId, Grams = 50m });
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Single().TotalWeight == 150m, () => $"ready={state.AreDetailsReady}");
        var saved = state.Recipe.UsageEdits.Single();
        saved.SubAssemblyId.Should().Be(fillingId, "Save rewrites the product's own in place");
        saved.IsOwned.Should().BeTrue();
        state.Recipe.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Usage.Should().BeSameAs(saved);
        (await SubAssemblyTotalsAsync(fillingId)).TotalWeight.Should().Be(150m);
    });

    [Test]
    public Task Save_has_the_saved_product_showing_with_its_form_open_by_the_time_it_returns() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1 && state.UsageForm.EditingUsage is not null, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = milkId, Grams = 50m });

        // The form closes while the product is picked again and opens on the reloaded section: in one pass, with nothing
        // left to arrive afterwards, or the screen draws the form without its section in between (a flicker on Save).
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.AreDetailsReady.Should().BeTrue();
        state.UsageForm.IsUsageFormOpen.Should().BeTrue();
        var saved = state.Recipe.UsageEdits.Should().ContainSingle().Subject;
        saved.TotalWeight.Should().Be(150m, "the reloaded section, as saved");
        state.UsageForm.EditingUsage.Should().BeSameAs(saved);
        state.Recipe.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Usage.Should().BeSameAs(saved);
        state.UsageForm.FormLines.Select(l => l.Grams).Should().Equal(100m, 50m);
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task A_new_section_in_a_product_owned_role_is_the_products_own_after_Save() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Cheese Roll");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}");
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == fillingRoleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = cheeseId, Grams = 100m });
        state.UsageForm.TypeWeightPerPiece(10m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.SingleOrDefault()?.IsOwned == true,
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");
        state.UsageForm.IsFormEditingLines.Should().BeTrue();
        state.Recipe.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Usage.Should().BeSameAs(state.Recipe.UsageEdits.Single());
        (await new SubAssemblyRepository(CreateDbContextFactory()).GetStandaloneAsync(Cancellation)).Should().BeEmpty();
    });

    [Test]
    public Task Another_products_own_is_offered_to_copy_but_not_to_link() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var ownerId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(ownerId, new RecipeDraft(
            [new NewSubAssemblyDraft(fillingRoleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m, SectionName: "Cheese")], []));
        var productId = await AddProductAsync("Cheese Bun");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}");

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.UsageForm.LinkableSubAssemblies.Should().BeEmpty("a product's own is copied, never linked");

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Copy);
        state.UsageForm.LinkableSubAssemblies.Select(o => o.DisplayName)
            .Should().Equal("Cheese Roll Filling: Cheese — Cheese Roll (Filling)");
    });

    [Test]
    public Task A_role_made_product_owned_in_Settings_turns_a_linked_section_into_the_products_own_on_coming_back() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling");
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var (_, fillingId) = await AddProductWithSubAssemblyAsync("Cheese Roll", fillingRoleId, [(cheeseId, 100m)]);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1 && state.UsageForm.EditingUsage is not null, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Recipe.UsageEdits.Single().IsLinked.Should().BeTrue();
        state.UsageForm.IsFormEditingLines.Should().BeFalse();

        var roles = new SubAssemblyRoleRepository(CreateDbContextFactory());
        var role = (await roles.GetAllAsync(Cancellation)).Single(r => r.Id == fillingRoleId);
        role.IsProductOwned = true;
        await roles.UpdateAsync(role, Cancellation);
        await state.LoadCommand.ExecuteAsync();

        var usage = state.Recipe.UsageEdits.Single();
        usage.SubAssemblyId.Should().Be(fillingId);
        usage.IsOwned.Should().BeTrue();
        state.UsageForm.EditingUsage.Should().BeSameAs(usage);
        state.UsageForm.IsFormEditingLines.Should().BeTrue("the form opens on it again, its lines the product's now");
        state.History.IsDirty.Should().BeFalse("nothing was typed");
    });

    [Test]
    public Task A_typed_yield_is_kept_and_the_weight_per_piece_follows_the_batch() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Pandesal");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}");

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == roleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1200m });
        state.UsageForm.TypeWeightPerPiece(50m);
        state.UsageForm.EditingUsageYield.Should().Be(24m, "1,200 g at 50 g a piece");
        state.UsageForm.TypeYield(30m);
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(40m, "typing the yield works out the weight per piece");

        // The batch grows while the form is open, typed over the line's grams: the typed yield stays.
        state.UsageForm.FormLines.Single().Grams = 1500m;
        state.UsageForm.EditingUsageYield.Should().Be(30m);
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(50m, "1,500 g over the 30 pieces typed");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.Include(u => u.SubAssembly).SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.AuthoredField.Should().Be(UsageAuthoredField.Yield);
        usage.AuthoredValue.Should().Be(30m);
        (await SubAssemblyTotalsAsync(usage.SubAssemblyId)).TotalWeight.Should().Be(1500m);
    });

    [Test]
    public Task The_pieces_share_out_the_whole_batch_and_the_wastage_goes_on_the_costing() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1700m)], weightPerPiece: 60m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        await state.Recipe.EditUsageCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        state.UsageForm.EditingUsageYield.Should().Be(28.33m, "1,700 g at 60 g a piece: nothing is taken off the batch");
        state.UsageForm.UsageFormTotalWeightText.Should().Be("1,700 g");

        // A piece takes ₱3 of the ₱85 batch (60 g of ₱0.05 flour). The shop-wide 3% wastage goes on the costing, on top.
        state.Costing.Costs.SubAssemblies.Should().Be(3m);
        state.Costing.Costs.WastagePercent.Should().Be(3m, "nothing is saved in Settings yet");
        state.Costing.Costs.Wastage.Should().Be(0.09m);
        state.Costing.Costs.BaseCost.Should().Be(3.09m);
    });

    /// <summary>
    /// The wastage is the shop's, not the product's: one changed in Settings reprices a product already open on the
    /// screen's next load, and isn't an edit of it.
    /// </summary>
    [Test]
    public Task A_wastage_changed_in_Settings_reprices_the_open_product_on_the_next_load() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1700m)], weightPerPiece: 60m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Costing.Costs.Wastage.Should().Be(0.09m);

        await SetWastageAsync(10m);
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.Costing.Costs.WastagePercent == 10m, () => $"wastage={state.Costing.Costs.WastagePercent}");

        state.Costing.Costs.Wastage.Should().Be(0.3m, "10% of the ₱3 a piece takes");
        state.Costing.Costs.BaseCost.Should().Be(3.3m);
        state.History.IsDirty.Should().BeFalse("the shop's wastage isn't an edit of the product");
    });

    [Test]
    public Task A_category_with_its_own_wastage_prices_the_product_put_in_it_at_once() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1700m)], weightPerPiece: 60m);
        var categories = new ProductCategoryRepository(CreateDbContextFactory());
        await categories.AddAsync(new ProductCategory { Name = "Breads", WastagePercent = 10m }, Cancellation);
        await categories.AddAsync(new ProductCategory { Name = "Cakes" }, Cancellation);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Costing.Costs.WastagePercent.Should().Be(3m, "no category: the shop-wide one");

        state.EditingCategory = state.Categories.Single(c => c.Name == "Breads");

        state.Costing.Costs.WastagePercent.Should().Be(10m);
        state.Costing.Costs.Wastage.Should().Be(0.3m, "10% of the ₱3 a piece takes");

        state.EditingCategory = state.Categories.Single(c => c.Name == "Cakes");
        state.Costing.Costs.WastagePercent.Should().Be(3m, "Cakes leaves it to the shop-wide one");
    });

    /// <summary>
    /// A section made in a product-owned role is written for the one piece it goes into, so its form starts at a yield
    /// of 1 where any other starts with no size: the weight per piece is the whole batch, and
    /// follows the lines as they're typed and after it's saved, being the yield that's kept.
    /// </summary>
    [Test]
    public Task A_section_added_in_a_product_owned_role_starts_at_a_yield_of_1_and_its_weight_per_piece_follows_the_batch() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough");
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 2, isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Cheese Roll");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}");

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Dough");
        state.UsageForm.EditingUsageYield.Should().Be(0m, "a Dough is a batch of many pieces, sized by hand");

        state.UsageForm.PickRole(state.UsageForm.FormRoleOptions.Single(r => r.Id == toppingRoleId));
        state.UsageForm.EditingUsageYield.Should().Be(1m, "a Topping is the product's own");
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(0m, "there's no batch yet");

        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = cheeseId, Grams = 20m });
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(20m, "the 20 g that go in");
        state.UsageForm.UsageFormCostPerPieceText.Should().Be("8.00", "one piece takes the whole batch");

        state.UsageForm.FormLines.Single().Grams = 30m;
        state.UsageForm.EditingUsageYield.Should().Be(1m);
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(30m);
        state.UsageForm.UsageFormCostPerPieceText.Should().Be("12.00");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.AuthoredField.Should().Be(UsageAuthoredField.Yield, "so the weight per piece goes on following the lines");
        usage.AuthoredValue.Should().Be(1m);
    });

    /// <summary>
    /// The starting yield of 1 is the role's, not an entry: it follows the role while neither figure is typed, a form
    /// still at it counts as untouched, and a link starts with no size, its batch being a shared one. A typed size
    /// stays whatever the role becomes.
    /// </summary>
    [Test]
    public Task The_starting_yield_of_1_follows_the_role_until_a_piece_size_is_typed() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Topping", sortOrder: 1, isProductOwned: true);
        await AddRoleAsync("Dough", sortOrder: 2);
        var productId = await AddProductAsync("Cheese Roll");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}");

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Topping", "the first role the recipe doesn't have yet");
        state.UsageForm.EditingUsageYield.Should().Be(1m);

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.Recipe.RecipeTabs.OfType<AddSubAssemblyRecipeTab>().Should().ContainSingle("a form showing only what it opened with is changed in place");
        state.Recipe.UsageEdits.Should().BeEmpty("nothing was entered to add");
        state.UsageForm.EditingUsageYield.Should().Be(0m, "a linked batch is shared, and sized by hand");

        // Undo puts the New form back at its yield of 1, which is still only what it opened with.
        state.TryUndo().Should().BeTrue();
        state.UsageForm.EditingUsageMode.Should().Be(UsageFormMode.New);
        state.UsageForm.EditingUsageYield.Should().Be(1m);
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Copy);
        state.ErrorMessage.Should().BeNull("there was nothing in the form to apply");
        state.Recipe.RecipeTabs.OfType<AddSubAssemblyRecipeTab>().Should().ContainSingle();
        state.Recipe.UsageEdits.Should().BeEmpty();
        state.UsageForm.EditingUsageYield.Should().Be(1m, "a copy made in a Topping is the product's own too");

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.EditingUsageYield.Should().Be(1m);
        state.UsageForm.PickRole(state.UsageForm.FormRoleOptions.Single(r => r.Name == "Dough"));
        state.UsageForm.EditingUsageYield.Should().Be(0m, "a Dough starts with no size");
        state.UsageForm.PickRole(state.UsageForm.FormRoleOptions.Single(r => r.Name == "Topping"));
        state.UsageForm.EditingUsageYield.Should().Be(1m);

        state.UsageForm.TypeWeightPerPiece(15m);
        state.UsageForm.PickRole(state.UsageForm.FormRoleOptions.Single(r => r.Name == "Dough"));
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(15m, "the typed figure stays put");
        state.UsageForm.PickRole(state.UsageForm.FormRoleOptions.Single(r => r.Name == "Topping"));
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(15m);
        state.UsageForm.EditingUsageYield.Should().Be(0m, "the weight is the figure kept, and there's no batch to work a yield out from");
    });

    /// <summary>
    /// Add Sub-Assembly is a drop-down of the three ways in (RecipeStructure), and the way it carries is what the
    /// form opens in — the form has no picker of its own any more. Another item picked while the add tab shows changes
    /// that form instead of opening a second one, and the whole add is one undo step.
    /// </summary>
    [Test]
    public Task Add_Sub_Assembly_opens_the_form_in_the_way_its_drop_down_asked_for_and_changes_it_in_place() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        await AddRoleAsync("Filling", sortOrder: 2, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == miniId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == miniId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");

        // Link: the tab opens with the form already linking, and its title says so in place of the picker that used to.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.AddSubAssemblyTab);
        state.UsageForm.EditingUsageMode.Should().Be(UsageFormMode.Link);
        state.UsageForm.UsageFormTitle.Should().Be("Link an existing sub-assembly");
        state.UsageForm.IsFormPickingSource.Should().BeTrue("a link picks the sub-assembly it comes from");
        state.UsageForm.IsFormEditingLines.Should().BeFalse("a link's lines are its owner's");
        state.UsageForm.IsFormNewMovingToSubAssemblies.Should().BeFalse("only a new one carries the badge");

        // Copy, from the same tab: the form changes in place, and nothing else does.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Copy);
        state.Recipe.RecipeTabs.OfType<AddSubAssemblyRecipeTab>().Should().ContainSingle("the tab showing is the one that changes");
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.AddSubAssemblyTab);
        state.UsageForm.UsageFormTitle.Should().Be("Copy an existing sub-assembly");
        state.UsageForm.IsFormPickingSource.Should().BeTrue();
        state.UsageForm.IsFormEditingLines.Should().BeTrue("a copy is the product's own from then on");
        state.Recipe.UsageEdits.Should().BeEmpty("nothing is in the recipe until the form is applied");

        // New: no source to pick, so the form's picker row isn't there at all, and no title either: in its place the
        // badge says Save keeps it on the Sub-Assemblies screen, which it does unless its role is product-owned.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.UsageFormTitle.Should().BeEmpty();
        state.UsageForm.IsFormPickingSource.Should().BeFalse();
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Dough", "the first role the recipe doesn't have yet");
        state.UsageForm.IsFormNewMovingToSubAssemblies.Should().BeTrue("a Dough isn't product-owned, so Save moves it to Sub-Assemblies");
        state.UsageForm.PickRole(state.UsageForm.FormRoleOptions.Single(r => r.Name == "Filling"));
        state.UsageForm.IsFormNewMovingToSubAssemblies.Should().BeFalse("a Filling is product-owned, so it stays the product's own");
        state.UsageForm.PickRole(state.UsageForm.FormRoleOptions.Single(r => r.Name == "Dough"));

        // Each pick is its own undo step, back to the add that opened the tab, and then the tab itself.
        state.TryUndo().Should().BeTrue();
        state.UsageForm.EditingUsageMode.Should().Be(UsageFormMode.Copy);
        state.TryUndo().Should().BeTrue();
        state.UsageForm.EditingUsageMode.Should().Be(UsageFormMode.Link);
        state.TryUndo().Should().BeTrue();
        state.Recipe.RecipeTabs.Should().NotContain(state.Recipe.AddSubAssemblyTab, "the add went with its tab in one step");
        state.UsageForm.IsUsageFormOpen.Should().BeFalse();
    });

    [Test]
    public Task A_linked_sub_assembly_follows_its_changes_and_Make_a_Copy_saves_a_new_one_the_product_links() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (bigId, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        // Link Cream Loaf's dough into the mini, at its own weight.
        state.Pick(state.Products.Single(p => p.Id == miniId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == miniId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.UsageForm.IsFormEditingLines.Should().BeFalse("a link's lines are the Sub-Assemblies screen's");
        state.UsageForm.PickSource(state.UsageForm.LinkableSubAssemblies.Single());
        state.UsageForm.EditingUsageSource!.DisplayName.Should().Be("Cream Loaf Dough — Sub-Assemblies (Dough)");
        await Settle.UntilAsync(state, () => state.UsageForm.FormLines.Count == 1, () => $"form lines={state.UsageForm.FormLines.Count}");
        state.UsageForm.IsFormShowingLines.Should().BeTrue("the sub-assembly picked to link shows the lines it brings, read-only");
        state.UsageForm.FormLines.Single().Grams.Should().Be(1000m);
        state.UsageForm.TypeWeightPerPiece(30m);
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        var linked = state.Recipe.UsageEdits.Single();
        linked.IsLinked.Should().BeTrue();
        linked.LinkedFromText.Should().Be("Cream Loaf Dough, from Sub-Assemblies");
        linked.CostPerPieceAt(CostBasis.Buffered).Should().Be(1.5m, "30 g of a ₱50, 1,000 g batch");

        // Its tab shows its form, with its lines in it to look at but not to change.
        state.UsageForm.EditingUsage.Should().BeSameAs(linked);
        state.UsageForm.IsFormShowingLines.Should().BeTrue();
        state.UsageForm.IsFormEditingLines.Should().BeFalse();
        state.UsageForm.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal(("Flour", 1000m));

        // Butter goes into the dough on the Sub-Assemblies screen: the mini's linked section follows on the next refresh.
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Id = doughId, Name = "Cream Loaf Dough", RoleId = roleId }, [(flourId, 1000m), (butterId, 250m)], Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.Recipe.UsageEdits.Single().TotalWeight.Should().Be(1250m);
        state.Recipe.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered).Should().Be(3.6m, "30 g of a ₱150, 1,250 g batch");
        state.UsageForm.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should()
            .Equal([("Flour", 1000m), ("Butter", 250m)], "the read-only lines show the sub-assembly's, as they now are");
        state.SaveCommand.CanExecute().Should().BeFalse("the lines were saved on the Sub-Assemblies screen, not typed here");
        state.HasUnsavedChanges.Should().BeFalse();

        // Make a Copy: the mini gets a dough of its own to change, at the same weight, and stops following.
        await state.Recipe.MakeCopyCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        var copy = state.Recipe.UsageEdits.Single();
        copy.IsNew.Should().BeTrue();
        copy.RoleName.Should().Be("Dough");
        state.Recipe.RecipeTabs[0].Header.Should().Be("Dough");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var miniUsage = await dbContext.ProductSubAssemblyUsages.Include(u => u.SubAssembly).SingleAsync(u => u.ProductId == miniId, Cancellation);
        miniUsage.SubAssemblyId.Should().NotBe(doughId);
        miniUsage.SubAssembly!.Name.Should().Be("Cream Loaf Mini Dough");
        (await SubAssemblyTotalsAsync(miniUsage.SubAssemblyId)).TotalWeight.Should().Be(1250m);
        miniUsage.AuthoredValue.Should().Be(30m);
    });

    // Something typed before the load and undone after it, or redone: the linked dough, in its section and in the form
    // open on it, stays as it's saved now, not as it was when the step was taken.
    [Test]
    public Task Undoing_an_edit_from_before_a_load_keeps_a_linked_sections_lines_as_saved_now() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == miniId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == miniId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.UsageForm.PickSource(state.UsageForm.LinkableSubAssemblies.Single());
        await Settle.UntilAsync(state, () => state.UsageForm.FormLines.Count == 1, () => $"form lines={state.UsageForm.FormLines.Count}");
        state.UsageForm.TypeWeightPerPiece(30m);
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1 && !state.HasUnsavedChanges, () => $"usage rows={state.Recipe.UsageEdits.Count}, dirty={state.HasUnsavedChanges}");

        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.EditingName = "Cream Loaf Petite";
        recorder.EndUndoStep();

        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Id = doughId, Name = "Cream Loaf Dough", RoleId = roleId }, [(flourId, 1000m), (butterId, 250m)], Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.Recipe.UsageEdits.Single().TotalWeight.Should().Be(1250m);

        state.TryUndo().Should().BeTrue();

        state.EditingName.Should().Be("Cream Loaf Mini");
        state.Recipe.UsageEdits.Single().TotalWeight.Should().Be(1250m, "the dough as it's saved now, not as it was");
        state.UsageForm.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should()
            .Equal([("Flour", 1000m), ("Butter", 250m)], "the form open on it shows it as saved now too");
        state.SaveCommand.CanExecute().Should().BeFalse("nothing typed is left");

        state.TryRedo().Should().BeTrue();

        state.EditingName.Should().Be("Cream Loaf Petite");
        state.Recipe.UsageEdits.Single().TotalWeight.Should().Be(1250m);
        state.UsageForm.FormLines.Select(l => l.Ingredient!.Name).Should().Equal("Flour", "Butter");
    });

    // Something typed before a load that moved an ingredient's cost, undone after it, or redone: the product's own dough and
    // the header show the cost the load brought, not the one the step was taken at.
    [Test]
    public Task Undoing_an_edit_from_before_a_load_shows_the_costs_the_load_brought() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Dough", isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(
            state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"ready={state.AreDetailsReady}");
        state.Recipe.UsageEdits.Single().IsOwned.Should().BeTrue();
        state.Costing.Costs.Material.Should().Be(3m, "60 g of flour at 0.05");

        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.EditingName = "Cream Loaf Large";
        recorder.EndUndoStep();

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(m => m.Id == flourId).ExecuteUpdateAsync(
                set => set.SetProperty(m => m.CostPerBaseUnit, 0.08m).SetProperty(m => m.BufferedCostPerBaseUnit, 0.08m), Cancellation);
        }

        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.Costing.Costs.Material == 4.8m, () => $"material={state.Costing.Costs.Material}");

        state.TryUndo().Should().BeTrue();

        state.EditingName.Should().Be("Cream Loaf");
        state.Recipe.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered).Should().Be(4.8m, "the dough at the flour's cost now, 0.08, not 0.05 as when the step was taken");
        state.Costing.Costs.Material.Should().Be(4.8m, "and the header with it");
        state.SaveCommand.CanExecute().Should().BeFalse("nothing typed is left");

        state.TryRedo().Should().BeTrue();

        state.EditingName.Should().Be("Cream Loaf Large");
        state.Costing.Costs.Material.Should().Be(4.8m);
    });

    [Test]
    public Task A_linked_sections_lines_are_edited_in_the_product_and_Save_writes_them_to_the_sub_assembly_after_asking() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(miniId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));
        var state = CreateState();
        string? confirmation = null;
        var confirm = false;
        state.ConfirmDelete.RegisterHandler(context =>
        {
            confirmation = context.Input;
            context.SetOutput(confirm);
        });
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == miniId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // Its form types into its lines as it would a section of the product's own.
        await state.Recipe.EditUsageCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        state.UsageForm.IsEditingLinkedUsage.Should().BeTrue();
        state.UsageForm.IsFormEditingLines.Should().BeFalse("its role and name are still the Sub-Assemblies screen's");
        state.UsageForm.IsFormLinesEditable.Should().BeTrue();
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = butterId, Grams = 250m });

        // A refresh while the form is open keeps what was typed into its lines.
        await state.LoadCommand.ExecuteAsync();
        state.UsageForm.FormLines.Select(l => (l.IngredientId, l.Grams)).Should().Equal((flourId, 1000m), (butterId, 250m));

        // Save asks first, since Cream Loaf changes too: declined, nothing is written and the change stays in the form.
        await state.SaveCommand.ExecuteAsync();
        confirmation.Should().Be("Save changes the lines of Cream Loaf Dough, also used by Cream Loaf. Their recipes and costs change with it. Save anyway?");
        var edited = state.Recipe.UsageEdits.Single();
        edited.AreLinkedLinesEdited.Should().BeTrue();
        edited.TotalWeight.Should().Be(1250m);
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.SubAssemblyLines.CountAsync(l => l.SubAssemblyId == doughId, Cancellation)).Should().Be(1);
        }

        confirm = true;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.SingleOrDefault() is { AreLinkedLinesEdited: false, TotalWeight: 1250m },
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");
        var saved = state.Recipe.UsageEdits.Single();
        saved.IsLinked.Should().BeTrue("it's still the one kept on the Sub-Assemblies screen, not a copy");
        saved.SubAssemblyId.Should().Be(doughId);
        saved.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.6m, "30 g of a ₱150, 1,250 g batch");
        state.History.IsDirty.Should().BeFalse("what was saved is the new starting point");
        await using (var dbContext = CreateDbContext())
        {
            (await SubAssemblyTotalsAsync(doughId)).Should().Be((1250m, 150m));
            (await dbContext.SubAssemblyLines.Where(l => l.SubAssemblyId == doughId).OrderBy(l => l.SortOrder).Select(l => l.IngredientId).ToListAsync(Cancellation))
                .Should().Equal(flourId, butterId);
        }
    });

    [Test]
    public Task Changing_a_link_relinks_the_section_keeping_its_piece_size_and_the_add_ins_the_new_recipe_lacks() => UiThread.RunAsync(async () =>
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
            [], [new LinkedUsageDraft(houseId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(butterId, 100m), (ubeId, 50m)] }]));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        await state.Recipe.EditUsageCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());

        await state.UsageForm.StartChangingLinkCommand.ExecuteAsync();

        state.UsageForm.IsChangingLink.Should().BeTrue();
        state.UsageForm.IsShowingLinkBadge.Should().BeFalse("the picker takes the badge's place");
        state.UsageForm.ChangeLinkOptions.Select(o => o.SubAssembly!.Id).Should().Equal([richId], "House Dough is the one linked already");

        state.UsageForm.PickLinkTarget(state.UsageForm.ChangeLinkOptions.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Single().SubAssemblyId == richId, () => $"linked={state.Recipe.UsageEdits.Single().SubAssemblyId}");

        var relinked = state.Recipe.UsageEdits.Single();
        relinked.IsLinked.Should().BeTrue();
        relinked.LinkedName.Should().Be("Rich Dough");
        (relinked.AuthoredField, relinked.AuthoredValue).Should().Be((UsageAuthoredField.WeightPerPiece, 30m));
        relinked.AddIns.Select(a => a.Ingredient.Id).Should().Equal([ubeId], "Rich Dough has butter already, so that add-in goes");
        state.SuccessMessage.Should().Be("Now linked to Rich Dough. Butter left the add-ins: Rich Dough already has it.");
        state.UsageForm.EditingUsage.Should().BeSameAs(relinked, "its tab's form follows it");
        state.UsageForm.IsChangingLink.Should().BeFalse();
        state.UsageForm.IsShowingLinkBadge.Should().BeTrue();

        // One step back is the old link, add-ins and all.
        state.TryUndo().Should().BeTrue();
        state.Recipe.UsageEdits.Single().SubAssemblyId.Should().Be(houseId);
        state.Recipe.UsageEdits.Single().AddIns.Should().HaveCount(2);
        state.TryRedo().Should().BeTrue();

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.AsNoTracking().SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.SubAssemblyId.Should().Be(richId);
        usage.AuthoredValue.Should().Be(30m);
        (await dbContext.SubAssemblies.CountAsync(s => s.Id == houseId, Cancellation)).Should().Be(1, "the old one stays on the Sub-Assemblies screen");
    });

    [Test]
    public Task Cancel_on_a_link_being_changed_puts_the_badge_back_and_keeps_the_link() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var houseId = await subAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "House Dough", RoleId = roleId }, [(flourId, 1000m)], Cancellation);
        await subAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "Rich Dough", RoleId = roleId }, [(flourId, 1200m)], Cancellation);
        var productId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(houseId, UsageAuthoredField.WeightPerPiece, 30m)]));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        await state.Recipe.EditUsageCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        await state.UsageForm.StartChangingLinkCommand.ExecuteAsync();
        state.UsageForm.IsChangingLink.Should().BeTrue();

        await state.UsageForm.CancelChangingLinkCommand.ExecuteAsync();

        state.UsageForm.IsChangingLink.Should().BeFalse();
        state.UsageForm.IsShowingLinkBadge.Should().BeTrue("the badge comes back in the picker's place");
        state.Recipe.UsageEdits.Single().SubAssemblyId.Should().Be(houseId, "nothing was picked, so the link stays");
        state.History.IsDirty.Should().BeFalse("opening and closing the picker changes nothing Save writes");
    });

    [Test]
    public Task Remove_Line_under_a_linked_sections_add_ins_takes_out_the_one_picked_as_one_undo_step() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        var houseId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = roleId }, [(flourId, 1000m)], Cancellation);
        var productId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [], [new LinkedUsageDraft(houseId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(butterId, 100m), (ubeId, 50m)] }]));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        await state.Recipe.EditUsageCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        var form = state.UsageForm;
        form.RemoveAddInCommand.CanExecute().Should().BeFalse("no add-in is picked yet");

        form.SelectedFormAddIn = form.FormAddIns.Single(l => l.IngredientId == butterId);
        form.RemoveAddInCommand.CanExecute().Should().BeTrue();
        await form.RemoveAddInCommand.ExecuteAsync();

        form.FormAddIns.Select(l => l.IngredientId).Should().Equal(ubeId);
        state.TryUndo().Should().BeTrue();
        form.FormAddIns.Select(l => l.IngredientId).Should().BeEquivalentTo([butterId, ubeId], "Undo puts it back");
    });

    [Test]
    public Task Unlink_makes_a_linked_section_the_products_own_in_a_role_that_isnt_product_owned() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var butterId = await AddIngredientAsync("Butter", costPerGram: 0.4m);
        var subAssemblies = new SubAssemblyRepository(CreateDbContextFactory());
        var doughId = await subAssemblies.SaveStandaloneAsync(new SubAssembly { Name = "House Dough", RoleId = roleId }, [(flourId, 1000m)], Cancellation);
        var productId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(butterId, 250m)] }]));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.UsageForm.IsEditingLinkedUsage.Should().BeTrue("Unlink shows on a linked section");

        await state.Recipe.UnlinkCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());

        // Its own before Save: the badge shows, its lines are typed into, and its add-in is one of them.
        var copy = state.Recipe.UsageEdits.Single();
        copy.IsOwned.Should().BeTrue();
        copy.IsLinked.Should().BeFalse();
        copy.Lines.Select(l => (l.Ingredient.Name, l.Grams)).Should().Equal(("Flour", 1000m), ("Butter", 250m));
        state.UsageForm.EditingUsage!.IsOwned.Should().BeTrue();
        state.UsageForm.IsFormEditingLines.Should().BeTrue();
        state.UsageForm.IsEditingLinkedUsage.Should().BeFalse();

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1 && state.Recipe.UsageEdits[0].SubAssemblyId > 0,
            () => $"usage rows={state.Recipe.UsageEdits.Count}");

        state.Recipe.UsageEdits.Single().IsOwned.Should().BeTrue("it's read back as the product's own");
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.Include(u => u.SubAssembly).SingleAsync(u => u.ProductId == productId, Cancellation);
        usage.SubAssemblyId.Should().NotBe(doughId);
        usage.SubAssembly!.Name.Should().Be("Cream Loaf Mini Dough");
        usage.SubAssembly.OwnerProductId.Should().Be(productId);
        (await SubAssemblyTotalsAsync(usage.SubAssemblyId)).TotalWeight.Should().Be(1250m);
        usage.AuthoredValue.Should().Be(30m);
        (await dbContext.UsageAddInLines.AnyAsync(Cancellation)).Should().BeFalse("the add-in went into the copy's lines");
        (await subAssemblies.GetStandaloneAsync(Cancellation)).Select(s => s.Name).Should()
            .Equal(["House Dough"], "the one it was linked to stays on the Sub-Assemblies screen, and the copy isn't there");
        (await SubAssemblyTotalsAsync(doughId)).TotalWeight.Should().Be(1000m, "as it was");
    });

    [Test]
    public Task Move_to_Sub_Assemblies_puts_the_products_own_there_with_what_was_typed_and_links_it() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(roleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var fillingId = await SubAssemblyIdAsync(productId, roleId);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.UsageForm.FormLines.Count == 1, () => $"form lines={state.UsageForm.FormLines.Count}");
        state.UsageForm.EditingUsage!.IsOwned.Should().BeTrue();
        state.UsageForm.IsEditingOwnedUsage.Should().BeTrue("Move to Sub-Assemblies shows on the product's own");

        // Typed into the form and not applied yet: the move takes it along.
        state.UsageForm.FormLines.Single().Grams = 120m;
        await state.Recipe.MoveToSubAssembliesCommand.ExecuteAsync(state.UsageForm.EditingUsage);

        var moving = state.Recipe.UsageEdits.Single();
        moving.IsReleasing.Should().BeTrue();
        moving.Lines.Single().Grams.Should().Be(120m);
        state.UsageForm.EditingUsage.Should().BeSameAs(moving, "its tab is its form, so the form stays open on it");
        state.UsageForm.IsEditingOwnedUsage.Should().BeFalse("it's on its way already");
        state.UsageForm.IsFormEditingLines.Should().BeTrue("its lines are still edited here until Save");
        state.SaveCommand.CanExecute().Should().BeTrue();

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1 && state.Recipe.UsageEdits[0].IsLinked,
            () => $"linked={state.Recipe.UsageEdits.FirstOrDefault()?.IsLinked}");

        var linked = state.Recipe.UsageEdits.Single();
        linked.SubAssemblyId.Should().Be(fillingId, "the same sub-assembly, not a copy");
        linked.LinkedName.Should().Be("Cheese Roll Filling");
        var moved = await new SubAssemblyRepository(CreateDbContextFactory()).GetStandaloneAsync(Cancellation);
        moved.Should().ContainSingle().Which.Id.Should().Be(fillingId);
        (await SubAssemblyTotalsAsync(fillingId)).TotalWeight.Should().Be(120m);
    });

    [Test]
    public Task Move_to_In_House_asks_for_a_name_and_Made_category_and_Save_moves_the_section_to_Sub_Assemblies_and_makes_the_ingredient_from_it() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var inHouseId = await MadeCategoryIdAsync();
        await MadeCategoryIdAsync("Fillings");
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(roleId, [(cheeseId, 100m), (milkId, 50m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var fillingId = await SubAssemblyIdAsync(productId, roleId);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.UsageForm.FormLines.Count == 2, () => $"form lines={state.UsageForm.FormLines.Count}");
        state.UsageForm.CanMoveEditingUsageToInHouse.Should().BeTrue("Move to In-House shows on the product's own");
        ChoicePromptRequest? asked = null;
        state.Recipe.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(new ChoicePromptResult("Cheese Filling", interaction.Input.InitialChoice));
        });

        // Typed into the form and not applied yet: the move takes it along.
        state.UsageForm.FormLines[0].Grams = 120m;
        await state.Recipe.MoveToInHouseCommand.ExecuteAsync(state.UsageForm.EditingUsage!);

        state.ErrorMessage.Should().BeNull();
        asked!.Title.Should().Be("Move to In-House");
        asked.InitialValue.Should().Be("Cheese Roll Filling", "the name it's saved under");
        asked.Choices.Should().Equal(["Fillings", "In-House"], "the Made categories, by name");
        asked.InitialChoice.Should().Be(1, "In-House, when there's one");
        var moving = state.Recipe.UsageEdits.Single();
        moving.ToMadeIngredient.Should().Be(new MadeIngredientDraft("Cheese Filling", inHouseId));
        moving.MovingToInHouseText.Should().Be("Moves to Sub-Assemblies and In-House on Save, as Cheese Filling");
        moving.Lines[0].Grams.Should().Be(120m);
        state.UsageForm.EditingUsage.Should().BeSameAs(moving, "its tab is its form, so the form stays open on it");
        state.UsageForm.CanMoveEditingUsageToInHouse.Should().BeFalse("it's on its way already");
        state.UsageForm.IsEditingOwnedUsage.Should().BeFalse("so it can't move to Sub-Assemblies as well");
        var costPerPiece = moving.CostPerPieceAt(CostBasis.Buffered);

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits is [{ IsOwned: false, Lines.Count: 2 }],
            () => $"lines={state.Recipe.UsageEdits.FirstOrDefault()?.Lines.Count}, own={state.Recipe.UsageEdits.FirstOrDefault()?.IsOwned}");

        var saved = state.Recipe.UsageEdits.Single();
        saved.SubAssemblyId.Should().Be(fillingId, "the same section, linked from the Sub-Assemblies screen now");
        saved.Lines.Select(l => (l.Ingredient.Name, l.Grams)).Should().Equal(("Cheese", 120m), ("Milk", 50m));
        saved.CostPerPieceAt(CostBasis.Buffered).Should().BeApproximately(costPerPiece, 0.000001m, "it costs what it did");
        state.Ingredients.Should().Contain(m => m.Name == "Cheese Filling" && m.Source == RawMaterialSource.Made && m.SubAssemblyId == fillingId);
    });

    [Test]
    public Task Move_to_In_House_needs_a_Made_category_and_a_name_no_ingredient_has() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Filling", isProductOwned: true);
        var cheeseId = await AddIngredientAsync("Cheese", costPerGram: 0.4m);
        var productId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(roleId, [(cheeseId, 100m)], UsageAuthoredField.WeightPerPiece, 10m)], []));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.UsageForm.FormLines.Count == 1, () => $"form lines={state.UsageForm.FormLines.Count}");
        var asked = 0;
        state.Recipe.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            asked++;
            interaction.SetOutput(new ChoicePromptResult("cheese", 0));
        });

        await state.Recipe.MoveToInHouseCommand.ExecuteAsync(state.UsageForm.EditingUsage!);

        state.ErrorMessage.Should().Be("There's no Made category to put it in. Add one on the Raw Materials screen first, with New Made Category.");
        asked.Should().Be(0, "there's nothing to pick");

        // One added since is offered at once: the categories are read when it's clicked.
        await MadeCategoryIdAsync();
        await state.Recipe.MoveToInHouseCommand.ExecuteAsync(state.UsageForm.EditingUsage!);

        state.ErrorMessage.Should().Be("There's already a raw material named 'cheese'. Give the made ingredient its own name.");
        state.Recipe.UsageEdits.Single().IsMovingToInHouse.Should().BeFalse();
        state.SaveCommand.CanExecute().Should().BeFalse("nothing changed");
    });

    [Test]
    public Task Removing_a_sub_assembly_another_product_links_leaves_it_there() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (creamLoafId, doughId) = await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(flourId, 1000m)]);
        var miniId = await AddProductAsync("Cream Loaf Mini");
        await SaveRecipeAsync(miniId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m)]));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == creamLoafId));
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        await state.Recipe.RemoveUsageCommand.ExecuteAsync();
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.Recipe.UsageEdits.Should().BeEmpty();
        (await new ProductSubAssemblyUsageRepository(CreateDbContextFactory()).GetForProductAsync(miniId, Cancellation))
            .Should().ContainSingle(u => u.SubAssemblyId == doughId, "the dough stays on the Sub-Assemblies screen, linked where it was");
    });

    [Test]
    public Task The_recipe_tabs_are_each_sub_assembly_in_recipe_order_then_Packaging() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Recipe.RecipeTabs.Should().Equal([state.Recipe.PackagingTab], "a new product starts with nothing in its recipe, but the Packaging tab is always there, for its bulk packaging");
        state.Recipe.HasRecipeSections.Should().BeFalse();
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.PackagingTab, "its tables are there to type into from the start");
        state.UsageForm.IsUsageFormOpen.Should().BeFalse("nothing is being added until Add Sub-Assembly is pressed");

        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 2, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Packaging");
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.RecipeTabs[0]);
        state.UsageForm.EditingUsage.Should().BeSameAs(((SubAssemblyRecipeTab)state.Recipe.RecipeTabs[0]).Usage, "a sub-assembly's tab is the form that edits it");
    });

    [Test]
    public Task The_costing_summary_breaks_the_sub_assemblies_down_one_by_one_per_sellable_unit() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 2, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // Sold in twos: each sub-assembly's cost is per unit of two pieces, as the Sub-assemblies row is.
        state.EditingQuantityRequired = 2;
        state.Costing.EditingLaborPercent = 20m;

        var costs = state.Costing.Costs;
        costs.SubAssemblyLines.Select(l => (l.Name, l.Cost)).Should()
            .Equal([("Dough", 5m), ("Filling", 4.8m)], "₱2.50 of dough and ₱2.40 of filling a piece, in recipe order");
        costs.SubAssemblyLines.Sum(l => l.Cost).Should().Be(costs.SubAssemblies);
        costs.SubAssemblyLines.Should().AllSatisfy(l => l.Share.Should().Be(CostingRules.PercentOf(l.Cost, costs.SellingPrice)));
    });

    [Test]
    public Task A_role_reordered_in_Settings_moves_its_tab_on_the_next_refresh() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 2, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Packaging");

        // Filling now sorts before Dough, the way Settings' Roles screen would save it.
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.SubAssemblyRoles.FindAsync([doughRoleId], Cancellation))!.SortOrder = 2;
            (await dbContext.SubAssemblyRoles.FindAsync([fillingRoleId], Cancellation))!.SortOrder = 1;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await state.LoadCommand.ExecuteAsync();

        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "Dough", "Packaging");
    });

    [Test]
    public Task Adding_sub_assemblies_out_of_role_order_still_sorts_their_tabs_by_role() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Ube Roll";

        // Filling (role order 2) typed first, then Dough (role order 1) — the reverse of role order.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == fillingRoleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = ubeId, Grams = 100m });
        state.UsageForm.TypeWeightPerPiece(10m);

        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.FormRoleOptions.Select(r => r.Name).Should().Equal(["Dough", "Filling"], "a recipe can have more than one in a role");
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Dough", "the first role the recipe doesn't have yet");
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == doughRoleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1000m });
        state.UsageForm.TypeWeightPerPiece(50m);
        await state.SaveCommand.ExecuteAsync();

        // Dough sorts before Filling regardless of which was typed in first, and it's the tab showing.
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Packaging");
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.RecipeTabs[0]);
    });

    [Test]
    public Task OpenProduct_waits_for_a_list_not_loaded_yet_and_clears_a_search_hiding_the_product() => UiThread.RunAsync(async () =>
    {
        var creamLoafId = await AddProductAsync("Cream Loaf");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var state = CreateState();

        // As a Sub-Assemblies Dependents tile does before the Products screen was ever shown.
        state.OpenRecord(ensaymadaId);
        state.SelectedProduct.Should().BeNull("the list is empty until the screen loads");

        await state.LoadCommand.ExecuteAsync();

        state.SelectedProduct!.Id.Should().Be(ensaymadaId);
        state.EditingName.Should().Be("Ensaymada", "the product's form is loaded, as picking it in the list would");

        state.View.List.SearchText = "Cream";
        state.OpenRecord(creamLoafId);
        state.SelectedProduct!.Id.Should().Be(creamLoafId);
        state.View.List.SearchText.Should().Be("Cream", "a search showing the product is left alone");

        state.OpenRecord(ensaymadaId);
        state.SelectedProduct!.Id.Should().Be(ensaymadaId);
        state.View.List.SearchText.Should().BeEmpty("a search hiding the product is cleared, so the list shows the row picked");
        state.View.List.VisibleProducts.Should().Contain(state.SelectedProduct);
    });

    [Test]
    public Task StartNewProductFrom_starts_a_new_product_linking_the_sub_assembly_on_the_next_load_and_Save_writes_the_link() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var creamLoafId = await AddProductAsync("Cream Loaf");
        var doughId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Pandesal Dough", RoleId = roleId }, [(flourId, 1000m)], Cancellation);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.OpenRecord(creamLoafId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");

        // As Sub-Assemblies' New Product does: nothing changes until the load that showing the screen starts.
        state.View.StartNewProductFrom(doughId);
        state.SelectedProduct!.Id.Should().Be(creamLoafId);

        await state.LoadCommand.ExecuteAsync();

        state.SelectedProduct.Should().BeNull("the product is a new one, not saved yet");
        state.EditingName.Should().Be("Pandesal Dough", "it's named after the sub-assembly, to rename");
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.AddSubAssemblyTab);
        state.UsageForm.EditingUsageMode.Should().Be(UsageFormMode.Link);
        state.UsageForm.EditingUsageSource!.SubAssembly!.Id.Should().Be(doughId);
        await Settle.UntilAsync(state, () => state.UsageForm.FormLines.Count == 1, () => $"form lines={state.UsageForm.FormLines.Count}");
        state.History.IsDirty.Should().BeTrue("Save is offered straight away");

        state.EditingName = "Pandesal";
        state.UsageForm.TypeWeightPerPiece(30m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.SelectedProduct!.Name.Should().Be("Pandesal");
        await using var dbContext = CreateDbContext();
        var usage = await dbContext.ProductSubAssemblyUsages.SingleAsync(u => u.ProductId == state.SelectedProduct.Id, Cancellation);
        usage.SubAssemblyId.Should().Be(doughId, "the product links the sub-assembly, not a copy of it");
    });

    [Test]
    public Task OpenByName_opens_the_product_a_name_means_best_as_the_Vim_command_line_asks() => UiThread.RunAsync(async () =>
    {
        await AddProductAsync("Ube Cheese Pandesal");
        var pandesalId = await AddProductAsync("Pandesal");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.OpenByName("pandesal").Should().BeTrue();

        state.SelectedProduct!.Id.Should().Be(pandesalId, "the name typed in full beats one that only holds it");
        state.OpenByName("croissant").Should().BeFalse();
        state.SelectedProduct!.Id.Should().Be(pandesalId, "nothing matching leaves the product open");
    });

    [Test]
    public Task A_role_changed_on_the_Sub_Assemblies_screen_moves_its_tab_to_where_the_new_role_sorts() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 2, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Packaging");
        var doughId = await SubAssemblyIdAsync(productId, doughRoleId);

        // Re-role the Dough as a Topping (order 3) where it's kept: on coming back it sorts after the Filling (order 2),
        // and is called Topping.
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Id = doughId, Name = "Ube Roll Dough", RoleId = toppingRoleId }, [(flourId, 1000m)], Cancellation);
        await state.LoadCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "Topping", "Packaging");
    });

    /// <summary>
    /// The piece size isn't a requirement: a section goes into the recipe, and
    /// saves, before how much of it a piece takes is known. It just costs nothing per piece until one is typed.
    /// </summary>
    [Test]
    public Task A_sub_assembly_goes_in_and_saves_before_its_piece_size_is_known() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Pan de Sal");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == productId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");

        // A section with its lines but neither figure of its piece size: it applies, with nothing refused.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == roleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 1000m });
        state.UsageForm.UsageFormCostPerPieceText.Should().Be("\u2014", "a piece takes none of it yet");
        state.UsageForm.UsageFormBatchCostText.Should().Be("50.00", "the batch costs its lines, sized or not: 1,000 g at 0.05");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        var usage = state.Recipe.UsageEdits.Single();
        usage.TotalWeight.Should().Be(1000m, "the batch is its lines, sized or not");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(0m);
        state.Costing.Costs.Material.Should().Be(0m, "an unsized section costs the product nothing per piece");

        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.ProductSubAssemblyUsages.SingleAsync(u => u.ProductId == productId, Cancellation);
            saved.AuthoredValue.Should().Be(0m, "stored as it was left, waiting to be sized");
        }

        // Sized later, from its own tab: it prices at once, and saves over the unsized one.
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single());
        state.UsageForm.TypeWeightPerPiece(50m);
        state.UsageForm.EditingUsageYield.Should().Be(20m, "1,000 g at 50 g a piece");
        state.UsageForm.UsageFormCostPerPieceText.Should().Be("2.50", "the batch's 50.00 over 20 pieces, to two decimals like the tile beside it");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered) == 2.5m, () => $"cost per piece={state.Recipe.UsageEdits.Single().CostPerPieceAt(CostBasis.Buffered)}");
    });

    [Test]
    public Task A_second_sub_assembly_in_a_role_needs_a_name_and_the_tabs_then_read_by_their_names() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // Linking: Cream Loaf's Dough is offered too, though the recipe has a Dough; it would go by its name. The recipe's
        // own Dough isn't, as it's linked already.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.UsageForm.LinkableSubAssemblies.Select(o => o.DisplayName).Should().Equal(
            "Cream Loaf Dough — Sub-Assemblies (Dough)", "Ube Bun Filling — Sub-Assemblies (Filling)");

        // A new one in the Dough role with no name, beside the Dough that has none: refused, and nothing goes in.
        // Another way picked on the add tab changes the form open there, rather than opening a second one.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.Recipe.RecipeTabs.OfType<AddSubAssemblyRecipeTab>().Should().ContainSingle();
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == doughRoleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = ubeId, Grams = 500m });
        state.UsageForm.TypeWeightPerPiece(20m);
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().StartWith("This recipe has more than one Dough without a name");
        state.Recipe.UsageEdits.Should().ContainSingle();
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.AddSubAssemblyTab, "the form has to be put right first");

        // Named, it goes in, and the second Dough's tab reads by its name.
        state.UsageForm.EditingUsageName = "Ube";
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header).Should().Equal("Dough", "Dough: Ube");

        // Saved and loaded again, both are there, by the same names.
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        var reloaded = CreateState();
        await reloaded.LoadCommand.ExecuteAsync();
        reloaded.Pick(reloaded.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(reloaded, () => reloaded.Recipe.UsageEdits.Count == 2, () => $"usage rows={reloaded.Recipe.UsageEdits.Count}");
        reloaded.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header).Should().Equal("Dough", "Dough: Ube");

        // Taking the other one out leaves the named one alone in its role: it's plain "Dough" again.
        reloaded.Recipe.PickRecipeTab(reloaded.Recipe.RecipeTabs[0]);
        await reloaded.Recipe.RemoveUsageCommand.ExecuteAsync();
        reloaded.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header).Should().Equal("Dough");
        reloaded.Recipe.UsageEdits.Single().Name.Should().Be("Ube", "it keeps its name, for when another joins it");
    });
    [Test]
    public Task Save_leaves_the_Add_Sub_Assembly_form_alone_when_nothing_was_entered_in_it() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.AddSubAssemblyTab);
        state.UsageForm.IsUsageFormOpen.Should().BeTrue();
        state.EditingName = "Gift Box Set";

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.SelectedProduct is not null && !state.Recipe.RecipeTabs.Contains(state.Recipe.AddSubAssemblyTab),
            () => $"selected={state.SelectedProduct?.Id}, tabs={state.Recipe.RecipeTabs.Count}");
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(Cancellation)).Name.Should().Be("Gift Box Set");
            (await dbContext.ProductSubAssemblyUsages.CountAsync(Cancellation)).Should().Be(0);
        }

        // The reload shows the saved product's tabs — only Packaging, always there — and the New Sub-Assembly tab, with nothing in it, goes.
        state.Recipe.RecipeTabs.Should().Equal(state.Recipe.PackagingTab);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.PackagingTab);
    });

    [Test]
    public Task Revert_reads_the_product_back_as_it_was_saved_and_drops_a_sub_assembly_being_added() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)], []));
        var state = CreateState();
        state.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // Rename it, retype the Dough's piece size, then start a Filling that is left sitting in the form.
        state.EditingName = "Ube Roll Mini";
        await state.Recipe.EditUsageCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        state.UsageForm.TypeWeightPerPiece(60m);
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Filling");
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = ubeId, Grams = 100m });
        state.UsageForm.TypeWeightPerPiece(10m);
        state.Recipe.UsageEdits.Should().ContainSingle("the Dough went in when its tab was left; the Filling is still in the form");

        await state.RevertCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1,
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");
        state.EditingName.Should().Be("Ube Roll", "the typed name goes back to the saved one");
        state.UsageForm.IsAddingUsage.Should().BeFalse("the Filling being added went with the rest");
        state.UsageForm.EditingUsage!.RoleName.Should().Be("Dough", "the reloaded Dough's tab shows, with its own form");
        state.Recipe.UsageEdits.Should().ContainSingle().Which.RoleName.Should().Be("Dough");
        state.Recipe.UsageEdits.Single().WeightPerPiece.Should().Be(50m, "the retyped piece size goes back to the saved 50 g");
        state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Select(t => t.Header).Should().Equal("Dough");

        // Revert only reads: nothing of the typing reached the database.
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.SingleAsync(Cancellation)).Name.Should().Be("Ube Roll");
        (await dbContext.ProductSubAssemblyUsages.CountAsync(Cancellation)).Should().Be(1);
    });

    [Test]
    public Task Add_Sub_Assembly_opens_a_New_Sub_Assembly_tab_that_goes_once_another_tab_shows() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());

        // The load fills the sections, then shows the first one's tab: Delete Sub-Assembly follows the tab.
        await Settle.UntilAsync(state,
            () => state.Recipe.UsageEdits.Count == 1 && state.Recipe.SelectedRecipeTab is SubAssemblyRecipeTab,
            () => $"usage rows={state.Recipe.UsageEdits.Count}, tab={state.Recipe.SelectedRecipeTab?.Header}");
        state.Recipe.RemoveUsageCommand.CanExecute().Should().BeTrue("a sub-assembly's tab is showing");

        // Add Sub-Assembly: its tab goes before Packaging and shows the add form, on the first role the recipe doesn't
        // have. Deleting isn't offered there; adding still is, since its drop-down is the only place the way in can be
        // changed once the form is open.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "New Sub-Assembly", "Packaging");
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.AddSubAssemblyTab);
        state.UsageForm.IsUsageFormOpen.Should().BeTrue();
        state.UsageForm.IsAddingUsage.Should().BeTrue();
        state.UsageForm.FormRoleOptions.Select(r => r.Name).Should().Equal(["Dough", "Filling", "Topping"]);
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Dough");
        // The command has just run, and is offered again as soon as it's done.
        state.Recipe.AddUsageCommand.CanExecute(UsageFormMode.New).Should().BeTrue();
        state.Recipe.RemoveUsageCommand.CanExecute().Should().BeFalse();

        // Left with nothing entered, as a click on another tab leaves it: the form closes and the tab goes.
        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "Packaging");
        state.UsageForm.IsUsageFormOpen.Should().BeFalse();
        state.Recipe.RemoveUsageCommand.CanExecute().Should().BeFalse("the Packaging tab isn't a sub-assembly to delete");

        // Left with a sub-assembly typed in: it's applied, in role order, and its tab goes.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == toppingRoleId));
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = cheeseId, Grams = 200m });
        state.UsageForm.TypeWeightPerPiece(20m);
        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);

        state.ErrorMessage.Should().BeNull();
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Filling", "Topping", "Packaging");
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.PackagingTab);

        // Applied on the way out of its tab: the Dough is the first tab, since Dough is the first role.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Dough", "the only role the recipe doesn't have yet");
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = flourId, Grams = 800m });
        state.UsageForm.TypeWeightPerPiece(40m);
        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);

        state.ErrorMessage.Should().BeNull();
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Topping", "Packaging");

        // Its own tab opens its form again.
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs[0]);
        state.UsageForm.EditingUsage!.RoleName.Should().Be("Dough");

        // Opened from a sub-assembly's tab and left with nothing typed in: the form closes and the New Sub-Assembly tab goes.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Dough", "the recipe has one in every role, so the first is picked");
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First());
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Topping", "Packaging");
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.RecipeTabs[0]);
    });

    [Test]
    public Task Editing_a_sub_assembly_opens_the_form_in_its_tab_which_stays_through_a_refresh_and_Save() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        var tab = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single();

        await state.Recipe.EditUsageCommand.ExecuteAsync(tab.Usage);
        state.UsageForm.EditingUsage.Should().BeSameAs(tab.Usage);
        state.UsageForm.TypeWeightPerPiece(15m);

        // As another tab would, while the form is open: ube's price goes up, and the refresh swaps in a repriced copy.
        await using (var dbContext = CreateDbContext())
        {
            var ube = await dbContext.RawMaterials.SingleAsync(m => m.Id == ubeId, Cancellation);
            ube.CostPerBaseUnit = 0.36m;
            ube.BufferedCostPerBaseUnit = 0.36m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await state.LoadCommand.ExecuteAsync();
        tab.Usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.6m);
        state.UsageForm.EditingUsage.Should().BeSameAs(tab.Usage, "the form follows the repriced copy that took its place");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1,
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");

        // Save reloads the product, so its tab and usage are fresh objects; there is still just the one of each.
        state.Recipe.UsageEdits.Should().ContainSingle("Save replaces the refreshed copy instead of adding a second").Which.WeightPerPiece.Should().Be(15m);
        var savedTab = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Should().ContainSingle().Subject;
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(savedTab);
        state.UsageForm.EditingUsage.Should().BeSameAs(savedTab.Usage, "the tab has nothing else to show, so its form is open on it");
        savedTab.Usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(5.4m, "15 g a piece of a 100 g, ₱36 batch");
        savedTab.Header.Should().Be("Filling");
    });

    [Test]
    public Task Deleting_the_sub_assembly_showing_shows_the_next_tab_and_cancelling_an_add_goes_back() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 2, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // From Packaging, Add Sub-Assembly then back to Packaging: nothing was typed in, so the form closes
        // with nothing applied and the add's tab goes.
        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.AddSubAssemblyTab);
        state.UsageForm.IsUsageFormOpen.Should().BeTrue();
        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.PackagingTab);
        state.UsageForm.IsUsageFormOpen.Should().BeFalse();
        state.Recipe.RecipeTabs.Should().NotContain(state.Recipe.AddSubAssemblyTab);

        // Deleting the first sub-assembly while it shows: the next one's tab shows.
        var dough = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First();
        state.Recipe.PickRecipeTab(dough);
        await state.Recipe.RemoveUsageCommand.ExecuteAsync();
        state.Recipe.UsageEdits.Select(u => u.RoleName).Should().Equal("Filling");
        state.Recipe.SelectedRecipeTab.Should().BeOfType<SubAssemblyRecipeTab>().Which.Header.Should().Be("Filling");

        // Deleting the last: Packaging, the only tab left, shows.
        await state.Recipe.RemoveUsageCommand.ExecuteAsync();
        state.Recipe.RecipeTabs.Should().Equal(state.Recipe.PackagingTab);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.PackagingTab);
        state.UsageForm.IsUsageFormOpen.Should().BeFalse();
    });

    [Test]
    public Task A_standalone_sub_assembly_is_offered_to_link_and_shows_where_its_from() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var glazeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new Domain.Entities.SubAssembly { Name = "Chocolate Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var productId = await AddProductAsync("Cream Loaf");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        // A new product, not saved yet, is offered it too.
        state.UsageForm.PickMode(UsageFormMode.Link);
        state.UsageForm.LinkableSubAssemblies.Should().ContainSingle().Which.DisplayName.Should().Be("Chocolate Glaze — Sub-Assemblies (Glaze)");

        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state,
            () => state.SelectedProduct?.Id == productId && state.AreDetailsReady && !state.Recipe.HasRecipeSections,
            () => $"selected={state.SelectedProduct?.Id}, tabs={state.Recipe.RecipeTabs.Count}");
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.UsageForm.PickSource(state.UsageForm.LinkableSubAssemblies.Single());
        await Settle.UntilAsync(state, () => state.UsageForm.UsageFormTotalWeightText == "100 g", () => $"batch={state.UsageForm.UsageFormTotalWeightText}");
        state.UsageForm.TypeWeightPerPiece(5m);
        await state.SaveCommand.ExecuteAsync();

        var usage = state.Recipe.UsageEdits.Should().ContainSingle().Subject;
        usage.IsLinked.Should().BeTrue();
        usage.LinkedFromText.Should().Be("Chocolate Glaze, from Sub-Assemblies", "a standalone one keeps its name, which its tab doesn't show");
        usage.RoleName.Should().Be("Glaze");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(0.45m, "5 g of a 100 g, ₱9 batch");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var saved = await dbContext.ProductSubAssemblyUsages.SingleAsync(Cancellation);
        saved.ProductId.Should().Be(productId);
        saved.SubAssemblyId.Should().Be(glazeId);
        saved.AuthoredValue.Should().Be(5m);
    });

    [Test]
    public Task The_header_title_follows_the_name_as_it_is_typed() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.HeaderTitle.Should().Be("New product");

        state.EditingName = "Cheese roll";
        state.HeaderTitle.Should().Be("Cheese roll", "the header follows the name as it's typed");
    });

    [Test]
    public Task Save_puts_the_product_in_the_picked_category_which_shows_again_when_it_is_selected_and_New_clears_it() => UiThread.RunAsync(async () =>
    {
        await AddProductCategoryAsync("Breads");
        var cakesId = await AddProductCategoryAsync("Cakes");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Categories.Select(c => c.Name).Should().Equal("Breads", "Cakes");

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Chocolate Cake";
        state.EditingCategory = state.Categories.Single(c => c.Name == "Cakes");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(Cancellation)).CategoryId.Should().Be(cakesId);
        }

        state.Products.Should().ContainSingle().Which.Category!.Name.Should().Be("Cakes", "the list groups the product under it");

        await state.NewCommand.ExecuteAsync();
        state.EditingCategory.Should().BeNull("a new product starts uncategorized");

        state.Pick(state.Products.Single());
        state.EditingCategory!.Name.Should().Be("Cakes");
    });

    [Test]
    public Task A_product_saved_without_a_category_is_uncategorized() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Pandesal";
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull("a category is optional");
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.SingleAsync(Cancellation)).CategoryId.Should().BeNull();
    });

    [Test]
    public Task The_current_price_shows_the_markup_it_earns_as_it_is_typed_and_Save_keeps_it() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        await SetVatAsync(12m);
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.25m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Costing.EditingLaborPercent = 100m;
        state.Costing.Costs.BaseCost.Should().Be(5m, "₱2.50 of filling and as much again in labor");
        state.Costing.Costs.CurrentMarkupPercent.Should().BeNull("no current price is entered");

        state.EditingCurrentPrice = 7m;
        state.Costing.Costs.CurrentMarkupPercent.Should().Be(25m, "₱7 with 12% VAT is ₱6.25 before it: ₱1.25 on ₱5 of cost");
        state.Costing.Costs.IsCurrentPriceBelowCost.Should().BeFalse();

        state.EditingCurrentPrice = 5m;
        state.Costing.Costs.IsCurrentPriceBelowCost.Should().BeTrue("₱5 with VAT is under ₱5 before it");

        state.EditingCurrentPrice = 7m;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(Cancellation)).CurrentPrice.Should().Be(7m);
        }

        await state.NewCommand.ExecuteAsync();
        state.EditingCurrentPrice.Should().BeNull("a new product has no current price");
        state.Costing.Costs.CurrentMarkupPercent.Should().BeNull();

        state.Pick(state.Products.Single());
        state.EditingCurrentPrice.Should().Be(7m);
    });

    [Test]
    public Task Each_metric_s_value_follows_its_percentage_and_a_value_typed_sets_the_percentage_Save_keeps() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Costing.Costs.SubAssemblies.Should().Be(2m, "10 g of ube at ₱0.20 a gram");
        List<string> changed = [];
        state.Costing.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        state.Costing.EditingLaborPercent = 25m;
        state.Costing.EditingLaborAmount.Should().Be(0.5m, "25% of ₱2 of sub-assemblies");
        changed.Should().Contain(nameof(ProductCosting.EditingLaborAmount), "the value box follows the percentage typed");

        state.Costing.EditingOverheadAmount = 0.3m;
        state.Costing.EditingOverheadPercent.Should().Be(15m, "₱0.30 is 15% of ₱2");
        state.Costing.EditingAdministrativeAmount = 0.2m;
        state.Costing.EditingAdministrativePercent.Should().Be(10m);
        state.Costing.Costs.BaseCost.Should().Be(3m, "₱2 of sub-assemblies, then ₱0.50 + ₱0.30 + ₱0.20");

        state.Costing.EditingMarkupAmount = 0.75m;
        state.Costing.EditingMarkupPercent.Should().Be(25m, "the markup is a percentage of the ₱3 base cost");
        state.Costing.Costs.Markup.Should().Be(0.75m);
        state.History.IsDirty.Should().BeTrue();

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var costing = await dbContext.CostingParameters.SingleAsync(Cancellation);
        (costing.LaborPercent, costing.OverheadPercent, costing.AdministrativePercent, costing.MarkupPercent)
            .Should().Be((25m, 15m, 10m, 25m));
    });

    [Test]
    public Task Reloading_keeps_the_picked_category_and_shows_a_rename_made_in_Settings() => UiThread.RunAsync(async () =>
    {
        var cakesId = await AddProductCategoryAsync("Cakes");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        state.EditingCategory = state.Categories.Single();

        await new ProductCategoryRepository(CreateDbContextFactory()).UpdateAsync(new ProductCategory { Id = cakesId, Name = "Cakes & Tortes" }, Cancellation);
        await state.LoadCommand.ExecuteAsync();

        state.EditingCategory!.Name.Should().Be("Cakes & Tortes", "the unsaved pick points at the refreshed category");
        state.Categories.Select(c => c.Name).Should().Equal("Cakes & Tortes");
    });

    // Nothing awaits this: the picker starts it, from the keyboard or the focus leaving.
    [Test]
    public Task A_prompt_for_a_new_category_that_fails_says_so_and_the_next_one_works() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        var failing = true;
        state.PromptForNewCategory.RegisterHandler(interaction =>
        {
            if (failing)
            {
                throw new InvalidOperationException("The prompt couldn't open.");
            }

            interaction.SetOutput("Pastries");
        });
        await state.LoadCommand.ExecuteAsync();

        state.BeginCategoryEdit();
        state.CategorySearchText = "Pas";
        await state.CommitCategoryTextAsync();

        state.ErrorMessage.Should().Contain("The prompt couldn't open.");

        failing = false;
        await state.CommitCategoryTextAsync();

        state.EditingCategory!.Name.Should().Be("Pastries");
    });

    [Test]
    public Task Typing_a_name_that_isnt_a_category_asks_to_add_it_and_picks_it_for_the_product() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        string? promptedWith = null;
        state.PromptForNewCategory.RegisterHandler(interaction =>
        {
            promptedWith = interaction.Input;
            interaction.SetOutput("Pastries");
        });
        await state.LoadCommand.ExecuteAsync();

        state.BeginCategoryEdit();
        state.CategorySearchText = "Pas";
        await state.CommitCategoryTextAsync();

        promptedWith.Should().Be("Pas", "the prompt starts with what was typed");
        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("Pastries was added.");
        state.EditingCategory!.Name.Should().Be("Pastries");
        state.CategorySearchText.Should().Be("Pastries");
        state.Categories.Select(c => c.Name).Should().Equal("Pastries");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductCategories.SingleAsync(Cancellation)).Name.Should().Be("Pastries");
    });

    [Test]
    public Task Typing_a_listed_categorys_name_picks_it_whatever_its_case_without_asking() => UiThread.RunAsync(async () =>
    {
        await AddProductCategoryAsync("Breads");
        var state = CreateState();
        var prompted = false;
        state.PromptForNewCategory.RegisterHandler(interaction =>
        {
            prompted = true;
            interaction.SetOutput(null);
        });
        await state.LoadCommand.ExecuteAsync();

        state.BeginCategoryEdit();
        state.CategorySearchText = "breads";
        await state.CommitCategoryTextAsync();

        prompted.Should().BeFalse();
        state.EditingCategory!.Name.Should().Be("Breads");
    });

    [Test]
    public Task Cancelling_the_new_category_prompt_puts_back_the_category_the_picker_had() => UiThread.RunAsync(async () =>
    {
        await AddProductCategoryAsync("Breads");
        var state = CreateState();
        var prompted = false;
        state.PromptForNewCategory.RegisterHandler(interaction =>
        {
            prompted = true;
            interaction.SetOutput(null);
        });
        await state.LoadCommand.ExecuteAsync();
        state.EditingCategory = state.Categories.Single();

        // A name the picker doesn't list can clear its pick as it's typed (CategoryPicker.Begin).
        state.BeginCategoryEdit();
        state.CategorySearchText = "Cakes";
        state.EditingCategory = null;
        await state.CommitCategoryTextAsync();

        prompted.Should().BeTrue();
        state.EditingCategory!.Name.Should().Be("Breads");
        state.CategorySearchText.Should().Be("Breads");
        state.Categories.Should().ContainSingle("nothing was added");
    });

    [Test]
    public Task The_lists_row_and_the_picked_product_follow_each_other_and_a_folder_picked_leaves_the_product() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductInAsync("Mamon", breadsId);
        var ensaymadaId = await SaveProductInAsync("Ensaymada", breadsId);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        var tree = state.View.List.ProductTree;
        var breads = tree.Folder(breadsId)!;
        breads.Children.Should().Equal(tree.Find(ensaymadaId)!, tree.Find(mamonId)!);

        state.View.List.SelectedTreeNode = tree.Find(mamonId);
        state.SelectedProduct!.Id.Should().Be(mamonId, "picking a product's row picks the product");
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");

        state.View.List.SelectedTreeNode = breads;
        state.SelectedProduct!.Id.Should().Be(mamonId, "a folder picked leaves the product showing");

        state.OpenRecord(ensaymadaId);
        state.View.List.SelectedTreeNode.Should().BeSameAs(tree.Find(ensaymadaId), "a product opened from elsewhere takes the row with it");
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");

        state.View.List.SearchText = "mam";
        tree.Find(ensaymadaId).Should().BeNull();
        state.View.List.SelectedTreeNode.Should().BeNull("the search hides the product picked");
        state.View.List.SearchText = string.Empty;
        state.View.List.SelectedTreeNode.Should().BeSameAs(tree.Find(ensaymadaId), "its row comes back with it");

        await state.NewCommand.ExecuteAsync();
        state.View.List.SelectedTreeNode.Should().BeSameAs(tree.PendingProduct, "New picks the new product's row, its name to be typed");
        state.View.List.CancelTreeName();
        state.View.List.SelectedTreeNode.Should().BeNull("Esc takes the row away, and the blank form has none");
    });

    [Test]
    public Task Dragging_the_product_on_the_form_to_another_folder_saves_the_move_and_leaves_nothing_else_to_save() => UiThread.RunAsync(async () =>
    {
        var breadsCategoryId = await AddProductCategoryAsync("Breads");
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var mamonId = await SaveProductInAsync("Mamon", breadsId, breadsCategoryId);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.OpenRecord(mamonId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");
        TypeName(state, "Mamon Special");

        var move = ProductTree.MoveTo(state.View.List.ProductTree.Folder(cakesId), state.SelectedProduct!);
        await state.View.List.MoveProductsCommand.ExecuteAsync([move!]);

        state.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.Products.SingleAsync(Cancellation);
            saved.Stage.Should().Be(ProductStage.Production);
            saved.FolderId.Should().Be(cakesId);
            saved.CategoryId.Should().Be(breadsCategoryId, "a folder isn't a category");
            saved.Name.Should().Be("Mamon", "the move saves nothing typed into the form");
        }

        var leaf = state.View.List.ProductTree.Find(mamonId);
        state.View.List.ProductTree.Folder(cakesId)!.Children.Should().ContainSingle().Which.Should().BeSameAs(leaf);
        state.View.List.ProductTree.Folder(breadsId)!.Children.Should().BeEmpty();
        state.View.List.SelectedTreeNode.Should().BeSameAs(leaf);
        state.SelectedProduct!.Stage.Should().Be(ProductStage.Production, "the selection is the product as it's saved now");
        state.EditingCategory!.Name.Should().Be("Breads", "the Category picker stays as it was");
        state.EditingName.Should().Be("Mamon Special", "what's typed into the form stays");
        state.History.IsDirty.Should().BeTrue("the new name is still to be saved");

        state.TryUndo().Should().BeTrue();
        state.EditingName.Should().Be("Mamon");
        state.History.IsDirty.Should().BeFalse("a move leaves nothing on the form to save");
        state.SaveCommand.CanExecute().Should().BeFalse();
    });

    [Test]
    public Task Dragging_another_product_leaves_the_form_as_it_is() => UiThread.RunAsync(async () =>
    {
        var breadsCategoryId = await AddProductCategoryAsync("Breads");
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductInAsync("Mamon", breadsId, breadsCategoryId);
        var ensaymadaId = await SaveProductInAsync("Ensaymada", breadsId, breadsCategoryId);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.OpenRecord(mamonId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");
        TypeName(state, "Mamon Special");

        var ensaymada = state.Products.Single(p => p.Id == ensaymadaId);
        var production = state.View.List.ProductTree.StageFolder(ProductStage.Production);
        var move = ProductTree.MoveTo(production, ensaymada);
        await state.View.List.MoveProductsCommand.ExecuteAsync([move!]);

        state.ErrorMessage.Should().BeNull();
        production.Children.Should().ContainSingle()
            .Which.Should().BeSameAs(state.View.List.ProductTree.Find(ensaymadaId), "dropped on the stage's folder, it goes to the top of the stage");
        state.SelectedProduct!.Id.Should().Be(mamonId);
        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.ProductTree.Find(mamonId));
        state.EditingName.Should().Be("Mamon Special");
        state.EditingCategory!.Name.Should().Be("Breads");
        state.History.CanUndo.Should().BeTrue();
        state.History.IsDirty.Should().BeTrue();
    });

    [Test]
    public Task Create_Variant_makes_a_product_that_links_the_sources_own_with_no_packaging_and_shows_it() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough", isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (sourceId, doughId) = await AddProductWithSubAssemblyAsync("Ensaymada Big", roleId, [(flourId, 1000m)], weightPerPiece: 60m);
        await GivePackagingAsync(sourceId);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 1,
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");

        state.EditingCloneName = "Ensaymada Small";
        await state.CreateVariantCommand.ExecuteAsync();

        // The variant opens in a tab of its own, kept, beside the source's.
        state.ErrorMessage.Should().BeNull();
        var variant = state.View.Form!;
        variant.Should().NotBeSameAs(state);
        state.View.Tabs.Active!.IsPreview.Should().BeFalse();
        variant.SelectedProduct!.Name.Should().Be("Ensaymada Small");
        var variantId = variant.SelectedProduct.Id;
        await Settle.UntilAsync(variant,
            () => variant.AreDetailsReady && variant.Recipe.UsageEdits.Count == 1,
            () => $"ready={variant.AreDetailsReady}, usages={variant.Recipe.UsageEdits.Count}");
        var usage = variant.Recipe.UsageEdits.Single();
        usage.IsLinked.Should().BeTrue();
        usage.SubAssemblyId.Should().Be(doughId);
        usage.LinkedOwner.Should().Be((sourceId, "Ensaymada Big"));
        usage.LinkedFromText.Should().Be("Ensaymada Big Dough, from Ensaymada Big");
        variant.Packaging.IsPackagingLinked.Should().BeFalse();
        variant.Packaging.LineEdits.Should().BeEmpty("a variant's packaging is neither linked nor copied");

        // Its Save keeps the link to the source's own, which no other product could link.
        variant.EditingCurrentPrice = 20m;
        await variant.SaveCommand.ExecuteAsync();
        variant.ErrorMessage.Should().BeNull();
        (await new ProductSubAssemblyUsageRepository(CreateDbContextFactory()).GetForProductAsync(variantId, Cancellation))
            .Should().ContainSingle().Which.SubAssemblyId.Should().Be(doughId);

        // The badge opens the source, where the dough is kept: its own tab.
        await variant.Recipe.OpenLinkedSourceCommand.ExecuteAsync(usage);
        state.View.Form.Should().BeSameAs(state);
        state.SelectedProduct!.Id.Should().Be(sourceId);
    });

    [Test]
    public Task A_new_product_starts_with_the_costing_defaults_and_its_first_Save_keeps_them() => UiThread.RunAsync(async () =>
    {
        await new CostingDefaultsRepository(CreateDbContextFactory()).SetAsync(
            new CostingDefaults { LaborPercent = 25m, OverheadPercent = 10m, AdministrativePercent = 5m, MarkupPercent = 40m, VatPercent = 12m, PriceRoundingStep = 0.25m },
            Cancellation);
        var state = CreateState();

        await state.LoadCommand.ExecuteAsync();

        (state.Costing.EditingLaborPercent, state.Costing.EditingOverheadPercent, state.Costing.EditingAdministrativePercent, state.Costing.EditingMarkupPercent)
            .Should().Be((25m, 10m, 5m, 40m), "the screen opens on a new product");
        state.Costing.Costs.VatPercent.Should().Be(12m, "every product is priced with the shop's VAT");
        state.Costing.Costs.PriceRoundingStep.Should().Be(0.25m, "and its price rounded up as the shop rounds");

        state.EditingName = "Pandesal";
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var costing = await dbContext.CostingParameters.SingleAsync(c => c.Product!.Name == "Pandesal", Cancellation);
        (costing.LaborPercent, costing.MarkupPercent).Should().Be((25m, 40m));
    });

    [Test]
    public Task Changed_defaults_reach_an_untouched_new_product_but_not_a_typed_one_or_a_saved_product() => UiThread.RunAsync(async () =>
    {
        var defaultsRepository = new CostingDefaultsRepository(CreateDbContextFactory());
        var productId = await AddProductAsync("Cheese roll");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        // As Settings would, while the screen shows a new product: it takes them up on the next load.
        await defaultsRepository.SetAsync(new CostingDefaults { LaborPercent = 20m, VatPercent = 12m }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.Costing.EditingLaborPercent.Should().Be(20m);

        // Typed into, it keeps what was typed.
        state.Costing.EditingLaborPercent = 7m;
        await defaultsRepository.SetAsync(new CostingDefaults { LaborPercent = 30m, VatPercent = 12m }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.Costing.EditingLaborPercent.Should().Be(7m);

        // A saved product without costing of its own shows none: the defaults are only where a new product starts.
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");
        state.Costing.EditingLaborPercent.Should().Be(0m);

        // New starts from the latest defaults.
        await state.NewCommand.ExecuteAsync();
        (state.Costing.EditingLaborPercent, state.Costing.Costs.VatPercent).Should().Be((30m, 12m));
    });

    private async Task<int> AddProductCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = new ProductCategory { Name = name };
        dbContext.ProductCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    [Test]
    public Task A_name_with_spaces_round_it_is_saved_and_shown_without_them() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        TypeName(state, " Pandesal ");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.EditingName == "Pandesal", () => $"name=[{state.EditingName}]");
        state.History.IsDirty.Should().BeFalse("what's showing is what was saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.SingleAsync(Cancellation)).Name.Should().Be("Pandesal");
    });

    [Test]
    public Task A_name_taken_but_for_a_space_at_the_end_is_refused_and_not_saved_beside_it() => UiThread.RunAsync(async () =>
    {
        await SaveProductAsync("Pandesal", null);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        await state.NewCommand.ExecuteAsync();
        TypeName(state, "Pandesal ");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be(Failure.DuplicateNameText);
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.CountAsync(Cancellation)).Should().Be(1);
    });

    /// <summary>A product with nothing but its name and category, saved through the repository, which files it in Innovation.</summary>
    /// <summary>A folder of the Products list, at the top of <paramref name="stage"/> (Innovation, as a product saved here is).</summary>
    private async Task<int> AddFolderAsync(string name, ProductStage stage = ProductStage.Innovation)
    {
        var folder = new ProductFolder { Name = name, Stage = stage };
        await new ProductFolderRepository(CreateDbContextFactory()).AddAsync(folder, Cancellation);
        return folder.Id;
    }

    private Task<int> SaveProductInAsync(string name, int folderId, int? categoryId = null) =>
        new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = name, FolderId = folderId, CategoryId = categoryId }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    private Task<int> SaveProductAsync(string name, int? categoryId) =>
        new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = name, CategoryId = categoryId }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    /// <summary>A name typed into the header and left, as one undo step, the way the name box records it.</summary>
    private static void TypeName(ProductForm state, string name)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.EditingName = name;
        recorder.EndUndoStep();
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

    private ProductForm CreateState(IViewStateStore? viewStateStore = null)
    {
        var factory = CreateDbContextFactory();
        return ProductsViews.Create(factory, CreateUnitOfWork(), viewStateStore).Form!;
    }

    [Test]
    public Task Leaving_a_sub_assemblys_tab_applies_its_form_and_a_refused_one_keeps_the_tab() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 2, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // Their saved lines are edited on the Sub-Assemblies screen; Make a Copy gives the product sections to type into.
        foreach (var saved in state.Recipe.UsageEdits.ToList())
        {
            await state.Recipe.MakeCopyCommand.ExecuteAsync(saved);
        }

        var dough = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Dough");
        var filling = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => t.Header == "Filling");

        // Typed into the Dough's form, then away to the Filling's tab: the typing is applied on the way out.
        state.Recipe.PickRecipeTab(dough);
        state.UsageForm.EditingUsage.Should().BeSameAs(dough.Usage, "showing a sub-assembly's tab opens its form");
        state.UsageForm.FormLines.Single().Grams = 1500m;
        state.Recipe.PickRecipeTab(filling);

        state.ErrorMessage.Should().BeNull();
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(filling);
        state.UsageForm.EditingUsage.Should().BeSameAs(filling.Usage);
        dough.Usage.TotalWeight.Should().Be(1500m, "leaving its tab applied what was typed");

        // A form a rule refuses keeps its own tab, with the message shown: here, the other one's role, where neither has a name.
        state.Recipe.PickRecipeTab(dough);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == fillingRoleId));
        state.Recipe.PickRecipeTab(filling);

        state.Recipe.SelectedRecipeTab.Should().BeSameAs(dough, "the form has to be put right before another tab can show");
        state.ErrorMessage.Should().StartWith("This recipe has more than one Filling without a name");
        state.UsageForm.EditingUsage.Should().BeSameAs(dough.Usage);

        // The form has no discard of its own: it's put right where it stands, and then the tab can be left.
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == doughRoleId));
        state.UsageForm.EditingUsageRole!.Name.Should().Be("Dough");
        state.Recipe.PickRecipeTab(filling);
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(filling);
        state.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task Merely_showing_a_sub_assemblys_tab_leaves_the_recipe_as_it_was() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.24m);
        await AddProductWithSubAssemblyAsync("Ube pandesal", roleId, [(ubeId, 100m)], weightPerPiece: 10m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        var tab = state.Recipe.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single();
        var usage = tab.Usage;

        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);
        state.Recipe.PickRecipeTab(tab);
        state.Recipe.PickRecipeTab(state.Recipe.PackagingTab);

        state.ErrorMessage.Should().BeNull();
        tab.Usage.Should().BeSameAs(usage, "a form nothing was typed into is applied to nothing");
        state.UsageForm.IsUsageFormOpen.Should().BeFalse("Packaging has no sub-assembly form");
    });

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

    /// <summary>
    /// Links the dough into the product <paramref name="state"/> shows, with <paramref name="addInGrams"/> of ube and
    /// <paramref name="weightPerPiece"/> a piece, and saves it.
    /// </summary>
    private static async Task LinkWithUbeAsync(ProductForm state, int ubeId, decimal addInGrams, decimal weightPerPiece)
    {
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.UsageForm.PickSource(state.UsageForm.LinkableSubAssemblies.Single());
        await Settle.UntilAsync(state, () => state.UsageForm.FormLines.Count == 1, () => $"form lines={state.UsageForm.FormLines.Count}");
        state.UsageForm.FormAddIns.Add(new IngredientLineRow(state.Ingredients.Single(m => m.Id == ubeId), addInGrams));
        state.UsageForm.TypeWeightPerPiece(weightPerPiece);
        await state.SaveCommand.ExecuteAsync();
    }

    [Test]
    public Task Add_ins_go_into_a_linked_usages_batch_and_its_cost_per_piece() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var (linkerId, _, ubeId, _) = await SeedLinkableDoughAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == linkerId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == linkerId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");

        await LinkWithUbeAsync(state, ubeId, addInGrams: 20m, weightPerPiece: 30m);

        state.ErrorMessage.Should().BeNull();
        var usage = state.Recipe.UsageEdits.Should().ContainSingle().Subject;
        usage.IsLinked.Should().BeTrue();
        usage.BaseTotalWeight.Should().Be(1000m, "the dough itself is still the dough");
        usage.TotalWeight.Should().Be(1020m, "the 20 g of ube goes into the batch");
        usage.TotalCostAt(CostBasis.Buffered).Should().Be(66m, "1000*0.05 + 20*0.80");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(66m * 30m / 1020m);

        // The form is showing it, and its two tables add up to the batch the piece is cut from.
        state.UsageForm.IsFormShowingAddIns.Should().BeTrue();
        state.UsageForm.UsageFormTotalWeightText.Should().Be("1,020 g");
        state.UsageForm.FormAddIns.Single().Grams.Should().Be(20m);

        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var saved = await dbContext.UsageAddInLines.AsNoTracking().Where(l => l.Usage!.ProductId == linkerId).ToListAsync(Cancellation);
        saved.Select(l => (l.IngredientId, l.Grams)).Should().Equal((ubeId, 20m));
    });

    [Test]
    public Task A_linked_recipes_add_ins_table_always_shows_and_its_badge_counts_the_add_ins() => UiThread.RunAsync(async () =>
    {
        var (linkerId, _, ubeId, flourId) = await SeedLinkableDoughAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == linkerId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == linkerId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");

        // A picked recipe to link: its add-ins table shows under it, empty, and the badge says so.
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.Link);
        state.UsageForm.PickSource(state.UsageForm.LinkableSubAssemblies.Single());
        await Settle.UntilAsync(state, () => state.UsageForm.FormLines.Count == 1, () => $"form lines={state.UsageForm.FormLines.Count}");
        state.UsageForm.IsFormShowingAddIns.Should().BeTrue("the add-ins table shows for every linked recipe, empty or not");
        state.UsageForm.HasFormAddIns.Should().BeFalse();
        state.UsageForm.IsFormShowingNoAddIns.Should().BeTrue();
        state.UsageForm.FormAddInsBadgeText.Should().Be("No add-ins");

        // The add row's untouched line isn't an add-in: only one with an ingredient picked counts.
        state.UsageForm.FormAddIns.Add(state.UsageForm.NewFormAddIn());
        state.UsageForm.HasFormAddIns.Should().BeFalse();
        state.UsageForm.FormAddIns.Clear();

        state.UsageForm.FormAddIns.Add(new IngredientLineRow(state.Ingredients.Single(m => m.Id == ubeId), 20m));
        state.UsageForm.HasFormAddIns.Should().BeTrue();
        state.UsageForm.IsFormShowingNoAddIns.Should().BeFalse();
        state.UsageForm.FormAddInsBadgeText.Should().Be("1 add-in");
        state.UsageForm.UsageFormTotalWeightText.Should().Be("1,020 g");

        state.UsageForm.TypeWeightPerPiece(30m);
        await state.SaveCommand.ExecuteAsync();

        // Its tab reopens with the badge counting them. Save reloads the product, so the form reopens on the
        // reloaded copy rather than the one typed into.
        state.ErrorMessage.Should().BeNull();
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.UsageForm.EditingUsage?.AddIns.Count == 1,
            () => $"ready={state.AreDetailsReady}, add-ins={state.UsageForm.EditingUsage?.AddIns.Count}");
        state.UsageForm.IsFormShowingAddIns.Should().BeTrue();
        state.UsageForm.FormAddInsBadgeText.Should().Be("1 add-in");

        // An add-in the linked recipe already has: refused, and the badge counts both lines still in the table.
        state.UsageForm.FormAddIns.Add(new IngredientLineRow(state.Ingredients.Single(m => m.Id == flourId), 50m));
        state.UsageForm.FormAddInsBadgeText.Should().Be("2 add-ins");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Contain("Flour");
    });

    [Test]
    public Task Picking_another_product_from_a_sub_assemblys_tab_opens_on_its_first_sub_assembly() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Name == "Cream Loaf"));
        await Settle.UntilAsync(state, () => state.Recipe.SelectedRecipeTab?.Header == "Filling", () => $"tab={state.Recipe.SelectedRecipeTab?.Header}");

        state.Pick(state.Products.Single(p => p.Id == cheeseRollId));
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 2, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Packaging");
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.RecipeTabs[0], "the Dough's tab comes first and shows, not the tab Cream Loaf showed");
    });

    [Test]
    public Task Picking_another_product_keeps_the_recipe_tabs_and_opens_the_form_on_its_first_section() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        state.Pick(state.Products.Single(p => p.Id == ubeBreadId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 2, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Recipe.PickRecipeTab(state.Recipe.RecipeTabs[1]);
        List<RecipeTab> ubeBreadTabs = [.. state.Recipe.RecipeTabs];

        state.Pick(state.Products.Single(p => p.Id == cheeseBreadId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 3, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // The strip keeps its tabs, which show the cheese bread's sections; only its Topping gets a tab of its own.
        state.Recipe.RecipeTabs.Select(t => t.Header).Should().Equal("Dough", "Filling", "Topping", "Packaging");
        state.Recipe.RecipeTabs[0].Should().BeSameAs(ubeBreadTabs[0]);
        state.Recipe.RecipeTabs[1].Should().BeSameAs(ubeBreadTabs[1]);

        // Another product's recipe opens on its first tab, the form on its own section, not the one the tab showed before.
        state.Recipe.SelectedRecipeTab.Should().BeSameAs(state.Recipe.RecipeTabs[0]);
        state.UsageForm.EditingUsage.Should().BeSameAs(state.Recipe.UsageEdits[0]);
        state.UsageForm.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal([("Flour", 800m)]);
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(40m);

        // Back to the ube bread: the Topping's tab goes, and the first two show its sections again.
        state.Pick(state.Products.Single(p => p.Id == ubeBreadId));
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 2, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        state.Recipe.RecipeTabs.Should().Equal(ubeBreadTabs);
        state.UsageForm.FormLines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal([("Flour", 1000m)]);
        state.UsageForm.EditingUsageWeightPerPiece.Should().Be(50m);
    });

    [Test]
    public Task An_add_in_already_in_the_linked_recipe_is_refused_on_the_spot() => UiThread.RunAsync(async () =>
    {
        var (linkerId, _, _, flourId) = await SeedLinkableDoughAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == linkerId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == linkerId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");

        await LinkWithUbeAsync(state, flourId, addInGrams: 100m, weightPerPiece: 30m);

        state.ErrorMessage.Should().NotBeNull();
        state.ErrorMessage.Should().Contain("Flour").And.Contain("Cream Loaf Dough");
        state.Recipe.UsageEdits.Should().BeEmpty("nothing goes into the recipe until the form is put right");
    });

    [Test]
    public Task Make_a_Copy_folds_the_add_ins_into_the_new_sections_lines() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var (linkerId, _, ubeId, flourId) = await SeedLinkableDoughAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == linkerId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == linkerId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");
        await LinkWithUbeAsync(state, ubeId, addInGrams: 20m, weightPerPiece: 30m);

        await state.Recipe.MakeCopyCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());

        var copy = state.Recipe.UsageEdits.Should().ContainSingle().Subject;
        copy.IsNew.Should().BeTrue();
        copy.AddIns.Should().BeEmpty("a new section has no add-ins; its lines are its own");
        copy.Lines.Select(l => (l.Ingredient.Id, l.Grams)).Should().Equal([(flourId, 1000m), (ubeId, 20m)]);
        copy.TotalWeight.Should().Be(1020m, "the batch it was showing is the batch it keeps");
        copy.CostPerPieceAt(CostBasis.Buffered).Should().Be(66m * 30m / 1020m);
    });

    [Test]
    public Task Saving_twice_with_no_change_leaves_a_linked_usages_add_ins_alone() => UiThread.RunAsync(async () =>
    {
        var (linkerId, _, ubeId, _) = await SeedLinkableDoughAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == linkerId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == linkerId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");
        await LinkWithUbeAsync(state, ubeId, addInGrams: 20m, weightPerPiece: 30m);
        await state.SaveCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        int AddInIdAsync()
        {
            using var dbContext = CreateDbContext();
            return dbContext.UsageAddInLines.Single(l => l.Usage!.ProductId == linkerId).Id;
        }

        var before = AddInIdAsync();

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        AddInIdAsync().Should().Be(before, "a recipe that didn't change isn't rewritten — the draft is compared field by field, not by record equality");
    });

    [Test]
    public Task A_repriced_add_in_ingredient_reaches_the_usage_on_a_refresh() => UiThread.RunAsync(async () =>
    {
        await WithoutWastageAsync();
        var (linkerId, _, ubeId, _) = await SeedLinkableDoughAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == linkerId));
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == linkerId && state.AreDetailsReady, () => $"selected={state.SelectedProduct?.Id}, ready={state.AreDetailsReady}");
        await LinkWithUbeAsync(state, ubeId, addInGrams: 20m, weightPerPiece: 30m);
        await state.SaveCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");
        state.Recipe.UsageEdits.Single().TotalCostAt(CostBasis.Buffered).Should().Be(66m);

        // The ube doubles on the Raw Materials screen; coming back to Products re-reads it.
        await using (var dbContext = CreateDbContext())
        {
            var ube = await dbContext.RawMaterials.SingleAsync(m => m.Id == ubeId, Cancellation);
            ube.CostPerBaseUnit = 1.6m;
            ube.BufferedCostPerBaseUnit = 1.6m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await state.LoadCommand.ExecuteAsync();

        var usage = state.Recipe.UsageEdits.Single();
        usage.TotalCostAt(CostBasis.Buffered).Should().Be(82m, "1000*0.05 + 20*1.60 — the add-in is repriced like any other line");
        usage.CostPerPieceAt(CostBasis.Buffered).Should().Be(82m * 30m / 1020m);
        state.UsageForm.FormAddIns.Single().Cost.Should().Be(32m, "the form's own table is repriced in place, not refilled");
    });

    [Test]
    public Task Ticking_a_line_as_the_base_is_saved_and_reads_as_a_hundred_percent() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        var productId = await AddProductAsync("Pandesal");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(roleId, [(flourId, 1000m), (waterId, 650m)], UsageAuthoredField.WeightPerPiece, 50m)],
            []));
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // Its saved lines are edited on the Sub-Assemblies screen; Make a Copy gives the product a section to type into.
        await state.Recipe.MakeCopyCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());
        state.UsageForm.IsFormEditingLines.Should().BeTrue();

        // Nothing ticked yet, so the lines read as absolute percentages: their shares of the 1,650 g batch.
        state.UsageForm.UsageFormPercentHeader.Should().Be("Absolute %");
        state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(1000m / 1650m * 100m);
        state.UsageForm.FormLines.Sum(l => l.Percent).Should().BeApproximately(100m, 0.0001m);
        state.UsageForm.FormLines.Should().OnlyContain(l => !l.CanTypeBakersPercent, "an absolute percentage isn't typed");

        state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == flourId).IsFormulaBase = true;

        state.UsageForm.UsageFormPercentHeader.Should().Be("Baker's %");

        state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(100m);
        state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == waterId).Percent.Should().Be(65m, "650 g of water on 1,000 g of flour");
        state.UsageForm.UsageFormBaseCaption.Should().Contain("1,000 g").And.Contain("165");

        // This is the trap: without CurrentRecipe() carrying the tick, Save treats the recipe as unchanged.
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.AsNoTracking()
            .Where(l => dbContext.ProductSubAssemblyUsages.Any(u => u.ProductId == productId && u.SubAssemblyId == l.SubAssemblyId))
            .ToListAsync(Cancellation);
        lines.Single(l => l.IngredientId == flourId).IsFormulaBase.Should().BeTrue();
        lines.Single(l => l.IngredientId == waterId).IsFormulaBase.Should().BeFalse();
    });

    [Test]
    public Task Make_a_Copy_keeps_the_linked_recipes_base() => UiThread.RunAsync(async () =>
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

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == linkerId));
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // The link shows the owner's base, read-only, and the percentages that follow from it.
        state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(100m);
        state.UsageForm.IsFormEditingLines.Should().BeFalse("a linked recipe's base is the Sub-Assemblies screen's to set");

        await state.Recipe.MakeCopyCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());

        var copy = state.Recipe.UsageEdits.Should().ContainSingle().Subject;
        copy.IsNew.Should().BeTrue();
        copy.Lines.Single(l => l.Ingredient.Id == flourId).IsFormulaBase.Should().BeTrue("the copy is the same formula");
        copy.Lines.Single(l => l.Ingredient.Id == waterId).IsFormulaBase.Should().BeFalse();
    });

    [Test]
    public Task Add_ins_take_their_percentage_from_the_linked_recipes_base() => UiThread.RunAsync(async () =>
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

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == linkerId));
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // 20 g of ube on the recipe's 1,000 g of flour is 2% — not 100% of its own 20 g.
        state.UsageForm.FormAddIns.Single().Percent.Should().Be(2m);
    });

    [Test]
    public Task With_no_base_a_linked_recipe_and_its_add_ins_read_as_shares_of_the_whole_batch() => UiThread.RunAsync(async () =>
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

        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == linkerId));
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // The batch is the recipe's 900 g and the 100 g add-in: 90% and 10%, together 100%.
        state.UsageForm.UsageFormPercentHeader.Should().Be("Absolute %");
        state.UsageForm.UsageFormBaseCaption.Should().StartWith("Absolute %");
        state.UsageForm.FormLines.Single().Percent.Should().Be(90m);
        state.UsageForm.FormAddIns.Single().Percent.Should().Be(10m);
    });

    [Test]
    public Task A_typed_percentage_is_saved_and_follows_the_flour_while_typed_grams_stay_put() => UiThread.RunAsync(async () =>
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
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single());
        await Settle.UntilAsync(state, () => state.Recipe.UsageEdits.Count == 1, () => $"usage rows={state.Recipe.UsageEdits.Count}");

        // Its saved lines are edited on the Sub-Assemblies screen; Make a Copy gives the product a section to type into.
        await state.Recipe.MakeCopyCommand.ExecuteAsync(state.Recipe.UsageEdits.Single());

        var water = state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == waterId);
        water.CanTypeBakersPercent.Should().BeTrue();
        water.Percent = 65m;
        water.Grams.Should().Be(650m);

        // The flour doubles: the typed 65% follows it, the typed 100 g of eggs doesn't.
        state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == flourId).Grams = 2000m;

        state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == waterId).Grams.Should().Be(1300m);
        state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == waterId).IsPercentTyped.Should().BeTrue();
        state.UsageForm.FormLines.Single(l => l.Ingredient!.Id == eggsId).Grams.Should().Be(100m);

        // The trap: Save only writes a recipe it sees as changed, so the typed percentage has to reach the draft.
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();

        await using var dbContext = CreateDbContext();
        var lines = await dbContext.SubAssemblyLines.AsNoTracking()
            .Where(l => dbContext.ProductSubAssemblyUsages.Any(u => u.ProductId == productId && u.SubAssemblyId == l.SubAssemblyId))
            .ToListAsync(Cancellation);
        lines.Single(l => l.IngredientId == waterId).Grams.Should().Be(1300m);
        lines.Single(l => l.IngredientId == waterId).AuthoredBakersPercent.Should().Be(65m);
        lines.Single(l => l.IngredientId == eggsId).Grams.Should().Be(100m);
        lines.Single(l => l.IngredientId == eggsId).AuthoredBakersPercent.Should().BeNull();
    });

    [Test]
    public Task The_forms_version_goes_up_as_another_product_or_a_new_one_takes_the_form() => UiThread.RunAsync(async () =>
    {
        var creamLoafId = await AddProductAsync("Cream Loaf");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.OpenRecord(ensaymadaId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");
        var ensaymada = state.FormVersion;

        // What the boxes hold is still the product's: they keep the cursor.
        state.EditingName = "Ensaymada, large";
        await state.LoadCommand.ExecuteAsync();
        state.FormVersion.Should().Be(ensaymada, "typing and a reload leave the form the same product's");

        state.OpenRecord(creamLoafId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");
        var creamLoaf = state.FormVersion;
        creamLoaf.Should().BeGreaterThan(ensaymada);

        await state.NewCommand.ExecuteAsync();
        var fresh = state.FormVersion;
        fresh.Should().BeGreaterThan(creamLoaf);

        await state.NewCommand.ExecuteAsync();
        var freshAgain = state.FormVersion;
        freshAgain.Should().BeGreaterThan(fresh, "a new one in a new one's place empties the boxes too");

        // New, then the browser's Back: the Product Name box had the cursor, and stayed blank over the product's name.
        state.OpenRecord(ensaymadaId);
        state.FormVersion.Should().BeGreaterThan(freshAgain);
    });

    [Test]
    public Task The_product_in_the_screens_address_is_opened_once_the_load_is_back() => UiThread.RunAsync(async () =>
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var state = CreateState();

        // As the page does when it opens on /products/42, before the load it then runs.
        state.OpenRecordOnLoad(ensaymadaId);
        state.SelectedProduct.Should().BeNull("the list is empty until the screen loads");

        await state.LoadCommand.ExecuteAsync();

        state.SelectedProduct!.Id.Should().Be(ensaymadaId);
        state.EditingName.Should().Be("Ensaymada");
    });

    [Test]
    public Task New_clicked_while_the_screen_is_still_opening_the_product_in_its_address_keeps_the_new_form_and_Save_adds_it() => UiThread.RunAsync(async () =>
    {
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var state = CreateState();
        state.OpenRecordOnLoad(ensaymadaId);
        var loading = state.LoadCommand.ExecuteAsync();

        // The load is still reading. Ensaymada opened over the new form at its end, and the name typed was saved as its.
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Mamon";
        await loading;

        state.SelectedProduct.Should().BeNull("New was the later word");
        state.EditingName.Should().Be("Mamon");

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.OrderBy(p => p.Name).Select(p => p.Name).ToListAsync(Cancellation)).Should().Equal("Ensaymada", "Mamon");
    });

    [Test]
    public Task New_drops_a_new_product_waiting_to_start_from_a_sub_assembly() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var doughId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Pandesal Dough", RoleId = roleId }, [(flourId, 1000m)], Cancellation);
        var state = CreateState();

        // As Sub-Assemblies' New Product does, and then New before the load that showing the screen runs is back.
        state.View.StartNewProductFrom(doughId);
        var loading = state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        state.EditingName = "Mamon";
        await loading;

        state.EditingName.Should().Be("Mamon", "the product named after the sub-assembly would have replaced it");
        state.UsageForm.IsUsageFormOpen.Should().BeFalse();
    });

    [Test]
    public Task The_largest_number_a_box_takes_is_still_costed_in_a_recipe_line_and_the_piece_size() => UiThread.RunAsync(async () =>
    {
        var roleId = await AddRoleAsync("Dough");
        var saffronId = await AddIngredientAsync("Saffron", costPerGram: 500m);
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await state.NewCommand.ExecuteAsync();
        await state.Recipe.AddUsageCommand.ExecuteAsync(UsageFormMode.New);
        state.UsageForm.PickRole(state.Roles.Single(r => r.Id == roleId));

        // NumberField.Largest: the most a held key leaves in a box. Longer numbers (21 digits) took the cost per piece
        // past what a decimal holds, from the lines' own change subscription, which nothing catches.
        const decimal largest = 999_999_999.9999m;
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = saffronId, Grams = largest });
        state.UsageForm.TypeYield(24m);
        state.UsageForm.FormLines.Single().Grams = 1m;
        state.UsageForm.FormLines.Single().Grams = largest;
        state.UsageForm.TypeWeightPerPiece(largest);
        state.EditingQuantityRequired = (int)largest;

        state.ErrorMessage.Should().BeNull();
        state.UsageForm.EditingUsageYield.Should().Be(1m, "the whole batch in one piece: the lines are still followed");
    });
}
