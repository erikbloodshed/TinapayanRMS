using System.ComponentModel;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A folder of one of the main screens' lists (ProductFolderNode, RawMaterialFolderNode, SubAssemblyFolderNode), as
/// <see cref="FolderMemory"/> sees it: a key that lasts from one run to the next, and whether it's open.
/// </summary>
public interface IListFolder : INotifyPropertyChanged
{
    /// <summary>What names the folder from one run to the next, such as "Production/12".</summary>
    string Key { get; }

    /// <summary>The folder is open. Its changes are raised as PropertyChanged.</summary>
    bool IsExpanded { get; set; }
}
