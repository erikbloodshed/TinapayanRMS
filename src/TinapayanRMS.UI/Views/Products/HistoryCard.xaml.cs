using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// The Products screen's History tab: a saved product's versions, what changed in the one picked, and Restore. Inherits
/// the Products screen's DataContext (ProductsViewModel).
/// </summary>
public sealed partial class HistoryCard : UserControl
{
    public HistoryCard()
    {
        InitializeComponent();
    }
}
