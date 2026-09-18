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

    /// <summary>Follows the packaging's link chain (if any) until an unlinked product is found, then returns that product's own lines. visitedProductIds guards against a cycle slipping past SetLinkAsync's own check (e.g. a manual data edit).</summary>
    internal static async Task<IReadOnlyList<PackagingLine>> ResolveEffectiveAsync(
        TinapayanRMSDbContext dbContext, int productId, HashSet<int> visitedProductIds, CancellationToken cancellationToken)
    {
        if (!visitedProductIds.Add(productId))
        {
            throw new InvalidOperationException("Circular packaging link detected.");
        }

        var link = await dbContext.PackagingLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.ProductId == productId, cancellationToken);

        if (link is not null)
        {
            return await ResolveEffectiveAsync(dbContext, link.LinkedProductId, visitedProductIds, cancellationToken);
        }

        // Per-piece lines only. A packaging link says "this product's packaging is that product's", and
        // that holds for what one piece is wrapped in. A pack's lines can't come through it: they point at
        // rows in ProductPacks belonging to the linked product, so the linking product would show lines for
        // packs it doesn't sell and can't edit. A pack is always its own product's, link or no link.
        return await WithMaterialAndUnits(dbContext.PackagingLines)
            .Where(l => l.ProductId == productId && l.PackId == null)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// A line's material with its base unit and alternative units (the Products editor's unit picker
    /// and cost need them), and the unit the line itself is entered in.
    /// </summary>
    internal static IQueryable<PackagingLine> WithMaterialAndUnits(IQueryable<PackagingLine> lines) =>
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
        await SetLinesAsync(dbContext, productId, packId: null, lines, cancellationToken);
    }

    /// <summary>
    /// Replaces one group of the product's packaging lines on the caller's context and saves them, after
    /// every check SetLinesAsync makes. <paramref name="packId"/> null is the per-piece group; a pack's id
    /// is that pack's own group. Only that group is replaced — writing a pack's lines never touches the
    /// per-piece ones, and vice versa.
    /// </summary>
    internal static async Task SetLinesAsync(
        TinapayanRMSDbContext dbContext,
        int productId,
        int? packId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        ProductRules.EnsureValidPackagingLines(
            [.. lines.Select(l => (packId, l.PackagingMaterialId, l.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? l.AuthoredValue : 1m / l.AuthoredValue))]);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException($"Product {productId} does not exist.");
        }

        if (packId is null)
        {
            // The link covers the per-piece packaging, which is all it resolves (ResolveEffectiveAsync).
            // A linked product still owns its packs, so a pack's lines stay editable while it's linked.
            var isLinked = await dbContext.PackagingLinks.AnyAsync(l => l.ProductId == productId, cancellationToken);
            if (isLinked)
            {
                throw new InvalidOperationException("This product's packaging is linked to another product's — unlink it before editing its lines directly.");
            }
        }
        else
        {
            var packBelongs = await dbContext.ProductPacks.AnyAsync(p => p.Id == packId && p.ProductId == productId, cancellationToken);
            if (!packBelongs)
            {
                throw new InvalidOperationException($"Pack {packId} isn't one of this product's.");
            }
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

        // This group only. A nullable parameter is part of EF's query-cache key, so `l.PackId == packId`
        // generates `PackId IS NULL` for the per-piece group rather than a comparison that matches nothing.
        await dbContext.PackagingLines
            .Where(l => l.ProductId == productId && l.PackId == packId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.PackagingLines.AddRange(lines.Select(l =>
        {
            var (qtyPerUnit, unitsPerPackagingUnit) = ProductRules.ReconcileReciprocal(l.AuthoredField, l.AuthoredValue);
            return new PackagingLine
            {
                ProductId = productId,
                PackId = packId,
                PackagingMaterialId = l.PackagingMaterialId,
                UnitId = l.UnitId,
                QtyPerUnit = qtyPerUnit,
                UnitsPerPackagingUnit = unitsPerPackagingUnit,
                AuthoredField = l.AuthoredField,
            };
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
