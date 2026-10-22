using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TinapayanRMS.Infrastructure.CompiledModels;

namespace TinapayanRMS.Infrastructure.Tests.CompiledModels;

/// <summary>
/// The app runs on the compiled model in Infrastructure/CompiledModels (made by `dotnet ef dbcontext optimize`), which
/// saves building the model on the first query. Nothing regenerates it when the configurations change, and a stale one
/// still compiles and runs, mapping the model as it was. These fail until it's generated again. No database.
/// </summary>
public class CompiledModelTests
{
    private static TinapayanRMSDbContext CreateDbContext() => new(TinapayanRMSDbContext.CreateOptions("Host=unused"));

    [Fact]
    public void The_context_uses_the_compiled_model()
    {
        using var dbContext = CreateDbContext();

        dbContext.Model.Should().BeSameAs(TinapayanRMSDbContextModel.Instance);
    }

    /// <summary>
    /// The entities, properties, keys, relationships, navigations and indexes. Without their annotations, which the
    /// compiled model holds in another order and without the design-time ones; the next test covers what they map to.
    /// </summary>
    [Fact]
    public void The_compiled_model_has_the_configured_entities()
    {
        using var dbContext = CreateDbContext();
        var configured = dbContext.GetService<IDesignTimeModel>().Model;

        TinapayanRMSDbContextModel.Instance.ToDebugString(MetadataDebugStringOptions.ShortDefault).Should().Be(
            configured.ToDebugString(MetadataDebugStringOptions.ShortDefault), RegenerateIt);
    }

    /// <summary>
    /// The tables each model maps to: columns with their types and nullability, and the keys, foreign keys
    /// and indexes. Listed rather than compared by the migrations differ, which needs design-time data the compiled
    /// model doesn't keep.
    /// </summary>
    [Fact]
    public void The_compiled_model_maps_the_configured_tables()
    {
        using var dbContext = CreateDbContext();
        var configured = dbContext.GetService<IDesignTimeModel>().Model;

        Tables(TinapayanRMSDbContextModel.Instance).Should().Equal(Tables(configured), RegenerateIt);
    }

    private static List<string> Tables(IModel model) =>
    [
        .. model.GetRelationalModel().Tables.OrderBy(t => t.Name).SelectMany(table => (IEnumerable<string>)
        [
            $"table {table.Name}",
            .. table.Columns.OrderBy(c => c.Name)
                .Select(c => $"  column {c.Name} {c.StoreType} {(c.IsNullable ? "null" : "not null")}"),
            .. table.PrimaryKey is { } key ? [$"  primary key {key.Name} ({Names(key.Columns)})"] : Array.Empty<string>(),
            .. table.ForeignKeyConstraints.OrderBy(f => f.Name)
                .Select(f => $"  foreign key {f.Name} ({Names(f.Columns)}) -> {f.PrincipalTable.Name} ({Names(f.PrincipalColumns)}) on delete {f.OnDeleteAction}"),
            .. table.Indexes.OrderBy(i => i.Name)
                .Select(i => $"  index {i.Name} ({Names(i.Columns)}){(i.IsUnique ? " unique" : "")} {i.Filter}"),
        ]),
    ];

    private static string Names(IEnumerable<IColumn> columns) => string.Join(", ", columns.Select(c => c.Name));

    private const string RegenerateIt =
        "the compiled model is generated again after every model change: dotnet ef dbcontext optimize (see the developer manual)";
}
