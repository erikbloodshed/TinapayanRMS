using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.ViewModels;

// Where an ingredient comes from: a made one's recipe and add-ins, and a linked one's share.
public sealed partial class RawMaterialsViewModel
{
    /// <summary>
    /// A made ingredient's recipe: its sub-assembly's lines, the tiles they and the weight loss make, and the badge.
    /// </summary>
    [MemberNotNull(nameof(_isPickingSubAssembly), nameof(_isShowingSubAssemblyBadge), nameof(_subAssemblyBadgeText))]
    private void WireRecipe()
    {
        // The made-ingredient tiles follow the Weight Loss % box, which moves the yield the cost is spread over.
        this.WhenAnyValue(x => x.EditingWeightLossPercent)
            .Subscribe(_ => _updates.Run(RecomputeBomTotals));

        // The Recipe card shows the picked sub-assembly's lines as it's saved: read again on every pick, an undo's
        // included, and on every load, which re-points the pick at the refreshed copy, so a change made on the
        // Sub-Assemblies screen shows here. The tiles follow them.
        LoadLatest(this.WhenAnyValue(x => x.EditingSubAssembly), LoadRecipeLinesAsync);

        // Once a sub-assembly is picked, it shows as a badge with Edit beside it, and the picker only comes back on Edit.
        _isPickingSubAssembly = this.WhenAnyValue(x => x.EditingSubAssembly, x => x.IsChangingSubAssembly,
                (subAssembly, isChanging) => subAssembly is null || isChanging)
            .ToProperty(this, x => x.IsPickingSubAssembly, true);
        _isShowingSubAssemblyBadge = this.WhenAnyValue(x => x.IsPickingSubAssembly)
            .Select(isPicking => !isPicking)
            .ToProperty(this, x => x.IsShowingSubAssemblyBadge);
        _subAssemblyBadgeText = this.WhenAnyValue(x => x.EditingSubAssembly)
            .Select(subAssembly => subAssembly is null ? string.Empty : $"{subAssembly.Name}, from Sub-Assemblies")
            .ToProperty(this, x => x.SubAssemblyBadgeText, string.Empty);
    }

    /// <summary>A made ingredient's add-ins, as they're typed.</summary>
    private void WireAddIns()
    {
        // An ingredient picked in the Add-Ins table is pointed at the screen's copy of it, for its name.
        AddInEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ => _updates.Run(ResolveAddIns));

        // The header's Total Weight and Total Cost follow the add-ins as they're typed into: Cost covers both an
        // ingredient picked and grams typed, as the sub-assembly form's batch totals do.
        AddInEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.Cost)
            .Subscribe(_ => _updates.Run(RecomputeBomTotals));
    }

    /// <summary>A made ingredient's sub-assembly changed or opened, and an add-in taken out.</summary>
    [MemberNotNull(
        nameof(StartChangingSubAssemblyCommand), nameof(CancelChangingSubAssemblyCommand),
        nameof(OpenSubAssemblyCommand), nameof(RemoveAddInCommand))]
    private void WireRecipeCommands()
    {
        StartChangingSubAssemblyCommand = ReactiveCommand.Create(
            () => { IsChangingSubAssembly = true; },
            this.WhenAnyValue(x => x.IsShowingSubAssemblyBadge));
        CancelChangingSubAssemblyCommand = ReactiveCommand.Create(() => { IsChangingSubAssembly = false; });

        OpenSubAssemblyCommand = ReactiveCommand.Create(
            () => _subAssemblyOpenRequests.OnNext(EditingSubAssembly!.Id),
            this.WhenAnyValue(x => x.EditingSubAssembly).Select(subAssembly => subAssembly is not null));

        RemoveAddInCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => AddInEdits.Remove(SelectedAddInEdit!)),
            this.WhenAnyValue(x => x.SelectedAddInEdit).Select(line => line is not null));
    }

    /// <summary>
    /// "Egg costs 0.0900/g, buffered 0.0990/g. At 70% of its weight for 30% of its cost, this costs 0.0386/g, buffered 0.0424/g."
    /// Worked out with the same rule the cascade saves with, from the linked-to ingredient's stored costs: the
    /// same share of its true cost and of its (fixed) buffered cost.
    /// </summary>
    private static string LinkedCostPreview(RawMaterial? linkedTo, decimal? weightShare, decimal? costShare)
    {
        if (linkedTo is null)
        {
            return "Pick the ingredient this one is a part of.";
        }

        var linkedToText = $"{linkedTo.Name} costs {linkedTo.CostPerBaseUnit:N4}/g, buffered {linkedTo.BufferedCostPerBaseUnit:N4}/g.";
        if (weightShare is not (> 0 and <= 100) || costShare is not (> 0 and <= 100))
        {
            return $"{linkedToText} Enter this part's share of its weight and of its cost.";
        }

        var cost = RawMaterialRules.LinkedCost(linkedTo.CostPerBaseUnit, weightShare.Value, costShare.Value);
        var buffered = RawMaterialRules.LinkedCost(linkedTo.BufferedCostPerBaseUnit, weightShare.Value, costShare.Value);
        return $"{linkedToText} At {weightShare:0.##}% of its weight for {costShare:0.##}% of its cost, this costs {cost:N4}/g, buffered {buffered:N4}/g.";
    }

    /// <summary>
    /// "Also linked to Egg: Egg Yolk (30% of the weight, 70% of the cost). With this one, 100% of its weight and 100% of its cost."
    /// The other parts come from the list's copies, so they're as of the last load; Save checks the saved ones.
    /// </summary>
    private string LinkedPartsSummary(RawMaterial? linkedTo, decimal? weightShare, decimal? costShare)
    {
        if (linkedTo is null)
        {
            return string.Empty;
        }

        var others = RawMaterials
            .Where(m => m.LinkedToId == linkedTo.Id && m.Id != _editingId)
            .ToList();
        var totalWeight = others.Sum(m => m.WeightSharePercent ?? 0m) + (weightShare ?? 0m);
        var totalCost = others.Sum(m => m.CostSharePercent ?? 0m) + (costShare ?? 0m);
        var totals = $"{totalWeight:0.##}% of its weight and {totalCost:0.##}% of its cost";

        if (others.Count == 0)
        {
            return $"Nothing else is linked to {linkedTo.Name}. This part takes {totals}.";
        }

        var parts = string.Join(", ", others.Select(m => $"{m.Name} ({m.WeightSharePercent:0.##}% of the weight, {m.CostSharePercent:0.##}% of the cost)"));
        return $"Also linked to {linkedTo.Name}: {parts}. With this one, {totals} (at most 100% each).";
    }

    /// <summary>An ingredient picked in the Add-Ins table is pointed at the screen's copy of it, for its name.</summary>
    private void ResolveAddIns() => IngredientLineRow.Resolve(AddInEdits, Ingredients.ToDictionary(m => m.Id));

    /// <summary>
    /// The made-ingredient tiles, worked out from the FORM: the picked sub-assembly's lines, the Add-Ins rows and the
    /// Weight Loss % box. The yield comes from IngredientBomRules.CalculateCost, the same formula IngredientCostPropagator
    /// saves with, so the tiles and the stored cost can't drift apart, and the cost is the sum of the rows' own Cost — both
    /// tables' Cost columns, at the ingredients' buffered costs. A row with no ingredient picked counts for neither. Left
    /// alone until the details are this material's (AreDetailsReady), since a pick loads the form before the grids.
    /// </summary>
    private void RecomputeBomTotals()
    {
        if (!AreDetailsReady)
        {
            return;
        }

        List<IngredientLineRow> rows = [.. RecipeLines, .. AddInEdits];
        var (outputGrams, _) = IngredientBomRules.CalculateCost(
            [.. rows
                .Where(l => l.Ingredient is not null)
                .Select(l => (Grams: l.Grams ?? 0m, ComponentCostPerGram: l.Ingredient!.BufferedCostPerBaseUnit))],
            EditingWeightLossPercent ?? 0m);

        BomTotalWeight = outputGrams;
        BomTotalCost = rows.Sum(l => l.Cost);
        UpdateHeaderCostTexts();
    }

    /// <summary>
    /// The Recipe card's lines: <paramref name="subAssembly"/>'s as it's saved, each priced at the screen's copy of its
    /// ingredient, as the add-ins are. None for no pick.
    /// </summary>
    private async Task LoadRecipeLinesAsync(SubAssembly? subAssembly, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubAssemblyLine> lines = subAssembly is null
            ? []
            : await _subAssemblyLineRepository.GetForSubAssembliesAsync([subAssembly.Id], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        RecipeLines.Clear();
        RecipeLines.AddRange(lines.Select(l => new IngredientLineRow(Ingredients.FirstOrDefault(m => m.Id == l.IngredientId) ?? l.Ingredient!, l.Grams)));
        RecomputeBomTotals();
    }

    /// <summary>The Add-Ins table's lines when they differ from those last loaded, or null to leave the saved add-ins as they are.</summary>
    private List<(int IngredientId, decimal Grams)>? ChangedAddIns()
    {
        var lines = CurrentAddIns();
        return lines.SequenceEqual(_savedAddIns) ? null : lines;
    }

    /// <summary>The Add-Ins table's lines; a half-filled or repeated one is refused, naming the ingredient being made.</summary>
    private List<(int IngredientId, decimal Grams)> CurrentAddIns() =>
        [.. IngredientLineRow.CompleteLines(AddInEdits, EditingName.Trim()).Select(l => (l.Ingredient.Id, l.Grams))];
}
