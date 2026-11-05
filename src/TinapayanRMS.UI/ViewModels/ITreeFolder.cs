using System.Collections.ObjectModel;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A folder of one of the main screens' lists that holds rows of <typeparamref name="TNode"/>, as TreeRows lays it out:
/// ProductFolderNode, RawMaterialFolderNode and SubAssemblyFolderNode.
/// </summary>
internal interface ITreeFolder<TNode> : IListFolder
{
    /// <summary>Its rows, which the tree finds through a HierarchyPropertyDescriptor for the folder's type.</summary>
    ObservableCollection<TNode> Children { get; }

    /// <summary>Shows how many records it holds, after its title.</summary>
    void ShowCount(int count);

    /// <summary>
    /// Tells the tree again whether the folder is open. The tree reads IsExpanded as it changes, but not for a folder
    /// shown again after a search hid it, nor for one that was empty when it opened (it can't open a folder with nothing
    /// in it) and has rows now: without this, an open folder would come back closed.
    /// </summary>
    void AnnounceExpanded();
}
