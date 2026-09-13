using ReactiveUI;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A unit a raw material can be priced or measured in: its base unit (no RawMaterialUnit) — the gram,
/// for an ingredient — or one of its alternative units. Used by every picker that chooses one: the
/// purchase unit, the usage example, and the BOM, sub-assembly and packaging line editors.
/// </summary>
public sealed class RawMaterialUnitOption(RawMaterialUnit? unit, RawMaterial material, bool isNew = false) : ReactiveObject
{
    public RawMaterialUnit? Unit { get; } = unit;

    /// <summary>
    /// A unit of measure the material doesn't have yet, offered by the Purchase Cost Setup: picking it
    /// adds it as an alternative unit, and it stops being new; picking another makes it new again.
    /// </summary>
    private bool _isNew = isNew;
    public bool IsNew
    {
        get => _isNew;
        set => this.RaiseAndSetIfChanged(ref _isNew, value);
    }

    public int? UnitId => Unit?.Id;

    public string Name => Unit?.UnitOfMeasure?.Name ?? BaseUnitText.NameOf(material);

    public string Abbreviation => Unit?.UnitOfMeasure?.Abbreviation ?? BaseUnitText.AbbreviationOf(material);

    public decimal BaseUnitsPerUnit => Unit?.BaseUnitsPerUnit ?? 1m;

    /// <summary>The name: what a ComboBoxAdv with an ItemTemplate (and so no DisplayMemberPath) shows in its closed box.</summary>
    public override string ToString() => Name;

    /// <summary>The material's base unit first (a packaging material's BaseUnit must be loaded), then its alternative units.</summary>
    public static List<RawMaterialUnitOption> For(RawMaterial? material) =>
        material is null ? [] : [new(null, material), .. material.Units.Select(u => new RawMaterialUnitOption(u, material))];
}
