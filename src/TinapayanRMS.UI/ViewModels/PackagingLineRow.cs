using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// One row of the Recipe Structure's Packaging table, which is typed into directly. The grid's material cell sets
/// <see cref="PackagingMaterialId"/>; the screen points <see cref="PackagingMaterial"/> at that material
/// (<see cref="Resolve"/>), which prices the row. The quantity is always in the material's own base unit.
/// A row can be half filled in: <see cref="CompleteLines"/> checks them all before they're saved.
/// </summary>
public sealed class PackagingLineRow : ReactiveObject
{
    /// <summary>Looks up a packaging material by id as the screen has it; null for a row the screen resolves itself.</summary>
    private readonly Func<int, RawMaterial?>? _findMaterial;

    public PackagingLineRow()
    {
    }

    /// <summary>
    /// A new row that prices itself as soon as its material is picked. The grid's add row edits one of these
    /// (controls:GridAddNewRow.ItemFactory): until it's committed it isn't in the screen's lines, so the screen
    /// can't resolve it.
    /// </summary>
    public PackagingLineRow(Func<int, RawMaterial?> findMaterial)
    {
        _findMaterial = findMaterial;
    }

    /// <param name="qtyPerUnit">How much of the material, in its base unit, one piece — or one pack — takes.</param>
    public PackagingLineRow(RawMaterial packagingMaterial, decimal qtyPerUnit)
    {
        _packagingMaterialId = packagingMaterial.Id;
        _packagingMaterial = packagingMaterial;
        _qtyPerUnit = qtyPerUnit;
    }

    private int? _packagingMaterialId;
    public int? PackagingMaterialId
    {
        get => _packagingMaterialId;
        set
        {
            this.RaiseAndSetIfChanged(ref _packagingMaterialId, value);
            if (_findMaterial is not null)
            {
                PackagingMaterial = value is { } id ? _findMaterial(id) : null;
            }
        }
    }

    private RawMaterial? _packagingMaterial;
    /// <summary>The material <see cref="PackagingMaterialId"/> names, as the screen last loaded it (its BaseUnit loaded).</summary>
    public RawMaterial? PackagingMaterial
    {
        get => _packagingMaterial;
        private set
        {
            this.RaiseAndSetIfChanged(ref _packagingMaterial, value);
            this.RaisePropertyChanged(nameof(Unit));
            this.RaisePropertyChanged(nameof(UnitCost));
            this.RaisePropertyChanged(nameof(CostPerUnit));
        }
    }

    private decimal? _qtyPerUnit;
    /// <summary>
    /// How much of the material one piece takes, in its base unit: 1 box, 30 cm of ribbon. Null until it's typed, so the
    /// grid's cell is empty rather than 0.0000 (AllowNullValue).
    /// </summary>
    public decimal? QtyPerUnit
    {
        get => _qtyPerUnit;
        set
        {
            this.RaiseAndSetIfChanged(ref _qtyPerUnit, value);
            this.RaisePropertyChanged(nameof(CostPerUnit));
        }
    }

    /// <summary>The material's base unit, which the quantity is in; blank until a material is picked.</summary>
    public string Unit => PackagingMaterial is { } material ? BaseUnitText.AbbreviationOf(material) : string.Empty;

    /// <summary>What one base unit of the material costs, buffered: what products are costed with.</summary>
    public decimal UnitCost => PackagingMaterial?.BufferedCostPerBaseUnit ?? 0m;

    public decimal CostPerUnit => ProductRules.CalculatePackagingLineCost(UnitCost, QtyPerUnit ?? 0m, baseUnitsPerEntryUnit: null);

    /// <summary>Nothing typed into it: the grid's add row left as it was, which isn't a line at all.</summary>
    private bool IsBlank => PackagingMaterialId is null && QtyPerUnit is null or 0m;

    /// <summary>
    /// Points each row at its material in <paramref name="materialsById"/> (the screen's current copies, so a
    /// refreshed cost shows). A row whose material isn't there keeps the one it has, if it's the same.
    /// </summary>
    public static void Resolve(IEnumerable<PackagingLineRow> rows, IReadOnlyDictionary<int, RawMaterial> materialsById)
    {
        foreach (var row in rows)
        {
            var resolved = row.PackagingMaterialId is { } id
                ? materialsById.GetValueOrDefault(id) ?? (row.PackagingMaterial?.Id == id ? row.PackagingMaterial : null)
                : null;
            if (!ReferenceEquals(resolved, row.PackagingMaterial))
            {
                row.PackagingMaterial = resolved;
            }
        }
    }

    /// <summary>
    /// The rows as lines, leaving out blank ones. A row with only a material or only a quantity is refused, and so
    /// is a material on two rows, each with a message naming it.
    /// </summary>
    /// <param name="unitNoun">What one of these lines packs, for the messages: "piece", or a pack's name.</param>
    public static IReadOnlyList<(RawMaterial PackagingMaterial, decimal QtyPerUnit)> CompleteLines(
        IEnumerable<PackagingLineRow> rows, string unitNoun = "piece")
    {
        List<(RawMaterial PackagingMaterial, decimal QtyPerUnit)> lines = [];
        foreach (var row in rows.Where(r => !r.IsBlank))
        {
            if (row.PackagingMaterial is not { } material)
            {
                throw new InvalidOperationException($"Pick the packaging material for the {row.QtyPerUnit:#,0.####} per {unitNoun} line, or delete that line.");
            }

            if (row.QtyPerUnit is not > 0m)
            {
                throw new InvalidOperationException($"Enter how much {material.Name} one {unitNoun} takes, or delete its line.");
            }

            if (lines.Any(l => l.PackagingMaterial.Id == material.Id))
            {
                throw new InvalidOperationException(
                    $"{material.Name} is on two of the same {unitNoun}'s packaging lines — put its quantity on one line and delete the other.");
            }

            lines.Add((material, row.QtyPerUnit.Value));
        }

        return lines;
    }
}
