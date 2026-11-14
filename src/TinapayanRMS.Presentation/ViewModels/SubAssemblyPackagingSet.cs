using System.Collections.ObjectModel;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A packaging set's lines on the Sub-Assemblies screen (through SubAssembliesViewModel.PackagingSet): how much of each
/// packaging material one set takes, typed straight into its table, and what the set costs. It shows them as saved, keeps
/// and puts them back for an undo step, prices them, and reads them as Save writes them. The screen keeps the undo
/// history, the Remove Line its recipe's table shares, and says when the lines changed or their materials were read again.
/// </summary>
/// <param name="packagingMaterials">The packaging materials as the screen last loaded them, which a line prices itself from.</param>
public sealed class SubAssemblyPackagingSet(IReadOnlyList<RawMaterial> packagingMaterials) : ReactiveObject
{
    /// <summary>The set's lines, typed straight into the table: how much of each material one set takes.</summary>
    public ObservableCollection<PackagingLineRow> Lines { get; } = [];

    /// <summary>The line the table's add row edits: it prices itself as its material is picked.</summary>
    public Func<PackagingLineRow> NewLine => () => new PackagingLineRow(FindMaterial);

    private PackagingLineRow? _selectedLine;
    public PackagingLineRow? SelectedLine
    {
        get => _selectedLine;
        set => this.RaiseAndSetIfChanged(ref _selectedLine, value);
    }

    private decimal _cost;
    /// <summary>What one packaging set costs, at its materials' buffered costs: a linking product's packaging per piece.</summary>
    public decimal Cost
    {
        get => _cost;
        private set => this.RaiseAndSetIfChanged(ref _cost, value);
    }

    /// <summary>A packaging material as the screen last loaded it, for a line that prices itself.</summary>
    private RawMaterial? FindMaterial(int id) => packagingMaterials.FirstOrDefault(m => m.Id == id);

    /// <summary>The lines price themselves as their material is picked, at the materials as the screen last read them.</summary>
    public void ResolveRows() => PackagingLineRow.Resolve(Lines, packagingMaterials.ToDictionary(m => m.Id));

    /// <summary>The set's cost follows its lines.</summary>
    public void RecomputeCost() => Cost = Lines.Sum(l => l.CostPerUnit);

    /// <summary>No lines, and none picked: a new sub-assembly's form.</summary>
    public void Clear()
    {
        SelectedLine = null;
        Lines.Clear();
    }

    /// <summary>The set's lines as saved, each finding its material as the screen last loaded it.</summary>
    public void Show(IEnumerable<SubAssemblyPackagingLine> saved)
    {
        var packagingById = packagingMaterials.ToDictionary(m => m.Id);
        SelectedLine = null;
        Lines.Clear();
        Lines.AddRange(saved.Select(l => new PackagingLineRow(packagingById.GetValueOrDefault(l.PackagingMaterialId) ?? l.PackagingMaterial!, l.QtyPerSet, l.UnitId)));
    }

    /// <summary>The lines as an undo step keeps them.</summary>
    public IReadOnlyList<PackagingLineState> Capture() => [.. Lines.Select(PackagingLineState.Of)];

    /// <summary>The lines an undo step kept, in place of those showing.</summary>
    public void Restore(IEnumerable<PackagingLineState> lines)
    {
        Lines.Clear();
        foreach (var line in lines)
        {
            // The material goes on first: it brings the units the row can be in, so the unit sticks.
            Lines.Add(new PackagingLineRow(FindMaterial)
            {
                PackagingMaterialId = line.PackagingMaterialId,
                UnitKey = line.UnitId ?? PackagingUnitChoice.BaseUnitKey,
                QtyPerUnit = line.QtyPerUnit,
            });
        }
    }

    /// <summary>The lines as Save writes them, those filled in; a line half filled in is refused (BusinessRuleException).</summary>
    public List<(int PackagingMaterialId, int? UnitId, decimal QtyPerUnit)> CurrentLines() =>
        [.. PackagingLineRow.CompleteLines(Lines, "set").Select(l => (l.PackagingMaterial.Id, l.UnitId, l.QtyPerUnit))];
}
