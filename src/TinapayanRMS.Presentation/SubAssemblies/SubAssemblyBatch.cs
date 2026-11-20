using System.Collections.ObjectModel;
using DynamicData;
using DynamicData.Binding;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.SubAssemblies;

/// <summary>
/// A recipe's lines on the Sub-Assemblies screen (SubAssemblyDraft.Batch): its ingredients in grams, typed
/// straight into the table, the formula's base and the batch's totals that follow them, and Scale. It shows the lines as
/// saved, keeps and puts them back for an undo step, and prices them; Save reads them as typed. The screen keeps the undo
/// history, the Remove Line its two tables share, and says when the ingredients were read again.
/// </summary>
public sealed class SubAssemblyBatch : ObservableState
{
    /// <summary>With nothing ticked as the base, the percentages are absolute: each line's share of the batch.</summary>
    internal const string NoBaseCaption = "Absolute %: each line's share of the batch. Tick the base — usually the flour — for baker's %.";

    private readonly IUndoRecorder _recorder;
    private readonly Action<string?> _showError;
    private readonly IReadOnlyList<RawMaterial> _ingredients;

    /// <summary>The batch's figures, worked out from the lines when read (<see cref="BatchFigures"/>).</summary>
    private readonly Derived<BatchFigures> _figures;

    /// <param name="recorder">The undo history Scale is recorded in as one step.</param>
    /// <param name="showError">Where Scale says why it can't scale, and clears what it said.</param>
    /// <param name="ingredients">The ingredients as the screen last loaded them, which a line prices itself from.</param>
    /// <param name="updates">
    /// The draft's derived updates, so an undo putting the lines back points them at their ingredients and applies the
    /// formula base once, from the whole draft.
    /// </param>
    /// <param name="isShowing">Whether the recipe's table shows: not for a packaging set, which Scale has nothing to do with.</param>
    public SubAssemblyBatch(IUndoRecorder recorder, Action<string?> showError, IReadOnlyList<RawMaterial> ingredients, DerivedUpdates updates, Func<bool> isShowing)
    {
        _recorder = recorder;
        _showError = showError;
        _ingredients = ingredients;

        // An ingredient picked in the table is pointed at the screen's copy of it, which prices the line.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ => updates.Run(ResolveLines));

        // The figures follow any change to the lines, and are worked out as they're next read.
        _figures = new Derived<BatchFigures>(Figures, RaiseFiguresChanged);
        RowChanges.OnAnyChange(Lines, _figures.Invalidate);

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

    private decimal _formulaBase;
    /// <summary>
    /// The formula base as last applied to the lines (<see cref="RecomputeFormulaBase"/>), or as an undo step kept it: for a
    /// line started in the add row, for the step to keep, and for the caption.
    /// </summary>
    internal decimal FormulaBase
    {
        get => _formulaBase;
        private set
        {
            if (value != _formulaBase)
            {
                _formulaBase = value;
                _figures.Invalidate();
            }
        }
    }

    /// <summary>
    /// The formula in baker's terms, e.g. "Base 1,000 g · whole formula 167.7%" — the figure a batch is scaled by — or,
    /// with nothing ticked, what the absolute percentages are read against.
    /// </summary>
    public string FormulaBaseCaption => _figures.Value.FormulaBaseCaption;

    /// <summary>The lines table's percentage header: "Baker's %" while a base is ticked, "Absolute %" while none is.</summary>
    public string PercentHeader => IngredientLineRow.PercentHeader(FormulaBase);

    /// <summary>The batch: the sum of the grams of its lines with an ingredient picked.</summary>
    public decimal BatchWeight => _figures.Value.Weight;

    /// <summary>What the batch costs, at its ingredients' buffered costs.</summary>
    public decimal BatchCost => _figures.Value.Cost;

    public decimal CostPerGram => _figures.Value.CostPerGram;

    /// <summary>The batch's figures, worked out together from the lines as they stand.</summary>
    private sealed record BatchFigures(decimal Weight, decimal Cost, decimal CostPerGram, string FormulaBaseCaption);

    private BatchFigures Figures()
    {
        var weight = Lines.Where(l => l.Ingredient is not null).Sum(l => l.Grams ?? 0m);
        var cost = Lines.Sum(l => l.Cost);
        var formulaWeight = IngredientLineRow.BatchWeightOf(Lines);
        var caption = FormulaBase > 0
            ? $"Base {FormulaBase:#,0.##} g · whole formula {formulaWeight / FormulaBase * 100m:#,0.#}%"
            : NoBaseCaption;
        return new BatchFigures(weight, cost, weight > 0 ? cost / weight : 0m, caption);
    }

    private void RaiseFiguresChanged()
    {
        RaisePropertyChanged(nameof(BatchWeight));
        RaisePropertyChanged(nameof(BatchCost));
        RaisePropertyChanged(nameof(CostPerGram));
        RaisePropertyChanged(nameof(FormulaBaseCaption));
        RaisePropertyChanged(nameof(PercentHeader));
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
    public IReadOnlyList<IngredientLineSnapshot> Capture() =>
        [.. Lines.Select(l => new IngredientLineSnapshot(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];

    /// <summary>
    /// The lines an undo step kept, in place of those showing, at the formula base they were read against. They're rebuilt
    /// rather than written into: IngredientLineRow's setters drop a typed percentage.
    /// </summary>
    public void Restore(IEnumerable<IngredientLineSnapshot> lines, decimal formulaBase)
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

    /// <summary>
    /// The formula's base, given to every row so each reads its own percentage (and a line typed as a percentage its
    /// grams): a write into the rows, so it's pushed, where the figures are only worked out as they're read.
    /// </summary>
    public void RecomputeFormulaBase() =>
        FormulaBase = IngredientLineRow.ApplyFormulaBase([.. Lines], IngredientLineRow.BatchWeightOf(Lines));

    private void Scale()
    {
        _showError(null);
        var from = ScaleOption.Basis == BatchScaleBasis.Base ? FormulaBase : BatchWeight;
        if (from <= 0m || ScaleTo is not { } to || to <= 0m)
        {
            _showError("Nothing is ticked as the base, so there's no base to scale. Tick the base lines, or scale the whole batch.");
            return;
        }

        _recorder.AsUndoStep(() =>
        {
            // Lines typed in grams are scaled; a line typed as a percentage follows the base, which is among them.
            foreach (var line in Lines.Where(l => l.Ingredient is not null && l.AuthoredBakersPercent is null && l.Grams is not null))
            {
                line.Grams = SubAssemblyRules.ScaledGrams(line.Grams!.Value, from, to);
            }

            RecomputeFormulaBase();
        });
    }
}
