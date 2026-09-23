using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class ProductRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductRepository
{
    private static readonly string ProductWithCategorySql = $"""
        SELECT {Columns.Of<Product>("p")}, {Columns.Of<ProductCategory>("c")}
        FROM Products p
        LEFT JOIN ProductCategories c ON c.Id = p.CategoryId
        """;

    public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken) =>
        GetWithCategoryAsync("ORDER BY p.Name", null, cancellationToken);

    public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        (await GetWithCategoryAsync("WHERE p.Id = @id", new { id }, cancellationToken)).SingleOrDefault();

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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AddAsync(dbContext, product, cancellationToken);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await UpdateAsync(dbContext, product, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> SaveAsync(
        Product product,
        RecipeDraft? recipe,
        PackagingLinkDraft? packagingLink,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        CostingParameters? costing,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // One transaction for every part, so a part that's rejected leaves nothing half-saved. Each
        // step saves as it goes, so the checks of the steps after it see what it wrote (a new product's
        // id, above all).
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // A copy of the form's fields: the caller's entity is never attached, and never given an id by
        // a save that then rolls back.
        var fields = new Product
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            CategoryId = product.CategoryId,
            QuantityRequired = product.QuantityRequired,
            CurrentPrice = product.CurrentPrice,
        };
        if (fields.Id == 0)
        {
            await AddAsync(dbContext, fields, cancellationToken);
        }
        else
        {
            await UpdateAsync(dbContext, fields, cancellationToken);
        }

        if (recipe is not null)
        {
            await ProductSubAssemblyUsageRepository.SetRecipeAsync(dbContext, fields.Id, recipe, cancellationToken);
        }

        // Before the lines: SetLinesAsync refuses a linked product's, so an unlink has to land first.
        if (packagingLink is not null)
        {
            await PackagingLinkRepository.SetAsync(dbContext, fields.Id, packagingLink, cancellationToken);
        }

        if (packagingLines is not null)
        {
            await PackagingLineRepository.SetLinesAsync(dbContext, fields.Id, packagingLines, cancellationToken);
        }

        if (costing is not null)
        {
            await CostingParametersRepository.SetAsync(
                dbContext,
                new CostingParameters
                {
                    ProductId = fields.Id,
                    LaborPercent = costing.LaborPercent,
                    OverheadPercent = costing.OverheadPercent,
                    AdministrativePercent = costing.AdministrativePercent,
                    MarginPercent = costing.MarginPercent,
                    VatPercent = costing.VatPercent,
                },
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return fields.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var packagingLinkedBy = await dbContext.PackagingLinks
            .Where(l => l.LinkedProductId == id)
            .Select(l => l.Product!.Name)
            .ToListAsync(cancellationToken);
        if (packagingLinkedBy.Count > 0)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", packagingLinkedBy.Order(StringComparer.CurrentCultureIgnoreCase))} "
                + $"{(packagingLinkedBy.Count == 1 ? "links" : "link")} this product's packaging. Unlink it there first.");
        }

        // Its own sub-assemblies go with it (Cascade). Nothing else can link one, but should another product's recipe
        // somehow use it, say so rather than let the database refuse.
        await SubAssemblyRepository.EnsureNoneLinkedElsewhereAsync(
            dbContext, u => u.SubAssembly!.OwnerProductId == id && u.ProductId != id, cancellationToken);

        // The product's cascades take its own sub-assemblies, its packaging lines, its usages with their add-ins, and
        // its costing parameters with it. The ones it links from the Sub-Assemblies screen stay there.
        await dbContext.Products.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Adds <paramref name="product"/>, stamped as saved now, and saves it: the product gets its id.</summary>
    internal static async Task AddAsync(TinapayanRMSDbContext dbContext, Product product, CancellationToken cancellationToken)
    {
        product.UpdatedAt = DateTime.Now;
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Copies the name, description, category, quantity required and current price onto the stored product, stamps it as saved now, and saves it.</summary>
    internal static async Task UpdateAsync(TinapayanRMSDbContext dbContext, Product product, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == product.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Product {product.Id} does not exist.");

        var oldName = existing.Name;
        existing.Name = product.Name;
        existing.Description = product.Description;
        existing.CategoryId = product.CategoryId;
        existing.QuantityRequired = product.QuantityRequired;
        existing.CurrentPrice = product.CurrentPrice;
        existing.UpdatedAt = DateTime.Now;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Its own sub-assemblies are named after it, "Cheese Roll Filling: Cheese": they take its new name. Run on the
        // caller's context, which should hold a transaction.
        if (!string.Equals(oldName, existing.Name, StringComparison.Ordinal))
        {
            var owned = await dbContext.SubAssemblies
                .Include(s => s.Role)
                .Where(s => s.OwnerProductId == existing.Id)
                .ToListAsync(cancellationToken);
            await SubAssemblyRepository.ResyncOwnedNamesAsync(
                dbContext,
                owned,
                s => SubAssemblyRules.OwnedNameAfterRename(s.Name, oldName, s.Role!.Name, existing.Name, s.Role.Name),
                cancellationToken);
        }
    }
}
