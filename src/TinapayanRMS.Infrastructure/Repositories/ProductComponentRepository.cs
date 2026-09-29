using Dapper;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Repositories;

/// <summary>
/// A composite product's lines: which products go into it, and how many of each. They're written with the rest of the
/// product (<see cref="ProductRepository.SaveAsync"/>, ProductCloner) and read through IProductRepository.GetComponentsAsync,
/// as a product's own detail, so there's no repository of their own to inject.
/// </summary>
public static class ProductComponentRepository
{
    private static readonly string ComponentsWithProductSql = $"""
        SELECT {Columns.Of<ProductComponent>("pc")}, {Columns.Of<Product>("p")}, {Columns.Of<ProductCategory>("c")}
        FROM ProductComponents pc
        JOIN Products p ON p.Id = pc.ComponentProductId
        LEFT JOIN ProductCategories c ON c.Id = p.CategoryId
        """;

    /// <summary>
    /// <paramref name="productId"/>'s lines in its Products table's order, each with its product and that product's
    /// category. Through Dapper, in <paramref name="dbContext"/>'s transaction if it has one (ProductCloner's does).
    /// </summary>
    internal static async Task<IReadOnlyList<ProductComponent>> GetForProductAsync(
        TinapayanRMSDbContext dbContext, int productId, CancellationToken cancellationToken) =>
        (await dbContext.Connection().QueryAsync<ProductComponent, Product, ProductCategory?, ProductComponent>(
            dbContext.Command($"{ComponentsWithProductSql}\nWHERE pc.ProductId = @productId\nORDER BY pc.SortOrder, pc.Id", new { productId }, cancellationToken),
            (component, product, category) =>
            {
                product.Category = category;
                component.ComponentProduct = product;
                return component;
            },
            splitOn: "Id,Id")).AsList();

    /// <summary>
    /// Replaces a composite's lines on the caller's context, which should hold a transaction. Only a composite holds
    /// products; it needs at least two different ones, each at a quantity of 1 or more, and none that would leave a
    /// composite containing itself (CompositeProductRules). Nothing is deleted until every line passes. Each line's
    /// SortOrder is its place in <paramref name="components"/>.
    /// </summary>
    /// <param name="product">The product, saved already (it has its id), with its stored IsComposite and Name.</param>
    internal static async Task SetComponentsAsync(
        TinapayanRMSDbContext dbContext,
        Product product,
        IReadOnlyList<(int ComponentProductId, int Quantity)> components,
        CancellationToken cancellationToken)
    {
        if (!product.IsComposite)
        {
            if (components.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{product.Name} is made of sub-assemblies, so it can't hold other products. Make a composite product for that.");
            }

            return;
        }

        var ids = components.Select(c => c.ComponentProductId).Distinct().ToList();
        var names = await dbContext.Products
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        if (ids.FirstOrDefault(id => !names.ContainsKey(id)) is var missingId and not 0)
        {
            throw new InvalidOperationException($"Product {missingId} does not exist.");
        }

        CompositeProductRules.EnsureValidComponents(
            product.Id, [.. components.Select(c => (c.ComponentProductId, c.Quantity, names[c.ComponentProductId]))]);

        // What every other composite holds now; this one's own lines are about to be replaced. The names the message
        // needs beyond these lines' are only ever the product's own.
        var otherLines = await dbContext.ProductComponents
            .Where(c => c.ProductId != product.Id)
            .Select(c => new { c.ProductId, c.ComponentProductId })
            .ToListAsync(cancellationToken);
        var held = otherLines
            .GroupBy(c => c.ProductId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g.Select(c => c.ComponentProductId)]);
        CompositeProductRules.EnsureNoCycle(product.Id, ids, held, id => id == product.Id ? product.Name : names[id]);

        await dbContext.ProductComponents
            .Where(c => c.ProductId == product.Id)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.ProductComponents.AddRange(components.Select((c, index) => new ProductComponent
        {
            ProductId = product.Id,
            ComponentProductId = c.ComponentProductId,
            Quantity = c.Quantity,
            SortOrder = index,
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A composite's sections, new or linked, are all in product-owned roles (CompositeProductRules.EnsureSectionRoles),
    /// read from the database on the caller's context: a new section's role, and a linked one's sub-assembly's.
    /// </summary>
    internal static async Task EnsureSectionRolesAsync(
        TinapayanRMSDbContext dbContext, string compositeName, RecipeDraft recipe, CancellationToken cancellationToken)
    {
        var roleIds = recipe.New.Select(n => n.RoleId).Distinct().ToList();
        var linkedIds = recipe.Linked.Select(l => l.SubAssemblyId).Distinct().ToList();
        if (roleIds.Count == 0 && linkedIds.Count == 0)
        {
            return;
        }

        var roles = await dbContext.SubAssemblyRoles
            .Where(r => roleIds.Contains(r.Id) || dbContext.SubAssemblies.Any(s => linkedIds.Contains(s.Id) && s.RoleId == r.Id))
            .OrderBy(r => r.SortOrder)
            .Select(r => new { r.Name, r.IsProductOwned })
            .ToListAsync(cancellationToken);
        CompositeProductRules.EnsureSectionRoles(compositeName, [.. roles.Select(r => (r.Name, r.IsProductOwned))]);
    }

    /// <summary>
    /// The composites with a section in <paramref name="roleId"/>, by name: while there are any, the role stays
    /// product-owned (SubAssemblyRoleRepository.UpdateAsync). Optionally only through <paramref name="subAssemblyId"/>.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> CompositesUsingRoleAsync(
        TinapayanRMSDbContext dbContext, int roleId, int? subAssemblyId, CancellationToken cancellationToken) =>
        await dbContext.ProductSubAssemblyUsages
            .Where(u => u.Product!.IsComposite
                && u.SubAssembly!.RoleId == roleId
                && (subAssemblyId == null || u.SubAssemblyId == subAssemblyId))
            .Select(u => u.Product!.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);

    /// <summary>"Party Box has" or "Party Box, Pasalubong Box have": the start of a refusal naming composites.</summary>
    internal static string NamesHave(IReadOnlyList<string> names) =>
        $"{string.Join(", ", names)} {(names.Count == 1 ? "has" : "have")}";
}
