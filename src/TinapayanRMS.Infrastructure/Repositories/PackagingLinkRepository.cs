using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

public class PackagingLinkRepository(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IPackagingLineRepository packagingLineRepository) : IPackagingLinkRepository
{
    // ProductId is unique, so there's at most one link.
    public Task<PackagingLink?> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(
            async dbContext => (await dbContext.Connection().QueryAsync<PackagingLink, Product?, SubAssembly?, PackagingLink>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<PackagingLink>("l")}, {Columns.Of<Product>("p")}, {Columns.Of<SubAssembly>("s")}
                    FROM PackagingLinks l
                    LEFT JOIN Products p ON p.Id = l.LinkedProductId
                    LEFT JOIN SubAssemblies s ON s.Id = l.LinkedSubAssemblyId
                    WHERE l.ProductId = @productId
                    """,
                    new { productId },
                    cancellationToken),
                (link, linkedProduct, linkedSet) =>
                {
                    link.LinkedProduct = linkedProduct;
                    link.LinkedSubAssembly = linkedSet;
                    return link;
                },
                splitOn: "Id,Id")).SingleOrDefault(),
            cancellationToken);

    public async Task SetSubAssemblyLinkAsync(int productId, int subAssemblyId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureProductExistsAsync(dbContext, productId, cancellationToken);
        await SetSetLinkAsync(dbContext, productId, subAssemblyId, cancellationToken);
    }

    public async Task SetLinkAsync(int productId, int linkedProductId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureProductExistsAsync(dbContext, productId, cancellationToken);
        await SetProductLinkAsync(dbContext, productId, linkedProductId, cancellationToken);
    }

    public async Task RemoveLinkAsync(int productId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var link = await dbContext.PackagingLinks.FirstOrDefaultAsync(l => l.ProductId == productId, cancellationToken);
        if (link is null)
        {
            return;
        }

        // Freeze what the packaging currently shows (resolved through the link) into the product's own
        // lines, then drop the link — never leaves less data behind.
        var effectiveLines = await packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken);
        dbContext.PackagingLinks.Remove(link);
        await dbContext.SaveChangesAsync(cancellationToken);

        await packagingLineRepository.SetLinesAsync(
            productId,
            [.. effectiveLines.Select(l => (l.PackagingMaterialId, l.UnitId, l.AuthoredField,
                l.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? l.QtyPerUnit : l.UnitsPerPackagingUnit))],
            cancellationToken);
    }

    /// <summary>
    /// Makes <paramref name="productId"/>'s packaging link what <paramref name="link"/> says, on the caller's context and
    /// saved: another product's packaging, a packaging set, or (<see cref="PackagingLinkDraft.None"/>) no link at all.
    /// Removing a link here drops it and nothing more: unlike RemoveLinkAsync, the caller writes whatever lines the
    /// product owns from then on (ProductRepository.SaveAsync, whose form already holds them).
    /// </summary>
    internal static async Task SetAsync(TinapayanRMSDbContext dbContext, int productId, PackagingLinkDraft link, CancellationToken cancellationToken)
    {
        if (link.LinkedSubAssemblyId is { } setId)
        {
            await SetSetLinkAsync(dbContext, productId, setId, cancellationToken);
        }
        else if (link.LinkedProductId is { } linkedProductId)
        {
            await SetProductLinkAsync(dbContext, productId, linkedProductId, cancellationToken);
        }
        else
        {
            await dbContext.PackagingLinks.Where(l => l.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <summary>Links the product's packaging to another product's, after the self-link, existence and cycle checks.</summary>
    private static async Task SetProductLinkAsync(TinapayanRMSDbContext dbContext, int productId, int linkedProductId, CancellationToken cancellationToken)
    {
        PackagingLinkRules.EnsureNotSelfLink(productId, linkedProductId);

        var linkedProductExists = await dbContext.Products.AnyAsync(p => p.Id == linkedProductId, cancellationToken);
        if (!linkedProductExists)
        {
            throw new InvalidOperationException($"Product {linkedProductId} does not exist.");
        }

        var links = await dbContext.PackagingLinks.ToDictionaryAsync(l => l.ProductId, l => l.LinkedProductId, cancellationToken);
        PackagingLinkRules.EnsureNoCycle(productId, linkedProductId, id => links.TryGetValue(id, out var next) ? next : null);

        var existing = await dbContext.PackagingLinks.FirstOrDefaultAsync(l => l.ProductId == productId, cancellationToken);
        if (existing is null)
        {
            dbContext.PackagingLinks.Add(new PackagingLink { ProductId = productId, LinkedProductId = linkedProductId });
        }
        else
        {
            existing.LinkedProductId = linkedProductId;
            existing.LinkedSubAssemblyId = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Links the product's packaging to a standalone packaging set, which is all it can link among sub-assemblies.</summary>
    private static async Task SetSetLinkAsync(TinapayanRMSDbContext dbContext, int productId, int subAssemblyId, CancellationToken cancellationToken)
    {
        var set = await dbContext.SubAssemblies
            .Where(s => s.Id == subAssemblyId)
            .Select(s => new { s.OwnerProductId, s.Role!.IsPackaging })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Sub-assembly {subAssemblyId} does not exist.");
        if (set.OwnerProductId is not null || !set.IsPackaging)
        {
            throw new InvalidOperationException("A product's packaging can only link a packaging set from the Sub-Assemblies screen.");
        }

        // A set links nothing itself, so no chain runs through it and there's nothing to cycle.
        var existing = await dbContext.PackagingLinks.FirstOrDefaultAsync(l => l.ProductId == productId, cancellationToken);
        if (existing is null)
        {
            dbContext.PackagingLinks.Add(new PackagingLink { ProductId = productId, LinkedSubAssemblyId = subAssemblyId });
        }
        else
        {
            existing.LinkedProductId = null;
            existing.LinkedSubAssemblyId = subAssemblyId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureProductExistsAsync(TinapayanRMSDbContext dbContext, int productId, CancellationToken cancellationToken)
    {
        var productExists = await dbContext.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException($"Product {productId} does not exist.");
        }
    }
}
