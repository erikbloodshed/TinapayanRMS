using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class PrintSettingsRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPrintSettingsRepository
{
    public async Task<PrintSettings> GetAsync(CancellationToken cancellationToken) =>
        await dbContextFactory.QuerySingleOrDefaultAsync<PrintSettings>(
            $"SELECT {Columns.Of<PrintSettings>("s")} FROM print_settings s WHERE s.id = @id",
            new { id = PrintSettings.SingletonId },
            cancellationToken)
        ?? new PrintSettings();

    public async Task SetAsync(PrintSettings settings, CancellationToken cancellationToken)
    {
        // Checked as it's kept: a name of spaces is no name, and a long one is measured without them.
        PrintRules.EnsureValid(new PrintSettings
        {
            PaperSize = settings.PaperSize, Orientation = settings.Orientation, Margins = settings.Margins, TypeSize = settings.TypeSize,
            ShopName = PrintRules.Tidy(settings.ShopName), ShopAddress = PrintRules.Tidy(settings.ShopAddress),
            FooterNote = PrintRules.Tidy(settings.FooterNote), Logo = settings.Logo, LogoContentType = settings.LogoContentType,
        });

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.PrintSettings.FirstOrDefaultAsync(s => s.Id == PrintSettings.SingletonId, cancellationToken);
        if (existing is null)
        {
            existing = new PrintSettings();
            dbContext.PrintSettings.Add(existing);
        }

        existing.PaperSize = settings.PaperSize;
        existing.Orientation = settings.Orientation;
        existing.Margins = settings.Margins;
        existing.ShopName = PrintRules.Tidy(settings.ShopName);
        existing.ShopAddress = PrintRules.Tidy(settings.ShopAddress);
        existing.FooterNote = PrintRules.Tidy(settings.FooterNote);
        existing.Logo = settings.Logo;
        existing.LogoContentType = settings.LogoContentType;
        existing.TypeSize = settings.TypeSize;
        existing.OmitUnitCosts = settings.OmitUnitCosts;
        existing.OmitPercentages = settings.OmitPercentages;
        existing.OmitFigures = settings.OmitFigures;
        existing.OmitPriceComparison = settings.OmitPriceComparison;

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
