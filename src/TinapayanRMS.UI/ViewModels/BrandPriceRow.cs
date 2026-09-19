using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A unit the Purchase Cost Setup grid's unit cell offers: the material's base unit (<see cref="Key"/> 0), or a unit of
/// measure by its id, whether the material has it as an alternative unit yet or not. Picking one it doesn't have adds it.
/// </summary>
public sealed record PurchaseUnitChoice(int Key, string Name, string Abbreviation)
{
    /// <summary>The base unit's key: it has no unit of measure row of its own for an ingredient (the gram).</summary>
    public const int BaseUnitKey = 0;
}

/// <summary>
/// One row of the Purchase Cost Setup grid, which is typed into directly: a brand, the unit it's bought in, and its price.
/// The unit cell sets <see cref="UnitKey"/> (a <see cref="PurchaseUnitChoice"/>); the screen tells the row how many base
/// units that unit holds, and a unit the material didn't have yet takes its multiplier from <see cref="Quantity"/>.
/// A row can be half filled in; Save checks them all first.
/// </summary>
public sealed class BrandPriceRow : ReactiveObject
{
    private readonly IBrandPriceRowHost? _host;

    /// <summary>An empty row, for the grid to compare an add row against (controls:GridAddNewRow.DropWhenUntouched).</summary>
    public BrandPriceRow()
    {
    }

    /// <summary>A row that follows the screen's units and buffer, even while it's still the grid's add row.</summary>
    public BrandPriceRow(IBrandPriceRowHost host)
    {
        _host = host;
    }

    public BrandPriceRow(IBrandPriceRowHost host, string? brand, int unitKey, decimal price)
        : this(host)
    {
        _brand = brand;
        _unitKey = unitKey;
        _price = price;
    }

    private string? _brand;
    /// <summary>Optional: a material bought in one brand needn't name it.</summary>
    public string? Brand
    {
        get => _brand;
        set => this.RaiseAndSetIfChanged(ref _brand, value);
    }

    private int? _unitKey;
    /// <summary>The unit picked (a <see cref="PurchaseUnitChoice.Key"/>); null until one is.</summary>
    public int? UnitKey
    {
        get => _unitKey;
        set
        {
            if (value == _unitKey)
            {
                return;
            }

            _unitKey = value;
            this.RaisePropertyChanged();
            _host?.OnUnitPicked(this);
            Refresh();
        }
    }

    private decimal? _price;
    /// <summary>The price of one of <see cref="UnitKey"/>'s unit: one sack, one roll.</summary>
    public decimal? Price
    {
        get => _price;
        set
        {
            this.RaiseAndSetIfChanged(ref _price, value);
            this.RaisePropertyChanged(nameof(BufferPercent));
        }
    }

    /// <summary>
    /// How many base units one of the unit holds: 1 for the base unit, the alternative unit's multiplier otherwise, empty
    /// until one is typed for a unit the material didn't have yet. Only that one's can be typed here
    /// (<see cref="CanTypeQuantity"/>); a saved unit's multiplier is corrected in the Alternative Units table.
    /// </summary>
    public decimal? Quantity
    {
        get => UnitKey switch
        {
            null => null,
            PurchaseUnitChoice.BaseUnitKey => 1m,
            { } key => _host?.FindUnit(key) is { BaseUnitsPerUnit: > 0 } unit ? unit.BaseUnitsPerUnit : null,
        };
        set
        {
            if (CanTypeQuantity && value != Quantity)
            {
                _host!.SetMultiplier(UnitKey!.Value, value ?? 0m);
                this.RaisePropertyChanged();
            }
        }
    }

    /// <summary>Whether <see cref="Quantity"/> can be typed: the unit is one the material doesn't have saved yet.</summary>
    public bool CanTypeQuantity =>
        UnitKey is { } key && key != PurchaseUnitChoice.BaseUnitKey && _host?.FindUnit(key) is { Id: 0 };

    /// <summary>
    /// How far the material's buffered cost is over this brand's price, in percent: the buffered cost for one of the row's
    /// unit, less the price, over the price. ₱976.50 a sack over ₱930 is 5%; a cheaper brand's is higher. Empty until the
    /// material has a buffered cost (or a price for Save to work one out from), and until the row has a price and a quantity.
    /// </summary>
    public decimal? BufferPercent =>
        _host?.BufferedCostPerBaseUnit is { } buffered && Quantity is > 0 && Price is > 0
            ? RawMaterialRules.CostBufferPercentOf(Price.Value, buffered * Quantity.Value)
            : null;

    /// <summary>What the price comes to per base unit, once there's a price and a quantity.</summary>
    public decimal? CostPerBaseUnit =>
        Price is > 0 && Quantity is > 0 ? UnitConversionRules.CostPerBaseUnit(Price.Value, Quantity) : null;

    /// <summary>Shows the screen's current units and buffered cost: after a unit's multiplier changes, or the buffered cost.</summary>
    public void Refresh()
    {
        this.RaisePropertyChanged(nameof(Quantity));
        this.RaisePropertyChanged(nameof(CanTypeQuantity));
        this.RaisePropertyChanged(nameof(BufferPercent));
    }

    /// <summary>Shows the screen's current buffered cost, which moves with the prices while Save is still to work it out.</summary>
    public void RefreshBufferPercent() => this.RaisePropertyChanged(nameof(BufferPercent));
}

/// <summary>What a <see cref="BrandPriceRow"/> needs from the screen it's on.</summary>
public interface IBrandPriceRowHost
{
    /// <summary>The form's alternative unit in the unit of measure <paramref name="unitOfMeasureId"/>, saved or not; null when it has none.</summary>
    RawMaterialUnit? FindUnit(int unitOfMeasureId);

    /// <summary>Sets a not-yet-saved alternative unit's multiplier, typed as the row's quantity.</summary>
    void SetMultiplier(int unitOfMeasureId, decimal baseUnitsPerUnit);

    /// <summary>
    /// The form's fixed buffered cost per base unit, or while it has none, what Save will work out from the dearest
    /// brand and the buffer percent; null while there's neither.
    /// </summary>
    decimal? BufferedCostPerBaseUnit { get; }

    /// <summary>A unit was picked on the row: one the material doesn't have yet is added as an alternative unit.</summary>
    void OnUnitPicked(BrandPriceRow row);
}
