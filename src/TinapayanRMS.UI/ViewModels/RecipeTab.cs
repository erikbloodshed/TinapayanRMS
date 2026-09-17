using ReactiveUI;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A tab of the Products screen's Recipe Structure (ProductsViewModel.RecipeTabs): one per sub-assembly, in
/// recipe order, then New Sub-Assembly while one is being added, then Packaging, which is always there.
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

    /// <summary>Its role: a product has one sub-assembly in each, and it's what the sub-assembly is called.</summary>
    public override string Header => Usage.RoleName;

    /// <summary>A list for the product's own sub-assembly, a link for another product's.</summary>
    public override string Glyph => Usage.IsLinked ? "" : "";
}

/// <summary>
/// The tab a sub-assembly is added in, with the add form. Add Sub-Assembly opens it; it goes again once the add is
/// applied, cancelled, or left with nothing entered.
/// </summary>
public sealed class AddSubAssemblyRecipeTab : RecipeTab
{
    public override string Header => "New Sub-Assembly";

    public override string Glyph => "";
}

/// <summary>The packaging's tab, with its lines and form. There's always exactly one.</summary>
public sealed class PackagingRecipeTab : RecipeTab
{
    public override string Header => "Packaging";

    public override string Glyph => "";
}
