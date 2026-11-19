using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>How printouts are laid out (PrintSettings, Settings → Printing).</summary>
public interface IPrintSettingsRepository
{
    /// <summary>The one row, or A4, portrait and normal margins while there's none.</summary>
    Task<PrintSettings> GetAsync(CancellationToken cancellationToken);

    /// <summary>Saves the paper, orientation and margins, refusing any the lists don't offer (PrintRules.EnsureValid).</summary>
    Task SetAsync(PrintSettings settings, CancellationToken cancellationToken);
}
