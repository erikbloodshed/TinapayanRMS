using ReactiveUI;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A tab of the Products screen's Recipe Structure (ProductsViewModel.RecipeTabs): one per sub-assembly, in
/// recipe order, then Add Sub-Assembly, then Packaging, which is always there.
/// </summary>
public abstract class RecipeTab : ReactiveObject
{
    public abstract string Header { get; }

    /// <summary>The Segoe Fluent Icons glyph before the header.</summary>
    public abstract string Glyph { get; }
}

/// <summary>
/// A sub-assembly's tab, which shows the form that edits it (ProductsViewModel.EditingUsage). When its section
/// is replaced (applied, copied, or refreshed with new costs) the tab is kept and shows the replacement, so it
/// stays the tab showing.
/// </summary>
public sealed class SubAssemblyRecipeTab(ProductUsageEdit usage) : RecipeTab
{
    private ProductUsageEdit _usage = usage;
    public ProductUsageEdit Usage
    {
        get => _usage;
        set
        {
            this.RaiseAndSetIfChanged(ref _usage, value);
            this.RaisePropertyChanged(nameof(Header));
            this.RaisePropertyChanged(nameof(Glyph));
        }
    }

    public override string Header => Usage.Name;

    /// <summary>A list for the product's own sub-assembly, a link for another product's.</summary>
    public override string Glyph => Usage.IsLinked ? "" : "";
}

/// <summary>The tab that adds a sub-assembly: showing it opens the sub-assembly form.</summary>
public sealed class AddSubAssemblyRecipeTab : RecipeTab
{
    public override string Header => "Add Sub-Assembly";

    public override string Glyph => "";
}

/// <summary>The packaging's tab, with its lines and form. There's always exactly one.</summary>
public sealed class PackagingRecipeTab : RecipeTab
{
    public override string Header => "Packaging";

    public override string Glyph => "";
}
