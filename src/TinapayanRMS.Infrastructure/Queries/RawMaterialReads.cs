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
        JOIN raw_material_categories c ON c.id = m.category_id
        LEFT JOIN units_of_measure b ON b.id = m.base_unit_id
        """;

    private static readonly string UnitSelect = $"""
        SELECT {Columns.Of<RawMaterialUnit>("u")}, {Columns.Of<UnitOfMeasure>("um")}
        FROM raw_material_units u
        JOIN units_of_measure um ON um.id = u.unit_of_measure_id
        """;

    private const string UnitOrder = "ORDER BY u.base_units_per_unit DESC, u.id";

    /// <summary>
    /// The materials <paramref name="filterAndOrder"/> (a WHERE and ORDER BY over <c>m</c>) selects, with
    /// their details.
    /// </summary>
    public static async Task<IReadOnlyList<RawMaterial>> GetWithDetailsAsync(
        TinapayanRMSDbContext dbContext, string filterAndOrder, object? parameters, CancellationToken cancellationToken)
    {
        var materials = (await dbContext.Connection().QueryAsync<RawMaterial, RawMaterialCategory, UnitOfMeasure?, RawMaterial>(
            dbContext.Command($"SELECT {MaterialColumns}\nFROM raw_materials m\n{MaterialJoins}\n{filterAndOrder}", parameters, cancellationToken),
            WithDetails,
            splitOn: "Id,Id")).AsList();

        await AttachUnitsAsync(dbContext, materials, cancellationToken);
        return materials;
    }

    /// <summary>A material's alternative units, largest first, each with its unit of measure.</summary>
    public static async Task<IReadOnlyList<RawMaterialUnit>> GetUnitsAsync(
        TinapayanRMSDbContext dbContext, int rawMaterialId, CancellationToken cancellationToken) =>
        (await dbContext.Connection().QueryAsync<RawMaterialUnit, UnitOfMeasure, RawMaterialUnit>(
            dbContext.Command($"{UnitSelect}\nWHERE u.raw_material_id = @rawMaterialId\n{UnitOrder}", new { rawMaterialId }, cancellationToken),
            WithUnitOfMeasure,
            splitOn: "Id")).AsList();

    /// <summary>
    /// The product packaging lines <paramref name="filter"/> (a WHERE over <c>l</c>) selects, in the order
    /// the user put them in: each with its material's details, and the unit the line is entered in.
    /// </summary>
    public static Task<IReadOnlyList<PackagingLine>> GetPackagingLinesAsync(
        TinapayanRMSDbContext dbContext, string filter, object? parameters, CancellationToken cancellationToken) =>
        ReadLinesAsync<PackagingLine>(
            dbContext,
            "packaging_lines",
            filter,
            "ORDER BY l.sort_order, l.id",
            parameters,
            (line, material, unit) =>
            {
                line.PackagingMaterial = material;
                line.Unit = unit;
            },
            cancellationToken);

    /// <summary>
    /// A packaging set's lines, in the order the user put them in: each with its material's details, and the unit
    /// the line is entered in.
    /// </summary>
    public static Task<IReadOnlyList<SubAssemblyPackagingLine>> GetSetLinesAsync(
        TinapayanRMSDbContext dbContext, int subAssemblyId, CancellationToken cancellationToken) =>
        GetSetLinesAsync(dbContext, "WHERE l.sub_assembly_id = @subAssemblyId", new { subAssemblyId }, cancellationToken);

    /// <summary>Several packaging sets' lines at once, each set's in the order the user put them in, as GetSetLinesAsync reads one set's.</summary>
    public static Task<IReadOnlyList<SubAssemblyPackagingLine>> GetSetLinesAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> subAssemblyIds, CancellationToken cancellationToken) =>
        GetSetLinesAsync(
            dbContext, $"WHERE l.sub_assembly_id IN ({DapperReads.IdsTable})", new { ids = DapperReads.IdList(subAssemblyIds) }, cancellationToken);

    private static Task<IReadOnlyList<SubAssemblyPackagingLine>> GetSetLinesAsync(
        TinapayanRMSDbContext dbContext, string filter, object parameters, CancellationToken cancellationToken) =>
        ReadLinesAsync<SubAssemblyPackagingLine>(
            dbContext,
            "sub_assembly_packaging_lines",
            filter,
            "ORDER BY l.sub_assembly_id, l.sort_order, l.id",
            parameters,
            (line, material, unit) =>
            {
                line.PackagingMaterial = material;
                line.Unit = unit;
            },
            cancellationToken);

    /// <summary>
    /// The lines of <paramref name="table"/> that <paramref name="filter"/> (a WHERE over <c>l</c>) selects, in
    /// <paramref name="order"/>: a product's packaging lines and a packaging set's are two tables of one shape, a
    /// material and the unit it's entered in, so one query reads both, and a column or join it needs is added once.
    /// <paramref name="attach"/> gives a line its material, with the material's details, and its unit, null for the
    /// material's base unit.
    /// </summary>
    private static async Task<IReadOnlyList<TLine>> ReadLinesAsync<TLine>(
        TinapayanRMSDbContext dbContext,
        string table,
        string filter,
        string order,
        object? parameters,
        Action<TLine, RawMaterial, RawMaterialUnit?> attach,
        CancellationToken cancellationToken)
    {
        List<RawMaterial> materials = [];
        var lines = (await dbContext.Connection().QueryAsync<TLine, RawMaterial, RawMaterialCategory, UnitOfMeasure?, RawMaterialUnit?, UnitOfMeasure?, TLine>(
            dbContext.Command(
                $"""
                SELECT {Columns.Of<TLine>("l")}, {MaterialColumns}, {Columns.Of<RawMaterialUnit>("lu")}, {Columns.Of<UnitOfMeasure>("lum")}
                FROM {table} l
                JOIN raw_materials m ON m.id = l.packaging_material_id
                {MaterialJoins}
                LEFT JOIN raw_material_units lu ON lu.id = l.unit_id
                LEFT JOIN units_of_measure lum ON lum.id = lu.unit_of_measure_id
                {filter}
                {order}
                """,
                parameters,
                cancellationToken),
            (line, material, category, baseUnit, unit, unitOfMeasure) =>
            {
                materials.Add(WithDetails(material, category, baseUnit));
                attach(line, material, unit is null ? null : WithUnitOfMeasure(unit, unitOfMeasure!));
                return line;
            },
            splitOn: "Id,Id,Id,Id,Id")).AsList();

        // Each line's material, in the lines' order: AsList has run the mapping above for every row by now.
        await AttachUnitsAsync(dbContext, materials, cancellationToken);
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
                $"{UnitSelect}\nWHERE u.raw_material_id IN ({DapperReads.IdsTable})\n{UnitOrder}",
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
