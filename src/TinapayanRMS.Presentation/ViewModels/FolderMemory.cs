using System.Reactive.Subjects;
using TinapayanRMS.ApplicationCore.Interfaces;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// Which of a list's folders start open, from what was kept last time, and every folder opened or closed since, so the
/// closed ones can be kept for next time (<see cref="ClosedKeys"/>, which FolderStateWriter writes). Every folder starts
/// open, one new since last time included, but those left closed. With nothing kept yet, a folder starts as its list says
/// (only Products' Deleted folder starts closed). ProductTree, RawMaterialTree and SubAssemblyTree each hold one.
/// </summary>
public sealed class FolderMemory : IDisposable
{
    /// <summary>The folders left closed last time, by key; null when nothing has been kept.</summary>
    private readonly HashSet<string>? _closedKeys;

    /// <summary>
    /// The folders left open last time, by key, from a list that kept its open folders rather than its closed ones, read
    /// only when no closed ones are kept: those open start open, and the rest closed, so the list opens as it was left.
    /// </summary>
    private readonly HashSet<string>? _openKeys;

    /// <summary>
    /// With nothing kept, the folders tracked that start closed (Products' Deleted): what the list would keep if nothing
    /// were touched, and so what it gives before it's filled.
    /// </summary>
    private readonly HashSet<string> _closedAtFirst = new(StringComparer.Ordinal);

    private readonly Subject<RxVoid> _expansionChanged = new();

    /// <param name="closedKeys">The folders left closed last time, as <see cref="ClosedKeys"/> gave them; null for none kept.</param>
    /// <param name="openKeys">The folders left open last time, kept by a list before it kept the closed ones; read only when <paramref name="closedKeys"/> is null.</param>
    public FolderMemory(IEnumerable<string>? closedKeys = null, IEnumerable<string>? openKeys = null)
    {
        _closedKeys = closedKeys?.ToHashSet(StringComparer.Ordinal);
        _openKeys = _closedKeys is null ? openKeys?.ToHashSet(StringComparer.Ordinal) : null;
    }

    /// <summary>
    /// What <paramref name="store"/> kept for a list: its closed folders under <paramref name="closedKey"/>, or failing
    /// those, its open ones under <paramref name="openKey"/>, where it kept them before.
    /// </summary>
    /// <param name="currentKey">
    /// A folder's key as kept, as its key now, for a list whose keys have changed since (ProductFolderNode.CurrentFolderKey):
    /// read as they were, a folder left closed would open, its key not found.
    /// </param>
    public static FolderMemory Read(IViewStateStore store, string closedKey, string? openKey = null, Func<string, string>? currentKey = null)
    {
        var current = currentKey ?? (key => key);
        return store.ReadList(closedKey) is { } closed
            ? new FolderMemory(closed.Select(current))
            : new FolderMemory(openKeys: openKey is null ? null : store.ReadList(openKey)?.Select(current));
    }

    /// <summary>A folder opened or closed, by a click, a search or Expand All: for keeping them (<see cref="ClosedKeys"/>).</summary>
    public IObservable<RxVoid> ExpansionChanged => _expansionChanged;

    /// <summary>
    /// Opens <paramref name="folder"/> or closes it, as it was left last time, or as <paramref name="startsOpen"/> says
    /// with nothing kept, and reports its opening and closing from now on (<see cref="ExpansionChanged"/>).
    /// </summary>
    public T Track<T>(T folder, bool startsOpen = true)
        where T : IListFolder
    {
        folder.IsExpanded = _closedKeys is not null ? !_closedKeys.Contains(folder.Key)
            : _openKeys is not null ? _openKeys.Contains(folder.Key)
            : startsOpen;
        if (_closedKeys is null && _openKeys is null && !startsOpen)
        {
            _closedAtFirst.Add(folder.Key);
        }

        // The event itself rather than WhenAnyValue, which needs ReactiveUI set up first: the trees are used without it too.
        // Only a change counts: a tree raises it again, unchanged, for a folder the tree has to be told about
        // (ITreeFolder.AnnounceExpanded).
        var wasExpanded = folder.IsExpanded;
        folder.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IListFolder.IsExpanded) && folder.IsExpanded != wasExpanded)
            {
                wasExpanded = folder.IsExpanded;
                _expansionChanged.OnNext(RxVoid.Default);
            }
        };
        return folder;
    }

    /// <summary>
    /// The keys of the <paramref name="folders"/> closed now, in order: what to keep for next time. Until the list has made
    /// its folders (<paramref name="isFilled"/> false), what was kept, or with nothing kept, the folders that start closed,
    /// so a screen never shown, or shown and left untouched, keeps it as it was.
    /// </summary>
    public IReadOnlyList<string> ClosedKeys(IEnumerable<IListFolder> folders, bool isFilled) =>
        isFilled
            ? [.. folders.Where(f => !f.IsExpanded).Select(f => f.Key).Order(StringComparer.Ordinal)]
            : [.. (_closedKeys ?? (_openKeys is null ? _closedAtFirst : [])).Order(StringComparer.Ordinal)];

    public void Dispose() => _expansionChanged.Dispose();
}
