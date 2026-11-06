using System.ComponentModel;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Xps;
using Serilog;
using Syncfusion.Windows.Shared.Printing;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Printing;

/// <summary>
/// What Syncfusion's print pane (<see cref="PrintPreview"/>, in Syncfusion.Shared.WPF) prints: a product's printout, its
/// recipe scaled, or a set of products', laid out again (<see cref="ProductPrintLayout"/>) whenever the pane's paper,
/// margins or orientation change or a part is ticked in or out, each page handed over newly built. The pane asks for its
/// pages one at a time as it scrolls (PrintPreviewPanel), and printing asks for every page again for page controls of its
/// own.
/// </summary>
public sealed class ProductPrintManager : PrintManager
{
    /// <summary>
    /// A4, Syncfusion's own starting page (PrintSettingsBase), in device-independent pixels: the paper when Windows has no
    /// default printer to ask.
    /// </summary>
    public static readonly Size A4 = new(793.7, 1122.52);

    private ProductPrintLayout? _layout;

    /// <summary><paramref name="printSet"/>, laid out on <paramref name="paper"/> until another is picked in the pane.</summary>
    /// <param name="paper">
    /// The paper the pane starts on, portrait whichever way it's given: <see cref="DefaultPrinterPaper"/> in the app. The
    /// pane picks the printer's paper nearest it in its paper list.
    /// </param>
    public ProductPrintManager(ProductPrintSet printSet, Size paper)
        : base(PortraitSettings(paper))
    {
        _printSet = printSet;

        // The page itself starts on the paper too, not only the settings: the pane's paper list picks the paper nearest the
        // page as soon as the pane is made, before the settings are copied into the page (InitializeProperties), and the
        // page starts as A4. Otherwise the list showed A4 over pages laid out on the paper.
        PageWidth = PrintSettings.PageWidth;
        PageHeight = PrintSettings.PageHeight;
    }

    /// <summary>One product on its own.</summary>
    public ProductPrintManager(ProductPrintout printout, Size paper)
        : this(ProductPrintSet.Of(printout), paper)
    {
    }

    /// <summary>
    /// The paper of Windows' default printer, as its printing preferences set it (its user ticket, else its own default):
    /// the printer the pane starts on, and the ticket a job goes out on (<see cref="JobTicket"/>). <see cref="A4"/> when
    /// there's no default printer, the print spooler can't be reached, or the ticket names no size.
    /// </summary>
    public static Size DefaultPrinterPaper()
    {
        try
        {
            using var server = new LocalPrintServer();
            using var queue = server.DefaultPrintQueue;
            if (queue is not null
                && (queue.UserPrintTicket ?? queue.DefaultPrintTicket)?.PageMediaSize is { Width: double width and > 0, Height: double height and > 0 })
            {
                return new Size(width, height);
            }
        }
        catch (Exception ex) when (ex is PrintSystemException or Win32Exception or InvalidOperationException)
        {
            Log.Warning(ex, "The default printer's paper couldn't be read; the print pane starts on A4");
        }

        return A4;
    }

    // Syncfusion's settings with the page set to the paper, portrait, and its own 2.5 cm margins.
    private static PrintSettingsBase PortraitSettings(Size paper) => new()
    {
        PageWidth = Math.Min(paper.Width, paper.Height),
        PageHeight = Math.Max(paper.Width, paper.Height),
    };

    private ProductPrintSet _printSet;
    /// <summary>
    /// What's printed. The Print tab hands over a new one each time it's shown or a different choice is made, and the pane
    /// lays it out again on the paper, margins and orientation already picked.
    /// </summary>
    public ProductPrintSet PrintSet
    {
        get => _printSet;
        set
        {
            if (!ReferenceEquals(_printSet, value))
            {
                var wasScaling = _printSet.IsScaling;
                _printSet = value;
                OnPropertyChanged(nameof(PrintSet));
                if (value.IsScaling != wasScaling)
                {
                    OnPropertyChanged(nameof(IsScaling));
                }

                OnPrintPropertyChanged();
            }
        }
    }

    /// <summary>
    /// The pages are the Products screen's Scaling tab, in place of the recipe and the costing: the Include bar then offers
    /// only its costs (<see cref="IncludeCosting"/>).
    /// </summary>
    public bool IsScaling => PrintSet.IsScaling;

    private bool _includeRecipe = true;
    /// <summary>Print the recipe: each sub-assembly's table and piece size, then the packaging.</summary>
    public bool IncludeRecipe
    {
        get => _includeRecipe;
        set
        {
            if (_includeRecipe != value)
            {
                _includeRecipe = value;
                OnPropertyChanged(nameof(IncludeRecipe));
                OnPrintPropertyChanged();
            }
        }
    }

    private bool _includeCosting = true;
    /// <summary>
    /// Print the costing: the headline figures and the breakdown to the selling price. Without it nothing printed is a
    /// cost: the recipe leaves out its costs, and so does the Scaling tab, which this is the only choice for.
    /// </summary>
    public bool IncludeCosting
    {
        get => _includeCosting;
        set
        {
            if (_includeCosting != value)
            {
                _includeCosting = value;
                OnPropertyChanged(nameof(IncludeCosting));

                // Something always prints. The Scaling tab's Costs can be cleared while Recipe is cleared too, which would
                // leave the next recipe and costing with neither part, so Recipe is ticked again.
                if (!value && !_includeRecipe)
                {
                    _includeRecipe = true;
                    OnPropertyChanged(nameof(IncludeRecipe));
                }

                OnPrintPropertyChanged();
            }
        }
    }

    /// <summary>The printout couldn't be handed to the printer. The pane reports nothing itself, so the window says so.</summary>
    public event EventHandler<Exception>? PrintFailed;

    /// <summary>What a page leaves for the printout: the paper, in the orientation picked, less its margins.</summary>
    public Size PrintableSize => new(
        Math.Max(0, PageWidth - PageMargin.Left - PageMargin.Right),
        Math.Max(0, PageHeight - PageMargin.Top - PageMargin.Bottom - PageHeaderHeight - PageFooterHeight));

    /// <summary>
    /// Lays the printout out for the page as it's now set up. The pane calls it, through InitializePrint, after swapping the
    /// page's width and height for the orientation, and again on every change to the paper, the margins or the orientation.
    /// </summary>
    protected override void ComputePageCount()
    {
        _layout = new ProductPrintLayout(PrintSet, IncludeRecipe, IncludeCosting, PrintableSize);
        PageCount = _layout.PageCount;
    }

    /// <summary>
    /// Puts page <paramref name="pageIndex"/> (from 1) in <paramref name="pageControl"/>: the pane reuses one control as it
    /// scrolls, and the printer asks with new ones, so the page is built anew every time. The control's template supplies
    /// the paper and the margins around it. The paper is white and unframed: the theme's template leaves it transparent
    /// over the pane's grey, and the plain one the printer gets draws a grey line round the page's edge.
    /// </summary>
    protected override PrintPageControl CreatePage(int pageIndex, PrintPageControl pageControl)
    {
        _layout ??= new ProductPrintLayout(PrintSet, IncludeRecipe, IncludeCosting, PrintableSize);
        pageControl.PageIndex = pageIndex;
        pageControl.Background = _layout.Paper;
        pageControl.BorderThickness = new Thickness(0);
        pageControl.Content = _layout.BuildPage(pageIndex);
        return pageControl;
    }

    /// <summary>
    /// Prints the pages in the pane's range to the printer picked there, on the printer's own ticket with the pane's choices
    /// on it (<see cref="JobTicket"/>). The job goes by the printout's name, and a failure is logged and reported
    /// (<see cref="PrintFailed"/>) where the base class swallows it.
    /// </summary>
    protected override void PrintDocument(PrintDialog printDialog, bool? canPrint)
    {
        if (canPrint != true)
        {
            return;
        }

        try
        {
            _layout ??= new ProductPrintLayout(PrintSet, IncludeRecipe, IncludeCosting, PrintableSize);
            var (from, to) = printDialog.PageRange is { PageFrom: > 0, PageTo: > 0 } range
                ? (range.PageFrom, Math.Min(range.PageTo, PageCount))
                : (1, PageCount);

            var document = new FixedDocument();
            for (var index = from; index <= to; index++)
            {
                var page = new FixedPage { Width = PageWidth, Height = PageHeight };
                page.Children.Add(CreatePage(index));
                var content = new PageContent();
                ((IAddChild)content).AddChild(page);
                document.Pages.Add(content);
            }

            if (printDialog.PrintQueue is { } queue)
            {
                printDialog.PrintTicket = JobTicket(queue, printDialog.PrintTicket);
            }

            printDialog.PrintDocument(document.DocumentPaginator, _layout.DocumentName);
        }
        catch (PrintingCanceledException)
        {
            // Cancelled where the printer asked something, such as Microsoft Print to PDF's file name: nothing to report.
        }
        catch (Exception ex) when (ex is PrintSystemException or XpsException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Printing {Document} to {Printer} failed", _layout?.DocumentName, printDialog.PrintQueue?.FullName);
            PrintFailed?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// The job's ticket: the printer's own, with the paper, orientation, copies and collation picked in the pane on it, as
    /// the printer validates it. Not the ticket the pane hands over (Syncfusion's GetCustomizedPrintTicket, whose paper the
    /// base class also swaps for a bare size): Microsoft Print to PDF can't read it, its filter stops, and the PDF comes
    /// out empty, while the same pages print through the printer's own. The paper is the printer's own size nearest the
    /// page, to 2 px either way; with none that near, the printer's default stays.
    /// </summary>
    private PrintTicket JobTicket(PrintQueue queue, PrintTicket chosen)
    {
        var width = Math.Min(PageWidth, PageHeight);
        var height = Math.Max(PageWidth, PageHeight);
        var choices = new PrintTicket
        {
            PageOrientation = PageOrientation == PrintOrientation.Landscape
                ? System.Printing.PageOrientation.Landscape
                : System.Printing.PageOrientation.Portrait,
            CopyCount = chosen.CopyCount,
            Collation = chosen.Collation,
            PageMediaSize = queue.GetPrintCapabilities().PageMediaSizeCapability.FirstOrDefault(paper =>
                Math.Abs((paper.Width ?? 0) - width) < 2 && Math.Abs((paper.Height ?? 0) - height) < 2),
        };

        return queue.MergeAndValidatePrintTicket(queue.UserPrintTicket ?? queue.DefaultPrintTicket, choices).ValidatedPrintTicket;
    }
}
