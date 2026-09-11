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

        var materialStandardUnits = await dbContext.PackagingMaterials
            .Where(m => lines.Select(l => l.PackagingMaterialId).Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.StandardUnitId, cancellationToken);

        foreach (var line in lines)
        {
            if (!materialStandardUnits.TryGetValue(line.PackagingMaterialId, out var standardUnitId))
            {
                throw new InvalidOperationException($"Packaging material {line.PackagingMaterialId} does not exist.");
            }

            ProductRules.EnsureValidEntryUnit(standardUnitId, line.EntryUnitId);
        }

        var existingLines = await dbContext.PackagingLines
            .Where(l => l.ProductId == productId)
            .ToListAsync(cancellationToken);
        dbContext.PackagingLines.RemoveRange(existingLines);

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
