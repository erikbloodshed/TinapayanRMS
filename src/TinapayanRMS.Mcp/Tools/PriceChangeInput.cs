using System.ComponentModel;

namespace TinapayanRMS.Mcp.Tools;

/// <summary>One price change to try, as a row of the Price Simulator screen's Price Changes: give one of the two.</summary>
public sealed record PriceChangeInput(
    [property: Description("A purchased raw material (ingredient or packaging), by name or a part of it that names only it.")]
    string Material,
    [property: Description("Its new price per its purchase unit (the unit its dearest brand is priced in, e.g. a 25 kg sack), with VAT as entered.")]
    decimal? NewPrice = null,
    [property: Description("Or its change in percent from its current price: 10 for a 10% rise, -5 for a 5% fall.")]
    decimal? ChangePercent = null);
