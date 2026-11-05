using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public sealed class PackagingLinkRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IPackagingLinkRepository
{
    // ProductId is unique, so there's at most one link.
    public async Task<PackagingLink?> GetForProductAsync(int productId, CancellationToken cancellationToken) =>
        (await GetLinksAsync("l.product_id = @productId", new { productId }, cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<PackagingLink>> GetForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken) =>
        productIds.Count == 0
            ? []
            : await GetLinksAsync($"l.product_id IN ({DapperReads.IdsTable})", new { ids = DapperReads.IdList(productIds) }, cancellationToken);

    /// <summary>The links <paramref name="filter"/> picks, each with the product or packaging set it's linked to.</summary>
    private Task<IReadOnlyList<PackagingLink>> GetLinksAsync(string filter, object parameters, CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync<IReadOnlyList<PackagingLink>>(
            async dbContext => (await dbContext.Connection().QueryAsync<PackagingLink, Product?, SubAssembly?, PackagingLink>(
                dbContext.Command(
                    $"""
                    SELECT {Columns.Of<PackagingLink>("l")}, {Columns.Of<Product>("p")}, {Columns.Of<SubAssembly>("s")}
                    FROM packaging_links l
                    LEFT JOIN products p ON p.id = l.linked_product_id
                    LEFT JOIN sub_assemblies s ON s.id = l.linked_sub_assembly_id
                    WHERE {filter}
                    ORDER BY l.product_id
                    """,
                    parameters,
                    cancellationToken),
                (link, linkedProduct, linkedSet) =>
                {
                    link.LinkedProduct = linkedProduct;
                    link.LinkedSubAssembly = linkedSet;
                    return link;
                },
                splitOn: "Id,Id")).AsList(),
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

        // The link goes and the lines it showed take its place in one transaction, so lines refused after the link had gone
        // can't leave the product with no packaging at all.
        await using var transaction = await dbContext.Database.BeginOrJoinTransactionAsync(cancellationToken);
        var link = await dbContext.PackagingLinks.FirstOrDefaultAsync(l => l.ProductId == productId, cancellationToken);
        if (link is null)
        {
            return;
        }

        // Freeze what the packaging currently shows (resolved through the link) into the product's own
        // lines, then drop the link — never leaves less data behind.
        var effectiveLines = await PackagingLineRepository.ResolveEffectiveAsync(dbContext, productId, cancellationToken);
        dbContext.PackagingLinks.Remove(link);
        await dbContext.SaveChangesAsync(cancellationToken);

        await PackagingLineRepository.SetLinesAsync(
            dbContext,
            productId,
            [.. effectiveLines.Select(l => (l.PackagingMaterialId, l.UnitId, l.AuthoredField,
                l.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? l.QtyPerUnit : l.UnitsPerPackagingUnit))],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Makes <paramref name="productId"/>'s packaging link what <paramref name="link"/> says, on the caller's context and
    /// saved: another product's packaging, a packaging set, or (<see cref="PackagingLinkDraft.None"/>) no link at all.
    /// Removing a link here drops it and nothing more: unlike RemoveLinkAsync, the caller writes whatever lines the
    /// product owns from then on (ProductRepository.SaveAsync, whose form already holds them). A new set made of the
    /// product's lines (<see cref="PackagingLinkDraft.NewSet"/>) is linked by <see cref="LinkNewSetAsync"/> instead.
    /// </summary>
    internal static async Task SetAsync(TinapayanRMSDbContext dbContext, int productId, PackagingLinkDraft link, CancellationToken cancellationToken)
    {
        switch (link)
        {
            case PackagingLinkDraft.Set(var setId):
                await SetSetLinkAsync(dbContext, productId, setId, cancellationToken);
                break;

            case PackagingLinkDraft.OtherProduct(var linkedProductId):
                await SetProductLinkAsync(dbContext, productId, linkedProductId, cancellationToken);
                break;

            case PackagingLinkDraft.None:
                await dbContext.PackagingLinks.Where(l => l.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
                break;

            case PackagingLinkDraft.NewSet:
                throw new ArgumentException("A new set is made of the product's lines, so LinkNewSetAsync links it.", nameof(link));
        }
    }

    /// <summary>
    /// Move to Sub-Assemblies on the Products screen's Packaging tab (<see cref="PackagingLinkDraft.NewSet"/>), on the
    /// caller's context, which should hold a transaction: <paramref name="lines"/> become a new packaging set on the
    /// Sub-Assemblies screen, named after the product and the Packaging role as a recipe section moved there is
    /// ("Ensaymada Packaging", numbered when taken), and the product's packaging links it in place of lines of its own.
    /// One set per sellable unit, so each line's quantity for one unit is its quantity for one set, and the product's
    /// packaging reads and costs as it did.
    /// </summary>
    internal static async Task LinkNewSetAsync(
        TinapayanRMSDbContext dbContext,
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken)
    {
        // Read in the caller's transaction, so a product added or renamed by the same save is named as it now is.
        var productName = await dbContext.Products
            .Where(p => p.Id == productId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException($"Product {productId} does not exist.");

        // Known by its flag, never its name: the role can be renamed.
        var role = await dbContext.SubAssemblyRoles.AsNoTracking().FirstOrDefaultAsync(r => r.IsPackaging, cancellationToken)
            ?? throw new BusinessRuleException("There's no Packaging role to keep a packaging set in.");
        var takenNames = await dbContext.SubAssemblies.Select(s => s.Name).ToListAsync(cancellationToken);
        var name = SubAssemblyRules.UniqueStandaloneName(SubAssemblyRules.StandaloneNameFor(productName, role.Name, null), takenNames);

        var setId = await SubAssemblyRepository.SavePackagingSetAsync(
            dbContext,
            new SubAssembly { Name = name, RoleId = role.Id },
            [.. lines.Select(l => (l.PackagingMaterialId, l.UnitId, ProductRules.ReconcileReciprocal(l.AuthoredField, l.AuthoredValue).QtyPerUnit))],
            cancellationToken);

        await dbContext.PackagingLines.Where(l => l.ProductId == productId).ExecuteDeleteAsync(cancellationToken);
        await SetSetLinkAsync(dbContext, productId, setId, cancellationToken);
    }

    /// <summary>Links the product's packaging to another product's, after the self-link, existence and cycle checks.</summary>
    private static async Task SetProductLinkAsync(TinapayanRMSDbContext dbContext, int productId, int linkedProductId, CancellationToken cancellationToken)
    {
        PackagingLinkRules.EnsureNotSelfLink(productId, linkedProductId);

        var linked = await dbContext.Products
            .Where(p => p.Id == linkedProductId)
            .Select(p => new { p.Name, p.DeletedAt })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException($"Product {linkedProductId} does not exist.");
        if (linked.DeletedAt is not null)
        {
            throw new BusinessRuleException($"{linked.Name} is deleted. Restore it from the Deleted folder to link its packaging.");
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

    /// <summary>Links the product's packaging to a packaging set, which is all it can link among sub-assemblies.</summary>
    private static async Task SetSetLinkAsync(TinapayanRMSDbContext dbContext, int productId, int subAssemblyId, CancellationToken cancellationToken)
    {
        var set = await dbContext.SubAssemblies
            .Where(s => s.Id == subAssemblyId)
            .Select(s => new { s.Role!.IsPackaging })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException($"Sub-assembly {subAssemblyId} does not exist.");
        if (!set.IsPackaging)
        {
            throw new BusinessRuleException("A product's packaging can only link a packaging set from the Sub-Assemblies screen.");
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
            throw new BusinessRuleException($"Product {productId} does not exist.");
        }
    }
}
