using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;

namespace TinapayanRMS.Mcp.Serialization;

/// <summary>
/// How the tools' results are written for the model: the SDK's own options, with every decimal rounded to
/// <see cref="DecimalPlaces"/> places. Costs are worked out at full precision, 28 digits of which only cost the model's
/// context; a cost per gram is stored to 20 places, but none the shop uses needs more than 6.
/// </summary>
public static class ToolJson
{
    public const int DecimalPlaces = 6;

    public static JsonSerializerOptions Options { get; } = new(McpJsonUtilities.DefaultOptions)
    {
        Converters = { new RoundedDecimalConverter() },
    };

    private sealed class RoundedDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(Math.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero));
    }
}
