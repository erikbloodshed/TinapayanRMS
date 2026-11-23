using System.Collections.ObjectModel;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// The section form (<see cref="ProductUsageForm"/>) as an undo step keeps it (<see cref="SectionFormSnapshot"/>), and its
/// tables and piece size put back from one. The recipe (ProductRecipe.Capture and Restore) puts the rest back around it: the
/// sections, the option lists and the pickers.
/// </summary>
internal static class SectionFormCapture
{
    /// <summary>
    /// <paramref name="form"/> as it stands, the section it edits by its place among <paramref name="usages"/>: for an undo
    /// step, and for a load to leave what's typed into it alone.
    /// </summary>
    public static SectionFormSnapshot Capture(ProductUsageForm form, IList<ProductUsageEdit> usages) => new()
    {
        IsOpen = form.IsUsageFormOpen,
        IsAdding = form.IsAddingUsage,
        Title = form.UsageFormTitle,
        Mode = form.EditingUsageMode,
        IsEditingLines = form.IsFormEditingLines,
        IsPickingSource = form.IsFormPickingSource,
        EditingUsageIndex = form.EditingUsage is { } editing && usages.IndexOf(editing) is var i and >= 0 ? i : null,
        RoleId = form.EditingUsageRole?.Id,
        Name = form.EditingUsageName,
        SourceKey = form.EditingUsageSource?.Key,
        Lines = [.. form.FormLines.Select(CaptureIngredientLine)],
        AddIns = [.. form.FormAddIns.Select(CaptureIngredientLine)],
        FormulaBase = form.FormFormulaBase,
        AuthoredField = form.AuthoredField,
        WeightPerPiece = form.EditingUsageWeightPerPiece,
        Yield = form.EditingUsageYield,
        GoesIntoIndex = SectionFormSnapshot.IndexOfKey([.. usages], form.Fold.TargetKey),
    };

    /// <summary>
    /// Puts back <paramref name="snapshot"/>'s tables, formula base and piece size, each row priced from
    /// <paramref name="ingredients"/>, the ingredients as the screen last loaded them, and what the section goes into whole,
    /// among what <paramref name="draft"/>'s sections, already put back, let it. Written, never typed, so nothing a change of
    /// them sets off runs.
    /// </summary>
    public static void RestoreTables(ProductUsageForm form, SectionFormSnapshot snapshot, IReadOnlyList<RawMaterial> ingredients, ProductRecipeDraft draft)
    {
        var editing = snapshot.EditingUsageIndex is { } index && index < draft.UsageEdits.Count ? draft.UsageEdits[index] : null;
        var goesInto = snapshot.GoesIntoIndex is { } at && at >= 0 && at < draft.UsageEdits.Count ? draft.UsageEdits[at].Key : (long?)null;
        form.Fold.Show(draft.FoldTargetsFor(editing), goesInto, editing is null ? [] : draft.FoldedInto(editing));
        form.FormFormulaBase = snapshot.FormulaBase;
        RestoreIngredientLines(form.FormLines, snapshot.Lines, snapshot.FormulaBase, ingredients);
        RestoreIngredientLines(form.FormAddIns, snapshot.AddIns, snapshot.FormulaBase, ingredients);
        form.RestorePieceSize(snapshot.AuthoredField, snapshot.WeightPerPiece, snapshot.Yield);
    }

    /// <summary>
    /// The recipe's sections changed (a load repricing them, a linked one's lines saved elsewhere, an undo): the open form's
    /// rows of what goes into its section follow, and the piece-size figure not typed follows them, as it follows the lines.
    /// </summary>
    public static void FollowFoldedIn(ProductUsageForm form, ProductRecipeDraft draft)
    {
        if (form.EditingUsage is { } editing && form.Fold.ShowFoldedIn(draft.FoldedInto(editing)))
        {
            form.FollowPieceSize();
        }
    }

    private static IngredientLineSnapshot CaptureIngredientLine(IngredientLineRow row) =>
        new(row.IngredientId, row.Grams, row.IsFormulaBase, row.AuthoredBakersPercent);

    private static void RestoreIngredientLines(
        ObservableCollection<IngredientLineRow> rows,
        IReadOnlyList<IngredientLineSnapshot> states,
        decimal baseWeight,
        IReadOnlyList<RawMaterial> ingredients)
    {
        rows.Clear();
        foreach (var state in states)
        {
            rows.Add(IngredientLineRow.Restore(
                id => ingredients.FirstOrDefault(m => m.Id == id),
                baseWeight,
                state.IngredientId,
                state.Grams,
                state.IsFormulaBase,
                state.AuthoredBakersPercent));
        }
    }
}
