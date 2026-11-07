namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The row of each record a list's tree shows, by the record's id, kept while the record is listed: a fresh copy of the
/// record (after a load, or a move) updates its row rather than replacing it, so the tree's selection stays on it, and a
/// record moved to another folder takes its row along. ProductTree, RawMaterialTree and SubAssemblyTree each hold one.
/// </summary>
/// <param name="idOf">The record's id.</param>
/// <param name="create">A row for a record listed for the first time.</param>
/// <param name="update">Gives a row a fresh copy of its record.</param>
internal sealed class TreeLeaves<TRecord, TLeaf>(Func<TRecord, int> idOf, Func<TRecord, TLeaf> create, Action<TLeaf, TRecord> update)
    where TLeaf : class
{
    private readonly Dictionary<int, TLeaf> _leaves = [];

    /// <summary>The row of the record listed under <paramref name="id"/>.</summary>
    public TLeaf this[int id] => _leaves[id];

    /// <summary>The record's row, or null while it isn't listed.</summary>
    public TLeaf? Find(int id) => _leaves.GetValueOrDefault(id);

    /// <summary>The rows of <paramref name="records"/>, each listed already, in their order.</summary>
    public IEnumerable<TLeaf> RowsOf(IEnumerable<TRecord> records) => records.Select(r => _leaves[idOf(r)]);

    /// <summary>Lists <paramref name="records"/> and no others: a row for each new one, a fresh copy into each kept one.</summary>
    public void Merge(IEnumerable<TRecord> records)
    {
        HashSet<int> listed = [];
        foreach (var record in records)
        {
            var id = idOf(record);
            listed.Add(id);
            if (_leaves.TryGetValue(id, out var leaf))
            {
                update(leaf, record);
            }
            else
            {
                _leaves[id] = create(record);
            }
        }

        foreach (var gone in _leaves.Keys.Where(id => !listed.Contains(id)).ToList())
        {
            _leaves.Remove(gone);
        }
    }
}
