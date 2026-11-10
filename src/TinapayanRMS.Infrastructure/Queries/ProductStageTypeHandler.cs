using System.Data;
using Dapper;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Configurations;

namespace TinapayanRMS.Infrastructure.Queries;

/// <summary>
/// Reads a <see cref="ProductStage"/> as EF does (<see cref="ProductStageConverter"/>), Innovation's former name included:
/// Dapper's own reading of a string into an enum knows only the names it has, and products and Settings' defaults are
/// read through Dapper. Without it, a stage written by a copy built before the rename failed every read of the products.
/// Dapper uses it for an enum only with SqlMapper.Settings.PreferTypeHandlersForEnums set (DapperReads).
/// </summary>
internal sealed class ProductStageTypeHandler : SqlMapper.TypeHandler<ProductStage>
{
    public override ProductStage Parse(object value) => ProductStageConverter.FromStored((string)value);

    public override void SetValue(IDbDataParameter parameter, ProductStage value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value.ToString();
    }
}
