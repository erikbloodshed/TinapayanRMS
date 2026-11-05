namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A product that uses a standalone sub-assembly (<see cref="ISubAssemblyRepository.GetStandaloneLinkedByAsync"/>) or a raw
/// material (<see cref="IRawMaterialRepository.GetUsedByAsync"/>): its id, so a screen can open it, its name, to show, and
/// its category's name (null when uncategorized) and whether it's a composite, which pick the tile's icon.
/// </summary>
public sealed record LinkingProduct(int Id, string Name, string? CategoryName, bool IsComposite = false)
{
    /// <summary>
    /// What the material makes up of the product's material cost, "38% of materials" (Raw Materials' Used By);
    /// null where it isn't worked out, as on Sub-Assemblies' tiles.
    /// </summary>
    public string? CostShareText { get; init; }
}
