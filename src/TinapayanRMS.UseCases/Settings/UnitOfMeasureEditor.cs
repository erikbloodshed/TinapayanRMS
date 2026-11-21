using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.Settings;

/// <summary>Settings' Units of Measure tab: each unit's name and abbreviation, through <see cref="ILookupListEditor{TItem}"/>.</summary>
public sealed class UnitOfMeasureEditor(IUnitOfMeasureRepository repository, IValidator<UnitOfMeasure> validator) : ILookupListEditor<UnitOfMeasure>
{
    public Task<IReadOnlyList<UnitOfMeasure>> GetAllAsync(CancellationToken cancellationToken) => repository.GetAllAsync(cancellationToken);

    public void EnsureValid(UnitOfMeasure item)
    {
        var validation = validator.Validate(item);
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }
    }

    public async Task AddAsync(UnitOfMeasure item, CancellationToken cancellationToken)
    {
        // Async, so a refusal fails the task rather than throwing as it's called.
        EnsureValid(item);
        await repository.AddAsync(item, cancellationToken);
    }

    public async Task UpdateAsync(UnitOfMeasure item, CancellationToken cancellationToken)
    {
        EnsureValid(item);
        await repository.UpdateAsync(item, cancellationToken);
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken) => repository.DeleteAsync(id, cancellationToken);
}
