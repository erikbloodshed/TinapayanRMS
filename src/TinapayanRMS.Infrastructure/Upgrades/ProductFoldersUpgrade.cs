using Microsoft.EntityFrameworkCore;

namespace TinapayanRMS.Infrastructure.Upgrades;

/// <summary>
/// Files the products in folders as the Products list showed them before folders were their own (2026-11-12), when each
/// category was a folder in each stage: a folder at the top of each stage for each category with products there (not one
/// in the Deleted folder alone), named after it, holding them. A deleted product goes in its category's folder in its
/// stage when there's one, for its restore. Uncategorized products stay at the top of their stage, and an empty category
/// gets no folder. From then on a folder and a category are each their own.
/// </summary>
internal static class ProductFoldersUpgrade
{
    public static async Task RunAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO product_folders (name, stage, parent_id)
            SELECT DISTINCT c.name, p.stage, NULL::integer
            FROM products p
            JOIN product_categories c ON c.id = p.category_id
            WHERE p.deleted_at IS NULL
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE products p
            SET folder_id = f.id
            FROM product_categories c, product_folders f
            WHERE c.id = p.category_id AND f.parent_id IS NULL AND f.stage = p.stage AND f.name = c.name
            """,
            cancellationToken);
    }
}
