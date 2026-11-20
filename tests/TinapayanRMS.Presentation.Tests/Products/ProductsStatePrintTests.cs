using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// Print on the Products screen, on real repositories: what the Print dialog shows is the product as the screen shows it,
/// unsaved edits included, with a form left open applied first, as Save applies it.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class ProductsStatePrintTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Print_shows_the_product_as_the_screen_shows_it() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m),
                new NewSubAssemblyDraft(fillingRoleId, [(sugarId, 200m)], UsageAuthoredField.WeightPerPiece, 10m),
            ],
            []));
        await AddPackagingLineAsync(productId);
        var state = await OpenAsync(productId, vm => vm.Recipe.UsageEdits.Count == 2 && vm.Packaging.LineEdits.Count > 0);

        var printout = await PrintAsync(state);

        printout.ProductName.Should().Be("Ube Roll");
        printout.CategoryName.Should().Be("Uncategorized");
        printout.StageTitle.Should().Be("Innovation");
        printout.CostUnitCaption.Should().Be("Per piece");
        printout.SaveNote.Should().BeNull("nothing was changed since it was saved");

        printout.Sections.Select(s => s.Name).Should().Equal("Dough", "Filling");
        printout.Sections[0].Lines.Should().Equal(new PrintedLine("Flour", 1000m, 100m, CostPerGram: 0.05m, 50m));
        printout.Sections[1].Lines.Should().Equal(new PrintedLine("Sugar", 200m, 100m, CostPerGram: 0.09m, 18m));
        (printout.Sections[0].WeightPerPiece, printout.Sections[0].Yield).Should().Be((50m, 20m));

        printout.Packaging.Should().NotBeNull();
        printout.Packaging!.Lines.Should().Equal(new PrintedPackagingLine("Gift Box", 1m, "pc", 30m, 30m));
        printout.Costs.Should().BeSameAs(state.Costing.Costs, "the costing is the Costing tab's");
    });

    [Test]
    public Task The_suggested_selling_price_label_follows_the_quantity_required() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        state.Costing.SuggestedSellingPriceLabel.Should().Be("Suggested Selling Price per Piece");
        var changed = new List<string?>();
        state.Costing.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        state.EditingQuantityRequired = 8;

        changed.Should().Contain(nameof(ProductCosting.SuggestedSellingPriceLabel));
        state.Costing.SuggestedSellingPriceLabel.Should().Be("Suggested Selling Price per Unit of 8 Pieces", "the Costing Summary's price row follows the quantity");
    });

    [Test]
    public Task Print_applies_a_line_typed_into_the_open_form_and_notes_the_unsaved_change() => UiThread.RunAsync(async () =>
    {
        var (state, sugarId) = await OpenOneSubAssemblyProductAsync();
        state.UsageForm.IsUsageFormOpen.Should().BeTrue();
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 500m });

        var printout = await PrintAsync(state);

        printout.Sections.Single().Lines.Select(l => (l.Ingredient, l.Grams)).Should().Equal(("Flour", 1000m), ("Sugar", 500m));
        printout.SaveNote.Should().Be(ProductPrintout.UnsavedChangesNote);
        state.Recipe.UsageEdits.Single().Lines.Should().HaveCount(2, "the form was applied, as Save applies it");
        state.UsageForm.IsUsageFormOpen.Should().BeTrue("its tab shows it again, as after a Save");

        // Applying it was one step, which Undo takes back.
        state.TryUndo().Should().BeTrue();
        state.Recipe.UsageEdits.Single().Lines.Should().ContainSingle();
    });

    [Test]
    public Task A_form_a_rule_refuses_shows_why_and_nothing_opens() => UiThread.RunAsync(async () =>
    {
        var (state, sugarId) = await OpenOneSubAssemblyProductAsync();
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = sugarId });
        var requests = 0;
        using var subscription = state.PrintRequests.Subscribe(_ => requests++);

        await state.PrintCommand.ExecuteAsync();

        state.ErrorMessage.Should().Contain("Sugar", "a line with no grams can't go into the recipe");
        requests.Should().Be(0, "the Print dialog doesn't open");
        state.Recipe.UsageEdits.Single().Lines.Should().ContainSingle();
    });

    [Test]
    public Task The_Print_tab_shown_again_has_the_edits_made_on_Products_since() => UiThread.RunAsync(async () =>
    {
        var (state, sugarId) = await OpenOneSubAssemblyProductAsync();
        (await PrintAsync(state)).SaveNote.Should().BeNull();

        state.EditingName = "Ube Roll Deluxe";
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 250m });
        var printout = state.PreparePrintout().Value.Should().BeOfType<ProductPrintout>().Subject;

        printout.ProductName.Should().Be("Ube Roll Deluxe");
        printout.Sections.Single().Lines.Select(l => (l.Ingredient, l.Grams)).Should().Equal(("Flour", 1000m), ("Sugar", 250m));
        printout.SaveNote.Should().Be(ProductPrintout.UnsavedChangesNote);
    });

    [Test]
    public Task A_form_refused_when_the_Print_tab_is_shown_again_leaves_its_pages_and_says_why() => UiThread.RunAsync(async () =>
    {
        var (state, sugarId) = await OpenOneSubAssemblyProductAsync();
        await PrintAsync(state);

        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = sugarId });
        var problem = state.PreparePrintout().Value.Should().BeOfType<PrintProblem>("the tab keeps the pages it has").Subject.Message;

        problem.Should().Contain("Sugar");
        state.ErrorMessage.Should().Be(problem, "Products shows it too, for when it's next shown");
    });

    [Test]
    public Task A_new_product_prints_as_not_saved_yet() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.EditingName = "Pandesal";

        var printout = await PrintAsync(state);

        printout.ProductName.Should().Be("Pandesal");
        printout.SaveNote.Should().Be(ProductPrintout.NotSavedNote);
        printout.Sections.Should().BeEmpty();
        printout.Packaging.Should().BeNull();
    });

    [Test]
    public Task Export_to_PDF_saves_the_product_as_the_screen_shows_it_and_says_where() => UiThread.RunAsync(async () =>
    {
        var (state, sugarId) = await OpenOneSubAssemblyProductAsync();
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 500m });
        ProductPrintSet? asked = null;
        using var handler = state.ExportPdf.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(@"C:\Exports\Ube Roll.pdf");
        });

        await state.ExportPdfCommand.ExecuteAsync();

        var printout = asked!.Products.Should().ContainSingle().Subject;
        asked.Title.Should().BeNull("one product has no contents page");
        printout.ProductName.Should().Be("Ube Roll");
        printout.Sections.Single().Lines.Select(l => (l.Ingredient, l.Grams)).Should().Equal(("Flour", 1000m), ("Sugar", 500m));
        printout.SaveNote.Should().Be(ProductPrintout.UnsavedChangesNote);
        state.SuccessMessage.Should().Be(@"Saved Ube Roll.pdf in C:\Exports.");
    });

    [Test]
    public Task Cancelling_Export_to_PDF_says_nothing() => UiThread.RunAsync(async () =>
    {
        var (state, _) = await OpenOneSubAssemblyProductAsync();
        using var handler = state.ExportPdf.RegisterHandler(interaction => interaction.SetOutput(null));

        await state.ExportPdfCommand.ExecuteAsync();

        state.SuccessMessage.Should().BeNull();
        state.ErrorMessage.Should().BeNull();
    });

    [Test]
    public Task Export_to_PDF_of_a_form_a_rule_refuses_shows_why_and_asks_nothing() => UiThread.RunAsync(async () =>
    {
        var (state, sugarId) = await OpenOneSubAssemblyProductAsync();
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = sugarId });
        var asked = 0;
        using var handler = state.ExportPdf.RegisterHandler(interaction =>
        {
            asked++;
            interaction.SetOutput(null);
        });

        await state.ExportPdfCommand.ExecuteAsync();

        state.ErrorMessage.Should().Contain("Sugar", "a line with no grams can't go into the recipe");
        asked.Should().Be(0);
    });

    [Test]
    public Task The_Costing_tabs_Export_to_Excel_gives_the_product_as_the_screen_shows_it_and_says_where() => UiThread.RunAsync(async () =>
    {
        var (state, sugarId) = await OpenOneSubAssemblyProductAsync();
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = sugarId, Grams = 500m });
        ProductPrintout? asked = null;
        using var handler = state.ExportCosting.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(@"C:\Exports\Ube Roll Costing.xlsx");
        });

        await state.ExportCostingCommand.ExecuteAsync();

        asked!.ProductName.Should().Be("Ube Roll");
        asked.Sections.Single().Lines.Select(l => l.Ingredient).Should().Equal("Flour", "Sugar");
        state.SuccessMessage.Should().Be(@"Saved Ube Roll Costing.xlsx in C:\Exports.");
    });

    [Test]
    public Task The_Costing_tabs_Export_to_Excel_of_a_form_a_rule_refuses_shows_why_and_asks_nothing() => UiThread.RunAsync(async () =>
    {
        var (state, sugarId) = await OpenOneSubAssemblyProductAsync();
        state.UsageForm.FormLines.Add(new IngredientLineRow { IngredientId = sugarId });
        var asked = 0;
        using var handler = state.ExportCosting.RegisterHandler(interaction =>
        {
            asked++;
            interaction.SetOutput(null);
        });

        await state.ExportCostingCommand.ExecuteAsync();

        state.ErrorMessage.Should().Contain("Sugar");
        asked.Should().Be(0);
    });

    /// <summary>Runs Print, then asks for the printout as the Print dialog does once Print has asked for it.</summary>
    private static async Task<ProductPrintout> PrintAsync(ProductForm state)
    {
        var requests = 0;
        using (state.PrintRequests.Subscribe(_ => requests++))
        {
            await state.PrintCommand.ExecuteAsync();
        }

        state.ErrorMessage.Should().BeNull();
        requests.Should().Be(1, "Print asks for the Print dialog");
        return state.PreparePrintout().Value.Should().BeOfType<ProductPrintout>("the Print dialog gets the pages").Subject;
    }

    /// <summary>A saved product with one Dough of 1,000 g of flour, opened with its form showing, and the id of an ingredient it doesn't use.</summary>
    private async Task<(ProductForm State, int SugarId)> OpenOneSubAssemblyProductAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var productId = await AddProductAsync("Ube Roll");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)], []));

        return (await OpenAsync(productId, vm => vm.UsageForm.FormLines.Count == 1), sugarId);
    }

    /// <summary>A box a piece, saved straight to the product.</summary>
    private async Task AddPackagingLineAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        var box = new RawMaterial
        {
            Name = "Gift Box", Type = RawMaterialType.Packaging, Category = new RawMaterialCategory { Name = "Boxes" },
            BaseUnit = new UnitOfMeasure { Name = "piece", Abbreviation = "pc" },
            CostPerBaseUnit = 30m, BufferedCostPerBaseUnit = 30m,
        };
        dbContext.RawMaterials.Add(box);
        await dbContext.SaveChangesAsync(Cancellation);

        dbContext.PackagingLines.Add(new PackagingLine
        {
            ProductId = productId, PackagingMaterialId = box.Id, AuthoredField = PackagingLineAuthoredField.QtyPerUnit, QtyPerUnit = 1m,
        });
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task<ProductForm> OpenAsync(int productId, Func<ProductForm, bool> ready)
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && ready(state),
            () => $"ready={state.AreDetailsReady}, usages={state.Recipe.UsageEdits.Count}, lines={state.UsageForm.FormLines.Count}");
        return state;
    }

    private ProductForm CreateState()
    {
        var factory = CreateDbContextFactory();
        return ProductsViews.Create(factory, CreateUnitOfWork()).Form!;
    }
}
