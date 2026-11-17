using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Trees;

/// <summary>
/// A row of one of the main screens' lists, a folder or a record in one: what the list's tree draws for it, whichever
/// list it is. ProductTreeNode, RawMaterialTreeNode and SubAssemblyTreeNode are each a closed set of these.
/// </summary>
public abstract class ListTreeNode : ObservableState
{
    public abstract string Title { get; }

    /// <summary>The icon before the title, or null for none.</summary>
    public abstract AppIcon? Icon { get; }

    public abstract bool IsFolder { get; }

    /// <summary>How many records a folder holds, shown after its title; always 0 for a record's row, which shows none.</summary>
    public virtual int Count => 0;
}
