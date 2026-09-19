using Dapper;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Queries;

/// <summary>Sub-assemblies with their role and owning product (if any), which is what they're called by.</summary>
internal static class SubAssemblyReads
{
    /// <summary>The columns of <c>s</c>, its role <c>r</c> and its owner <c>o</c>, in the order <see cref="WithRoleAndOwner"/> maps them.</summary>
    public static readonly string ColumnsWithRoleAndOwner =
        $"{Columns.Of<SubAssembly>("s")}, {Columns.Of<SubAssemblyRole>("r")}, {Columns.Of<Product>("o")}";

    /// <summary>Joins <c>s</c>'s role and owner.</summary>
    public const string RoleAndOwnerJoins = """
        JOIN SubAssemblyRoles r ON r.Id = s.RoleId
        LEFT JOIN Products o ON o.Id = s.OwnerProductId
        """;

    /// <summary>The sub-assemblies <paramref name="filterAndOrder"/> (a WHERE and ORDER BY over <c>s</c>, <c>r</c> and <c>o</c>) selects.</summary>
    public static async Task<IReadOnlyList<SubAssembly>> GetWithRoleAndOwnerAsync(
        TinapayanRMSDbContext dbContext, string filterAndOrder, object? parameters, CancellationToken cancellationToken) =>
        (await dbContext.Connection().QueryAsync<SubAssembly, SubAssemblyRole, Product?, SubAssembly>(
            dbContext.Command($"SELECT {ColumnsWithRoleAndOwner}\nFROM SubAssemblies s\n{RoleAndOwnerJoins}\n{filterAndOrder}", parameters, cancellationToken),
            WithRoleAndOwner,
            splitOn: "Id,Id")).AsList();

    public static SubAssembly WithRoleAndOwner(SubAssembly subAssembly, SubAssemblyRole role, Product? owner)
    {
        subAssembly.Role = role;
        subAssembly.OwnerProduct = owner;
        return subAssembly;
    }
}
