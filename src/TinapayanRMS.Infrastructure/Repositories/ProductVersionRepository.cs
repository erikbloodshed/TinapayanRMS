using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class ProductVersionRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IProductVersionRepository
{
    public Task<IReadOnlyList<ProductVersion>> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<ProductVersion>(
            $"SELECT {Columns.Of<ProductVersion>("v")} FROM product_versions v WHERE v.product_id = @productId ORDER BY v.number DESC",
            new { productId },
            cancellationToken);

    public Task<IReadOnlyList<ProductVersion>> GetLatestAsOfAsync(DateTime asOf, CancellationToken cancellationToken) =>
        dbContextFactory.QueryAsync<ProductVersion>(
            $"SELECT DISTINCT ON (v.product_id) {Columns.Of<ProductVersion>("v")} FROM product_versions v WHERE v.saved_at <= @asOf ORDER BY v.product_id, v.number DESC",
            new { asOf },
            cancellationToken);

    public Task<bool> HasAnyAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.QuerySingleOrDefaultAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM product_versions v WHERE v.product_id = @productId)",
            new { productId },
            cancellationToken);

    public async Task<ProductVersion?> AddAsync(
        int productId, ProductVersionContent content, DateTime savedAt, string? note, CancellationToken cancellationToken)
    {
        var normalizedNote = ProductVersionRules.NormalizeNote(note);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Serializable, so two Saves recording at once can't both read the same latest and take the same number.
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        if (!await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            throw new BusinessRuleException($"Product {productId} does not exist.");
        }

        var latest = await dbContext.ProductVersions
            .Where(v => v.ProductId == productId)
            .OrderByDescending(v => v.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (Next(latest, new ProductVersionDraft(productId, content, savedAt, normalizedNote)) is not { } version)
        {
            return null;
        }

        dbContext.ProductVersions.Add(version);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return version;
    }

    public async Task<IReadOnlyList<ProductVersion?>> AddRangeAsync(IReadOnlyList<ProductVersionDraft> drafts, CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return [];
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Serializable, as AddAsync's. Every product's latest comes in one read, however many products there are
        // (2026-10-17: a call of AddAsync each was 209 commands for a sub-assembly 40 products link).
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        List<int> productIds = [.. drafts.Select(d => d.ProductId).Distinct()];
        var latestByProduct = await dbContext.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                Latest = dbContext.ProductVersions.Where(v => v.ProductId == p.Id).OrderByDescending(v => v.Number).FirstOrDefault(),
            })
            .ToDictionaryAsync(p => p.Id, p => p.Latest, cancellationToken);

        List<ProductVersion?> recorded = [];
        foreach (var draft in drafts)
        {
            if (!latestByProduct.TryGetValue(draft.ProductId, out var latest))
            {
                recorded.Add(null);
                continue;
            }

            if (latest is null && draft.Baseline is { } baseline)
            {
                latest = Next(null, baseline with { ProductId = draft.ProductId });
                dbContext.ProductVersions.Add(latest!);
            }

            var version = Next(latest, draft);
            if (version is not null)
            {
                dbContext.ProductVersions.Add(version);
                latestByProduct[draft.ProductId] = version;
            }
            else
            {
                latestByProduct[draft.ProductId] = latest;
            }

            recorded.Add(version);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return recorded;
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

    /// <summary>
    /// <paramref name="draft"/> as the version after <paramref name="latest"/>, or null when it holds the same recipe and
    /// price. Refused when its note is too long.
    /// </summary>
    private static ProductVersion? Next(ProductVersion? latest, ProductVersionDraft draft) =>
        latest is not null && IsSameRecipe(latest, draft.Content)
            ? null
            : new ProductVersion
            {
                ProductId = draft.ProductId,
                Number = (latest?.Number ?? 0) + 1,
                SavedAt = draft.SavedAt,
                Note = ProductVersionRules.NormalizeNote(draft.Note),
                Content = draft.Content.ToJson(),
            };

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
