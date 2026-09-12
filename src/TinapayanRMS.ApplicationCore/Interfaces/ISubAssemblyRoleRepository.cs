using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface ISubAssemblyRoleRepository
{
    Task<IReadOnlyList<SubAssemblyRole>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(SubAssemblyRole role, CancellationToken cancellationToken);

    Task UpdateAsync(SubAssemblyRole role, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
