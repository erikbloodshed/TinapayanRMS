using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

// The costs: the brands' prices, the buffered cost and its target, the header's figures, and the worked example.
public sealed partial class RawMaterialsViewModel
{
    /// <summary>The header's costs, and the brands' prices and buffered prices as they're typed.</summary>
    private void WireCosts()
    {
        // The header's saved costs; a new Per unit, or a unit's multiplier, updates them through UpdatePurchaseCostTexts.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Subscribe(_ => UpdateHeaderCostTexts());

        // The summary follows the grid as it's typed into: the dearest brand, as Save will cost the material.
        BrandPriceRows.ToObservableChangeSet()
            .AutoRefresh(r => r.Price)
            .AutoRefresh(r => r.Quantity)
            .AutoRefresh(r => r.Brand)
            .Subscribe(_ => _updates.Run(UpdatePurchaseCostTexts));

        // Each brand's buffered price follows the fixed buffered cost. Only typing the percent moves the amount (TypedCostBufferPercent).
        this.WhenAnyValue(x => x.EditingBufferedCost, x => x.EditingCostBufferPercent)
            .Subscribe(_ => _updates.Run(BufferedCostChanged));
    }

    /// <summary>The worked example's units, and its result, belong to what it was worked out from.</summary>
    private void WireExample()
    {
        this.WhenAnyValue(x => x.ExampleRawMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material => ReplaceExampleUnitOptions(material, kept: null));

        // A worked-out example belongs to the inputs it was worked out from.
        this.WhenAnyValue(x => x.ExampleRawMaterial, x => x.ExampleQuantity, x => x.ExampleUnit)
            .Where(_ => !IsRefreshing)
            .Subscribe(_ => ExampleResult = null);
    }

    /// <summary>The target buffer shows while the buffer has drifted from it.</summary>
    private void WireBufferTarget()
    {
        // The target buffer beside the Cost Buffer % box shows while the buffer has drifted from it.
        this.WhenAnyValue(x => x.EditingCostBufferPercent, x => x.EditingBufferedCost, x => x.DearestPurchaseCost)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsBufferOffTarget)));
    }

    /// <summary>
    /// The target buffer put back, the worked example, a price history entry deleted, and a brand taken out.
    /// </summary>
    [MemberNotNull(
        nameof(ReapplyTargetBufferCommand), nameof(CalculateExampleCommand), nameof(DeletePriceCommand),
        nameof(RemoveBrandPriceCommand))]
    private void WireCostCommands()
    {
        ReapplyTargetBufferCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => TypedCostBufferPercent = EditingTargetCostBufferPercent),
            this.WhenAnyValue(x => x.IsBufferOffTarget));

        CalculateExampleCommand = ReactiveCommand.Create(
            () =>
            {
                var material = ExampleRawMaterial!;
                var unit = ExampleUnit!;
                var baseQuantity = UnitConversionRules.ToBaseQuantity(ExampleQuantity, unit.Unit?.BaseUnitsPerUnit);
                ExampleResult = new UsageExampleResult(
                    $"Cost for {ExampleQuantity:#,0.####} {unit.Abbreviation} of {material.Name}",
                    baseQuantity,
                    BaseUnitText.AbbreviationOf(material),
                    material.CostPerBaseUnit,
                    baseQuantity * material.CostPerBaseUnit,
                    // What the fixed buffered cost comes to over the cost, which drifts from the percent as prices move.
                    material.CostPerBaseUnit > 0
                        ? Math.Round(RawMaterialRules.CostBufferPercentOf(material.CostPerBaseUnit, material.BufferedCostPerBaseUnit), 2)
                        : material.CostBufferPercent,
                    baseQuantity * material.BufferedCostPerBaseUnit);
            },
            this.WhenAnyValue(x => x.ExampleRawMaterial, x => x.ExampleUnit, x => x.ExampleQuantity,
                (material, unit, quantity) => material is not null && unit is not null && quantity > 0));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync("Delete this price history entry? The material's cost stays as it is. This can't be undone.", async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceRow!.Entry.Id, CancellationToken.None);

                // The history is only a log, so the cost stays and the form keeps its unsaved edits. Only the history reloads.
                var material = SelectedRawMaterial!;
                ReplacePriceHistory(material, await _priceHistoryRepository.GetForRawMaterialAsync(material.Id, CancellationToken.None));
                UpdateCanChangeBaseUnit();
            }),
            this.WhenAnyValue(x => x.SelectedPriceRow).Select(row => row is not null));

        // Nothing to confirm: the brand only leaves the grid, and its price goes when the material is saved.
        RemoveBrandPriceCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                BrandPriceRows.Remove(SelectedBrandPriceRow!);
                DropUnusedAddedUnits(pickedRow: null);
            }),
            this.WhenAnyValue(x => x.SelectedBrandPriceRow).Select(row => row is not null));
    }

    /// <summary>Each brand's buffered price follows the fixed buffered cost. Only typing the percent moves the amount (TypedCostBufferPercent).</summary>
    private void BufferedCostChanged()
    {
        this.RaisePropertyChanged(nameof(TypedCostBufferPercent));
        RefreshBrandPriceRows();
        UpdatePurchaseCostTexts();
    }

    /// <summary>
    /// Takes up Settings' cost buffer for a new material as LoadCommand read it. A new material still showing the one it
    /// started with takes the new one, and what Save measures against follows it, since the change isn't the user's; one
    /// whose buffer was typed keeps it.
    /// </summary>
    private void RefreshDefaultCostBuffer(decimal defaultCostBufferPercent)
    {
        var untouchedNewMaterial = _editingId == 0 && EditingCostBufferPercent == _defaultCostBufferPercent;
        _defaultCostBufferPercent = defaultCostBufferPercent;
        if (untouchedNewMaterial)
        {
            EditingCostBufferPercent = defaultCostBufferPercent;
            EditingTargetCostBufferPercent = defaultCostBufferPercent;
            History.Rebase((settled, now) => settled with { CostBufferPercent = now.CostBufferPercent, TargetCostBufferPercent = now.TargetCostBufferPercent });
        }
    }

    /// <summary>Shows each brand row's quantity and buffered price as the units and buffer now are.</summary>
    private void RefreshBrandPriceRows()
    {
        foreach (var row in BrandPriceRows)
        {
            row.Refresh();
        }
    }

    /// <summary>
    /// The header's saved costs in the Per unit: to 4 decimals on the base unit, whose costs are fractions of a centavo,
    /// and to the centavo on any other.
    /// </summary>
    private void UpdateHeaderCostTexts()
    {
        HeaderCostUnitText = BufferedCostUnit?.Abbreviation ?? BaseUnitAbbreviation;
        CurrentCostText = SelectedRawMaterial is { } material ? FormatInBufferedCostUnit(material.CostPerBaseUnit) : "—";

        // A made ingredient's buffered cost follows the form's BOM instead of the saved row, so
        // the Total Cost and Total Weight tiles beside it always divide out to it as the grid
        // is typed into; Save then stores the same figure. Every other material shows what was last saved.
        BufferedCostText = IsEditingMade
            ? FormatInBufferedCostUnit(BomTotalWeight > 0 ? BomTotalCost / BomTotalWeight : 0m)
            : SelectedRawMaterial is { } saved ? FormatInBufferedCostUnit(saved.BufferedCostPerBaseUnit) : "—";
    }

    /// <summary>
    /// The Buffered Cost unit picker's choices: the base unit, then each alternative unit with a multiplier. A picked unit
    /// that's gone is swapped for the base unit; one that's still there is kept, as the new list's own copy.
    /// </summary>
    private void RebuildBufferedCostUnitOptions()
    {
        List<PurchaseUnitChoice> choices =
        [
            new(PurchaseUnitChoice.BaseUnitKey, BaseUnitName, BaseUnitAbbreviation),
            .. UnitRows
                .Where(r => r.Unit is { UnitOfMeasureId: > 0, BaseUnitsPerUnit: > 0 } && FindUnitOfMeasure(r.Unit.UnitOfMeasureId) is not null)
                .Select(r => FindUnitOfMeasure(r.Unit.UnitOfMeasureId)!)
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
            this.RaisePropertyChanged(nameof(BufferedCostUnit));
        }
        finally
        {
            _rebuildingBufferedCostUnits = false;
        }
    }

    /// <summary>The unit of the brand price the cost comes from as the grid now is, among the picker's choices; the base unit while unpriced.</summary>
    private PurchaseUnitChoice DearestBrandUnit()
    {
        var priced = BrandPriceRows.Where(r => r.CostPerBaseUnit is not null).ToList();
        var key = CostBrandIndex(priced) is { } source ? priced[source].UnitKey : null;
        return BufferedCostUnitOptions.FirstOrDefault(c => c.Key == key) ?? BufferedCostUnitOptions[0];
    }

    /// <summary>
    /// What Save costs the material at per base unit, as the grid now is: the preferred brand's price, else the dearest
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
    /// The Basis picker's choices as the grid now is: the dearest brand, each named brand once, and the preferred brand
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

        this.RaisePropertyChanged(nameof(CostFrom));
    }

    /// <summary>
    /// The summary of the grid: the dearest brand per base unit, as Save will cost the material, and what the fixed
    /// buffered cost comes to over it.
    /// </summary>
    private void UpdatePurchaseCostTexts()
    {
        foreach (var row in BrandPriceRows)
        {
            row.RefreshBufferPercent();
        }

        RebuildBufferedCostUnitOptions();
        RebuildCostFromOptions();
        this.RaisePropertyChanged(nameof(EditingBufferedCostInUnit));
        UpdateHeaderCostTexts();

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
        var unit = BufferedCostUnit?.Abbreviation ?? BaseUnitAbbreviation;

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
            EditingCostBufferPercent = Math.Clamp(Math.Round(over, 4), 0m, 100m);
            PurchaseBufferedCostText = $"{FormatInBufferedCostUnit(buffered)}/{unit} is {over:0.##}% over {source}'s {FormatInBufferedCostUnit(cost)}/{unit}.";
            // Compared to the 4 places costs per base unit are shown to: a saved buffered cost comes back cut to its
            // column's 20 places, so a repeating one (170/240) would otherwise read as a hair under an unchanged dearest brand.
            BufferedCostWarningText = Math.Round(buffered, 4) < Math.Round(cost, 4)
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
    /// with nothing typed into it is left out. Throws InvalidOperationException for the first problem, which
    /// RunSafelyAsync shows.
    /// </summary>
    private List<(string? Brand, int? UnitOfMeasureId, decimal Price)> CompleteBrandPrices()
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

            if (key != PurchaseUnitChoice.BaseUnitKey && ((IBrandPriceRowHost)this).FindUnit(key) is null)
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
}
