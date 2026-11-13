using System.Collections.ObjectModel;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A record screen with a list beside its form: Products, Raw Materials and Sub-Assemblies. What the three lists work the
/// same way: the rows picked, shared with the tree, and what picking one does; the row whose name is being typed in place;
/// and the notice of the last move. Each screen gives it its own tree's rows (<see cref="ShowingRow"/>,
/// <see cref="PickRowFromList"/>), what a name typed does (<see cref="CommitTreeNameToAsync"/>) and what Esc takes away
/// (<see cref="TreeNameCancelled"/>); its moves, menus and folders stay its own.
/// </summary>
public abstract class ListFormViewModel<TSnapshot> : RecordFormViewModel<TSnapshot>
    where TSnapshot : class, IFormSnapshot<TSnapshot>
{
    private object? _selectedTreeNode;
    private string? _moveNotice;

    protected ListFormViewModel(ILogger logger)
        : base(logger)
    {
        ListNaming = new TreeNaming(ListSelection, () => this.RaisePropertyChanged(nameof(TreeNameEdit)), IsNewRow, CommitTreeNameToAsync, TreeNameCancelled);
        SelectedTreeNodes.CollectionChanged += (_, _) => TreeSelectionChanged();
        DismissMoveNoticeCommand = ReactiveCommand.Create(() => { MoveNotice = null; });
    }

    /// <summary>The rows picked in the list, as the tree and this screen change them (TreeSelection).</summary>
    protected TreeSelection ListSelection { get; } = new();

    /// <summary>The naming in place the list does, and its rules (TreeNaming).</summary>
    protected TreeNaming ListNaming { get; }

    /// <summary>
    /// The rows picked in the list, bound to the tree's SelectedItems, which it shares: one row, or several with Ctrl or
    /// Shift held. Its first is <see cref="SelectedTreeNode"/>. The tree changes it as rows are clicked, and the screen as
    /// the record it shows changes (<see cref="SyncTreeSelection"/>).
    /// </summary>
    public ObservableCollection<object> SelectedTreeNodes => ListSelection.Nodes;

    /// <summary>
    /// The first row picked in the list: a record's, a folder's, or null. Picking a record's row picks the record
    /// (<see cref="PickRowFromList"/>); picking a folder's leaves the form as it is, as a file explorer does, and so do more
    /// rows picked with Ctrl. The record shown changed any other way (a Save, New, a link from another screen) moves the row
    /// to it. Setting it picks that row alone, as a click does.
    /// </summary>
    public object? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set => ListSelection.Pick(value);
    }

    /// <summary>
    /// The row of the list whose name is being typed in place, as in a file explorer, or null: a record or folder being
    /// renamed (Rename, F2), or a new one being named (New, New Folder). The list draws a box on it, which gives the name
    /// typed to <see cref="CommitTreeNameAsync"/>, or Esc to <see cref="CancelTreeName"/>.
    /// </summary>
    public TreeNameEdit? TreeNameEdit => ListNaming.Edit;

    /// <summary>
    /// What the last move in the list did ("Moved Ube Halaya to Filling"), on the list's notice bar with Undo beside it;
    /// null once it's undone or dismissed. The bar hides itself after a few seconds.
    /// </summary>
    public string? MoveNotice
    {
        get => _moveNotice;
        protected set => this.RaiseAndSetIfChanged(ref _moveNotice, value);
    }

    /// <summary>Puts the notice of the last move away.</summary>
    public ReactiveCommand<RxVoid, RxVoid> DismissMoveNoticeCommand { get; }

    /// <summary>
    /// The name typed into the list's box (<see cref="TreeNameEdit"/>), on Enter or on leaving it: what it does is the
    /// screen's (<see cref="CommitTreeNameToAsync"/>). An empty name, or the name it had, is an Esc; a name refused shows
    /// why, and the box comes back with it. A second call for the same box, as Enter's box is left, does nothing.
    /// </summary>
    public Task CommitTreeNameAsync(string text) => ListNaming.CommitAsync(text);

    /// <summary>Esc in the list's name box: the name stays as it was, and a new record's or folder's row goes, with nothing saved.</summary>
    public void CancelTreeName() => ListNaming.Cancel();

    /// <summary>The row of the record the form shows, or the new record's row, or null for neither (or a search hiding it).</summary>
    protected abstract object? ShowingRow { get; }

    /// <summary>The name of the screen's property listing the records picked (SelectedTreeProducts and the like), raised as the rows picked change.</summary>
    protected abstract string PickedRecordsProperty { get; }

    /// <summary>
    /// The user picked <paramref name="row"/> first in the list, by a click or a key: a saved record's row other than the one
    /// shown picks its record (PickFromList); any other row (a folder's, the new record's) leaves the form as it is.
    /// </summary>
    protected abstract void PickRowFromList(object row);

    /// <summary>Whether a row of the list is a new record's or a new folder's, still to be named (TreeNaming).</summary>
    protected abstract bool IsNewRow(object node);

    /// <summary>What a name typed into the list's box does (<see cref="CommitTreeNameAsync"/>), once it's taken for one.</summary>
    protected abstract Task CommitTreeNameToAsync(TreeNameEdit edit, string name, string text);

    /// <summary>What Esc does to the row named (<see cref="CancelTreeName"/>), once its box is closed: a new row goes.</summary>
    protected abstract void TreeNameCancelled(object node);

    /// <summary>
    /// Puts the row of the record shown first among the rows picked, and so makes it <see cref="SelectedTreeNode"/>: alone,
    /// unless it's picked already, when the rows picked with it stay. Nothing shown, or a search hiding it, picks no row.
    /// </summary>
    protected void SyncTreeSelection() => ListSelection.BringFirst(ShowingRow);

    /// <summary>
    /// The list's selection changed: by a click (or several, with Ctrl or Shift), or by this screen. Its first row is
    /// SelectedTreeNode, and a row the user picked first goes to the screen (<see cref="PickRowFromList"/>).
    /// </summary>
    private void TreeSelectionChanged()
    {
        var first = SelectedTreeNodes.Count > 0 ? SelectedTreeNodes[0] : null;
        this.RaisePropertyChanged(PickedRecordsProperty);
        if (ReferenceEquals(first, _selectedTreeNode))
        {
            return;
        }

        _selectedTreeNode = first;
        this.RaisePropertyChanged(nameof(SelectedTreeNode));

        // The tree calls this in the middle of changing SelectedItems: picking the record leads to SyncTreeSelection,
        // which then finds its row first already and leaves the collection alone, as it must mid-change.
        if (!ListSelection.IsSyncing && first is not null)
        {
            PickRowFromList(first);
        }
    }
}
