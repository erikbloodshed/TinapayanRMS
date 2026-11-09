using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;

namespace TinapayanRMS.Infrastructure.Services;

public sealed class ProductCloner(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductCloner
{
    public Task<int> CloneAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken) =>
        CreateFromAsync(sourceProductId, newProductName, asVariant: false, cancellationToken);

    public Task<int> CreateVariantAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken) =>
        CreateFromAsync(sourceProductId, newProductName, asVariant: true, cancellationToken);

    /// <summary>
    /// A clone links the sub-assemblies the source's recipe links, has its own copy of each of the source's own, and
    /// copies its packaging. A variant links every sub-assembly the source uses, its own ones included, and has no
    /// packaging. A composite's copy holds the same products, at the same quantities, either way. The
    /// product row — its quantity required and whether it's a composite among the rest — and its costing are copied alike.
    /// </summary>
    private async Task<int> CreateFromAsync(int sourceProductId, string newProductName, bool asVariant, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);

        var source = await dbContext.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == sourceProductId, cancellationToken)
            ?? throw new BusinessRuleException($"Product {sourceProductId} does not exist.");

        // What the source shows: its usages (with its own sub-assemblies' lines), its packaging resolved through any
        // link, and its costing.
        var usages = await dbContext.ProductSubAssemblyUsages
            .AsNoTracking()
            .Include(u => u.SubAssembly)
            .ThenInclude(s => s!.Role)
            .Where(u => u.ProductId == sourceProductId)
            .OrderBy(u => u.Id)
            .ToListAsync(cancellationToken);
        var ownIds = usages.Where(u => u.SubAssembly!.OwnerProductId == sourceProductId).Select(u => u.SubAssemblyId).ToList();
        var ownLines = (await dbContext.SubAssemblyLines
                .AsNoTracking()
                .Where(l => ownIds.Contains(l.SubAssemblyId))
                .OrderBy(l => l.SortOrder)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.SubAssemblyId);

        // Keyed on the usage, not the sub-assembly: an add-in is the source product's own, and two
        // products linking the same sub-assembly each have their own set.
        var usageIds = usages.Select(u => u.Id).ToList();
        var addInsByUsage = (await dbContext.UsageAddInLines
                .AsNoTracking()
                .Where(l => usageIds.Contains(l.UsageId))
                .OrderBy(l => l.SortOrder)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.UsageId);
        var packagingLines = await PackagingLineRepository.ResolveEffectiveAsync(dbContext, sourceProductId, cancellationToken);

        // A packaging set the source links directly stays linked, like its recipe's sub-assemblies: it's kept apart to
        // be shared. Reached through another product's link, it's copied like the rest of that product's packaging.
        var sourceLink = await dbContext.PackagingLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ProductId == sourceProductId, cancellationToken);
        var costing = await dbContext.CostingParameters.AsNoTracking().FirstOrDefaultAsync(c => c.ProductId == sourceProductId, cancellationToken);

        // Not the source's stage: a clone is a new product, so it starts in Innovation.
        var newProduct = new Product
        {
            Name = newProductName,
            Description = source.Description,
            CategoryId = source.CategoryId,
            QuantityRequired = source.QuantityRequired,
            CurrentPrice = source.CurrentPrice,
            IsComposite = source.IsComposite,
            IncludesComponentPackaging = source.IncludesComponentPackaging,
        };
        await ProductRepository.AddAsync(dbContext, newProduct, cancellationToken);

        // A composite's products are products in their own right whichever way it's copied: the new one holds the same
        // ones. Its own sections (a topping, a filling) are copied or linked as any product's are.
        if (source.IsComposite)
        {
            var components = await ProductComponentRepository.GetForProductAsync(dbContext, sourceProductId, cancellationToken);
            await ProductComponentRepository.SetComponentsAsync(
                dbContext, newProduct, [.. components.Select(c => (c.ComponentProductId, c.Quantity))], cancellationToken);
        }

        await ProductSubAssemblyUsageRepository.SetRecipeAsync(
            dbContext,
            newProduct.Id,
            asVariant ? VariantRecipe(usages, addInsByUsage) : DerivedRecipe(usages, ownLines, addInsByUsage),
            linksOthersOwn: asVariant,
            cancellationToken);

        // A variant has no packaging: it's given its own afterward.
        if (!asVariant && sourceLink?.LinkedSubAssemblyId is { } setId)
        {
            dbContext.PackagingLinks.Add(new PackagingLink { ProductId = newProduct.Id, LinkedSubAssemblyId = setId });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (!asVariant && packagingLines.Count > 0)
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
                },
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return newProduct.Id;
    }

    /// <summary>
    /// The new product's recipe, at the source's piece sizes: its own copy of each of the source's own
    /// sub-assemblies (lines, role, and its section name there, named after the new product by SetRecipeAsync), and a
    /// link to every one the source links, with the source's add-ins on it as the new product's own.
    /// </summary>
    private static RecipeDraft DerivedRecipe(
        List<ProductSubAssemblyUsage> usages,
        ILookup<int, SubAssemblyLine> ownLines,
        ILookup<int, UsageAddInLine> addInsByUsage)
    {
        List<NewSubAssemblyDraft> own = [];
        List<LinkedUsageDraft> linked = [];
        foreach (var usage in usages)
        {
            var subAssembly = usage.SubAssembly!;
            if (subAssembly.OwnerProductId == usage.ProductId)
            {
                // A product's own has no add-ins, but should one have them they go in with its lines, as Make a Copy
                // folds them in: never a duplicate ingredient (ProductRules.EnsureValidAddIns).
                own.Add(new NewSubAssemblyDraft(
                    subAssembly.RoleId,
                    [
                        .. ownLines[subAssembly.Id].Select(l => new RecipeLine(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)),
                        .. addInsByUsage[usage.Id].Select(l => new RecipeLine(l.IngredientId, l.Grams)),
                    ],
                    usage.AuthoredField,
                    usage.AuthoredValue,
                    subAssembly.SectionName)
                {
                    // Its own whatever its role: the source's may have been unlinked in a role that isn't product-owned.
                    Placement = new SectionPlacement.ProductsOwn(),
                });
                continue;
            }

            linked.Add(new LinkedUsageDraft(usage.SubAssemblyId, usage.AuthoredField, usage.AuthoredValue)
            {
                AddIns = [.. addInsByUsage[usage.Id].Select(l => (l.IngredientId, l.Grams))],
            });
        }

        return new RecipeDraft(own, linked);
    }

    /// <summary>
    /// A variant's recipe, at the source's piece sizes: a link to every sub-assembly the source uses, its own ones and
    /// the ones it links from the Sub-Assemblies screen alike, with the source's add-ins on each as the variant's own.
    /// </summary>
    private static RecipeDraft VariantRecipe(List<ProductSubAssemblyUsage> usages, ILookup<int, UsageAddInLine> addInsByUsage) =>
        new(
            [],
            [
                .. usages
                    .Select(u => new LinkedUsageDraft(u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)
                    {
                        AddIns = [.. addInsByUsage[u.Id].Select(l => (l.IngredientId, l.Grams))],
                    }),
            ]);
}
