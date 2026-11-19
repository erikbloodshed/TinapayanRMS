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

    public const int MaxShopNameLength = 100;
    public const int MaxShopAddressLength = 200;
    public const int MaxFooterNoteLength = 150;

    /// <summary>The largest logo kept: half a megabyte, many times what a logo printed 16 mm tall needs.</summary>
    public const int MaxLogoBytes = 512 * 1024;

    /// <summary>The type's size as a share of the normal size.</summary>
    public static decimal TypeScaleOf(PrintTypeSize size) => size switch
    {
        PrintTypeSize.Normal => 1m,
        PrintTypeSize.Compact => 0.9m,
        PrintTypeSize.Large => 1.2m,
        _ => throw new BusinessRuleException("Choose a type size from the list."),
    };

    /// <summary>
    /// A picture's media type, read from its first bytes rather than its file's name: "image/png" or "image/jpeg". Refuses
    /// anything else, and a picture over <see cref="MaxLogoBytes"/>.
    /// </summary>
    public static string LogoContentTypeOf(byte[] picture)
    {
        EnsureLogoSize(picture.Length);
        if (picture is [0x89, 0x50, 0x4E, 0x47, ..])
        {
            return "image/png";
        }

        if (picture is [0xFF, 0xD8, 0xFF, ..])
        {
            return "image/jpeg";
        }

        throw new BusinessRuleException("A logo must be a PNG or JPEG picture.");
    }

    /// <summary>Refuses a picture over <see cref="MaxLogoBytes"/>, before it's read when its file's size is known.</summary>
    public static void EnsureLogoSize(long bytes)
    {
        if (bytes > MaxLogoBytes)
        {
            throw new BusinessRuleException($"That picture is {(bytes + 1023) / 1024:N0} KB: a logo can be at most {MaxLogoBytes / 1024:N0} KB. Save a smaller copy of it.");
        }
    }

    /// <summary>A typed name, address or note as it's kept: without spaces around it, and null when there's nothing left.</summary>
    public static string? Tidy(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Refuses a paper size, orientation, margins or type size the lists don't offer, a heading or note too long, and a logo that isn't one.</summary>
    public static void EnsureValid(PrintSettings settings)
    {
        _ = PaperSizeOf(settings.PaperSize);
        _ = MarginsOf(settings.Margins);
        _ = TypeScaleOf(settings.TypeSize);
        if (!Enum.IsDefined(settings.Orientation))
        {
            throw new BusinessRuleException("Choose portrait or landscape.");
        }

        EnsureAtMost(settings.ShopName, MaxShopNameLength, "The shop's name");
        EnsureAtMost(settings.ShopAddress, MaxShopAddressLength, "The address line");
        EnsureAtMost(settings.FooterNote, MaxFooterNoteLength, "The note at the foot of each page");

        if (settings.Logo is { } logo)
        {
            if (LogoContentTypeOf(logo) != settings.LogoContentType)
            {
                throw new BusinessRuleException("The logo's picture and its type don't match.");
            }
        }
        else if (settings.LogoContentType is not null)
        {
            throw new BusinessRuleException("A logo's type was given with no picture.");
        }
    }

    private static void EnsureAtMost(string? text, int length, string what)
    {
        if (text is not null && text.Length > length)
        {
            throw new BusinessRuleException($"{what} can be at most {length} characters.");
        }
    }
}

/// <summary>A width and a height, in millimetres.</summary>
public readonly record struct PageArea(decimal Width, decimal Height);

/// <summary>A page's margins, in millimetres: the top, each side, and the foot.</summary>
public readonly record struct PageMarginWidths(decimal Top, decimal Side, decimal Bottom);
