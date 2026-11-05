using System.Collections.ObjectModel;
using System.Globalization;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>A row of the Sub-Assemblies list's tree (SubAssemblyTree): a role's folder, or a sub-assembly in one.</summary>
public closed class SubAssemblyTreeNode : ReactiveObject
{
    public abstract string Title { get; }

    /// <summary>The Segoe Fluent Icons glyph before the title, or empty for none.</summary>
    public abstract string Glyph { get; }

    public abstract bool IsFolder { get; }

    /// <summary>How many sub-assemblies a folder holds, shown after its title; always 0 for a sub-assembly, which shows none.</summary>
    public virtual int SubAssemblyCount => 0;
}

/// <summary>
/// A role's folder in the Sub-Assemblies list, holding the sub-assemblies in that role. The tree finds its rows in
/// <see cref="Children"/> through a HierarchyPropertyDescriptor for this type alone (SubAssemblyListCard), as the Raw
/// Materials list does (RawMaterialFolderNode).
/// </summary>
public sealed class SubAssemblyFolderNode : SubAssemblyTreeNode, IListFolder
{
    internal SubAssemblyFolderNode(SubAssemblyRole role)
    {
        RoleId = role.Id;
        IsPackaging = role.IsPackaging;
        _title = role.Name;
        Children.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(Glyph));
    }

    public int RoleId { get; }

    /// <summary>The Packaging role's folder, which holds the packaging sets. Nothing clears the flag (SubAssemblyRole.IsPackaging).</summary>
    public bool IsPackaging { get; }

    /// <summary>What names the folder from one run to the next, for remembering whether it was left closed: the role's id, "12".</summary>
    public string Key => RoleId.ToString(CultureInfo.InvariantCulture);

    public IEnumerable<SubAssemblyLeafNode> SubAssemblies => Children.OfType<SubAssemblyLeafNode>();

    private int _subAssemblyCount;
    /// <summary>The sub-assemblies it shows, or while a search filters the list, the matches.</summary>
    public override int SubAssemblyCount => _subAssemblyCount;

    internal void ShowCount(int count)
    {
        if (_subAssemblyCount != count)
        {
            _subAssemblyCount = count;
            this.RaisePropertyChanged(nameof(SubAssemblyCount));
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
            this.RaisePropertyChanged(nameof(Title));
        }
    }

    /// <summary>Segoe Fluent Icons' FolderOpen while the folder is open on its rows, and Folder otherwise, empty ones included.</summary>
    public override string Glyph => IsExpanded && Children.Count > 0 ? "" : "";

    public override bool IsFolder => true;

    public ObservableCollection<SubAssemblyTreeNode> Children { get; } = [];

    private bool _isExpanded;
    /// <summary>
    /// The folder is open. The tree reads and writes it (its HierarchyPropertyDescriptor's IsExpandedPropertyName), and
    /// it's kept here, so a folder stays as it was left through a refresh, or while a search hides it.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            this.RaiseAndSetIfChanged(ref _isExpanded, value);
            this.RaisePropertyChanged(nameof(Glyph));
        }
    }

    /// <summary>
    /// Tells the tree again whether the folder is open, for a folder shown again after a search hid it, or one that was
    /// empty when it opened and has rows now (ProductFolderNode.AnnounceExpanded says why).
    /// </summary>
    internal void AnnounceExpanded() => this.RaisePropertyChanged(nameof(IsExpanded));
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
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(Title));
            }
        }
    }

    public SubAssembly SubAssembly => Row.SubAssembly;

    public override string Title => Row.Name;

    public override string Glyph => string.Empty;

    public override bool IsFolder => false;
}

/// <summary>
/// The Sub-Assemblies list as a tree: a folder for every role, in the roles' order (Settings → Roles), each holding the
/// sub-assemblies in that role (by name). Refresh merges rather than rebuilds, as RawMaterialTree's does: every folder and
/// sub-assembly keeps its node, which the tree's selection and open folders are tied to.
/// </summary>
public sealed class SubAssemblyTree : IDisposable
{
    private readonly Dictionary<int, SubAssemblyLeafNode> _leaves = [];

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
    public IObservable<RxVoid> ExpansionChanged => _memory.ExpansionChanged;

    /// <summary>The sub-assembly's row, or null while it isn't listed (a search hides it, or it isn't loaded).</summary>
    public SubAssemblyLeafNode? Find(int subAssemblyId) => _leaves.GetValueOrDefault(subAssemblyId);

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

    public void Dispose() => _memory.Dispose();

    /// <summary>
    /// Where dropping <paramref name="subAssembly"/> on <paramref name="target"/> files it: a role's folder puts it in that
    /// role, and another sub-assembly in that one's role. Null when that's its role already, for a role of the other kind
    /// (a packaging set stays in the Packaging role, and a recipe out of it), or with nothing to drop on.
    /// </summary>
    public static SubAssemblyMove? MoveTo(SubAssemblyTreeNode? target, SubAssembly subAssembly)
    {
        (int RoleId, bool IsPackaging)? place = target switch
        {
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

        foreach (var row in rows)
        {
            if (_leaves.TryGetValue(row.Id, out var leaf))
            {
                leaf.Row = row;
            }
            else
            {
                _leaves[row.Id] = new SubAssemblyLeafNode(row);
            }
        }

        foreach (var gone in _leaves.Keys.Except(rows.Select(r => r.Id)).ToList())
        {
            _leaves.Remove(gone);
        }

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
            [.. allRoles.Select(role => (_folders[role.Id], (List<SubAssemblyTreeNode>)[.. byRole[role.Id].Select(r => _leaves[r.Id])]))];
        List<SubAssemblyFolderNode> shown = [.. wanted.Where(f => !isFiltered || f.Rows.Count > 0).Select(f => f.Folder)];

        // Everything leaving the tree goes before anything arrives, so a sub-assembly moving from one folder to another is
        // never in both at once.
        TreeRows.RemoveMissing(Roots, shown);
        foreach (var (folder, folderRows) in wanted)
        {
            TreeRows.RemoveMissing(folder.Children, folderRows);
        }

        var added = TreeRows.Arrange(Roots, shown);
        List<SubAssemblyFolderNode> filled = [];
        foreach (var (folder, folderRows) in wanted)
        {
            if (folder.Children.Count == 0 && folderRows.Count > 0)
            {
                filled.Add(folder);
            }

            TreeRows.Arrange(folder.Children, folderRows);
            folder.ShowCount(folderRows.Count);
        }

        // Every match shows. The folders stay open once the search is cleared, as they would in a file explorer.
        if (isFiltered)
        {
            foreach (var folder in shown)
            {
                folder.IsExpanded = true;
            }
        }

        // The tree is told again about a folder shown (for the first time, or again after a search hid it), and one that
        // has just got its first rows: it can't open a folder with nothing in it, and doesn't look at it again when rows
        // arrive (ProductTree.Refresh says more). The roles come in before their sub-assemblies on a load, so on the first
        // one every folder is shown empty and filled a moment later.
        foreach (var folder in added.Union(filled).Where(f => f.IsExpanded))
        {
            folder.AnnounceExpanded();
        }
    }
}
