namespace TinapayanRMS.Domain.Enums;

/// <summary>Where a product stands: still being worked out, or made and sold. The Products list files it under one of the two.</summary>
public enum ProductStage
{
    /// <summary>Under development. A new product, or a clone, starts here.</summary>
    ResearchAndDevelopment,

    /// <summary>Made and sold.</summary>
    Production,
}
