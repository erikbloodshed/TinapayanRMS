using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class ProductRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductRepository
{
    private static readonly string ProductWithCategorySql = $"""
        SELECT {Columns.Of<Product>("p")}, {Columns.Of<ProductCategory>("c")}
        FROM products p
        LEFT JOIN product_categories c ON c.id = p.category_id
        """;

    public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken) =>
        GetWithCategoryAsync("WHERE p.deleted_at IS NULL\nORDER BY p.name", null, cancellationToken);

    public Task<IReadOnlyList<Product>> GetDeletedAsync(CancellationToken cancellationToken) =>
        GetWithCategoryAsync("WHERE p.deleted_at IS NOT NULL\nORDER BY p.name", null, cancellationToken);

    public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        (await GetWithCategoryAsync("WHERE p.id = @id", new { id }, cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? []
            : await GetWithCategoryAsync($"WHERE p.id IN ({DapperReads.IdsTable})\nORDER BY p.name", new { ids = DapperReads.IdList(ids) }, cancellationToken);

    public Task<IReadOnlyList<ProductComponent>> GetComponentsAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(dbContext => ProductComponentRepository.GetForProductAsync(dbContext, productId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<ProductComponent>> GetComponentsForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(dbContext => ProductComponentRepository.GetForProductsAsync(dbContext, productIds, cancellationToken), cancellationToken);

    public async Task<IReadOnlyDictionary<int, decimal>> GetCategoryWastageForProductsAsync(
        IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        productIds.Count == 0
            ? new Dictionary<int, decimal>()
            : (await dbContextFactory.QueryAsync<CategoryWastageRow>(
                $"""
                SELECT p.id AS product_id, c.wastage_percent
                FROM products p
                JOIN product_categories c ON c.id = p.category_id
                WHERE c.wastage_percent IS NOT NULL AND p.id IN ({DapperReads.IdsTable})
                """,
                new { ids = DapperReads.IdList(productIds) },
                cancellationToken))
            .ToDictionary(r => r.ProductId, r => r.WastagePercent);

    /// <summary>A product whose category sets its own Wastage %.</summary>
    private sealed record CategoryWastageRow(int ProductId, decimal WastagePercent);

    public Task<IReadOnlyList<ProductComponent>> GetAllComponentsAsync(CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<ProductComponent>(
            $"SELECT {Columns.Of<ProductComponent>("pc")} FROM product_components pc ORDER BY pc.product_id, pc.sort_order, pc.id",
            null,
            cancellationToken);

    public Task<IReadOnlyList<int>> GetProductsReadingAsync(
        IReadOnlyCollection<int> subAssemblyIds, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        subAssemblyIds.Count == 0 && productIds.Count == 0
            ? Task.FromResult<IReadOnlyList<int>>([])
            : dbContextFactory.QueryAsync<int>(
                // The app refuses a loop of packaging links (PackagingLinkRules), but one left by an edit outside it would run
                // until the command timed out, and a save waits on this, so chain stops where a product comes round again
                // (CYCLE, as RawMaterialRepository.DependentsSql does).
                """
                WITH RECURSIVE seeds AS (
                    SELECT unnest(@productIds) AS product_id
                ),
                sub_assembly_ids AS (
                    SELECT unnest(@subAssemblyIds) AS id
                    UNION
                    SELECT u.sub_assembly_id FROM product_sub_assembly_usages u JOIN seeds s ON s.product_id = u.product_id
                ),
                readers AS (
                    SELECT product_id FROM seeds
                    UNION
                    SELECT u.product_id FROM product_sub_assembly_usages u JOIN sub_assembly_ids s ON s.id = u.sub_assembly_id
                    UNION
                    SELECT l.product_id FROM packaging_links l JOIN sub_assembly_ids s ON s.id = l.linked_sub_assembly_id
                ),
                chain AS (
                    SELECT product_id FROM readers
                    UNION ALL
                    SELECT l.product_id FROM packaging_links l JOIN chain c ON c.product_id = l.linked_product_id
                ) CYCLE product_id SET is_cycle USING path
                SELECT DISTINCT p.id FROM chain c JOIN products p ON p.id = c.product_id ORDER BY p.id
                """,
                new { subAssemblyIds = DapperReads.IdList(subAssemblyIds), productIds = DapperReads.IdList(productIds) },
                cancellationToken);

    private Task<IReadOnlyList<Product>> GetWithCategoryAsync(string filterAndOrder, object? parameters, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<Product>>(
            async dbContext => (await dbContext.Connection().QueryAsync<Product, ProductCategory?, Product>(
                dbContext.Command($"{ProductWithCategorySql}\n{filterAndOrder}", parameters, cancellationToken),
                (product, category) =>
                {
                    product.Category = category;
                    return product;
                },
                splitOn: "Id")).AsList(),
            cancellationToken);

    public async Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        // A composite holds at least two products from the moment it's saved, and only SaveAsync writes them with it.
        if (product.IsComposite)
        {
            throw new BusinessRuleException("A composite product is saved with its products (SaveAsync).");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AddAsync(dbContext, product, cancellationToken);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        await UpdateAsync(dbContext, product, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task StampSavedAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        List<int> productIds = [.. ids];
        var now = dbContext.LocalNow();
        await dbContext.Products
            .Where(p => productIds.Contains(p.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, now), cancellationToken);
    }

    public Task MoveAsync(IReadOnlyCollection<ProductMove> moves, CancellationToken cancellationToken) =>
        MoveAsync(moves, showing: null, cancellationToken);

    public async Task MoveAsync(IReadOnlyCollection<ProductMove> moves, SavedVersionCheck? showing, CancellationToken cancellationToken)
    {
        if (moves.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // One turn for the check and the write, so no save lands between them.
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var productIds = moves.Select(m => m.ProductId).Distinct().ToList();
        var products = await dbContext.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        if (productIds.Where(id => !products.ContainsKey(id)).ToList() is [var missingId, ..])
        {
            throw new BusinessRuleException($"Product {missingId} does not exist.");
        }

        // Checked rather than left to the foreign key, whose refusal would read as a duplicate name: a move undone after
        // the folder it came from was deleted.
        var folderIds = moves.Select(m => m.FolderId).OfType<int>().Distinct().ToList();
        var folders = await dbContext.ProductFolders.AsNoTracking().Where(f => folderIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        if (folderIds.Any(id => !folders.ContainsKey(id)))
        {
            throw new BusinessRuleException(ProductFolderRules.MissingFolderMessage);
        }

        var now = dbContext.LocalNow();
        foreach (var move in moves)
        {
            if (move.FolderId is { } folderId)
            {
                ProductFolderRepository.EnsureSameStage(folders[folderId].Stage, move.Stage);
            }

            var product = products[move.ProductId];
            SavedVersionRules.EnsureNotSavedSince(showing, product.Id, product.Name, product.UpdatedAt);
            product.Stage = move.Stage;
            product.FolderId = move.FolderId;
            product.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<StageMove> MoveToStageAsync(IReadOnlyCollection<int> productIds, ProductStage stage, CancellationToken cancellationToken) =>
        MoveToStageAsync(productIds, stage, showing: null, cancellationToken);

    public async Task<StageMove> MoveToStageAsync(IReadOnlyCollection<int> productIds, ProductStage stage, SavedVersionCheck? showing, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var ids = productIds.Distinct().ToList();
        var products = await dbContext.Products.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).ToListAsync(cancellationToken);
        HashSet<int> found = [.. products.Select(p => p.Id)];
        if (ids.Where(id => !found.Contains(id)).ToList() is [var missingId, ..])
        {
            throw new BusinessRuleException($"Product {missingId} does not exist.");
        }

        // Each product goes to the folder at its own folder's path in the other stage, made if it isn't there: found once a
        // folder, however many products it holds.
        var folders = await dbContext.ProductFolders.AsNoTracking().ToListAsync(cancellationToken);
        Dictionary<int, int?> folderInStage = [];
        List<int> made = [];
        List<ProductMove> moves = [];
        var now = dbContext.LocalNow();
        foreach (var product in products.Where(p => p.Stage != stage))
        {
            SavedVersionRules.EnsureNotSavedSince(showing, product.Id, product.Name, product.UpdatedAt);
            if (product.FolderId is not { } from || !folderInStage.TryGetValue(from, out var folderId))
            {
                folderId = await ProductFolderRepository.EnsurePathAsync(
                    dbContext, stage, ProductFolderRules.PathOf(folders, product.FolderId), made, cancellationToken);
                if (product.FolderId is { } source)
                {
                    folderInStage[source] = folderId;
                }
            }

            product.Stage = stage;
            product.FolderId = folderId;
            product.UpdatedAt = now;
            moves.Add(new ProductMove(product.Id, stage, folderId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StageMove(moves, made);
    }

    public Task RenameAsync(int productId, string name, CancellationToken cancellationToken) =>
        RenameAsync(productId, name, showing: null, cancellationToken);

    public async Task RenameAsync(int productId, string name, SavedVersionCheck? showing, CancellationToken cancellationToken)
    {
        name = ProductRules.StoredName(name);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var existing = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new BusinessRuleException($"Product {productId} does not exist.");
        SavedVersionRules.EnsureNotSavedSince(showing, productId, existing.Name, existing.UpdatedAt);
        await EnsureNameNotHeldByDeletedAsync(dbContext, productId, name, cancellationToken);

        var oldName = existing.Name;
        existing.Name = name;
        existing.UpdatedAt = dbContext.LocalNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        await ResyncOwnedNamesAsync(dbContext, existing.Id, oldName, existing.Name, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<int> SaveAsync(
        Product product,
        RecipeDraft? recipe,
        PackagingLinkDraft? packagingLink,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        CostingParameters? costing,
        CancellationToken cancellationToken) =>
        SaveAsync(product, recipe, packagingLink, packagingLines, bulkPackaging: null, costing, cancellationToken);

    public async Task<int> SaveAsync(
        Product product,
        RecipeDraft? recipe,
        PackagingLinkDraft? packagingLink,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        BulkPackagingDraft? bulkPackaging,
        CostingParameters? costing,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // One transaction for every part, so a part that's rejected leaves nothing half-saved. Each
        // step saves as it goes, so the checks of the steps after it see what it wrote (a new product's
        // id, above all).
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);

        // A copy of the form's fields: the caller's entity is never attached, and never given an id by
        // a save that then rolls back. The stage and folder are only a new product's: a saved one's change by moving it.
        // So is whether it's a composite, which a saved one keeps (UpdateAsync refuses another).
        var fields = new Product
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            CategoryId = product.CategoryId,
            QuantityRequired = product.QuantityRequired,
            CurrentPrice = product.CurrentPrice,
            Stage = product.Stage,
            FolderId = product.FolderId,
            IsComposite = product.IsComposite,
            IncludesComponentPackaging = product.IncludesComponentPackaging,

            // When the product the form opened had last been saved, for UpdateAsync's check (SavedVersionRules).
            UpdatedAt = product.UpdatedAt,
        };
        var stored = fields.Id == 0
            ? await AddAsync(dbContext, fields, cancellationToken)
            : await UpdateAsync(dbContext, fields, cancellationToken);

        // A composite's recipe is its products, and a new one saved with none still has to hold two; its own sections are
        // only in product-owned roles, a topping or a filling.
        if (stored.IsComposite)
        {
            if (recipe is not null || product.Id == 0)
            {
                await ProductComponentRepository.SetComponentsAsync(dbContext, stored, recipe?.Components ?? [], cancellationToken);
            }

            if (recipe is not null)
            {
                await ProductComponentRepository.EnsureSectionRolesAsync(dbContext, stored.Name, recipe, cancellationToken);
                await ProductSubAssemblyUsageRepository.SetRecipeAsync(dbContext, fields.Id, recipe, cancellationToken);
            }
        }
        else if (recipe is not null)
        {
            // Refuses products in a recipe of sub-assemblies.
            await ProductComponentRepository.SetComponentsAsync(dbContext, stored, recipe.Components, cancellationToken);
            await ProductSubAssemblyUsageRepository.SetRecipeAsync(dbContext, fields.Id, recipe, cancellationToken);
        }

        // Move to Sub-Assemblies: the lines go to a new packaging set, which the packaging links in their place. After
        // the recipe, so the set's name is kept apart from the sub-assemblies this save has just made.
        if (packagingLink is PackagingLinkDraft.NewSet)
        {
            await PackagingLinkRepository.LinkNewSetAsync(dbContext, fields.Id, packagingLines ?? [], cancellationToken);
        }
        else
        {
            // Before the lines: SetLinesAsync refuses a linked product's, so an unlink has to land first.
            if (packagingLink is not null)
            {
                await PackagingLinkRepository.SetAsync(dbContext, fields.Id, packagingLink, cancellationToken);
            }

            if (packagingLines is not null)
            {
                await PackagingLineRepository.SetLinesAsync(dbContext, fields.Id, packagingLines, cancellationToken);
            }
        }

        // The bulk packaging is the product's own whatever its packaging links, so it's written after the link, apart from
        // the lines, with how many units a pack holds: none without lines.
        if (bulkPackaging is not null)
        {
            ProductRules.EnsureValidBulkPackaging(bulkPackaging.Lines.Count, bulkPackaging.Quantity);
            await PackagingLineRepository.SetBulkLinesAsync(dbContext, fields.Id, bulkPackaging.Lines, cancellationToken);
            stored.BulkPackagingQuantity = bulkPackaging.Lines.Count > 0 ? bulkPackaging.Quantity : null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (costing is not null)
        {
            await CostingParametersRepository.SetAsync(dbContext, fields.Id, costing, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return fields.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new BusinessRuleException($"Product {id} does not exist.");

        // The same refusals as a permanent delete: what restoring it would bring back has to still make sense, so nothing
        // that leans on it can be left leaning on a deleted product.
        await EnsureNothingLeansOnAsync(dbContext, id, cancellationToken);

        product.DeletedAt = dbContext.LocalNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new BusinessRuleException($"Product {id} does not exist.");

        // Whole as it was deleted: everything it uses was held for it, and its category, if deleted since, has left it
        // uncategorized (SetNull).
        product.DeletedAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeletePermanentlyAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureNothingLeansOnAsync(dbContext, id, cancellationToken);

        // The product's cascades take its own sub-assemblies, its packaging lines, its usages with their add-ins, a
        // composite's lines, and its costing parameters with it. The ones it links from the Sub-Assemblies screen stay
        // there, and so do the products a composite holds.
        await dbContext.Products.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Refuses deleting product <paramref name="id"/>, to the Deleted folder or for good, while another product links its
    /// packaging or one of its own sub-assemblies (a variant), or a composite holds it, deleted or not, naming them.
    /// </summary>
    private static async Task EnsureNothingLeansOnAsync(TinapayanRMSDbContext dbContext, int id, CancellationToken cancellationToken)
    {
        var packagingLinkedBy = await dbContext.PackagingLinks
            .Where(l => l.LinkedProductId == id)
            .Select(l => new { l.Product!.Name, l.Product.DeletedAt })
            .ToListAsync(cancellationToken);
        if (packagingLinkedBy.Count > 0)
        {
            throw new BusinessRuleException(
                $"{ListedNames(packagingLinkedBy.Select(p => (p.Name, p.DeletedAt)))} "
                + $"{(packagingLinkedBy.Count == 1 ? "links" : "link")} this product's packaging. Unlink it there first.");
        }

        // Checked rather than left to the foreign key, whose refusal would read as something else, and so the message can
        // name the composites.
        var heldBy = await dbContext.ProductComponents
            .Where(c => c.ComponentProductId == id)
            .Select(c => new { c.Product!.Name, c.Product.DeletedAt })
            .ToListAsync(cancellationToken);
        if (heldBy.Count > 0)
        {
            throw new BusinessRuleException(
                $"{ListedNames(heldBy.Select(p => (p.Name, p.DeletedAt)))} "
                + $"{(heldBy.Count == 1 ? "holds" : "hold")} this product. Take it out there first.");
        }

        // Its own sub-assemblies go with it for good (Cascade), so none can be left linked by a variant.
        await SubAssemblyRepository.EnsureNoneLinkedElsewhereAsync(
            dbContext, u => u.SubAssembly!.OwnerProductId == id && u.ProductId != id, cancellationToken);

        static string ListedNames(IEnumerable<(string Name, DateTime? DeletedAt)> products) =>
            string.Join(", ", products
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => ProductRules.ListedName(p.Name, p.DeletedAt is not null)));
    }

    /// <summary>
    /// Refuses <paramref name="name"/> for product <paramref name="id"/> (0 for a new one) while a deleted product has it,
    /// before the unique index would, so the message can say where that one is.
    /// </summary>
    internal static async Task EnsureNameNotHeldByDeletedAsync(TinapayanRMSDbContext dbContext, int id, string name, CancellationToken cancellationToken) =>
        ProductRules.EnsureNameNotHeldByDeleted(
            name, await dbContext.Products.AnyAsync(p => p.Id != id && p.Name == name && p.DeletedAt != null, cancellationToken));

    /// <summary>
    /// Adds <paramref name="product"/>, stamped as saved now, and saves it: the product gets its id. Returns it, tracked.
    /// A composite is one sellable unit. Filed in a folder, it's in that folder's stage; a folder deleted meanwhile files it
    /// at the top of its stage.
    /// </summary>
    internal static async Task<Product> AddAsync(TinapayanRMSDbContext dbContext, Product product, CancellationToken cancellationToken)
    {
        product.Name = ProductRules.StoredName(product.Name);
        if (product.FolderId is { } folderId)
        {
            var folderStage = await dbContext.ProductFolders
                .Where(f => f.Id == folderId)
                .Select(f => (ProductStage?)f.Stage)
                .FirstOrDefaultAsync(cancellationToken);
            product.FolderId = folderStage is null ? null : folderId;
            product.Stage = folderStage ?? product.Stage;
        }

        CompositeProductRules.EnsureOneUnit(product.IsComposite, product.QuantityRequired);
        CompositeProductRules.EnsureComponentPackagingOnComposite(product.IsComposite, product.IncludesComponentPackaging);
        await EnsureNameNotHeldByDeletedAsync(dbContext, 0, product.Name, cancellationToken);
        product.UpdatedAt = dbContext.LocalNow();
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return product;
    }

    /// <summary>
    /// Copies the name, description, category, quantity required, current price and whether a composite's products bring
    /// their packaging onto the stored product, stamps it as saved now, and saves it. Returns the stored product, tracked.
    /// Whether it's a composite can't change, and a composite stays one sellable unit.
    /// </summary>
    internal static async Task<Product> UpdateAsync(TinapayanRMSDbContext dbContext, Product product, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == product.Id, cancellationToken)
            ?? throw new BusinessRuleException($"Product {product.Id} does not exist.");

        // Not over a save made since the form opened it, by another PC: product.UpdatedAt is the time it opened, if it says.
        SavedVersionRules.EnsureNotSavedSince(existing.Name, existing.UpdatedAt, product.UpdatedAt);

        CompositeProductRules.EnsureKindUnchanged(existing.IsComposite, product.IsComposite);
        CompositeProductRules.EnsureOneUnit(existing.IsComposite, product.QuantityRequired);
        CompositeProductRules.EnsureComponentPackagingOnComposite(existing.IsComposite, product.IncludesComponentPackaging);
        var name = ProductRules.StoredName(product.Name);
        await EnsureNameNotHeldByDeletedAsync(dbContext, existing.Id, name, cancellationToken);

        var oldName = existing.Name;
        existing.Name = name;
        existing.Description = product.Description;
        existing.CategoryId = product.CategoryId;
        existing.QuantityRequired = product.QuantityRequired;
        existing.CurrentPrice = product.CurrentPrice;
        existing.IncludesComponentPackaging = product.IncludesComponentPackaging;
        existing.UpdatedAt = dbContext.LocalNow();

        await dbContext.SaveChangesAsync(cancellationToken);
        await ResyncOwnedNamesAsync(dbContext, existing.Id, oldName, existing.Name, cancellationToken);
        return existing;
    }

    /// <summary>
    /// Its own sub-assemblies named after it, "Cheese Roll Filling: Cheese", numbered or not, take its new name
    /// (SubAssemblyRules.IsNamedAfter); one named otherwise keeps its name. Run on the caller's context, which should hold
    /// a transaction.
    /// </summary>
    private static async Task ResyncOwnedNamesAsync(
        TinapayanRMSDbContext dbContext, int productId, string oldName, string newName, CancellationToken cancellationToken)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return;
        }

        var owned = await dbContext.SubAssemblies
            .Include(s => s.Role)
            .Where(s => s.OwnerProductId == productId)
            .ToListAsync(cancellationToken);
        await SubAssemblyRepository.ResyncOwnedNamesAsync(
            dbContext,
            owned,
            s => SubAssemblyRules.IsNamedAfter(s.Name, oldName, s.Role!.Name, s.SectionName)
                ? SubAssemblyRules.StandaloneNameFor(newName, s.Role.Name, s.SectionName)
                : s.Name,
            cancellationToken);
    }
}
