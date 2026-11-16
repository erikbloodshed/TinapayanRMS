using System.Collections.ObjectModel;
using DynamicData;
using DynamicData.Binding;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A recipe's lines on the Sub-Assemblies screen (through SubAssembliesViewModel.Batch): its ingredients in grams, typed
/// straight into the table, the formula's base and the batch's totals that follow them, and Scale. It shows the lines as
/// saved, keeps and puts them back for an undo step, and prices them; Save reads them as typed. The screen keeps the undo
/// history, the Remove Line its two tables share, and says when the ingredients were read again.
/// </summary>
public sealed class SubAssemblyBatch : ObservableState
{
    /// <summary>With nothing ticked as the base, the percentages are absolute: each line's share of the batch.</summary>
    internal const string NoBaseCaption = "Absolute %: each line's share of the batch. Tick the base — usually the flour — for baker's %.";

    private readonly IFormPartHost _host;
    private readonly IReadOnlyList<RawMaterial> _ingredients;

    /// <param name="host">The screen, which shows Scale's message and records it as an undo step.</param>
    /// <param name="ingredients">The ingredients as the screen last loaded them, which a line prices itself from.</param>
    /// <param name="updates">The screen's derived updates, so an undo putting the lines back works the totals out once.</param>
    /// <param name="isShowing">Whether the recipe's table shows: not for a packaging set, which Scale has nothing to do with.</param>
    public SubAssemblyBatch(IFormPartHost host, IReadOnlyList<RawMaterial> ingredients, DerivedUpdates updates, Func<bool> isShowing)
    {
        _host = host;
        _ingredients = ingredients;

        // An ingredient picked in the table is pointed at the screen's copy of it, which prices the line.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ => updates.Run(ResolveLines));

        // Cost follows both an ingredient picked and grams typed.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.Cost)
            .Subscribe(_ => updates.Run(RecomputeTotals));

        // The formula's base: what's ticked, and how much of it there is.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IsFormulaBase)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ => updates.Run(RecomputeFormulaBase));

        _scaleOption = ScaleOptions[0];
        ScaleCommand = ScreenCommand.Of(Scale, () => ScaleTo is > 0m && BatchWeight > 0m && isShowing());
    }

    /// <summary>The recipe's lines, typed straight into the table, in grams.</summary>
    public ObservableCollection<IngredientLineRow> Lines { get; } = [];

    /// <summary>The line the table's add row edits: it prices itself as its ingredient is picked, before it's committed.</summary>
    public Func<IngredientLineRow> NewLine => () => new IngredientLineRow(id => _ingredients.FirstOrDefault(m => m.Id == id), FormulaBase);

    private IngredientLineRow? _selectedLine;
    public IngredientLineRow? SelectedLine
    {
        get => _selectedLine;
        set => RaiseAndSetIfChanged(ref _selectedLine, value);
    }

    /// <summary>The formula base as last worked out, for a line started in the add row, and for an undo step to keep.</summary>
    internal decimal FormulaBase { get; private set; }

    private string _formulaBaseCaption = NoBaseCaption;
    /// <summary>
    /// The formula in baker's terms, e.g. "Base 1,000 g · whole formula 167.7%" — the figure a batch is scaled by — or,
    /// with nothing ticked, what the absolute percentages are read against.
    /// </summary>
    public string FormulaBaseCaption
    {
        get => _formulaBaseCaption;
        private set => RaiseAndSetIfChanged(ref _formulaBaseCaption, value);
    }

    private string _percentHeader = IngredientLineRow.PercentHeader(0m);
    /// <summary>The lines table's percentage header: "Baker's %" while a base is ticked, "Absolute %" while none is.</summary>
    public string PercentHeader
    {
        get => _percentHeader;
        private set => RaiseAndSetIfChanged(ref _percentHeader, value);
    }

    private decimal _batchWeight;
    /// <summary>The batch: the sum of its lines' grams.</summary>
    public decimal BatchWeight
    {
        get => _batchWeight;
        private set => RaiseAndSetIfChanged(ref _batchWeight, value);
    }

    private decimal _batchCost;
    /// <summary>What the batch costs, at its ingredients' buffered costs.</summary>
    public decimal BatchCost
    {
        get => _batchCost;
        private set => RaiseAndSetIfChanged(ref _batchCost, value);
    }

    private decimal _costPerGram;
    public decimal CostPerGram
    {
        get => _costPerGram;
        private set => RaiseAndSetIfChanged(ref _costPerGram, value);
    }

    private decimal? _scaleTo;
    /// <summary>What Scale makes the batch, or its base, weigh, in grams. Not part of the sub-assembly.</summary>
    public decimal? ScaleTo
    {
        get => _scaleTo;
        set => RaiseAndSetIfChanged(ref _scaleTo, value);
    }

    /// <summary>What Scale can make weigh <see cref="ScaleTo"/>: the whole batch, or the lines ticked as its base.</summary>
    public IReadOnlyList<BatchScaleOption> ScaleOptions { get; } =
    [
        new(BatchScaleBasis.Batch, "the whole batch"),
        new(BatchScaleBasis.Base, "the base (ticked lines)"),
    ];

    private BatchScaleOption _scaleOption;
    public BatchScaleOption ScaleOption
    {
        get => _scaleOption;
        set => RaiseAndSetIfChanged(ref _scaleOption, value);
    }

    /// <summary>
    /// Every line's grams by the factor that makes the batch, or its base, weigh <see cref="ScaleTo"/>, as one undo step
    /// ("make it 5 kg"). A line typed as a baker's percentage keeps it and follows the base.
    /// </summary>
    public ScreenCommand ScaleCommand { get; }

    /// <summary>No lines, and none picked: a new sub-assembly's form.</summary>
    public void Clear()
    {
        SelectedLine = null;
        Lines.Clear();
    }

    /// <summary>The lines as saved, each finding its ingredient as the screen last loaded it.</summary>
    public void Show(IEnumerable<SubAssemblyLine> saved)
    {
        var ingredientsById = _ingredients.ToDictionary(m => m.Id);
        SelectedLine = null;
        Lines.Clear();
        Lines.AddRange(saved.Select(l => new IngredientLineRow(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)));
    }

    /// <summary>The lines as an undo step keeps them: each ingredient by id, so a refresh's fresh copies aren't an edit.</summary>
    public IReadOnlyList<IngredientLineState> Capture() =>
        [.. Lines.Select(l => new IngredientLineState(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];

    /// <summary>
    /// The lines an undo step kept, in place of those showing, at the formula base they were read against. They're rebuilt
    /// rather than written into: IngredientLineRow's setters drop a typed percentage.
    /// </summary>
    public void Restore(IEnumerable<IngredientLineState> lines, decimal formulaBase)
    {
        FormulaBase = formulaBase;
        Lines.Clear();
        foreach (var line in lines)
        {
            Lines.Add(IngredientLineRow.Restore(
                id => _ingredients.FirstOrDefault(m => m.Id == id),
                formulaBase,
                line.IngredientId,
                line.Grams,
                line.IsFormulaBase,
                line.AuthoredBakersPercent));
        }
    }

    /// <summary>An ingredient picked in the table is pointed at the screen's copy of it, which prices the line.</summary>
    public void ResolveLines() => IngredientLineRow.Resolve(Lines, _ingredients.ToDictionary(m => m.Id));

    /// <summary>The formula's base, given to every row so each reads its own percentage, and said in the caption.</summary>
    public void RecomputeFormulaBase()
    {
        var totalWeight = IngredientLineRow.BatchWeightOf(Lines);
        var baseWeight = IngredientLineRow.ApplyFormulaBase([.. Lines], totalWeight);
        FormulaBase = baseWeight;
        PercentHeader = IngredientLineRow.PercentHeader(baseWeight);
        FormulaBaseCaption = baseWeight > 0
            ? $"Base {baseWeight:#,0.##} g · whole formula {totalWeight / baseWeight * 100m:#,0.#}%"
            : NoBaseCaption;
    }

    /// <summary>The batch's weight and cost, and its cost a gram, from the lines as they are.</summary>
    public void RecomputeTotals()
    {
        BatchWeight = Lines.Where(l => l.Ingredient is not null).Sum(l => l.Grams ?? 0m);
        BatchCost = Lines.Sum(l => l.Cost);
        CostPerGram = BatchWeight > 0 ? BatchCost / BatchWeight : 0m;
    }

    private void Scale()
    {
        _host.ShowError(null);
        var from = ScaleOption.Basis == BatchScaleBasis.Base ? FormulaBase : BatchWeight;
        if (from <= 0m || ScaleTo is not { } to || to <= 0m)
        {
            _host.ShowError("Nothing is ticked as the base, so there's no base to scale. Tick the base lines, or scale the whole batch.");
            return;
        }

        _host.AsUndoStep(() =>
        {
            // Lines typed in grams are scaled; a line typed as a percentage follows the base, which is among them.
            foreach (var line in Lines.Where(l => l.Ingredient is not null && l.AuthoredBakersPercent is null && l.Grams is not null))
            {
                line.Grams = SubAssemblyRules.ScaledGrams(line.Grams!.Value, from, to);
            }

            RecomputeFormulaBase();
            RecomputeTotals();
        });
    }
}
