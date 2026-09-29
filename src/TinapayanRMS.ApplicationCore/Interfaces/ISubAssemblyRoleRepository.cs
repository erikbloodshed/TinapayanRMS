using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ISubAssemblyRoleRepository
{
    Task<IReadOnlyList<SubAssemblyRole>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds the role after all the others: its SortOrder is set here, one past the last, whatever it held. Refused
    /// product-owned on a packaging role.
    /// </summary>
    Task AddAsync(SubAssemblyRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the role's name and whether it's product-owned, in one transaction; its place in the order is
    /// <see cref="MoveAsync"/>'s. Turned product-owned, each
    /// sub-assembly in it on the Sub-Assemblies screen that exactly one product uses becomes that product's own (its
    /// add-ins there merged into its lines); turned back, every one products own in it goes to the Sub-Assemblies
    /// screen, still linked. Renamed, the sub-assemblies products own in it are renamed with it.
    /// </summary>
    Task UpdateAsync(SubAssemblyRole role, CancellationToken cancellationToken);

    /// <summary>
    /// How many sub-assemblies <see cref="UpdateAsync"/> would move by making the role product-owned
    /// (<paramref name="makeOwned"/>: the ones on the Sub-Assemblies screen exactly one product uses) or not (the ones
    /// products own in it).
    /// </summary>
    Task<int> CountOwnershipChangesAsync(int roleId, bool makeOwned, CancellationToken cancellationToken);

    /// <summary>
    /// Swaps the role with the one before it (<paramref name="up"/>) or after it, and numbers every role 1, 2, 3… in the
    /// new order. Nothing moves when it's already first or last.
    /// </summary>
    Task MoveAsync(int id, bool up, CancellationToken cancellationToken);

    /// <summary>Deletes the role, and numbers the rest 1, 2, 3… again so the order has no gap.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
