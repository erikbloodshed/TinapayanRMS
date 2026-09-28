using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>The Recipe Structure's add/edit sub-assembly form. Inherits the Products screen's DataContext (ProductsViewModel).</summary>
public partial class UsageFormPanel : UserControl
{
    public UsageFormPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The add-ins badge: scrolls the tab down to the add-ins table (RecipeStructureCard's ScrollViewer) and puts
    /// focus in it, so a keyboard user lands there too. Click fires as the button comes up, so FocusScrolling's
    /// SkipOnClick, which only holds while the button is down, lets the scroll through.
    /// </summary>
    private void AddInsBadge_Click(object sender, RoutedEventArgs e)
    {
        Keyboard.Focus(AddInsGrid);
        AddInsSection.BringIntoView();
    }

    /// <summary>
    /// The add-ins table's Go to top badge: the way back. Focus goes to the lines table, and the form's top left corner
    /// is brought into view, which scrolls the tab to its top, so the badges over the lines show as well as the lines.
    /// </summary>
    private void GoToTopBadge_Click(object sender, RoutedEventArgs e)
    {
        Keyboard.Focus(LinesGrid);
        FormRoot.BringIntoView(new Rect(0, 0, 1, 1));
    }
}
