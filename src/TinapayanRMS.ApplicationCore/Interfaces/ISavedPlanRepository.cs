using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>The Production Plan's saved plans (SavedPlanRules).</summary>
public interface ISavedPlanRepository
{
    /// <summary>Every saved plan, newest first (then by name), each with its lines in order.</summary>
    Task<IReadOnlyList<SavedPlan>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves <paramref name="lines"/> and <paramref name="halfBatches"/> under <paramref name="name"/>, trimmed, stamped
    /// <paramref name="savedAt"/>: they replace those of the plan with that name, ignoring case, or make a new one. Each
    /// line's order is its place in <paramref name="lines"/>. Refused, with nothing written, when SavedPlanRules refuses
    /// it. Returns the plan's id.
    /// </summary>
    Task<int> SaveAsync(string name, IReadOnlyList<SavedPlanLine> lines, bool halfBatches, DateTime savedAt, CancellationToken cancellationToken);

    /// <summary>Deletes the plan and its lines. One already gone is left alone.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
