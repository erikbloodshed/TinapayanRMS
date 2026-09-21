using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class PackagingLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingLineRepository
{
    public Task<IReadOnlyList<PackagingLine>> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(
            dbContext => RawMaterialReads.GetPackagingLinesAsync(dbContext, "WHERE l.ProductId = @productId", new { productId }, cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyList<PackagingLine>> GetEffectiveForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ResolveEffectiveAsync(dbContext, productId, [], cancellationToken);
    }

    /// <summary>
    /// Follows the packaging's link chain (if any) until an unlinked product or a packaging set is found, then
    /// returns that product's own lines, or the set's lines as this product's (<see cref="SetLinesAsPackagingLines"/>).
    /// visitedProductIds guards against a cycle slipping past SetLinkAsync's own check (e.g. a manual data edit).
    /// Reads through Dapper, in <paramref name="dbContext"/>'s transaction if it has one (ProductCloner's does).
    /// </summary>
    internal static async Task<IReadOnlyList<PackagingLine>> ResolveEffectiveAsync(
        TinapayanRMSDbContext dbContext, int productId, HashSet<int> visitedProductIds, CancellationToken cancellationToken)
    {
        if (!visitedProductIds.Add(productId))
        {
            throw new InvalidOperationException("Circular packaging link detected.");
        }

        var link = await dbContext.QuerySingleOrDefaultAsync<PackagingLink>(
            $"SELECT {Columns.Of<PackagingLink>("l")} FROM PackagingLinks l WHERE l.ProductId = @productId", new { productId }, cancellationToken);

        if (link?.LinkedSubAssemblyId is { } setId)
        {
            return await SetLinesAsPackagingLines(dbContext, productId, setId, cancellationToken);
        }

        if (link?.LinkedProductId is { } linkedProductId)
        {
            return await ResolveEffectiveAsync(dbContext, linkedProductId, visitedProductIds, cancellationToken);
        }

        return await RawMaterialReads.GetPackagingLinesAsync(
            dbContext, "WHERE l.ProductId = @productId", new { productId }, cancellationToken);
    }

    /// <summary>
    /// A packaging set's lines as <paramref name="productId"/>'s own packaging lines, never saved: one set per
    /// sellable unit, so a set's quantity is that unit's, in the unit the set line was entered in, typed as the quantity.
    /// Everything that reads a product's packaging (the Products screen, Reports, unlinking's copy, a clone)
    /// takes them as they are.
    /// </summary>
    private static async Task<IReadOnlyList<PackagingLine>> SetLinesAsPackagingLines(
        TinapayanRMSDbContext dbContext, int productId, int setId, CancellationToken cancellationToken)
    {
        var setLines = await RawMaterialReads.GetSetLinesAsync(dbContext, setId, cancellationToken);

        return
        [
            .. setLines.Select(l =>
            {
                var (qtyPerUnit, unitsPerPackagingUnit) = ProductRules.ReconcileReciprocal(PackagingLineAuthoredField.QtyPerUnit, l.QtyPerSet);
                return new PackagingLine
                {
                    ProductId = productId,
                    PackagingMaterialId = l.PackagingMaterialId,
                    PackagingMaterial = l.PackagingMaterial,
                    UnitId = l.UnitId,
                    Unit = l.Unit,
                    QtyPerUnit = qtyPerUnit,
                    UnitsPerPackagingUnit = unitsPerPackagingUnit,
                    AuthoredField = PackagingLineAuthoredField.QtyPerUnit,
                };
            }),
        ];
    }

    public async Task SetLinesAsync(
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await SetLinesAsync(dbContext, productId, lines, cancellationToken);
    }

    /// <summary>
    /// Replaces the product's packaging lines on the caller's context and saves them, after every check
    /// SetLinesAsync makes.
    /// </summary>
    internal static async Task SetLinesAsync(
        TinapayanRMSDbContext dbContext,
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        ProductRules.EnsureValidPackagingLines(
            [.. lines.Select(l => (l.PackagingMaterialId, l.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? l.AuthoredValue : 1m / l.AuthoredValue))]);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException($"Product {productId} does not exist.");
        }

        var isLinked = await dbContext.PackagingLinks.AnyAsync(l => l.ProductId == productId, cancellationToken);
        if (isLinked)
        {
            throw new InvalidOperationException("This product's packaging is linked to another product's — unlink it before editing its lines directly.");
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
            var (qtyPerUnit, unitsPerPackagingUnit) = ProductRules.ReconcileReciprocal(l.AuthoredField, l.AuthoredValue);
            return new PackagingLine
            {
                ProductId = productId,
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
