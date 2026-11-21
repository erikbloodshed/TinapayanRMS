using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.PriceSimulator;

/// <summary>
/// What the Price Simulator does to a scenario, its price changes kept under a name: saves one, replacing the changes of one
/// of the same name, and deletes one. No real price changes. A scenario its rules refuse (PriceScenarioRules) is a
/// <see cref="BusinessRuleException"/>, in words for the shop's staff. Two computers saving a new scenario of the same name
/// at the same moment both find none, and the second is refused by the database's unique name, shown as what it couldn't
/// do rather than as a rule.
/// </summary>
public interface IPriceScenarioEditor
{
    /// <summary>Saves <paramref name="lines"/> as the scenario <paramref name="name"/>, replacing one of that name's; its id.</summary>
    Task<int> SaveAsync(string name, IReadOnlyList<PriceScenarioLine> lines, DateTime savedAt, CancellationToken cancellationToken);

    Task DeleteAsync(int scenarioId, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IPriceScenarioEditor"/>
public sealed class PriceScenarioEditor(IPriceScenarioRepository scenarioRepository) : IPriceScenarioEditor
{
    public Task<int> SaveAsync(string name, IReadOnlyList<PriceScenarioLine> lines, DateTime savedAt, CancellationToken cancellationToken) =>
        scenarioRepository.SaveAsync(name, lines, savedAt, cancellationToken);

    public Task DeleteAsync(int scenarioId, CancellationToken cancellationToken) => scenarioRepository.DeleteAsync(scenarioId, cancellationToken);
}
