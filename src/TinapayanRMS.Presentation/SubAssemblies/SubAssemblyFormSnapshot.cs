using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.SubAssemblies;

/// <summary>
/// The Sub-Assemblies screen's editable state at one instant: everything one undo step puts back. A sub-assembly
/// holds one kind of line or the other, as its role says, so both collections are carried and one of them is
/// normally empty. Left out is everything worked out from these — the batch weight and cost, the cost per gram, a
/// set's cost, the formula base caption and the percentage header — which the one recompute at the end of a restore
/// works out again, and the row picked in the list, which an undo never changes.
/// </summary>
public sealed record SubAssemblyFormSnapshot : IFormSnapshot<SubAssemblyFormSnapshot>
{
    public required int EditingId { get; init; }
    public required string Name { get; init; }
    public required int? RoleId { get; init; }

    /// <summary>A recipe sub-assembly's ingredient lines; empty for a packaging set.</summary>
    public required IReadOnlyList<IngredientLineSnapshot> Lines { get; init; }

    /// <summary>A packaging set's lines; empty for a recipe sub-assembly.</summary>
    public required IReadOnlyList<PackagingLineSnapshot> PackagingLines { get; init; }

    public required decimal FormulaBase { get; init; }

    /// <summary>
    /// True when two snapshots would look the same to the user. Written out rather than left to the record's own
    /// equality, which compares the list members by reference.
    /// </summary>
    public bool ShowsSameAs(SubAssemblyFormSnapshot other) =>
        EditingId == other.EditingId
        && Name == other.Name
        && RoleId == other.RoleId
        && Lines.SequenceEqual(other.Lines)
        && PackagingLines.SequenceEqual(other.PackagingLines)
        && FormulaBase == other.FormulaBase;
}
