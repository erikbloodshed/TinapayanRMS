using System.IO;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Xps.Packaging;
using AwesomeAssertions;
using Syncfusion.Windows.Shared.Printing;
using TinapayanRMS.UI.Printing;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.Printing;

/// <summary>
/// ProductPrintManager: a product's printout as Syncfusion's print pane and the printer take it, through the PrintManager
/// they drive (Syncfusion.Shared.WPF): laid out for the pane's page, and each page handed over in a page control of its own.
/// </summary>
public class ProductPrintManagerTests
{
    [Fact]
    public void The_print_document_holds_each_page_of_the_layout_in_a_page_control() => StaThread.RunLicensed(() =>
    {
        using var manager = new ProductPrintManager(SamplePrintouts.WithLines(80));

        var document = manager.GetPrintDocument();

        manager.PrintableSize.Should().Be(SamplePrintouts.A4Printable, "Syncfusion's page starts as A4 with 2.5 cm margins");
        document.Pages.Should().HaveCount(manager.PageCount).And.HaveCountGreaterThan(1);
        foreach (var content in document.Pages)
        {
            var pageControl = content.Child.Children.OfType<PrintPageControl>().Single();
            pageControl.Content.Should().BeOfType<Grid>().Which.Width.Should().Be(manager.PrintableSize.Width);

            // White paper with no frame: Syncfusion's plain page template, which a printed page gets, draws a grey line round it.
            pageControl.Background.Should().Be(Brushes.White);
            pageControl.BorderThickness.Should().Be(new Thickness(0));
        }
    });

    [Fact]
    public void The_pages_embed_only_subsets_of_their_fonts_as_Microsoft_Print_to_PDF_needs() => StaThread.RunLicensed(() =>
    {
        // A formula base's tick, typed as ✓, came from Segoe UI Emoji, which the pages then embedded whole (8 MB), and
        // Microsoft Print to PDF's filter gave up on them: the PDF came out empty. The pages keep to what Segoe UI has.
        using var manager = new ProductPrintManager(SamplePrintouts.WithLines(15));
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xps");
        try
        {
            using (var xps = new XpsDocument(path, FileAccess.ReadWrite))
            {
                XpsDocument.CreateXpsDocumentWriter(xps).Write(manager.GetPrintDocument().DocumentPaginator);
            }

            using var package = Package.Open(path, FileMode.Open, FileAccess.Read);
            var fonts = package.GetParts().Where(p => p.ContentType == "application/vnd.ms-package.obfuscated-opentype").ToList();
            fonts.Should().NotBeEmpty();
            foreach (var font in fonts)
            {
                using var stream = font.GetStream();
                stream.Length.Should().BeLessThan(1_000_000, $"{font.Uri} should be a subset of Segoe UI");
            }
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public void Leaving_the_costing_out_lays_the_pages_out_again_without_it() => StaThread.RunLicensed(() =>
    {
        // A recipe that fills most of the first page, so the costing, kept together, takes a page of its own.
        using var manager = new ProductPrintManager(SamplePrintouts.WithLines(25));
        List<string?> changed = [];
        manager.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        manager.GetPrintDocument().Pages.Should().HaveCount(2);

        manager.IncludeCosting = false;

        changed.Should().Contain(nameof(ProductPrintManager.IncludeCosting), "the Costing tick box follows it");
        manager.GetPrintDocument().Pages.Should().ContainSingle();
    });

    [Fact]
    public void A_scaled_recipe_offers_only_its_costs_and_clearing_them_keeps_the_recipe_ticked_for_the_next_printout() => StaThread.RunLicensed(() =>
    {
        using var manager = new ProductPrintManager(SamplePrintouts.WithLines(3)) { IncludeRecipe = false };
        List<string?> changed = [];
        manager.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        manager.IsScaling.Should().BeFalse();

        manager.PrintSet = ProductPrintSet.Of(SamplePrintouts.Scaled());
        changed.Should().Contain(nameof(ProductPrintManager.IsScaling), "the Include bar swaps Recipe and Costing for Costs");
        manager.IsScaling.Should().BeTrue();

        // The Costs box, cleared while Recipe was: the recipe and costing printed next must still have a part.
        manager.IncludeCosting = false;

        manager.IncludeRecipe.Should().BeTrue();
        changed.Should().Contain(nameof(ProductPrintManager.IncludeRecipe), "the Recipe tick box follows it");
    });
}
