using Dapper;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Queries;

/// <summary>
/// Materials as the screens show them — each with its category, base unit, and alternative units (largest
/// first) — alone or as the material of a packaging line. The units are a second query stitched on by
/// material id: GridReader can't multi-map asynchronously, and a join would repeat every row once per unit.
/// </summary>
internal static class RawMaterialReads
{
    private static readonly string MaterialColumns =
        $"{Columns.Of<RawMaterial>("m")}, {Columns.Of<RawMaterialCategory>("c")}, {Columns.Of<UnitOfMeasure>("b")}";

    private const string MaterialJoins = """
        JOIN RawMaterialCategories c ON c.Id = m.CategoryId
        LEFT JOIN UnitsOfMeasure b ON b.Id = m.BaseUnitId
        """;

    private static readonly string UnitSelect = $"""
        SELECT {Columns.Of<RawMaterialUnit>("u")}, {Columns.Of<UnitOfMeasure>("um")}
        FROM RawMaterialUnits u
        JOIN UnitsOfMeasure um ON um.Id = u.UnitOfMeasureId
        """;

    private const string UnitOrder = "ORDER BY u.BaseUnitsPerUnit DESC, u.Id";

    /// <summary>
    /// The materials <paramref name="filterAndOrder"/> (a WHERE and ORDER BY over <c>m</c>) selects, with
    /// their details.
    /// </summary>
    public static async Task<IReadOnlyList<RawMaterial>> GetWithDetailsAsync(
        TinapayanRMSDbContext dbContext, string filterAndOrder, object? parameters, CancellationToken cancellationToken)
    {
        var materials = (await dbContext.Connection().QueryAsync<RawMaterial, RawMaterialCategory, UnitOfMeasure?, RawMaterial>(
            dbContext.Command($"SELECT {MaterialColumns}\nFROM RawMaterials m\n{MaterialJoins}\n{filterAndOrder}", parameters, cancellationToken),
            WithDetails,
            splitOn: "Id,Id")).AsList();

        await AttachUnitsAsync(dbContext, materials, cancellationToken);
        return materials;
    }

    /// <summary>A material's alternative units, largest first, each with its unit of measure.</summary>
    public static async Task<IReadOnlyList<RawMaterialUnit>> GetUnitsAsync(
        TinapayanRMSDbContext dbContext, int rawMaterialId, CancellationToken cancellationToken) =>
        (await dbContext.Connection().QueryAsync<RawMaterialUnit, UnitOfMeasure, RawMaterialUnit>(
            dbContext.Command($"{UnitSelect}\nWHERE u.RawMaterialId = @rawMaterialId\n{UnitOrder}", new { rawMaterialId }, cancellationToken),
            WithUnitOfMeasure,
            splitOn: "Id")).AsList();

    /// <summary>
    /// The product packaging lines <paramref name="filter"/> (a WHERE over <c>l</c>) selects, in the order
    /// they were added: each with its material's details, and the unit the line is entered in.
    /// </summary>
    public static async Task<IReadOnlyList<PackagingLine>> GetPackagingLinesAsync(
        TinapayanRMSDbContext dbContext, string filter, object? parameters, CancellationToken cancellationToken)
    {
        var lines = (await dbContext.Connection().QueryAsync<PackagingLine, RawMaterial, RawMaterialCategory, UnitOfMeasure?, RawMaterialUnit?, UnitOfMeasure?, PackagingLine>(
            dbContext.Command(
                $"""
                SELECT {Columns.Of<PackagingLine>("l")}, {MaterialColumns}, {Columns.Of<RawMaterialUnit>("lu")}, {Columns.Of<UnitOfMeasure>("lum")}
                FROM PackagingLines l
                JOIN RawMaterials m ON m.Id = l.PackagingMaterialId
                {MaterialJoins}
                LEFT JOIN RawMaterialUnits lu ON lu.Id = l.UnitId
                LEFT JOIN UnitsOfMeasure lum ON lum.Id = lu.UnitOfMeasureId
                {filter}
                ORDER BY l.Id
                """,
                parameters,
                cancellationToken),
            (line, material, category, baseUnit, unit, unitOfMeasure) =>
            {
                line.PackagingMaterial = WithDetails(material, category, baseUnit);
                line.Unit = unit is null ? null : WithUnitOfMeasure(unit, unitOfMeasure!);
                return line;
            },
            splitOn: "Id,Id,Id,Id,Id")).AsList();

        await AttachUnitsAsync(dbContext, [.. lines.Select(l => l.PackagingMaterial!)], cancellationToken);
        return lines;
    }

    /// <summary>
    /// A packaging set's lines, in the order they were added: each with its material's details, and the unit
    /// the line is entered in.
    /// </summary>
    public static async Task<IReadOnlyList<SubAssemblyPackagingLine>> GetSetLinesAsync(
        TinapayanRMSDbContext dbContext, int subAssemblyId, CancellationToken cancellationToken)
    {
        var lines = (await dbContext.Connection().QueryAsync<SubAssemblyPackagingLine, RawMaterial, RawMaterialCategory, UnitOfMeasure?, RawMaterialUnit?, UnitOfMeasure?, SubAssemblyPackagingLine>(
            dbContext.Command(
                $"""
                SELECT {Columns.Of<SubAssemblyPackagingLine>("l")}, {MaterialColumns}, {Columns.Of<RawMaterialUnit>("lu")}, {Columns.Of<UnitOfMeasure>("lum")}
                FROM SubAssemblyPackagingLines l
                JOIN RawMaterials m ON m.Id = l.PackagingMaterialId
                {MaterialJoins}
                LEFT JOIN RawMaterialUnits lu ON lu.Id = l.UnitId
                LEFT JOIN UnitsOfMeasure lum ON lum.Id = lu.UnitOfMeasureId
                WHERE l.SubAssemblyId = @subAssemblyId
                ORDER BY l.Id
                """,
                new { subAssemblyId },
                cancellationToken),
            (line, material, category, baseUnit, unit, unitOfMeasure) =>
            {
                line.PackagingMaterial = WithDetails(material, category, baseUnit);
                line.Unit = unit is null ? null : WithUnitOfMeasure(unit, unitOfMeasure!);
                return line;
            },
            splitOn: "Id,Id,Id,Id,Id")).AsList();

        await AttachUnitsAsync(dbContext, [.. lines.Select(l => l.PackagingMaterial!)], cancellationToken);
        return lines;
    }

    private static RawMaterial WithDetails(RawMaterial material, RawMaterialCategory category, UnitOfMeasure? baseUnit)
    {
        material.Category = category;
        material.BaseUnit = baseUnit;
        return material;
    }

    private static RawMaterialUnit WithUnitOfMeasure(RawMaterialUnit unit, UnitOfMeasure unitOfMeasure)
    {
        unit.UnitOfMeasure = unitOfMeasure;
        return unit;
    }

    /// <summary>
    /// Gives each material its alternative units. A material on several lines is one object per line, as an
    /// untracked EF query returns it, and each gets its own list.
    /// </summary>
    private static async Task AttachUnitsAsync(TinapayanRMSDbContext dbContext, List<RawMaterial> materials, CancellationToken cancellationToken)
    {
        if (materials.Count == 0)
        {
            return;
        }

        var units = await dbContext.Connection().QueryAsync<RawMaterialUnit, UnitOfMeasure, RawMaterialUnit>(
            dbContext.Command(
                $"{UnitSelect}\nWHERE u.RawMaterialId IN ({DapperReads.IdsTable})\n{UnitOrder}",
                new { ids = DapperReads.IdList(materials.Select(m => m.Id).Distinct()) },
                cancellationToken),
            WithUnitOfMeasure,
            splitOn: "Id");

        var unitsByMaterial = units.ToLookup(u => u.RawMaterialId);
        foreach (var material in materials)
        {
            material.Units = [.. unitsByMaterial[material.Id]];
        }
    }
}
