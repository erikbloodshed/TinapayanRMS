using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Configurations;

/// <summary>
/// Stores a <see cref="ProductStage"/> by name, as EF's own enum-to-string converter does, and also reads
/// <see cref="ProductRules.FormerInnovationName"/>, Innovation's name before 2026-11-09, as Innovation. The data upgrade
/// "2026-11-09 innovation stage" renamed what was stored then; this reads what a copy of the app, the Recipe API or the
/// MCP server built before the rename writes until it's updated, which would otherwise fail every read of the products.
/// Dapper's reads read it the same way (Queries/ProductStageTypeHandler).
/// </summary>
public sealed class ProductStageConverter() : ValueConverter<ProductStage, string>(
    stage => stage.ToString(),
    name => FromStored(name))
{
    /// <summary>The stage stored as <paramref name="name"/>: its name, or Innovation's former name.</summary>
    public static ProductStage FromStored(string name) =>
        name == ProductRules.FormerInnovationName ? ProductStage.Innovation : Enum.Parse<ProductStage>(name);
}
