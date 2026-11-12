using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class ProductFolderRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductFolderRepository
{
    public Task<IReadOnlyList<ProductFolder>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<ProductFolder>(
            $"SELECT {Columns.Of<ProductFolder>("f")} FROM product_folders f ORDER BY f.name, f.id", null, cancellationToken);

    public async Task AddAsync(ProductFolder folder, CancellationToken cancellationToken)
    {
        var name = ProductFolderRules.StoredName(folder.Name);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var folders = await dbContext.ProductFolders.AsNoTracking().ToListAsync(cancellationToken);

        // Inside a folder, it's in that folder's stage.
        var stage = folder.ParentId is { } parentId
            ? (folders.FirstOrDefault(f => f.Id == parentId) ?? throw new BusinessRuleException("The folder to add it in no longer exists.")).Stage
            : folder.Stage;
        await EnsureNameFreeAsync(dbContext, folders, stage, folder.ParentId, name, exceptId: 0, cancellationToken);

        var added = new ProductFolder { Name = name, Stage = stage, ParentId = folder.ParentId };
        dbContext.ProductFolders.Add(added);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        folder.Id = added.Id;
        folder.Name = name;
        folder.Stage = stage;
    }

    public async Task RenameAsync(int id, string name, CancellationToken cancellationToken)
    {
        name = ProductFolderRules.StoredName(name);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var folders = await dbContext.ProductFolders.ToListAsync(cancellationToken);
        var folder = folders.FirstOrDefault(f => f.Id == id) ?? throw Missing(id);
        await EnsureNameFreeAsync(dbContext, folders, folder.Stage, folder.ParentId, name, id, cancellationToken);

        folder.Name = name;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> MoveAsync(ProductFolderMove move, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var folders = await dbContext.ProductFolders.ToListAsync(cancellationToken);
        var folder = folders.FirstOrDefault(f => f.Id == move.FolderId) ?? throw Missing(move.FolderId);
        if (move.ParentId is { } parentId)
        {
            var parent = folders.FirstOrDefault(f => f.Id == parentId)
                ?? throw new BusinessRuleException(ProductFolderRules.MissingFolderMessage);
            EnsureSameStage(parent.Stage, move.Stage);
        }

        ProductFolderRules.EnsureNotInsideItself(folders, folder.Id, move.ParentId);
        if (folder.Stage == move.Stage && folder.ParentId == move.ParentId)
        {
            return [];
        }

        await EnsureNameFreeAsync(dbContext, folders, move.Stage, move.ParentId, folder.Name, folder.Id, cancellationToken);

        var subtree = ProductFolderRules.SubtreeIds(folders, folder.Id).ToList();
        IReadOnlyList<int> restaged = [];
        if (folder.Stage != move.Stage)
        {
            // Everything in it goes to the other stage with it: its folders, and their products, deleted ones too, so
            // each product stays in its folder's stage.
            foreach (var inside in folders.Where(f => subtree.Contains(f.Id)))
            {
                inside.Stage = move.Stage;
            }

            restaged = await dbContext.Products
                .Where(p => p.FolderId != null && subtree.Contains(p.FolderId.Value))
                .OrderBy(p => p.Id)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
            var now = dbContext.LocalNow();
            await dbContext.Products
                .Where(p => restaged.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stage, move.Stage).SetProperty(p => p.UpdatedAt, now), cancellationToken);
        }

        folder.ParentId = move.ParentId;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return restaged;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var folders = await dbContext.ProductFolders.ToListAsync(cancellationToken);
        var folder = folders.FirstOrDefault(f => f.Id == id) ?? throw Missing(id);

        // What it holds goes up a level, so each of its folders' names has to be free there.
        var children = folders.Where(f => f.ParentId == id).ToList();
        foreach (var child in children)
        {
            await EnsureNameFreeAsync(dbContext, folders, folder.Stage, folder.ParentId, child.Name, folder.Id, cancellationToken);
        }

        // A folder in it may have its name (Breads in Breads): the unique index is checked row by row, so the folder going
        // gives up its name before the one in it takes its place.
        folder.Name = $"(deleting {Guid.NewGuid():N})";
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var child in children)
        {
            child.ParentId = folder.ParentId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Products
            .Where(p => p.FolderId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.FolderId, folder.ParentId), cancellationToken);

        dbContext.ProductFolders.Remove(folder);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteIfEmptyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var remaining = ids.Distinct().ToList();
        while (true)
        {
            var empty = await dbContext.ProductFolders
                .Where(f => remaining.Contains(f.Id)
                    && !dbContext.ProductFolders.Any(c => c.ParentId == f.Id)
                    && !dbContext.Products.Any(p => p.FolderId == f.Id))
                .Select(f => f.Id)
                .ToListAsync(cancellationToken);
            if (empty.Count == 0)
            {
                break;
            }

            await dbContext.ProductFolders.Where(f => empty.Contains(f.Id)).ExecuteDeleteAsync(cancellationToken);
            remaining = [.. remaining.Except(empty)];
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The folder at <paramref name="names"/>' path from the top of <paramref name="stage"/> (Breads, then Holiday in it),
    /// each folder on the way found by its name, whatever its case, or added and its id put in <paramref name="made"/>.
    /// Null, the top of the stage, for no names. Saves on the caller's context, which should hold a transaction: Promote,
    /// Send Back, Clone and Create Variant file a product in the other stage at the path it had.
    /// </summary>
    internal static async Task<int?> EnsurePathAsync(
        TinapayanRMSDbContext dbContext, ProductStage stage, IReadOnlyList<string> names, List<int> made, CancellationToken cancellationToken)
    {
        int? parentId = null;
        foreach (var name in names)
        {
            var existing = await dbContext.ProductFolders
                .Where(f => f.Stage == stage && f.ParentId == parentId && f.Name == name)
                .Select(f => (int?)f.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is null)
            {
                var folder = new ProductFolder { Name = name, Stage = stage, ParentId = parentId };
                dbContext.ProductFolders.Add(folder);
                await dbContext.SaveChangesAsync(cancellationToken);
                made.Add(folder.Id);
                existing = folder.Id;
            }

            parentId = existing;
        }

        return parentId;
    }

    /// <summary>
    /// Refuses <paramref name="name"/> where another folder beside it has it (the database's collation deciding, so whatever
    /// its case), before the unique index would, so the message can say where.
    /// </summary>
    private static async Task EnsureNameFreeAsync(
        TinapayanRMSDbContext dbContext,
        IReadOnlyList<ProductFolder> folders,
        ProductStage stage,
        int? parentId,
        string name,
        int exceptId,
        CancellationToken cancellationToken)
    {
        if (await dbContext.ProductFolders.AnyAsync(
                f => f.Id != exceptId && f.Stage == stage && f.ParentId == parentId && f.Name == name, cancellationToken))
        {
            throw ProductFolderRules.NameTaken(ProductFolderRules.PathTitle(folders, stage, parentId), name);
        }
    }

    /// <summary>A folder goes in its parent's stage, and a product in its folder's.</summary>
    internal static void EnsureSameStage(ProductStage folderStage, ProductStage stage)
    {
        if (folderStage != stage)
        {
            throw new BusinessRuleException($"That folder is in {ProductRules.StageTitle(folderStage)}, not {ProductRules.StageTitle(stage)}.");
        }
    }

    private static BusinessRuleException Missing(int id) => new($"Folder {id} does not exist.");
}
