using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;


namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class ProductSubAssemblyUsageRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductSubAssemblyUsageRepository
{
    public Task<IReadOnlyList<ProductSubAssemblyUsage>> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
        GetUsagesAsync("u.product_id = @productId", new { productId }, cancellationToken);

    public async Task<IReadOnlyList<ProductSubAssemblyUsage>> GetForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        productIds.Count == 0
            ? []
            : await GetUsagesAsync($"u.product_id IN ({DapperReads.IdsTable})", new { ids = DapperReads.IdList(productIds) }, cancellationToken);

    private Task<IReadOnlyList<ProductSubAssemblyUsage>> GetUsagesAsync(string filter, object parameters, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<ProductSubAssemblyUsage>>(
            async dbContext => (await dbContext.Connection().QueryAsync<ProductSubAssemblyUsage, SubAssembly, SubAssemblyRole, Product?, ProductSubAssemblyUsage>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<ProductSubAssemblyUsage>("u")}, {SubAssemblyReads.ColumnsWithRole}
                    FROM product_sub_assembly_usages u
                    JOIN sub_assemblies s ON s.id = u.sub_assembly_id
                    {SubAssemblyReads.RoleJoin}
                    WHERE {filter}
                    ORDER BY u.product_id, u.id
                    """,
                    parameters,
                    cancellationToken),
                (usage, subAssembly, role, owner) =>
                {
                    usage.SubAssembly = SubAssemblyReads.WithRole(subAssembly, role, owner);
                    return usage;
                },
                splitOn: "Id,Id,Id")).AsList(),
            cancellationToken);

    // The Ingredient comes with it: every add-in is priced from its BufferedCostPerBaseUnit on read.
    public Task<IReadOnlyList<UsageAddInLine>> GetAddInsForProductAsync(int productId, CancellationToken cancellationToken) =>
        GetAddInsAsync("u.product_id = @productId", new { productId }, cancellationToken);

    public async Task<IReadOnlyList<UsageAddInLine>> GetAddInsForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        productIds.Count == 0
            ? []
            : await GetAddInsAsync($"u.product_id IN ({DapperReads.IdsTable})", new { ids = DapperReads.IdList(productIds) }, cancellationToken);

    private Task<IReadOnlyList<UsageAddInLine>> GetAddInsAsync(string filter, object parameters, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<UsageAddInLine>>(
            async dbContext => (await dbContext.Connection().QueryAsync<UsageAddInLine, RawMaterial, UsageAddInLine>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<UsageAddInLine>("l")}, {Columns.Of<RawMaterial>("i")}
                    FROM usage_add_in_lines l
                    JOIN product_sub_assembly_usages u ON u.id = l.usage_id
                    JOIN raw_materials i ON i.id = l.ingredient_id
                    WHERE {filter}
                    ORDER BY u.product_id, l.sort_order, l.id
                    """,
                    parameters,
                    cancellationToken),
                (line, ingredient) =>
                {
                    line.Ingredient = ingredient;
                    return line;
                },
                splitOn: "Id")).AsList(),
            cancellationToken);

    /// <summary>A product's recipe as its own screen saves it, linking nothing that's another product's own but what it already links.</summary>
    internal static Task SetRecipeAsync(TinapayanRMSDbContext dbContext, int productId, RecipeDraft recipe, CancellationToken cancellationToken) =>
        SetRecipeAsync(dbContext, productId, recipe, linksOthersOwn: false, cancellationToken);

    /// <summary>
    /// Replaces the product's recipe on the caller's context, which should hold a transaction: its usages, one for each
    /// sub-assembly <paramref name="recipe"/> links and one for each section whose lines it edits (RecipeDraft.New).
    /// Such a section in a product-owned role (SubAssemblyRole.IsProductOwned), or a new one placed SectionPlacement.ProductsOwn
    /// (an unlinked one), is the product's own sub-assembly, added or rewritten in place; in any other role a new one is kept
    /// on the Sub-Assemblies screen and linked, and one of its own moved there out of a product-owned role is released to
    /// it. One placed SectionPlacement.MoveToSubAssemblies goes to the Sub-Assemblies screen and is linked, whatever its
    /// role, and so does one placed MoveToInHouse, which a new made ingredient is then made from (Move to In-House).
    /// Either is named after the product and its role (SubAssemblyRules.StandaloneNameFor, made unique). One of its own the recipe no longer lists is deleted; one on the
    /// Sub-Assemblies screen is kept. A linked one carrying new lines (LinkedUsageDraft.Lines) is relined where it's kept,
    /// for every product linking it. Every check runs before anything is written: the sections' lines, names and piece
    /// sizes, that a saved one is the product's own, that no section is a packaging set, that nothing newly linked is
    /// another product's own (unless <paramref name="linksOthersOwn"/>), that none of its own the recipe drops is linked
    /// by another product, sections in one role told apart by their names, and add-ins.
    /// </summary>
    /// <param name="linksOthersOwn">
    /// Create Variant's recipe, which links its source's own sub-assemblies. Only a variant links another product's own;
    /// the links it's made with stay through every later save of it, and a dropped one can't be linked again.
    /// </param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1502:Avoid excessive complexity", Justification = "Baseline of 2026-11-20 (ADR 0004): split it when it next changes for another reason.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "Baseline of 2026-11-20 (ADR 0004): split it when it next changes for another reason.")]
    internal static async Task SetRecipeAsync(
        TinapayanRMSDbContext dbContext, int productId, RecipeDraft recipe, bool linksOthersOwn, CancellationToken cancellationToken)
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
            .. recipe.New.Select((n, i) => (n.Id > 0 ? n.Id : -(i + 1), n.AuthoredValue)),
            .. recipe.Linked.Select(l => (l.SubAssemblyId, l.AuthoredValue)),
        ]);

        // Read in the caller's transaction, so a product added or renamed by the same save is named as it now is.
        var productName = await dbContext.Products
            .Where(p => p.Id == productId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException($"Product {productId} does not exist.");

        var roleIds = recipe.New.Select(n => n.RoleId).Distinct().ToList();
        var roles = await dbContext.SubAssemblyRoles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        if (roleIds.Count != roles.Count)
        {
            throw new BusinessRuleException("Every sub-assembly needs a role, and that role must still exist.");
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
            throw new BusinessRuleException(
                $"Sub-assembly {section.Id} isn't one of this product's own any more. Revert the product to see its recipe as it's saved now.");
        }

        var linkedIds = recipe.Linked.Select(l => l.SubAssemblyId).ToList();
        var linkedSubAssemblies = await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .Include(s => s.OwnerProduct)
            .Where(s => linkedIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        // Another product's own already linked here came from Create Variant, and stays linked.
        HashSet<int> alreadyLinkedIds = linksOthersOwn
            ? []
            : [.. await dbContext.ProductSubAssemblyUsages.Where(u => u.ProductId == productId).Select(u => u.SubAssemblyId).ToListAsync(cancellationToken)];
        foreach (var id in linkedIds)
        {
            if (!linkedSubAssemblies.TryGetValue(id, out var linked))
            {
                throw new BusinessRuleException($"Sub-assembly {id} does not exist.");
            }

            // A packaging set is linked by the product's packaging, never as a recipe section; a product's own is copied,
            // except by its variants.
            SubAssemblyRules.EnsureRecipeRole(linked.Role!);
            if (!linksOthersOwn && !alreadyLinkedIds.Contains(id))
            {
                SubAssemblyRules.EnsureLinkable(linked.Name, linked.OwnerProduct?.Name);
            }
        }

        SubAssemblyRules.EnsureSectionsDistinct(
        [
            .. recipe.New.Select(n => (n.RoleId, roles[n.RoleId].Name, SubAssemblyRules.NormalizeName(n.SectionName))),
            .. recipe.Linked.Select(l => linkedSubAssemblies[l.SubAssemblyId])
                .Select(s => (s.RoleId, s.Role!.Name, SubAssemblyRules.LabelIn(s, productId, productName, s.Role.Name))),
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

        // A section moving to In-House goes to the Sub-Assemblies screen, with a made ingredient made from it under a raw
        // material name no other has, not even another moving in this save, in a Made category.
        List<MadeIngredientDraft> toMade = [.. recipe.New.Select(n => n.Placement).OfType<SectionPlacement.MoveToInHouse>().Select(p => p.Ingredient)];
        if (toMade.Count > 0)
        {
            List<string> newNames = [.. toMade.Select(m => m.Name.Trim())];
            HashSet<string> taken = new(
                await dbContext.RawMaterials.Where(m => newNames.Contains(m.Name)).Select(m => m.Name).ToListAsync(cancellationToken),
                StringComparer.OrdinalIgnoreCase);
            foreach (var made in toMade)
            {
                var name = made.Name.Trim();
                SubAssemblyRules.EnsureValidMadeIngredientName(name, !taken.Add(name));
            }

            List<int> categoryIds = [.. toMade.Select(m => m.CategoryId).Distinct()];
            if (await dbContext.RawMaterialCategories.CountAsync(c => categoryIds.Contains(c.Id), cancellationToken) < categoryIds.Count)
            {
                throw new BusinessRuleException("The category to put the made ingredient in no longer exists.");
            }
        }

        // One of its own the recipe no longer lists is deleted, so a variant can't be linking it.
        HashSet<int> keptIds = [.. recipe.New.Where(n => n.Id > 0).Select(n => n.Id)];
        List<int> removedIds = [.. existingOwned.Keys.Where(id => !keptIds.Contains(id))];
        if (removedIds.Count > 0)
        {
            await SubAssemblyRepository.EnsureNoneLinkedElsewhereAsync(
                dbContext, u => removedIds.Contains(u.SubAssemblyId) && u.ProductId != productId, cancellationToken);
        }

        // The usages go first: they reference the product's own sub-assemblies about to be deleted. One on the
        // Sub-Assemblies screen the recipe no longer lists stays there; one of its own goes, with its lines (Cascade).
        await dbContext.ProductSubAssemblyUsages
            .Where(u => u.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);

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
            var sectionName = SubAssemblyRules.StoredSectionName(section.SectionName, role.Name);
            if (stored.RoleId != section.RoleId || !string.Equals(stored.SectionName, sectionName, StringComparison.Ordinal))
            {
                renames.Add((stored, SubAssemblyRules.StandaloneNameFor(productName, role.Name, sectionName)));
            }

            // Read before RoleId changes: the tracked Role isn't moved over to the new one until SaveChanges. One moved to
            // the Sub-Assemblies screen (MovesToSubAssemblies, or to In-House) goes there whatever its role, keeping its name, and is linked below.
            var wasRoleProductOwned = stored.Role!.IsProductOwned;
            stored.RoleId = section.RoleId;
            stored.OwnerProductId = !section.MovesToSubAssemblies && (role.IsProductOwned || !wasRoleProductOwned) ? productId : null;

            // Its section name is its product's; on the Sub-Assemblies screen it goes by its name.
            stored.SectionName = stored.OwnerProductId is null ? null : sectionName;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SubAssemblyRepository.RenameAsync(dbContext, renames, cancellationToken);

        // New ones: the product's own in a product-owned role or when unlinked (ProductsOwn), otherwise, or when moved there,
        // kept on the Sub-Assemblies screen. Saved one at a time for the id its lines are written against, and named apart from every
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
                var isOwn = section.Placement switch
                {
                    SectionPlacement.ByRole => role.IsProductOwned,
                    SectionPlacement.ProductsOwn => true,
                    SectionPlacement.MoveToSubAssemblies or SectionPlacement.MoveToInHouse => false,
                };
                subAssembly = new SubAssembly
                {
                    Name = name,
                    RoleId = section.RoleId,
                    OwnerProductId = isOwn ? productId : null,
                    SectionName = isOwn ? SubAssemblyRules.StoredSectionName(section.SectionName, role.Name) : null,
                };
                dbContext.SubAssemblies.Add(subAssembly);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await SubAssemblyLineRepository.SetLinesAsync(dbContext, subAssembly, section.Lines, cancellationToken);

            // Moved to In-House: a made ingredient made from it, now that it's on the Sub-Assemblies screen with its lines.
            if (section.Placement is SectionPlacement.MoveToInHouse(var made))
            {
                await RawMaterialRepository.AddMadeAsync(dbContext, made, subAssembly.Id, cancellationToken);
            }

            dbContext.ProductSubAssemblyUsages.Add(new ProductSubAssemblyUsage
            {
                ProductId = productId,
                SubAssemblyId = subAssembly.Id,
                AuthoredField = section.AuthoredField,
                AuthoredValue = section.AuthoredValue,
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
