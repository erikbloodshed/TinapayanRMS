using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// How the bakeshop's printouts are laid out, set in Settings → Printing: the paper, which way up and the margins; the
/// shop's heading (name, address, logo) at the top of each part and a note at the foot of each page; the type's size; and
/// which columns and parts are left out. Print, Export to PDF and the Price Simulator report all use it. One row at most
/// (Id 1); no row means A4, portrait, normal margins, normal type, no heading or note, and everything printed.
/// </summary>
public sealed class PrintSettings
{
    /// <summary>The one row's id.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public PaperSize PaperSize { get; set; } = PaperSize.A4;

    public PaperOrientation Orientation { get; set; } = PaperOrientation.Portrait;

    public PageMargins Margins { get; set; } = PageMargins.Normal;

    /// <summary>The shop's name, at the top of each part of a printout; null for no heading.</summary>
    public string? ShopName { get; set; }

    /// <summary>A line under the name: an address, a phone number; null for none.</summary>
    public string? ShopAddress { get; set; }

    /// <summary>The logo beside the heading, a PNG or JPEG picture of at most PrintRules.MaxLogoBytes; null for none.</summary>
    public byte[]? Logo { get; set; }

    /// <summary>The logo's media type, "image/png" or "image/jpeg", with it or null without it.</summary>
    public string? LogoContentType { get; set; }

    /// <summary>A note at the foot of every page, opposite its number: "For internal use only"; null for none.</summary>
    public string? FooterNote { get; set; }

    public PrintTypeSize TypeSize { get; set; } = PrintTypeSize.Normal;

    /// <summary>Leaves out each recipe line's Cost/g and each packaging line's Unit Cost.</summary>
    public bool OmitUnitCosts { get; set; }

    /// <summary>Leaves out the recipe's Baker's % or Absolute % column.</summary>
    public bool OmitPercentages { get; set; }

    /// <summary>Leaves out the figure tiles under each product's title (Material Cost, Base Cost…).</summary>
    public bool OmitFigures { get; set; }

    /// <summary>Leaves out the suggested price beside the current one, under the costing.</summary>
    public bool OmitPriceComparison { get; set; }
}
