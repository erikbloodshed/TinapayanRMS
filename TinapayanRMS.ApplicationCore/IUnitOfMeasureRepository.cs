using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface IUnitOfMeasureRepository
{
    Task<IReadOnlyList<UnitOfMeasure>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(UnitOfMeasure unitOfMeasure, CancellationToken cancellationToken);

    Task UpdateAsync(UnitOfMeasure unitOfMeasure, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
