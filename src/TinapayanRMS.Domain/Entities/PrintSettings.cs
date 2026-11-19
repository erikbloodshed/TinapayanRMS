using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// How the bakeshop's printouts are laid out, set in Settings → Printing: the paper, which way up, and the margins. Print,
/// Export to PDF and the Price Simulator report all use it. One row at most (Id 1); no row means A4, portrait, normal
/// margins.
/// </summary>
public sealed class PrintSettings
{
    /// <summary>The one row's id.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public PaperSize PaperSize { get; set; } = PaperSize.A4;

    public PaperOrientation Orientation { get; set; } = PaperOrientation.Portrait;

    public PageMargins Margins { get; set; } = PageMargins.Normal;
}
