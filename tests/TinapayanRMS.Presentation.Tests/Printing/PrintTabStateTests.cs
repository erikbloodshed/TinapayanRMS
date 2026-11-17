using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Printing;

/// <summary>
/// The Print tab, on real repositories: with no product ticked it prints the Products screen's product as it shows it;
/// the products ticked, or a folder of the Products list or all products asked for there, which tick theirs, print as
/// they're saved, in the list's order. And a saved product read for a set is the same printout the Products screen gives
/// once it's opened.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class PrintTabStateTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_folder_prints_its_products_and_those_of_the_folders_in_it_in_the_lists_order_under_its_path() => UiThread.RunAsync(async () =>
    {
        var (breads, _) = await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);

        tab.Pick(PrintScope.Folder.Of(products.List.ProductTree.Folder(breads.Id)!));
        await tab.LoadCommand.ExecuteAsync();

        tab.PrintSet!.Title.Should().Be("Production › Breads");
        tab.PrintSet.Products.Select(p => p.ProductName).Should().Equal("Cheese Roll", "Pan de Coco");
        tab.PrintSet.Products.Should().OnlyContain(p => p.SaveNote == null, "a set prints as saved");
        tab.Note.Should().BeNull();
        tab.ChosenProductIds.Should().Equal([IdOf(products, "Cheese Roll"), IdOf(products, "Pan de Coco")], "the Print list ticks them, in its order");
    });

    [Test]
    public Task A_stage_holds_every_product_in_it_whatever_its_folder() => UiThread.RunAsync(async () =>
    {
        await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);

        tab.Pick(PrintScope.Folder.Of(products.List.ProductTree.StageFolder(ProductStage.Production)));
        await tab.LoadCommand.ExecuteAsync();
        tab.PrintSet!.Products.Select(p => p.ProductName).Should().Equal("Cheese Roll", "Pan de Coco", "Mocha Roll", "Loaf");

        tab.Pick(PrintScope.Folder.Of(products.List.ProductTree.StageFolder(ProductStage.Innovation)));
        await tab.LoadCommand.ExecuteAsync();
        tab.PrintSet!.Title.Should().Be("Innovation");
        tab.PrintSet.Products.Select(p => p.ProductName).Should().Equal("Test Bread");
    });

    [Test]
    public Task All_products_print_Production_first_folder_by_folder() => UiThread.RunAsync(async () =>
    {
        await AddCatalogueAsync();
        var tab = CreatePrintTab(await OpenProductsAsync());

        tab.Pick(new PrintScope.AllProducts());
        await tab.LoadCommand.ExecuteAsync();

        tab.PrintSet!.Title.Should().Be("All Products");
        tab.PrintSet.Products.Select(p => p.ProductName).Should().Equal("Cheese Roll", "Pan de Coco", "Mocha Roll", "Loaf", "Test Bread");
        tab.ChosenProductIds.Should().HaveCount(5);
    });

    [Test]
    public Task A_Print_on_Products_ticks_its_products_in_place_of_those_ticked_before_and_offers_every_category_again() => UiThread.RunAsync(async () =>
    {
        var (_, cakes) = await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);
        await tab.LoadCommand.ExecuteAsync();
        tab.ChosenCategoryIds = [tab.CategoryChoices.Single(c => c.Name == "Baked").Id];
        tab.ChosenProductIds = [IdOf(products, "Pan de Coco")];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products is [{ ProductName: "Pan de Coco" }], () => $"pages: {tab.PrintSet?.Products.Count}");

        tab.Pick(PrintScope.Folder.Of(products.List.ProductTree.Folder(cakes.Id)!));
        tab.ChosenCategoryIds.Should().BeEmpty("a folder's products may be in any category");
        await tab.LoadCommand.ExecuteAsync();

        tab.ChosenProductIds.Should().Equal(IdOf(products, "Mocha Roll"));
        tab.ProductChoices.Should().HaveCount(5);
        tab.PrintSet!.Products.Select(p => p.ProductName).Should().Equal("Mocha Roll");

        // The header's Print unticks them, back to the product Products has open.
        tab.Pick(new PrintScope.ThisProduct());
        tab.ChosenProductIds.Should().BeEmpty();
    });

    [Test]
    public Task Products_picked_in_the_list_print_from_their_menu_as_saved_in_the_lists_order() => UiThread.RunAsync(async () =>
    {
        await AddCatalogueAsync();
        var products = await OpenProductsAsync("Loaf");
        var tab = CreatePrintTab(products);
        List<PrintScope> asked = [];
        using var _ = products.PrintRequests.Subscribe(asked.Add);

        // Ctrl+clicks, from three folders and both stages, in an order of their own.
        var tree = products.List.ProductTree;
        products.List.SelectedTreeNodes.Add(tree.Find(IdOf(products, "Test Bread"))!);
        products.List.SelectedTreeNodes.Add(tree.Find(IdOf(products, "Cheese Roll"))!);
        var print = products.List.TreeMenuFor(tree.Find(IdOf(products, "Loaf"))).Single(i => i.Header == "Print 3 Products");
        await ((ScreenCommand<IReadOnlyList<Product>>)print.Command!).ExecuteAsync((IReadOnlyList<Product>)print.Parameter!);

        var scope = asked.Should().ContainSingle().Which.Should().BeOfType<PrintScope.Products>().Subject;
        tab.Pick(scope);
        await tab.LoadCommand.ExecuteAsync();

        tab.PrintSet!.Title.Should().Be("Selected Products");
        tab.PrintSet.Products.Select(p => p.ProductName).Should().Equal("Cheese Roll", "Loaf", "Test Bread");
        tab.PrintSet.Products.Should().OnlyContain(p => p.SaveNote == null, "a set prints as saved");
        tab.ChosenProductIds.Should().Equal([IdOf(products, "Cheese Roll"), IdOf(products, "Loaf"), IdOf(products, "Test Bread")],
            "the Print list ticks them, in its order, which its box names them in");
    });

    [Test]
    public Task One_product_picked_prints_on_its_own_under_its_name_with_no_contents_page() => UiThread.RunAsync(async () =>
    {
        await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);

        tab.Pick(PrintScope.Products.Of([products.Products.Single(p => p.Name == "Mocha Roll")]));
        await tab.LoadCommand.ExecuteAsync();

        tab.PrintSet!.Title.Should().BeNull("one product prints as This product does");
        tab.PrintSet.Products.Select(p => p.ProductName).Should().Equal("Mocha Roll");
    });

    [Test]
    public Task A_folder_set_keeps_its_title_while_its_products_are_the_ones_ticked() => UiThread.RunAsync(async () =>
    {
        var (breads, _) = await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);
        tab.Pick(PrintScope.Folder.Of(products.List.ProductTree.Folder(breads.Id)!));
        await tab.LoadCommand.ExecuteAsync();
        tab.PrintSet!.Title.Should().Be("Production › Breads");

        tab.ChosenProductIds = [.. tab.ChosenProductIds, IdOf(products, "Loaf")];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products.Count == 3, () => $"pages: {tab.PrintSet?.Products.Count}");
        tab.PrintSet!.Title.Should().Be("Selected Products");

        // Every product ticked is all of them, however it came about.
        tab.ChosenProductIds = [.. tab.ProductChoices.Select(c => c.Id)];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products.Count == 5, () => $"pages: {tab.PrintSet?.Products.Count}");
        tab.PrintSet!.Title.Should().Be("All Products");
    });

    [Test]
    public Task The_Print_list_offers_every_product_in_the_lists_order_and_the_categories_those_in_use() => UiThread.RunAsync(async () =>
    {
        await AddCatalogueAsync();
        await using (var dbContext = CreateDbContext())
        {
            dbContext.ProductCategories.Add(new ProductCategory { Name = "Unused" });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var tab = CreatePrintTab(await OpenProductsAsync());

        await tab.LoadCommand.ExecuteAsync();

        tab.ProductChoices.Select(c => c.Name).Should().Equal("Cheese Roll", "Pan de Coco", "Mocha Roll", "Loaf", "Test Bread");
        tab.CategoryChoices.Select(c => c.Name).Should().Equal("Baked", "Cakes", "No category");
        tab.CategoryChoices[^1].Id.Should().Be(PrintableCategory.None);
    });

    [Test]
    public Task Ticking_categories_offers_only_their_products_unticks_the_rest_and_titles_a_set_of_all_of_them_by_their_names() => UiThread.RunAsync(async () =>
    {
        await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);
        await tab.LoadCommand.ExecuteAsync();
        tab.ChosenProductIds = [IdOf(products, "Loaf"), IdOf(products, "Pan de Coco"), IdOf(products, "Mocha Roll")];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products.Count == 3, () => $"pages: {tab.PrintSet?.Products.Count}");

        tab.ChosenCategoryIds = [tab.CategoryChoices.Single(c => c.Name == "Baked").Id, PrintableCategory.None];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products.Count == 2, () => $"pages: {tab.PrintSet?.Products.Count}");

        tab.ProductChoices.Select(c => c.Name).Should().Equal("Cheese Roll", "Pan de Coco", "Loaf", "Test Bread");
        tab.ChosenProductIds.Should().Equal([IdOf(products, "Pan de Coco"), IdOf(products, "Loaf")], "Mocha Roll is a cake");
        tab.PrintSet!.Title.Should().Be("Selected Products");

        // Select All ticks what the list offers: the categories' every product.
        tab.ChosenProductIds = [.. tab.ProductChoices.Select(c => c.Id)];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products.Count == 4, () => $"pages: {tab.PrintSet?.Products.Count}");
        tab.PrintSet!.Title.Should().Be("Baked, No category");

        // Every category unticked offers every product again, and keeps those ticked. With products ticked it prepares the
        // pages again (for their title), so the test waits for that: a read left running would hold up the next test's reset.
        tab.ChosenCategoryIds = [];
        await Settle.UntilAsync(tab, () => tab.ProductChoices.Count == 5 && tab.ChosenProductIds.Length == 4,
            () => $"offered: {tab.ProductChoices.Count}, ticked: {tab.ChosenProductIds.Length}");
        tab.PrintSet!.Title.Should().Be("Selected Products", "four of the five products are ticked");
    });

    [Test]
    public Task Ticking_products_in_the_Print_list_prints_them_and_unticking_the_last_goes_back_to_This_product() => UiThread.RunAsync(async () =>
    {
        await AddCatalogueAsync();
        var products = await OpenProductsAsync("Loaf");
        var tab = CreatePrintTab(products);
        await tab.LoadCommand.ExecuteAsync();
        tab.PrintSet!.Products.Select(p => p.ProductName).Should().Equal("Loaf");

        tab.ChosenProductIds = [IdOf(products, "Test Bread"), IdOf(products, "Pan de Coco")];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products.Count == 2, () => $"pages: {tab.PrintSet?.Products.Count}");

        tab.PrintSet!.Title.Should().Be("Selected Products");
        tab.PrintSet.Products.Select(p => p.ProductName).Should().Equal("Pan de Coco", "Test Bread");

        // One left ticked prints on its own, under its name.
        tab.ChosenProductIds = [IdOf(products, "Pan de Coco")];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products.Count == 1, () => $"pages: {tab.PrintSet?.Products.Count}");
        tab.PrintSet!.Title.Should().BeNull();

        tab.ChosenProductIds = [];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products is [{ ProductName: "Loaf" }], () => $"pages: {tab.PrintSet?.Products.Count}");
        tab.PrintSet!.Title.Should().BeNull();
    });

    [Test]
    public Task A_product_ticked_and_deleted_since_is_unticked() => UiThread.RunAsync(async () =>
    {
        await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);
        await tab.LoadCommand.ExecuteAsync();
        tab.ChosenProductIds = [IdOf(products, "Loaf"), IdOf(products, "Mocha Roll")];
        await Settle.UntilAsync(tab, () => tab.PrintSet?.Products.Count == 2, () => $"pages: {tab.PrintSet?.Products.Count}");

        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(IdOf(products, "Mocha Roll"), Cancellation);
        await tab.LoadCommand.ExecuteAsync();

        tab.ChosenProductIds.Should().Equal(IdOf(products, "Loaf"));
        tab.PrintSet!.Products.Select(p => p.ProductName).Should().Equal("Loaf");
        tab.ProductChoices.Select(c => c.Name).Should().NotContain("Mocha Roll");
    });

    private static int IdOf(ProductsState products, string name) => products.Products.Single(p => p.Name == name).Id;

    [Test]
    public Task A_folder_prints_a_folder_made_in_it_after_it_was_picked() => UiThread.RunAsync(async () =>
    {
        var (breads, _) = await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);
        tab.Pick(PrintScope.Folder.Of(products.List.ProductTree.Folder(breads.Id)!));

        var holiday = new ProductFolder { Name = "Holiday", Stage = ProductStage.Production, ParentId = breads.Id };
        await new ProductFolderRepository(CreateDbContextFactory()).AddAsync(holiday, Cancellation);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Products.Add(new Product { Name = "Bibingka", Stage = ProductStage.Production, FolderId = holiday.Id });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await products.LoadCommand.ExecuteAsync();
        await tab.LoadCommand.ExecuteAsync();

        tab.PrintSet!.Products.Select(p => p.ProductName).Should().Equal("Bibingka", "Cheese Roll", "Pan de Coco");
    });

    [Test]
    public Task A_folder_prints_what_another_computer_filed_in_a_folder_it_made_in_it_before_Products_reads_it_again() => UiThread.RunAsync(async () =>
    {
        var (breads, _) = await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);

        // Made and filed in from another computer: this one's Products list hasn't read it.
        var holiday = new ProductFolder { Name = "Holiday", Stage = ProductStage.Production, ParentId = breads.Id };
        await new ProductFolderRepository(CreateDbContextFactory()).AddAsync(holiday, Cancellation);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Products.Add(new Product { Name = "Bibingka", Stage = ProductStage.Production, FolderId = holiday.Id });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        tab.Pick(PrintScope.Folder.Of(products.List.ProductTree.Folder(breads.Id)!));
        await tab.LoadCommand.ExecuteAsync();
        tab.PrintSet!.Products.Select(p => p.ProductName).Should().Equal(["Bibingka", "Cheese Roll", "Pan de Coco"], "Bibingka is in Breads › Holiday");

        tab.Pick(new PrintScope.AllProducts());
        await tab.LoadCommand.ExecuteAsync();
        tab.PrintSet!.Products.Select(p => p.ProductName).Should().Equal(
            ["Bibingka", "Cheese Roll", "Pan de Coco", "Mocha Roll", "Loaf", "Test Bread"], "in its folder's place, not loose at the top of Production");
    });

    [Test]
    public Task A_folder_moved_to_the_other_stage_since_it_was_picked_prints_its_products_under_its_new_path() => UiThread.RunAsync(async () =>
    {
        var (_, cakes) = await AddCatalogueAsync();
        var products = await OpenProductsAsync();
        var tab = CreatePrintTab(products);
        tab.Pick(PrintScope.Folder.Of(products.List.ProductTree.Folder(cakes.Id)!));

        // Its products go with it (ProductFolderRepository.MoveAsync).
        await new ProductFolderRepository(CreateDbContextFactory()).MoveAsync(new ProductFolderMove(cakes.Id, ProductStage.Innovation, null), Cancellation);
        await tab.LoadCommand.ExecuteAsync();

        tab.PrintSet!.Products.Select(p => p.ProductName).Should().Equal(["Mocha Roll"], "it went to Innovation with it");
        tab.PrintSet.Title.Should().Be("Innovation › Cakes", "the title says where it is now");
    });

    [Test]
    public Task A_set_prints_the_open_product_as_saved_and_says_so_while_This_product_has_its_edits() => UiThread.RunAsync(async () =>
    {
        await AddCatalogueAsync();
        var products = await OpenProductsAsync("Cheese Roll");
        products.EditingCurrentPrice = 45m;
        var tab = CreatePrintTab(products);

        tab.Pick(new PrintScope.AllProducts());
        await tab.LoadCommand.ExecuteAsync();

        tab.PrintSet!.Products.Single(p => p.ProductName == "Cheese Roll").Costs.CurrentPrice.Should().BeNull("it isn't saved");
        tab.Note.Should().Be("Cheese Roll has unsaved changes on Products; it's printed as saved.");

        tab.Pick(new PrintScope.ThisProduct());
        await tab.LoadCommand.ExecuteAsync();

        tab.PrintSet!.Title.Should().BeNull();
        tab.PrintSet.Products.Single().Costs.CurrentPrice.Should().Be(45m);
        tab.PrintSet.Products.Single().SaveNote.Should().Be(ProductPrintout.UnsavedChangesNote);
        tab.Note.Should().BeNull();
    });

    [Test]
    public Task A_saved_product_reads_as_the_Products_screen_shows_it_once_opened() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(sugarId, 200m)], UsageAuthoredField.Yield, 40m),
            ],
            []));
        await AddBoxAsync(productId);
        await SetCostingAsync(productId);
        var products = await OpenProductsAsync("Ube Roll", vm => vm.Recipe.UsageEdits.Count == 2 && vm.Packaging.LineEdits.Count > 0);

        var screen = products.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Subject;
        var saved = (await CreateReader().ReadAsync(products.SelectedProduct!, Cancellation)).ToPrintout(screen.PrintedAt);

        saved.Should().BeEquivalentTo(screen);
        saved.Costs.SellingPrice.Should().BePositive();
    });

    [Test]
    public Task Unbuffered_a_set_prints_at_the_true_costs_as_This_product_does_and_says_so() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.04m, bufferedCostPerGram: 0.05m);
        var (productId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m);
        await AddBoxAsync(productId, unbufferedCost: 25m);
        await SetCostingAsync(productId);
        var products = await OpenProductsAsync("Ensaymada", vm => vm.Recipe.UsageEdits.Count == 1 && vm.Packaging.LineEdits.Count > 0);
        await products.ToggleCostBasisCommand.ExecuteAsync();
        var tab = CreatePrintTab(products);

        tab.Pick(new PrintScope.ThisProduct());
        await tab.LoadCommand.ExecuteAsync();
        var screen = tab.PrintSet!.Products.Single();

        tab.Pick(new PrintScope.AllProducts());
        await tab.LoadCommand.ExecuteAsync();
        var saved = tab.PrintSet!.Products.Single();

        saved.CostBasis.Should().Be(CostBasis.Unbuffered);
        (saved.Costs.SubAssemblies, saved.Costs.Packaging).Should().Be((2.00m, 25m));
        saved.Should().BeEquivalentTo(screen, o => o.Excluding(p => p.PrintedAt));
        tab.Note.Should().BeNull("the open product has no edits: the switch isn't one");
    });

    [Test]
    public Task A_saved_composite_reads_as_the_Products_screen_shows_it_once_opened() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 1000m)], weightPerPiece: 50m);
        var (cheeseRollId, _) = await AddProductWithSubAssemblyAsync("Cheese Roll", doughRoleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var boxId = await new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = "Pasalubong Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ensaymadaId, 6), (cheeseRollId, 4)] },
            null,
            null,
            null,
            Cancellation);
        await AddBoxAsync(boxId);
        await SetCostingAsync(boxId);
        var products = await OpenProductsAsync("Pasalubong Box", vm => vm.ComponentsTable.ComponentEdits.Count == 2 && vm.Packaging.LineEdits.Count > 0);

        var screen = products.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Subject;
        var saved = (await CreateReader().ReadAsync(products.SelectedProduct!, Cancellation)).ToPrintout(screen.PrintedAt);

        saved.Should().BeEquivalentTo(screen);
        saved.Components.Should().HaveCount(2);
        saved.Costs.SubAssemblies.Should().Be(35m);
        saved.Costs.Packaging.Should().Be(30m);
    });

    /// <summary>
    /// Production › Breads (Pan de Coco) › Rolls (Cheese Roll), Production › Cakes (Mocha Roll), Loaf at the top of
    /// Production, and Innovation › Breads (Test Bread), named out of order, so the list's order is the test's. Categories:
    /// Baked (Pan de Coco, Cheese Roll, Test Bread), Cakes (Mocha Roll), and none for Loaf.
    /// </summary>
    private async Task<(ProductFolder Breads, ProductFolder Cakes)> AddCatalogueAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new ProductCategory { Name = "Baked" };
        var cakeCategory = new ProductCategory { Name = "Cakes" };
        var breads = new ProductFolder { Name = "Breads", Stage = ProductStage.Production };
        var cakes = new ProductFolder { Name = "Cakes", Stage = ProductStage.Production };
        var newBreads = new ProductFolder { Name = "Breads", Stage = ProductStage.Innovation };
        dbContext.ProductCategories.AddRange(cakeCategory, category);
        dbContext.ProductFolders.AddRange(breads, cakes, newBreads);
        await dbContext.SaveChangesAsync(Cancellation);
        var rolls = new ProductFolder { Name = "Rolls", Stage = ProductStage.Production, ParentId = breads.Id };
        dbContext.ProductFolders.Add(rolls);
        await dbContext.SaveChangesAsync(Cancellation);
        dbContext.Products.AddRange(
            new Product { Name = "Pan de Coco", Stage = ProductStage.Production, FolderId = breads.Id, Category = category },
            new Product { Name = "Test Bread", Stage = ProductStage.Innovation, FolderId = newBreads.Id, Category = category },
            new Product { Name = "Mocha Roll", Stage = ProductStage.Production, FolderId = cakes.Id, Category = cakeCategory },
            new Product { Name = "Loaf", Stage = ProductStage.Production },
            new Product { Name = "Cheese Roll", Stage = ProductStage.Production, FolderId = rolls.Id, Category = category });
        await dbContext.SaveChangesAsync(Cancellation);
        return (breads, cakes);
    }

    /// <summary>A box a piece, 30 buffered and <paramref name="unbufferedCost"/> (30 unless given) true, saved straight to the product.</summary>
    private async Task AddBoxAsync(int productId, decimal unbufferedCost = 30m)
    {
        await using var dbContext = CreateDbContext();
        var box = new RawMaterial
        {
            Name = "Gift Box", Type = RawMaterialType.Packaging, Category = new RawMaterialCategory { Name = "Boxes" },
            BaseUnit = new UnitOfMeasure { Name = "piece", Abbreviation = "pc" },
            CostPerBaseUnit = unbufferedCost, BufferedCostPerBaseUnit = 30m,
        };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);

        dbContext.PackagingLines.Add(new PackagingLine
        {
            ProductId = productId, PackagingMaterialId = box.Id, AuthoredField = PackagingLineAuthoredField.QtyPerUnit, QtyPerUnit = 1m,
        });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task SetCostingAsync(int productId)
    {
        await SetVatAsync(12m);
        await using var dbContext = CreateDbContext();
        dbContext.CostingParameters.Add(new CostingParameters
        {
            ProductId = productId, LaborPercent = 25m, OverheadPercent = 10m, AdministrativePercent = 5m, MarginPercent = 30m,
        });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private PrintTabState CreatePrintTab(ProductsState products)
    {
        var factory = CreateDbContextFactory();
        return new PrintTabState(
            products, new ProductRepository(factory), new ProductFolderRepository(factory), new ProductCategoryRepository(factory), CreateReader(), new LoggerConfiguration().CreateLogger());
    }

    private SavedProductReader CreateReader()
    {
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);
        return new SavedProductReader(
            new PackagingLinkRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new SubAssemblyLineRepository(factory),
            new ProductRepository(factory),
            new CostingDefaultsRepository(factory),
            CreateUnitOfWork());
    }

    /// <summary>The Products screen, loaded, with <paramref name="productName"/> open when one is given.</summary>
    private async Task<ProductsState> OpenProductsAsync(string? productName = null, Func<ProductsState, bool>? ready = null)
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        if (productName is not null)
        {
            state.Pick(state.Products.Single(p => p.Name == productName));
            await Settle.UntilAsync(state,
                () => state.AreDetailsReady && (ready?.Invoke(state) ?? true),
                () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}");
        }

        return state;
    }

    private ProductsState CreateState()
    {
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ProductsState(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new CostingDefaultsRepository(factory),
            new PackagingLinkRepository(factory),
            new ProductCloner(factory),
            CreateUnitOfWork(),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductFolderRepository(factory),
            new ProductVersionRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }
}
