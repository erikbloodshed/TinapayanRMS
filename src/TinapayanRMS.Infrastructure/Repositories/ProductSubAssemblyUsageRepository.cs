using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class ProductSubAssemblyUsageRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductSubAssemblyUsageRepository
{
    public Task<IReadOnlyList<ProductSubAssemblyUsage>> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<ProductSubAssemblyUsage>>(
            async dbContext => (await dbContext.Connection().QueryAsync<ProductSubAssemblyUsage, SubAssembly, SubAssemblyRole, ProductSubAssemblyUsage>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<ProductSubAssemblyUsage>("u")}, {SubAssemblyReads.ColumnsWithRole}
                    FROM ProductSubAssemblyUsages u
                    JOIN SubAssemblies s ON s.Id = u.SubAssemblyId
                    {SubAssemblyReads.RoleJoin}
                    WHERE u.ProductId = @productId
                    ORDER BY u.Id
                    """,
                    new { productId },
                    cancellationToken),
                (usage, subAssembly, role) =>
                {
                    usage.SubAssembly = SubAssemblyReads.WithRole(subAssembly, role);
                    return usage;
                },
                splitOn: "Id,Id")).AsList(),
            cancellationToken);

    // The Ingredient comes with it: every add-in is priced from its BufferedCostPerBaseUnit on read.
    public Task<IReadOnlyList<UsageAddInLine>> GetAddInsForProductAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<UsageAddInLine>>(
            async dbContext => (await dbContext.Connection().QueryAsync<UsageAddInLine, RawMaterial, UsageAddInLine>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<UsageAddInLine>("l")}, {Columns.Of<RawMaterial>("i")}
                    FROM UsageAddInLines l
                    JOIN ProductSubAssemblyUsages u ON u.Id = l.UsageId
                    JOIN RawMaterials i ON i.Id = l.IngredientId
                    WHERE u.ProductId = @productId
                    ORDER BY l.SortOrder, l.Id
                    """,
                    new { productId },
                    cancellationToken),
                (line, ingredient) =>
                {
                    line.Ingredient = ingredient;
                    return line;
                },
                splitOn: "Id")).AsList(),
            cancellationToken);

    /// <summary>
    /// Replaces the product's recipe on the caller's context, which should hold a transaction: its usages, one for each
    /// sub-assembly <paramref name="recipe"/> links and one for each section whose lines it edits (RecipeDraft.New).
    /// Such a section in a product-owned role (SubAssemblyRole.IsProductOwned), or marked NewSubAssemblyDraft.IsOwn (an
    /// unlinked one), is the product's own sub-assembly, added or rewritten in place; in any other role a new one is kept
    /// on the Sub-Assemblies screen and linked, and one of its own moved there out of a product-owned role is released to
    /// it. One marked NewSubAssemblyDraft.Release goes to the Sub-Assemblies screen and is linked, whatever its role.
    /// Either is named after the product and its role (SubAssemblyRules.StandaloneNameFor, made unique). One of its own the recipe no longer lists is deleted; one on the
    /// Sub-Assemblies screen is kept. A linked one carrying new lines (LinkedUsageDraft.Lines) is relined where it's kept,
    /// for every product linking it. Every check runs before anything is written: the sections' lines, names and piece
    /// sizes, that a saved one is the product's own, that no section is a packaging set, that nothing linked is another
    /// product's own, sections in one role told apart by their names, and add-ins.
    /// </summary>
    internal static async Task SetRecipeAsync(TinapayanRMSDbContext dbContext, int productId, RecipeDraft recipe, CancellationToken cancellationToken)
    {
        foreach (var section in recipe.New)
        {
            SubAssemblyRules.EnsureValidLines(section.Lines);
            SubAssemblyRules.EnsureValidSectionName(section.SectionName);
        }

        foreach (var link in recipe.Linked)
        {
            if (link.Lines is { } lines)
            {
                SubAssemblyRules.EnsureValidLines(lines);
            }
        }

        // A new sub-assembly has no id yet: a negative stand-in keeps it distinct from every other.
        ProductRules.EnsureValidUsages(
        [
            .. recipe.New.Select((n, i) => (n.Id > 0 ? n.Id : -(i + 1), n.AuthoredValue, n.ShrinkagePercent)),
            .. recipe.Linked.Select(l => (l.SubAssemblyId, l.AuthoredValue, l.ShrinkagePercent)),
        ]);

        // Read in the caller's transaction, so a product added or renamed by the same save is named as it now is.
        var productName = await dbContext.Products
            .Where(p => p.Id == productId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Product {productId} does not exist.");

        var roleIds = recipe.New.Select(n => n.RoleId).Distinct().ToList();
        var roles = await dbContext.SubAssemblyRoles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        if (roleIds.Count != roles.Count)
        {
            throw new InvalidOperationException("Every sub-assembly needs a role, and that role must still exist.");
        }

        foreach (var role in roles.Values)
        {
            SubAssemblyRules.EnsureRecipeRole(role);
        }

        // Tracked: the ones kept are rewritten in place.
        var existingOwned = await dbContext.SubAssemblies
            .Include(s => s.Role)
            .Where(s => s.OwnerProductId == productId)
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        foreach (var section in recipe.New.Where(n => n.Id > 0 && !existingOwned.ContainsKey(n.Id)))
        {
            throw new InvalidOperationException(
                $"Sub-assembly {section.Id} isn't one of this product's own any more. Revert the product to see its recipe as it's saved now.");
        }

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

            // A packaging set is linked by the product's packaging, never as a recipe section; a product's own is copied.
            SubAssemblyRules.EnsureRecipeRole(linked.Role!);
            SubAssemblyRules.EnsureLinkable(linked.Name, linked.OwnerProduct?.Name);
        }

        SubAssemblyRules.EnsureSectionsDistinct(
        [
            .. recipe.New.Select(n => (n.RoleId, roles[n.RoleId].Name, SubAssemblyRules.NormalizeName(n.SectionName))),
            .. recipe.Linked.Select(l => linkedSubAssemblies[l.SubAssemblyId])
                .Select(s => (s.RoleId, s.Role!.Name, SubAssemblyRules.SectionLabel(s.Name, productName, s.Role.Name))),
        ]);

        // Add-ins go on top of a linked recipe, so they're only for what it doesn't already have: its lines as this save
        // leaves them, the new ones where they're changed here. Checked here, before anything is written; SetLinesAsync
        // checks them again as it writes them, after the new lines.
        if (recipe.Linked.Any(l => l.AddIns.Count > 0))
        {
            var storedIngredientIds = (await dbContext.SubAssemblyLines
                    .Where(l => linkedIds.Contains(l.SubAssemblyId))
                    .Select(l => new { l.SubAssemblyId, l.IngredientId })
                    .ToListAsync(cancellationToken))
                .ToLookup(l => l.SubAssemblyId, l => l.IngredientId);
            var baseIngredientIds = recipe.Linked.ToDictionary(
                l => l.SubAssemblyId,
                l => l.Lines?.Select(line => line.IngredientId).ToList() ?? [.. storedIngredientIds[l.SubAssemblyId]]);

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

        // The usages go first: they reference the product's own sub-assemblies about to be deleted. One on the
        // Sub-Assemblies screen the recipe no longer lists stays there; one of its own goes, with its lines (Cascade).
        await dbContext.ProductSubAssemblyUsages
            .Where(u => u.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);

        HashSet<int> keptIds = [.. recipe.New.Where(n => n.Id > 0).Select(n => n.Id)];
        List<int> removedIds = [.. existingOwned.Keys.Where(id => !keptIds.Contains(id))];
        if (removedIds.Count > 0)
        {
            foreach (var id in removedIds)
            {
                dbContext.Entry(existingOwned[id]).State = EntityState.Detached;
            }

            await dbContext.SubAssemblies.Where(s => removedIds.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        // Its own ones kept take their role, and go to the Sub-Assemblies screen when they move out of a product-owned
        // role into one that isn't. One unlinked into a role that isn't product-owned was never the role's, so it stays
        // the product's own wherever it moves. One given another role or name is named after it again.
        List<(SubAssembly SubAssembly, string Name)> renames = [];
        foreach (var section in recipe.New.Where(n => n.Id > 0))
        {
            var stored = existingOwned[section.Id];
            var role = roles[section.RoleId];
            var label = SubAssemblyRules.SectionLabel(stored.Name, productName, stored.Role!.Name);
            if (stored.RoleId != section.RoleId
                || !string.Equals(label, SubAssemblyRules.NormalizeName(section.SectionName), StringComparison.Ordinal))
            {
                renames.Add((stored, SubAssemblyRules.StandaloneNameFor(productName, role.Name, section.SectionName)));
            }

            // Read before RoleId changes: the tracked Role isn't moved over to the new one until SaveChanges. One moved to
            // the Sub-Assemblies screen (Release) goes there whatever its role, keeping its name, and is linked below.
            var wasRoleProductOwned = stored.Role.IsProductOwned;
            stored.RoleId = section.RoleId;
            stored.OwnerProductId = !section.Release && (role.IsProductOwned || !wasRoleProductOwned) ? productId : null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SubAssemblyRepository.RenameAsync(dbContext, renames, cancellationToken);

        // New ones: the product's own in a product-owned role or when unlinked (IsOwn), otherwise, or when moved there
        // (Release), kept on the Sub-Assemblies screen. Saved one at a time for the id its lines are written against, and named apart from every
        // other, those saved just before it included. The ones kept are relined, and every one is used at the product's
        // piece size.
        List<string> takenNames = recipe.New.Any(n => n.Id == 0) ? await dbContext.SubAssemblies.Select(s => s.Name).ToListAsync(cancellationToken) : [];
        foreach (var section in recipe.New)
        {
            SubAssembly subAssembly;
            if (section.Id > 0)
            {
                subAssembly = existingOwned[section.Id];
            }
            else
            {
                var role = roles[section.RoleId];
                var name = SubAssemblyRules.UniqueStandaloneName(SubAssemblyRules.StandaloneNameFor(productName, role.Name, section.SectionName), takenNames);
                takenNames.Add(name);
                subAssembly = new SubAssembly { Name = name, RoleId = section.RoleId, OwnerProductId = !section.Release && (section.IsOwn || role.IsProductOwned) ? productId : null };
                dbContext.SubAssemblies.Add(subAssembly);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await SubAssemblyLineRepository.SetLinesAsync(dbContext, subAssembly, section.Lines, cancellationToken);

            dbContext.ProductSubAssemblyUsages.Add(new ProductSubAssemblyUsage
            {
                ProductId = productId,
                SubAssemblyId = subAssembly.Id,
                AuthoredField = section.AuthoredField,
                AuthoredValue = section.AuthoredValue,
                ShrinkagePercent = section.ShrinkagePercent,
            });
        }

        // A linked one whose lines were changed in this recipe is relined where it's kept, before its add-ins are checked
        // against them; every product linking it follows, their costs read from its new totals.
        foreach (var link in recipe.Linked)
        {
            if (link.Lines is { } lines)
            {
                var linked = await dbContext.SubAssemblies.FirstAsync(s => s.Id == link.SubAssemblyId, cancellationToken);
                await SubAssemblyLineRepository.SetLinesAsync(dbContext, linked, lines, cancellationToken);
            }
        }

        // The linked usages go in together, then one save gives every one of them the id its add-ins need.
        List<(LinkedUsageDraft Draft, ProductSubAssemblyUsage Usage)> linkedUsages =
        [
            .. recipe.Linked.Select(l => (l, new ProductSubAssemblyUsage
            {
                ProductId = productId,
                SubAssemblyId = l.SubAssemblyId,
                AuthoredField = l.AuthoredField,
                AuthoredValue = l.AuthoredValue,
                ShrinkagePercent = l.ShrinkagePercent,
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
