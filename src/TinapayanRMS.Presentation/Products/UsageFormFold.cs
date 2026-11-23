using System.Collections.ObjectModel;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>A section the one the form edits can go into whole, by its key (ProductUsageEdit.Key), or none.</summary>
/// <param name="Key">The section's key; null for none, cut into pieces itself.</param>
/// <param name="Name">What the picker shows: the section's name.</param>
public sealed record FoldTargetOption(long? Key, string Name)
{
    /// <summary>The section goes into none: it's cut into pieces itself, by its own piece size.</summary>
    public static FoldTargetOption None { get; } = new(null, "Nothing");
}

/// <summary>
/// The section form's say in how the recipe's sections go into each other (ProductUsageEdit.GoesInto): the section the one
/// it edits goes into whole, picked from the recipe's others (<see cref="Options"/>), and the ones that go into it, whose
/// whole batches its table shows under its lines and its figures count (<see cref="FoldedIn"/>). Part of the form
/// (ProductUsageForm.Fold), its pick kept in the form's undo steps (SectionFormSnapshot.GoesIntoIndex) and applied with the rest
/// of the form.
/// </summary>
/// <param name="changed">Says the form's figures read it again: called by the setters here, after each change.</param>
public sealed class UsageFormFold(Action changed) : ObservableState
{
    /// <summary>What the section can go into: none first, then the recipe's other sections that make no loop with it.</summary>
    public ObservableCollection<FoldTargetOption> Options { get; } = [FoldTargetOption.None];

    private FoldTargetOption _target = FoldTargetOption.None;
    /// <summary>The section picked for it to go into whole, or <see cref="FoldTargetOption.None"/>.</summary>
    public FoldTargetOption Target
    {
        get => _target;
        private set
        {
            RaiseAndSetIfChanged(ref _target, value);
            RaisePropertyChanged(nameof(GoesWhole));
            changed();
        }
    }

    /// <summary>The user picks what the section goes into whole; none, for pieces of its own.</summary>
    public void Pick(FoldTargetOption? target) => Target = target ?? FoldTargetOption.None;

    /// <summary>The key of the section picked to go into; null for none.</summary>
    public long? TargetKey => Target.Key;

    /// <summary>The section goes whole into another: it has no piece size, so the form shows what it goes into in its place.</summary>
    public bool GoesWhole => Target.Key is not null;

    private IReadOnlyList<FoldedSection> _foldedIn = [];
    /// <summary>The recipe's sections that go into this one whole, each with its whole batch, as the recipe has them now.</summary>
    public IReadOnlyList<FoldedSection> FoldedIn
    {
        get => _foldedIn;
        private set
        {
            RaiseAndSetIfChanged(ref _foldedIn, value);
            changed();
        }
    }

    /// <summary>What goes into the section's batch whole from the others: in its total weight, and its percentages.</summary>
    public decimal FoldedWeight => FoldedIn.Sum(f => f.Batch.Weight);

    /// <summary>
    /// What goes into the section whole as its lines table shows it, under its lines: each section's whole batch, its
    /// percentage as the lines' are read (of <paramref name="formulaBase"/>, the ticked lines' grams, or with none ticked of
    /// the whole batch, <paramref name="linesWeight"/> of lines and add-ins and these), and its cost at <paramref name="basis"/>.
    /// </summary>
    public IReadOnlyList<FoldedRow> Rows(decimal formulaBase, decimal linesWeight, CostBasis basis)
    {
        var batchWeight = linesWeight + FoldedWeight;
        return
        [
            .. FoldedIn.Select(f => new FoldedRow(
                f.SectionName,
                f.Batch.Weight,
                formulaBase > 0 ? SubAssemblyRules.CalculateBakersPercent(f.Batch.Weight, formulaBase) : SubAssemblyRules.CalculateAbsolutePercent(f.Batch.Weight, batchWeight),
                f.Batch.CostAt(basis))),
        ];
    }

    /// <summary>
    /// Shows the form's say for a section: what it could go into (<paramref name="targets"/>), what it goes into now
    /// (<paramref name="goesInto"/>, by key; none when that's no longer offered), and what goes into it.
    /// </summary>
    internal void Show(IReadOnlyList<ProductUsageEdit> targets, long? goesInto, IReadOnlyList<FoldedSection> foldedIn)
    {
        Options.MergeFrom([FoldTargetOption.None, .. targets.Select(t => new FoldTargetOption(t.Key, t.SectionName))], o => o.Key ?? 0L);
        Target = Options.FirstOrDefault(o => o.Key == goesInto) ?? FoldTargetOption.None;
        FoldedIn = foldedIn;
    }

    /// <summary>What goes into the section, as the recipe has it now; true when that's other than it showed.</summary>
    internal bool ShowFoldedIn(IReadOnlyList<FoldedSection> foldedIn)
    {
        if (foldedIn.SequenceEqual(FoldedIn))
        {
            return false;
        }

        FoldedIn = foldedIn;
        return true;
    }
}

/// <summary>
/// A section that goes into the one the form edits whole, as a row under its lines: what it's called, its whole batch's
/// grams and cost, and its percentage as the lines' are read.
/// </summary>
public sealed record FoldedRow(string Name, decimal Grams, decimal? Percent, decimal Cost)
{
    /// <summary>What a gram of it costs: its cost spread over its weight, as a line's is.</summary>
    public decimal CostPerGram => Grams > 0 ? Cost / Grams : 0m;
}
