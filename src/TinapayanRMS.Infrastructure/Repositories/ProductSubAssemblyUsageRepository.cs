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
    /// and deleted when it no longer does. Every check runs before anything is deleted: sections in one role told
    /// apart by their names, its own sub-assemblies' names, lines and piece sizes, that listed sub-assemblies are the
    /// product's own and linked ones another product's or standalone, that no product linking one of its own is left
    /// unable to tell its sections apart by a new role or name, and that no sub-assembly being deleted is linked by
    /// another product.
    /// </summary>
    internal static async Task SetRecipeAsync(TinapayanRMSDbContext dbContext, int productId, RecipeDraft recipe, CancellationToken cancellationToken)
    {
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

        var ownedRoleIds = recipe.Owned.Select(o => o.RoleId).Distinct().ToList();
        var ownedRoles = await dbContext.SubAssemblyRoles.AsNoTracking().Where(r => ownedRoleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        if (ownedRoleIds.Count != ownedRoles.Count)
        {
            throw new InvalidOperationException("Every sub-assembly needs a role, and that role must still exist.");
        }

        foreach (var role in ownedRoles.Values)
        {
            SubAssemblyRules.EnsureRecipeRole(role);
        }

        var roleNames = ownedRoles.ToDictionary(p => p.Key, p => p.Value.Name);

        var existingOwned = await dbContext.SubAssemblies
            .Where(s => s.OwnerProductId == productId)
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        foreach (var owned in recipe.Owned.Where(o => o.Id > 0 && !existingOwned.ContainsKey(o.Id)))
        {
            throw new InvalidOperationException($"Sub-assembly {owned.Id} isn't one of this product's own.");
        }

        // With their role and owner, which a linked one is called by when it has no name of its own.
        var linkedIds = recipe.Linked.Select(l => l.SubAssemblyId).ToList();
        var linkedSubAssemblies = await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .Include(s => s.OwnerProduct)
            .Where(s => linkedIds.Contains(s.Id))
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

            // A packaging set is linked by the product's packaging, never as a recipe section.
            SubAssemblyRules.EnsureRecipeRole(linked.Role!);
        }

        SubAssemblyRules.EnsureSectionsDistinct(
        [
            .. recipe.Owned.Select(o => (o.RoleId, roleNames[o.RoleId], SubAssemblyRules.NormalizeName(o.Name))),
            .. recipe.Linked.Select(l => linkedSubAssemblies[l.SubAssemblyId]).Select(s => (s.RoleId, s.Role?.Name ?? string.Empty, SubAssemblyRules.SectionLabel(s, productId))),
        ]);
        SubAssemblyRules.EnsureValidOwnedNames([.. recipe.Owned.Select(o => o.Name)]);

        // One of its own given another role or name takes it into every product linking it too, which may have one
        // going by it there already.
        foreach (var owned in recipe.Owned.Where(o => o.Id > 0
            && (existingOwned[o.Id].RoleId != o.RoleId || existingOwned[o.Id].Name != SubAssemblyRules.NormalizeName(o.Name))))
        {
            await SubAssemblyRepository.EnsureDistinctWhereLinkedAsync(dbContext, owned.Id, owned.RoleId, SubAssemblyRules.NormalizeName(owned.Name), cancellationToken);
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
                    SubAssemblyRules.DisplayName(linkedSubAssemblies[link.SubAssemblyId]),
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

        // Names are unique within the product (its index), and each sub-assembly is saved on its own below: a name
        // moving from one kept sub-assembly to another (Ube and Cheese swapped) would clash halfway. The ones being
        // renamed let theirs go first.
        var renamed = recipe.Owned.Where(o => o.Id > 0 && existingOwned[o.Id].Name != SubAssemblyRules.NormalizeName(o.Name)).ToList();
        if (renamed.Count > 0)
        {
            foreach (var owned in renamed)
            {
                existingOwned[owned.Id].Name = null;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        foreach (var owned in recipe.Owned)
        {
            SubAssembly subAssembly;
            if (owned.Id > 0)
            {
                subAssembly = existingOwned[owned.Id];
                subAssembly.Name = SubAssemblyRules.NormalizeName(owned.Name);
                subAssembly.RoleId = owned.RoleId;
            }
            else
            {
                subAssembly = new SubAssembly { Name = SubAssemblyRules.NormalizeName(owned.Name), RoleId = owned.RoleId, OwnerProductId = productId };
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
        // (An owned sub-assembly is saved one at a time above, for the id its lines are written against.)
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
                dbContext, usage, SubAssemblyRules.DisplayName(linkedSubAssemblies[usage.SubAssemblyId]), draft.AddIns, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
