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
            .Include(l => l.Unit)
            .ThenInclude(u => u!.UnitOfMeasure);

    public async Task SetLinesAsync(
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await SetLinesAsync(dbContext, productId, lines, cancellationToken);
    }

    /// <summary>Replaces the product's packaging lines on the caller's context and saves them, after every check SetLinesAsync makes.</summary>
    internal static async Task SetLinesAsync(
        TinapayanRMSDbContext dbContext,
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        ProductRules.EnsureValidPackagingLines([.. lines.Select(l => (l.PackagingMaterialId, l.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? l.AuthoredValue : 1m / l.AuthoredValue))]);

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

        var materialIds = lines.Select(l => l.PackagingMaterialId).ToList();
        var materials = await dbContext.RawMaterials
            .Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!materials.TryGetValue(line.PackagingMaterialId, out var material))
            {
                throw new InvalidOperationException($"Packaging material {line.PackagingMaterialId} does not exist.");
            }

            RawMaterialRules.EnsureUsableAsPackaging(material.Type, material.Name);
        }

        var unitIds = lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value).Distinct().ToList();
        var materialIdByUnitId = await dbContext.RawMaterialUnits
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.RawMaterialId, cancellationToken);

        foreach (var line in lines)
        {
            if (line.UnitId is { } unitId
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
                UnitId = l.UnitId,
                QtyPerPiece = qtyPerPiece,
                PiecesPerPackagingUnit = piecesPerPackagingUnit,
                AuthoredField = l.AuthoredField,
            };
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
