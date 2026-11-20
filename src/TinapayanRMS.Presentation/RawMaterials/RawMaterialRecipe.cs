using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using DynamicData;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>What a made ingredient's recipe asks of its draft (RawMaterialDraft), on top of the undo recorder its steps go in.</summary>
public interface IRawMaterialRecipeHost : IUndoRecorder
{
    /// <summary>The details showing are the picked material's (RawMaterialForm.AreDetailsReady): the totals wait until they are.</summary>
    bool AreDetailsReady { get; }

    /// <summary>The form's Weight Loss %, which the batch's yield is less.</summary>
    decimal? WeightLossPercent { get; }

    /// <summary>The form's name, which a half-filled add-in's message names.</summary>
    string EditingName { get; }
}

/// <summary>
/// A made ingredient's recipe on the Raw Materials screen (through RawMaterialForm.Recipe; BillOfMaterials draws it):
/// the sub-assembly it's made from, shown as a badge with Edit once picked, that recipe's lines as it's saved on the
/// Sub-Assemblies screen, for show, the made ingredient's own add-ins on top, typed straight into their table, and the
/// batch's totals. The screen keeps the Weight Loss % and says when the recipe's lines are read again.
/// </summary>
public sealed class RawMaterialRecipe : ObservableState, IDisposable
{
    private readonly IRawMaterialRecipeHost _host;
    private readonly IReadOnlyList<RawMaterial> _ingredients;
    private readonly Subject<int> _subAssemblyOpenRequests = new();

    /// <summary>Each sub-assembly picked or shown, nothing picked first: what reads the recipe's lines (<see cref="SubAssemblies"/>).</summary>
    private readonly BehaviorSubject<SubAssembly?> _subAssemblies = new(null);

    /// <summary>The add-ins as last loaded, so Save only sends them when they were changed.</summary>
    private List<(int IngredientId, decimal Grams)> _savedAddIns = [];

    /// <summary>The batch's totals, worked out from the form when read (<see cref="BomTotals"/>).</summary>
    private readonly Derived<(decimal Weight, decimal Cost)> _bomTotals;

    /// <summary>The totals as last worked out from the material's own details, which show while another's are on their way.</summary>
    private (decimal Weight, decimal Cost) _heldBomTotals;

    /// <param name="host">The screen, whose Weight Loss % the totals are less, and whose header follows them.</param>
    /// <param name="ingredients">The ingredients as the screen last loaded them, which a line prices itself from.</param>
    public RawMaterialRecipe(IRawMaterialRecipeHost host, IReadOnlyList<RawMaterial> ingredients)
    {
        _host = host;
        _ingredients = ingredients;

        StartChangingSubAssemblyCommand = ScreenCommand.Of(() => IsChangingSubAssembly = true, () => IsShowingSubAssemblyBadge);
        CancelChangingSubAssemblyCommand = ScreenCommand.Of(() => IsChangingSubAssembly = false);
        OpenSubAssemblyCommand = ScreenCommand.Of(() => _subAssemblyOpenRequests.OnNext(SubAssembly!.Id), () => SubAssembly is not null);
        RemoveAddInCommand = ScreenCommand.Of(() => host.AsUndoStep(() => AddIns.Remove(SelectedAddIn!)), () => SelectedAddIn is not null);

        // The totals follow any change to either table; the screen says when the Weight Loss % or the details change.
        _bomTotals = new Derived<(decimal Weight, decimal Cost)>(BomTotals, () =>
        {
            RaisePropertyChanged(nameof(BomTotalWeight));
            RaisePropertyChanged(nameof(BomTotalCost));
        });
        RowChanges.OnAnyChange(Lines, _bomTotals.Invalidate);
        RowChanges.OnAnyChange(AddIns, _bomTotals.Invalidate);
    }

    /// <summary>
    /// The sub-assemblies a made ingredient can be made from, for the picker: every recipe on the Sub-Assemblies screen, with
    /// its role, by name. Not a packaging set, and never a product's own, which that screen doesn't list
    /// (SubAssemblyRules.EnsureLinkableByMadeIngredient).
    /// </summary>
    public ObservableCollection<SubAssembly> SubAssemblyOptions { get; } = [];

    private SubAssembly? _subAssembly;
    /// <summary>
    /// The sub-assembly it's made from, the recipe its add-ins go on top of: as the screen loads or puts it back, or as the
    /// user picks it from <see cref="SubAssemblyOptions"/> (<see cref="PickSubAssembly"/>). Its lines are read again when
    /// it's another.
    /// </summary>
    public SubAssembly? SubAssembly
    {
        get => _subAssembly;
        internal set
        {
            if (RaiseAndSetIfChanged(ref _subAssembly, value))
            {
                _subAssemblies.OnNext(value);
            }
        }
    }

    /// <summary>The user picks <paramref name="subAssembly"/> in the Made from picker: one picked after Edit settles it, and the badge comes back.</summary>
    public void PickSubAssembly(SubAssembly? subAssembly)
    {
        if (subAssembly == SubAssembly)
        {
            return;
        }

        SubAssembly = subAssembly;
        if (subAssembly is not null && IsChangingSubAssembly)
        {
            IsChangingSubAssembly = false;
        }
    }

    /// <summary>
    /// Edit was pressed: the Made from picker shows again in place of the badge, until another sub-assembly is picked or
    /// Cancel is pressed, so the recipe it's made from isn't changed by accident.
    /// </summary>
    public bool IsChangingSubAssembly
    {
        get;
        internal set => RaiseAndSetIfChanged(ref field, value);
    }

    /// <summary>
    /// The Made from picker shows: no sub-assembly is picked yet, or Edit was pressed. Otherwise the badge and Edit show, so
    /// once a sub-assembly is picked the picker only comes back on Edit.
    /// </summary>
    public bool IsPickingSubAssembly => SubAssembly is null || IsChangingSubAssembly;

    /// <summary>The picked sub-assembly shows as a badge that opens it, with Edit beside it, so a stray click or scroll can't change it.</summary>
    public bool IsShowingSubAssemblyBadge => !IsPickingSubAssembly;

    /// <summary>"Butter Icing Base, from Sub-Assemblies", as a linked section's badge reads on the Products screen.</summary>
    public string SubAssemblyBadgeText => SubAssembly is { } subAssembly ? $"{subAssembly.Name}, from Sub-Assemblies" : string.Empty;

    /// <summary>The badge's Edit: shows the Made from picker again (<see cref="IsChangingSubAssembly"/>).</summary>
    public ScreenCommand StartChangingSubAssemblyCommand { get; }

    /// <summary>The picker's Cancel: puts the badge back, keeping the sub-assembly as it is.</summary>
    public ScreenCommand CancelChangingSubAssemblyCommand { get; }

    /// <summary>The badge: asks for the sub-assembly it's made from to be opened, through <see cref="SubAssemblyOpenRequests"/>.</summary>
    public ScreenCommand OpenSubAssemblyCommand { get; }

    /// <summary>
    /// The sub-assembly as each pick or load shows it, nothing picked first: the screen reads the recipe's lines from it
    /// (LoadLatest), so a newer one cancels a read still under way.
    /// </summary>
    public IObservable<SubAssembly?> SubAssemblies => _subAssemblies;

    /// <summary>
    /// The sub-assemblies the badge asks to open, by id. ScreenLinks subscribes and opens each on the Sub-Assemblies screen,
    /// where its lines are edited, as it does for a linked badge on Products.
    /// </summary>
    public IObservable<int> SubAssemblyOpenRequests => _subAssemblyOpenRequests;

    /// <summary>
    /// The lines of <see cref="SubAssembly"/>, as it's saved on the Sub-Assemblies screen, for show: read-only here, since that
    /// screen is the one place the recipe is written. Read again whenever the pick changes or the screen loads.
    /// </summary>
    public ObservableCollection<IngredientLineRow> Lines { get; } = [];

    /// <summary>The made ingredient's own extra ingredients on top of its sub-assembly's recipe, typed straight into their table (IngredientAddInLine).</summary>
    public ObservableCollection<IngredientLineRow> AddIns { get; } = [];

    /// <summary>The line the Add-Ins table's add row edits: it prices itself as its ingredient is picked, before it's committed.</summary>
    public Func<IngredientLineRow> NewAddIn => () => new IngredientLineRow(id => _ingredients.FirstOrDefault(m => m.Id == id));

    public IngredientLineRow? SelectedAddIn
    {
        get;
        set => RaiseAndSetIfChanged(ref field, value);
    }

    public ScreenCommand RemoveAddInCommand { get; }

    /// <summary>
    /// The batch's yield as the form has it, the sub-assembly's grams and the add-ins' less the weight loss, so it follows the
    /// pick, the Add-Ins table and the Weight Loss % box as they change. 0 with no sub-assembly picked.
    /// </summary>
    public decimal BomTotalWeight => _bomTotals.Value.Weight;

    /// <summary>
    /// What the batch costs at its components' buffered costs: the sum of both tables' Cost columns, the recipe's and the
    /// add-ins'. BomTotalCost ÷ BomTotalWeight is the buffered cost per gram Save stores, since a made ingredient adds no
    /// buffer of its own, which is why the header shows the two beside Buffered Cost per g.
    /// </summary>
    public decimal BomTotalCost => _bomTotals.Value.Cost;

    /// <summary>The sub-assemblies as a load read them, packaging sets left out, merged into the picker's list.</summary>
    public void TakeOptions(IEnumerable<SubAssembly> subAssemblies) =>
        SubAssemblyOptions.MergeFrom([.. subAssemblies.Where(s => s.Role is not { IsPackaging: true })], s => s.Id);

    /// <summary>
    /// The recipe's lines: the picked sub-assembly's as it's saved, which the form reads, each priced at the screen's copy of
    /// its ingredient, as the add-ins are. None for no pick.
    /// </summary>
    public void ShowLines(IReadOnlyList<SubAssemblyLine> lines)
    {
        Lines.Clear();
        Lines.AddRange(lines.Select(l => new IngredientLineRow(_ingredients.FirstOrDefault(m => m.Id == l.IngredientId) ?? l.Ingredient!, l.Grams)));
    }

    /// <summary>An ingredient picked in the Add-Ins table is pointed at the screen's copy of it, for its name.</summary>
    public void ResolveAddIns() => IngredientLineRow.Resolve(AddIns, _ingredients.ToDictionary(m => m.Id));

    /// <summary>
    /// Something the totals are worked out from changed that isn't a row (the Weight Loss %, the details arriving, a load's
    /// fresh ingredients): they're worked out again as they're next read. The rows' own changes say so themselves.
    /// </summary>
    public void BomInputsChanged() => _bomTotals.Invalidate();

    /// <summary>
    /// The batch's totals, worked out from the FORM: the picked sub-assembly's lines, the Add-Ins rows and the Weight Loss %
    /// box. The yield comes from IngredientBomRules.CalculateCost, the same formula IngredientCostPropagator saves with, so the
    /// totals and the stored cost can't drift apart, and the cost is the sum of the rows' own Cost — both tables' Cost
    /// columns, at the ingredients' buffered costs. A row with no ingredient picked counts for neither. Held at what they last
    /// were until the details are the material's (AreDetailsReady), since a pick loads the form before the grids.
    /// </summary>
    private (decimal Weight, decimal Cost) BomTotals()
    {
        if (!_host.AreDetailsReady)
        {
            return _heldBomTotals;
        }

        List<IngredientLineRow> rows = [.. Lines, .. AddIns];
        var (outputGrams, _) = IngredientBomRules.CalculateCost(
            [.. rows
                .Where(l => l.Ingredient is not null)
                .Select(l => (Grams: l.Grams ?? 0m, ComponentCostPerGram: l.Ingredient!.BufferedCostPerBaseUnit))],
            _host.WeightLossPercent ?? 0m);

        _heldBomTotals = (outputGrams, rows.Sum(l => l.Cost));
        return _heldBomTotals;
    }

    /// <summary>The material's add-ins as saved (a BOM's add-in lines), in place of the table's, and what Save compares them against.</summary>
    public void ShowAddIns(IEnumerable<IngredientBomLine> bomLines)
    {
        AddIns.Clear();
        AddIns.AddRange(bomLines.Where(l => l.IsAddIn).Select(l => new IngredientLineRow(l.ComponentIngredient!, l.Grams)));
        _savedAddIns = CurrentAddIns();
    }

    /// <summary>The add-ins as an undo step keeps them: each ingredient by id, so a refresh's fresh copies aren't an edit.</summary>
    public IReadOnlyList<IngredientLineSnapshot> CaptureAddIns() =>
        [.. AddIns.Select(l => new IngredientLineSnapshot(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];

    /// <summary>The add-ins an undo step kept, in place of the table's: rebuilt, since IngredientLineRow's setters drop a typed percentage.</summary>
    public void RestoreAddIns(IEnumerable<IngredientLineSnapshot> lines)
    {
        AddIns.Clear();
        foreach (var line in lines)
        {
            AddIns.Add(IngredientLineRow.Restore(
                id => _ingredients.FirstOrDefault(m => m.Id == id),
                baseWeight: 0m,
                line.IngredientId,
                line.Grams,
                line.IsFormulaBase,
                line.AuthoredBakersPercent));
        }
    }

    /// <summary>The Add-Ins table's lines when they differ from those last loaded, or null to leave the saved add-ins as they are.</summary>
    public List<(int IngredientId, decimal Grams)>? ChangedAddIns()
    {
        var lines = CurrentAddIns();
        return lines.SequenceEqual(_savedAddIns) ? null : lines;
    }

    /// <summary>The Add-Ins table's lines; a half-filled or repeated one is refused, naming the ingredient being made.</summary>
    private List<(int IngredientId, decimal Grams)> CurrentAddIns() =>
        [.. IngredientLineRow.CompleteLines(AddIns, _host.EditingName.Trim()).Select(l => (l.Ingredient.Id, l.Grams))];

    public void Dispose()
    {
        _subAssemblyOpenRequests.Dispose();
        _subAssemblies.Dispose();
    }
}
