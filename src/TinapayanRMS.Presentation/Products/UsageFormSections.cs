using System.Collections.ObjectModel;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// The section form's say in how the recipe's sections are lines of each other (ProductUsageEdit.SectionLines): what a line
/// of the section it edits can be, the recipe's other sections and the ingredients (<see cref="Picks"/>), and what the
/// others use of it on their lines (<see cref="UsedElsewhere"/>), which its pieces come of what's left of. Part of the form
/// (ProductUsageForm.Sections).
/// </summary>
/// <param name="ingredients">The ingredients as the screen last loaded them.</param>
/// <param name="changed">Says the form's figures read it again: called by the setters here, after each change.</param>
public sealed class UsageFormSections(IReadOnlyList<RawMaterial> ingredients, Action changed) : ObservableState
{
    private IReadOnlyList<LineSection> _sections = [];

    /// <summary>
    /// What a line of the section can be, as its picker lists it: the recipe's other sections that make no loop with it
    /// ("Sponge (section)"), then every ingredient.
    /// </summary>
    public ObservableCollection<LinePick> Picks { get; } = [];

    /// <summary>The sections a line can be, as the recipe prices them now, for the picker (ProductRecipeDraft.LineSectionsFor).</summary>
    internal void ShowPicks(IReadOnlyList<LineSection> sections)
    {
        _sections = sections;
        RefreshPicks();
    }

    /// <summary>The picker's list again, with the ingredients as the screen last loaded them.</summary>
    internal void RefreshPicks() =>
        Picks.MergeFrom([.. _sections.Select(LinePick.Of), .. ingredients.Select(LinePick.Of)], p => (p.Section?.Key, p.Ingredient?.Id));

    /// <summary>What the recipe's other sections use of the section on their lines, in grams; 0 for none.</summary>
    public decimal UsedElsewhere { get; private set; }

    /// <summary>The recipe's sections that use it on their lines, by name.</summary>
    public IReadOnlyList<string> Users { get; private set; } = [];

    /// <summary>What the others use of the section, as the recipe has it now; true when that's other than it showed.</summary>
    internal bool ShowUsedElsewhere((decimal Grams, IReadOnlyList<string> Users) used)
    {
        if (used.Grams == UsedElsewhere && used.Users.SequenceEqual(Users))
        {
            return false;
        }

        UsedElsewhere = used.Grams;
        Users = used.Users;
        RaisePropertyChanged(nameof(UsedElsewhere));
        RaisePropertyChanged(nameof(Users));
        changed();
        return true;
    }
}
