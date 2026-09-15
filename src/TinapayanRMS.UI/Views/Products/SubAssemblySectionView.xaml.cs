using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// One section of the Recipe Structure: a sub-assembly the product uses (its DataContext is the
/// Usage of its Recipe Structure tab, a ProductUsageEdit) with its ingredient lines scaled to one piece.
/// </summary>
public partial class SubAssemblySectionView : UserControl
{
    public SubAssemblySectionView()
    {
        InitializeComponent();
    }
}
