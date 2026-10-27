using Dapper;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Queries;

/// <summary>
/// Sub-assemblies with their role, which decides the recipe section they show in, and the product owning one (its id
/// and name), if a product does: a variant links its source's own, and names the source as where it's kept.
/// </summary>
internal static class SubAssemblyReads
{
    /// <summary>The columns of <c>s</c>, its role <c>r</c> and its owner <c>o</c>, in the order <see cref="WithRole"/> maps them.</summary>
    public static readonly string ColumnsWithRole = $"{Columns.Of<SubAssembly>("s")}, {Columns.Of<SubAssemblyRole>("r")}, o.id, o.name";

    /// <summary>Joins <c>s</c>'s role, and its owner if it has one.</summary>
    public const string RoleJoin = "JOIN sub_assembly_roles r ON r.id = s.role_id\nLEFT JOIN products o ON o.id = s.owner_product_id";

    /// <summary>The sub-assemblies <paramref name="filterAndOrder"/> (a WHERE and ORDER BY over <c>s</c> and <c>r</c>) selects.</summary>
    public static async Task<IReadOnlyList<SubAssembly>> GetWithRoleAsync(
        TinapayanRMSDbContext dbContext, string filterAndOrder, object? parameters, CancellationToken cancellationToken) =>
        (await dbContext.Connection().QueryAsync<SubAssembly, SubAssemblyRole, Product?, SubAssembly>(
            dbContext.Command($"SELECT {ColumnsWithRole}\nFROM sub_assemblies s\n{RoleJoin}\n{filterAndOrder}", parameters, cancellationToken),
            WithRole,
            splitOn: "Id,Id")).AsList();

    public static SubAssembly WithRole(SubAssembly subAssembly, SubAssemblyRole role, Product? owner)
    {
        subAssembly.Role = role;
        subAssembly.OwnerProduct = owner;
        return subAssembly;
    }
}
