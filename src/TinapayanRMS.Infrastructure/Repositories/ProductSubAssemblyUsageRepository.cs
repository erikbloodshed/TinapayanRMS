using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Repositories;

public class ProductSubAssemblyUsageRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductSubAssemblyUsageRepository
{
    public async Task<IReadOnlyList<ProductSubAssemblyUsage>> GetForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ProductSubAssemblyUsages
            .AsNoTracking()
            .Include(u => u.SubAssembly)
            .ThenInclude(s => s!.Role)
            .Include(u => u.SubAssembly)
            .ThenInclude(s => s!.OwnerProduct)
            .Where(u => u.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UsageAddInLine>> GetAddInsForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // The Ingredient comes with it: every add-in is priced from its BufferedCostPerBaseUnit on read.
        return await dbContext.UsageAddInLines
            .AsNoTracking()
            .Include(l => l.Ingredient)
            .Where(l => l.Usage!.ProductId == productId)
            .OrderBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the product's recipe on the caller's context, which should hold a transaction: its usages,
    /// and its own sub-assemblies — updated, added and relined as <paramref name="recipe"/> lists them,
    /// and deleted when it no longer does. Every check runs before anything is deleted: names, lines and
    /// piece sizes, that listed sub-assemblies are the product's own and linked ones another product's or
    /// standalone, and that no sub-assembly being deleted is linked by another product.
    /// </summary>
    internal static async Task SetRecipeAsync(TinapayanRMSDbContext dbContext, int productId, RecipeDraft recipe, CancellationToken cancellationToken)
    {
        SubAssemblyRules.EnsureValidOwnedNames([.. recipe.Owned.Select(o => o.Name)]);
        foreach (var owned in recipe.Owned)
        {
            SubAssemblyRules.EnsureValidLines(owned.Lines);
        }

        // A new sub-assembly has no id yet: a negative stand-in keeps it distinct from every other.
        ProductRules.EnsureValidUsages(
        [
            .. recipe.Owned.Select((o, i) => (o.Id > 0 ? o.Id : -(i + 1), o.AuthoredValue)),
            .. recipe.Linked.Select(l => (l.SubAssemblyId, l.AuthoredValue)),
        ]);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException($"Product {productId} does not exist.");
        }

        var roleIds = recipe.Owned.Select(o => o.RoleId).Distinct().ToList();
        var existingRoleIds = await dbContext.SubAssemblyRoles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(cancellationToken);
        if (roleIds.Count != existingRoleIds.Count)
        {
            throw new InvalidOperationException("Every sub-assembly needs a role, and that role must still exist.");
        }

        var existingOwned = await dbContext.SubAssemblies
            .Where(s => s.OwnerProductId == productId)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        foreach (var owned in recipe.Owned.Where(o => o.Id > 0 && !existingOwned.ContainsKey(o.Id)))
        {
            throw new InvalidOperationException($"Sub-assembly {owned.Id} isn't one of this product's own.");
        }

        var linkedIds = recipe.Linked.Select(l => l.SubAssemblyId).ToList();
        var linkedSubAssemblies = await dbContext.SubAssemblies
            .Where(s => linkedIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.OwnerProductId })
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        foreach (var id in linkedIds)
        {
            if (!linkedSubAssemblies.TryGetValue(id, out var linked))
            {
                throw new InvalidOperationException($"Sub-assembly {id} does not exist.");
            }

            if (linked.OwnerProductId == productId)
            {
                throw new InvalidOperationException("A product uses its own sub-assemblies directly; it can't link one.");
            }
        }

        // Add-ins go on top of a linked recipe, so they're only for what it doesn't already have. Checked
        // here, before anything is deleted; SetLinesAsync checks them again as it writes them.
        if (recipe.Linked.Any(l => l.AddIns.Count > 0))
        {
            var baseIngredientIds = (await dbContext.SubAssemblyLines
                    .Where(l => linkedIds.Contains(l.SubAssemblyId))
                    .Select(l => new { l.SubAssemblyId, l.IngredientId })
                    .ToListAsync(cancellationToken))
                .ToLookup(l => l.SubAssemblyId, l => l.IngredientId);

            var addInIds = recipe.Linked.SelectMany(l => l.AddIns.Select(a => a.IngredientId)).Distinct().ToList();
            var addInNames = await dbContext.RawMaterials
                .Where(m => addInIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);

            foreach (var link in recipe.Linked.Where(l => l.AddIns.Count > 0))
            {
                ProductRules.EnsureValidAddIns(
                    [.. link.AddIns.Select(a => (a.IngredientId, a.Grams, addInNames.GetValueOrDefault(a.IngredientId, $"Ingredient {a.IngredientId}")))],
                    linkedSubAssemblies[link.SubAssemblyId].Name,
                    [.. baseIngredientIds[link.SubAssemblyId]]);
            }
        }

        HashSet<int> keptIds = [.. recipe.Owned.Where(o => o.Id > 0).Select(o => o.Id)];
        List<int> removedIds = [.. existingOwned.Keys.Where(id => !keptIds.Contains(id))];
        if (removedIds.Count > 0)
        {
            await SubAssemblyRepository.EnsureNoneLinkedElsewhereAsync(dbContext, u => removedIds.Contains(u.SubAssemblyId), cancellationToken);
        }

        // The usages go first: they reference the sub-assemblies about to be deleted. A deleted
        // sub-assembly takes its lines with it (Cascade).
        await dbContext.ProductSubAssemblyUsages
            .Where(u => u.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);

        if (removedIds.Count > 0)
        {
            await dbContext.SubAssemblies
                .Where(s => removedIds.Contains(s.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Kept ones before new ones, so a new sub-assembly can take a name a kept one just gave up.
        foreach (var owned in recipe.Owned.OrderBy(o => o.Id > 0 ? 0 : 1))
        {
            SubAssembly subAssembly;
            if (owned.Id > 0)
            {
                subAssembly = existingOwned[owned.Id];
                subAssembly.Name = owned.Name.Trim();
                subAssembly.RoleId = owned.RoleId;
            }
            else
            {
                subAssembly = new SubAssembly { Name = owned.Name.Trim(), RoleId = owned.RoleId, OwnerProductId = productId };
                dbContext.SubAssemblies.Add(subAssembly);
            }

            // Saved here so a new one has its id for its lines and usage.
            await dbContext.SaveChangesAsync(cancellationToken);
            await SubAssemblyLineRepository.SetLinesAsync(dbContext, subAssembly, owned.Lines, cancellationToken);

            dbContext.ProductSubAssemblyUsages.Add(new ProductSubAssemblyUsage
            {
                ProductId = productId,
                SubAssemblyId = subAssembly.Id,
                AuthoredField = owned.AuthoredField,
                AuthoredValue = owned.AuthoredValue,
            });
        }

        // The linked usages go in together, then one save gives every one of them the id its add-ins need.
        // (An owned sub-assembly is saved one at a time above, because each one's lines must be written
        // before the next one's name is checked.)
        List<(LinkedUsageDraft Draft, ProductSubAssemblyUsage Usage)> linkedUsages =
        [
            .. recipe.Linked.Select(l => (l, new ProductSubAssemblyUsage
            {
                ProductId = productId,
                SubAssemblyId = l.SubAssemblyId,
                AuthoredField = l.AuthoredField,
                AuthoredValue = l.AuthoredValue,
            })),
        ];

        dbContext.ProductSubAssemblyUsages.AddRange(linkedUsages.Select(p => p.Usage));
        await dbContext.SaveChangesAsync(cancellationToken);

        // This product's own extra ingredients on top of a linked recipe. They belong to the usage, not to
        // the sub-assembly, so two products linking the same one each keep their own.
        foreach (var (draft, usage) in linkedUsages.Where(p => p.Draft.AddIns.Count > 0))
        {
            await UsageAddInLineRepository.SetLinesAsync(
                dbContext, usage, linkedSubAssemblies[usage.SubAssemblyId].Name, draft.AddIns, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
