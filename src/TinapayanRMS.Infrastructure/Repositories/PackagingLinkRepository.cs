using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Repositories;

public class PackagingLinkRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IPackagingLineRepository packagingLineRepository) : IPackagingLinkRepository
{
    public async Task<PackagingLink?> GetForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PackagingLinks
            .AsNoTracking()
            .Include(l => l.LinkedProduct)
            .FirstOrDefaultAsync(l => l.ProductId == productId, cancellationToken);
    }

    public async Task SetLinkAsync(int productId, int linkedProductId, CancellationToken cancellationToken)
    {
        PackagingLinkRules.EnsureNotSelfLink(productId, linkedProductId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException($"Product {productId} does not exist.");
        }

        var linkedProductExists = await dbContext.Products.AnyAsync(p => p.Id == linkedProductId, cancellationToken);
        if (!linkedProductExists)
        {
            throw new InvalidOperationException($"Product {linkedProductId} does not exist.");
        }

        var links = await dbContext.PackagingLinks.ToDictionaryAsync(l => l.ProductId, l => l.LinkedProductId, cancellationToken);
        PackagingLinkRules.EnsureNoCycle(productId, linkedProductId, id => links.TryGetValue(id, out var next) ? next : null);

        var existing = await dbContext.PackagingLinks.FirstOrDefaultAsync(l => l.ProductId == productId, cancellationToken);
        if (existing is null)
        {
            dbContext.PackagingLinks.Add(new PackagingLink { ProductId = productId, LinkedProductId = linkedProductId });
        }
        else
        {
            existing.LinkedProductId = linkedProductId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveLinkAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var link = await dbContext.PackagingLinks.FirstOrDefaultAsync(l => l.ProductId == productId, cancellationToken);
        if (link is null)
        {
            return;
        }

        // Freeze what the packaging currently shows (resolved through the link) into the product's own
        // lines, then drop the link — never leaves less data behind.
        var effectiveLines = await packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken);
        dbContext.PackagingLinks.Remove(link);
        await dbContext.SaveChangesAsync(cancellationToken);

        await packagingLineRepository.SetLinesAsync(
            productId,
            [.. effectiveLines.Select(l => (l.PackagingMaterialId, l.UnitId, l.AuthoredField,
                l.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? l.QtyPerPiece : l.PiecesPerPackagingUnit))],
            cancellationToken);
    }
}
