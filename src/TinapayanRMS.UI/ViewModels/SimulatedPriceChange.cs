using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A row of the Price Simulator screen's Price Changes: a purchased material and the price it's tried at, typed as a change in
/// percent or as the new price per its purchase unit, the other worked out from it. Nothing is saved.
/// </summary>
/// <param name="find">The material with an id, and what it's bought in; null for one that isn't there.</param>
public sealed class SimulatedPriceChange(Func<int, (RawMaterial Material, PurchaseUnit Unit)?> find) : ReactiveObject
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
            this.RaisePropertyChanged(nameof(UnitName));
            this.RaisePropertyChanged(nameof(CurrentPrice));
            this.RaisePropertyChanged();
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

        this.RaisePropertyChanged(propertyName);
    }

    /// <summary>
    /// The material's costs per base unit at the new price, or null for a row not filled in yet. A price that can't be is
    /// refused (a BusinessRuleException): a fall of 100% or more, a price of 0, or a percentage on a material with no price.
    /// </summary>
    public (int MaterialId, decimal Cost, decimal BufferedCost)? NewCosts()
    {
        if (Material is not { } material || Unit is not { } unit)
        {
            return null;
        }

        if (_percentTyped && ChangePercent is { } percent)
        {
            if (unit.Price is null)
            {
                throw new BusinessRuleException($"{material.Name}: {PriceChangeRules.NoPriceToChangeText}");
            }

            PriceChangeRules.EnsureValidChange(percent);
        }

        if (NewPrice is not { } newPrice)
        {
            return null;
        }

        PriceChangeRules.EnsureValidNewPrice(newPrice);
        var (cost, bufferedCost) = PriceChangeRules.CostsAfter(material, unit.Price, newPrice, unit.BaseUnitsPerUnit);
        return (material.Id, cost, bufferedCost);
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
