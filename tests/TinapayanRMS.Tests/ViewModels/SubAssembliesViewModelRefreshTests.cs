using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.ViewModels;

/// <summary>
/// The Sub-Assemblies screen, on real repositories: one Save writes a standalone sub-assembly's name, role
/// and lines; coming back to the tab (LoadCommand) keeps unsaved lines and reprices them; Delete is refused
/// while a product links it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SubAssembliesViewModelRefreshTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task One_Save_writes_a_new_standalone_sub_assembly_and_selects_it()
    {
        await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.HeaderTitle.Should().Be("New sub-assembly");
        viewModel.EditingRole!.Name.Should().Be("Glaze", "a new form takes the first role");
        viewModel.EditingName = "Chocolate Glaze";
        viewModel.Lines.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 200m });
        viewModel.BatchWeight.Should().Be(200m);
        viewModel.BatchCost.Should().Be(18m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Chocolate Glaze was saved.");
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.SubAssemblies.SingleAsync(Cancellation);
            saved.Name.Should().Be("Chocolate Glaze");
            saved.OwnerProductId.Should().BeNull();
            saved.TotalCost.Should().Be(18m);
        }

        var row = viewModel.Rows.Should().ContainSingle().Subject;
        viewModel.SelectedRow.Should().BeSameAs(row);
        row.TotalWeight.Should().Be(200m);
        viewModel.HeaderTitle.Should().Be("Chocolate Glaze");
        viewModel.Lines.Should().ContainSingle().Which.Grams.Should().Be(200m);
    }

    [Fact]
    public async Task Saving_without_lines_is_refused_with_a_message()
    {
        await AddRoleAsync("Glaze");
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        viewModel.EditingName = "Chocolate Glaze";
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("Add the ingredients that go into Chocolate Glaze, in grams.");
        viewModel.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Reloading_keeps_unsaved_lines_and_reprices_them()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single();
        await WaitUntilSettledAsync(() => viewModel.Lines.Count == 1, () => $"lines={viewModel.Lines.Count}");

        viewModel.Lines.Single().Grams = 300m;
        await using (var dbContext = CreateDbContext())
        {
            var sugar = await dbContext.RawMaterials.SingleAsync(m => m.Id == sugarId, Cancellation);
            sugar.CostPerBaseUnit = 0.10m;
            sugar.BufferedCostPerBaseUnit = 0.10m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await viewModel.LoadCommand.Execute();

        viewModel.Lines.Should().ContainSingle().Which.Grams.Should().Be(300m, "an unsaved edit survives the refresh");
        viewModel.BatchCost.Should().Be(30m, "the line is priced at sugar's new cost");
        viewModel.SelectedRow!.Name.Should().Be("Glaze");
    }

    [Fact]
    public async Task Deleting_a_linked_one_is_refused_and_the_form_shows_who_uses_it()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Glaze", RoleId = roleId }, [(sugarId, 100m)], Cancellation);
        await SaveRecipeAsync(await AddProductAsync("Cream Loaf"), new RecipeDraft([], [new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m)]));
        var viewModel = CreateViewModel();
        viewModel.ConfirmDelete.RegisterHandler(context => context.SetOutput(true));
        await viewModel.LoadCommand.Execute();

        viewModel.Rows.Single().UsedByText.Should().Be("Cream Loaf");
        viewModel.SelectedRow = viewModel.Rows.Single();
        await WaitUntilSettledAsync(() => viewModel.Lines.Count == 1, () => $"lines={viewModel.Lines.Count}");
        viewModel.UsedByText.Should().StartWith("Used by Cream Loaf.");

        await viewModel.DeleteCommand.Execute();

        viewModel.ErrorMessage.Should().StartWith("Cream Loaf links Glaze.");
        viewModel.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task In_the_packaging_role_the_form_saves_and_reloads_a_packaging_set()
    {
        await AddRoleAsync("Glaze");
        await AddRoleAsync("Packaging", sortOrder: 2, isPackaging: true);
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        int boxId;
        await using (var dbContext = CreateDbContext())
        {
            var box = new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = "Gift Box",
                Category = new RawMaterialCategory { Name = "Boxes" },
                BaseUnitId = pieceId,
                CostPerBaseUnit = 30m,
                BufferedCostPerBaseUnit = 33m,
            };
            dbContext.RawMaterials.Add(box);
            await dbContext.SaveChangesAsync(Cancellation);
            boxId = box.Id;
        }

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.IsPackaging.Should().BeFalse("a new form takes the first role, Glaze");

        viewModel.EditingRole = viewModel.Roles.Single(r => r.IsPackaging);
        viewModel.IsPackaging.Should().BeTrue();
        viewModel.EditingName = "Gift Box Set";
        viewModel.PackagingLines.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 2m });
        viewModel.SetCost.Should().Be(66m, "two boxes at their buffered ₱33");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Gift Box Set was saved.");
        await WaitUntilSettledAsync(() => viewModel.PackagingLines.Count == 1, () => $"packaging lines={viewModel.PackagingLines.Count}");
        viewModel.IsPackaging.Should().BeTrue();
        viewModel.Lines.Should().BeEmpty();
        viewModel.PackagingLines.Single().BaseUnitAbbreviation.Should().Be("pc");
        viewModel.SetCost.Should().Be(66m);
        viewModel.UsedByText.Should().Contain("Add Sub-Assembly");
    }

    private SubAssembliesViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();

        return new SubAssembliesViewModel(
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory, CreateCostPropagator()),
            new SubAssemblyValidator(),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>Selecting a row loads its lines in the background, with nothing to await: waits until <paramref name="condition"/> has held for a moment.</summary>
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

        throw new TimeoutException($"The sub-assembly never finished loading: {describe()}.");
    }

    [Fact]
    public async Task A_typed_percentage_is_saved_and_reopens_as_typed()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = roleId },
            [new RecipeLine(flourId, 1000m, IsFormulaBase: true), (waterId, 600m)],
            Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single();
        await WaitUntilSettledAsync(() => viewModel.Lines.Count == 2, () => $"lines={viewModel.Lines.Count}");

        viewModel.PercentHeader.Should().Be("Baker's %");
        viewModel.Lines.Single(l => l.Ingredient!.Id == waterId).Percent = 62.5m;
        viewModel.FormulaBaseCaption.Should().Contain("162.5");
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();

        await WaitUntilSettledAsync(() => viewModel.Lines.Count == 2, () => $"lines={viewModel.Lines.Count}");
        var water = viewModel.Lines.Single(l => l.Ingredient!.Id == waterId);
        water.Grams.Should().Be(625m);
        water.IsPercentTyped.Should().BeTrue("the form reopens with the percentage as the figure that was typed");
    }

    [Fact]
    public async Task Unticking_the_base_switches_the_lines_to_absolute_percentages()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water", costPerGram: 0m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "House Dough", RoleId = roleId },
            [new RecipeLine(flourId, 1500m, IsFormulaBase: true), (waterId, 500m)],
            Cancellation);
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        viewModel.SelectedRow = viewModel.Rows.Single();
        await WaitUntilSettledAsync(() => viewModel.Lines.Count == 2, () => $"lines={viewModel.Lines.Count}");
        viewModel.Lines.Single(l => l.Ingredient!.Id == waterId).Percent.Should().Be(500m / 1500m * 100m);

        viewModel.Lines.Single(l => l.Ingredient!.Id == flourId).IsFormulaBase = false;

        viewModel.PercentHeader.Should().Be("Absolute %");
        viewModel.FormulaBaseCaption.Should().StartWith("Absolute %");
        viewModel.Lines.Single(l => l.Ingredient!.Id == flourId).Percent.Should().Be(75m, "1,500 g of the 2,000 g batch");
        viewModel.Lines.Single(l => l.Ingredient!.Id == waterId).Percent.Should().Be(25m);
    }
}
