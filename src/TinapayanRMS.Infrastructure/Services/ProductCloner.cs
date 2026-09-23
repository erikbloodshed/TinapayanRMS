using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;

namespace TinapayanRMS.Infrastructure.Services;

public class ProductCloner(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductCloner
{
    public Task<int> CloneAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken) =>
        CreateFromAsync(sourceProductId, newProductName, asVariant: false, cancellationToken);

    public Task<int> CreateVariantAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken) =>
        CreateFromAsync(sourceProductId, newProductName, asVariant: true, cancellationToken);

    /// <summary>
    /// A clone and a variant differ only in their packaging: a clone copies it, a variant links it. Both link the
    /// sub-assemblies the source's recipe does. The product row — its quantity required among the rest — and its
    /// costing are copied alike.
    /// </summary>
    private async Task<int> CreateFromAsync(int sourceProductId, string newProductName, bool asVariant, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var source = await dbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == sourceProductId, cancellationToken)
            ?? throw new InvalidOperationException($"Product {sourceProductId} does not exist.");

        // What the source shows: its usages, its packaging resolved through any link, and its costing.
        var usages = await dbContext.ProductSubAssemblyUsages
            .AsNoTracking()
            .Where(u => u.ProductId == sourceProductId)
            .OrderBy(u => u.Id)
            .ToListAsync(cancellationToken);

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

        // A packaging set the source links directly stays linked, like its recipe's sub-assemblies: it's kept apart to
        // be shared. Reached through another product's link, it's copied like the rest of that product's packaging.
        var sourceLink = await dbContext.PackagingLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ProductId == sourceProductId, cancellationToken);
        var costing = await dbContext.CostingParameters.AsNoTracking().FirstOrDefaultAsync(c => c.ProductId == sourceProductId, cancellationToken);

        var newProduct = new Product
        {
            Name = newProductName,
            Description = source.Description,
            CategoryId = source.CategoryId,
            QuantityRequired = source.QuantityRequired,
            CurrentPrice = source.CurrentPrice,
        };
        await ProductRepository.AddAsync(dbContext, newProduct, cancellationToken);

        await ProductSubAssemblyUsageRepository.SetRecipeAsync(dbContext, newProduct.Id, LinkedRecipe(usages, addInsByUsage), cancellationToken);

        if (asVariant)
        {
            // The variant's packaging is the source's, followed through a link — a chain, when the source links a set
            // or another product's — whenever the source has any: its own lines or a link.
            if (sourceLink is not null || packagingLines.Count > 0)
            {
                await PackagingLinkRepository.SetAsync(dbContext, newProduct.Id, new PackagingLinkDraft(sourceProductId, null), cancellationToken);
            }
        }
        else if (sourceLink?.LinkedSubAssemblyId is { } setId)
        {
            dbContext.PackagingLinks.Add(new PackagingLink { ProductId = newProduct.Id, LinkedSubAssemblyId = setId });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (packagingLines.Count > 0)
        {
            await PackagingLineRepository.SetLinesAsync(
                dbContext,
                newProduct.Id,
                [.. packagingLines.Select(l => (l.PackagingMaterialId, l.UnitId, l.AuthoredField,
                    l.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? l.QtyPerUnit : l.UnitsPerPackagingUnit))],
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

    /// <summary>
    /// The new product links everything the source's recipe does, at the source's piece sizes and shrinkage, with the
    /// source's add-ins as its own.
    /// </summary>
    private static RecipeDraft LinkedRecipe(List<ProductSubAssemblyUsage> usages, ILookup<int, UsageAddInLine> addInsByUsage) =>
        new(
            [],
            [
                .. usages.Select(usage => new LinkedUsageDraft(usage.SubAssemblyId, usage.AuthoredField, usage.AuthoredValue)
                {
                    ShrinkagePercent = usage.ShrinkagePercent,
                    AddIns = [.. addInsByUsage[usage.Id].Select(l => (l.IngredientId, l.Grams))],
                }),
            ]);
}
