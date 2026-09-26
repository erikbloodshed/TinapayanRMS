using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
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
/// The product's packaging on the Products screen, on real repositories: it comes in through Add Sub-Assembly like a
/// recipe section — the Packaging role for a new one, or packaging picked to link or copy (a packaging set, or another
/// product's) — and gets a Packaging tab only then. Make a Copy on a linked tab makes it the product's own; Delete
/// Sub-Assembly takes it out. Save writes the link and the lines together.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductsViewModelPackagingTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task A_new_packaging_is_added_in_the_Packaging_role_and_saved_as_the_products_own_lines()
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Packaging", sortOrder: 6, isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.EditingName = "Ensaymada";

        await viewModel.AddUsageCommand.Execute();
        viewModel.FormRoleOptions.Select(r => r.Name).Should().Equal("Dough", "Packaging");
        viewModel.EditingUsageRole = viewModel.FormRoleOptions.Single(r => r.IsPackaging);
        viewModel.IsFormPackaging.Should().BeTrue();
        viewModel.IsFormEditingPackaging.Should().BeTrue();
        viewModel.FormPackagingLines.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        viewModel.UsageFormCostPerPieceText.Should().Be("30.0000");

        // The form has no Apply of its own: Save puts it in the recipe and shows its tab, as leaving the tab would.
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RecipeTabs.Should().Equal(viewModel.PackagingTab);
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.PackagingTab);
        viewModel.HasPackaging.Should().BeTrue();
        viewModel.IsPackagingLinked.Should().BeFalse();
        viewModel.Costs.Packaging.Should().Be(30m);

        // One packaging to a product: the role isn't offered again. Nothing is typed into this form, so Save drops it.
        await viewModel.AddUsageCommand.Execute();
        viewModel.FormRoleOptions.Select(r => r.Name).Should().Equal("Dough");
        viewModel.LinkableSubAssemblies.Should().NotContain(o => o.IsPackaging);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var line = await dbContext.PackagingLines.SingleAsync(Cancellation);
        (line.PackagingMaterialId, line.QtyPerUnit).Should().Be((boxId, 1m));
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task The_form_shows_under_a_sub_assemblys_tab_or_New_Sub_Assemblys_and_the_packaging_under_its_own()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Packaging", sortOrder: 6, isPackaging: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m);
        var viewModel = await CreateLoadedViewModelAsync();
        (viewModel.IsUsageFormShowing, viewModel.IsPackagingShowing).Should().Be((false, false), "no product is picked, so there are no tabs");

        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.SelectedRecipeTab is SubAssemblyRecipeTab, () => $"tab={viewModel.SelectedRecipeTab?.Header}");
        (viewModel.IsUsageFormShowing, viewModel.IsPackagingShowing).Should().Be((true, false));

        await viewModel.AddUsageCommand.Execute();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.AddSubAssemblyTab);
        (viewModel.IsUsageFormShowing, viewModel.IsPackagingShowing).Should().Be((true, false));

        viewModel.EditingUsageRole = viewModel.FormRoleOptions.Single(r => r.IsPackaging);
        viewModel.FormPackagingLines.Add(new PackagingLineRow { PackagingMaterialId = boxId, QtyPerUnit = 1m });
        await viewModel.SaveCommand.Execute();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.PackagingTab);
        (viewModel.IsUsageFormShowing, viewModel.IsPackagingShowing).Should().Be((false, true));

        viewModel.SelectedRecipeTab = viewModel.RecipeTabs.OfType<SubAssemblyRecipeTab>().Single();
        (viewModel.IsUsageFormShowing, viewModel.IsPackagingShowing).Should().Be((true, false));
    }

    [Fact]
    public async Task Linking_a_packaging_set_shows_its_lines_read_only_and_Save_writes_the_link()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var ribbonId = await AddPackagingAsync("Ribbon", 0.05m, "cm");
        var setId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m), (ribbonId, null, 30m)], Cancellation);
        var productId = await AddProductAsync("Ensaymada");
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Id == productId, () => $"selected={viewModel.SelectedProduct?.Id}");
        viewModel.RecipeTabs.Should().BeEmpty();

        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.LinkableSubAssemblies.Select(o => o.DisplayName).Should().Equal("Gift Box Set — Sub-Assemblies (Packaging)");
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single();
        await WaitUntilSettledAsync(() => viewModel.FormPackagingLines.Count == 2, () => $"lines={viewModel.FormPackagingLines.Count}");
        viewModel.IsFormPackaging.Should().BeTrue();
        viewModel.IsFormEditingPackaging.Should().BeFalse("a link's lines are kept where it's from");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SelectedRecipeTab.Should().BeSameAs(viewModel.PackagingTab);
        viewModel.IsPackagingLinked.Should().BeTrue();
        viewModel.IsPackagingEditable.Should().BeFalse();
        viewModel.PackagingLinkedToText.Should().Be("Linked to Gift Box Set (Sub-Assemblies)");
        viewModel.Costs.Packaging.Should().Be(31.5m, "one set per piece: a ₱30 box and 30 cm of ribbon at ₱0.05");

        await using (var dbContext = CreateDbContext())
        {
            var link = await dbContext.PackagingLinks.SingleAsync(Cancellation);
            (link.ProductId, link.LinkedSubAssemblyId, link.LinkedProductId).Should().Be((productId, setId, null));
            (await dbContext.PackagingLines.CountAsync(Cancellation)).Should().Be(0, "a link has no lines of its own");
        }

        await WaitUntilSettledAsync(() => viewModel.LineEdits.Count == 2, () => $"lines={viewModel.LineEdits.Count}");
        viewModel.IsPackagingLinked.Should().BeTrue("the reload shows the saved link");
        viewModel.RecipeTabs.Should().Equal(viewModel.PackagingTab);
    }

    [Fact]
    public async Task Copying_another_products_packaging_makes_it_this_products_own()
    {
        await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var sourceId = await AddProductAsync("Cream Loaf");
        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(sourceId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 2m)], Cancellation);
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.EditingName = "Cream Loaf Mini";

        await viewModel.AddUsageCommand.Execute(UsageFormMode.Copy);
        viewModel.EditingUsageSource = viewModel.LinkableSubAssemblies.Single(o => o.DisplayName == "Packaging — Cream Loaf");
        await WaitUntilSettledAsync(() => viewModel.FormPackagingLines.Count == 1, () => $"lines={viewModel.FormPackagingLines.Count}");
        viewModel.EditingUsageRole!.IsPackaging.Should().BeTrue("a copy of packaging takes the Packaging role");
        viewModel.IsFormEditingPackaging.Should().BeTrue();
        viewModel.FormPackagingLines.Single().QtyPerUnit = 3m;

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        var copyId = await dbContext.Products.Where(p => p.Name == "Cream Loaf Mini").Select(p => p.Id).SingleAsync(Cancellation);
        (await dbContext.PackagingLines.SingleAsync(l => l.ProductId == copyId, Cancellation)).QtyPerUnit.Should().Be(3m);
        (await dbContext.PackagingLines.SingleAsync(l => l.ProductId == sourceId, Cancellation)).QtyPerUnit.Should().Be(2m, "the source is untouched");
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task Make_a_Copy_on_a_linked_Packaging_tab_unlinks_it_on_Save_and_keeps_its_lines()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var setId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
        var productId = await AddProductAsync("Ensaymada");
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(productId, setId, Cancellation);
        var viewModel = await CreateLoadedViewModelAsync();
        viewModel.SelectedProduct = viewModel.Products.Single();
        await WaitUntilSettledAsync(() => viewModel.LineEdits.Count == 1, () => $"lines={viewModel.LineEdits.Count}");
        viewModel.RecipeTabs.Should().Equal(viewModel.PackagingTab);
        viewModel.IsPackagingLinked.Should().BeTrue();

        await viewModel.CopyPackagingCommand.Execute();

        viewModel.IsPackagingLinked.Should().BeFalse();
        viewModel.IsPackagingEditable.Should().BeTrue();
        viewModel.LineEdits.Should().ContainSingle().Which.QtyPerUnit.Should().Be(1m);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
        var line = await dbContext.PackagingLines.SingleAsync(Cancellation);
        (line.ProductId, line.PackagingMaterialId, line.QtyPerUnit).Should().Be((productId, boxId, 1m),
            "the lines the link showed are written even though they match what was loaded");
    }

    [Fact]
    public async Task Delete_Sub_Assembly_on_the_Packaging_tab_takes_out_the_lines_or_the_link()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        var setId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
        var ownId = await AddProductAsync("Cream Loaf");
        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(ownId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
        var linkedId = await AddProductAsync("Ensaymada");
        await CreatePackagingLinkRepository().SetSubAssemblyLinkAsync(linkedId, setId, Cancellation);
        var viewModel = await CreateLoadedViewModelAsync();

        foreach (var productId in new[] { ownId, linkedId })
        {
            viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == productId);
            await WaitUntilSettledAsync(() => viewModel.SelectedRecipeTab is PackagingRecipeTab, () => $"tab={viewModel.SelectedRecipeTab?.Header}");
            (await viewModel.RemoveUsageCommand.CanExecute.FirstAsync()).Should().BeTrue();

            await viewModel.RemoveUsageCommand.Execute();

            viewModel.RecipeTabs.Should().BeEmpty();
            viewModel.SelectedRecipeTab.Should().BeNull();
            viewModel.HasPackaging.Should().BeFalse();
            viewModel.Costs.Packaging.Should().Be(0m);

            await viewModel.SaveCommand.Execute();
            viewModel.ErrorMessage.Should().BeNull();
        }

        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingLines.CountAsync(Cancellation)).Should().Be(0);
        (await dbContext.PackagingLinks.CountAsync(Cancellation)).Should().Be(0);
    }

    [Fact]
    public async Task Packaging_is_offered_to_link_or_copy_only_while_the_product_has_none()
    {
        var packagingRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        var boxId = await AddPackagingAsync("Gift Box", 30m);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Box Set", RoleId = packagingRoleId }, [(boxId, null, 1m)], Cancellation);
        var ownId = await AddProductAsync("Cream Loaf");
        await new PackagingLineRepository(CreateDbContextFactory())
            .SetLinesAsync(ownId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);
        await AddProductAsync("Ensaymada");
        var viewModel = await CreateLoadedViewModelAsync();

        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Name == "Ensaymada");
        await WaitUntilSettledAsync(() => viewModel.SelectedProduct?.Name == "Ensaymada", () => $"selected={viewModel.SelectedProduct?.Name}");
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.LinkableSubAssemblies.Select(o => o.DisplayName).Should().Equal(
            ["Gift Box Set — Sub-Assemblies (Packaging)"],
            "linking offers only the Sub-Assemblies screen's, never another product's packaging");
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Copy);
        viewModel.LinkableSubAssemblies.Select(o => o.DisplayName).Should().Equal(
            ["Gift Box Set — Sub-Assemblies (Packaging)", "Packaging — Cream Loaf"],
            "another product's packaging can still be copied, and a product never offers its own");

        // Picking another product closes the form with nothing applied, which is what abandons this add now.
        viewModel.SelectedProduct = viewModel.Products.Single(p => p.Id == ownId);
        await WaitUntilSettledAsync(() => viewModel.HasPackaging, () => $"has packaging={viewModel.HasPackaging}");
        await viewModel.AddUsageCommand.Execute(UsageFormMode.Link);
        viewModel.LinkableSubAssemblies.Should().NotContain(o => o.IsPackaging);
        viewModel.FormRoleOptions.Should().NotContain(r => r.IsPackaging);
    }

    private PackagingLinkRepository CreatePackagingLinkRepository() =>
        new(CreateDbContextFactory(), new PackagingLineRepository(CreateDbContextFactory()));

    /// <summary>A packaging material with <paramref name="baseUnit"/> as its base unit, costing <paramref name="cost"/> a base unit, buffered the same.</summary>
    private async Task<int> AddPackagingAsync(string name, decimal cost, string baseUnit = "pc")
    {
        var unitId = await GetOrCreateUnitOfMeasureAsync(baseUnit, baseUnit);

        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Boxes", Cancellation)
            ?? new RawMaterialCategory { Name = "Boxes" };
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
            Category = category,
            BaseUnitId = unitId,
            CostPerBaseUnit = cost,
            BufferedCostPerBaseUnit = cost,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    private async Task<ProductsViewModel> CreateLoadedViewModelAsync()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        var viewModel = new ProductsViewModel(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new CostingDefaultsRepository(factory),
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
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
        await viewModel.LoadCommand.Execute();
        return viewModel;
    }

    /// <summary>Selecting a product loads its detail in the background: waits until <paramref name="condition"/> has held for a moment.</summary>
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
}
