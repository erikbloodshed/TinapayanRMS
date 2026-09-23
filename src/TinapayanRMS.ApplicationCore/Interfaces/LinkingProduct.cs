namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A product that uses a standalone sub-assembly (<see cref="ISubAssemblyRepository.GetStandaloneLinkedByAsync"/>):
/// its id, so a screen can open it, and its name, to show.
/// </summary>
public sealed record LinkingProduct(int Id, string Name);
