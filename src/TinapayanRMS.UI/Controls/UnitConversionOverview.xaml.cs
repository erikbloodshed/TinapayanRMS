using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// The "Unit Conversion Overview" diagram: one box per alternative unit ("1 bag", "25,000 g")
/// pointing down into the item's base unit. Each item in ItemsSource needs a UnitName and a
/// BaseEquivalentText property, which the ingredient and packaging unit rows and the Products
/// screen's sub-assembly usages all have.
/// </summary>
public partial class UnitConversionOverview : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(UnitConversionOverview), new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty BaseUnitNameProperty = DependencyProperty.Register(
        nameof(BaseUnitName), typeof(string), typeof(UnitConversionOverview), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowAsRowsProperty = DependencyProperty.Register(
        nameof(ShowAsRows), typeof(bool), typeof(UnitConversionOverview), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey HasItemsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasItems), typeof(bool), typeof(UnitConversionOverview), new PropertyMetadata(false));

    public static readonly DependencyProperty HasItemsProperty = HasItemsPropertyKey.DependencyProperty;

    public UnitConversionOverview()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>The base unit's name, e.g. "gram".</summary>
    public string BaseUnitName
    {
        get => (string)GetValue(BaseUnitNameProperty);
        set => SetValue(BaseUnitNameProperty, value);
    }

    /// <summary>
    /// A row per unit ("1 Sweet Dough batch → 28 pc") instead of boxes side by side pointing down into the
    /// base unit. For a narrow column, where the boxes would wrap one under another and read as a chain
    /// of conversions (Products' Recipe tab).
    /// </summary>
    public bool ShowAsRows
    {
        get => (bool)GetValue(ShowAsRowsProperty);
        set => SetValue(ShowAsRowsProperty, value);
    }

    /// <summary>Whether there's at least one alternative unit to draw; the empty-state hint shows otherwise.</summary>
    public bool HasItems => (bool)GetValue(HasItemsProperty);

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overview = (UnitConversionOverview)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= overview.OnItemsChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += overview.OnItemsChanged;
        }

        overview.UpdateHasItems();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateHasItems();

    private void UpdateHasItems() =>
        SetValue(HasItemsPropertyKey, ItemsSource?.GetEnumerator().MoveNext() == true);
}
