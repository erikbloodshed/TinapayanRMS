namespace TinapayanRMS.Domain.Enums;

/// <summary>Where a raw material's cost comes from: its price history, or (ingredients only) a bill of materials.</summary>
public enum RawMaterialSource
{
    Purchased,
    Made,
}
