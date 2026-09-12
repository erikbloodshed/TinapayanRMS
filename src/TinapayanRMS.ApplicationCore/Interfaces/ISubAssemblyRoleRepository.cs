using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ISubAssemblyRoleRepository
{
    Task<IReadOnlyList<SubAssemblyRole>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(SubAssemblyRole role, CancellationToken cancellationToken);

    Task UpdateAsync(SubAssemblyRole role, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
