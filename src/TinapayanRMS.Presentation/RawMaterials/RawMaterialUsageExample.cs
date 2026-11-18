using System.Collections.ObjectModel;
using DynamicData;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>
/// The Raw Materials screen's Usage Example tab (through RawMaterialsState.UsageExample): what a quantity of a material,
/// in one of its units, costs as saved and buffered. It starts on the material picked, and any other can be picked; it isn't
/// part of the form, so nothing in it is an edit to save.
/// </summary>
public sealed class RawMaterialUsageExample : ObservableState
{
    public RawMaterialUsageExample()
    {
        ReplaceUnitOptions(null, kept: null);
        CalculateCommand = ScreenCommand.Of(Calculate, () => Material is not null && Unit is not null && Quantity > 0);
    }

    /// <summary>The material costed: picked (<see cref="Pick"/>), or a load's fresh copy of it (<see cref="Refresh"/>).</summary>
    public RawMaterial? Material
    {
        get;
        private set => RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Another material picked: it brings its own units, and the example goes.</summary>
    public void Pick(RawMaterial? material)
    {
        if (material != Material)
        {
            Material = material;
            ReplaceUnitOptions(material, kept: null);
            InputsEdited();
        }
    }

    public decimal Quantity
    {
        get;
        set
        {
            if (RaiseAndSetIfChanged(ref field, value))
            {
                InputsEdited();
            }
        }
    } = 1m;

    /// <summary>The units <see cref="Material"/> can be counted in: its base unit, then its alternative units.</summary>
    public ObservableCollection<RawMaterialUnitOption> UnitOptions { get; } = [];

    public RawMaterialUnitOption? Unit
    {
        get;
        set
        {
            if (RaiseAndSetIfChanged(ref field, value))
            {
                InputsEdited();
            }
        }
    }

    public UsageExampleResult? Result
    {
        get;
        private set => RaiseAndSetIfChanged(ref field, value);
    }

    public ScreenCommand CalculateCommand { get; }

    /// <summary>A worked-out example belongs to the inputs it was worked out from, so it goes as they're changed.</summary>
    private void InputsEdited() => Result = null;

    /// <summary>
    /// A load's fresh copy of the material picked (<paramref name="material"/>): another material means other units, so they're
    /// replaced, keeping <paramref name="keptUnit"/> where there's one like it, and the result goes.
    /// </summary>
    public void Refresh(RawMaterial? material, RawMaterialUnitOption? keptUnit)
    {
        var previous = Material;
        Material = material;
        if (!ReferenceEquals(previous, Material))
        {
            ReplaceUnitOptions(Material, keptUnit);
            Result = null;
        }
    }

    /// <summary>
    /// The unit choices for <paramref name="material"/>. Keeps the unit picked before (<paramref name="kept"/>, matched by id —
    /// the base unit's is null) when there was one; otherwise starts on its largest alternative unit, else its base unit.
    /// </summary>
    private void ReplaceUnitOptions(RawMaterial? material, RawMaterialUnitOption? kept)
    {
        UnitOptions.Clear();
        UnitOptions.AddRange(RawMaterialUnitOption.For(material));
        Unit = (kept is null ? null : UnitOptions.FirstOrDefault(o => o.UnitId == kept.UnitId))
            ?? UnitOptions.Skip(1).FirstOrDefault()
            ?? UnitOptions.FirstOrDefault();
    }

    private void Calculate()
    {
        var material = Material!;
        var unit = Unit!;
        var baseQuantity = UnitConversionRules.ToBaseQuantity(Quantity, unit.Unit?.BaseUnitsPerUnit);
        Result = new UsageExampleResult(
            $"Cost for {Quantity:#,0.####} {unit.Abbreviation} of {material.Name}",
            baseQuantity,
            BaseUnitText.AbbreviationOf(material),
            material.CostPerBaseUnit,
            baseQuantity * material.CostPerBaseUnit,
            // What the fixed buffered cost comes to over the cost, which drifts from the percent as prices move.
            material.CostPerBaseUnit > 0
                ? Math.Round(RawMaterialRules.CostBufferPercentOf(material.CostPerBaseUnit, material.BufferedCostPerBaseUnit), 2, MidpointRounding.AwayFromZero)
                : material.CostBufferPercent,
            baseQuantity * material.BufferedCostPerBaseUnit);
    }
}
