using Dapper;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Queries;

/// <summary>Sub-assemblies with their role, which decides the recipe section they show in.</summary>
internal static class SubAssemblyReads
{
    /// <summary>The columns of <c>s</c> and its role <c>r</c>, in the order <see cref="WithRole"/> maps them.</summary>
    public static readonly string ColumnsWithRole = $"{Columns.Of<SubAssembly>("s")}, {Columns.Of<SubAssemblyRole>("r")}";

    /// <summary>Joins <c>s</c>'s role.</summary>
    public const string RoleJoin = "JOIN sub_assembly_roles r ON r.id = s.role_id";

    /// <summary>The sub-assemblies <paramref name="filterAndOrder"/> (a WHERE and ORDER BY over <c>s</c> and <c>r</c>) selects.</summary>
    public static async Task<IReadOnlyList<SubAssembly>> GetWithRoleAsync(
        TinapayanRMSDbContext dbContext, string filterAndOrder, object? parameters, CancellationToken cancellationToken) =>
        (await dbContext.Connection().QueryAsync<SubAssembly, SubAssemblyRole, SubAssembly>(
            dbContext.Command($"SELECT {ColumnsWithRole}\nFROM sub_assemblies s\n{RoleJoin}\n{filterAndOrder}", parameters, cancellationToken),
            WithRole,
            splitOn: "Id")).AsList();

    public static SubAssembly WithRole(SubAssembly subAssembly, SubAssemblyRole role)
    {
        subAssembly.Role = role;
        return subAssembly;
    }
}
