using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

// The base unit, the alternative units, and the units a brand is bought in.
public sealed partial class RawMaterialsViewModel
{
    /// <summary>What the base unit is called, as the form and the saved material have it.</summary>
    [MemberNotNull(nameof(_baseUnitName), nameof(_baseUnitAbbreviation), nameof(_savedBaseUnitText))]
    private void WireBaseUnitNames()
    {
        _baseUnitName = this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit,
                (type, baseUnit) => BaseUnitText.NameOf(type, baseUnit))
            .ToProperty(this, x => x.BaseUnitName, "gram");

        _baseUnitAbbreviation = this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit,
                (type, baseUnit) => BaseUnitText.AbbreviationOf(type, baseUnit))
            .ToProperty(this, x => x.BaseUnitAbbreviation, "g");

        _savedBaseUnitText = this.WhenAnyValue(x => x.EditingType, x => x.SelectedRawMaterial, SavedBaseUnitTextOf)
            .ToProperty(this, x => x.SavedBaseUnitText, "gram (g)");
    }

    /// <summary>The unit lists that name the base unit follow it.</summary>
    private void WireUnitViews()
    {
        // The alternative units and the purchase unit choices name the base unit: worked out again whenever it or the type
        // changes, a load's or an undo's included, as they're only ever derived from them.
        this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit)
            .Subscribe(_ => RebuildUnitViews());
    }

    /// <summary>A unit's multiplier changed reprices the brands bought in it.</summary>
    private void WireUnitRows()
    {
        // A unit's multiplier changed in the Alternative Units grid reprices the brands bought in it straight away,
        // and the add row goes once there are two units.
        UnitRows.ToObservableChangeSet()
            .AutoRefresh(r => r.UnitOfMeasureId)
            .AutoRefresh(r => r.BaseUnitsPerUnit)
            .Where(_ => !_syncingUnitRows && !IsBusyApplying)
            .Subscribe(_ => OnUnitRowsEdited());
    }

    /// <summary>Deleting an alternative unit.</summary>
    [MemberNotNull(nameof(DeleteUnitCommand))]
    private void WireUnitCommands()
    {
        var hasSelectedUnitRow = this.WhenAnyValue(x => x.SelectedUnitRow).Select(row => row is not null);

        // Nothing to confirm: the unit only leaves the list, and is deleted when the material is saved.
        DeleteUnitCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                var removed = SelectedUnitRow!.Unit;
                _brandAddedUnits.Remove(removed);
                ReplaceUnitRows([.. UnitRows.Select(r => r.Unit).Where(u => !ReferenceEquals(u, removed))], selectedUnit: null);
                RefreshBrandPriceRows();
                UpdatePurchaseCostTexts();
            }),
            hasSelectedUnitRow);
    }

    /// <summary>The form's alternative units, from <paramref name="material"/>'s saved ones (none for a new material).</summary>
    private void LoadUnitRows(RawMaterial? material)
    {
        // Copies, since the form edits its units in place and the cached material must stay as saved.
        // Each points at the Units of Measure list's own object, which refreshes keep current.
        var units = (material?.Units ?? []).Select(u => new RawMaterialUnit
        {
            Id = u.Id,
            RawMaterialId = u.RawMaterialId,
            UnitOfMeasureId = u.UnitOfMeasureId,
            UnitOfMeasure = UnitsOfMeasure.FirstOrDefault(m => m.Id == u.UnitOfMeasureId) ?? u.UnitOfMeasure,
            BaseUnitsPerUnit = u.BaseUnitsPerUnit,
            Description = u.Description,
        });

        _brandAddedUnits.Clear();
        ReplaceUnitRows([.. units], selectedUnit: null);
        RefreshBrandPriceRows();
        UpdateCanChangeBaseUnit();
    }

    /// <summary>
    /// A packaging material's unit picked on a brand row before it was made the base unit: the rows priced in it are now
    /// priced in the base unit, and the alternative unit the pick added goes with them (OnUnitPicked). Kept, Save would
    /// refuse it as the base unit. One added in the Alternative Units grid is left for Save to refuse.
    /// </summary>
    private void MoveBrandRowsOntoTheBaseUnit()
    {
        if (EditingType != RawMaterialType.Packaging
            || EditingBaseUnit is not { } baseUnit
            || !_brandAddedUnits.Any(u => u.UnitOfMeasureId == baseUnit.Id))
        {
            return;
        }

        foreach (var row in BrandPriceRows.Where(r => r.UnitKey == baseUnit.Id).ToList())
        {
            row.UnitKey = PurchaseUnitChoice.BaseUnitKey;
        }
    }

    /// <summary>Shows the base unit on the form's alternative units and in the purchase unit choices.</summary>
    private void RebuildUnitViews()
    {
        UpdateUnitRowsBaseUnit();
        RebuildPurchaseUnitOptions();
    }

    /// <summary>
    /// Tells each row the base unit, which its multiplier is in, without replacing the rows the grid is showing. A standard
    /// unit's fixed multiplier follows it: a new packaging material's inch is 25.4 over millimetres, 2.54 over centimetres.
    /// </summary>
    private void UpdateUnitRowsBaseUnit()
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit);
        foreach (var row in UnitRows)
        {
            row.BaseUnitAbbreviation = baseUnitAbbreviation;
            row.ApplyStandardMultiplier();
        }
    }

    private void ReplaceUnitRows(IReadOnlyList<RawMaterialUnit> units, RawMaterialUnit? selectedUnit)
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit);
        SyncUnitRows(() =>
        {
            SelectedUnitRow = null;
            UnitRows.Clear();
            UnitRows.AddRange(units.Select(u => new RawMaterialUnitRow(u, baseUnitAbbreviation, FindUnitOfMeasure, StandardMultiplierOf)));
            SelectedUnitRow = UnitRows.FirstOrDefault(r => ReferenceEquals(r.Unit, selectedUnit));
        });
    }

    /// <summary>Changes the unit rows for the user without that counting as their edit (OnUnitRowsEdited).</summary>
    private void SyncUnitRows(Action change)
    {
        _syncingUnitRows = true;
        try
        {
            change();
        }
        finally
        {
            _syncingUnitRows = false;
        }
    }

    private UnitOfMeasure? FindUnitOfMeasure(int id) => UnitsOfMeasure.FirstOrDefault(u => u.Id == id);

    /// <summary>A standard unit's fixed multiplier for the material being edited (StandardUnitRules), or null for one that's typed.</summary>
    private decimal? StandardMultiplierOf(UnitOfMeasure unit) => StandardUnitRules.BaseUnitsPerUnit(unit, EditingType, EditingBaseUnit);

    /// <summary>
    /// What an edit in the Alternative Units grid does: a brand bought in a unit whose multiplier changed there is repriced
    /// straight away.
    /// </summary>
    private void OnUnitRowsEdited()
    {
        RefreshBrandPriceRows();
        UpdatePurchaseCostTexts();
    }

    /// <summary>
    /// The checks the Alternative Units form's Apply used to make, now that units are typed straight into the grid: every row has a
    /// unit and a positive multiplier, none is the base unit or repeats another's unit. Throws
    /// InvalidOperationException for the first problem, which RunSafelyAsync shows.
    /// </summary>
    private void EnsureValidUnitRows()
    {
        var units = UnitRows.Select(r => r.Unit).ToList();
        foreach (var unit in units)
        {
            if (unit.UnitOfMeasureId <= 0)
            {
                throw new BusinessRuleException("Pick a unit for every alternative unit, or remove the empty row.");
            }

            if (unit.BaseUnitsPerUnit <= 0)
            {
                // A unit added by picking it on a brand row gets its multiplier from that row's quantity.
                var where = _brandAddedUnits.Contains(unit) ? "Qty per Purchase Unit" : "Alternative Units";
                throw new BusinessRuleException($"Enter how many {BaseUnitAbbreviation} are in one {unit.UnitOfMeasure?.Name ?? "unit"} ({where}).");
            }

            var validation = _unitValidator.Validate(unit);
            if (!validation.IsValid)
            {
                throw new BusinessRuleException(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            }

            RawMaterialRules.EnsureNotTheBaseUnit(EditingType == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null, unit.UnitOfMeasureId);
            RawMaterialRules.EnsureUnitNotAlreadyAdded(units.Any(u => !ReferenceEquals(u, unit) && u.UnitOfMeasureId == unit.UnitOfMeasureId));
        }
    }

    private void UpdateCanChangeBaseUnit() =>
        CanChangeBaseUnit = SelectedRawMaterial is null
            || (SelectedRawMaterial.Units.Count == 0 && _savedBrandPriceCount == 0 && PriceHistory.Count == 0);

    /// <summary>
    /// The usage example's unit choices for <paramref name="material"/>. Keeps the unit picked before
    /// (<paramref name="kept"/>, matched by id — the base unit's is null) when there was one; otherwise
    /// starts on its largest alternative unit, else its base unit.
    /// </summary>
    private void ReplaceExampleUnitOptions(RawMaterial? material, RawMaterialUnitOption? kept)
    {
        ExampleUnitOptions.Clear();
        ExampleUnitOptions.AddRange(RawMaterialUnitOption.For(material));
        ExampleUnit = (kept is null ? null : ExampleUnitOptions.FirstOrDefault(o => o.UnitId == kept.UnitId))
            ?? ExampleUnitOptions.Skip(1).FirstOrDefault()
            ?? ExampleUnitOptions.FirstOrDefault();
    }

    /// <summary>
    /// The Purchase Cost Setup grid's unit choices: the base unit, then every unit of measure but one that is (or is named
    /// like) the base unit. Left alone when nothing changed, so the grid's open unit cell keeps its list.
    /// </summary>
    private void RebuildPurchaseUnitOptions()
    {
        var baseUnitId = EditingType == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null;
        List<PurchaseUnitChoice> choices =
        [
            new(PurchaseUnitChoice.BaseUnitKey, BaseUnitText.NameOf(EditingType, EditingBaseUnit), BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit)),
            .. UnitsOfMeasure
                .Where(u => u.Id != baseUnitId && !IsNamedLikeTheBaseUnit(u))
                .Select(u => new PurchaseUnitChoice(u.Id, u.Name, u.Abbreviation)),
        ];

        if (!choices.SequenceEqual(PurchaseUnitOptions))
        {
            PurchaseUnitOptions.Clear();
            PurchaseUnitOptions.AddRange(choices);
        }
    }

    private bool IsNamedLikeTheBaseUnit(UnitOfMeasure unit) =>
        string.Equals(unit.Name, BaseUnitText.NameOf(EditingType, EditingBaseUnit), StringComparison.OrdinalIgnoreCase)
        || string.Equals(unit.Abbreviation, BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit), StringComparison.OrdinalIgnoreCase);

    RawMaterialUnit? IBrandPriceRowHost.FindUnit(int unitOfMeasureId) =>
        UnitRows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unitOfMeasureId)?.Unit;

    bool IBrandPriceRowHost.IsMultiplierFixed(int unitOfMeasureId) =>
        UnitRows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unitOfMeasureId) is { CanTypeMultiplier: false };

    void IBrandPriceRowHost.SetMultiplier(int unitOfMeasureId, decimal baseUnitsPerUnit)
    {
        if (UnitRows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unitOfMeasureId) is { } row)
        {
            SyncUnitRows(() => row.BaseUnitsPerUnit = baseUnitsPerUnit);
            RefreshBrandPriceRows();
            UpdatePurchaseCostTexts();
        }
    }

    decimal? IBrandPriceRowHost.BufferedCostPerBaseUnit =>
        EditingBufferedCost is > 0 ? EditingBufferedCost
        : DearestLiveCost() is { } cost ? RawMaterialRules.BufferedCost(cost, EditingCostBufferPercent ?? 0m)
        : null;

    /// <summary>
    /// What picking a unit on a brand row does: one the material doesn't have yet is added as an alternative unit, with its
    /// multiplier typed as the row's quantity; and one added that way that no row is in
    /// any more goes. <paramref name="row"/> may still be the grid's add row, which isn't in BrandPriceRows yet.
    /// </summary>
    void IBrandPriceRowHost.OnUnitPicked(BrandPriceRow row)
    {
        DropUnusedAddedUnits(row);

        if (row.UnitKey is not { } key || key == PurchaseUnitChoice.BaseUnitKey || ((IBrandPriceRowHost)this).FindUnit(key) is not null)
        {
            return;
        }

        var unit = new RawMaterialUnit { UnitOfMeasureId = key, UnitOfMeasure = FindUnitOfMeasure(key) };
        _brandAddedUnits.Add(unit);
        ReplaceUnitRows([.. UnitRows.Select(r => r.Unit), unit], SelectedUnitRow?.Unit);
        RefreshBrandPriceRows();
    }

    /// <summary>
    /// Takes back out of the alternative units each one added by picking it on a brand row that no row, nor
    /// <paramref name="pickedRow"/> (a row still being added), is in any more.
    /// </summary>
    private void DropUnusedAddedUnits(BrandPriceRow? pickedRow)
    {
        var usedKeys = BrandPriceRows.Append(pickedRow).Select(r => r?.UnitKey).ToHashSet();
        var unused = _brandAddedUnits.Where(u => !usedKeys.Contains(u.UnitOfMeasureId)).ToList();
        if (unused.Count == 0)
        {
            return;
        }

        foreach (var unit in unused)
        {
            _brandAddedUnits.Remove(unit);
        }

        ReplaceUnitRows([.. UnitRows.Select(r => r.Unit).Where(u => !unused.Contains(u))], SelectedUnitRow?.Unit);
        RefreshBrandPriceRows();
        UpdatePurchaseCostTexts();
    }

    /// <summary>
    /// SavedBaseUnitText. The saved base unit is looked up in the Units of Measure list, as the form's picker is, since
    /// the list keeps it current through a rename; the material's own BaseUnit is the fallback before the list loads.
    /// </summary>
    private string SavedBaseUnitTextOf(RawMaterialType type, RawMaterial? saved)
    {
        if (type != RawMaterialType.Packaging)
        {
            return "gram (g)";
        }

        var unit = saved?.BaseUnitId is { } id ? UnitsOfMeasure.FirstOrDefault(u => u.Id == id) ?? saved.BaseUnit : null;
        return unit is null ? "—" : $"{unit.Name} ({unit.Abbreviation})";
    }
}
