using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.Windows.Shared.Printing;
using TinapayanRMS.UI.Printing;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.Printing;

/// <summary>
/// PrintPaneFit: Syncfusion's print pane, as the Windows11Light theme draws it, fitted to the app's 14 px text. Opened
/// off-screen in a themed window at the app's theme settings, as the main window's Print tab has it.
/// </summary>
public class PrintPaneFitTests
{
    [Test]
    public void The_settings_column_is_wider_and_every_dropdown_shows_its_choice_in_an_item_that_fits_its_text() => WithPane(preview =>
    {
        var root = (Grid)VisualTreeHelper.GetChild(preview, 0);
        root.ColumnDefinitions[0].ActualWidth.Should().Be(PrintPaneFit.OptionsWidth);

        var dropdowns = Descendants<ComboBox>(Options(preview)).Where(c => c.IsVisible).ToList();
        dropdowns.Should().HaveCount(6, "printer, pages, collation, orientation, paper and margins; Scaling is hidden");
        foreach (var dropdown in dropdowns)
        {
            dropdown.ActualWidth.Should().Be(PrintPaneFit.FieldWidth, dropdown.Name);
            dropdown.SelectionBoxItemTemplate.Should().BeSameAs(dropdown.ItemTemplate, $"{dropdown.Name}'s closed box shows its choice in the new item");
        }
    });

    [Test]
    public void No_text_in_the_settings_or_the_page_box_is_cut_off() => WithPane(preview =>
    {
        foreach (var text in Descendants<TextBlock>(Options(preview)).Where(t => t.IsVisible))
        {
            // A block held to a size smaller than its text keeps the text's size and is clipped by layout, so its room is
            // the smaller of the two; one trimmed with an ellipsis is simply narrower.
            var needs = Needs(text, new TextRange(text.ContentStart, text.ContentEnd).Text);
            var clip = LayoutInformation.GetLayoutClip(text)?.Bounds;
            Math.Min(text.ActualWidth, clip?.Width ?? double.PositiveInfinity)
                .Should().BeGreaterThanOrEqualTo(needs.Width - 0.5, $"\"{needs.Text}\" is whole");
            Math.Min(text.ActualHeight, clip?.Height ?? double.PositiveInfinity)
                .Should().BeGreaterThanOrEqualTo(needs.Height - 0.5, $"\"{needs.Text}\" isn't cut at its foot");
        }

        var pageBox = (TextBox)preview.Template.FindName("PART_TextBox", preview);
        pageBox.ActualHeight.Should().BeGreaterThanOrEqualTo(Needs(pageBox, "12").Height, "the page number fits its box");
    });

    [Test]
    public void The_page_stays_at_the_panes_zoom_when_it_is_laid_out_again() => WithPane((preview, manager) =>
    {
        var area = (PrintPreviewAreaControl)preview.Template.FindName("PART_PrintPreviewAreaControl", preview);
        var zoomed = PageWidth(area);
        zoomed.Should().BeLessThan(manager.PageWidth, "the pane fits the page to its height");

        manager.IncludeCosting = false;
        OffscreenGrid.Pump();
        PageWidth(area).Should().BeApproximately(zoomed, 1, "a part ticked out is laid out at the same zoom");

        manager.PrintSet = ProductPrintSet.Of(SamplePrintouts.WithLines(3));
        OffscreenGrid.Pump();
        PageWidth(area).Should().BeApproximately(zoomed, 1, "so is a new printout, as the Print tab hands over each time it's shown");
    });

    /// <summary>How wide the pane draws its page: its page control's viewbox, which the pane sizes to its zoom.</summary>
    private static double PageWidth(PrintPreviewAreaControl area)
    {
        var page = Descendants<PrintPageControl>(area).Single();
        return ((FrameworkElement)page.Template.FindName("PART_Viewbox", page)).ActualWidth;
    }

    private static void WithPane(Action<PrintPreview> check) => WithPane((preview, _) => check(preview));

    private static void WithPane(Action<PrintPreview, ProductPrintManager> check) => OffscreenPrintPane.Run(ProductPrintManager.A4, check);

    private static PrintOptionsControl Options(PrintPreview preview) => OffscreenPrintPane.Options(preview);

    /// <summary>What <paramref name="text"/> takes on one line in <paramref name="element"/>'s font.</summary>
    private static (string Text, double Width, double Height) Needs(Control element, string text) =>
        Needs(new Typeface(element.FontFamily, element.FontStyle, element.FontWeight, element.FontStretch), element.FontSize, text);

    private static (string Text, double Width, double Height) Needs(TextBlock element, string text) =>
        Needs(new Typeface(element.FontFamily, element.FontStyle, element.FontWeight, element.FontStretch), element.FontSize, text);

    private static (string Text, double Width, double Height) Needs(Typeface typeface, double size, string text)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, 1);
        return (text, formatted.WidthIncludingTrailingWhitespace, formatted.Height);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
