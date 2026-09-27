using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Serilog;
using Syncfusion.Windows.Shared.Printing;

namespace TinapayanRMS.UI.Printing;

/// <summary>
/// Fits Syncfusion's print pane (PrintPreview, as the Windows11Light theme draws it) to the app's 14 px text. The theme
/// sizes it for 12 px: a 250 px column of 220 px dropdowns whose items are 43 px rows, a Copies label 39 px wide, a Print
/// caption 14 px tall, and a page box under the pages 16 px tall, so at 14 px text runs out of its room. Once the pane's
/// templates are applied, this widens the column, sizes those parts for their text, and gives the dropdowns items that
/// grow with it (<see cref="PrintPaneTemplates"/>). The theme's own styles stay: its resources can't be reached from the
/// app's XAML (PLAN.md, the Products tree's white), so a copy of its templates would lose them. It also keeps the page at
/// the pane's zoom each time the pane lays it out again (<see cref="KeepZoom"/>).
/// </summary>
public static class PrintPaneFit
{
    /// <summary>The pane's left column, its settings: 250 px in the theme.</summary>
    public const double OptionsWidth = 340;

    /// <summary>Each dropdown's width, leaving the column a margin either side as the theme's 220 in 250 does.</summary>
    public const double FieldWidth = 300;

    // The Copies, From and To boxes: room for a three-digit number beside the spin buttons.
    private const double NumberBoxWidth = 76;
    private const double NumberBoxHeight = 30;

    // Each dropdown and the item template it gets in place of the theme's. Scaling isn't here: it's hidden, as the
    // printout gives no scale options.
    private static readonly (string Part, string Template)[] Dropdowns =
    [
        ("PART_PrintersComboBox", "PrinterItemTemplate"),
        ("PART_PageRangeSelection", "PageRangeItemTemplate"),
        ("PART_Collated", "CollationItemTemplate"),
        ("PART_OrientationComboBox", "OrientationItemTemplate"),
        ("PART_PapersComboBox", "PaperItemTemplate"),
        ("PART_MarginComboBox", "MarginItemTemplate"),
    ];

    /// <summary>
    /// Fits <paramref name="preview"/> once it's loaded, by when the pane and its settings have their templates. That's
    /// before its first frame is drawn, so the theme's sizes never show.
    /// </summary>
    public static void Apply(PrintPreview preview) => preview.Loaded += OnLoaded;

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var preview = (PrintPreview)sender;
        preview.Loaded -= OnLoaded;

        FitPane(preview);
        if (Part<PrintOptionsControl>(preview, "PART_PrintOptionsControl") is { } options)
        {
            FitOptions(options);
        }

        // The page is taken out and put back within one layout pass, so it's never unloaded: its new template is the sign.
        if (Part<PrintPreviewAreaControl>(preview, "PART_PrintPreviewAreaControl") is { } area && Descendant<PrintPageControl>(area) is { } page)
        {
            DependencyPropertyDescriptor.FromProperty(Control.TemplateProperty, typeof(PrintPageControl)).AddValueChanged(
                page, (_, _) => area.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => KeepZoom(area, page)));
        }
    }

    /// <summary>
    /// Keeps the page at the pane's zoom when the pane lays the printout out again: a part ticked in or out, a new
    /// printout, margins changed. Laying out, the pane takes its one page control out of its panel and puts it back
    /// (PrintPreviewPanel.InValidate), and out of the window's tree it loses the theme's style, so back in it gets its
    /// template again, with a new viewbox of no size: the page is drawn at 100% while the zoom still says otherwise. The
    /// pane sizes that viewbox only when the zoom changes, so the zoom moves by one and back. Found in the app with the
    /// Print tab (2026-09-27); a tick box does it too (PrintPaneFitTests).
    /// </summary>
    private static void KeepZoom(PrintPreviewAreaControl area, PrintPageControl page)
    {
        if (page.Template?.FindName("PART_Viewbox", page) is not Viewbox viewbox || !double.IsNaN(viewbox.Width))
        {
            return;
        }

        var zoom = area.ZoomFactor;
        area.ZoomFactor = zoom > 10 ? zoom - 1 : zoom + 1;
        area.ZoomFactor = zoom;
    }

    private static T? Descendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if ((child as T ?? Descendant<T>(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void FitPane(PrintPreview preview)
    {
        if (VisualTreeHelper.GetChildrenCount(preview) > 0 && VisualTreeHelper.GetChild(preview, 0) is Grid { ColumnDefinitions.Count: 2 } root)
        {
            root.ColumnDefinitions[0].Width = new GridLength(OptionsWidth);
        }
        else
        {
            Log.Warning("The print pane's template has no two-column grid to widen; Syncfusion's template may have changed");
        }

        // Under the pages: the page box, 24 by 16, and "/  12" beside it, 24 wide.
        if (Part<TextBox>(preview, "PART_TextBox") is { } pageBox)
        {
            pageBox.Width = 40;
            pageBox.Height = 26;
        }

        if (Part<TextBlock>(preview, "PART_TextBlock") is { } pageCount)
        {
            pageCount.Width = double.NaN;
            pageCount.MinWidth = 24;
            pageCount.Margin = new Thickness(6, 0, 4, 0);
        }
    }

    private static void FitOptions(PrintOptionsControl options)
    {
        if (Part<TextBlock>(options, "textBlock") is { } printCaption)
        {
            printCaption.Height = double.NaN;
        }

        // Its middle level with the Copies box beside it.
        if (Part<TextBlock>(options, "Part_Copies") is { } copies)
        {
            copies.Width = double.NaN;
            copies.Height = double.NaN;
            copies.Margin = new Thickness(0, 24, 0, 0);
        }

        foreach (var name in (string[])["Part_CopyCount_Grid", "Part_FromPage_Grid", "Part_ToPage_Grid"])
        {
            if (Part<Grid>(options, name) is { } box)
            {
                box.Width = NumberBoxWidth;
                box.Height = NumberBoxHeight;
            }
        }

        var templates = new PrintPaneTemplates();
        foreach (var (part, template) in Dropdowns)
        {
            if (Part<ComboBox>(options, part) is { } dropdown)
            {
                dropdown.Width = FieldWidth;
                dropdown.Height = double.NaN;
                dropdown.ItemTemplate = (DataTemplate)templates[template];

                // A choice made before this, as the printer is while the template loads, stays in the closed box in the
                // theme's item: a ComboBox draws that box again only on a new selection, a new template or a change to
                // IsEditable. Selecting again would have Syncfusion reload the printer's papers, so IsEditable it is.
                if (dropdown.SelectionBoxItemTemplate != dropdown.ItemTemplate)
                {
                    dropdown.IsEditable = true;
                    dropdown.ClearValue(ComboBox.IsEditableProperty);
                }
            }
        }
    }

    /// <summary>The part of <paramref name="control"/>'s template named <paramref name="name"/>, or null, logged, when a newer template has none.</summary>
    private static T? Part<T>(Control control, string name)
        where T : class
    {
        if (control.Template?.FindName(name, control) is T part)
        {
            return part;
        }

        Log.Warning("The print pane has no {Part} ({Type}) to fit; Syncfusion's template may have changed", name, typeof(T).Name);
        return null;
    }
}
