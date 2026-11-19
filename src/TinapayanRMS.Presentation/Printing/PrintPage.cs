using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.Printing;

/// <summary>
/// The page every printout is laid out on (Settings → Printing): the paper, which way up, and the margins. Read again as
/// each printout is prepared (the Print screen's pages, Export to PDF, the Price Simulator's report), so a change saved
/// on another computer is used too, and set at once by Settings' own Save. A4, portrait and normal margins until read.
/// </summary>
public sealed class PrintPage(IPrintSettingsRepository repository)
{
    public PrintSettings Settings { get; private set; } = new();

    /// <summary>The paper as it's printed on, in millimetres: wider than tall for landscape.</summary>
    public PageArea Paper => PrintRules.PaperOf(Settings);

    public PageMarginWidths Margins => PrintRules.MarginsOf(Settings.Margins);

    /// <summary>What's printed on, in millimetres: the paper less its margins.</summary>
    public PageArea PrintedArea => PrintRules.PrintedAreaOf(Settings);

    public async Task RefreshAsync(CancellationToken cancellationToken) =>
        Settings = await repository.GetAsync(cancellationToken);

    /// <summary>What Settings has just saved.</summary>
    public void Use(PrintSettings settings) => Settings = settings;
}
