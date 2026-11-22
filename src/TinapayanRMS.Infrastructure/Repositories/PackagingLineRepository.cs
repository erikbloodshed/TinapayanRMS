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
        var (lines, bulk) = await ResolveAsync(dbContext, productIds, withLines: true, withBulk: true, looping, cancellationToken);
        return new EffectivePackaging(lines, looping) { Bulk = bulk };
    }

    /// <summary><see cref="ResolveEffectiveAsync(TinapayanRMSDbContext, IReadOnlyCollection{int}, CancellationToken, ISet{int})"/> for one product.</summary>
    internal static async Task<IReadOnlyList<PackagingLine>> ResolveEffectiveAsync(
        TinapayanRMSDbContext dbContext, int productId, CancellationToken cancellationToken) =>
        (await ResolveEffectiveAsync(dbContext, [productId], cancellationToken))[productId];

    /// <summary>
    /// Each product's packaging per piece as it shows, by product id: its link chain (if any) is followed to an unlinked
    /// product, whose own lines it shows, or to a packaging set, whose lines it shows as its own (<see cref="SetLinesAsPackagingLines"/>).
    /// Bulk packaging (PackagingLine.IsBulk) is never among them: <see cref="ReadBulkAsync"/> reads it. A chain that comes
    /// round again, which only an edit outside the app can leave (SetLinkAsync refuses one), is refused, as a write that
    /// copies the lines should be (ProductCloner, RemoveLinkAsync); given <paramref name="looping"/>, as a read of many
    /// products is, its product shows no lines and goes in it instead, so one bad chain doesn't fail every product's read
    /// (Reports, Print, the Products list, the servers). Reads through Dapper, in <paramref name="dbContext"/>'s transaction
    /// if it has one (ProductCloner's and RemoveLinkAsync's do).
    /// </summary>
    internal static async Task<IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>>> ResolveEffectiveAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken, ISet<int>? looping = null) =>
        (await ResolveAsync(dbContext, productIds, withLines: true, withBulk: false, looping, cancellationToken)).Lines;

    /// <summary>
    /// Each product's bulk packaging (PackagingLine.IsBulk), by product id, each line's quantity per bulk pack: the lines of
    /// the packaging set its bulk packaging links (PackagingLink.IsBulk), or its own, whatever its packaging per piece links.
    /// A product with none has an empty list.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>>> ReadBulkAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        (await ResolveAsync(dbContext, productIds, withLines: false, withBulk: true, looping: null, cancellationToken)).Bulk;

    /// <summary>
    /// The packaging per piece (<paramref name="withLines"/>) and the bulk packaging (<paramref name="withBulk"/>) of each
    /// product, as <see cref="ResolveEffectiveAsync(TinapayanRMSDbContext, IReadOnlyCollection{int}, CancellationToken, ISet{int})"/>
    /// and <see cref="ReadBulkAsync"/> say, the kind not asked for empty. Every link is read in one query and followed in
    /// memory, then the lines at the chains' ends and the bulk lines of the products' own in a query each, and every set's
    /// lines, per piece or bulk, in one, however many products there are and however long their chains.
    /// </summary>
    private static async Task<(IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>> Lines, IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>> Bulk)> ResolveAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> productIds, bool withLines, bool withBulk, ISet<int>? looping, CancellationToken cancellationToken)
    {
        // Two rows a product at most, so every link is a small read, and a chain costs nothing more to follow. Only the
        // links of the packaging per piece make chains: a bulk packaging's links a set.
        var allLinks = await dbContext.QueryAsync<PackagingLink>(
            $"SELECT {Columns.Of<PackagingLink>("l")} FROM packaging_links l", null, cancellationToken);
        var links = allLinks.Where(l => !l.IsBulk).ToDictionary(l => l.ProductId);
        var bulkSetByProduct = allLinks
            .Where(l => l.IsBulk && l.LinkedSubAssemblyId is not null)
            .ToDictionary(l => l.ProductId, l => l.LinkedSubAssemblyId!.Value);
        var ids = productIds.Distinct().ToList();

        var chainEnds = withLines ? ids.ToDictionary(id => id, id => ChainEnd(id, links)) : [];
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
        var bulkOwnerIds = withBulk ? ids.Where(id => !bulkSetByProduct.ContainsKey(id)).ToList() : [];
        List<int> setIds =
        [
            .. ends.Values.Select(end => end.SetId).OfType<int>()
                .Concat(withBulk ? ids.Where(bulkSetByProduct.ContainsKey).Select(id => bulkSetByProduct[id]) : [])
                .Distinct(),
        ];

        var ownLines = ownerIds.Count == 0
            ? []
            : await RawMaterialReads.GetPackagingLinesAsync(
                dbContext, $"WHERE l.product_id IN ({DapperReads.IdsTable}) AND NOT l.is_bulk", new { ids = DapperReads.IdList(ownerIds) }, cancellationToken);
        var setLines = setIds.Count == 0 ? [] : await RawMaterialReads.GetSetLinesAsync(dbContext, setIds, cancellationToken);
        var ownBulkLines = bulkOwnerIds.Count == 0
            ? []
            : await RawMaterialReads.GetPackagingLinesAsync(
                dbContext, $"WHERE l.product_id IN ({DapperReads.IdsTable}) AND l.is_bulk", new { ids = DapperReads.IdList(bulkOwnerIds) }, cancellationToken);

        var ownLinesByProduct = ownLines.ToLookup(l => l.ProductId);
        var setLinesBySet = setLines.ToLookup(l => l.SubAssemblyId);
        var ownBulkByProduct = ownBulkLines.ToLookup(l => l.ProductId);
        IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>> lines = chainEnds.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                null => [],
                { SetId: { } setId } end => SetLinesAsPackagingLines(end.ProductId, setLinesBySet[setId], isBulk: false),
                var end => (IReadOnlyList<PackagingLine>)[.. ownLinesByProduct[end.Value.ProductId]],
            });
        IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>> bulk = !withBulk
            ? new Dictionary<int, IReadOnlyList<PackagingLine>>()
            : ids.ToDictionary(
                id => id,
                id => bulkSetByProduct.TryGetValue(id, out var setId)
                    ? SetLinesAsPackagingLines(id, setLinesBySet[setId], isBulk: true)
                    : (IReadOnlyList<PackagingLine>)[.. ownBulkByProduct[id]]);
        return (lines, bulk);
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
    /// sellable unit, so a set's quantity is that unit's, in the unit the set line was entered in, typed as the quantity;
    /// for its bulk packaging (<paramref name="isBulk"/>), one set per bulk pack, so it's the pack's.
    /// Everything that reads a product's packaging (the Products screen, Reports, unlinking's copy, a clone)
    /// takes them as they are.
    /// </summary>
    private static IReadOnlyList<PackagingLine> SetLinesAsPackagingLines(int productId, IEnumerable<SubAssemblyPackagingLine> setLines, bool isBulk) =>
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
                    IsBulk = isBulk,
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
        var isLinked = await dbContext.PackagingLinks.AnyAsync(l => l.ProductId == productId && !l.IsBulk, cancellationToken);
        if (isLinked)
        {
            throw new BusinessRuleException("This product's packaging is linked to another product's — unlink it before editing its lines directly.");
        }

        await ReplaceLinesAsync(dbContext, productId, lines, isBulk: false, cancellationToken);
    }

    /// <summary>
    /// Replaces the product's bulk packaging lines (PackagingLine.IsBulk), each quantity per bulk pack, on the caller's
    /// context and saves them, after the checks SetLinesAsync makes on a line. Its other lines and its links stay as they
    /// are. Lines are refused while its bulk packaging links a set, whose lines it shows instead.
    /// </summary>
    internal static async Task SetBulkLinesAsync(
        TinapayanRMSDbContext dbContext,
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        if (lines.Count > 0 && await dbContext.PackagingLinks.AnyAsync(l => l.ProductId == productId && l.IsBulk, cancellationToken))
        {
            throw new BusinessRuleException("This product's bulk packaging is linked to a packaging set — unlink it before giving it lines of its own.");
        }

        await ReplaceLinesAsync(dbContext, productId, lines, isBulk: true, cancellationToken);
    }

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

        await LineChecks.EnsureUsablePackagingAsync(dbContext, [.. lines.Select(l => (l.PackagingMaterialId, l.UnitId))], "packaging line", cancellationToken);

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
