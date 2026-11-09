namespace TinapayanRMS.Domain.Enums;

/// <summary>Where a product stands: still being worked out, or made and sold. The Products list files it under one of the two.</summary>
public enum ProductStage
{
    /// <summary>
    /// Under development, the Products list's Innovation folder. A clone or variant starts here, and a new product when
    /// Settings → Costing Defaults says so. Called ResearchAndDevelopment until 2026-11-09 (ProductStageConverter).
    /// </summary>
    Innovation,

    /// <summary>Made and sold. A new product starts here unless Settings → Costing Defaults says otherwise.</summary>
    Production,
}
