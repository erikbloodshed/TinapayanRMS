using System.Collections.ObjectModel;
using DynamicData;
using DynamicData.Binding;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A packaging set's lines on the Sub-Assemblies screen (through SubAssembliesViewModel.PackagingSet): how much of each
/// packaging material one set takes, typed straight into its table, and what the set costs, which follows them. It shows
/// them as saved, keeps and puts them back for an undo step, prices them, and reads them as Save writes them. The screen
/// keeps the undo history, the Remove Line its recipe's table shares, and says when their materials were read again.
/// </summary>
public sealed class SubAssemblyPackagingSet : ObservableState
{
    private readonly IReadOnlyList<RawMaterial> _packagingMaterials;

    /// <param name="packagingMaterials">The packaging materials as the screen last loaded them, which a line prices itself from.</param>
    /// <param name="updates">The screen's derived updates, so an undo putting the lines back works the cost out once.</param>
    public SubAssemblyPackagingSet(IReadOnlyList<RawMaterial> packagingMaterials, DerivedUpdates updates)
    {
        _packagingMaterials = packagingMaterials;

        // A line prices itself as its material is picked, and the set's cost follows.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .Subscribe(_ => updates.Run(ResolveRows));
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ => updates.Run(RecomputeCost));
    }

    /// <summary>The set's lines, typed straight into the table: how much of each material one set takes.</summary>
    public ObservableCollection<PackagingLineRow> Lines { get; } = [];

    /// <summary>The line the table's add row edits: it prices itself as its material is picked.</summary>
    public Func<PackagingLineRow> NewLine => () => new PackagingLineRow(FindMaterial);

    private PackagingLineRow? _selectedLine;
    public PackagingLineRow? SelectedLine
    {
        get => _selectedLine;
        set => RaiseAndSetIfChanged(ref _selectedLine, value);
    }

    private decimal _cost;
    /// <summary>What one packaging set costs, at its materials' buffered costs: a linking product's packaging per piece.</summary>
    public decimal Cost
    {
        get => _cost;
        private set => RaiseAndSetIfChanged(ref _cost, value);
    }

    /// <summary>A packaging material as the screen last loaded it, for a line that prices itself.</summary>
    private RawMaterial? FindMaterial(int id) => _packagingMaterials.FirstOrDefault(m => m.Id == id);

    /// <summary>The lines price themselves as their material is picked, at the materials as the screen last read them.</summary>
    public void ResolveRows() => PackagingLineRow.Resolve(Lines, _packagingMaterials.ToDictionary(m => m.Id));

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
        var packagingById = _packagingMaterials.ToDictionary(m => m.Id);
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
            Lines.Add(PackagingLineRow.Restore(line, FindMaterial));
        }
    }

    /// <summary>The lines as Save writes them, those filled in; a line half filled in is refused (BusinessRuleException).</summary>
    public List<(int PackagingMaterialId, int? UnitId, decimal QtyPerUnit)> CurrentLines() =>
        [.. PackagingLineRow.CompleteLines(Lines, "set").Select(l => (l.PackagingMaterial.Id, l.UnitId, l.QtyPerUnit))];
}
