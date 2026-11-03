using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Linked Cost for a linked ingredient: the ingredient it's a part of, its shares of that
/// one's weight and cost, and what they work out to. Inherits the Raw Materials screen's DataContext
/// (RawMaterialsViewModel).
/// </summary>
public sealed partial class LinkedCostCard : UserControl
{
    public LinkedCostCard()
    {
        InitializeComponent();
    }
}
