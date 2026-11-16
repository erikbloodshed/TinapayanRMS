using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>A row of the Sub-Assemblies list's tree (SubAssemblyTree): a role's folder, or a sub-assembly in one.</summary>
public closed class SubAssemblyTreeNode : ObservableState
{
    public abstract string Title { get; }

    /// <summary>The icon before the title, or null for none.</summary>
    public abstract AppIcon? Icon { get; }

    public abstract bool IsFolder { get; }

    /// <summary>How many sub-assemblies a folder holds, shown after its title; always 0 for a sub-assembly, which shows none.</summary>
    public virtual int SubAssemblyCount => 0;
}

/// <summary>
/// A role's folder in the Sub-Assemblies list, holding the sub-assemblies in that role. Its rows are in
/// <see cref="Children"/>, which the tree (FolderTree) draws under it, as a Raw Materials folder's are
/// (RawMaterialFolderNode).
/// </summary>
public sealed class SubAssemblyFolderNode : SubAssemblyTreeNode, ITreeFolder<SubAssemblyTreeNode>
{
    internal SubAssemblyFolderNode(SubAssemblyRole role, bool isNewFolder = false)
    {
        RoleId = role.Id;
        IsPackaging = role.IsPackaging;
        IsNewFolder = isNewFolder;
        _title = role.Name;
        Children.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(Icon));
    }

    /// <summary>
    /// The row New Folder adds after the roles while its name is typed into it (SubAssemblyTree.BeginNewFolder): a role
    /// once the name typed saves one, and none at all if it's cancelled. Its RoleId is 0.
    /// </summary>
    public static SubAssemblyFolderNode NewFolder() => new(new SubAssemblyRole { Name = NewFolderTitle }, isNewFolder: true);

    public const string NewFolderTitle = "New folder";

    /// <summary>The row New Folder adds, named in the list before it's a role: nothing is dropped on it, and it has no menu.</summary>
    public bool IsNewFolder { get; }

    public int RoleId { get; }

    /// <summary>The Packaging role's folder, which holds the packaging sets. Nothing clears the flag (SubAssemblyRole.IsPackaging).</summary>
    public bool IsPackaging { get; }

    /// <summary>What names the folder from one run to the next, for remembering whether it was left closed: the role's id, "12".</summary>
    public string Key => IsNewFolder ? "New" : RoleId.ToString(CultureInfo.InvariantCulture);

    public IEnumerable<SubAssemblyLeafNode> SubAssemblies => Children.OfType<SubAssemblyLeafNode>();

    private int _subAssemblyCount;
    /// <summary>The sub-assemblies it shows, or while a search filters the list, the matches.</summary>
    public override int SubAssemblyCount => _subAssemblyCount;

    internal void ShowCount(int count)
    {
        if (_subAssemblyCount != count)
        {
            _subAssemblyCount = count;
            RaisePropertyChanged(nameof(SubAssemblyCount));
        }
    }

    private string _title;
    public override string Title => _title;

    /// <summary>A role renamed keeps its folder, under its new name.</summary>
    internal void Rename(string title)
    {
        if (_title != title)
        {
            _title = title;
            RaisePropertyChanged(nameof(Title));
        }
    }

    /// <summary>FolderOpen while the folder is open on its rows, and Folder otherwise, empty ones included.</summary>
    public override AppIcon? Icon => IsExpanded && Children.Count > 0 ? AppIcon.FolderOpen : AppIcon.Folder;

    public override bool IsFolder => true;

    public ObservableCollection<SubAssemblyTreeNode> Children { get; } = [];

    private bool _isExpanded;
    /// <summary>
    /// The folder is open. The tree (FolderTree) reads and writes it, and
    /// it's kept here, so a folder stays as it was left through a refresh, or while a search hides it.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            RaiseAndSetIfChanged(ref _isExpanded, value);
            RaisePropertyChanged(nameof(Icon));
        }
    }

    /// <summary>Tells the tree again whether the folder is open (ITreeFolder.AnnounceExpanded says when, and why).</summary>
    internal void AnnounceExpanded() => RaisePropertyChanged(nameof(IsExpanded));

    void ITreeFolder<SubAssemblyTreeNode>.ShowCount(int count) => ShowCount(count);

    void ITreeFolder<SubAssemblyTreeNode>.AnnounceExpanded() => AnnounceExpanded();
}

/// <summary>
/// A sub-assembly in its role's folder. There's one for each sub-assembly, kept while it's listed, so a fresh copy of its
/// row (after a load, or a move) updates it rather than replacing it, and one moved to another role takes it along.
/// </summary>
public sealed class SubAssemblyLeafNode(StandaloneSubAssemblyRow row) : SubAssemblyTreeNode
{
    private StandaloneSubAssemblyRow _row = row;
    public StandaloneSubAssemblyRow Row
    {
        get => _row;
        internal set
        {
            if (!ReferenceEquals(_row, value))
            {
                _row = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Title));
            }
        }
    }

    public SubAssembly SubAssembly => Row.SubAssembly;

    public override string Title => Row.Name;

    public override AppIcon? Icon => null;

    public override bool IsFolder => false;

    /// <summary>
    /// The row New adds while its name is typed into it, and until the sub-assembly is saved (SubAssemblyTree.BeginNewSubAssembly),
    /// over the one the form starts: it has no menu, isn't dragged, and picking it loads nothing.
    /// </summary>
    public bool IsNew => Row.Id == 0;
}

/// <summary>
/// The Sub-Assemblies list as a tree: a folder for every role, in the roles' order (Settings → Roles), each holding the
/// sub-assemblies in that role (by name). Refresh merges rather than rebuilds, as RawMaterialTree's does: every folder and
/// sub-assembly keeps its node, which the tree's selection and open folders are tied to.
/// </summary>
public sealed class SubAssemblyTree : IDisposable
{
    private readonly TreeLeaves<StandaloneSubAssemblyRow, SubAssemblyLeafNode> _leaves = new(r => r.Id, r => new SubAssemblyLeafNode(r), (leaf, r) => leaf.Row = r);

    /// <summary>Every role's folder, shown or not, by role id.</summary>
    private readonly Dictionary<int, SubAssemblyFolderNode> _folders = [];

    /// <summary>Which folders start open, and every one opened or closed since.</summary>
    private readonly FolderMemory _memory;

    /// <param name="memory">
    /// The folders left closed last time (<see cref="ClosedFolderKeys"/>, by <see cref="SubAssemblyFolderNode.Key"/>); null
    /// for none kept, so the list first shows every sub-assembly.
    /// </param>
    public SubAssemblyTree(FolderMemory? memory = null)
    {
        _memory = memory ?? new FolderMemory();
    }

    /// <summary>The role folders the list shows, in the roles' order: all of them, or while a search filters it, those holding a match.</summary>
    public ObservableCollection<SubAssemblyFolderNode> Roots { get; } = [];

    /// <summary>A folder opened or closed, by a click, a search or Expand All: for remembering them (ClosedFolderKeys).</summary>
    public IObservable<Unit> ExpansionChanged => _memory.ExpansionChanged;

    /// <summary>The sub-assembly's row, or null while it isn't listed (a search hides it, or it isn't loaded).</summary>
    public SubAssemblyLeafNode? Find(int subAssemblyId) => _leaves.Find(subAssemblyId);

    /// <summary>The role's folder, shown or not; null for one it has no folder for yet.</summary>
    public SubAssemblyFolderNode? Folder(int roleId) => _folders.GetValueOrDefault(roleId);

    /// <summary>
    /// Every folder closed now, by <see cref="SubAssemblyFolderNode.Key"/>: what to start with next time. Before the first
    /// Refresh, which makes the folders, the ones it was given, so a screen never shown keeps them as they were.
    /// </summary>
    public IReadOnlyList<string> ClosedFolderKeys() => _memory.ClosedKeys(_folders.Values, _folders.Count > 0);

    public void ExpandAll()
    {
        foreach (var folder in _folders.Values)
        {
            folder.IsExpanded = true;
        }
    }

    public void CollapseAll()
    {
        foreach (var folder in _folders.Values)
        {
            folder.IsExpanded = false;
        }
    }

    /// <summary>The row New Folder added, while its name is being typed; null otherwise.</summary>
    public SubAssemblyFolderNode? PendingFolder { get; private set; }

    /// <summary>The row New added, while its name is being typed and until the sub-assembly is saved; null otherwise.</summary>
    public SubAssemblyLeafNode? PendingSubAssembly { get; private set; }

    /// <summary>
    /// Adds a "New folder" row after the roles, for its name to be typed into. A refresh keeps it there until
    /// <see cref="EndNewFolder"/>. Any earlier one goes first: there's one at a time.
    /// </summary>
    public SubAssemblyFolderNode BeginNewFolder()
    {
        EndNewFolder();
        var folder = PendingFolder = SubAssemblyFolderNode.NewFolder();
        Roots.Add(folder);
        return folder;
    }

    /// <summary>Takes away the row <see cref="BeginNewFolder"/> added, if there is one.</summary>
    public void EndNewFolder()
    {
        if (PendingFolder is { } folder)
        {
            PendingFolder = null;
            Roots.Remove(folder);
        }
    }

    /// <summary>
    /// Adds a row for the new sub-assembly the form starts, <paramref name="subAssembly"/> (no id yet, its role set), last in
    /// its role's folder, which opens to show it. A refresh keeps it there, uncounted, until <see cref="EndNewSubAssembly"/>.
    /// Any earlier one goes first. Null when its role has no folder.
    /// </summary>
    public SubAssemblyLeafNode? BeginNewSubAssembly(SubAssembly subAssembly)
    {
        EndNewSubAssembly();
        if (Folder(subAssembly.RoleId) is not { } folder)
        {
            return null;
        }

        var row = PendingSubAssembly = new SubAssemblyLeafNode(new StandaloneSubAssemblyRow(subAssembly, []));
        folder.Children.Add(row);
        folder.IsExpanded = true;
        return row;
    }

    /// <summary>Takes away the row <see cref="BeginNewSubAssembly"/> added, if there is one.</summary>
    public void EndNewSubAssembly()
    {
        if (PendingSubAssembly is { } row)
        {
            PendingSubAssembly = null;
            foreach (var folder in _folders.Values)
            {
                folder.Children.Remove(row);
            }
        }
    }

    public void Dispose() => _memory.Dispose();

    /// <summary>
    /// Where dropping <paramref name="subAssembly"/> on <paramref name="target"/> files it: a role's folder puts it in that
    /// role, and another sub-assembly in that one's role. Null when that's its role already, for a role of the other kind
    /// (a packaging set stays in the Packaging role, and a recipe out of it), or with nothing to drop on.
    /// </summary>
    public static SubAssemblyMove? MoveTo(SubAssemblyTreeNode? target, SubAssembly subAssembly)
    {
        (int RoleId, bool IsPackaging)? place = subAssembly.Id == 0 ? null : target switch
        {
            SubAssemblyFolderNode { IsNewFolder: true } or SubAssemblyLeafNode { IsNew: true } => null,
            SubAssemblyFolderNode folder => (folder.RoleId, folder.IsPackaging),
            SubAssemblyLeafNode leaf => (leaf.SubAssembly.RoleId, IsPackagingSet(leaf.SubAssembly)),
            null => null,
        };

        return place is { } to && to.IsPackaging == IsPackagingSet(subAssembly) && to.RoleId != subAssembly.RoleId
            ? new SubAssemblyMove(subAssembly.Id, to.RoleId)
            : null;
    }

    /// <summary>
    /// What dropping several sub-assemblies on <paramref name="target"/> does: <see cref="MoveTo(SubAssemblyTreeNode?, SubAssembly)"/>
    /// for each, leaving out any that are there already. Empty when none of them would move.
    /// </summary>
    public static IReadOnlyList<SubAssemblyMove> MovesTo(SubAssemblyTreeNode? target, IEnumerable<SubAssembly> subAssemblies) =>
        [.. subAssemblies.Select(s => MoveTo(target, s)).OfType<SubAssemblyMove>()];

    /// <summary>
    /// Giving each of <paramref name="subAssemblies"/> <paramref name="role"/>, leaving out any in it already and any of
    /// the other kind: the list's Move To.
    /// </summary>
    public static IReadOnlyList<SubAssemblyMove> MovesTo(SubAssemblyRole role, IEnumerable<SubAssembly> subAssemblies) =>
        [.. from subAssembly in subAssemblies
            where IsPackagingSet(subAssembly) == role.IsPackaging && subAssembly.RoleId != role.Id
            select new SubAssemblyMove(subAssembly.Id, role.Id)];

    /// <summary>A packaging set rather than a recipe: it's in the Packaging role.</summary>
    public static bool IsPackagingSet(SubAssembly subAssembly) => subAssembly.Role?.IsPackaging == true;

    /// <summary>
    /// Brings the tree in line with <paramref name="rows"/> (in the order the list shows them) and <paramref name="roles"/>
    /// (in theirs). Every role has a folder, so an empty one can still be dropped on; while a search filters the list
    /// (<paramref name="isFiltered"/>), only the folders holding a match show, open.
    /// </summary>
    public void Refresh(IReadOnlyList<SubAssemblyRole> roles, IReadOnlyList<StandaloneSubAssemblyRow> rows, bool isFiltered)
    {
        // A sub-assembly's role is read with it, so one added since the roles were loaded still gets its folder.
        var allRoles = roles
            .Concat(rows.Select(r => r.SubAssembly.Role).OfType<SubAssemblyRole>())
            .DistinctBy(r => r.Id)
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();
        var roleIds = allRoles.Select(r => r.Id).ToHashSet();

        _leaves.Merge(rows);

        foreach (var role in allRoles)
        {
            if (_folders.TryGetValue(role.Id, out var folder))
            {
                folder.Rename(role.Name);
            }
            else
            {
                _folders[role.Id] = _memory.Track(new SubAssemblyFolderNode(role));
            }
        }

        // A deleted role's folder goes for good.
        foreach (var id in _folders.Keys.Where(id => !roleIds.Contains(id)).ToList())
        {
            _folders.Remove(id);
        }

        var byRole = rows.ToLookup(r => r.SubAssembly.RoleId);
        List<(SubAssemblyFolderNode Folder, List<SubAssemblyTreeNode> Rows)> wanted =
            [.. allRoles.Select(role => (_folders[role.Id], (List<SubAssemblyTreeNode>)[.. _leaves.RowsOf(byRole[role.Id])]))];

        // The rows being named stay where they were added: a new folder after the roles, a new sub-assembly last in its role's.
        var pendingAt = PendingSubAssembly is { } pending ? wanted.FindIndex(w => w.Folder.RoleId == pending.SubAssembly.RoleId) : -1;
        if (pendingAt >= 0)
        {
            wanted[pendingAt].Rows.Add(PendingSubAssembly!);
        }

        if (PendingFolder is { } newFolder)
        {
            wanted.Add((newFolder, []));
        }

        // Everything leaving the tree goes before anything arrives, so a sub-assembly moving from one folder to another is
        // never in both at once. The roles come in before their sub-assemblies on a load, so on the first one every folder
        // is shown empty and filled a moment later.
        TreeRows.RemoveLeaving(Roots, wanted, isFiltered);
        TreeRows.LayFolders(Roots, wanted, isFiltered);

        // A new sub-assembly isn't one yet: its folder counts only the saved ones.
        if (pendingAt >= 0)
        {
            var folder = wanted[pendingAt].Folder;
            folder.ShowCount(folder.SubAssemblyCount - 1);
        }
    }
}
