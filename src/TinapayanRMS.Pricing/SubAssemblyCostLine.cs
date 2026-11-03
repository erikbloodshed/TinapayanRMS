namespace TinapayanRMS.Pricing;

/// <summary>
/// One sub-assembly's part of the Costing Summary's Sub-assemblies row: what it costs in one sellable unit,
/// and its share of the selling price.
/// </summary>
/// <param name="Name">What the recipe calls it: its tab's header (ProductUsageEdit.SectionName), e.g. "Filling: Ube".</param>
public sealed record SubAssemblyCostLine(string Name, decimal Cost, decimal Share);
