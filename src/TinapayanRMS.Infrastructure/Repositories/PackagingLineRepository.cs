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
            dbContext => RawMaterialReads.GetPackagingLinesAsync(
                dbContext, "WHERE l.product_id = @productId AND NOT l.is_bulk", new { productId }, cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyList<PackagingLine>> GetEffectiveForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await ResolveEffectiveAsync(dbContext, productId, cancellationToken);
    }

    public async Task<EffectivePackaging> GetEffectiveForProductsAsync(
        IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        HashSet<int> looping = [];
        var lines = await ResolveEffectiveAsync(dbContext, productIds, cancellationToken, looping);
        var bulk = await ReadBulkAsync(dbContext, productIds, cancellationToken);
        return new EffectivePackaging(lines, looping) { Bulk = bulk };
    }

    /// <summary><see cref="ResolveEffectiveAsync(TinapayanRMSDbContext, IReadOnlyCollection{int}, CancellationToken, ISet{int})"/> for one product.</summary>
    internal static async Task<IReadOnlyList<PackagingLine>> ResolveEffectiveAsync(
        TinapayanRMSDbContext dbContext, int productId, CancellationToken cancellationToken) =>
        (await ResolveEffectiveAsync(dbContext, [productId], cancellationToken))[productId];

    /// <summary>
    /// Each product's packaging as it shows, by product id: its link chain (if any) is followed to an unlinked product,
    /// whose own lines it shows, or to a packaging set, whose lines it shows as its own (<see cref="SetLinesAsPackagingLines"/>).
    /// Bulk packaging (PackagingLine.IsBulk) is never among them: it's each product's own, read by <see cref="ReadBulkAsync"/>.
    /// Every link is read in one query and followed in memory, then the lines at the chains' ends in one query for
    /// products and one for sets, however many products and however long their chains. A chain that comes round again,
    /// which only an edit outside the app can leave (SetLinkAsync refuses one), is refused, as a write that copies the
    /// lines should be (ProductCloner, RemoveLinkAsync); given <paramref name="looping"/>, as a read of many products is,
    /// its product shows no lines and goes in it instead, so one bad chain doesn't fail every product's read (Reports,
    /// Print, the Products list, the servers). Reads through Dapper, in <paramref name="dbContext"/>'s transaction if it has one
    /// (ProductCloner's and RemoveLinkAsync's do).
    /// </summary>
    internal static async Task<IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>>> ResolveEffectiveAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken, ISet<int>? looping = null)
    {
        // One row a product at most, so every link is a small read, and a chain costs nothing more to follow.
        var links = (await dbContext.QueryAsync<PackagingLink>(
                $"SELECT {Columns.Of<PackagingLink>("l")} FROM packaging_links l", null, cancellationToken))
            .ToDictionary(l => l.ProductId);

        var chainEnds = productIds.Distinct().ToDictionary(id => id, id => ChainEnd(id, links));
        foreach (var id in chainEnds.Where(e => e.Value is null).Select(e => e.Key))
        {
            if (looping is null)
            {
                throw new BusinessRuleException(PackagingLinkRules.LoopText);
            }

            looping.Add(id);
        }

        var ends = chainEnds.Where(e => e.Value is not null).ToDictionary(e => e.Key, e => e.Value!.Value);
        var ownerIds = ends.Values.Where(end => end.SetId is null).Select(end => end.ProductId).Distinct().ToList();
        var setIds = ends.Values.Select(end => end.SetId).OfType<int>().Distinct().ToList();

        var ownLines = ownerIds.Count == 0
            ? []
            : await RawMaterialReads.GetPackagingLinesAsync(
                dbContext, $"WHERE l.product_id IN ({DapperReads.IdsTable}) AND NOT l.is_bulk", new { ids = DapperReads.IdList(ownerIds) }, cancellationToken);
        var setLines = setIds.Count == 0 ? [] : await RawMaterialReads.GetSetLinesAsync(dbContext, setIds, cancellationToken);

        var ownLinesByProduct = ownLines.ToLookup(l => l.ProductId);
        var setLinesBySet = setLines.ToLookup(l => l.SubAssemblyId);
        return chainEnds.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                null => [],
                { SetId: { } setId } end => SetLinesAsPackagingLines(end.ProductId, setLinesBySet[setId]),
                var end => (IReadOnlyList<PackagingLine>)[.. ownLinesByProduct[end.Value.ProductId]],
            });
    }

    /// <summary>
    /// Each product's bulk packaging (PackagingLine.IsBulk), by product id, in one query: its own, whatever its packaging
    /// links, each line's quantity per bulk pack. A product with none has an empty list.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>>> ReadBulkAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToList();
        var lines = ids.Count == 0
            ? []
            : await RawMaterialReads.GetPackagingLinesAsync(
                dbContext, $"WHERE l.product_id IN ({DapperReads.IdsTable}) AND l.is_bulk", new { ids = DapperReads.IdList(ids) }, cancellationToken);
        var byProduct = lines.ToLookup(l => l.ProductId);
        return ids.ToDictionary(id => id, id => (IReadOnlyList<PackagingLine>)[.. byProduct[id]]);
    }

    /// <summary>
    /// Where <paramref name="productId"/>'s link chain ends: at the product whose own lines it shows, or at the set the
    /// last product in it links (SetId), with that product; null for a chain that comes round again.
    /// </summary>
    private static (int ProductId, int? SetId)? ChainEnd(int productId, Dictionary<int, PackagingLink> links)
    {
        HashSet<int> visited = [];
        while (true)
        {
            if (!visited.Add(productId))
            {
                return null;
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
    /// SetLinesAsync makes. Its bulk packaging stays as it is.
    /// </summary>
    internal static async Task SetLinesAsync(
        TinapayanRMSDbContext dbContext,
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        var isLinked = await dbContext.PackagingLinks.AnyAsync(l => l.ProductId == productId, cancellationToken);
        if (isLinked)
        {
            throw new BusinessRuleException("This product's packaging is linked to another product's — unlink it before editing its lines directly.");
        }

        await ReplaceLinesAsync(dbContext, productId, lines, isBulk: false, cancellationToken);
    }

    /// <summary>
    /// Replaces the product's bulk packaging lines (PackagingLine.IsBulk), each quantity per bulk pack, on the caller's
    /// context and saves them, after the checks SetLinesAsync makes on a line. Its other lines and its link stay as they are.
    /// </summary>
    internal static Task SetBulkLinesAsync(
        TinapayanRMSDbContext dbContext,
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken) =>
        ReplaceLinesAsync(dbContext, productId, lines, isBulk: true, cancellationToken);

    /// <summary>
    /// Checks <paramref name="lines"/> (each a packaging material, in one of its own units, at a positive quantity, no
    /// material twice) and puts them in place of the product's lines of the same kind, bulk or not.
    /// </summary>
    private static async Task ReplaceLinesAsync(
        TinapayanRMSDbContext dbContext,
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        bool isBulk,
        CancellationToken cancellationToken)
    {
        ProductRules.EnsureValidPackagingLines(
            [.. lines.Select(l => (l.PackagingMaterialId, ProductRules.ReconcileReciprocal(l.AuthoredField, l.AuthoredValue).QtyPerUnit))]);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new BusinessRuleException($"Product {productId} does not exist.");
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
            .Where(l => l.ProductId == productId && l.IsBulk == isBulk)
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
                IsBulk = isBulk,
            };
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
