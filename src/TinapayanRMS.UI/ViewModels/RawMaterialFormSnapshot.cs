using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// One alternative unit, by value. RawMaterialUnitRow writes straight through into the stored RawMaterialUnit, and
/// Save hands those very entities to the repository, so neither the row nor the entity can be held by reference.
/// <paramref name="Id"/> has to survive for the same reason a pack's does: a unit that lost its id would be saved
/// as a new one and the stored one deleted.
/// </summary>
public readonly record struct UnitRowState(int Id, int RawMaterialId, int? UnitOfMeasureId, decimal BaseUnitsPerUnit, string BaseUnitAbbreviation);

/// <summary>
/// One brand price, by what the user typed. Its Quantity isn't carried: the row has no field of its own for it and
/// reads it back through the unit it names, which <see cref="RawMaterialFormSnapshot.UnitRows"/> already holds — so
/// capturing it here would record the same figure twice and restore it twice.
/// </summary>
public readonly record struct BrandPriceState(string? Brand, int? UnitKey, decimal? Price);

/// <summary>
/// The Raw Materials screen's editable state at one instant: everything one undo step puts back.
///
/// Two things are deliberately absent. The **price history** is a log rather than part of the form, and its Delete
/// writes to the database on the spot — undoing that would mean putting a row back, not restoring unsaved state.
/// The **usage example** is a calculator whose result belongs to the inputs it was worked out from; nothing about
/// it is saved, so an undo has no business moving it.
/// </summary>
public sealed record RawMaterialFormSnapshot
{
    public required int EditingId { get; init; }
    public required string Name { get; init; }
    public required RawMaterialType Type { get; init; }
    public required int? CategoryId { get; init; }
    public required RawMaterialSource Source { get; init; }
    public required bool CanChangeTypeAndSource { get; init; }

    public required int? BaseUnitId { get; init; }
    public required decimal? WeightLossPercent { get; init; }
    public required int? SubAssemblyId { get; init; }

    public required int? LinkedToId { get; init; }
    public required decimal? WeightSharePercent { get; init; }
    public required decimal? CostSharePercent { get; init; }

    public required decimal? CostBufferPercent { get; init; }
    public required decimal? TargetCostBufferPercent { get; init; }
    public required string? PreferredBrand { get; init; }
    public required decimal? BufferedCost { get; init; }
    public required int? BufferedCostUnitKey { get; init; }

    public required IReadOnlyList<UnitRowState> UnitRows { get; init; }
    public required IReadOnlyList<BrandPriceState> BrandPrices { get; init; }
    /// <summary>A made ingredient's add-ins. Its sub-assembly's lines aren't the form's: they follow <see cref="SubAssemblyId"/>.</summary>
    public required IReadOnlyList<IngredientLineState> AddIns { get; init; }

    /// <summary>
    /// True when two snapshots would look the same to the user. Written out rather than left to the record's own
    /// equality, which compares the list members by reference.
    /// </summary>
    public bool ShowsSameAs(RawMaterialFormSnapshot other) =>
        EditingId == other.EditingId
        && Name == other.Name
        && Type == other.Type
        && CategoryId == other.CategoryId
        && Source == other.Source
        && CanChangeTypeAndSource == other.CanChangeTypeAndSource
        && BaseUnitId == other.BaseUnitId
        && WeightLossPercent == other.WeightLossPercent
        && SubAssemblyId == other.SubAssemblyId
        && LinkedToId == other.LinkedToId
        && WeightSharePercent == other.WeightSharePercent
        && CostSharePercent == other.CostSharePercent
        && CostBufferPercent == other.CostBufferPercent
        && TargetCostBufferPercent == other.TargetCostBufferPercent
        && PreferredBrand == other.PreferredBrand
        && BufferedCost == other.BufferedCost
        && BufferedCostUnitKey == other.BufferedCostUnitKey
        && UnitRows.SequenceEqual(other.UnitRows)
        && BrandPrices.SequenceEqual(other.BrandPrices)
        && AddIns.SequenceEqual(other.AddIns);
}
