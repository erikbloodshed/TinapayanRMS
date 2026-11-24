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
    public static SectionFormSnapshot Capture(ProductUsageForm form, IList<ProductUsageEdit> usages)
    {
        List<ProductUsageEdit> sections = [.. usages];
        IngredientLineSnapshot Line(IngredientLineRow row) =>
            new(row.IngredientId, row.Grams, row.IsFormulaBase, row.AuthoredBakersPercent, SectionFormSnapshot.IndexOfKey(sections, row.SectionKey));
        return Capture(form, usages, Line);
    }

    private static SectionFormSnapshot Capture(ProductUsageForm form, IList<ProductUsageEdit> usages, Func<IngredientLineRow, IngredientLineSnapshot> line) => new()
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
        Lines = [.. form.FormLines.Select(line)],
        AddIns = [.. form.FormAddIns.Select(line)],
        FormulaBase = form.FormFormulaBase,
        AuthoredField = form.AuthoredField,
        WeightPerPiece = form.EditingUsageWeightPerPiece,
        Yield = form.EditingUsageYield,
    };

    /// <summary>
    /// Puts back <paramref name="snapshot"/>'s tables, formula base and piece size, each row priced from
    /// <paramref name="ingredients"/>, the ingredients as the screen last loaded them, and each that is another section from
    /// <paramref name="draft"/>'s sections, already put back, by its place among them. Written, never typed, so nothing a
    /// change of them sets off runs.
    /// </summary>
    public static void RestoreTables(ProductUsageForm form, SectionFormSnapshot snapshot, IReadOnlyList<RawMaterial> ingredients, ProductRecipeDraft draft)
    {
        var editing = snapshot.EditingUsageIndex is { } index && index < draft.UsageEdits.Count ? draft.UsageEdits[index] : null;
        ShowSections(form, editing, draft);
        form.FormFormulaBase = snapshot.FormulaBase;
        RestoreIngredientLines(form.FormLines, snapshot.Lines, snapshot.FormulaBase, ingredients, draft);
        RestoreIngredientLines(form.FormAddIns, snapshot.AddIns, snapshot.FormulaBase, ingredients, draft);
        form.RestorePieceSize(snapshot.AuthoredField, snapshot.WeightPerPiece, snapshot.Yield);
    }

    /// <summary>
    /// What the form on <paramref name="editing"/>, or adding one with null, needs of the recipe's other sections: which a
    /// line can be, and what they use of it.
    /// </summary>
    public static void ShowSections(ProductUsageForm form, ProductUsageEdit? editing, ProductRecipeDraft draft)
    {
        form.Sections.ShowPicks(RecipeSectionLines.For(draft, editing));
        form.Sections.ShowUsedElsewhere(editing is null ? (0m, []) : RecipeSectionLines.UsedElsewhere(draft, editing));
    }

    /// <summary><paramref name="usage"/>'s lines that are other sections as rows of its form, each at its place, priced as the recipe prices them.</summary>
    public static IEnumerable<(int Position, IngredientLineRow Row)> SectionRows(ProductUsageEdit usage, ProductRecipeDraft draft) =>
        usage.SectionLines.Select(l => (l.Position, new IngredientLineRow(
            RecipeSectionLines.Find(draft, l.Key) ?? new LineSection(l.Key, "A section no longer in the recipe", 0m, 0m), l.Grams, l.AuthoredBakersPercent)));

    /// <summary>
    /// The rows of a form's table that are other sections, as the section's lines that are (ProductUsageEdit.SectionLines);
    /// one with no grams, or a section on two rows, is refused with a message naming <paramref name="madeInto"/>.
    /// </summary>
    public static IReadOnlyList<UsedSectionEdit> SectionLinesOf(IEnumerable<IngredientLineRow> rows, string madeInto) =>
        [.. IngredientLineRow.CompleteSectionLines(rows, madeInto).Select(l => new UsedSectionEdit(l.Section.Key, l.Grams, l.AuthoredBakersPercent, l.Position))];

    /// <summary>
    /// The recipe's sections changed (a load repricing them, a linked one's lines saved elsewhere, an undo): the open form's
    /// rows that are sections follow their costs, its picker the sections there are, and its piece-size figure not typed
    /// what the others now use of it, as it follows the lines.
    /// </summary>
    public static void FollowSections(ProductUsageForm form, ProductRecipeDraft draft)
    {
        if (!form.IsUsageFormOpen)
        {
            return;
        }

        IngredientLineRow.ResolveSections(form.FormLines.Concat(form.FormAddIns), key => RecipeSectionLines.Find(draft, key));
        form.Sections.ShowPicks(RecipeSectionLines.For(draft, form.EditingUsage));
        if (form.EditingUsage is { } editing && form.Sections.ShowUsedElsewhere(RecipeSectionLines.UsedElsewhere(draft, editing)))
        {
            form.FollowPieceSize();
        }
    }

    private static void RestoreIngredientLines(
        ObservableCollection<IngredientLineRow> rows,
        IReadOnlyList<IngredientLineSnapshot> states,
        decimal baseWeight,
        IReadOnlyList<RawMaterial> ingredients,
        ProductRecipeDraft draft)
    {
        rows.Clear();

        // A row whose section the recipe no longer had when the step was taken (-1) goes, rather than come back as grams with
        // nothing picked.
        foreach (var state in states.Where(s => s.SectionIndex is not < 0))
        {
            var section = state.SectionIndex is { } at && at < draft.UsageEdits.Count ? RecipeSectionLines.Find(draft, draft.UsageEdits[at].Key) : null;
            rows.Add(IngredientLineRow.Restore(
                id => ingredients.FirstOrDefault(m => m.Id == id),
                baseWeight,
                state.IngredientId,
                state.Grams,
                state.IsFormulaBase,
                state.AuthoredBakersPercent,
                section));
        }
    }
}
