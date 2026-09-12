using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface ISubAssemblyRepository
{
    Task<IReadOnlyList<SubAssembly>> GetAllAsync(CancellationToken cancellationToken);

    Task<SubAssembly?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task AddAsync(SubAssembly subAssembly, CancellationToken cancellationToken);

    Task UpdateAsync(SubAssembly subAssembly, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
