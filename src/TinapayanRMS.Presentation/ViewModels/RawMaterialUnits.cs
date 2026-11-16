using System.Collections.ObjectModel;
using DynamicData;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>What the Alternative Units tab asks of its screen, on top of what any part of its form asks.</summary>
public interface IRawMaterialUnitsHost : IFormPartHost
{
    /// <summary>The form's type: a packaging material's base unit is its own, an ingredient's the gram.</summary>
    RawMaterialType EditingType { get; }

    /// <summary>The form's base unit, for a packaging material: what the units' multipliers are in.</summary>
    UnitOfMeasure? EditingBaseUnit { get; }

    /// <summary>The base unit's abbreviation, as the form shows it: "g", or a packaging material's own.</summary>
    string BaseUnitAbbreviation { get; }

    /// <summary>The units changed (one taken out): the brands bought in them are priced again.</summary>
    void UnitsChanged();
}

/// <summary>
/// A material's alternative units on the Raw Materials screen (through RawMaterialsViewModel.Units; AlternativeUnits draws
/// them): each a unit of measure and how many base units it holds, typed straight into the table, saved with the material.
/// A unit picked on a brand row that the material didn't have is added here too, with its multiplier typed as the row's
/// quantity, and goes again once no row is in it (the Cost Setup tab, RawMaterialPurchaseCost, asks for both).
/// </summary>
public sealed class RawMaterialUnits : ObservableState
{
    private readonly IRawMaterialUnitsHost _host;
    private readonly IReadOnlyList<UnitOfMeasure> _unitsOfMeasure;
    private readonly IValidator<RawMaterialUnit> _unitValidator;

    /// <summary>
    /// The alternative units added by picking, on a brand row, a unit the material didn't have. Each goes again once no row is
    /// in it, so trying units out leaves nothing behind.
    /// </summary>
    private readonly HashSet<RawMaterialUnit> _brandAddedUnits = [];

    /// <param name="host">The screen: the form's base unit, and the brands to price again when a unit goes.</param>
    /// <param name="unitsOfMeasure">Every unit of measure, as the screen last loaded them, which a row's unit is one of.</param>
    public RawMaterialUnits(IRawMaterialUnitsHost host, IReadOnlyList<UnitOfMeasure> unitsOfMeasure, IValidator<RawMaterialUnit> unitValidator)
    {
        _host = host;
        _unitsOfMeasure = unitsOfMeasure;
        _unitValidator = unitValidator;

        // Nothing to confirm: the unit only leaves the list, and is deleted when the material is saved.
        DeleteCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() =>
            {
                var removed = SelectedRow!.Unit;
                _brandAddedUnits.Remove(removed);
                Replace([.. Rows.Select(r => r.Unit).Where(u => !ReferenceEquals(u, removed))], selectedUnit: null);
                host.UnitsChanged();
            }),
            () => SelectedRow is not null);
    }

    /// <summary>The form's alternative units, saved or not. Their units are the form's own copies, edited in place.</summary>
    public ObservableCollection<RawMaterialUnitRow> Rows { get; } = [];

    /// <summary>The row picked, which Remove takes out.</summary>
    public RawMaterialUnitRow? SelectedRow
    {
        get;
        set => RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The row the table's add row edits: one that names its unit as soon as it's picked and shows the current base unit,
    /// which a row the table made itself couldn't.
    /// </summary>
    public Func<RawMaterialUnitRow> NewRow => () => new RawMaterialUnitRow(new RawMaterialUnit(), _host.BaseUnitAbbreviation, FindUnitOfMeasure, StandardMultiplierOf);

    /// <summary>Removes the unit picked from the form; Save deletes it.</summary>
    public ScreenCommand DeleteCommand { get; }

    /// <summary>Set while the screen itself replaces or changes the rows, so only the user's edits count as edits.</summary>
    public bool IsSyncing { get; private set; }

    /// <summary>The form's units, from <paramref name="material"/>'s saved ones (none for a new material).</summary>
    public void Load(RawMaterial? material)
    {
        // Copies, since the form edits its units in place and the cached material must stay as saved.
        // Each points at the Units of Measure list's own object, which refreshes keep current.
        var units = (material?.Units ?? []).Select(u => new RawMaterialUnit
        {
            Id = u.Id,
            RawMaterialId = u.RawMaterialId,
            UnitOfMeasureId = u.UnitOfMeasureId,
            UnitOfMeasure = _unitsOfMeasure.FirstOrDefault(m => m.Id == u.UnitOfMeasureId) ?? u.UnitOfMeasure,
            BaseUnitsPerUnit = u.BaseUnitsPerUnit,
            Description = u.Description,
        });

        _brandAddedUnits.Clear();
        Replace([.. units], selectedUnit: null);
    }

    /// <summary>The rows as <paramref name="units"/>, with <paramref name="selectedUnit"/>'s picked, without that counting as the user's edit.</summary>
    public void Replace(IReadOnlyList<RawMaterialUnit> units, RawMaterialUnit? selectedUnit)
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(_host.EditingType, _host.EditingBaseUnit);
        Sync(() =>
        {
            SelectedRow = null;
            Rows.Clear();
            Rows.AddRange(units.Select(u => new RawMaterialUnitRow(u, baseUnitAbbreviation, FindUnitOfMeasure, StandardMultiplierOf)));
            SelectedRow = Rows.FirstOrDefault(r => ReferenceEquals(r.Unit, selectedUnit));
        });
    }

    /// <summary>Changes the rows for the user without that counting as their edit.</summary>
    public void Sync(Action change)
    {
        IsSyncing = true;
        try
        {
            change();
        }
        finally
        {
            IsSyncing = false;
        }
    }

    /// <summary>
    /// Tells each row the base unit, which its multiplier is in, without replacing the rows the table is showing. A standard
    /// unit's fixed multiplier follows it: a new packaging material's inch is 25.4 over millimetres, 2.54 over centimetres.
    /// </summary>
    public void UpdateBaseUnit()
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(_host.EditingType, _host.EditingBaseUnit);
        foreach (var row in Rows)
        {
            row.BaseUnitAbbreviation = baseUnitAbbreviation;
            row.ApplyStandardMultiplier();
        }
    }

    /// <summary>Each row points at its refreshed unit of measure, so one renamed in Settings shows its new name.</summary>
    public void RefreshUnitsOfMeasure()
    {
        foreach (var row in Rows)
        {
            row.RefreshUnitOfMeasure();
        }
    }

    public UnitOfMeasure? FindUnitOfMeasure(int id) => _unitsOfMeasure.FirstOrDefault(u => u.Id == id);

    /// <summary>A standard unit's fixed multiplier for the material being edited (StandardUnitRules), or null for one that's typed.</summary>
    public decimal? StandardMultiplierOf(UnitOfMeasure unit) => StandardUnitRules.BaseUnitsPerUnit(unit, _host.EditingType, _host.EditingBaseUnit);

    /// <summary>The material's unit of <paramref name="unitOfMeasureId"/>, saved or not, or null when it has none.</summary>
    public RawMaterialUnit? FindUnit(int unitOfMeasureId) => Rows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unitOfMeasureId)?.Unit;

    /// <summary>Whether the unit of <paramref name="unitOfMeasureId"/> is a standard one, whose multiplier is fixed rather than typed.</summary>
    public bool IsMultiplierFixed(int unitOfMeasureId) =>
        Rows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unitOfMeasureId) is { CanTypeMultiplier: false };

    /// <summary>The multiplier of the unit of <paramref name="unitOfMeasureId"/>, typed as a brand row's quantity: not the user's edit of this table. False when there's no such unit.</summary>
    public bool SetMultiplier(int unitOfMeasureId, decimal baseUnitsPerUnit)
    {
        if (Rows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unitOfMeasureId) is not { } row)
        {
            return false;
        }

        Sync(() => row.BaseUnitsPerUnit = baseUnitsPerUnit);
        return true;
    }

    /// <summary>Whether a unit of <paramref name="unitOfMeasureId"/> was added by picking it on a brand row.</summary>
    public bool IsBrandAdded(int unitOfMeasureId) => _brandAddedUnits.Any(u => u.UnitOfMeasureId == unitOfMeasureId);

    /// <summary>A unit of <paramref name="unitOfMeasureId"/> the material didn't have, picked on a brand row: added, its multiplier to come from the row.</summary>
    public void AddBrandUnit(int unitOfMeasureId)
    {
        var unit = new RawMaterialUnit { UnitOfMeasureId = unitOfMeasureId, UnitOfMeasure = FindUnitOfMeasure(unitOfMeasureId) };
        _brandAddedUnits.Add(unit);
        Replace([.. Rows.Select(r => r.Unit), unit], SelectedRow?.Unit);
    }

    /// <summary>
    /// Takes back out each unit added by picking it on a brand row that no row's unit (<paramref name="usedKeys"/>) is any
    /// more. False when none went.
    /// </summary>
    public bool DropBrandUnits(IReadOnlySet<int?> usedKeys)
    {
        var unused = _brandAddedUnits.Where(u => !usedKeys.Contains(u.UnitOfMeasureId)).ToList();
        if (unused.Count == 0)
        {
            return false;
        }

        foreach (var unit in unused)
        {
            _brandAddedUnits.Remove(unit);
        }

        Replace([.. Rows.Select(r => r.Unit).Where(u => !unused.Contains(u))], SelectedRow?.Unit);
        return true;
    }

    /// <summary>
    /// The checks the Alternative Units form's Apply used to make, now that units are typed straight into the table: every row
    /// has a unit and a positive multiplier, none is the base unit or repeats another's unit. Throws BusinessRuleException for
    /// the first problem, which RunSafelyAsync shows.
    /// </summary>
    public void EnsureValid()
    {
        var units = Rows.Select(r => r.Unit).ToList();
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
                throw new BusinessRuleException($"Enter how many {_host.BaseUnitAbbreviation} are in one {unit.UnitOfMeasure?.Name ?? "unit"} ({where}).");
            }

            var validation = _unitValidator.Validate(unit);
            if (!validation.IsValid)
            {
                throw new BusinessRuleException(validation.ToString(" "));
            }

            RawMaterialRules.EnsureNotTheBaseUnit(_host.EditingType == RawMaterialType.Packaging ? _host.EditingBaseUnit?.Id : null, unit.UnitOfMeasureId);
            RawMaterialRules.EnsureUnitNotAlreadyAdded(units.Any(u => !ReferenceEquals(u, unit) && u.UnitOfMeasureId == unit.UnitOfMeasureId));
        }
    }

    /// <summary>The rows as an undo step keeps them: each unit by its id, as well as its unit of measure and multiplier.</summary>
    public IReadOnlyList<UnitRowState> Capture() =>
    [
        .. Rows.Select(r => new UnitRowState(r.Unit.Id, r.Unit.RawMaterialId, r.UnitOfMeasureId, r.BaseUnitsPerUnit, r.BaseUnitAbbreviation)),
    ];

    /// <summary>
    /// The rows an undo step kept, in place of the table's. A unit row writes straight through to the stored RawMaterialUnit
    /// that Save hands the repository, so each unit comes back as a fresh entity carrying its own Id: one that lost it would be
    /// saved as a new unit and the stored one deleted.
    /// </summary>
    public void Restore(IEnumerable<UnitRowState> units) =>
        Sync(() =>
        {
            Rows.Clear();
            foreach (var unit in units)
            {
                Rows.Add(new RawMaterialUnitRow(
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
}
