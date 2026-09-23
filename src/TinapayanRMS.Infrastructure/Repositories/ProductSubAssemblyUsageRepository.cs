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
                    ORDER BY l.Id
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
    /// sub-assembly <paramref name="recipe"/> links, and one for each new section, which is first saved as a sub-assembly
    /// of its own, named after the product and its role (SubAssemblyRules.StandaloneNameFor, made unique). A sub-assembly
    /// the recipe no longer lists is kept. Every check runs before anything is written: the new sections' lines, names and
    /// piece sizes, that no section is a packaging set, sections in one role told apart by their names, and add-ins.
    /// </summary>
    internal static async Task SetRecipeAsync(TinapayanRMSDbContext dbContext, int productId, RecipeDraft recipe, CancellationToken cancellationToken)
    {
        foreach (var section in recipe.New)
        {
            SubAssemblyRules.EnsureValidLines(section.Lines);
            SubAssemblyRules.EnsureValidSectionName(section.SectionName);
        }

        // A new sub-assembly has no id yet: a negative stand-in keeps it distinct from every other.
        ProductRules.EnsureValidUsages(
        [
            .. recipe.New.Select((n, i) => (-(i + 1), n.AuthoredValue, n.ShrinkagePercent)),
            .. recipe.Linked.Select(l => (l.SubAssemblyId, l.AuthoredValue, l.ShrinkagePercent)),
        ]);

        // Read in the caller's transaction, so a product added or renamed by the same save is named as it now is.
        var productName = await dbContext.Products
            .Where(p => p.Id == productId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Product {productId} does not exist.");

        var newRoleIds = recipe.New.Select(n => n.RoleId).Distinct().ToList();
        var newRoles = await dbContext.SubAssemblyRoles.AsNoTracking().Where(r => newRoleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        if (newRoleIds.Count != newRoles.Count)
        {
            throw new InvalidOperationException("Every sub-assembly needs a role, and that role must still exist.");
        }

        foreach (var role in newRoles.Values)
        {
            SubAssemblyRules.EnsureRecipeRole(role);
        }

        var linkedIds = recipe.Linked.Select(l => l.SubAssemblyId).ToList();
        var linkedSubAssemblies = await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .Where(s => linkedIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        foreach (var id in linkedIds)
        {
            if (!linkedSubAssemblies.TryGetValue(id, out var linked))
            {
                throw new InvalidOperationException($"Sub-assembly {id} does not exist.");
            }

            // A packaging set is linked by the product's packaging, never as a recipe section.
            SubAssemblyRules.EnsureRecipeRole(linked.Role!);
        }

        SubAssemblyRules.EnsureSectionsDistinct(
        [
            .. recipe.New.Select(n => (n.RoleId, newRoles[n.RoleId].Name, SubAssemblyRules.NormalizeName(n.SectionName))),
            .. recipe.Linked.Select(l => linkedSubAssemblies[l.SubAssemblyId])
                .Select(s => (s.RoleId, s.Role!.Name, (string?)SubAssemblyRules.SectionLabel(s.Name, productName, s.Role.Name))),
        ]);

        // Add-ins go on top of a linked recipe, so they're only for what it doesn't already have. Checked
        // here, before anything is written; SetLinesAsync checks them again as it writes them.
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

        // Only the usages go: a sub-assembly the recipe no longer lists stays on the Sub-Assemblies screen.
        await dbContext.ProductSubAssemblyUsages
            .Where(u => u.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);

        // Each new section becomes a sub-assembly, saved one at a time for the id its lines are written against, and
        // named apart from every other, those saved just before it included.
        List<string> takenNames = recipe.New.Count > 0 ? await dbContext.SubAssemblies.Select(s => s.Name).ToListAsync(cancellationToken) : [];
        foreach (var section in recipe.New)
        {
            var name = SubAssemblyRules.UniqueStandaloneName(
                SubAssemblyRules.StandaloneNameFor(productName, newRoles[section.RoleId].Name, section.SectionName), takenNames);
            takenNames.Add(name);

            var subAssembly = new SubAssembly { Name = name, RoleId = section.RoleId };
            dbContext.SubAssemblies.Add(subAssembly);
            await dbContext.SaveChangesAsync(cancellationToken);
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
