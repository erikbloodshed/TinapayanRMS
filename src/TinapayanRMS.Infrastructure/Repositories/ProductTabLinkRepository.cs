using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Repositories;

public class ProductTabLinkRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IProductSubAssemblyUsageRepository usageRepository,
    IPackagingLineRepository packagingLineRepository) : IProductTabLinkRepository
{
    public async Task<IReadOnlyList<ProductTabLink>> GetForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ProductTabLinks
            .AsNoTracking()
            .Include(l => l.SubAssemblyRole)
            .Include(l => l.LinkedProduct)
            .Where(l => l.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetLinkAsync(int productId, ProductTabKind tabKind, int? subAssemblyRoleId, int linkedProductId, CancellationToken cancellationToken)
    {
        ProductTabLinkRules.EnsureNotSelfLink(productId, linkedProductId);

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

        // Only links for this exact tab identity (same TabKind + role) can ever form a
        // cycle with this one, so that's all that needs to be loaded for the check.
        var linksForThisTab = await dbContext.ProductTabLinks
            .Where(l => l.TabKind == tabKind && l.SubAssemblyRoleId == subAssemblyRoleId)
            .ToDictionaryAsync(l => l.ProductId, l => l.LinkedProductId, cancellationToken);

        ProductTabLinkRules.EnsureNoCycle(productId, linkedProductId, id => linksForThisTab.TryGetValue(id, out var next) ? next : null);

        var existing = await dbContext.ProductTabLinks
            .FirstOrDefaultAsync(l => l.ProductId == productId && l.TabKind == tabKind && l.SubAssemblyRoleId == subAssemblyRoleId, cancellationToken);

        if (existing is null)
        {
            dbContext.ProductTabLinks.Add(new ProductTabLink
            {
                ProductId = productId,
                TabKind = tabKind,
                SubAssemblyRoleId = subAssemblyRoleId,
                LinkedProductId = linkedProductId,
            });
        }
        else
        {
            existing.LinkedProductId = linkedProductId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveLinkAsync(int productId, ProductTabKind tabKind, int? subAssemblyRoleId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var link = await dbContext.ProductTabLinks
            .FirstOrDefaultAsync(l => l.ProductId == productId && l.TabKind == tabKind && l.SubAssemblyRoleId == subAssemblyRoleId, cancellationToken);

        if (link is null)
        {
            return;
        }

        // Freeze what this tab currently shows (resolved through the link) into the
        // product's own rows, then drop the link — never leaves less data behind.
        if (tabKind == ProductTabKind.SubAssemblyRole)
        {
            var effectiveUsages = await usageRepository.GetEffectiveForRoleAsync(productId, subAssemblyRoleId!.Value, cancellationToken);
            dbContext.ProductTabLinks.Remove(link);
            await dbContext.SaveChangesAsync(cancellationToken);

            await usageRepository.SetUsagesForRoleAsync(
                productId, subAssemblyRoleId.Value,
                [.. effectiveUsages.Select(u => (u.SubAssemblyId, u.WeightPerPiece))],
                cancellationToken);
        }
        else
        {
            var effectiveLines = await packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken);
            dbContext.ProductTabLinks.Remove(link);
            await dbContext.SaveChangesAsync(cancellationToken);

            await packagingLineRepository.SetLinesAsync(
                productId,
                [.. effectiveLines.Select(l => (l.PackagingMaterialId, l.PackagingMaterialUnitId, l.AuthoredField,
                    l.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? l.QtyPerPiece : l.PiecesPerPackagingUnit))],
                cancellationToken);
        }
    }
}
