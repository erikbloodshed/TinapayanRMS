using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.SubAssemblies;

/// <summary>
/// The Sub-Assemblies list: the sub-assemblies in a folder for their role, with a search box. It works as the Raw
/// Materials and Products lists do (Controls/ListTree): dragging recipes onto another role's folder gives them that role; a
/// right-click opens a menu of what can be done with the row, or with the list; F2, Del and Enter work on the row picked.
/// Inherits the Sub-Assemblies screen's DataContext (SubAssembliesViewModel).
/// </summary>
public sealed partial class SubAssemblyListCard : UserControl
{
    public SubAssemblyListCard() => InitializeComponent();
}
