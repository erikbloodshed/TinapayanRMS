using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.Settings;

/// <summary>
/// Settings' Costing Defaults tab: what a new product and a new raw material start with and what every product is priced
/// with (<see cref="CostingDefaults"/>), and working every stored cost out again. A refusal is a
/// <see cref="BusinessRuleException"/>, in words for the shop's staff.
/// </summary>
public interface ICostingDefaultsEditor
{
    /// <summary>The defaults as saved, or the app's own where none are.</summary>
    Task<CostingDefaults> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves <paramref name="defaults"/>: the labor, overhead, administrative and markup percentages checked as a product's
    /// own costing's are, with the same messages; the rest is the repository's to check.
    /// </summary>
    Task SaveAsync(CostingDefaults defaults, CancellationToken cancellationToken);

    /// <summary>
    /// Works every made and linked ingredient's costs out again from what they're made of, for after the database was
    /// changed outside the app; how many were out of date.
    /// </summary>
    Task<CostRecalculation> RecalculateCostsAsync(CancellationToken cancellationToken);
}

/// <inheritdoc cref="ICostingDefaultsEditor"/>
public sealed class CostingDefaultsEditor(
    ICostingDefaultsRepository repository, IValidator<CostingParameters> validator, ICostRecalculator costRecalculator) : ICostingDefaultsEditor
{
    public Task<CostingDefaults> GetAsync(CancellationToken cancellationToken) => repository.GetAsync(cancellationToken);

    public async Task SaveAsync(CostingDefaults defaults, CancellationToken cancellationToken)
    {
        var validation = validator.Validate(new CostingParameters
        {
            LaborPercent = defaults.LaborPercent,
            OverheadPercent = defaults.OverheadPercent,
            AdministrativePercent = defaults.AdministrativePercent,
            MarginPercent = defaults.MarginPercent,
        });
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }

        await repository.SetAsync(defaults, cancellationToken);
    }

    public Task<CostRecalculation> RecalculateCostsAsync(CancellationToken cancellationToken) => costRecalculator.RecalculateAllAsync(cancellationToken);
}
