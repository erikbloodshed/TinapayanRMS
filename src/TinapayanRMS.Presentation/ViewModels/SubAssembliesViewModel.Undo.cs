using System.Reactive.Linq;

namespace TinapayanRMS.Presentation.ViewModels;

// What the form's undo history and the unsaved-changes question ask of this screen (RecordFormViewModel).
public sealed partial class SubAssembliesViewModel
{
    /// <summary>Save offers itself whenever the form holds something the saved sub-assembly doesn't.</summary>
    private void WireUnsavedChanges()
    {
        // Save offers itself only when the form holds something the saved sub-assembly doesn't. Re-read on every
        // change rather than only when an undo step settles: a step closes on the way out of a field, and a Save
        // button still disabled under the cursor would swallow the very click that enabled it.
        // The packaging set's own changes too: a quantity typed into one of its lines changes the set's cost, the one
        // figure that moves while the line's cell still has the cursor.
        this.Changed.Subscribe(_ => History.RefreshDirty());
        PackagingSet.Changed.Subscribe(_ => History.RefreshDirty());
        WatchForSave(Lines);
        WatchForSave(PackagingSet.Lines);
    }

    protected override SubAssemblyFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        RoleId = EditingRole?.Id,
        Lines = [.. Lines.Select(l => new IngredientLineState(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
        PackagingLines = PackagingSet.Capture(),
        FormulaBase = _formulaBase,
    };

    /// <summary>
    /// Puts the form back as <paramref name="snapshot"/> had it. The lines are rebuilt rather than written into —
    /// IngredientLineRow's setters drop a typed percentage — and the one recompute runs at the end, outside the
    /// guard, so the totals and the formula base are worked out once from the form that was put back.
    /// </summary>
    protected override void RestoreSnapshot(SubAssemblyFormSnapshot snapshot, SubAssemblyFormSnapshot? settledThen)
    {
        ErrorMessage = null;

        // A load still on its way would otherwise land on top of what's being put back.
        _formLoadVersion++;

        ApplyRestore(() => _updates.Batch(() =>
        {
            SelectedLine = null;
            PackagingSet.SelectedLine = null;

            _editingId = snapshot.EditingId;
            EditingName = snapshot.Name;
            EditingRole = snapshot.RoleId is { } roleId ? Roles.FirstOrDefault(r => r.Id == roleId) : null;

            _formulaBase = snapshot.FormulaBase;
            Lines.Clear();
            foreach (var line in snapshot.Lines)
            {
                Lines.Add(IngredientLineRow.Restore(
                    id => Ingredients.FirstOrDefault(m => m.Id == id),
                    snapshot.FormulaBase,
                    line.IngredientId,
                    line.Grams,
                    line.IsFormulaBase,
                    line.AuthoredBakersPercent));
            }

            PackagingSet.Restore(snapshot.PackagingLines);
        }));

        // Once more, in this order, from the form put back: an undo that leaves a table as it was asks for none of these.
        ResolveLines();
        PackagingSet.ResolveRows();
        RecomputeFormulaBase();
        RecomputeTotals();
        PackagingSet.RecomputeCost();
    }

    protected override string UnsavedChangesName =>
        !string.IsNullOrWhiteSpace(EditingName) ? EditingName.Trim() : SelectedRow?.Name ?? "the new sub-assembly";

    protected override async Task SaveFormAsync() => await SaveCommand.Execute();

    /// <summary>
    /// Don't Save on leaving the screen: the form goes back to the sub-assembly as it was last saved, its lines read again.
    /// A new one (or a copy) was never saved, so it goes back to a blank one.
    /// </summary>
    protected override Task RevertToSavedAsync() => LoadIntoFormAsync(SelectedRow);
}
