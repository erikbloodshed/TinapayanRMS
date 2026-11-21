using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.UseCases.Settings;

/// <summary>
/// What one of Settings' list tabs does to its list (units of measure, raw material and product categories, roles): reads
/// it, checks an item, adds one, writes one over its stored self, deletes one. A refusal is a
/// <see cref="BusinessRuleException"/>, in words for the shop's staff.
/// </summary>
/// <typeparam name="TItem">The kind of item the list holds.</typeparam>
public interface ILookupListEditor<TItem>
    where TItem : class
{
    /// <summary>Every item, as stored.</summary>
    Task<IReadOnlyList<TItem>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Refuses an item the list can't take, before anything is asked or written.</summary>
    void EnsureValid(TItem item);

    /// <summary>Adds <paramref name="item"/>, checked; its id is filled in.</summary>
    Task AddAsync(TItem item, CancellationToken cancellationToken);

    /// <summary>Writes <paramref name="item"/>, checked, over the stored item with its id.</summary>
    Task UpdateAsync(TItem item, CancellationToken cancellationToken);

    /// <summary>Deletes the item with <paramref name="id"/>; the database refuses one anything still uses.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
