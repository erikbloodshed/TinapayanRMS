using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Services;

public class ProductCloner(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IProductSubAssemblyUsageRepository usageRepository,
    IPackagingLineRepository packagingLineRepository,
    ICostingParametersRepository costingParametersRepository) : IProductCloner
{
    public async Task<int> CloneAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken)
    {
        // Read what the source currently shows (resolved through any tab links it has) —
        // a clone copies the visible recipe, never the link structure behind it.
        var effectiveUsages = await usageRepository.GetEffectiveForProductAsync(sourceProductId, cancellationToken);
        var effectiveLines = await packagingLineRepository.GetEffectiveForProductAsync(sourceProductId, cancellationToken);
        var costingParameters = await costingParametersRepository.GetForProductAsync(sourceProductId, cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sourceExists = await dbContext.Products.AnyAsync(p => p.Id == sourceProductId, cancellationToken);
        if (!sourceExists)
        {
            throw new InvalidOperationException($"Product {sourceProductId} does not exist.");
        }

        var newProduct = new Product { Name = newProductName };
        dbContext.Products.Add(newProduct);
        await dbContext.SaveChangesAsync(cancellationToken);

        // SetUsagesForRoleAsync only ever replaces one role's rows at a time, so the
        // source's usages are grouped by role and written one group per call.
        foreach (var roleGroup in effectiveUsages.GroupBy(u => u.SubAssembly!.RoleId))
        {
            await usageRepository.SetUsagesForRoleAsync(
                newProduct.Id,
                roleGroup.Key,
                [.. roleGroup.Select(u => (u.SubAssemblyId, u.WeightPerPiece))],
                cancellationToken);
        }

        if (effectiveLines.Count > 0)
        {
            await packagingLineRepository.SetLinesAsync(
                newProduct.Id,
                [.. effectiveLines.Select(l => (l.PackagingMaterialId, l.PackagingMaterialUnitId, l.AuthoredField,
                    l.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? l.QtyPerPiece : l.PiecesPerPackagingUnit))],
                cancellationToken);
        }

        if (costingParameters is not null)
        {
            await costingParametersRepository.SetAsync(
                newProduct.Id, costingParameters.LaborCost, costingParameters.OverheadCost,
                costingParameters.MarginPercent, costingParameters.VatPercent, cancellationToken);
        }

        return newProduct.Id;
    }
}
