using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Repositories;

public class PackagingLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingLineRepository
{
    public async Task<IReadOnlyList<PackagingLine>> GetForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await WithMaterialAndUnits(dbContext.PackagingLines)
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

        return await WithMaterialAndUnits(dbContext.PackagingLines)
            .Where(l => l.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// A line's material with its base unit and alternative units (the Products editor's unit picker
    /// and cost need them), and the unit the line itself is entered in.
    /// </summary>
    private static IQueryable<PackagingLine> WithMaterialAndUnits(IQueryable<PackagingLine> lines) =>
        lines
            .AsNoTracking()
            .Include(l => l.PackagingMaterial)
            .ThenInclude(m => m!.BaseUnit)
            .Include(l => l.PackagingMaterial)
            .ThenInclude(m => m!.Units.OrderByDescending(u => u.BaseUnitsPerUnit))
            .ThenInclude(u => u.UnitOfMeasure)
            .Include(l => l.PackagingMaterialUnit)
            .ThenInclude(u => u!.UnitOfMeasure);

    public async Task SetLinesAsync(
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? PackagingMaterialUnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
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

        var existingMaterialIds = await dbContext.PackagingMaterials
            .Where(m => lines.Select(l => l.PackagingMaterialId).Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (lines.Select(l => l.PackagingMaterialId).Except(existingMaterialIds).Cast<int?>().FirstOrDefault() is { } missingMaterialId)
        {
            throw new InvalidOperationException($"Packaging material {missingMaterialId} does not exist.");
        }

        var unitIds = lines.Where(l => l.PackagingMaterialUnitId is not null).Select(l => l.PackagingMaterialUnitId!.Value).Distinct().ToList();
        var materialIdByUnitId = await dbContext.PackagingMaterialUnits
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.PackagingMaterialId, cancellationToken);

        foreach (var line in lines)
        {
            if (line.PackagingMaterialUnitId is { } unitId
                && (!materialIdByUnitId.TryGetValue(unitId, out var unitMaterialId) || unitMaterialId != line.PackagingMaterialId))
            {
                throw new InvalidOperationException("A packaging line's unit must be one of that material's own units.");
            }
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
                PackagingMaterialUnitId = l.PackagingMaterialUnitId,
                QtyPerPiece = qtyPerPiece,
                PiecesPerPackagingUnit = piecesPerPackagingUnit,
                AuthoredField = l.AuthoredField,
            };
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
