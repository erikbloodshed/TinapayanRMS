namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>What the Usage Example worked out: the calculated (true) cost, and that with the material's buffer on top.</summary>
public sealed record UsageExampleResult(
    string Title,
    decimal QuantityInBaseUnits,
    string BaseUnitAbbreviation,
    decimal CostPerBaseUnit,
    decimal TotalCost,
    decimal BufferPercent,
    decimal BufferedTotalCost);
