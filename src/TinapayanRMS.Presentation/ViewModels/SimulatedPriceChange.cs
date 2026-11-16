using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A row of the Price Simulator screen's Price Changes: a purchased material and the price it's tried at, typed as a change in
/// percent or as the new price per its purchase unit, the other worked out from it. Saved only as part of a scenario
/// (ToScenarioLine), which changes no real price.
/// </summary>
/// <param name="find">The material with an id, and what it's bought in; null for one that isn't there.</param>
public sealed class SimulatedPriceChange(Func<int, (RawMaterial Material, PurchaseUnit Unit)?> find) : ObservableRow
{
    // Which of the two was typed last, the other following it; and set while the other is being worked out.
    private bool _percentTyped;
    private bool _following;

    private int? _materialId;
    /// <summary>The material picked, by id (the picker's value). Picking the same one again points the row at a fresh copy of it.</summary>
    public int? MaterialId
    {
        get => _materialId;
        set
        {
            // The row is put right before anyone hears of the change, since what hears of it reads the row straight away.
            _materialId = value;
            var found = value is { } id ? find(id) : null;
            Material = found?.Material;
            Unit = found?.Unit;
            Follow();
            RaisePropertyChanged(nameof(UnitName));
            RaisePropertyChanged(nameof(CurrentPrice));
            RaisePropertyChanged();
        }
    }

    public RawMaterial? Material { get; private set; }

    public PurchaseUnit? Unit { get; private set; }

    /// <summary>What the prices are per: "sack".</summary>
    public string? UnitName => Unit?.Name;

    /// <summary>What one costs now; null for a material with no price.</summary>
    public decimal? CurrentPrice => Unit?.Price;

    private decimal? _changePercent;
    public decimal? ChangePercent
    {
        get => _changePercent;
        set => Type(ref _changePercent, value, percentTyped: true);
    }

    private decimal? _newPrice;
    public decimal? NewPrice
    {
        get => _newPrice;
        set => Type(ref _newPrice, value, percentTyped: false);
    }

    /// <summary>
    /// Change % or New Price typed (or worked out, while following): the other follows, and only then is the change told,
    /// since what hears of it reads the row straight away (the Price Simulator screen works its figures out again).
    /// </summary>
    private void Type(ref decimal? field, decimal? value, bool percentTyped, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
    {
        if (field == value)
        {
            return;
        }

        field = value;
        if (!_following)
        {
            _percentTyped = percentTyped;
            Follow();
        }

        RaisePropertyChanged(propertyName);
    }

    /// <summary>
    /// The material's costs per base unit at the new price, or null for a row not filled in yet. A price that can't be is
    /// refused (a BusinessRuleException): a fall of 100% or more, a price of 0, or a percentage on a material with no price.
    /// </summary>
    public (int MaterialId, decimal Cost, decimal BufferedCost)? NewCosts() =>
        Material is { } material && Unit is { } unit
            && TriedPriceChange.Of(material, unit, ChangePercent, NewPrice, _percentTyped) is { } tried
            ? (material.Id, tried.Cost, tried.BufferedCost)
            : null;

    /// <summary>
    /// What was typed for the row, as a saved scenario keeps it: the material and the one of Change % and New Price that
    /// was typed. Null for a row with no purchased material picked, or nothing typed.
    /// </summary>
    public PriceScenarioLine? ToScenarioLine()
    {
        if (Material is null || MaterialId is not { } id)
        {
            return null;
        }

        return (_percentTyped, ChangePercent, NewPrice) switch
        {
            (true, { } percent, _) => new PriceScenarioLine { RawMaterialId = id, ChangePercent = percent },
            (false, _, { } price) => new PriceScenarioLine { RawMaterialId = id, NewPrice = price },
            _ => null,
        };
    }

    /// <summary>
    /// Fills the row from a saved scenario's line, as typing it would: a change in percent goes on the material's price
    /// now, and a new price stays as it was saved.
    /// </summary>
    public void Open(PriceScenarioLine line)
    {
        MaterialId = line.RawMaterialId;
        if (line.ChangePercent is { } percent)
        {
            ChangePercent = percent;
        }
        else
        {
            NewPrice = line.NewPrice;
        }
    }

    /// <summary>Works out whichever of Change % and New Price wasn't typed, from the one that was and the current price.</summary>
    private void Follow()
    {
        _following = true;
        try
        {
            if (_percentTyped)
            {
                NewPrice = CurrentPrice is { } current && ChangePercent is { } percent ? PriceChangeRules.NewPrice(current, percent) : null;
            }
            else
            {
                ChangePercent = CurrentPrice is { } current && current > 0m && NewPrice is { } price ? PriceChangeRules.ChangePercent(current, price) : null;
            }
        }
        finally
        {
            _following = false;
        }
    }
}
