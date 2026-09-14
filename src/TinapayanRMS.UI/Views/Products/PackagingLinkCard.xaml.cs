using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>Products' packaging link: the product it reads its packaging from, if any, and linking or unlinking it. Inherits the Products screen's DataContext (ProductsViewModel).</summary>
public partial class PackagingLinkCard : UserControl
{
    public PackagingLinkCard()
    {
        InitializeComponent();
    }
}
