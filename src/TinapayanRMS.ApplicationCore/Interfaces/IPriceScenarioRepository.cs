using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>The Price Simulator's saved scenarios (PriceScenarioRules).</summary>
public interface IPriceScenarioRepository
{
    /// <summary>Every scenario, newest first (then by name), each with its lines in order.</summary>
    Task<IReadOnlyList<PriceScenario>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves <paramref name="lines"/> under <paramref name="name"/>, trimmed, stamped <paramref name="savedAt"/>: they
    /// replace the lines of the scenario with that name, ignoring case, or make a new one. Each line's order is its place
    /// in <paramref name="lines"/>. Refused, with nothing written, when PriceScenarioRules refuses it. Returns the
    /// scenario's id.
    /// </summary>
    Task<int> SaveAsync(string name, IReadOnlyList<PriceScenarioLine> lines, DateTime savedAt, CancellationToken cancellationToken);

    /// <summary>Deletes the scenario and its lines. One already gone is left alone.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
