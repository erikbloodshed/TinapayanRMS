using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class ProductVersionRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductVersionRepository
{
    public Task<IReadOnlyList<ProductVersion>> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<ProductVersion>(
            $"SELECT {Columns.Of<ProductVersion>("v")} FROM ProductVersions v WHERE v.ProductId = @productId ORDER BY v.Number DESC",
            new { productId },
            cancellationToken);

    public async Task<bool> HasAnyAsync(int productId, CancellationToken cancellationToken) =>
        await dbContextFactory.QuerySingleOrDefaultAsync<int>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM ProductVersions v WHERE v.ProductId = @productId) THEN 1 ELSE 0 END",
            new { productId },
            cancellationToken) == 1;

    public async Task<ProductVersion?> AddAsync(
        int productId, ProductVersionContent content, DateTime savedAt, string? note, CancellationToken cancellationToken)
    {
        var normalizedNote = ProductVersionRules.NormalizeNote(note);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Serializable, so two Saves recording at once can't both read the same latest and take the same number.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        if (!await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            throw new BusinessRuleException($"Product {productId} does not exist.");
        }

        var latest = await dbContext.ProductVersions
            .Where(v => v.ProductId == productId)
            .OrderByDescending(v => v.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not null && IsSameRecipe(latest, content))
        {
            return null;
        }

        var version = new ProductVersion
        {
            ProductId = productId,
            Number = (latest?.Number ?? 0) + 1,
            SavedAt = savedAt,
            Note = normalizedNote,
            Content = content.ToJson(),
        };
        dbContext.ProductVersions.Add(version);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return version;
    }

    public async Task SetNoteAsync(int versionId, string? note, CancellationToken cancellationToken)
    {
        var normalizedNote = ProductVersionRules.NormalizeNote(note);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var version = await dbContext.ProductVersions.FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken)
            ?? throw new BusinessRuleException("This version no longer exists.");
        version.Note = normalizedNote;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A latest version that can't be read is never the same: the new one is recorded rather than lost.</summary>
    private static bool IsSameRecipe(ProductVersion latest, ProductVersionContent content)
    {
        try
        {
            return ProductVersionRules.IsSameRecipe(ProductVersionContent.FromJson(latest.Content), content);
        }
        catch (BusinessRuleException)
        {
            return false;
        }
    }
}
