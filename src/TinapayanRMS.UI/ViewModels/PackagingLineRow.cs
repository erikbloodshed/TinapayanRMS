using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A unit a packaging line's quantity can be entered in: the material's base unit (<see cref="Key"/> 0), or one of
/// its alternative units by <see cref="RawMaterialUnit"/> id. The same shape as the Purchase Cost Setup grid's
/// <see cref="PurchaseUnitChoice"/>, and for the same reason: the grid's cell binds a scalar key, not an object.
/// <see cref="UnitCost"/> is what one of this unit costs, buffered unless the row is switched to its true cost — the popup's own first column, so picking a
/// unit previews what it costs before it's committed.
/// </summary>
public sealed record PackagingUnitChoice(int Key, string Abbreviation, decimal BaseUnitsPerUnit, decimal UnitCost)
{
    /// <summary>The base unit's key: it has no <see cref="RawMaterialUnit"/> row of its own, so a line in it stores no unit.</summary>
    public const int BaseUnitKey = 0;
}

/// <summary>
/// One row of the Recipe Structure's Packaging table and of a packaging set's Lines table, both typed into directly.
/// The grid's material cell sets <see cref="PackagingMaterialId"/>; the screen points <see cref="PackagingMaterial"/>
/// at that material (<see cref="Resolve"/>), which prices the row and fills <see cref="UnitOptions"/>. The quantity is
/// in <see cref="UnitKey"/>'s unit — the material's base unit, or one of its alternative units — and
/// <see cref="BaseQty"/> is what that comes to in base units — never shown, but what the line's total cost is
/// worked out from; <see cref="UnitCost"/> is what one of the *picked* unit costs, which follows <see cref="UnitKey"/>.
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

    /// <param name="qtyPerUnit">How much of the material, in <paramref name="unitId"/>'s unit, one piece — or one set — takes.</param>
    /// <param name="unitId">One of the material's alternative units, or null for its base unit.</param>
    public PackagingLineRow(RawMaterial packagingMaterial, decimal qtyPerUnit, int? unitId = null)
    {
        _packagingMaterialId = packagingMaterial.Id;
        _packagingMaterial = packagingMaterial;
        _qtyPerUnit = qtyPerUnit;
        _unitOptions = OptionsFor(packagingMaterial, CostBasis.Buffered);
        _unitKey = KeptKey(unitId ?? PackagingUnitChoice.BaseUnitKey, _unitOptions);
    }

    /// <summary>
    /// A saved packaging line as a table row, its quantity in the unit it was entered in. A line saved as pieces per unit
    /// shows as the quantity it comes to, at the same cost, and is saved that way once the packaging changes.
    /// </summary>
    public static PackagingLineRow FromSaved(PackagingLine line) => FromSaved(line, CostBasis.Buffered);

    /// <summary>The same, priced at <paramref name="basis"/>.</summary>
    public static PackagingLineRow FromSaved(PackagingLine line, CostBasis basis)
    {
        var authoredValue = line.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? line.QtyPerUnit : line.UnitsPerPackagingUnit;
        var qtyPerUnit = ProductRules.ReconcileReciprocal(line.AuthoredField, authoredValue).QtyPerUnit;
        return new PackagingLineRow(line.PackagingMaterial!, qtyPerUnit, line.UnitId) { CostBasis = basis };
    }

    private CostBasis _costBasis;
    /// <summary>
    /// Which of the material's costs the row is priced at: buffered, as every screen prices packaging, unless the
    /// Products screen has been switched to its unbuffered costs. The unit choices are priced again, and the picked
    /// unit stays.
    /// </summary>
    public CostBasis CostBasis
    {
        get => _costBasis;
        set
        {
            if (_costBasis == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _costBasis, value);
            var key = _unitKey;
            UnitOptions = OptionsFor(_packagingMaterial, value);
            UnitKey = KeptKey(key, _unitOptions);
            this.RaisePropertyChanged(nameof(UnitCost));
            this.RaisePropertyChanged(nameof(CostPerUnit));
        }
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
            UnitOptions = OptionsFor(value, _costBasis);
            this.RaisePropertyChanged(nameof(UnitCost));
            this.RaisePropertyChanged(nameof(BaseUnitAbbreviation));
            this.RaisePropertyChanged(nameof(BaseQty));
            this.RaisePropertyChanged(nameof(CostPerUnit));
        }
    }

    private IReadOnlyList<PackagingUnitChoice> _unitOptions = [];
    /// <summary>
    /// The units this row's quantity can be entered in: the material's base unit first, then its alternative units
    /// (at most two, <see cref="UnitConversionRules.MaxAlternativeUnits"/>). The grid's unit cell reads them per row
    /// through <c>PackagingUnitSelector</c>, since each row has its own material.
    /// </summary>
    public IReadOnlyList<PackagingUnitChoice> UnitOptions
    {
        get => _unitOptions;
        private set
        {
            this.RaiseAndSetIfChanged(ref _unitOptions, value);

            // A unit the new material doesn't have falls back to its base unit, so the row is never in a unit
            // that isn't the material's — which the repository would refuse at Save.
            UnitKey = KeptKey(_unitKey, value);
        }
    }

    private int _unitKey = PackagingUnitChoice.BaseUnitKey;
    /// <summary>
    /// The unit the quantity is typed in (a <see cref="PackagingUnitChoice.Key"/>); the base unit until one is picked.
    /// A unit this row's material doesn't have falls back to the base unit — the grid's cell can only offer the
    /// material's own, but a restored undo step or a snapshot carries a key that may no longer fit, and the
    /// repository would refuse it at Save.
    /// </summary>
    public int UnitKey
    {
        get => _unitKey;
        set
        {
            this.RaiseAndSetIfChanged(ref _unitKey, KeptKey(value, _unitOptions));
            this.RaisePropertyChanged(nameof(BaseQty));
            this.RaisePropertyChanged(nameof(UnitCost));
            this.RaisePropertyChanged(nameof(CostPerUnit));
        }
    }

    /// <summary>The unit as a line stores it: null for the base unit, which has no row of its own.</summary>
    public int? UnitId => _unitKey == PackagingUnitChoice.BaseUnitKey ? null : _unitKey;

    /// <summary>Base units in one of the row's unit, or null when it's already the base unit.</summary>
    private decimal? BaseUnitsPerEntryUnit =>
        _unitKey == PackagingUnitChoice.BaseUnitKey
            ? null
            : _unitOptions.FirstOrDefault(o => o.Key == _unitKey)?.BaseUnitsPerUnit;

    private decimal? _qtyPerUnit;
    /// <summary>
    /// How much of the material one piece — or one set — takes, in the row's unit: 1 box, 30 cm of ribbon, 1 roll.
    /// Null until it's typed, so the grid's cell is empty rather than 0.0000 (AllowNullValue).
    /// </summary>
    public decimal? QtyPerUnit
    {
        get => _qtyPerUnit;
        set
        {
            this.RaiseAndSetIfChanged(ref _qtyPerUnit, value);
            this.RaisePropertyChanged(nameof(BaseQty));
            this.RaisePropertyChanged(nameof(CostPerUnit));
        }
    }

    /// <summary>The material's own base unit, which its cost is per; blank until a material is picked.</summary>
    public string BaseUnitAbbreviation => PackagingMaterial is { } material ? BaseUnitText.AbbreviationOf(material) : string.Empty;

    /// <summary>What the typed quantity comes to in the material's base unit: 1 roll of a 1,000 cm roll is 1,000.</summary>
    public decimal? BaseQty =>
        QtyPerUnit is { } qty ? UnitConversionRules.ToBaseQuantity(qty, BaseUnitsPerEntryUnit) : null;

    /// <summary>What one base unit of the material costs at <see cref="CostBasis"/>: the rate everything below is costed from.</summary>
    private decimal CostPerBaseUnit => PackagingMaterial is { } material ? RawMaterialRules.CostPerBaseUnitAt(material, CostBasis) : 0m;

    /// <summary>
    /// What one of the row's currently selected unit costs at <see cref="CostBasis"/> — what the grid's Unit Cost column shows, and
    /// the same figure each <see cref="UnitOptions"/> choice previews in the picker's popup before it's picked.
    /// ₱0.05 for the base unit (a cm of ribbon), ₱50.00 once a 1,000 cm roll is picked.
    /// </summary>
    public decimal UnitCost => _unitOptions.FirstOrDefault(o => o.Key == _unitKey)?.UnitCost ?? 0m;

    /// <summary>
    /// The line's total cost: <see cref="CostPerBaseUnit"/> — never <see cref="UnitCost"/>, which is already scaled
    /// to the picked unit and would double-convert if it fed this too — times the quantity in base units.
    /// </summary>
    public decimal CostPerUnit => ProductRules.CalculatePackagingLineCost(CostPerBaseUnit, QtyPerUnit ?? 0m, BaseUnitsPerEntryUnit);

    /// <summary>Nothing typed into it: the grid's add row left as it was, which isn't a line at all.</summary>
    private bool IsBlank => PackagingMaterialId is null && QtyPerUnit is null or 0m;

    /// <summary>The material's base unit first, then its alternative units, priced at <paramref name="basis"/>; empty until a material is picked.</summary>
    private static IReadOnlyList<PackagingUnitChoice> OptionsFor(RawMaterial? material, CostBasis basis)
    {
        if (material is null)
        {
            return [];
        }

        var costPerBaseUnit = RawMaterialRules.CostPerBaseUnitAt(material, basis);
        return
        [
            new(PackagingUnitChoice.BaseUnitKey, BaseUnitText.AbbreviationOf(material), 1m, costPerBaseUnit),
            .. material.Units
                .Where(u => u.UnitOfMeasure is not null)
                .Select(u => new PackagingUnitChoice(u.Id, u.UnitOfMeasure!.Abbreviation, u.BaseUnitsPerUnit, costPerBaseUnit * u.BaseUnitsPerUnit)),
        ];
    }

    /// <summary><paramref name="key"/> when <paramref name="options"/> still offers it, else the base unit.</summary>
    private static int KeptKey(int key, IReadOnlyList<PackagingUnitChoice> options) =>
        options.Any(o => o.Key == key) ? key : PackagingUnitChoice.BaseUnitKey;

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
                // The unit survives a re-resolve to the same material: its alternative units come back with it.
                var key = row._unitKey;
                row.PackagingMaterial = resolved;
                row.UnitKey = KeptKey(key, row.UnitOptions);
            }
        }
    }

    /// <summary>
    /// The rows as lines, leaving out blank ones. A row with only a material or only a quantity is refused, and so
    /// is a material on two rows, each with a message naming it.
    /// </summary>
    /// <param name="unitNoun">What one of these lines packs, for the messages: "piece", or "set".</param>
    public static IReadOnlyList<(RawMaterial PackagingMaterial, int? UnitId, decimal QtyPerUnit)> CompleteLines(
        IEnumerable<PackagingLineRow> rows, string unitNoun = "piece")
    {
        List<(RawMaterial PackagingMaterial, int? UnitId, decimal QtyPerUnit)> lines = [];
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

            lines.Add((material, row.UnitId, row.QtyPerUnit.Value));
        }

        return lines;
    }
}
