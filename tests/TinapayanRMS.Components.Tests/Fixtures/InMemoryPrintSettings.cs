using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Components.Tests.Fixtures;

/// <summary>Settings → Printing kept in memory, for a component that reads it as it prints (Export to PDF), with no database.</summary>
public sealed class InMemoryPrintSettings : IPrintSettingsRepository
{
    public PrintSettings Saved { get; set; } = new();

    public Task<PrintSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Saved);

    public Task SetAsync(PrintSettings settings, CancellationToken cancellationToken)
    {
        Saved = settings;
        return Task.CompletedTask;
    }
}
