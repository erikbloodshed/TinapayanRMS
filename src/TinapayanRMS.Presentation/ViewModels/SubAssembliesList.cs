using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// What the Sub-Assemblies list asks of its screen, on top of what any record screen's list asks: the sub-assembly the form
/// shows, and what the list's actions do to it.
/// </summary>
public interface ISubAssembliesListHost : IRecordListHost
{
    /// <summary>The sub-assembly picked, which the form shows; null for a new one.</summary>
    StandaloneSubAssemblyRow? SelectedRow { get; }

    /// <summary>The sub-assembly the form holds: 0 for a new one, or a copy, not saved yet.</summary>
    int EditingId { get; }

    /// <summary>The role the form's sub-assembly is in, which a new one's row is filed under.</summary>
    SubAssemblyRole? EditingRole { get; }

    /// <summary>Reads the screen's lists again (its LoadCommand), keeping the form and what's typed into it.</summary>
    Task LoadAsync();

    /// <summary>A sub-assembly's row picked in the list: it opens, once the form's unsaved changes are saved or let go.</summary>
    void Pick(StandaloneSubAssemblyRow row);

    /// <summary>A fresh form for a new sub-assembly in <paramref name="role"/>, or the first role.</summary>
    void StartNew(SubAssemblyRole? role);

    /// <summary>Deletes the sub-assembly once it's confirmed, as the header's Delete does.</summary>
    Task DeleteAsync(StandaloneSubAssemblyRow row);

    /// <summary>The name typed into the new sub-assembly's row goes into the form, as typed there: one undo step.</summary>
    void NameNew(string name);

    /// <summary>The new sub-assembly's row went with Esc: the name its form was given goes too, as no edit of the user's.</summary>
    void ClearNewName();

    /// <summary>The sub-assembly the form shows was renamed in the list, saved: the form and its undo steps take the name as given.</summary>
    void RenamedOnForm(string name);

    /// <summary>The sub-assembly the form shows was moved to another role, saved: the form and its undo steps take the role as given.</summary>
    void MovedOnForm(int roleId);

    /// <summary>A role was deleted: the form's undo steps forget it, so none puts back a role there's no longer any of.</summary>
    void RoleDeleted(int roleId);
}

/// <summary>
/// The Sub-Assemblies list (through SubAssembliesViewModel.List; SubAssemblyList draws it): the standalone sub-assemblies,
/// searched by name and filed in a folder for each role, the rows picked, its menus and keys, moves and their Undo, naming in
/// place, the roles as its folders, and Make In-House. What its actions do to the sub-assembly the form shows, it asks of the
/// screen (<see cref="ISubAssembliesListHost"/>).
/// </summary>
public sealed class SubAssembliesList : RecordList, IListTreeScreen, IDisposable
{
    /// <summary>The view state key the closed folders of the Sub-Assemblies list are kept under (IViewStateStore).</summary>
    public const string ClosedFoldersKey = "SubAssemblies.ClosedFolders";

    /// <summary>What the name box on a role's folder says when pointed at: the prompts used to say it.</summary>
    private const string RoleHint =
        "A role: a folder here, and a section in the recipes that use it. Its place and whether products own it are set in Settings, on the Roles tab.";

    private readonly ISubAssembliesListHost _host;
    private readonly ObservableCollection<SubAssemblyRole> _roles;
    private readonly IReadOnlyList<RawMaterial> _ingredients;
    private readonly IReadOnlyList<RawMaterial> _packagingMaterials;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyRoleRepository _roleRepository;
    private readonly IRawMaterialCategoryRepository _rawMaterialCategoryRepository;
    private readonly IValidator<SubAssemblyRole> _roleValidator;
    private readonly SourceCache<StandaloneSubAssemblyRow, int> _rowCache = new(r => r.Id);
    private readonly ReadOnlyObservableCollection<StandaloneSubAssemblyRow> _rows;
    private readonly ReadOnlyObservableCollection<StandaloneSubAssemblyRow> _visibleRows;
    private readonly FolderStateWriter _closedFoldersWriter;

    /// <summary>Each search typed, what the list's filter follows: the sub-assemblies whose name holds it.</summary>
    private readonly BehaviorSubject<string> _searches = new(string.Empty);

    /// <summary>The roles the sub-assemblies the last move took were in before it: what Undo on the notice puts back.</summary>
    private IReadOnlyList<SubAssemblyMove> _moveUndo = [];

    /// <param name="host">The screen, which the list asks for the sub-assembly its form shows, and what to do to it.</param>
    /// <param name="roles">The roles as the screen last loaded them, which are the list's folders.</param>
    /// <param name="ingredients">The ingredients as the screen last loaded them, which a made ingredient's name can't be.</param>
    /// <param name="packagingMaterials">The packaging materials as the screen last loaded them, which a made ingredient's name can't be either.</param>
    public SubAssembliesList(
        ISubAssembliesListHost host,
        ObservableCollection<SubAssemblyRole> roles,
        IReadOnlyList<RawMaterial> ingredients,
        IReadOnlyList<RawMaterial> packagingMaterials,
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyRoleRepository roleRepository,
        IRawMaterialCategoryRepository rawMaterialCategoryRepository,
        IValidator<SubAssemblyRole> roleValidator,
        IViewStateStore viewStateStore)
    {
        _host = host;
        _roles = roles;
        _ingredients = ingredients;
        _packagingMaterials = packagingMaterials;
        _subAssemblyRepository = subAssemblyRepository;
        _roleRepository = roleRepository;
        _rawMaterialCategoryRepository = rawMaterialCategoryRepository;
        _roleValidator = roleValidator;

        // The list's folders open as they were left last time, a folder new since included, and the ones closed are written
        // for next time (FolderStateWriter), as on Raw Materials; Dispose writes any change still waiting. As read, so
        // nothing is written until a folder is opened or closed: the folders aren't made until the roles are loaded.
        SubAssemblyTree = new SubAssemblyTree(FolderMemory.Read(viewStateStore, ClosedFoldersKey));
        _closedFoldersWriter = new FolderStateWriter(viewStateStore, ClosedFoldersKey, SubAssemblyTree.ExpansionChanged, SubAssemblyTree.ClosedFolderKeys);

        var byName = SortExpressionComparer<StandaloneSubAssemblyRow>.Ascending(r => r.Name);

        _rowCache.Connect()
            .SortAndBind(out _rows, byName)
            .Subscribe();

        // The list's own filter, as Raw Materials and Products filter theirs: by name, ignoring case.
        var nameFilter = _searches
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<StandaloneSubAssemblyRow, bool>)(r => text.Length == 0 || r.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        // The list's tree follows the rows it shows once each change has reached them (a sub-assembly added, removed,
        // renamed, moved or filtered out) and the roles (one added, renamed, reordered or deleted).
        _rowCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleRows, byName)
            .Subscribe(_ => RefreshTree());

        // Through PropertyChanged, which a role added, removed or replaced raises too, for the reason
        // RecordFormViewModel.WatchForSave gives.
        ((INotifyPropertyChanged)roles).PropertyChanged += (_, _) => RefreshTree();

        // The search decides whether empty folders show, even when it leaves the same sub-assemblies showing (SearchText).
        RefreshTree();

        MoveSubAssembliesCommand = new ScreenCommand<IReadOnlyList<SubAssemblyMove>>(
            moves => host.RunSafelyAsync(() => MoveSubAssembliesAsync(moves, isUndo: false)));
        UndoMoveCommand = new ScreenCommand(() => host.RunSafelyAsync(() => MoveSubAssembliesAsync(_moveUndo, isUndo: true)), () => MoveNotice is not null);
        NewSubAssemblyInCommand = new ScreenCommand<SubAssemblyFolderNode?>(folder => host.AfterUnsavedChangesAsync(() => NewInList(
            folder is null ? null : _roles.FirstOrDefault(r => r.Id == folder.RoleId))));
        RenameSubAssemblyCommand = ScreenCommand.Of<SubAssemblyLeafNode>(BeginRenameSubAssembly);
        DeleteSubAssemblyCommand = new ScreenCommand<SubAssemblyLeafNode>(leaf => host.DeleteAsync(leaf.Row));
        MakeInHouseIngredientCommand = new ScreenCommand<SubAssemblyLeafNode>(leaf => host.RunSafelyAsync(() => MakeInHouseIngredientAsync(leaf.Row)));
        NewRoleCommand = ScreenCommand.Of(BeginNewFolder);
        RenameRoleCommand = ScreenCommand.Of<SubAssemblyFolderNode>(BeginRenameRole);
        DeleteRoleCommand = new ScreenCommand<SubAssemblyFolderNode>(DeleteRoleAsync);
        ExpandAllCommand = ScreenCommand.Of(SubAssemblyTree.ExpandAll);
        CollapseAllCommand = ScreenCommand.Of(SubAssemblyTree.CollapseAll);
    }

    /// <summary>Every standalone sub-assembly, whatever the list is filtered to.</summary>
    public ReadOnlyObservableCollection<StandaloneSubAssemblyRow> Rows => _rows;

    /// <summary>The sub-assemblies the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<StandaloneSubAssemblyRow> VisibleRows => _visibleRows;

    /// <summary>The list's tree (SubAssemblyList draws it): VisibleRows filed in a folder for their role.</summary>
    public SubAssemblyTree SubAssemblyTree { get; }

    private string _searchText = string.Empty;
    /// <summary>
    /// What's typed into the list's search: the sub-assemblies whose name holds it show. It decides whether empty folders
    /// show too, even when it leaves the same sub-assemblies showing, and a name being typed into a row is given up first,
    /// since the search may hide the row.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (RaiseAndSetIfChanged(ref _searchText, value))
            {
                _searches.OnNext(value);
                CancelTreeName();
                RefreshTree();
            }
        }
    }

    /// <summary>The sub-assemblies picked in the list, in the order they were picked.</summary>
    public IReadOnlyList<SubAssemblyLeafNode> SelectedTreeSubAssemblies => [.. SelectedTreeNodes.OfType<SubAssemblyLeafNode>().Where(l => !l.IsNew)];

    /// <summary>Gives sub-assemblies another role, saved at once: a drag in the list, or its menu's Move To.</summary>
    public ScreenCommand<IReadOnlyList<SubAssemblyMove>> MoveSubAssembliesCommand { get; }

    /// <summary>Puts back the roles the last move took the sub-assemblies from (the notice bar's Undo).</summary>
    public ScreenCommand UndoMoveCommand { get; }

    /// <summary>
    /// The list's New … Here: a new sub-assembly in the folder's role. With no folder (the list's empty space), as New
    /// starts one. Like New, it adds the sub-assembly's row to the list for its name to be typed into (TreeNameEdit).
    /// </summary>
    public ScreenCommand<SubAssemblyFolderNode?> NewSubAssemblyInCommand { get; }

    /// <summary>The list's Rename: the sub-assembly's name becomes a box to type into, in place, and the name typed is saved at once.</summary>
    public ScreenCommand<SubAssemblyLeafNode> RenameSubAssemblyCommand { get; }

    /// <summary>The list's Delete, as the header's: asks first.</summary>
    public ScreenCommand<SubAssemblyLeafNode> DeleteSubAssemblyCommand { get; }

    /// <summary>
    /// Make In-House Ingredient on a recipe of the list: asks for the made ingredient's name and Made category, then adds
    /// that ingredient, made from the recipe, at once. The recipe stays as it is.
    /// </summary>
    public ScreenCommand<SubAssemblyLeafNode> MakeInHouseIngredientCommand { get; }

    /// <summary>
    /// Input: Make In-House Ingredient's prompt (MadeIngredientPrompt), its name box starting with the sub-assembly's name. Output:
    /// the name typed and the Made category picked, or null if it was cancelled. The page answers it with a dialog with a
    /// picker (Prompts).
    /// </summary>
    public Interaction<ChoicePromptRequest, ChoicePromptResult?> PromptForMadeIngredient { get; } = new();

    /// <summary>
    /// The list's New Folder: a "New folder" row for its name to be typed into (TreeNameEdit), and the name typed adds a
    /// role after the others, as Settings → Roles adds one.
    /// </summary>
    public ScreenCommand NewRoleCommand { get; }

    /// <summary>The list's Rename Role, typed in place, which keeps the role's place and whether products own it.</summary>
    public ScreenCommand<SubAssemblyFolderNode> RenameRoleCommand { get; }

    /// <summary>The list's Delete Role, offered only for a role no sub-assembly in the list is in, and never the Packaging role.</summary>
    public ScreenCommand<SubAssemblyFolderNode> DeleteRoleCommand { get; }

    public ScreenCommand ExpandAllCommand { get; }

    public ScreenCommand CollapseAllCommand { get; }

    /// <summary>The sub-assemblies as a load read them, merged into the list: rows whose values haven't changed stay as they are.</summary>
    public void TakeRows(IEnumerable<StandaloneSubAssemblyRow> rows) => _rowCache.MergeFrom(rows);

    private void RefreshTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its sub-assembly the one picked.
        ListSelection.Quietly(() => SubAssemblyTree.Refresh(_roles, VisibleRows, isFiltered: SearchText.Trim().Length > 0));

        // A folder picked stays picked. Otherwise the row goes back to the sub-assembly picked: one moved to another folder
        // leaves the tree's selection as it goes, and one a search hid gets its row back when the search ends.
        if (SelectedTreeNode is not SubAssemblyFolderNode)
        {
            SyncTreeSelection();
        }
    }

    protected override object? ShowingRow => _host.SelectedRow is { } row ? SubAssemblyTree.Find(row.Id) : SubAssemblyTree.PendingSubAssembly;

    protected override string PickedRecordsProperty => nameof(SelectedTreeSubAssemblies);

    // A sub-assembly's row picks it; the new sub-assembly's row is the form's already.
    protected override void PickRowFromList(object row)
    {
        if (row is SubAssemblyLeafNode { IsNew: false } leaf && leaf.Row.Id != _host.SelectedRow?.Id)
        {
            _host.Pick(leaf.Row);
        }
    }

    /// <summary>
    /// Saves <paramref name="moves"/> and files the sub-assemblies in their new folders. On the one the form shows, the
    /// Role picker follows, and every undo step takes the new role as given, so no undo puts the old one back for a Save to
    /// write; whatever else is typed there stays unsaved.
    /// </summary>
    private async Task MoveSubAssembliesAsync(IReadOnlyList<SubAssemblyMove> moves, bool isUndo)
    {
        if (moves.Count == 0)
        {
            return;
        }

        // Where each is now, which Undo puts back, and the rows picked, which the tree drops as they move.
        var undo = moves
            .Select(m => _rowCache.Lookup(m.SubAssemblyId))
            .Where(found => found.HasValue)
            .Select(found => new SubAssemblyMove(found.Value.Id, found.Value.SubAssembly.RoleId))
            .ToList();
        var picked = SelectedTreeNodes.ToList();

        await _subAssemblyRepository.MoveAsync(moves, CancellationToken.None);

        // The load only re-points the selection at the fresh copy: the form isn't loaded again, so what's typed stays.
        await _host.LoadAsync();
        if (moves.FirstOrDefault(m => m.SubAssemblyId == _host.EditingId) is { } onForm)
        {
            _host.MovedOnForm(onForm.RoleId);
        }

        // Picks again the rows the tree dropped from its selection as they moved.
        ListSelection.Restore(picked, r => r is not SubAssemblyLeafNode leaf || SubAssemblyTree.Find(leaf.Row.Id) == leaf);

        _moveUndo = isUndo ? [] : undo;
        MoveNotice = isUndo ? null : MoveNoticeFor(moves);
    }

    /// <summary>
    /// What the notice bar says a move did: "Moved Ube Halaya to Filling", "Moved 3 sub-assemblies to Filling", or, for
    /// sub-assemblies given different roles, "Moved 3 sub-assemblies".
    /// </summary>
    private string MoveNoticeFor(IReadOnlyList<SubAssemblyMove> moves)
    {
        var what = moves.Count == 1 && _rowCache.Lookup(moves[0].SubAssemblyId) is { HasValue: true } one ? one.Value.Name : $"{moves.Count} sub-assemblies";
        var roleId = moves[0].RoleId;
        return moves.All(m => m.RoleId == roleId) && _roles.FirstOrDefault(r => r.Id == roleId) is { } role
            ? $"Moved {what} to {role.Name}"
            : $"Moved {what}";
    }

    /// <summary>
    /// The menu a right-click opens on the list (SubAssemblyList): on <paramref name="node"/>'s row, which the list
    /// picks first (keeping the rows picked with it, when it was one of them), or on the list's empty space when it's null.
    /// A sub-assembly's menu covers every sub-assembly picked with it.
    /// </summary>
    public IReadOnlyList<TreeMenuItem> TreeMenuFor(SubAssemblyTreeNode? node) => node switch
    {
        SubAssemblyLeafNode { IsNew: true } or SubAssemblyFolderNode { IsNewFolder: true } => [],
        SubAssemblyLeafNode leaf => SubAssembliesMenu(SelectedTreeSubAssemblies is var picked && picked.Contains(leaf) ? picked : [leaf]),
        SubAssemblyFolderNode folder => FolderMenu(folder),
        _ => EmptySpaceMenu(),
    };

    /// <summary>
    /// The item a key runs with the one row picked, as that row's menu shows the key beside it (F2, Del); null when the
    /// menu has none, or when several rows are picked.
    /// </summary>
    public TreeMenuItem? TreeShortcut(string gesture) =>
        SelectedTreeNodes is [SubAssemblyTreeNode node]
            ? TreeMenuFor(node).FirstOrDefault(item => item.Gesture == gesture && item.IsEnabled)
            : null;

    IReadOnlyList<TreeMenuItem> IListTreeScreen.TreeMenuFor(object? node) => node is null or SubAssemblyTreeNode ? TreeMenuFor(node as SubAssemblyTreeNode) : [];

    /// <summary>Only sub-assemblies can be dragged, one or several: the folders are the roles, in their own order.</summary>
    bool IListTreeScreen.CanDrag(object node) => node is SubAssemblyLeafNode { IsNew: false };

    bool IListTreeScreen.CanDrop(IEnumerable<object> nodes, object? target) => DropMoves(nodes, target).Count > 0;

    async Task<object?> IListTreeScreen.DropAsync(IEnumerable<object> nodes, object? target)
    {
        var moves = DropMoves(nodes, target);
        if (moves.Count == 0 || !MoveSubAssembliesCommand.CanExecute(moves))
        {
            return null;
        }

        await MoveSubAssembliesCommand.ExecuteAsync(moves);
        return SubAssemblyTree.Find(moves[0].SubAssemblyId);
    }

    /// <summary>What dropping <paramref name="nodes"/> on <paramref name="target"/> would do (SubAssemblyTree.MovesTo): none unless every one is a record.</summary>
    private static IReadOnlyList<SubAssemblyMove> DropMoves(IEnumerable<object> nodes, object? target) =>
        nodes.ToList() is { Count: > 0 } dragging && dragging.All(node => node is SubAssemblyLeafNode)
            ? SubAssemblyTree.MovesTo(target as SubAssemblyTreeNode, dragging.Cast<SubAssemblyLeafNode>().Select(leaf => leaf.SubAssembly))
            : [];

    private List<TreeMenuItem> SubAssembliesMenu(IReadOnlyList<SubAssemblyLeafNode> leaves)
    {
        var subAssemblies = leaves.Select(l => l.SubAssembly).ToList();
        var isOne = subAssemblies.Count == 1;
        List<TreeMenuItem> items = [];

        // Only recipes move, among the recipe roles: every packaging set is in the one Packaging role, and a set picked
        // with recipes has no role it could share with them.
        if (!subAssemblies.Any(SubAssemblyTree.IsPackagingSet))
        {
            List<TreeMenuItem> roles =
            [
                .. _roles.Where(r => !r.IsPackaging).Select(r =>
                {
                    var moves = SubAssemblyTree.MovesTo(r, subAssemblies);
                    return new TreeMenuItem(r.Name, MoveSubAssembliesCommand, moves) { IsEnabled = moves.Count > 0 };
                }),
            ];
            items.Add(new($"Move {(isOne ? string.Empty : $"{subAssemblies.Count} Sub-Assemblies ")}to")
            {
                Icon = AppIcon.Move,
                Items = roles,
                IsEnabled = roles.Any(r => r.IsEnabled),
            });
        }

        if (isOne)
        {
            var leaf = leaves[0];
            if (items.Count > 0)
            {
                items.Add(TreeMenuItem.Separator);
            }

            items.Add(new("Rename", RenameSubAssemblyCommand, leaf) { Gesture = "F2", Icon = AppIcon.Rename });

            // A made ingredient can be made from a recipe; a packaging set's lines are packaging.
            if (!SubAssemblyTree.IsPackagingSet(leaf.SubAssembly))
            {
                items.Add(new("Make In-House Ingredient…", MakeInHouseIngredientCommand, leaf) { Icon = AppIcon.InHouse });
            }

            items.AddRange([TreeMenuItem.Separator, new("Delete", DeleteSubAssemblyCommand, leaf) { Gesture = "Del", Icon = AppIcon.Delete }]);
        }

        return items;
    }

    private List<TreeMenuItem> FolderMenu(SubAssemblyFolderNode folder)
    {
        List<TreeMenuItem> items =
        [
            new(folder.IsPackaging ? "New Packaging Set Here" : "New Sub-Assembly Here", NewSubAssemblyInCommand, folder) { Icon = AppIcon.Add },
            TreeMenuItem.Separator,
            new("New Folder", NewRoleCommand) { Icon = AppIcon.NewFolder },
            new("Rename Role", RenameRoleCommand, folder) { Gesture = "F2", Icon = AppIcon.Rename },
        ];

        // The Packaging role is never deleted, so it isn't offered. Every sub-assembly in it counts, not just a search's
        // matches: the database won't delete a role anything is in.
        if (!folder.IsPackaging)
        {
            var holds = Rows.Count(r => r.SubAssembly.RoleId == folder.RoleId);
            items.Add(new(holds == 0 ? "Delete Role" : $"Delete Role (holds {holds})", DeleteRoleCommand, folder)
            {
                Gesture = "Del",
                Icon = AppIcon.Delete,
                IsEnabled = holds == 0,
            });
        }

        items.AddRange([TreeMenuItem.Separator, new("Expand All", ExpandAllCommand), new("Collapse All", CollapseAllCommand)]);
        return items;
    }

    private List<TreeMenuItem> EmptySpaceMenu() =>
    [
        new("New Sub-Assembly", NewSubAssemblyInCommand) { Icon = AppIcon.Add },
        new("New Folder", NewRoleCommand) { Icon = AppIcon.NewFolder },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand),
        new("Collapse All", CollapseAllCommand),
    ];

    /// <summary>
    /// The list's Make In-House Ingredient, saved at once once the prompt is answered: a new made ingredient made from the
    /// recipe, in the Made category picked, with no add-ins and no weight loss. The
    /// recipe stays here as it is, and the products linking it are
    /// left alone; it shows under Dependents. The form's unsaved changes to it have to be saved or undone first: the made
    /// ingredient is made from it as it's saved.
    /// </summary>
    private async Task MakeInHouseIngredientAsync(StandaloneSubAssemblyRow row)
    {
        _host.ShowError(null);
        if (_host.EditingId == row.Id && _host.IsDirty)
        {
            _host.ShowError($"Save {row.Name} first, or undo its changes: the made ingredient is made from it as it's saved.");
            return;
        }

        var categories = await MadeIngredientPrompt.MadeCategoriesAsync(_rawMaterialCategoryRepository);
        if (categories.Count == 0)
        {
            _host.ShowError(MadeIngredientPrompt.NoMadeCategoryMessage);
            return;
        }

        var answer = await PromptForMadeIngredient.Handle(MadeIngredientPrompt.RequestFor(
            "Make In-House Ingredient",
            row.Name,
            categories,
            $"A made ingredient made from {row.Name}, with no weight loss, so it costs per gram what the recipe does. {row.Name} stays here as it is, and its lines are still changed here."));
        if (answer is null)
        {
            return;
        }

        var name = answer.Text.Trim();
        if (_ingredients.Concat(_packagingMaterials).Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _host.ShowError(MadeIngredientPrompt.NameTakenMessage(name));
            return;
        }

        var category = categories[answer.Choice];
        await _subAssemblyRepository.AddMadeIngredientAsync(row.Id, new MadeIngredientDraft(name, category.Id), CancellationToken.None);

        await _host.LoadAsync();
        _host.ShowSuccess($"{name} was added to {category.Name}, made from {row.Name}.");
    }

    /// <summary>
    /// The list's Rename: the sub-assembly's name becomes a box to type into, in place (TreeNameEdit), and the name typed
    /// is saved at once (<see cref="RenameSubAssemblyToAsync"/>).
    /// </summary>
    private void BeginRenameSubAssembly(SubAssemblyLeafNode leaf)
    {
        if (leaf.IsNew)
        {
            return;
        }

        CancelTreeName();
        ListNaming.Start(leaf, leaf.Row.Name);
    }

    /// <summary>
    /// The list's Rename Role: the folder's name becomes a box to type into, in place, and the name typed renames the role
    /// (<see cref="RenameRoleToAsync"/>), keeping its place and whether products own it.
    /// </summary>
    private void BeginRenameRole(SubAssemblyFolderNode folder)
    {
        if (_roles.FirstOrDefault(r => r.Id == folder.RoleId) is not { } role)
        {
            return;
        }

        CancelTreeName();
        ListNaming.Start(folder, role.Name, RoleHint);
    }

    /// <summary>
    /// The list's New Folder: a "New folder" row after the roles, picked, with its name ready to be typed, as in a file
    /// explorer; the name typed adds a role (<see cref="RecordList.CommitTreeNameAsync"/>). The search is cleared first, so
    /// the row shows.
    /// </summary>
    private void BeginNewFolder() => BeginNewFolderWith(SubAssemblyFolderNode.NewFolderTitle);

    private void BeginNewFolderWith(string text)
    {
        CancelTreeName();
        SearchText = string.Empty;
        ListNaming.Start(SubAssemblyTree.BeginNewFolder(), text, RoleHint);
    }

    /// <summary>
    /// New and New … Here: a fresh form for a new sub-assembly (<see cref="ISubAssembliesListHost.StartNew"/>), and its row
    /// in its role's folder, picked, with its name ready to be typed, as a new file in a file explorer. The name typed goes
    /// into the form (<see cref="RecordList.CommitTreeNameAsync"/>), and the row stays until Save files the sub-assembly or
    /// the form leaves it. The search is cleared first, so the row shows. With no role to put it in (none yet), the form
    /// starts as before.
    /// </summary>
    public void NewInList(SubAssemblyRole? role = null)
    {
        CancelTreeName();
        SearchText = string.Empty;
        _host.StartNew(role);
        if (_host.EditingRole is not { } inRole)
        {
            return;
        }

        var title = inRole.IsPackaging ? "New packaging set" : "New sub-assembly";
        if (SubAssemblyTree.BeginNewSubAssembly(new SubAssembly { Name = title, RoleId = inRole.Id, Role = inRole }) is { } row)
        {
            ListNaming.Start(row, title);
        }
    }

    /// <summary>
    /// Takes the new sub-assembly's row out of the list (<see cref="NewInList"/>), and the rows picked, and stops the naming
    /// in it: the form no longer shows the new sub-assembly, or it's saved and has a row of its own. The form is left as it is.
    /// </summary>
    public void EndNewRow() => ListNaming.EndNewRow(SubAssemblyTree.PendingSubAssembly, SubAssemblyTree.EndNewSubAssembly);

    /// <summary>Whether a row of the list is a new sub-assembly's or a new folder's, still to be named.</summary>
    protected override bool IsNewRow(object node) => node is SubAssemblyLeafNode { IsNew: true } or SubAssemblyFolderNode { IsNewFolder: true };

    /// <summary>
    /// What a name typed into the list's box does (<see cref="RecordList.CommitTreeNameAsync"/>), once it's taken for one. A
    /// sub-assembly's or a role's is renamed at once; a new folder's adds a role after the others, or picks the one of that
    /// name already; a new sub-assembly's goes into the form, as typed there, and its row takes it: it needs a line before
    /// Save can file it.
    /// </summary>
    protected override async Task CommitTreeNameToAsync(TreeNameEdit edit, string name, string text)
    {
        switch (edit.Node)
        {
            case SubAssemblyLeafNode { IsNew: true } row:
                NameNewSubAssembly(row, name);
                break;

            case SubAssemblyLeafNode leaf:
                await _host.RunSafelyAsync(() => RenameSubAssemblyToAsync(leaf.Row, name));
                if (_host.ErrorMessage is not null && SubAssemblyTree.Find(leaf.Row.Id) is { } stillListed)
                {
                    ListNaming.Edit = edit with { Node = stillListed, InitialText = text };
                }

                break;

            case SubAssemblyFolderNode { IsNewFolder: true } folder:
                ListNaming.EndNewRow(folder, SubAssemblyTree.EndNewFolder);
                await _host.RunSafelyAsync(() => AddRoleAsync(name));
                if (_host.ErrorMessage is not null)
                {
                    BeginNewFolderWith(text);
                }

                break;

            case SubAssemblyFolderNode folder:
                await _host.RunSafelyAsync(() => RenameRoleToAsync(folder, name));
                if (_host.ErrorMessage is not null && _roles.Any(r => r.Id == folder.RoleId))
                {
                    ListNaming.Edit = edit with { InitialText = text };
                }

                break;
        }
    }

    /// <summary>
    /// What Esc does to the row named (<see cref="RecordList.CancelTreeName"/>), once its box is closed: a new sub-assembly's
    /// or folder's row goes, with nothing saved. The form stays on the new sub-assembly, blank, as New leaves it.
    /// </summary>
    protected override void TreeNameCancelled(object node)
    {
        switch (node)
        {
            case SubAssemblyLeafNode { IsNew: true }:
                EndNewRow();
                _host.ClearNewName();
                break;
            case SubAssemblyFolderNode { IsNewFolder: true } folder:
                ListNaming.EndNewRow(folder, SubAssemblyTree.EndNewFolder);
                break;
        }
    }

    /// <summary>
    /// The name typed into a new sub-assembly's row: it goes into the form, as typed there (an undo step, which Save is
    /// offered for), and the row takes it, until Save files the sub-assembly or the form leaves it.
    /// </summary>
    private void NameNewSubAssembly(SubAssemblyLeafNode row, string name)
    {
        _host.NameNew(name);
        var subAssembly = row.SubAssembly;
        row.Row = new StandaloneSubAssemblyRow(new SubAssembly { Name = name, RoleId = subAssembly.RoleId, Role = subAssembly.Role }, []);
    }

    /// <summary>
    /// Saves <paramref name="name"/> as the sub-assembly's at once. On the one the form shows, the title and the name box
    /// take it, and the form's undo steps take it as given, as a move gives them its role: whatever else is typed there
    /// stays to be saved, and no undo brings the old name back for a Save to write.
    /// </summary>
    private async Task RenameSubAssemblyToAsync(StandaloneSubAssemblyRow row, string name)
    {
        await _subAssemblyRepository.RenameAsync(row.Id, name, CancellationToken.None);
        await _host.LoadAsync();
        if (_host.EditingId == row.Id)
        {
            _host.RenamedOnForm(name);
        }
    }

    /// <summary>
    /// Adds a role named <paramref name="name"/> after the others, then picks its folder. A name that's a role already picks
    /// that one's folder instead. A name refused shows why.
    /// </summary>
    private async Task AddRoleAsync(string name)
    {
        var role = _roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            var added = new SubAssemblyRole { Name = name };
            var validation = _roleValidator.Validate(added);
            if (!validation.IsValid)
            {
                _host.ShowError(validation.ToString(" "));
                return;
            }

            // AddAsync fills in the saved Id, which the copy read back is found by.
            await _roleRepository.AddAsync(added, CancellationToken.None);
            await _host.LoadAsync();
            role = _roles.FirstOrDefault(r => r.Id == added.Id);
        }

        if (role is not null && SubAssemblyTree.Folder(role.Id) is { } folder && SubAssemblyTree.Roots.Contains(folder))
        {
            SelectedTreeNode = folder;
        }
    }

    /// <summary>Renames the folder's role to <paramref name="name"/>, keeping its place and whether products own it. A name refused shows why.</summary>
    private async Task RenameRoleToAsync(SubAssemblyFolderNode folder, string name)
    {
        if (_roles.FirstOrDefault(r => r.Id == folder.RoleId) is not { } role)
        {
            return;
        }

        var renamed = new SubAssemblyRole
        {
            Id = role.Id,
            Name = name,
            SortOrder = role.SortOrder,
            IsPackaging = role.IsPackaging,
            IsProductOwned = role.IsProductOwned,
        };
        var validation = _roleValidator.Validate(renamed);
        if (!validation.IsValid)
        {
            _host.ShowError(validation.ToString(" "));
            return;
        }

        // Everything is read again, the sub-assemblies with it, which carry their role's name. The Role picker keeps the
        // role it holds (the screen's LoadCommand), under its new name.
        await _roleRepository.UpdateAsync(renamed, CancellationToken.None);
        await _host.LoadAsync();
    }

    /// <summary>
    /// The list's Delete Role, once it's confirmed: only for a role no sub-assembly in the list is in, and never the
    /// Packaging role. The menu greys it out, or leaves it out, otherwise. A role a product's own sub-assembly is in, which
    /// the list doesn't show, is refused by the database, and the screen says so.
    /// </summary>
    private Task DeleteRoleAsync(SubAssemblyFolderNode folder)
    {
        if (_roles.FirstOrDefault(r => r.Id == folder.RoleId) is not { } role
            || role.IsPackaging
            || Rows.Any(r => r.SubAssembly.RoleId == role.Id))
        {
            return Task.CompletedTask;
        }

        return _host.RunWithConfirmationAsync(
            $"Delete the '{role.Name}' role? It holds no sub-assemblies here. This can't be undone.",
            async () =>
            {
                await _roleRepository.DeleteAsync(role.Id, CancellationToken.None);

                // A new sub-assembly's form may have had it picked: the undo steps forget it, so none puts back a role
                // there's no longer any of to save.
                await _host.LoadAsync();
                _host.RoleDeleted(role.Id);
            });
    }

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        _closedFoldersWriter.Dispose();
        SubAssemblyTree.Dispose();
        _rowCache.Dispose();
        _searches.Dispose();
    }
}
