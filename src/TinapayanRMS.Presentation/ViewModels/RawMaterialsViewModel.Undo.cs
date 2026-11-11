using System.Reactive.Linq;
using ReactiveUI;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.ViewModels;

// What the form's undo history and the unsaved-changes question ask of this screen (RecordFormViewModel).
public sealed partial class RawMaterialsViewModel
{
    /// <summary>Save offers itself whenever the form holds something the saved material doesn't.</summary>
    private void WireUnsavedChanges()
    {
        // Save offers itself only when the form holds something the saved material doesn't. Measured on every
        // change rather than only when an undo step settles: a step closes on the way out of a field, and a Save
        // button still disabled under the cursor would swallow the very click that enabled it. A grid's add row
        // is the same story the other way round - the grid raises nothing when it commits, so the step stays open.
        this.Changed.Subscribe(_ => History.RefreshDirty());
        WatchForSave(UnitRows);
        WatchForSave(BrandPriceRows);
        WatchForSave(AddInEdits);
    }

    protected override RawMaterialFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        Type = EditingType,
        CategoryId = EditingCategory?.Id,
        Source = EditingSource,
        CanChangeTypeAndSource = CanChangeTypeAndSource,
        BaseUnitId = EditingBaseUnit?.Id,
        WeightLossPercent = EditingWeightLossPercent,
        SubAssemblyId = EditingSubAssembly?.Id,
        LinkedToId = EditingLinkedTo?.Id,
        WeightSharePercent = EditingWeightSharePercent,
        CostSharePercent = EditingCostSharePercent,
        CostBufferPercent = EditingCostBufferPercent,
        TargetCostBufferPercent = EditingTargetCostBufferPercent,
        PreferredBrand = EditingPreferredBrand,
        BufferedCost = EditingBufferedCost,
        BufferedCostUnitKey = BufferedCostUnit?.Key,
        UnitRows =
        [
            .. UnitRows.Select(r => new UnitRowState(
                r.Unit.Id, r.Unit.RawMaterialId, r.UnitOfMeasureId, r.BaseUnitsPerUnit, r.BaseUnitAbbreviation)),
        ],
        BrandPrices = [.. BrandPriceRows.Select(r => new BrandPriceState(r.Brand, r.UnitKey, r.Price))],
        AddIns = [.. AddInEdits.Select(l => new IngredientLineState(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
    };

    /// <summary>
    /// Puts the form back as <paramref name="snapshot"/> had it. The rows are rebuilt rather than written into: an
    /// IngredientLineRow's setters drop a typed percentage, and a unit row writes straight through to the stored
    /// RawMaterialUnit that Save hands the repository, so each unit comes back as a fresh entity carrying its own
    /// Id - one that lost it would be saved as a new unit and the stored one deleted. The one recompute runs at the
    /// end, outside the guard, so the costs and captions are worked out once from the form that was put back.
    /// </summary>
    protected override void RestoreSnapshot(RawMaterialFormSnapshot snapshot, RawMaterialFormSnapshot? settledThen)
    {
        ErrorMessage = null;

        ApplyRestore(() => _updates.Batch(() =>
        {
            SelectedUnitRow = null;
            SelectedBrandPriceRow = null;
            SelectedAddInEdit = null;

            _editingId = snapshot.EditingId;
            CanChangeTypeAndSource = snapshot.CanChangeTypeAndSource;
            EditingName = snapshot.Name;
            ShowType(snapshot.Type);
            EditingCategory = snapshot.CategoryId is { } categoryId ? Categories.FirstOrDefault(c => c.Id == categoryId) : null;
            ShowSource(snapshot.Source);
            ShowBaseUnit(snapshot.BaseUnitId is { } baseUnitId ? UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId) : null);
            EditingWeightLossPercent = snapshot.WeightLossPercent;
            IsChangingSubAssembly = false;
            ShowSubAssembly(snapshot.SubAssemblyId is { } subAssemblyId ? SubAssemblyOptions.FirstOrDefault(s => s.Id == subAssemblyId) : null);
            EditingLinkedTo = snapshot.LinkedToId is { } linkedToId ? Ingredients.FirstOrDefault(m => m.Id == linkedToId) : null;
            EditingWeightSharePercent = snapshot.WeightSharePercent;
            EditingCostSharePercent = snapshot.CostSharePercent;
            EditingCostBufferPercent = snapshot.CostBufferPercent;
            EditingTargetCostBufferPercent = snapshot.TargetCostBufferPercent;
            EditingPreferredBrand = snapshot.PreferredBrand;
            EditingBufferedCost = snapshot.BufferedCost;

            // The alternative units, as fresh entities carrying their ids.
            SyncUnitRows(() =>
            {
                UnitRows.Clear();
                foreach (var unit in snapshot.UnitRows)
                {
                    UnitRows.Add(new RawMaterialUnitRow(
                        new RawMaterialUnit
                        {
                            Id = unit.Id,
                            RawMaterialId = unit.RawMaterialId,
                            UnitOfMeasureId = unit.UnitOfMeasureId ?? 0,
                            UnitOfMeasure = unit.UnitOfMeasureId is { } id ? FindUnitOfMeasure(id) : null,
                            BaseUnitsPerUnit = unit.BaseUnitsPerUnit,
                        },
                        unit.BaseUnitAbbreviation,
                        FindUnitOfMeasure,
                        StandardMultiplierOf));
                }
            });

            BrandPriceRows.Clear();
            foreach (var price in snapshot.BrandPrices)
            {
                BrandPriceRows.Add(new BrandPriceRow(this) { Brand = price.Brand, UnitKey = price.UnitKey, Price = price.Price });
            }

            AddInEdits.Clear();
            foreach (var line in snapshot.AddIns)
            {
                AddInEdits.Add(IngredientLineRow.Restore(
                    id => Ingredients.FirstOrDefault(m => m.Id == id),
                    baseWeight: 0m,
                    line.IngredientId,
                    line.Grams,
                    line.IsFormulaBase,
                    line.AuthoredBakersPercent));
            }
        }));

        // The Per picker's choices are rebuilt from the units above, so the one that was picked is found again here
        // rather than in the guard: RebuildUnitViews replaces the list BufferedCostUnit is chosen from.
        RebuildUnitViews();
        BufferedCostUnit = snapshot.BufferedCostUnitKey is { } unitKey
            ? BufferedCostUnitOptions.FirstOrDefault(o => o.Key == unitKey)
            : null;

        IngredientLineRow.Resolve(AddInEdits, Ingredients.ToDictionary(m => m.Id));
        RefreshBrandPriceRows();
        UpdatePurchaseCostTexts();

        // The Cost Buffer % box shows TypedCostBufferPercent, raised as the buffer changes: an undo that leaves the buffer
        // as it was doesn't, and without this the box would keep the percent from before the undo (undoing a
        // Basis pick, say).
        this.RaisePropertyChanged(nameof(TypedCostBufferPercent));
        RecomputeBomTotals();
    }

    /// <summary>
    /// Nothing is recorded until a material's details have arrived, or while the screen is being written to wholesale:
    /// the brand prices and the BOM land a moment after the pick (AreDetailsReady), and a step taken before they do would
    /// put the empty grids back over them.
    /// </summary>
    protected override bool CanRecordUndo => AreDetailsReady && !IsBusyApplying;

    protected override string UnsavedChangesName =>
        !string.IsNullOrWhiteSpace(EditingName) ? EditingName.Trim() : SelectedRawMaterial?.Name ?? "the new raw material";

    protected override async Task SaveFormAsync() => await SaveCommand.Execute();

    /// <summary>
    /// Don't Save on leaving the screen: the form goes back to the material as it was last saved, read again, since its
    /// alternative units write through to the copy the list holds. A new material was never saved, so it goes back to a
    /// blank one.
    /// </summary>
    protected override async Task RevertToSavedAsync()
    {
        if (SelectedRawMaterial is { } material)
        {
            await ReloadSavedRawMaterialAsync(material.Id);
        }
        else
        {
            ClearForm();
        }
    }
}
