using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class SubAssemblyRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : ISubAssemblyRepository
{
    /// <summary>
    /// A product's id and name for each recipe usage of a sub-assembly by a product other than its owner, for the
    /// sub-assemblies <c>s</c> the appended condition selects.
    /// </summary>
    private const string RecipeLinksSql = """
        SELECT u.SubAssemblyId, p.Id AS ProductId, p.Name AS ProductName
        FROM ProductSubAssemblyUsages u
        JOIN SubAssemblies s ON s.Id = u.SubAssemblyId
        JOIN Products p ON p.Id = u.ProductId
        WHERE (s.OwnerProductId IS NULL OR u.ProductId <> s.OwnerProductId) AND
        """;

    public async Task<IReadOnlyList<SubAssembly>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContextFactory.ReadAsync(
            dbContext => SubAssemblyReads.GetWithRoleAndOwnerAsync(
                dbContext, $"WHERE s.Id IN ({DapperReads.IdsTable})\nORDER BY s.Id", new { ids = DapperReads.IdList(ids) }, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<SubAssembly>> GetLinkableAsync(CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(
            dbContext => SubAssemblyReads.GetWithRoleAndOwnerAsync(
                dbContext,
                """
                WHERE s.TotalWeight > 0 AND r.IsPackaging = 0
                ORDER BY CASE WHEN s.OwnerProductId IS NULL THEN 0 ELSE 1 END, s.Name, o.Name, r.SortOrder
                """,
                null,
                cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetLinkedByAsync(int ownerProductId, CancellationToken cancellationToken) =>
        ToLinkedBy(await dbContextFactory.QueryAsync<LinkRow>(
            $"{RecipeLinksSql} s.OwnerProductId = @ownerProductId", new { ownerProductId }, cancellationToken));

    public Task<IReadOnlyList<SubAssembly>> GetStandaloneAsync(CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(
            dbContext => SubAssemblyReads.GetWithRoleAndOwnerAsync(dbContext, "WHERE s.OwnerProductId IS NULL\nORDER BY s.Name", null, cancellationToken),
            cancellationToken);

    /// <summary>The recipes linking a standalone sub-assembly, and the products whose packaging links a set, in one query.</summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>>> GetStandaloneLinkedByAsync(CancellationToken cancellationToken) =>
        // A sub-assembly is either a recipe or a set, so no id is in both halves.
        ToLinkingProducts(await dbContextFactory.QueryAsync<LinkRow>(
            $"""
            {RecipeLinksSql} s.OwnerProductId IS NULL
            UNION ALL
            SELECT l.LinkedSubAssemblyId AS SubAssemblyId, p.Id AS ProductId, p.Name AS ProductName
            FROM PackagingLinks l
            JOIN Products p ON p.Id = l.ProductId
            WHERE l.LinkedSubAssemblyId IS NOT NULL
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
                ?? throw new InvalidOperationException($"Packaging material {line.PackagingMaterialId} does not exist.");
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
                throw new InvalidOperationException("A packaging set line's unit must be one of that material's own units.");
            }
        }

        // Saved here so a new one has its id for its lines. A set is costed on read, so its stored totals stay 0.
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.SubAssemblyPackagingLines.Where(l => l.SubAssemblyId == stored.Id).ExecuteDeleteAsync(cancellationToken);
        dbContext.SubAssemblyPackagingLines.AddRange(lines.Select(l => new SubAssemblyPackagingLine
        {
            SubAssemblyId = stored.Id,
            PackagingMaterialId = l.PackagingMaterialId,
            UnitId = l.UnitId,
            QtyPerSet = l.QtyPerSet,
        }));
        await dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return stored.Id;
    }

    /// <summary>
    /// A standalone sub-assembly's name and role, checked and applied to a new or loaded entity on the caller's
    /// context, not yet saved: the name unique among standalone ones, the role of the kind being saved (the
    /// Packaging role for a set, any other for a recipe), and a saved one's kind unchanged.
    /// </summary>
    private static async Task<SubAssembly> StageStandaloneAsync(
        TinapayanRMSDbContext dbContext, SubAssembly subAssembly, bool isPackaging, CancellationToken cancellationToken)
    {
        var name = subAssembly.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Every sub-assembly needs a name.");
        }

        var role = await dbContext.SubAssemblyRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == subAssembly.RoleId, cancellationToken)
            ?? throw new InvalidOperationException("Every sub-assembly needs a role, and that role must still exist.");
        if (isPackaging && !role.IsPackaging)
        {
            throw new InvalidOperationException($"A packaging set goes in the Packaging role, not {role.Name}.");
        }

        if (!isPackaging)
        {
            SubAssemblyRules.EnsureRecipeRole(role);
        }

        var otherNames = await dbContext.SubAssemblies
            .Where(s => s.OwnerProductId == null && s.Id != subAssembly.Id)
            .Select(s => s.Name ?? string.Empty)
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
            stored = await dbContext.SubAssemblies.Include(s => s.Role).FirstOrDefaultAsync(s => s.Id == subAssembly.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Sub-assembly {subAssembly.Id} does not exist.");
            if (stored.OwnerProductId is not null)
            {
                throw new InvalidOperationException("That sub-assembly belongs to a product. Change it in that product's recipe.");
            }

            SubAssemblyRules.EnsureKindUnchanged(stored.Role!.IsPackaging, isPackaging);

            // Every product using it links it, and takes its new role and name with it.
            if (stored.RoleId != subAssembly.RoleId || !string.Equals(stored.Name, name, StringComparison.Ordinal))
            {
                await EnsureDistinctWhereLinkedAsync(dbContext, stored.Id, subAssembly.RoleId, name, cancellationToken);
            }

            stored.Name = name;
            stored.RoleId = subAssembly.RoleId;
        }

        return stored;
    }

    public async Task DeleteStandaloneAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ownerProductId = await dbContext.SubAssemblies
            .Where(s => s.Id == id)
            .Select(s => new { s.OwnerProductId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Sub-assembly {id} does not exist.");
        if (ownerProductId.OwnerProductId is not null)
        {
            throw new InvalidOperationException("That sub-assembly belongs to a product. Remove it from that product's recipe instead.");
        }

        // Every product using a standalone sub-assembly links it: a recipe through a usage, a set through its
        // packaging link.
        await EnsureNoneLinkedElsewhereAsync(dbContext, u => u.SubAssemblyId == id, cancellationToken);
        var setLinkedBy = await dbContext.PackagingLinks
            .Where(l => l.LinkedSubAssemblyId == id)
            .Select(l => l.Product!.Name)
            .ToListAsync(cancellationToken);
        if (setLinkedBy.Count > 0)
        {
            var name = await dbContext.SubAssemblies.Where(s => s.Id == id).Select(s => s.Name).FirstAsync(cancellationToken);
            SubAssemblyRules.EnsureNotLinkedElsewhere(name ?? string.Empty, [.. setLinkedBy.Order(StringComparer.CurrentCultureIgnoreCase)]);
        }

        // Its lines go with it (Cascade).
        await dbContext.SubAssemblies.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// For each sub-assembly matching <paramref name="subAssemblyFilter"/> (applied to its usages) that a
    /// product other than its owner uses, the names of those products. One query.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetLinkedByAsync(
        TinapayanRMSDbContext dbContext,
        System.Linq.Expressions.Expression<Func<ProductSubAssemblyUsage, bool>> subAssemblyFilter,
        CancellationToken cancellationToken)
    {
        var links = await dbContext.ProductSubAssemblyUsages
            .Where(subAssemblyFilter)
            .Where(u => u.ProductId != u.SubAssembly!.OwnerProductId)
            .Select(u => new LinkRow(u.SubAssemblyId, u.ProductId, u.Product!.Name))
            .ToListAsync(cancellationToken);

        return ToLinkedBy(links);
    }

    private sealed record LinkRow(int SubAssemblyId, int ProductId, string ProductName);

    /// <summary>Each sub-assembly's linking products' names, alphabetically.</summary>
    private static Dictionary<int, IReadOnlyList<string>> ToLinkedBy(IEnumerable<LinkRow> links) =>
        links
            .GroupBy(l => l.SubAssemblyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(l => l.ProductName).Order(StringComparer.CurrentCultureIgnoreCase)]);

    /// <summary>Each sub-assembly's linking products, alphabetically, each once however many of its usages link it.</summary>
    private static Dictionary<int, IReadOnlyList<LinkingProduct>> ToLinkingProducts(IEnumerable<LinkRow> links) =>
        links
            .GroupBy(l => l.SubAssemblyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LinkingProduct>)
            [
                .. g.DistinctBy(l => l.ProductId)
                    .Select(l => new LinkingProduct(l.ProductId, l.ProductName))
                    .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            ]);

    /// <summary>
    /// Refuses to go on while another product links a sub-assembly matching
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
        var subAssembly = await dbContext.SubAssemblies
            .AsNoTracking()
            .Include(s => s.Role)
            .Include(s => s.OwnerProduct)
            .FirstAsync(s => s.Id == subAssemblyId, cancellationToken);
        SubAssemblyRules.EnsureNotLinkedElsewhere(SubAssemblyRules.DisplayName(subAssembly), productNames);
    }

    /// <summary>
    /// Refuses a new role or name for a sub-assembly other products link when a recipe linking it could no longer tell
    /// its sections apart (SubAssemblyRules.EnsureSectionsDistinct): every link takes the role and name with it.
    /// </summary>
    internal static async Task EnsureDistinctWhereLinkedAsync(
        TinapayanRMSDbContext dbContext, int subAssemblyId, int roleId, string? name, CancellationToken cancellationToken)
    {
        var linkingProductIds = await dbContext.ProductSubAssemblyUsages
            .Where(u => u.SubAssemblyId == subAssemblyId && u.ProductId != u.SubAssembly!.OwnerProductId)
            .Select(u => u.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (linkingProductIds.Count == 0)
        {
            return;
        }

        // Every section of those recipes, with what each goes by: its role, and its name or else its owner's.
        var sections = await dbContext.ProductSubAssemblyUsages
            .AsNoTracking()
            .Include(u => u.Product)
            .Include(u => u.SubAssembly)
            .ThenInclude(s => s!.Role)
            .Include(u => u.SubAssembly)
            .ThenInclude(s => s!.OwnerProduct)
            .Where(u => linkingProductIds.Contains(u.ProductId))
            .ToListAsync(cancellationToken);
        var roleName = await dbContext.SubAssemblyRoles.Where(r => r.Id == roleId).Select(r => r.Name).FirstAsync(cancellationToken);

        foreach (var recipe in sections.GroupBy(u => u.ProductId))
        {
            SubAssemblyRules.EnsureSectionsDistinct(
                [
                    .. recipe.Select(u => u.SubAssemblyId == subAssemblyId
                        ? (roleId, roleName, SubAssemblyRules.SectionLabel(
                            new SubAssembly { Name = name, OwnerProductId = u.SubAssembly!.OwnerProductId, OwnerProduct = u.SubAssembly.OwnerProduct },
                            u.ProductId))
                        : (u.SubAssembly!.RoleId, u.SubAssembly.Role!.Name, SubAssemblyRules.SectionLabel(u.SubAssembly, u.ProductId))),
                ],
                $"{recipe.First().Product!.Name}, which links this sub-assembly,");
        }
    }
}
