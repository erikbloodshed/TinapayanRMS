using System.Collections.ObjectModel;
using ReactiveUI;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// One row of the Products screen's Packs table: a sellable pack, with its own packaging lines under it.
/// Wraps the stored <see cref="ProductPack"/> rather than copying its fields, so the pack's id survives a
/// save and <c>SetPacksAsync</c> updates it instead of deleting and recreating it — the same shape
/// <c>RawMaterialUnitRow</c> uses for alternative units.
/// <para>
/// <see cref="MaterialCost"/> and <see cref="SellingPrice"/> are not worked out here: the screen sets them
/// from the swapped-in <c>ProductCostSummary</c>, so a pack's figures change in the same step as every
/// other cost on the screen.
/// </para>
/// </summary>
public sealed class ProductPackRow : ReactiveObject
{
    public ProductPackRow(ProductPack pack, Func<int, RawMaterial?> findMaterial)
    {
        Pack = pack;
        NewLineEdit = () => new PackagingLineRow(findMaterial);
    }

    /// <summary>The stored pack this row edits. Its Id is 0 until the row has been saved once.</summary>
    public ProductPack Pack { get; }

    /// <summary>This pack's own packaging lines — its box, label and ribbon, each per pack.</summary>
    public ObservableCollection<PackagingLineRow> LineEdits { get; } = [];

    /// <summary>The add row's factory for this pack's own lines grid (controls:GridAddNewRow.ItemFactory).</summary>
    public Func<PackagingLineRow> NewLineEdit { get; }

    public string Name
    {
        get => Pack.Name;
        set
        {
            if (Pack.Name == value)
            {
                return;
            }

            this.RaisePropertyChanging();
            Pack.Name = value;
            this.RaisePropertyChanged();
        }
    }

    public int PiecesPerPack
    {
        get => Pack.PiecesPerPack;
        set
        {
            if (Pack.PiecesPerPack == value)
            {
                return;
            }

            this.RaisePropertyChanging();
            Pack.PiecesPerPack = value;
            this.RaisePropertyChanged();
        }
    }

    private PackagingLineRow? _selectedLineEdit;
    /// <summary>The line the pack's own grid has selected; the Remove button acts on it.</summary>
    public PackagingLineRow? SelectedLineEdit
    {
        get => _selectedLineEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedLineEdit, value);
    }

    /// <summary>What this pack's own packaging costs, per pack: the box, the label, the ribbon.</summary>
    public decimal LineCost => LineEdits.Sum(l => l.CostPerUnit);

    private decimal _materialCost;
    /// <summary>The pack's material cost, set by the screen from the cost summary.</summary>
    public decimal MaterialCost
    {
        get => _materialCost;
        set => this.RaiseAndSetIfChanged(ref _materialCost, value);
    }

    private decimal _sellingPrice;
    /// <summary>The pack's selling price with VAT, set by the screen from the cost summary.</summary>
    public decimal SellingPrice
    {
        get => _sellingPrice;
        set => this.RaiseAndSetIfChanged(ref _sellingPrice, value);
    }

    /// <summary>Raised so the pack's cost follows a line the user just typed into.</summary>
    public void RaiseLineCostChanged() => this.RaisePropertyChanged(nameof(LineCost));
}
