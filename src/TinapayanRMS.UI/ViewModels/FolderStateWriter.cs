using System.Reactive.Linq;
using TinapayanRMS.ApplicationCore.Interfaces;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// Keeps the folders of a screen's list as they were left, for next time: the open ones (Products, Raw Materials) or the
/// closed ones (Sub-Assemblies), under one view state key (IViewStateStore). Once they've settled after a change (a search
/// or Expand All opens several in a row) they're written off the UI thread, and only when they differ from what was written
/// last. Disposing it writes a change still settling, rather than losing it as the app closes.
/// </summary>
public sealed class FolderStateWriter : IDisposable
{
    private readonly IViewStateStore _viewStateStore;
    private readonly string _key;
    private readonly Func<IReadOnlyList<string>> _readFolders;
    private readonly IDisposable _writer;

    /// <summary>Held while writing, which the throttled writer does off the UI thread and Dispose on it.</summary>
    private readonly Lock _lock = new();

    /// <summary>The folders last written, so a write is only made for a change.</summary>
    private IReadOnlyList<string> _written;

    /// <param name="expansionChanged">The list's folders opening and closing (the tree's ExpansionChanged).</param>
    /// <param name="readFolders">The folders to keep as the list has them now, such as ProductTree.OpenFolderKeys.</param>
    public FolderStateWriter(IViewStateStore viewStateStore, string key, IObservable<RxVoid> expansionChanged, Func<IReadOnlyList<string>> readFolders)
    {
        _viewStateStore = viewStateStore;
        _key = key;
        _readFolders = readFolders;

        // As they start, so nothing is written until a folder is opened or closed, not even the first time.
        _written = readFolders();
        _writer = expansionChanged
            .Select(_ => readFolders())
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(Write);
    }

    public void Dispose()
    {
        _writer.Dispose();
        Write(_readFolders());
    }

    private void Write(IReadOnlyList<string> folders)
    {
        lock (_lock)
        {
            if (!folders.SequenceEqual(_written, StringComparer.Ordinal))
            {
                _viewStateStore.WriteList(_key, folders);
                _written = folders;
            }
        }
    }
}
