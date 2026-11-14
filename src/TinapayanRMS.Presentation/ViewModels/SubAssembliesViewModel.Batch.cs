using System.Diagnostics.CodeAnalysis;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Presentation.ViewModels;

// The batch: its lines in grams, their formula base and totals, Scale, and a packaging set's lines.
public sealed partial class SubAssembliesViewModel
{
    /// <summary>The lines priced as they're picked and typed, and the totals, the formula base and a set's cost following them.</summary>
    private void WireBatch()
    {
        // An ingredient picked in the table is pointed at the screen's copy of it, which prices the line.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ => _updates.Run(ResolveLines));

        // Cost follows both an ingredient picked and grams typed.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.Cost)
            .Subscribe(_ => _updates.Run(RecomputeTotals));

        // A packaging set's lines price themselves as their material is picked, and the set's cost follows.
        PackagingLines.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .Subscribe(_ => _updates.Run(ResolvePackagingLines));
        PackagingLines.ToObservableChangeSet()
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ => _updates.Run(RecomputeSetCost));

        // The formula's base: what's ticked, and how much of it there is.
        Lines.ToObservableChangeSet()
            .AutoRefresh(l => l.IsFormulaBase)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ => _updates.Run(RecomputeFormulaBase));
    }

    /// <summary>Scale, and Remove Line on whichever table is showing.</summary>
    [MemberNotNull(nameof(ScaleCommand), nameof(RemoveLineCommand))]
    private void WireBatchCommands()
    {
        ScaleOption = ScaleOptions[0];
        ScaleCommand = ReactiveCommand.Create(
            ScaleBatch,
            this.WhenAnyValue(x => x.ScaleTo, x => x.BatchWeight, x => x.IsPackaging, (to, weight, isPackaging) => to is > 0m && weight > 0m && !isPackaging));

        // The line selected in whichever table is showing.
        RemoveLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                if (IsPackaging)
                {
                    PackagingLines.Remove(SelectedPackagingLine!);
                }
                else
                {
                    Lines.Remove(SelectedLine!);
                }
            }),
            this.WhenAnyValue(
                x => x.SelectedLine,
                x => x.SelectedPackagingLine,
                x => x.IsPackaging,
                (line, packagingLine, isPackaging) => isPackaging ? packagingLine is not null : line is not null));
    }

    private void ScaleBatch()
    {
        ErrorMessage = null;
        var from = ScaleOption.Basis == BatchScaleBasis.Base ? _formulaBase : BatchWeight;
        if (from <= 0m || ScaleTo is not { } to || to <= 0m)
        {
            ErrorMessage = "Nothing is ticked as the base, so there's no base to scale. Tick the base lines, or scale the whole batch.";
            return;
        }

        AsUndoStep(() =>
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

    /// <summary>An ingredient picked in the table is pointed at the screen's copy of it, which prices the line.</summary>
    private void ResolveLines() => IngredientLineRow.Resolve(Lines, Ingredients.ToDictionary(m => m.Id));

    /// <summary>A packaging set's lines price themselves as their material is picked.</summary>
    private void ResolvePackagingLines() => PackagingLineRow.Resolve(PackagingLines, PackagingMaterials.ToDictionary(m => m.Id));

    /// <summary>A packaging set's cost follows its lines.</summary>
    private void RecomputeSetCost() => SetCost = PackagingLines.Sum(l => l.CostPerUnit);

    /// <summary>The formula's base, given to every row so each reads its own percentage, and said in the caption.</summary>
    private void RecomputeFormulaBase()
    {
        var totalWeight = IngredientLineRow.BatchWeightOf(Lines);
        var baseWeight = IngredientLineRow.ApplyFormulaBase([.. Lines], totalWeight);
        _formulaBase = baseWeight;
        PercentHeader = IngredientLineRow.PercentHeader(baseWeight);
        FormulaBaseCaption = baseWeight > 0
            ? $"Base {baseWeight:#,0.##} g · whole formula {totalWeight / baseWeight * 100m:#,0.#}%"
            : NoBaseCaption;
    }

    private void RecomputeTotals()
    {
        BatchWeight = Lines.Where(l => l.Ingredient is not null).Sum(l => l.Grams ?? 0m);
        BatchCost = Lines.Sum(l => l.Cost);
        CostPerGram = BatchWeight > 0 ? BatchCost / BatchWeight : 0m;
    }
}
