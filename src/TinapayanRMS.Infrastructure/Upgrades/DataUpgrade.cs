namespace TinapayanRMS.Infrastructure.Upgrades;

/// <summary>A one-time change to the data (DataUpgrades) that has been made on this database: its name, and when.</summary>
public sealed class DataUpgrade
{
    /// <summary>Its name, such as "2026-11-05 section names".</summary>
    public string Id { get; set; } = string.Empty;

    public DateTime AppliedAt { get; set; }
}
