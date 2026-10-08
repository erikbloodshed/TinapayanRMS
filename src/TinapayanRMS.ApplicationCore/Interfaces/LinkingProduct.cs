namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A product that uses a standalone sub-assembly (<see cref="ISubAssemblyRepository.GetStandaloneLinkedByAsync"/>) or a raw
/// material (<see cref="IRawMaterialRepository.GetUsedByAsync"/>): its id, so a screen can open it, its name, to show, and
/// its category's name (null when uncategorized) and whether it's a composite, which pick the tile's icon.
/// </summary>
public sealed record LinkingProduct(int Id, string Name, string? CategoryName, bool IsComposite = false);
