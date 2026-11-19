using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A printout's page from Settings → Printing: the paper's size, turned for landscape, and the blank paper left around
/// what's printed. In millimetres, as CSS's @page takes them.
/// </summary>
public static class PrintRules
{
    /// <summary>The paper's width and height, upright, in millimetres.</summary>
    public static PageArea PaperSizeOf(PaperSize paper) => paper switch
    {
        PaperSize.A4 => new(210m, 297m),
        PaperSize.Letter => new(215.9m, 279.4m),
        PaperSize.LongBond => new(215.9m, 330.2m),
        PaperSize.Legal => new(215.9m, 355.6m),
        _ => throw new BusinessRuleException("Choose a paper size from the list."),
    };

    /// <summary>The margins, in millimetres: the foot's is deeper, as the page number is printed in it.</summary>
    public static PageMarginWidths MarginsOf(PageMargins margins) => margins switch
    {
        PageMargins.Normal => new(14m, 14m, 16m),
        PageMargins.Narrow => new(8m, 8m, 12m),
        PageMargins.Wide => new(20m, 20m, 22m),
        _ => throw new BusinessRuleException("Choose margins from the list."),
    };

    /// <summary>The paper as it's printed on: <see cref="PaperSizeOf"/>, wider than tall for landscape.</summary>
    public static PageArea PaperOf(PrintSettings settings)
    {
        var upright = PaperSizeOf(settings.PaperSize);
        return settings.Orientation == PaperOrientation.Landscape ? new(upright.Height, upright.Width) : upright;
    }

    /// <summary>What's printed on: the paper less its margins.</summary>
    public static PageArea PrintedAreaOf(PrintSettings settings)
    {
        var paper = PaperOf(settings);
        var margins = MarginsOf(settings.Margins);
        return new(paper.Width - 2 * margins.Side, paper.Height - margins.Top - margins.Bottom);
    }

    /// <summary>Refuses a paper size, orientation or margins the lists don't offer.</summary>
    public static void EnsureValid(PrintSettings settings)
    {
        _ = PaperSizeOf(settings.PaperSize);
        _ = MarginsOf(settings.Margins);
        if (!Enum.IsDefined(settings.Orientation))
        {
            throw new BusinessRuleException("Choose portrait or landscape.");
        }
    }
}

/// <summary>A width and a height, in millimetres.</summary>
public readonly record struct PageArea(decimal Width, decimal Height);

/// <summary>A page's margins, in millimetres: the top, each side, and the foot.</summary>
public readonly record struct PageMarginWidths(decimal Top, decimal Side, decimal Bottom);
