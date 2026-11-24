using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// The Products form itself, on the database, over random sequences of the steps only the form has (docs/adr/0006,
/// docs/adr/0012): a recipe tab picked, the section form typed into (a piece size, a line's grams), undos, redos and Saves.
/// After every step the tab strip and the section form show the same section, and the form's figures are what its tables
/// and piece size give as they stand; with every step undone, there's nothing to save. A draft's own sequences, with no
/// form and no database, are in its *DraftTests. The sequences are made up front by CsCheck from a seed each message
/// names, and run on the form's UI thread, which CsCheck's own sampling doesn't keep to.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class ProductsStateSequenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>How many products a run takes a sequence through: each opened on the database, so a run takes some seconds.</summary>
    private const int Runs = 12;

    [Test]
    public Task Across_the_recipe_tabs_the_tab_the_form_and_its_figures_stay_together() => UiThread.RunAsync(async () =>
    {
        var (doughRoleId, fillingRoleId) = (await AddRoleAsync("Dough", sortOrder: 1), await AddRoleAsync("Filling", sortOrder: 2));
        var (flourId, sugarId) = (await AddIngredientAsync("Flour", costPerGram: 0.05m), await AddIngredientAsync("Sugar", costPerGram: 0.09m));

        var (stream, seed) = ((uint)Random.Shared.Next(), (ulong)Random.Shared.NextInt64());
        var pcg = new PCG(stream, seed);
        for (var run = 0; run < Runs; run++)
        {
            var steps = Steps.Array[1, 14].Generate(pcg, null, out _);
            var sequence = $"run {run + 1} of PCG({stream}, {seed}): [{string.Join(", ", steps.AsEnumerable())}]";
            var productId = await AddProductAsync($"Ube Roll {run + 1}");
            await SaveRecipeAsync(productId, new RecipeDraft(
                [
                    new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m), (sugarId, 120m)], UsageAuthoredField.WeightPerPiece, 50m),
                    new NewSubAssemblyDraft(fillingRoleId, [(sugarId, 200m)], UsageAuthoredField.WeightPerPiece, 10m),
                ],
                []));
            var state = await OpenAsync(productId);

            for (var i = 0; i < steps.Length; i++)
            {
                await ApplyAsync(state, steps[i], sequence);
                ShouldBeTogether(state, $"after step {i + 1}, {steps[i]}, of {sequence}");
            }

            while (state.TryUndo())
            {
                ShouldBeTogether(state, $"undoing every step of {sequence}");
            }

            state.SaveCommand.CanExecute().Should().BeFalse($"every step undone leaves nothing to save, {sequence}");
        }
    });

    /// <summary>One thing a user does on the form: a recipe tab picked, the section form typed into, an undo, a redo, a Save.</summary>
    private abstract record Step;

    /// <summary>The <c>Index</c>th of the recipe's tabs (its sections', then Packaging) picked in the strip.</summary>
    private sealed record PickTab(int Index) : Step;

    private sealed record TypeWeightPerPiece(int Grams) : Step;

    private sealed record TypeGrams(int Line, int Grams) : Step;

    private sealed record Undo : Step;

    private sealed record Redo : Step;

    private sealed record Save : Step;

    private static readonly Gen<Step> Steps = Gen.Frequency(
        (4, Gen.Int[0, 2].Select(index => (Step)new PickTab(index))),
        (3, Gen.Int[1, 120].Select(grams => (Step)new TypeWeightPerPiece(grams))),
        (3, Gen.Select(Gen.Int[0, 3], Gen.Int[1, 1500], (line, grams) => (Step)new TypeGrams(line, grams))),
        (3, Gen.Const<Step>(new Undo())),
        (2, Gen.Const<Step>(new Redo())),
        (1, Gen.Const<Step>(new Save())));

    /// <summary>A step as the screen takes it: typing only into a section's form showing, as a field brackets it in one undo step.</summary>
    private static async Task ApplyAsync(ProductForm state, Step step, string sequence)
    {
        var form = state.UsageForm;
        switch (step)
        {
            case PickTab(var index):
                var tabs = state.Recipe.RecipeTabs;
                state.Recipe.PickRecipeTab(tabs[index % tabs.Count]);
                break;
            case TypeWeightPerPiece(var grams) when form.IsUsageFormOpen:
                Edit(state, () => form.TypeWeightPerPiece(grams));
                break;
            case TypeGrams(var line, var grams) when form.IsUsageFormOpen && form.IsFormEditingLines && form.FormLines.Count > 0:
                Edit(state, () => form.FormLines[line % form.FormLines.Count].Grams = grams);
                break;
            case Undo:
                state.TryUndo();
                break;
            case Redo:
                state.TryRedo();
                break;
            case Save when state.SaveCommand.CanExecute():
                await state.SaveCommand.ExecuteAsync();
                state.ErrorMessage.Should().BeNull($"the Save of {sequence}");
                await Settle.UntilAsync(state,
                    () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 2,
                    () => $"details={state.AreDetailsReady} sections={state.Recipe.UsageEdits.Count}, {sequence}");
                break;
        }
    }

    /// <summary>
    /// The tab strip and the section form show the same section, and the form's figures are what its tables and piece size
    /// give as they stand, worked out afresh as the form works them out (ProductUsageForm.Figures): a mark missed leaves
    /// them as they were.
    /// </summary>
    private static void ShouldBeTogether(ProductForm state, string when)
    {
        var form = state.UsageForm;
        if (state.Recipe.SelectedRecipeTab is SubAssemblyRecipeTab tab)
        {
            form.IsUsageFormOpen.Should().BeTrue($"{when}, a section's tab shows its form");
            form.EditingUsage.Should().BeSameAs(tab.Usage, $"{when}, the form is the tab's section's, not another's");
        }
        else
        {
            state.Recipe.IsUsageFormShowing.Should().BeFalse($"{when}, no section's form shows on the {state.Recipe.SelectedRecipeTab} tab");
            return;
        }

        var expected = UsageFormFigures.Of(
            form.Batch(),
            form.FormAddIns.Where(l => l.Ingredient is not null || l.IsSection).Sum(l => l.Grams) ?? 0m,
            (form.Sections.UsedElsewhere, form.Sections.Users),
            form.AuthoredField,
            form.AuthoredValue,
            form.FormFormulaBase,
            form.EditingUsage?.DisplayName ?? form.EditingUsageSource?.SubAssembly?.Name ?? "the recipe");
        (form.UsageFormTotalWeightText, form.UsageFormPieceSizeCaption, form.UsageFormBaseCaption, form.UsageFormAddInsCaption, form.UsageFormCostPerPieceText, form.UsageFormBatchCostText)
            .Should().Be(
                (expected.TotalWeightText, expected.PieceSizeCaption, expected.BaseCaption, expected.AddInsCaption, expected.CostPerPieceText, expected.BatchCostText),
                $"{when}, the section form's figures follow its tables and piece size");
    }

    /// <summary>One finished change, as the fields and line tables bracket a gesture in the app.</summary>
    private static void Edit(ProductForm state, Action change)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    private async Task<ProductForm> OpenAsync(int productId)
    {
        var state = ProductsViews.Create(CreateDbContextFactory(), CreateUnitOfWork()).Form!;
        await state.LoadCommand.ExecuteAsync();
        state.Pick(state.Products.Single(p => p.Id == productId));
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Recipe.UsageEdits.Count == 2,
            () => $"ready={state.AreDetailsReady}, sections={state.Recipe.UsageEdits.Count}");
        return state;
    }
}
