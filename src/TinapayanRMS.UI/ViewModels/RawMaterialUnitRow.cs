using ReactiveUI;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A row in the Alternative Units grid and a box in the Unit Conversion Overview: one of the form's
/// alternative units, already saved (its Unit has an Id) or not yet (Id 0). Typed straight into: the grid's
/// unit cell sets <see cref="UnitOfMeasureId"/>, which the row resolves to its unit of measure, and the multiplier
/// cell sets <see cref="BaseUnitsPerUnit"/>. Both write through to <see cref="Unit"/>, which Save writes. A row can be
/// half filled in; Save checks them all first. A standard unit of the base unit's kind (a kilogram of an ingredient) takes
/// its fixed multiplier as it's picked, and its multiplier can't be typed (StandardUnitRules).
/// </summary>
public sealed class RawMaterialUnitRow : ReactiveObject
{
    /// <summary>Looks up a unit of measure by id as the screen has it.</summary>
    private readonly Func<int, UnitOfMeasure?>? _findUnitOfMeasure;

    /// <summary>A standard unit's fixed multiplier for the screen's material as it now is, or null for one that's typed.</summary>
    private readonly Func<UnitOfMeasure, decimal?>? _standardMultiplier;

    /// <summary>An empty row, for the grid to compare an add row against (controls:GridAddNewRow.DropWhenUntouched).</summary>
    public RawMaterialUnitRow()
        : this(new RawMaterialUnit(), string.Empty, findUnitOfMeasure: null, standardMultiplier: null)
    {
    }

    /// <param name="findUnitOfMeasure">Resolves a picked unit of measure, so a row names its unit as soon as it's picked, even in the add row.</param>
    /// <param name="standardMultiplier">A standard unit's fixed multiplier for the material (StandardUnitRules), or null to type it.</param>
    public RawMaterialUnitRow(
        RawMaterialUnit unit,
        string baseUnitAbbreviation,
        Func<int, UnitOfMeasure?>? findUnitOfMeasure,
        Func<UnitOfMeasure, decimal?>? standardMultiplier)
    {
        Unit = unit;
        _baseUnitAbbreviation = baseUnitAbbreviation;
        _findUnitOfMeasure = findUnitOfMeasure;
        _standardMultiplier = standardMultiplier;
        ApplyStandardMultiplier();
    }

    public RawMaterialUnit Unit { get; }

    /// <summary>The unit of measure picked in the grid; null until one is.</summary>
    public int? UnitOfMeasureId
    {
        get => Unit.UnitOfMeasureId > 0 ? Unit.UnitOfMeasureId : null;
        set
        {
            if (value == UnitOfMeasureId)
            {
                return;
            }

            Unit.UnitOfMeasureId = value ?? 0;
            Unit.UnitOfMeasure = value is { } id ? _findUnitOfMeasure?.Invoke(id) : null;
            this.RaisePropertyChanged();
            RaiseUnitOfMeasureChanged();
            ApplyStandardMultiplier();
        }
    }

    /// <summary>Points the row at the screen's current copy of its unit of measure, so one renamed in Settings shows its new name.</summary>
    public void RefreshUnitOfMeasure()
    {
        if (UnitOfMeasureId is { } id && _findUnitOfMeasure?.Invoke(id) is { } refreshed && !ReferenceEquals(refreshed, Unit.UnitOfMeasure))
        {
            Unit.UnitOfMeasure = refreshed;
            RaiseUnitOfMeasureChanged();
            ApplyStandardMultiplier();
        }
    }

    /// <summary>The fixed multiplier of the row's unit when it's a standard unit of the base unit's kind; null while it's typed.</summary>
    public decimal? StandardMultiplier => Unit.UnitOfMeasure is { } unitOfMeasure ? _standardMultiplier?.Invoke(unitOfMeasure) : null;

    /// <summary>Whether the multiplier cell can be typed into (controls:GridCellEditing.AllowWhen): not for a standard unit.</summary>
    public bool CanTypeMultiplier => StandardMultiplier is null;

    /// <summary>
    /// Gives a standard unit its fixed multiplier: when it's picked, renamed into one in Settings, or the material's base
    /// unit or type changes under it. Anything else keeps the multiplier typed.
    /// </summary>
    public void ApplyStandardMultiplier()
    {
        if (StandardMultiplier is { } standard)
        {
            BaseUnitsPerUnit = standard;
        }

        this.RaisePropertyChanged(nameof(CanTypeMultiplier));
    }

    public string UnitName => Unit.UnitOfMeasure?.Name ?? "?";

    /// <summary>Empty until a unit is picked, so the add row stays blank.</summary>
    public string Abbreviation => Unit.UnitOfMeasure?.Abbreviation ?? string.Empty;

    /// <summary>How many base units one of this unit holds: 25,000 for a 25 kg bag of an ingredient.</summary>
    public decimal BaseUnitsPerUnit
    {
        get => Unit.BaseUnitsPerUnit;
        set
        {
            if (value == Unit.BaseUnitsPerUnit)
            {
                return;
            }

            Unit.BaseUnitsPerUnit = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(BaseEquivalentText));
        }
    }

    private string _baseUnitAbbreviation;
    /// <summary>The base unit's abbreviation, which a new packaging material can still change.</summary>
    public string BaseUnitAbbreviation
    {
        get => _baseUnitAbbreviation;
        set
        {
            this.RaiseAndSetIfChanged(ref _baseUnitAbbreviation, value);
            this.RaisePropertyChanged(nameof(BaseEquivalentText));
        }
    }

    /// <summary>E.g. "25,000 g" or "1,000 cm".</summary>
    public string BaseEquivalentText => $"{Unit.BaseUnitsPerUnit:#,0.####} {BaseUnitAbbreviation}";

    private void RaiseUnitOfMeasureChanged()
    {
        this.RaisePropertyChanged(nameof(UnitName));
        this.RaisePropertyChanged(nameof(Abbreviation));
    }
}
