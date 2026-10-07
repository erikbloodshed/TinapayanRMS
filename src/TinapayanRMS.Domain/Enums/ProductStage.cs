namespace TinapayanRMS.Domain.Enums;

/// <summary>Where a product stands: still being worked out, or made and sold. The Products list files it under one of the two.</summary>
public enum ProductStage
{
    /// <summary>Under development. A clone or variant starts here, and a new product when Settings → Costing Defaults says so.</summary>
    ResearchAndDevelopment,

    /// <summary>Made and sold. A new product starts here unless Settings → Costing Defaults says otherwise.</summary>
    Production,
}
