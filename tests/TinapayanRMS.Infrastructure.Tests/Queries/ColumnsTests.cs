using AwesomeAssertions;
using AwesomeAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Tests.Queries;

/// <summary>
/// A Dapper read fills only the columns it selects, so a property missing from Columns would read back as its
/// default without any error. These keep the lists in step with EF's model. No database: the model is built
/// from the configurations alone.
/// </summary>
public class ColumnsTests
{
    private static readonly IModel Model = CreateModel();

    private static IModel CreateModel()
    {
        using var dbContext = new TinapayanRMSDbContext(
            TinapayanRMSDbContext.CreateOptions("Host=unused"));
        return dbContext.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void Every_entity_has_a_column_list()
    {
        Columns.ByEntity.Keys.Should().BeEquivalentTo(Model.GetEntityTypes().Select(e => e.ClrType));
    }

    [Fact]
    public void Each_column_list_is_exactly_its_entitys_columns_with_id_first()
    {
        using var scope = new AssertionScope();
        foreach (var entityType in Model.GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
            var columns = Columns.ByEntity.GetValueOrDefault(entityType.ClrType) ?? [];

            columns.Should().BeEquivalentTo(
                entityType.GetProperties().Select(p => p.GetColumnName(table)), $"Columns lists {entityType.ClrType.Name}'s columns");
            columns.FirstOrDefault().Should().Be("id", $"a multi-mapped read splits {entityType.ClrType.Name} on its id");
        }
    }
}
