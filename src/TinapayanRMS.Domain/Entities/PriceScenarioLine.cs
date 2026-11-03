namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One price change in a <see cref="PriceScenario"/>: a purchased material and what was typed for it, a change in percent
/// or a new price per its purchase unit. Exactly one of the two is stored, so a scenario opened after the price has moved
/// applies a percentage to the price then, and keeps a typed price as it was.
/// </summary>
public sealed class PriceScenarioLine
{
    public int Id { get; set; }

    public int PriceScenarioId { get; set; }

    public PriceScenario? PriceScenario { get; set; }

    /// <summary>Deleting the material takes its lines out of every scenario.</summary>
    public int RawMaterialId { get; set; }

    public RawMaterial? RawMaterial { get; set; }

    /// <summary>The change typed in percent: 10 for a 10% rise. Null when a new price was typed instead.</summary>
    public decimal? ChangePercent { get; set; }

    /// <summary>The new price typed, per the material's purchase unit. Null when a change in percent was typed instead.</summary>
    public decimal? NewPrice { get; set; }

    /// <summary>Where the line sits in the scenario, from 0.</summary>
    public int SortOrder { get; set; }
}
