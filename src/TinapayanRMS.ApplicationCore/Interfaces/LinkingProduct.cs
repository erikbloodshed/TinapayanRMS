namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A product that uses a standalone sub-assembly (<see cref="ISubAssemblyRepository.GetStandaloneLinkedByAsync"/>):
/// its id, so a screen can open it, its name, to show, and its category's name (null when uncategorized), which picks
/// the tile's icon.
/// </summary>
public sealed record LinkingProduct(int Id, string Name, string? CategoryName);
