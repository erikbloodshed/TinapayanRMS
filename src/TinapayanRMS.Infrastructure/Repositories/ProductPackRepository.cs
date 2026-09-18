using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Repositories;

public class ProductPackRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductPackRepository
{
    public async Task<IReadOnlyList<ProductPack>> GetForProductAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var packs = await dbContext.ProductPacks
            .AsNoTracking()
            .Where(p => p.ProductId == productId)
            .OrderBy(p => p.PiecesPerPack)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        // Two queries and a stitch rather than an Include: the lines need PackagingLineRepository's own
        // include chain (the material with its base and alternative units, and the line's entry unit), and
        // restating it from this end would be a second copy to keep in step.
        var lines = await PackagingLineRepository.WithMaterialAndUnits(dbContext.PackagingLines)
            .Where(l => l.ProductId == productId && l.PackId != null)
            .OrderBy(l => l.Id)
            .ToListAsync(cancellationToken);

        var linesByPack = lines.ToLookup(l => l.PackId!.Value);
        foreach (var pack in packs)
        {
            foreach (var line in linesByPack[pack.Id])
            {
                pack.Lines.Add(line);
            }
        }

        return packs;
    }

    public async Task SetPacksAsync(int productId, IReadOnlyList<ProductPackDraft> packs, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await SetPacksAsync(dbContext, productId, packs, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the product's packs and each pack's lines on the caller's context, which should hold a
    /// transaction. Every check runs before anything is deleted, as SetRecipeAsync's do.
    /// </summary>
    internal static async Task SetPacksAsync(
        TinapayanRMSDbContext dbContext, int productId, IReadOnlyList<ProductPackDraft> packs, CancellationToken cancellationToken)
    {
        ProductRules.EnsureValidPacks([.. packs.Select(p => (p.Name, p.PiecesPerPack))]);

        // Every pack's lines together, so "the same material twice" is judged per pack. A pack being added
        // has no id yet, so its group is told apart by a negative stand-in.
        ProductRules.EnsureValidPackagingLines(
        [
            .. packs.SelectMany((p, i) => p.Lines.Select(l => (
                (int?)(p.Id > 0 ? p.Id : -(i + 1)),
                l.PackagingMaterialId,
                l.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? l.AuthoredValue : 1m / l.AuthoredValue))),
        ]);

        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException($"Product {productId} does not exist.");
        }

        var existing = await dbContext.ProductPacks
            .Where(p => p.ProductId == productId)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var stranger = packs.FirstOrDefault(p => p.Id > 0 && !existing.ContainsKey(p.Id));
        if (stranger is not null)
        {
            throw new InvalidOperationException($"Pack {stranger.Id} isn't one of this product's.");
        }

        HashSet<int> keptIds = [.. packs.Where(p => p.Id > 0).Select(p => p.Id)];
        List<int> removedIds = [.. existing.Keys.Where(id => !keptIds.Contains(id))];
        if (removedIds.Count > 0)
        {
            // The lines go first: PackagingLine.PackId is Restrict, not Cascade — it would be a second
            // cascade path into PackagingLines — so nothing takes a removed pack's lines with it.
            await dbContext.PackagingLines
                .Where(l => l.PackId != null && removedIds.Contains(l.PackId.Value))
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.ProductPacks
                .Where(p => removedIds.Contains(p.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        // Removing first lets a renamed pack take a name another one just gave up, and lets a deleted pack
        // come back as a new row.
        var saved = new List<(ProductPack Pack, ProductPackDraft Draft)>(packs.Count);
        foreach (var draft in packs)
        {
            var pack = draft.Id > 0 ? existing[draft.Id] : new ProductPack { ProductId = productId };
            if (draft.Id == 0)
            {
                dbContext.ProductPacks.Add(pack);
            }

            pack.Name = draft.Name.Trim();
            pack.PiecesPerPack = draft.PiecesPerPack;
            saved.Add((pack, draft));
        }

        // Saved before the lines are written: a pack added here has no id until now, and its lines need it.
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var (pack, draft) in saved)
        {
            await PackagingLineRepository.SetLinesAsync(dbContext, productId, pack.Id, draft.Lines, cancellationToken);
        }
    }
}
