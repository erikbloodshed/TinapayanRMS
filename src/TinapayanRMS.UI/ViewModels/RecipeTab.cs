using ReactiveUI;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A tab of the Products screen's Recipe Structure (ProductsViewModel.RecipeTabs): one per sub-assembly, in
/// recipe order (after a composite's Products), then New Sub-Assembly while one is being added, then Packaging while
/// the product has any.
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

    /// <summary>What the sub-assembly is called: its role, with its name when the recipe has more than one in the role.</summary>
    public override string Header => Usage.SectionName;

    /// <summary>Shows the header again once the recipe has gained or lost another section in the role (ProductUsageEdit.IsRoleRepeated).</summary>
    public void RefreshHeader() => this.RaisePropertyChanged(nameof(Header));

    /// <summary>A list for a new section, whose lines are typed in here; a link for a linked one.</summary>
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

/// <summary>
/// A composite product's Products tab, its table of the products it holds and how many of each: first, before its own
/// sections' tabs (a topping, a filling). Only a composite has it.
/// </summary>
public sealed class ComponentsRecipeTab : RecipeTab
{
    public override string Header => "Products";

    /// <summary>Segoe Fluent Icons' AllApps: several things together.</summary>
    public override string Glyph => "";
}

/// <summary>The packaging's tab, with its lines and form. There's always exactly one.</summary>
public sealed class PackagingRecipeTab : RecipeTab
{
    public override string Header => "Packaging";

    public override string Glyph => "";
}
