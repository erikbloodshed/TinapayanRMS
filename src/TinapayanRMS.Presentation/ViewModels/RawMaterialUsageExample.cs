using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Raw Materials screen's Usage Example tab (through RawMaterialsViewModel.UsageExample): what a quantity of a material,
/// in one of its units, costs as saved and buffered. It starts on the material picked, and any other can be picked; it isn't
/// part of the form, so nothing in it is an edit to save.
/// </summary>
public sealed class RawMaterialUsageExample : ReactiveObject
{
    /// <param name="host">The screen, whose loads re-point the material picked without it being the user's pick.</param>
    public RawMaterialUsageExample(IFormPartHost host)
    {
        this.WhenAnyValue(x => x.Material)
            .Where(_ => !host.IsRefreshing)
            .Subscribe(material => ReplaceUnitOptions(material, kept: null));

        // A worked-out example belongs to the inputs it was worked out from.
        this.WhenAnyValue(x => x.Material, x => x.Quantity, x => x.Unit)
            .Where(_ => !host.IsRefreshing)
            .Subscribe(_ => Result = null);

        CalculateCommand = ReactiveCommand.Create(
            Calculate,
            this.WhenAnyValue(x => x.Material, x => x.Unit, x => x.Quantity,
                (material, unit, quantity) => material is not null && unit is not null && quantity > 0));
    }

    private RawMaterial? _material;
    public RawMaterial? Material
    {
        get => _material;
        set => this.RaiseAndSetIfChanged(ref _material, value);
    }

    private decimal _quantity = 1m;
    public decimal Quantity
    {
        get => _quantity;
        set => this.RaiseAndSetIfChanged(ref _quantity, value);
    }

    /// <summary>The units <see cref="Material"/> can be counted in: its base unit, then its alternative units.</summary>
    public ObservableCollection<RawMaterialUnitOption> UnitOptions { get; } = [];

    private RawMaterialUnitOption? _unit;
    public RawMaterialUnitOption? Unit
    {
        get => _unit;
        set => this.RaiseAndSetIfChanged(ref _unit, value);
    }

    private UsageExampleResult? _result;
    public UsageExampleResult? Result
    {
        get => _result;
        private set => this.RaiseAndSetIfChanged(ref _result, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> CalculateCommand { get; }

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
                ? Math.Round(RawMaterialRules.CostBufferPercentOf(material.CostPerBaseUnit, material.BufferedCostPerBaseUnit), 2)
                : material.CostBufferPercent,
            baseQuantity * material.BufferedCostPerBaseUnit);
    }
}
