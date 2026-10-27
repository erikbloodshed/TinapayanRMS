using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class SubAssemblyRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISubAssemblyRepository
{
    /// <summary>
    /// A product's id, name, category name, whether it's deleted and whether it's a composite, for each recipe usage of a
    /// sub-assembly.
    /// </summary>
    private const string RecipeLinksSql = """
        SELECT u.sub_assembly_id, p.id AS product_id, p.name AS product_name, c.name AS category_name, p.deleted_at, p.is_composite
        FROM product_sub_assembly_usages u
        JOIN products p ON p.id = u.product_id
        LEFT JOIN product_categories c ON c.id = p.category_id
        """;

    public async Task<IReadOnlyList<SubAssembly>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContextFactory.ReadAsync(
            dbContext => SubAssemblyReads.GetWithRoleAsync(
                dbContext, $"WHERE s.id IN ({DapperReads.IdsTable})\nORDER BY s.id", new { ids = DapperReads.IdList(ids) }, cancellationToken),
            cancellationToken);
    }

    /// <summary>With each one's owner, if a product owns it: that one can only be copied.</summary>
    public Task<IReadOnlyList<SubAssembly>> GetLinkableAsync(CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<SubAssembly>>(
            dbContext => SubAssemblyReads.GetWithRoleAsync(
                dbContext, "WHERE s.total_weight > 0 AND NOT r.is_packaging\nORDER BY s.name", null, cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<SubAssembly>> GetStandaloneAsync(CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(
            dbContext => SubAssemblyReads.GetWithRoleAsync(dbContext, "WHERE s.owner_product_id IS NULL\nORDER BY s.name", null, cancellationToken),
            cancellationToken);

    /// <summary>The recipes linking a sub-assembly, and the products whose packaging links a set, in one query.</summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>>> GetStandaloneLinkedByAsync(CancellationToken cancellationToken) =>
        // A sub-assembly is either a recipe or a set, so no id is in both halves.
        ToLinkingProducts(await dbContextFactory.QueryAsync<LinkRow>(
            $"""
            {RecipeLinksSql}
            UNION ALL
            SELECT l.linked_sub_assembly_id AS sub_assembly_id, p.id AS product_id, p.name AS product_name, c.name AS category_name, p.deleted_at, p.is_composite
            FROM packaging_links l
            JOIN products p ON p.id = l.product_id
            LEFT JOIN product_categories c ON c.id = p.category_id
            WHERE l.linked_sub_assembly_id IS NOT NULL
            """,
            null,
            cancellationToken));

    public Task<IReadOnlyList<SubAssemblyPackagingLine>> GetPackagingLinesAsync(int subAssemblyId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(dbContext => RawMaterialReads.GetSetLinesAsync(dbContext, subAssemblyId, cancellationToken), cancellationToken);

    public async Task<int> SaveStandaloneAsync(SubAssembly subAssembly, IReadOnlyList<RecipeLine> lines, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var stored = await StageStandaloneAsync(dbContext, subAssembly, isPackaging: false, cancellationToken);

        // Saved here so a new one has its id for its lines; SetLinesAsync checks them all before replacing any.
        SubAssemblyRules.EnsureValidLines(lines);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SubAssemblyLineRepository.SetLinesAsync(dbContext, stored, lines, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return stored.Id;
    }

    public async Task<int> SaveStandalonePackagingAsync(
        SubAssembly subAssembly, IReadOnlyList<(int PackagingMaterialId, int? UnitId, decimal QtyPerSet)> lines, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var id = await SavePackagingSetAsync(dbContext, subAssembly, lines, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// Saves a packaging set, new or saved, with its lines on the caller's context, which should hold a transaction,
    /// after every check SaveStandalonePackagingAsync makes. Returns its id. Also how a product's packaging lines move to
    /// the Sub-Assemblies screen (PackagingLinkRepository.LinkNewSetAsync).
    /// </summary>
    internal static async Task<int> SavePackagingSetAsync(
        TinapayanRMSDbContext dbContext,
        SubAssembly subAssembly,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, decimal QtyPerSet)> lines,
        CancellationToken cancellationToken)
    {
        var stored = await StageStandaloneAsync(dbContext, subAssembly, isPackaging: true, cancellationToken);

        SubAssemblyRules.EnsureValidPackagingLines(lines);
        var materialIds = lines.Select(l => l.PackagingMaterialId).ToList();
        var materials = await dbContext.RawMaterials
            .Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        foreach (var line in lines)
        {
            var material = materials.GetValueOrDefault(line.PackagingMaterialId)
                ?? throw new BusinessRuleException($"Packaging material {line.PackagingMaterialId} does not exist.");
            RawMaterialRules.EnsureUsableAsPackaging(material.Type, material.Name);
        }

        // A line's unit is one of its own material's alternative units; an empty one means that material's base unit.
        var unitIds = lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value).Distinct().ToList();
        var materialIdByUnitId = await dbContext.RawMaterialUnits
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.RawMaterialId, cancellationToken);
        foreach (var line in lines)
        {
            if (line.UnitId is { } unitId
                && (!materialIdByUnitId.TryGetValue(unitId, out var unitMaterialId) || unitMaterialId != line.PackagingMaterialId))
            {
                throw new BusinessRuleException("A packaging set line's unit must be one of that material's own units.");
            }
        }

        // Saved here so a new one has its id for its lines. A set is costed on read, so its stored totals stay 0.
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.SubAssemblyPackagingLines.Where(l => l.SubAssemblyId == stored.Id).ExecuteDeleteAsync(cancellationToken);
        dbContext.SubAssemblyPackagingLines.AddRange(lines.Select((l, index) => new SubAssemblyPackagingLine
        {
            SubAssemblyId = stored.Id,
            PackagingMaterialId = l.PackagingMaterialId,
            UnitId = l.UnitId,
            QtyPerSet = l.QtyPerSet,
            SortOrder = index,
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return stored.Id;
    }

    /// <summary>
    /// A sub-assembly's name and role, checked and applied to a new or loaded entity on the caller's
    /// context, not yet saved: the name unique, the role of the kind being saved (the
    /// Packaging role for a set, any other for a recipe), and a saved one's kind unchanged.
    /// </summary>
    private static async Task<SubAssembly> StageStandaloneAsync(
        TinapayanRMSDbContext dbContext, SubAssembly subAssembly, bool isPackaging, CancellationToken cancellationToken)
    {
        var name = subAssembly.Name.Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("Every sub-assembly needs a name.");
        }

        var role = await dbContext.SubAssemblyRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == subAssembly.RoleId, cancellationToken)
            ?? throw new BusinessRuleException("Every sub-assembly needs a role, and that role must still exist.");
        if (isPackaging && !role.IsPackaging)
        {
            throw new BusinessRuleException($"A packaging set goes in the Packaging role, not {role.Name}.");
        }

        if (!isPackaging)
        {
            SubAssemblyRules.EnsureRecipeRole(role);
        }

        var otherNames = await dbContext.SubAssemblies
            .Where(s => s.Id != subAssembly.Id)
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);
        SubAssemblyRules.EnsureStandaloneNameAvailable(name, otherNames);

        // The caller's entity is never attached, so a rolled-back save leaves no id on it.
        SubAssembly stored;
        if (subAssembly.Id == 0)
        {
            stored = new SubAssembly { Name = name, RoleId = subAssembly.RoleId };
            dbContext.SubAssemblies.Add(stored);
        }
        else
        {
            stored = await dbContext.SubAssemblies.Include(s => s.Role).Include(s => s.OwnerProduct).FirstOrDefaultAsync(s => s.Id == subAssembly.Id, cancellationToken)
                ?? throw new BusinessRuleException($"Sub-assembly {subAssembly.Id} does not exist.");
            if (stored.OwnerProduct is { } owner)
            {
                throw new BusinessRuleException($"{stored.Name} is {owner.Name}'s own. Change it in that product's recipe.");
            }

            SubAssemblyRules.EnsureKindUnchanged(stored.Role!.IsPackaging, isPackaging);

            // Every product using it links it, and takes its new role and name with it.
            if (stored.RoleId != subAssembly.RoleId || !string.Equals(stored.Name, name, StringComparison.Ordinal))
            {
                await EnsureDistinctWhereLinkedAsync(dbContext, stored.Id, subAssembly.RoleId, name, cancellationToken);
            }

            // A composite that links it can only have sections in product-owned roles.
            if (stored.RoleId != subAssembly.RoleId
                && !role.IsProductOwned
                && await ProductComponentRepository.CompositesUsingRoleAsync(dbContext, stored.RoleId, stored.Id, cancellationToken) is [_, ..] composites)
            {
                throw new BusinessRuleException(
                    $"{ProductComponentRepository.NamesHave(composites)} {stored.Name} as a section, and a composite product can only "
                    + $"have sections in product-owned roles, which {role.Name} isn't. Take it out there first, or pick a product-owned role.");
            }

            stored.Name = name;
            stored.RoleId = subAssembly.RoleId;
        }

        return stored;
    }

    public async Task MoveAsync(IReadOnlyCollection<SubAssemblyMove> moves, CancellationToken cancellationToken)
    {
        if (moves.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Saved one at a time, inside the transaction: each check reads the database, so two a product links that move
        // into the same role are checked against each other.
        foreach (var move in moves)
        {
            await RestageAsync(dbContext, move.SubAssemblyId, name: null, move.RoleId, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RenameAsync(int id, string name, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await RestageAsync(dbContext, id, name, roleId: null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A saved sub-assembly with a new name or role and everything else as it is, through every check a Save of the form
    /// makes (StageStandaloneAsync), not yet saved.
    /// </summary>
    private static async Task RestageAsync(TinapayanRMSDbContext dbContext, int id, string? name, int? roleId, CancellationToken cancellationToken)
    {
        var stored = await dbContext.SubAssemblies
            .Where(s => s.Id == id)
            .Select(s => new { s.Name, s.RoleId, s.Role!.IsPackaging })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException($"Sub-assembly {id} does not exist.");

        await StageStandaloneAsync(
            dbContext,
            new SubAssembly { Id = id, Name = name ?? stored.Name, RoleId = roleId ?? stored.RoleId },
            stored.IsPackaging,
            cancellationToken);
    }

    public async Task DeleteStandaloneAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stored = await dbContext.SubAssemblies.Where(s => s.Id == id).Select(s => new { s.Name, OwnerName = s.OwnerProduct!.Name }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException($"Sub-assembly {id} does not exist.");
        if (stored.OwnerName is { } ownerName)
        {
            throw new BusinessRuleException($"{stored.Name} is {ownerName}'s own. Remove it from that product's recipe instead.");
        }

        var name = stored.Name;

        // A made ingredient made from it keeps its recipe here (Restrict, refused first by name).
        SubAssemblyRules.EnsureNoMadeIngredientMadeFrom(
            name, await dbContext.RawMaterials.Where(m => m.SubAssemblyId == id).OrderBy(m => m.Name).Select(m => m.Name).ToListAsync(cancellationToken));

        // Every product using a sub-assembly links it: a recipe through a usage, a set through its
        // packaging link.
        await EnsureNoneLinkedElsewhereAsync(dbContext, u => u.SubAssemblyId == id, cancellationToken);
        var setLinkedBy = await dbContext.PackagingLinks
            .Where(l => l.LinkedSubAssemblyId == id)
            .Select(l => new LinkRow(id, l.ProductId, l.Product!.Name, null, l.Product.DeletedAt, l.Product.IsComposite))
            .ToListAsync(cancellationToken);
        if (setLinkedBy.Count > 0)
        {
            SubAssemblyRules.EnsureNotLinkedElsewhere(name, [.. setLinkedBy.Select(l => l.ListedName).Order(StringComparer.CurrentCultureIgnoreCase)]);
        }

        // Its lines go with it (Cascade).
        await dbContext.SubAssemblies.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<MadeIngredientUse>>> GetMadeIngredientsBySubAssemblyAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var made = await dbContext.RawMaterials
            .Where(m => m.SubAssemblyId != null)
            .Select(m => new { SubAssemblyId = m.SubAssemblyId!.Value, m.Id, m.Name })
            .ToListAsync(cancellationToken);
        return made
            .GroupBy(m => m.SubAssemblyId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<MadeIngredientUse>)[.. g.Select(m => new MadeIngredientUse(m.Id, m.Name)).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)]);
    }

    public async Task<int> AddMadeIngredientAsync(int id, MadeIngredientDraft made, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subAssembly = await dbContext.SubAssemblies
            .Where(s => s.Id == id)
            .Select(s => new { s.Name, OwnerName = s.OwnerProduct!.Name, s.Role!.IsPackaging })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException($"Sub-assembly {id} does not exist.");
        if (subAssembly.OwnerName is { } ownerName)
        {
            throw new BusinessRuleException($"{subAssembly.Name} is {ownerName}'s own. Move it to In-House from that product's recipe instead.");
        }

        SubAssemblyRules.EnsureLinkableByMadeIngredient(subAssembly.Name, ownerName: null, subAssembly.IsPackaging);

        var name = made.Name.Trim();
        SubAssemblyRules.EnsureValidMadeIngredientName(name, await dbContext.RawMaterials.AnyAsync(m => m.Name == name, cancellationToken));
        if (!await dbContext.RawMaterialCategories.AnyAsync(c => c.Id == made.CategoryId, cancellationToken))
        {
            throw new BusinessRuleException("The category to put the made ingredient in no longer exists.");
        }

        // The recipe stays here as it is, the one place it's written, and every product linking it is left alone.
        var ingredientId = await RawMaterialRepository.AddMadeAsync(dbContext, made with { Name = name }, id, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return ingredientId;
    }

    /// <summary>
    /// For each sub-assembly matching <paramref name="subAssemblyFilter"/> (applied to its usages) that a
    /// product uses, the names of those products. One query.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetLinkedByAsync(
        TinapayanRMSDbContext dbContext,
        System.Linq.Expressions.Expression<Func<ProductSubAssemblyUsage, bool>> subAssemblyFilter,
        CancellationToken cancellationToken)
    {
        var links = await dbContext.ProductSubAssemblyUsages
            .Where(subAssemblyFilter)
            .Select(u => new LinkRow(u.SubAssemblyId, u.ProductId, u.Product!.Name, null, u.Product.DeletedAt, u.Product.IsComposite))
            .ToListAsync(cancellationToken);

        return ToLinkedBy(links);
    }

    /// <summary>A product linking a sub-assembly. One in the Deleted folder still holds it, and is named as deleted.</summary>
    private sealed record LinkRow(int SubAssemblyId, int ProductId, string ProductName, string? CategoryName, DateTime? DeletedAt, bool IsComposite)
    {
        public string ListedName => ProductRules.ListedName(ProductName, DeletedAt is not null);
    }

    /// <summary>Each sub-assembly's linking products' names, alphabetically.</summary>
    private static Dictionary<int, IReadOnlyList<string>> ToLinkedBy(IEnumerable<LinkRow> links) =>
        links
            .GroupBy(l => l.SubAssemblyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(l => l.ListedName).Order(StringComparer.CurrentCultureIgnoreCase)]);

    /// <summary>Each sub-assembly's linking products, alphabetically, each once however many of its usages link it.</summary>
    private static Dictionary<int, IReadOnlyList<LinkingProduct>> ToLinkingProducts(IEnumerable<LinkRow> links) =>
        links
            .GroupBy(l => l.SubAssemblyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LinkingProduct>)
            [
                .. g.DistinctBy(l => l.ProductId)
                    .Select(l => new LinkingProduct(l.ProductId, l.ListedName, l.CategoryName, l.IsComposite))
                    .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            ]);

    /// <summary>
    /// Refuses to go on while a product links a sub-assembly matching
    /// <paramref name="subAssemblyFilter"/>, naming the first such sub-assembly and every product linking
    /// it (SubAssemblyRules.EnsureNotLinkedElsewhere).
    /// </summary>
    internal static async Task EnsureNoneLinkedElsewhereAsync(
        TinapayanRMSDbContext dbContext,
        System.Linq.Expressions.Expression<Func<ProductSubAssemblyUsage, bool>> subAssemblyFilter,
        CancellationToken cancellationToken)
    {
        var linkedBy = await GetLinkedByAsync(dbContext, subAssemblyFilter, cancellationToken);
        if (linkedBy.Count == 0)
        {
            return;
        }

        var (subAssemblyId, productNames) = linkedBy.First();
        var name = await dbContext.SubAssemblies.Where(s => s.Id == subAssemblyId).Select(s => s.Name).FirstAsync(cancellationToken);
        SubAssemblyRules.EnsureNotLinkedElsewhere(name, productNames);
    }

    /// <summary>
    /// Refuses a new role or name for a sub-assembly products link when a recipe linking it could no longer tell its
    /// sections apart (SubAssemblyRules.EnsureSectionsDistinct): every link takes the role and name with it, and a name
    /// can read like another's there once the product's own name is taken off it (SubAssemblyRules.SectionLabel).
    /// </summary>
    internal static async Task EnsureDistinctWhereLinkedAsync(
        TinapayanRMSDbContext dbContext, int subAssemblyId, int roleId, string name, CancellationToken cancellationToken)
    {
        var linkingProductIds = await dbContext.ProductSubAssemblyUsages
            .Where(u => u.SubAssemblyId == subAssemblyId)
            .Select(u => u.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (linkingProductIds.Count == 0)
        {
            return;
        }

        // Every section of those recipes, with what each goes by: its role, and its name.
        var sections = await dbContext.ProductSubAssemblyUsages
            .AsNoTracking()
            .Include(u => u.Product)
            .Include(u => u.SubAssembly)
            .ThenInclude(s => s!.Role)
            .Where(u => linkingProductIds.Contains(u.ProductId))
            .ToListAsync(cancellationToken);
        var roleName = await dbContext.SubAssemblyRoles.Where(r => r.Id == roleId).Select(r => r.Name).FirstAsync(cancellationToken);

        foreach (var recipe in sections.GroupBy(u => u.ProductId))
        {
            SubAssemblyRules.EnsureSectionsDistinct(
                [
                    .. recipe.Select(u => u.SubAssemblyId == subAssemblyId
                        ? (roleId, roleName, (string?)SubAssemblyRules.SectionLabel(name, u.Product!.Name, roleName))
                        : (u.SubAssembly!.RoleId, u.SubAssembly.Role!.Name, SubAssemblyRules.SectionLabel(u.SubAssembly.Name, u.Product!.Name, u.SubAssembly.Role.Name))),
                ],
                $"{recipe.First().Product!.Name}, which links this sub-assembly,");
        }
    }

    /// <summary>
    /// Gives each sub-assembly (tracked on the caller's context) the name it's paired with, made unique against every
    /// other (SubAssemblyRules.UniqueStandaloneName). Each first takes a name no one could have, so two can swap names
    /// in one go without the unique index refusing the first halfway.
    /// </summary>
    internal static async Task RenameAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyList<(SubAssembly SubAssembly, string Name)> renames, CancellationToken cancellationToken)
    {
        if (renames.Count == 0)
        {
            return;
        }

        foreach (var (subAssembly, _) in renames)
        {
            subAssembly.Name = $"~{Guid.NewGuid():N}";
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        List<int> renamedIds = [.. renames.Select(r => r.SubAssembly.Id)];
        List<string> taken = await dbContext.SubAssemblies.Where(s => !renamedIds.Contains(s.Id)).Select(s => s.Name).ToListAsync(cancellationToken);
        foreach (var (subAssembly, name) in renames)
        {
            subAssembly.Name = SubAssemblyRules.UniqueStandaloneName(name, taken);
            taken.Add(subAssembly.Name);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The name each of <paramref name="owned"/> (tracked, with their roles and owners) should have, from
    /// <paramref name="nameOf"/>, given where it differs: after its product or its role was renamed, so its name goes on
    /// reading "Cheese Roll Filling: Cheese".
    /// </summary>
    internal static Task ResyncOwnedNamesAsync(
        TinapayanRMSDbContext dbContext, IEnumerable<SubAssembly> owned, Func<SubAssembly, string> nameOf, CancellationToken cancellationToken) =>
        RenameAsync(
            dbContext,
            [.. owned.Select(s => (SubAssembly: s, Name: nameOf(s))).Where(r => !string.Equals(r.SubAssembly.Name, r.Name, StringComparison.Ordinal))],
            cancellationToken);

    /// <summary>
    /// The sub-assemblies on the Sub-Assemblies screen in <paramref name="roleId"/> that making it product-owned would
    /// give to a product: the ones exactly one product's recipe uses, and no made ingredient is made from (it uses the
    /// recipe too, and can only be made from one kept here).
    /// </summary>
    internal static IQueryable<SubAssembly> SingleUse(TinapayanRMSDbContext dbContext, int roleId) =>
        dbContext.SubAssemblies.Where(s =>
            s.OwnerProductId == null
            && s.RoleId == roleId
            && dbContext.ProductSubAssemblyUsages.Count(u => u.SubAssemblyId == s.Id) == 1
            && !dbContext.RawMaterials.Any(m => m.SubAssemblyId == s.Id));

    /// <summary>
    /// A role made product-owned: each of its sub-assemblies on the Sub-Assemblies screen that exactly one product uses
    /// becomes that product's own, keeping its name. The product's add-ins on it go into its lines, after its own, since
    /// the recipe is the product's alone now (an add-in is never one of its ingredients: ProductRules.EnsureValidAddIns).
    /// </summary>
    internal static async Task AdoptSingleUseAsync(TinapayanRMSDbContext dbContext, int roleId, CancellationToken cancellationToken)
    {
        var adopted = await SingleUse(dbContext, roleId).ToListAsync(cancellationToken);
        if (adopted.Count == 0)
        {
            return;
        }

        List<int> ids = [.. adopted.Select(s => s.Id)];
        var usages = await dbContext.ProductSubAssemblyUsages
            .AsNoTracking()
            .Where(u => ids.Contains(u.SubAssemblyId))
            .ToDictionaryAsync(u => u.SubAssemblyId, cancellationToken);
        List<int> usageIds = [.. usages.Values.Select(u => u.Id)];
        var addIns = (await dbContext.UsageAddInLines
                .AsNoTracking()
                .Where(l => usageIds.Contains(l.UsageId))
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.UsageId);

        foreach (var subAssembly in adopted)
        {
            var usage = usages[subAssembly.Id];
            subAssembly.OwnerProductId = usage.ProductId;

            if (addIns[usage.Id].Any())
            {
                var lines = await dbContext.SubAssemblyLines
                    .AsNoTracking()
                    .Where(l => l.SubAssemblyId == subAssembly.Id)
                    .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
                    .ToListAsync(cancellationToken);
                await SubAssemblyLineRepository.SetLinesAsync(
                    dbContext,
                    subAssembly,
                    [
                        .. lines.Select(l => new RecipeLine(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)),
                        .. addIns[usage.Id].Select(l => new RecipeLine(l.IngredientId, l.Grams)),
                    ],
                    cancellationToken);
                await dbContext.UsageAddInLines.Where(l => l.UsageId == usage.Id).ExecuteDeleteAsync(cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A role no longer product-owned: every sub-assembly products own in it goes to the Sub-Assemblies screen, and its
    /// product links it from there. Its name already is one no other has.
    /// </summary>
    internal static Task ReleaseOwnedAsync(TinapayanRMSDbContext dbContext, int roleId, CancellationToken cancellationToken) =>
        dbContext.SubAssemblies
            .Where(s => s.RoleId == roleId && s.OwnerProductId != null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.OwnerProductId, (int?)null), cancellationToken);
}
