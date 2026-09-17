using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
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

        // Keyed on the usage, not the sub-assembly: an add-in is the source product's own, and two
        // products linking the same sub-assembly each have their own set.
        var usageIds = usages.Select(u => u.Id).ToList();
        var addInsByUsage = (await dbContext.UsageAddInLines
                .AsNoTracking()
                .Where(l => usageIds.Contains(l.UsageId))
                .OrderBy(l => l.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.UsageId);
        var packagingLines = await PackagingLineRepository.ResolveEffectiveAsync(dbContext, sourceProductId, [], cancellationToken);
        var costing = await dbContext.CostingParameters.AsNoTracking().FirstOrDefaultAsync(c => c.ProductId == sourceProductId, cancellationToken);

        var newProduct = new Product { Name = newProductName, Description = source.Description };
        await ProductRepository.AddAsync(dbContext, newProduct, cancellationToken);

        // A standalone sub-assembly stays linked, at the same piece size: it's kept apart to be shared.
        // Every other one becomes the clone's own, in the role it has: the source has one in each role, so
        // the clone does too (SetRecipeAsync checks it).
        var owned = new List<OwnedSubAssemblyDraft>();
        var linked = new List<LinkedUsageDraft>();
        foreach (var usage in usages)
        {
            if (usage.SubAssembly!.OwnerProductId is null)
            {
                // Still a link, so its add-ins stay add-ins: the clone puts the same extras in.
                linked.Add(new LinkedUsageDraft(usage.SubAssemblyId, usage.AuthoredField, usage.AuthoredValue)
                {
                    AddIns = [.. addInsByUsage[usage.Id].Select(l => (l.IngredientId, l.Grams))],
                });
                continue;
            }

            // The link is broken here, so the source's add-ins become plain lines of the clone's own copy:
            // the batch it had is the batch it keeps. Never a duplicate ingredient, since an add-in can't
            // be one the recipe already has (ProductRules.EnsureValidAddIns).
            owned.Add(new OwnedSubAssemblyDraft(
                0,
                usage.SubAssembly.RoleId,
                [
                    // The formula's base comes with its lines; the add-ins folded in beside them never are one.
                    .. linesBySubAssembly[usage.SubAssemblyId].Select(l => new RecipeLine(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)),
                    .. addInsByUsage[usage.Id].Select(l => new RecipeLine(l.IngredientId, l.Grams)),
                ],
                usage.AuthoredField,
                usage.AuthoredValue));
        }

        await ProductSubAssemblyUsageRepository.SetRecipeAsync(dbContext, newProduct.Id, new RecipeDraft(owned, linked), cancellationToken);

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
