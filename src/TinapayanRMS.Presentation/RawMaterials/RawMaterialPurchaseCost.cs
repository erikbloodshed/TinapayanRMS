using System.Collections.ObjectModel;
using System.Globalization;
using DynamicData;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>What the Cost Setup tab asks of its screen, on top of what any part of its form asks.</summary>
public interface IRawMaterialPurchaseCostHost : IFormPartHost
{
    /// <summary>The form's type: a packaging material's base unit is its own, an ingredient's the gram.</summary>
    RawMaterialType EditingType { get; }

    /// <summary>The form's base unit, for a packaging material.</summary>
    UnitOfMeasure? EditingBaseUnit { get; }

    /// <summary>The base unit's name, as the form shows it: "gram", or a packaging material's own.</summary>
    string BaseUnitName { get; }

    /// <summary>The base unit's abbreviation, as the form shows it: "g", or a packaging material's own.</summary>
    string BaseUnitAbbreviation { get; }

    /// <summary>The material picked, whose saved costs the header shows.</summary>
    RawMaterial? SelectedRawMaterial { get; }

    /// <summary>The form is a made ingredient, whose buffered cost the header works out from its recipe as typed.</summary>
    bool IsEditingMade { get; }

    /// <summary>A made ingredient's batch, as its recipe has it: its yield in base units.</summary>
    decimal BomTotalWeight { get; }

    /// <summary>A made ingredient's batch, as its recipe has it: what it costs.</summary>
    decimal BomTotalCost { get; }
}

/// <summary>
/// A purchased material's cost on the Raw Materials screen (through RawMaterialsState.PurchaseCost; PurchaseCostSetup
/// draws it): a price per brand, typed straight into the table, which brand the cost comes from (the dearest, or one picked
/// in Basis), the fixed buffered cost on top of it and the Cost Buffer % it comes to, with the target buffer it was typed
/// at; and the header's saved costs, in the unit picked to show them in (Per). The brand rows' units are the form's
/// alternative units (RawMaterialUnits): a unit picked on a row that the material didn't have is added there.
/// </summary>
public sealed class RawMaterialPurchaseCost : ObservableState, IBrandPriceRowHost
{
    private readonly IRawMaterialPurchaseCostHost _host;
    private readonly RawMaterialUnits _units;
    private readonly IReadOnlyList<UnitOfMeasure> _unitsOfMeasure;
    private readonly IValidator<RawMaterialBrandPrice> _brandPriceValidator;
    private readonly DerivedUpdates _updates;

    /// <summary>
    /// Set while RebuildBufferedCostUnitOptions replaces the choices. Clearing them makes the picker write back an empty
    /// pick, whose texts update would rebuild the choices again, halfway through, until the stack overflowed.
    /// </summary>
    private bool _rebuildingBufferedCostUnits;

    private bool _rebuildingCostFromOptions;

    /// <param name="host">The screen: the form's type and base unit, the material picked, and a made ingredient's batch.</param>
    /// <param name="units">The form's alternative units, which the brands are bought in.</param>
    /// <param name="unitsOfMeasure">Every unit of measure, as the screen last loaded them, for the purchase unit choices.</param>
    /// <param name="updates">The screen's derived updates, so an undo putting the form back works the texts out once.</param>
    public RawMaterialPurchaseCost(
        IRawMaterialPurchaseCostHost host,
        RawMaterialUnits units,
        IReadOnlyList<UnitOfMeasure> unitsOfMeasure,
        IValidator<RawMaterialBrandPrice> brandPriceValidator,
        DerivedUpdates updates)
    {
        _host = host;
        _units = units;
        _unitsOfMeasure = unitsOfMeasure;
        _brandPriceValidator = brandPriceValidator;
        _updates = updates;

        ReapplyTargetBufferCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() => TypedCostBufferPercent = EditingTargetCostBufferPercent),
            () => IsBufferOffTarget);

        // Nothing to confirm: the brand only leaves the table, and its price goes when the material is saved.
        RemoveBrandPriceCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() =>
            {
                BrandPriceRows.Remove(SelectedBrandPriceRow!);
                DropUnusedAddedUnits(pickedRow: null);
            }),
            () => SelectedBrandPriceRow is not null);
    }

    /// <summary>The form's brand prices, saved or not. The material is costed at the preferred brand's, else the dearest, per base unit.</summary>
    public ObservableCollection<BrandPriceRow> BrandPriceRows { get; } = [];

    public BrandPriceRow? SelectedBrandPriceRow
    {
        get;
        set => RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>The row the table's add row edits: one that follows the form's units and buffer.</summary>
    public Func<BrandPriceRow> NewBrandPriceRow => () => new BrandPriceRow(this);

    /// <summary>Takes the brand picked out of the table; Save deletes its price.</summary>
    public ScreenCommand RemoveBrandPriceCommand { get; }

    /// <summary>The brand table's unit choices: the base unit, then every unit of measure it can be bought in.</summary>
    public ObservableCollection<PurchaseUnitChoice> PurchaseUnitOptions { get; } = [];

    /// <summary>How many brand prices the material has saved, which fix a packaging material's base unit.</summary>
    public int SavedBrandPriceCount { get; private set; }

    /// <summary>
    /// The material's cost buffer; empty saves as 0%, since the box shows 0.00 when cleared. A new one starts at Settings'
    /// default (<see cref="CostingDefaults.CostBufferPercent"/>).
    /// </summary>
    public decimal? EditingCostBufferPercent
    {
        get;
        set
        {
            if (RaiseAndSetIfChanged(ref field, value))
            {
                BufferEdited();
            }
        }
    } = CostingDefaults.DefaultCostBufferPercent;

    /// <summary>
    /// Purchased only: the buffer last typed, which the Cost Buffer % box drifts from as prices move while the
    /// buffered cost stays fixed. Re-apply Target types it again.
    /// </summary>
    public decimal? EditingTargetCostBufferPercent
    {
        get;
        set => RaiseAndSetIfChanged(ref field, value);
    } = CostingDefaults.DefaultCostBufferPercent;

    private string? _editingPreferredBrand;
    /// <summary>
    /// The brand a purchased material's cost comes from, as Save will keep it: null for the dearest brand. The
    /// costs and texts below follow it at once, as they follow the table (UpdatePurchaseCostTexts).
    /// </summary>
    public string? EditingPreferredBrand
    {
        get => _editingPreferredBrand;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingPreferredBrand, value))
            {
                _updates.Run(UpdatePurchaseCostTexts);
            }
        }
    }

    /// <summary>The Basis picker's choices: the dearest brand, then each brand in the table (RebuildCostFromOptions).</summary>
    public ObservableCollection<CostFromOption> CostFromOptions { get; } = [new(null)];

    /// <summary>The Basis picker's pick, <see cref="EditingPreferredBrand"/> as one of its choices.</summary>
    public CostFromOption? CostFrom
    {
        get => CostFromOptions.FirstOrDefault(o => string.Equals(o.Brand, EditingPreferredBrand, StringComparison.OrdinalIgnoreCase)) ?? CostFromOptions[0];
        set
        {
            // While the choices are replaced, the picker writes back an empty pick, as the Per picker does.
            if (_rebuildingCostFromOptions || value is null)
            {
                return;
            }

            EditingPreferredBrand = value.Brand;
        }
    }

    /// <summary>"Target 10%", beside the Cost Buffer % box, while the buffer has drifted from it.</summary>
    public string TargetBufferText => string.Create(CultureInfo.CurrentCulture, $"Target {EditingTargetCostBufferPercent ?? 0m:0.##}%");

    /// <summary>The buffer has drifted from its target as prices moved: Re-apply Target shows.</summary>
    public bool IsBufferOffTarget =>
        DearestPurchaseCost is not null
        && EditingBufferedCost is > 0m
        && RawMaterialRules.IsOffTarget(EditingCostBufferPercent ?? 0m, EditingTargetCostBufferPercent ?? 0m);

    /// <summary>Types the target buffer into the Cost Buffer % box again, which puts it back on top of the dearest brand; one undo step.</summary>
    public ScreenCommand ReapplyTargetBufferCommand { get; }

    /// <summary>
    /// The Cost Buffer % box. Typing it works the buffered cost out afresh: the dearest brand, as the table now is, with
    /// the percent on top (nothing to work out from while no brand is priced). The other way round, the % follows the
    /// fixed amount over the dearest brand as prices are typed (UpdatePurchaseCostTexts).
    /// </summary>
    public decimal? TypedCostBufferPercent
    {
        get => EditingCostBufferPercent;
        set
        {
            EditingCostBufferPercent = value;
            EditingTargetCostBufferPercent = value ?? 0m;
            if (DearestLiveCost() is { } dearest)
            {
                EditingBufferedCost = RawMaterialRules.BufferedCost(dearest, value ?? 0m);
            }
        }
    }

    /// <summary>
    /// Purchased only: the fixed buffered cost per base unit, which brand prices don't move (the Cost Buffer % follows
    /// them instead). Typed, or worked out from the dearest brand when the percent is typed; a cleared box saves as 0,
    /// which Save then works out afresh from the prices (as for a material's first price).
    /// </summary>
    public decimal? EditingBufferedCost
    {
        get;
        set
        {
            if (RaiseAndSetIfChanged(ref field, value))
            {
                BufferEdited();
            }
        }
    }

    /// <summary>
    /// The buffer or the buffered cost changed: each brand's buffered price follows the fixed buffered cost, once the
    /// screen's derived updates run (an undo's restore holds them back until the form is whole). Only typing the percent
    /// moves the amount (TypedCostBufferPercent).
    /// </summary>
    private void BufferEdited() => _updates.Run(BufferedCostChanged);

    /// <summary>
    /// The units the buffered cost can be shown and typed in: the base unit, then the form's alternative units that have
    /// a multiplier. Left alone when nothing changed, so the picker keeps its selection.
    /// </summary>
    public ObservableCollection<PurchaseUnitChoice> BufferedCostUnitOptions { get; } = [];

    private PurchaseUnitChoice? _bufferedCostUnit;
    /// <summary>
    /// The unit the Buffered Cost box is in, for show only: the buffered cost is kept per base unit. It starts on the
    /// dearest brand's unit, and falls back to the base unit when its unit goes.
    /// </summary>
    public PurchaseUnitChoice? BufferedCostUnit
    {
        get => _bufferedCostUnit;
        set
        {
            // While the choices are replaced, the picker writes back an empty pick; the rebuild sets the pick itself.
            if (_rebuildingBufferedCostUnits)
            {
                return;
            }

            RaiseAndSetIfChanged(ref _bufferedCostUnit, value);
            UpdatePurchaseCostTexts();
        }
    }

    /// <summary>
    /// EditingBufferedCost in BufferedCostUnit, rounded for show as the screen's other costs are: to the centavo on an
    /// alternative unit (₱0.0518948/g is ₱1,297.37 a 25 kg bag), and to 4 decimals on the base unit, where a cost is a
    /// fraction of a centavo (₱0.0525/g would otherwise read as ₱0.05/g, and a new material starts there).
    /// What's typed is divided back to the base unit at full precision, so it isn't rounded, and the Cost Buffer % becomes
    /// what it is over the dearest brand, to the 4 decimals the percent keeps: ₱1,300 a bag over ₱1,250 is 4%.
    /// </summary>
    public decimal? EditingBufferedCostInUnit
    {
        get => EditingBufferedCost is { } cost
            ? Math.Round(cost * BufferedCostUnitSize(), IsBufferedCostOnBaseUnit ? 4 : 2, MidpointRounding.ToEven)
            : null;
        set
        {
            // UpdatePurchaseCostTexts, which the change below reaches, works the % out from the new amount.
            EditingBufferedCost = value is { } typed ? typed / BufferedCostUnitSize() : null;
        }
    }

    /// <summary>What the dearest brand comes to per base unit, recalculated as the table is typed into, e.g. "0.0372".</summary>
    public string PurchaseCostText
    {
        get;
        private set => RaiseAndSetIfChanged(ref field, value);
    } = "—";

    /// <summary>What EditingBufferedCost comes to over the dearest brand, e.g. "0.0391/g is 5.11% over the dearest brand's 0.0372/g.", or empty while unpriced.</summary>
    public string PurchaseBufferedCostText
    {
        get;
        private set => RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    /// <summary>Set while the dearest brand costs more per base unit than EditingBufferedCost, so the buffer has been eaten up; empty otherwise.</summary>
    public string BufferedCostWarningText
    {
        get;
        private set => RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    /// <summary>The dearest brand per base unit as the table is typed into, which the buffer percent goes on top of; null while unpriced.</summary>
    public decimal? DearestPurchaseCost
    {
        get;
        private set => RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>Which brand the cost comes from, when there's more than one, e.g. "From Gold Medal, the dearest of 2 brands."</summary>
    public string PurchaseCostSourceText
    {
        get;
        private set => RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    /// <summary>
    /// The header's saved cost in the Per unit (BufferedCostUnit): "0.0372" a gram, to 4 decimals, or "930.00" a bag, to 2;
    /// "—" for a material not saved yet. The header's costs are worked out as they're read, in the Per unit: to 4 decimals
    /// on the base unit, whose costs are fractions of a centavo, and to the centavo on any other.
    /// </summary>
    public string CurrentCostText =>
        _host.SelectedRawMaterial is { } material ? FormatInBufferedCostUnit(material.CostPerBaseUnit) : "—";

    /// <summary>
    /// The header's buffered cost, as CurrentCostText: "0.0391" a gram, "976.50" a bag, or "—" for a material not saved
    /// yet. A made ingredient's follows the form's BOM instead of the saved row, so the Total Cost and Total Weight tiles
    /// beside it always divide out to it as the table is typed into; Save then stores the same figure. Every other material
    /// shows what was last saved.
    /// </summary>
    public string BufferedCostText =>
        _host.IsEditingMade
            ? FormatInBufferedCostUnit(_host.BomTotalWeight > 0 ? _host.BomTotalCost / _host.BomTotalWeight : 0m)
            : _host.SelectedRawMaterial is { } saved ? FormatInBufferedCostUnit(saved.BufferedCostPerBaseUnit) : "—";

    /// <summary>The unit the header's costs are in, for their labels: the Per unit's abbreviation, e.g. "g" or "bag".</summary>
    public string HeaderCostUnitText => BufferedCostUnit?.Abbreviation ?? _host.BaseUnitAbbreviation;

    /// <summary>How many base units BufferedCostUnit holds: 1 for the base unit.</summary>
    private decimal BufferedCostUnitSize() =>
        BufferedCostUnit is { Key: not PurchaseUnitChoice.BaseUnitKey } unit
        && _units.Rows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unit.Key)?.Unit is { BaseUnitsPerUnit: > 0 } row
            ? row.BaseUnitsPerUnit
            : 1m;

    /// <summary>Whether BufferedCostUnit is the base unit, whose costs are shown to 4 decimals rather than to the centavo.</summary>
    private bool IsBufferedCostOnBaseUnit => BufferedCostUnit is null or { Key: PurchaseUnitChoice.BaseUnitKey };

    /// <summary>A cost per base unit in BufferedCostUnit, as the screen shows it: "0.0525" a gram, "1,312.50" a bag.</summary>
    private string FormatInBufferedCostUnit(decimal costPerBaseUnit) =>
        IsBufferedCostOnBaseUnit ? $"{costPerBaseUnit:N4}" : $"{costPerBaseUnit * BufferedCostUnitSize():N2}";

    /// <summary>The material's brand prices as saved, in place of the table's.</summary>
    public void ShowBrandPrices(IReadOnlyList<RawMaterialBrandPrice> prices)
    {
        BrandPriceRows.Clear();
        BrandPriceRows.AddRange(prices.Select(p =>
            new BrandPriceRow(this, p.Brand, p.Unit?.UnitOfMeasureId ?? PurchaseUnitChoice.BaseUnitKey, p.Price)));
        SavedBrandPriceCount = prices.Count;
    }

    /// <summary>The brand rows as an undo step keeps them.</summary>
    public IReadOnlyList<BrandPriceSnapshot> CaptureBrandPrices() => [.. BrandPriceRows.Select(r => new BrandPriceSnapshot(r.Brand, r.UnitKey, r.Price))];

    /// <summary>The brand rows an undo step kept, in place of the table's.</summary>
    public void RestoreBrandPrices(IEnumerable<BrandPriceSnapshot> prices)
    {
        BrandPriceRows.Clear();
        foreach (var price in prices)
        {
            BrandPriceRows.Add(new BrandPriceRow(this) { Brand = price.Brand, UnitKey = price.UnitKey, Price = price.Price });
        }
    }

    /// <summary>The Cost Buffer % box shows TypedCostBufferPercent, which an undo leaving the buffer as it was doesn't raise.</summary>
    public void RaiseTypedCostBufferPercent() => RaisePropertyChanged(nameof(TypedCostBufferPercent));

    /// <summary>Each brand's buffered price follows the fixed buffered cost. Only typing the percent moves the amount (TypedCostBufferPercent).</summary>
    public void BufferedCostChanged()
    {
        RaisePropertyChanged(nameof(TypedCostBufferPercent));
        RefreshBrandPriceRows();
        UpdatePurchaseCostTexts();
    }

    /// <summary>Shows each brand row's quantity and buffered price as the units and buffer now are.</summary>
    public void RefreshBrandPriceRows()
    {
        foreach (var row in BrandPriceRows)
        {
            row.Refresh();
        }
    }

    /// <summary>
    /// The Buffered Cost unit picker's choices: the base unit, then each alternative unit with a multiplier. A picked unit
    /// that's gone is swapped for the base unit; one that's still there is kept, as the new list's own copy.
    /// </summary>
    public void RebuildBufferedCostUnitOptions()
    {
        List<PurchaseUnitChoice> choices =
        [
            new(PurchaseUnitChoice.BaseUnitKey, _host.BaseUnitName, _host.BaseUnitAbbreviation),
            .. _units.Rows
                .Where(r => r.Unit is { UnitOfMeasureId: > 0, BaseUnitsPerUnit: > 0 } && _units.FindUnitOfMeasure(r.Unit.UnitOfMeasureId) is not null)
                .Select(r => _units.FindUnitOfMeasure(r.Unit.UnitOfMeasureId)!)
                .Select(u => new PurchaseUnitChoice(u.Id, u.Name, u.Abbreviation)),
        ];

        if (choices.SequenceEqual(BufferedCostUnitOptions))
        {
            return;
        }

        var picked = BufferedCostUnit;
        _rebuildingBufferedCostUnits = true;
        try
        {
            BufferedCostUnitOptions.Clear();
            BufferedCostUnitOptions.AddRange(choices);
            _bufferedCostUnit = choices.FirstOrDefault(c => c.Key == picked?.Key) ?? choices[0];
            RaisePropertyChanged(nameof(BufferedCostUnit));
        }
        finally
        {
            _rebuildingBufferedCostUnits = false;
        }
    }

    /// <summary>The unit of the brand price the cost comes from as the table now is, among the picker's choices; the base unit while unpriced.</summary>
    public PurchaseUnitChoice DearestBrandUnit()
    {
        var priced = BrandPriceRows.Where(r => r.CostPerBaseUnit is not null).ToList();
        var key = CostBrandIndex(priced) is { } source ? priced[source].UnitKey : null;
        return BufferedCostUnitOptions.FirstOrDefault(c => c.Key == key) ?? BufferedCostUnitOptions[0];
    }

    /// <summary>
    /// What Save costs the material at per base unit, as the table now is: the preferred brand's price, else the dearest
    /// (RawMaterialRules.FindCostBrandPrice); null while unpriced.
    /// </summary>
    private decimal? DearestLiveCost()
    {
        var priced = BrandPriceRows.Where(r => r.CostPerBaseUnit is not null).ToList();
        return CostBrandIndex(priced) is { } source ? priced[source].CostPerBaseUnit : null;
    }

    /// <summary>The row of <paramref name="priced"/> the cost comes from: the preferred brand's, else the dearest.</summary>
    private int? CostBrandIndex(IReadOnlyList<BrandPriceRow> priced) =>
        RawMaterialRules.FindCostBrandPrice([.. priced.Select(r => (r.Brand, r.Price!.Value, r.Quantity))], EditingPreferredBrand);

    /// <summary>
    /// The Basis picker's choices as the table now is: the dearest brand, each named brand once, and the preferred brand
    /// still, saying so, when its prices have gone.
    /// </summary>
    private void RebuildCostFromOptions()
    {
        List<CostFromOption> choices = [new(null)];
        foreach (var brand in BrandPriceRows.Select(r => RawMaterialRules.NormalizeBrand(r.Brand)).OfType<string>())
        {
            if (!choices.Any(c => string.Equals(c.Brand, brand, StringComparison.OrdinalIgnoreCase)))
            {
                choices.Add(new(brand));
            }
        }

        if (RawMaterialRules.NormalizeBrand(EditingPreferredBrand) is { } preferred
            && !choices.Any(c => string.Equals(c.Brand, preferred, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new(preferred, HasPrice: false));
        }

        if (choices.SequenceEqual(CostFromOptions))
        {
            return;
        }

        _rebuildingCostFromOptions = true;
        try
        {
            CostFromOptions.Clear();
            CostFromOptions.AddRange(choices);
        }
        finally
        {
            _rebuildingCostFromOptions = false;
        }

        RaisePropertyChanged(nameof(CostFrom));
    }

    /// <summary>
    /// The brand table's unit choices: the base unit, then every unit of measure but one that is (or is named like) the base
    /// unit. Left alone when nothing changed, so the table's open unit cell keeps its list.
    /// </summary>
    public void RebuildPurchaseUnitOptions()
    {
        var baseUnitId = _host.EditingType == RawMaterialType.Packaging ? _host.EditingBaseUnit?.Id : null;
        List<PurchaseUnitChoice> choices =
        [
            new(PurchaseUnitChoice.BaseUnitKey, BaseUnitText.NameOf(_host.EditingType, _host.EditingBaseUnit), BaseUnitText.AbbreviationOf(_host.EditingType, _host.EditingBaseUnit)),
            .. _unitsOfMeasure
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
        string.Equals(unit.Name, BaseUnitText.NameOf(_host.EditingType, _host.EditingBaseUnit), StringComparison.OrdinalIgnoreCase)
        || string.Equals(unit.Abbreviation, BaseUnitText.AbbreviationOf(_host.EditingType, _host.EditingBaseUnit), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The summary of the table: the dearest brand per base unit, as Save will cost the material, and what the fixed
    /// buffered cost comes to over it.
    /// </summary>
    public void UpdatePurchaseCostTexts()
    {
        foreach (var row in BrandPriceRows)
        {
            row.RefreshBufferPercent();
        }

        RebuildBufferedCostUnitOptions();
        RebuildCostFromOptions();
        RaisePropertyChanged(nameof(EditingBufferedCostInUnit));

        var priced = BrandPriceRows.Where(r => r.CostPerBaseUnit is not null).ToList();
        if (CostBrandIndex(priced) is not { } dearest)
        {
            DearestPurchaseCost = null;
            PurchaseCostText = "—";
            PurchaseBufferedCostText = string.Empty;
            BufferedCostWarningText = string.Empty;
            PurchaseCostSourceText = string.Empty;
            return;
        }

        var cost = priced[dearest].CostPerBaseUnit!.Value;
        var unit = BufferedCostUnit?.Abbreviation ?? _host.BaseUnitAbbreviation;

        // What the cost comes from, as the texts name it: the brand picked in Basis while it has a price, else the dearest.
        var isPreferred = RawMaterialRules.IsBrandPriced(priced.Select(r => r.Brand), EditingPreferredBrand);
        var source = isPreferred ? RawMaterialRules.NormalizeBrand(priced[dearest].Brand)! : "the dearest brand";
        DearestPurchaseCost = cost;
        PurchaseCostText = $"{cost:N4}";
        if (EditingBufferedCost is > 0 and var buffered)
        {
            var over = RawMaterialRules.CostBufferPercentOf(cost, buffered);
            // The % box is what the fixed amount now comes to over the dearest brand, so a price change moves the % while
            // the amount stays put. Held to the 0-100 the box, the validator and the
            // check constraint allow, so a buffer that's gone (or more than double) pins it; the line below and the
            // warning say where it really stands. Typing the % still works the other way, through TypedCostBufferPercent.
            EditingCostBufferPercent = Math.Clamp(Math.Round(over, 4, MidpointRounding.ToEven), 0m, 100m);
            PurchaseBufferedCostText = $"{FormatInBufferedCostUnit(buffered)}/{unit} is {over:0.##}% over {source}'s {FormatInBufferedCostUnit(cost)}/{unit}.";
            // Compared to the 4 places costs per base unit are shown to: a saved buffered cost comes back cut to its
            // column's 20 places, so a repeating one (170/240) would otherwise read as a hair under an unchanged dearest brand.
            BufferedCostWarningText = Math.Round(buffered, 4, MidpointRounding.AwayFromZero) < Math.Round(cost, 4, MidpointRounding.AwayFromZero)
                ? $"{char.ToUpperInvariant(source[0])}{source[1..]} now costs more than the buffered cost. Type the Cost Buffer % again to put the buffer back on top."
                : string.Empty;
        }
        else
        {
            PurchaseBufferedCostText = $"Save works it out as {FormatInBufferedCostUnit(RawMaterialRules.BufferedCost(cost, EditingCostBufferPercent ?? 0m))}/{unit}, {source} with the buffer on top.";
            BufferedCostWarningText = string.Empty;
        }

        PurchaseCostSourceText = isPreferred
            ? $"From {source}, the brand picked in Basis."
            : priced.Count > 1
                ? $"From {RawMaterialRules.NormalizeBrand(priced[dearest].Brand) ?? "the unnamed brand"}, the dearest of {priced.Count} brands."
                : string.Empty;
    }

    /// <summary>
    /// The brand rows as Save sends them. Each needs a unit and a price, and its quantity when the unit is new; a row
    /// with nothing typed into it is left out. Throws BusinessRuleException for the first problem, which RunSafelyAsync shows.
    /// </summary>
    public List<(string? Brand, int? UnitOfMeasureId, decimal Price)> CompleteBrandPrices()
    {
        var prices = new List<(string? Brand, int? UnitOfMeasureId, decimal Price)>();
        foreach (var row in BrandPriceRows)
        {
            var brand = RawMaterialRules.NormalizeBrand(row.Brand);
            if (brand is null && row.UnitKey is null && row.Price is null)
            {
                continue;
            }

            if (row.UnitKey is not { } key)
            {
                throw new BusinessRuleException(brand is null
                    ? "Pick the purchase unit every price is for, or remove the empty row."
                    : $"Pick the purchase unit '{brand}' is priced in.");
            }

            if (key != PurchaseUnitChoice.BaseUnitKey && _units.FindUnit(key) is null)
            {
                throw new BusinessRuleException(
                    $"{(brand is null ? "A price" : $"'{brand}'")} is in a unit this material doesn't have. Pick another, or add it in Alternative Units.");
            }

            var validation = _brandPriceValidator.Validate(new RawMaterialBrandPrice { Brand = brand, Price = row.Price ?? 0m });
            if (!validation.IsValid)
            {
                throw new BusinessRuleException(validation.ToString(" "));
            }

            prices.Add((brand, key == PurchaseUnitChoice.BaseUnitKey ? null : key, row.Price!.Value));
        }

        RawMaterialRules.EnsureDistinctBrandPrices(prices.Select(p => (p.Brand, p.UnitOfMeasureId)));
        return prices;
    }

    /// <summary>
    /// A packaging material's unit picked on a brand row before it was made the base unit: the rows priced in it are now
    /// priced in the base unit, and the alternative unit the pick added goes with them (OnUnitPicked). Kept, Save would
    /// refuse it as the base unit. One added in the Alternative Units table is left for Save to refuse.
    /// </summary>
    public void MoveBrandRowsOntoTheBaseUnit()
    {
        if (_host.EditingType != RawMaterialType.Packaging
            || _host.EditingBaseUnit is not { } baseUnit
            || !_units.IsBrandAdded(baseUnit.Id))
        {
            return;
        }

        foreach (var row in BrandPriceRows.Where(r => r.UnitKey == baseUnit.Id).ToList())
        {
            row.UnitKey = PurchaseUnitChoice.BaseUnitKey;
        }
    }

    /// <summary>
    /// Takes back out of the alternative units each one added by picking it on a brand row that no row, nor
    /// <paramref name="pickedRow"/> (a row still being added), is in any more.
    /// </summary>
    private void DropUnusedAddedUnits(BrandPriceRow? pickedRow)
    {
        if (_units.DropBrandUnits(BrandPriceRows.Append(pickedRow).Select(r => r?.UnitKey).ToHashSet()))
        {
            RefreshBrandPriceRows();
            UpdatePurchaseCostTexts();
        }
    }

    RawMaterialUnit? IBrandPriceRowHost.FindUnit(int unitOfMeasureId) => _units.FindUnit(unitOfMeasureId);

    bool IBrandPriceRowHost.IsMultiplierFixed(int unitOfMeasureId) => _units.IsMultiplierFixed(unitOfMeasureId);

    void IBrandPriceRowHost.SetMultiplier(int unitOfMeasureId, decimal baseUnitsPerUnit)
    {
        if (_units.SetMultiplier(unitOfMeasureId, baseUnitsPerUnit))
        {
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
    /// multiplier typed as the row's quantity; and one added that way that no row is in any more goes. <paramref name="row"/>
    /// may still be the table's add row, which isn't in BrandPriceRows yet.
    /// </summary>
    void IBrandPriceRowHost.OnUnitPicked(BrandPriceRow row)
    {
        DropUnusedAddedUnits(row);

        if (row.UnitKey is not { } key || key == PurchaseUnitChoice.BaseUnitKey || _units.FindUnit(key) is not null)
        {
            return;
        }

        _units.AddBrandUnit(key);
        RefreshBrandPriceRows();
    }
}
