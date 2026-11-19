using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.Printing;

/// <summary>
/// How every printout is laid out (Settings → Printing): the paper, which way up and the margins, the shop's heading and
/// the note at each page's foot, the type's size, and what's left out. Read again as each printout is prepared (the Print
/// screen's pages, Export to PDF, the Price Simulator's report), so a change saved on another computer is used too, and
/// set at once by Settings' own Save. A4, portrait, normal margins and everything printed until read.
/// </summary>
public sealed class PrintPage(IPrintSettingsRepository repository)
{
    private PrintSettings _settings = new();

    public PrintSettings Settings
    {
        get => _settings;
        private set
        {
            _settings = value;
            LogoSource = value.Logo is { } logo ? $"data:{value.LogoContentType};base64,{Convert.ToBase64String(logo)}" : null;
        }
    }

    /// <summary>The paper as it's printed on, in millimetres: wider than tall for landscape.</summary>
    public PageArea Paper => PrintRules.PaperOf(Settings);

    public PageMarginWidths Margins => PrintRules.MarginsOf(Settings.Margins);

    /// <summary>What's printed on, in millimetres: the paper less its margins.</summary>
    public PageArea PrintedArea => PrintRules.PrintedAreaOf(Settings);

    /// <summary>The type's size as a share of the normal size.</summary>
    public decimal TypeScale => PrintRules.TypeScaleOf(Settings.TypeSize);

    /// <summary>The logo as a picture's address the page can show (a data: URL), worked out once a read; null with none.</summary>
    public string? LogoSource { get; private set; }

    /// <summary>Whether a printout's parts start with the shop's heading: a name, an address line or a logo.</summary>
    public bool HasHeading => Settings.ShopName is not null || Settings.ShopAddress is not null || Settings.Logo is not null;

    public async Task RefreshAsync(CancellationToken cancellationToken) =>
        Settings = await repository.GetAsync(cancellationToken);

    /// <summary>What Settings has just saved.</summary>
    public void Use(PrintSettings settings) => Settings = settings;
}
