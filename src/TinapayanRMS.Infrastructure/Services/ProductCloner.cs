using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;

namespace TinapayanRMS.Infrastructure.Services;

public class ProductCloner(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductCloner
{
    public async Task<int> CloneAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var source = await dbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == sourceProductId, cancellationToken)
            ?? throw new InvalidOperationException($"Product {sourceProductId} does not exist.");

        // What the source shows: its usages (its own sub-assemblies and linked ones alike), their lines,
        // its packaging resolved through any link, and its costing.
        var usages = await dbContext.ProductSubAssemblyUsages
            .AsNoTracking()
            .Include(u => u.SubAssembly)
            .Where(u => u.ProductId == sourceProductId)
            .OrderBy(u => u.Id)
            .ToListAsync(cancellationToken);
        var subAssemblyIds = usages.Select(u => u.SubAssemblyId).ToList();
        var linesBySubAssembly = (await dbContext.SubAssemblyLines
                .AsNoTracking()
                .Where(l => subAssemblyIds.Contains(l.SubAssemblyId))
                .OrderBy(l => l.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.SubAssemblyId);
        var packagingLines = await PackagingLineRepository.ResolveEffectiveAsync(dbContext, sourceProductId, [], cancellationToken);
        var costing = await dbContext.CostingParameters.AsNoTracking().FirstOrDefaultAsync(c => c.ProductId == sourceProductId, cancellationToken);

        var newProduct = new Product { Name = newProductName, Description = source.Description };
        await ProductRepository.AddAsync(dbContext, newProduct, cancellationToken);

        // Every sub-assembly becomes the clone's own. An owned one and a linked one can share a name
        // (they have different owners), and the clone's names must differ.
        var names = new List<string>();
        var owned = new List<OwnedSubAssemblyDraft>();
        foreach (var usage in usages)
        {
            var name = SubAssemblyRules.UniqueName(usage.SubAssembly!.Name, names);
            names.Add(name);
            owned.Add(new OwnedSubAssemblyDraft(
                0,
                name,
                usage.SubAssembly.RoleId,
                [.. linesBySubAssembly[usage.SubAssemblyId].Select(l => (l.IngredientId, l.Grams))],
                usage.AuthoredField,
                usage.AuthoredValue));
        }

        await ProductSubAssemblyUsageRepository.SetRecipeAsync(dbContext, newProduct.Id, new RecipeDraft(owned, []), cancellationToken);

        if (packagingLines.Count > 0)
        {
            await PackagingLineRepository.SetLinesAsync(
                dbContext,
                newProduct.Id,
                [.. packagingLines.Select(l => (l.PackagingMaterialId, l.UnitId, l.AuthoredField,
                    l.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? l.QtyPerPiece : l.PiecesPerPackagingUnit))],
                cancellationToken);
        }

        if (costing is not null)
        {
            await CostingParametersRepository.SetAsync(
                dbContext,
                new CostingParameters
                {
                    ProductId = newProduct.Id,
                    LaborPercent = costing.LaborPercent,
                    OverheadPercent = costing.OverheadPercent,
                    AdministrativePercent = costing.AdministrativePercent,
                    MarginPercent = costing.MarginPercent,
                    VatPercent = costing.VatPercent,
                },
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return newProduct.Id;
    }
}
