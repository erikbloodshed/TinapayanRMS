using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure;

public class PackagingLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingLineRepository
{
    public async Task<IReadOnlyList<PackagingLine>> GetForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.PackagingLines
            .AsNoTracking()
            .Include(l => l.PackagingMaterial)
            .ThenInclude(m => m!.StandardUnit)
            .Include(l => l.EntryUnit)
            .Where(l => l.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PackagingLine>> GetEffectiveForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ResolveEffectiveAsync(dbContext, productId, [], cancellationToken);
    }

    /// <summary>Follows the Packaging tab's link chain (if any) until an unlinked tab is found, then returns that tab's own lines. visitedProductIds guards against a cycle slipping past SetLinkAsync's own check (e.g. a manual data edit).</summary>
    private static async Task<IReadOnlyList<PackagingLine>> ResolveEffectiveAsync(
        TinapayanRMSDbContext dbContext, int productId, HashSet<int> visitedProductIds, CancellationToken cancellationToken)
    {
        if (!visitedProductIds.Add(productId))
        {
            throw new InvalidOperationException("Circular product tab link detected.");
        }

        var link = await dbContext.ProductTabLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.ProductId == productId && l.TabKind == ProductTabKind.Packaging, cancellationToken);

        if (link is not null)
        {
            return await ResolveEffectiveAsync(dbContext, link.LinkedProductId, visitedProductIds, cancellationToken);
        }

        return await dbContext.PackagingLines
            .AsNoTracking()
            .Include(l => l.PackagingMaterial)
            .ThenInclude(m => m!.StandardUnit)
            .Include(l => l.EntryUnit)
            .Where(l => l.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetLinesAsync(
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int EntryUnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        ProductRules.EnsureValidPackagingLines([.. lines.Select(l => (l.PackagingMaterialId, l.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? l.AuthoredValue : 1m / l.AuthoredValue))]);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException($"Product {productId} does not exist.");
        }

        var tabIsLinked = await dbContext.ProductTabLinks
            .AnyAsync(l => l.ProductId == productId && l.TabKind == ProductTabKind.Packaging, cancellationToken);
        if (tabIsLinked)
        {
            throw new InvalidOperationException("The Packaging tab is Live Template-linked — unlink it before editing its lines directly.");
        }

        var materialStandardUnits = await dbContext.PackagingMaterials
            .Include(m => m.StandardUnit)
            .Where(m => lines.Select(l => l.PackagingMaterialId).Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.StandardUnit!, cancellationToken);

        var entryUnits = await dbContext.UnitsOfMeasure
            .Where(u => lines.Select(l => l.EntryUnitId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!materialStandardUnits.TryGetValue(line.PackagingMaterialId, out var standardUnit))
            {
                throw new InvalidOperationException($"Packaging material {line.PackagingMaterialId} does not exist.");
            }

            if (!entryUnits.TryGetValue(line.EntryUnitId, out var entryUnit))
            {
                throw new InvalidOperationException($"Unit of measure {line.EntryUnitId} does not exist.");
            }

            ProductRules.EnsureValidEntryUnit(standardUnit, entryUnit);
        }

        await dbContext.PackagingLines
            .Where(l => l.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.PackagingLines.AddRange(lines.Select(l =>
        {
            var (qtyPerPiece, piecesPerPackagingUnit) = ProductRules.ReconcileReciprocal(l.AuthoredField, l.AuthoredValue);
            return new PackagingLine
            {
                ProductId = productId,
                PackagingMaterialId = l.PackagingMaterialId,
                EntryUnitId = l.EntryUnitId,
                QtyPerPiece = qtyPerPiece,
                PiecesPerPackagingUnit = piecesPerPackagingUnit,
                AuthoredField = l.AuthoredField,
            };
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
