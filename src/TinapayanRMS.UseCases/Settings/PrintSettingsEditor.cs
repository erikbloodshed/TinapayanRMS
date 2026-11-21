using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.Settings;

/// <summary>
/// How every printout is laid out (<see cref="PrintSettings"/>), kept in the database for every computer the shop prints
/// from: read as each printout is prepared, and saved by Settings' Printing tab. A refusal is a
/// <see cref="BusinessRuleException"/>, in words for the shop's staff.
/// </summary>
public interface IPrintSettingsEditor
{
    /// <summary>The settings as saved, or the app's own where none are.</summary>
    Task<PrintSettings> GetAsync(CancellationToken cancellationToken);

    /// <summary>Saves <paramref name="settings"/>; the settings as kept, names tidied, for the printouts and the fields.</summary>
    Task<PrintSettings> SaveAsync(PrintSettings settings, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IPrintSettingsEditor"/>
public sealed class PrintSettingsEditor(IPrintSettingsRepository repository) : IPrintSettingsEditor
{
    public Task<PrintSettings> GetAsync(CancellationToken cancellationToken) => repository.GetAsync(cancellationToken);

    public async Task<PrintSettings> SaveAsync(PrintSettings settings, CancellationToken cancellationToken)
    {
        await repository.SetAsync(settings, cancellationToken);
        return await repository.GetAsync(cancellationToken);
    }
}
