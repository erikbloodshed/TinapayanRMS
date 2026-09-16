namespace TinapayanRMS.Domain.Enums;

/// <summary>
/// Where a raw material's cost comes from: its price history, or (ingredients only) a bill of materials,
/// or a share of another ingredient's cost (egg white and egg yolk from the egg).
/// </summary>
public enum RawMaterialSource
{
    Purchased,
    Made,
    Linked,
}
