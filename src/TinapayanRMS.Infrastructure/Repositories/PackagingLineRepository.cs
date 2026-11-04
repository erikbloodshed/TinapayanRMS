using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class PackagingLineRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingLineRepository
{
    public Task<IReadOnlyList<PackagingLine>> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(
            dbContext => RawMaterialReads.GetPackagingLinesAsync(dbContext, "WHERE l.product_id = @productId", new { productId }, cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyList<PackagingLine>> GetEffectiveForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ResolveEffectiveAsync(dbContext, productId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>>> GetEffectiveForProductsAsync(
        IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ResolveEffectiveAsync(dbContext, productIds, cancellationToken);
    }

    /// <summary><see cref="ResolveEffectiveAsync(TinapayanRMSDbContext, IReadOnlyCollection{int}, CancellationToken)"/> for one product.</summary>
    internal static async Task<IReadOnlyList<PackagingLine>> ResolveEffectiveAsync(
        TinapayanRMSDbContext dbContext, int productId, CancellationToken cancellationToken) =>
        (await ResolveEffectiveAsync(dbContext, [productId], cancellationToken))[productId];

    /// <summary>
    /// Each product's packaging as it shows, by product id: its link chain (if any) is followed to an unlinked product,
    /// whose own lines it shows, or to a packaging set, whose lines it shows as its own (<see cref="SetLinesAsPackagingLines"/>).
    /// Every link is read in one query and followed in memory, then the lines at the chains' ends in one query for
    /// products and one for sets, however many products and however long their chains (2026-10-03: it was a query per
    /// link followed, for each product, and Reports read every product that way). A chain that comes round again is
    /// refused, should one slip past SetLinkAsync's own check (e.g. a manual data edit). Reads through Dapper, in
    /// <paramref name="dbContext"/>'s transaction if it has one (ProductCloner's and RemoveLinkAsync's do).
    /// </summary>
    internal static async Task<IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>>> ResolveEffectiveAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        // One row a product at most, so every link is a small read, and a chain costs nothing more to follow.
        var links = (await dbContext.QueryAsync<PackagingLink>(
                $"SELECT {Columns.Of<PackagingLink>("l")} FROM packaging_links l", null, cancellationToken))
            .ToDictionary(l => l.ProductId);

        var ends = productIds.Distinct().ToDictionary(id => id, id => ChainEnd(id, links));
        var ownerIds = ends.Values.Where(end => end.SetId is null).Select(end => end.ProductId).Distinct().ToList();
        var setIds = ends.Values.Select(end => end.SetId).OfType<int>().Distinct().ToList();

        var ownLines = ownerIds.Count == 0
            ? []
            : await RawMaterialReads.GetPackagingLinesAsync(
                dbContext, $"WHERE l.product_id IN ({DapperReads.IdsTable})", new { ids = DapperReads.IdList(ownerIds) }, cancellationToken);
        var setLines = setIds.Count == 0 ? [] : await RawMaterialReads.GetSetLinesAsync(dbContext, setIds, cancellationToken);

        var ownLinesByProduct = ownLines.ToLookup(l => l.ProductId);
        var setLinesBySet = setLines.ToLookup(l => l.SubAssemblyId);
        return ends.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.SetId is { } setId
                ? SetLinesAsPackagingLines(pair.Value.ProductId, setLinesBySet[setId])
                : (IReadOnlyList<PackagingLine>)[.. ownLinesByProduct[pair.Value.ProductId]]);
    }

    /// <summary>
    /// Where <paramref name="productId"/>'s link chain ends: at the product whose own lines it shows, or at the set the
    /// last product in it links (SetId), with that product.
    /// </summary>
    private static (int ProductId, int? SetId) ChainEnd(int productId, Dictionary<int, PackagingLink> links)
    {
        HashSet<int> visited = [];
        while (true)
        {
            if (!visited.Add(productId))
            {
                throw new BusinessRuleException("Circular packaging link detected.");
            }

            if (!links.TryGetValue(productId, out var link) || link is { LinkedSubAssemblyId: null, LinkedProductId: null })
            {
                return (productId, null);
            }

            if (link.LinkedSubAssemblyId is { } setId)
            {
                return (productId, setId);
            }

            productId = link.LinkedProductId!.Value;
        }
    }

    /// <summary>
    /// A packaging set's lines as <paramref name="productId"/>'s own packaging lines, never saved: one set per
    /// sellable unit, so a set's quantity is that unit's, in the unit the set line was entered in, typed as the quantity.
    /// Everything that reads a product's packaging (the Products screen, Reports, unlinking's copy, a clone)
    /// takes them as they are.
    /// </summary>
    private static IReadOnlyList<PackagingLine> SetLinesAsPackagingLines(int productId, IEnumerable<SubAssemblyPackagingLine> setLines) =>
        [
            .. setLines.Select(l =>
            {
                var (qtyPerUnit, unitsPerPackagingUnit) = ProductRules.ReconcileReciprocal(PackagingLineAuthoredField.QtyPerUnit, l.QtyPerSet);
                return new PackagingLine
                {
                    ProductId = productId,
                    PackagingMaterialId = l.PackagingMaterialId,
                    PackagingMaterial = l.PackagingMaterial,
                    UnitId = l.UnitId,
                    Unit = l.Unit,
                    QtyPerUnit = qtyPerUnit,
                    UnitsPerPackagingUnit = unitsPerPackagingUnit,
                    AuthoredField = PackagingLineAuthoredField.QtyPerUnit,
                };
            }),
        ];

    public async Task SetLinesAsync(
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        // The old lines' delete, which runs at once, and the new lines' insert commit together.
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        await SetLinesAsync(dbContext, productId, lines, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the product's packaging lines on the caller's context and saves them, after every check
    /// SetLinesAsync makes.
    /// </summary>
    internal static async Task SetLinesAsync(
        TinapayanRMSDbContext dbContext,
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        ProductRules.EnsureValidPackagingLines(
            [.. lines.Select(l => (l.PackagingMaterialId, ProductRules.ReconcileReciprocal(l.AuthoredField, l.AuthoredValue).QtyPerUnit))]);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new BusinessRuleException($"Product {productId} does not exist.");
        }

        var isLinked = await dbContext.PackagingLinks.AnyAsync(l => l.ProductId == productId, cancellationToken);
        if (isLinked)
        {
            throw new BusinessRuleException("This product's packaging is linked to another product's — unlink it before editing its lines directly.");
        }

        var materialIds = lines.Select(l => l.PackagingMaterialId).ToList();
        var materials = await dbContext.RawMaterials
            .Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!materials.TryGetValue(line.PackagingMaterialId, out var material))
            {
                throw new BusinessRuleException($"Packaging material {line.PackagingMaterialId} does not exist.");
            }

            RawMaterialRules.EnsureUsableAsPackaging(material.Type, material.Name);
        }

        var unitIds = lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value).Distinct().ToList();
        var materialIdByUnitId = await dbContext.RawMaterialUnits
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.RawMaterialId, cancellationToken);

        foreach (var line in lines)
        {
            if (line.UnitId is { } unitId
                && (!materialIdByUnitId.TryGetValue(unitId, out var unitMaterialId) || unitMaterialId != line.PackagingMaterialId))
            {
                throw new BusinessRuleException("A packaging line's unit must be one of that material's own units.");
            }
        }

        await dbContext.PackagingLines
            .Where(l => l.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.PackagingLines.AddRange(lines.Select((l, index) =>
        {
            var (qtyPerUnit, unitsPerPackagingUnit) = ProductRules.ReconcileReciprocal(l.AuthoredField, l.AuthoredValue);
            return new PackagingLine
            {
                ProductId = productId,
                PackagingMaterialId = l.PackagingMaterialId,
                UnitId = l.UnitId,
                QtyPerUnit = qtyPerUnit,
                UnitsPerPackagingUnit = unitsPerPackagingUnit,
                AuthoredField = l.AuthoredField,
                SortOrder = index,
            };
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
