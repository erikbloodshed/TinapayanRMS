using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
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
    /// A clone and a variant differ only in their recipe and packaging: a clone copies them, a variant links
    /// them. The product row — its quantity required among the rest — and its costing are copied alike.
    /// </summary>
    private async Task<int> CreateFromAsync(int sourceProductId, string newProductName, bool asVariant, CancellationToken cancellationToken)
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
            .ThenInclude(s => s!.OwnerProduct)
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

        // A packaging set the source links directly stays linked, like a standalone sub-assembly: it's kept apart to
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

        var recipe = asVariant
            ? VariantRecipe(usages, addInsByUsage)
            : CloneRecipe(sourceProductId, usages, linesBySubAssembly, addInsByUsage);
        await ProductSubAssemblyUsageRepository.SetRecipeAsync(dbContext, newProduct.Id, recipe, cancellationToken);

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
    /// A variant links everything the source's recipe uses, at the source's piece sizes. Links don't chain, so one the
    /// source links itself is linked where it's kept, and the source's add-ins on it become the variant's own; the
    /// source's own sub-assemblies have none. Where a role repeats, one of the source's own without a name goes by the
    /// source's name in the variant (SubAssemblyRules.SectionLabel), so the sections stay apart.
    /// </summary>
    private static RecipeDraft VariantRecipe(List<ProductSubAssemblyUsage> usages, ILookup<int, UsageAddInLine> addInsByUsage) =>
        new(
            [],
            [
                .. usages.Select(usage => new LinkedUsageDraft(usage.SubAssemblyId, usage.AuthoredField, usage.AuthoredValue)
                {
                    ShrinkagePercent = usage.ShrinkagePercent,
                    AddIns = [.. addInsByUsage[usage.Id].Select(l => (l.IngredientId, l.Grams))],
                }),
            ]);

    private static RecipeDraft CloneRecipe(
        int sourceProductId,
        List<ProductSubAssemblyUsage> usages,
        ILookup<int, SubAssemblyLine> linesBySubAssembly,
        ILookup<int, UsageAddInLine> addInsByUsage)
    {
        // A standalone sub-assembly stays linked, at the same piece size: it's kept apart to be shared.
        // Every other one becomes the clone's own, in the role it has, with its name. In a role the source has more
        // than one in, a linked one without a name of its own takes its owner's, which is what told it apart there,
        // so the clone's sections stay apart too (SetRecipeAsync checks it).
        var repeatedRoleIds = usages.GroupBy(u => u.SubAssembly!.RoleId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var owned = new List<OwnedSubAssemblyDraft>();
        var linked = new List<LinkedUsageDraft>();
        foreach (var usage in usages)
        {
            if (usage.SubAssembly!.OwnerProductId is null)
            {
                // Still a link, so its add-ins stay add-ins: the clone puts the same extras in.
                linked.Add(new LinkedUsageDraft(usage.SubAssemblyId, usage.AuthoredField, usage.AuthoredValue)
                {
                    ShrinkagePercent = usage.ShrinkagePercent,
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
                usage.AuthoredValue,
                repeatedRoleIds.Contains(usage.SubAssembly.RoleId)
                    ? SubAssemblyRules.SectionLabel(usage.SubAssembly, sourceProductId)
                    : SubAssemblyRules.NormalizeName(usage.SubAssembly.Name))
            {
                ShrinkagePercent = usage.ShrinkagePercent,
            });
        }

        return new RecipeDraft(owned, linked);
    }
}
