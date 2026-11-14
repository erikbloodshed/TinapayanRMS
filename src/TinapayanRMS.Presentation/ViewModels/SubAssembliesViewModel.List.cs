using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.ViewModels;

// The list: its filter and tree, its menus and keys, moves and their Undo, naming in place, the roles as its folders,
// and Make In-House.
public sealed partial class SubAssembliesViewModel
{
    /// <summary>The list's rows, filtered by the search and filed in their roles' folders.</summary>
    [MemberNotNull(nameof(_rows), nameof(_visibleRows))]
    private void WireList()
    {
        var byName = SortExpressionComparer<StandaloneSubAssemblyRow>.Ascending(r => r.Name);

        _rowCache.Connect()
            .SortAndBind(out _rows, byName)
            .Subscribe();

        // The list's own filter, as Raw Materials and Products filter theirs: by name, ignoring case.
        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<StandaloneSubAssemblyRow, bool>)(r => text.Length == 0 || r.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        // The list's tree follows the rows it shows once each change has reached them (a sub-assembly added, removed,
        // renamed, moved or filtered out) and the roles (one added, renamed, reordered or deleted).
        _rowCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleRows, byName)
            .Subscribe(_ => RefreshTree());

        // Through PropertyChanged, which a role added, removed or replaced raises too, for the reason WatchForSave gives.
        ((INotifyPropertyChanged)Roles).PropertyChanged += (_, _) => RefreshTree();

        // The search decides whether empty folders show, even when it leaves the same sub-assemblies showing. A name being
        // typed into a row is given up first: the search may hide the row.
        this.WhenAnyValue(x => x.SearchText)
            .Skip(1)
            .Subscribe(_ => CancelTreeName());
        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => RefreshTree());

        // A sub-assembly opened, by its Save included, ends the new one's row: the form no longer shows the new one.
        this.WhenAnyValue(x => x.SelectedRow)
            .Where(row => row is not null)
            .Subscribe(_ => EndNewSubAssemblyRow());
        this.WhenAnyValue(x => x.SelectedRow)
            .Subscribe(_ => SyncTreeSelection());
    }

    /// <summary>The list's commands: moves and their Undo, its menus' New, Rename, Delete and Make In-House, and the roles.</summary>
    [MemberNotNull(
        nameof(MoveSubAssembliesCommand), nameof(UndoMoveCommand), nameof(NewSubAssemblyInCommand), nameof(RenameSubAssemblyCommand),
        nameof(DeleteSubAssemblyCommand), nameof(MakeInHouseIngredientCommand), nameof(NewRoleCommand), nameof(RenameRoleCommand),
        nameof(DeleteRoleCommand), nameof(ExpandAllCommand), nameof(CollapseAllCommand))]
    private void WireListCommands()
    {
        MoveSubAssembliesCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<SubAssemblyMove>>(
            moves => RunSafelyAsync(() => MoveSubAssembliesAsync(moves, isUndo: false)));
        var hasMoveNotice = this.WhenAnyValue(x => x.MoveNotice).Select(notice => notice is not null);
        UndoMoveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(() => MoveSubAssembliesAsync(_moveUndo, isUndo: true)), hasMoveNotice);
        NewSubAssemblyInCommand = ReactiveCommand.CreateFromTask<SubAssemblyFolderNode?>(folder => AfterUnsavedChangesAsync(() => NewSubAssemblyInList(
            folder is null ? null : Roles.FirstOrDefault(r => r.Id == folder.RoleId))));
        RenameSubAssemblyCommand = ReactiveCommand.Create<SubAssemblyLeafNode>(BeginRenameSubAssembly);
        DeleteSubAssemblyCommand = ReactiveCommand.CreateFromTask<SubAssemblyLeafNode>(leaf => DeleteSubAssemblyAsync(leaf.Row));
        MakeInHouseIngredientCommand = ReactiveCommand.CreateFromTask<SubAssemblyLeafNode>(leaf => RunSafelyAsync(() => MakeInHouseIngredientAsync(leaf.Row)));
        NewRoleCommand = ReactiveCommand.Create(BeginNewFolder);
        RenameRoleCommand = ReactiveCommand.Create<SubAssemblyFolderNode>(BeginRenameRole);
        DeleteRoleCommand = ReactiveCommand.CreateFromTask<SubAssemblyFolderNode>(DeleteRoleAsync);
        ExpandAllCommand = ReactiveCommand.Create(SubAssemblyTree.ExpandAll);
        CollapseAllCommand = ReactiveCommand.Create(SubAssemblyTree.CollapseAll);
    }

    private void RefreshTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its sub-assembly the one picked.
        ListSelection.Quietly(() => SubAssemblyTree.Refresh(Roles, VisibleRows, isFiltered: SearchText.Trim().Length > 0));

        // A folder picked stays picked. Otherwise the row goes back to the sub-assembly picked: one moved to another folder
        // leaves the tree's selection as it goes, and one a search hid gets its row back when the search ends.
        if (SelectedTreeNode is not SubAssemblyFolderNode)
        {
            SyncTreeSelection();
        }
    }

    protected override object? ShowingRow => SelectedRow is { } row ? SubAssemblyTree.Find(row.Id) : SubAssemblyTree.PendingSubAssembly;

    protected override string PickedRecordsProperty => nameof(SelectedTreeSubAssemblies);

    // A sub-assembly's row picks it; the new sub-assembly's row is the form's already.
    protected override void PickRowFromList(object row)
    {
        if (row is SubAssemblyLeafNode { IsNew: false } leaf && leaf.Row.Id != SelectedRow?.Id)
        {
            PickFromList(leaf.Row.Id, () => SelectedRow = leaf.Row, SyncTreeSelection, OpenSubAssemblyAsync);
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
        await LoadCommand.Execute();
        if (moves.FirstOrDefault(m => m.SubAssemblyId == _editingId) is { } onForm)
        {
            ApplyRefresh(() => EditingRole = Roles.FirstOrDefault(r => r.Id == onForm.RoleId));
            History.RewriteAll(snapshot => snapshot with { RoleId = onForm.RoleId });
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
        return moves.All(m => m.RoleId == roleId) && Roles.FirstOrDefault(r => r.Id == roleId) is { } role
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
        if (moves.Count == 0 || !((ICommand)MoveSubAssembliesCommand).CanExecute(moves))
        {
            return null;
        }

        await MoveSubAssembliesCommand.Execute(moves);
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
                .. Roles.Where(r => !r.IsPackaging).Select(r =>
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
        ErrorMessage = null;
        if (_editingId == row.Id && History.IsDirty)
        {
            ErrorMessage = $"Save {row.Name} first, or undo its changes: the made ingredient is made from it as it's saved.";
            return;
        }

        var categories = await MadeIngredientPrompt.MadeCategoriesAsync(_rawMaterialCategoryRepository);
        if (categories.Count == 0)
        {
            ErrorMessage = MadeIngredientPrompt.NoMadeCategoryMessage;
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
        if (Ingredients.Concat(PackagingMaterials).Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = MadeIngredientPrompt.NameTakenMessage(name);
            return;
        }

        var category = categories[answer.Choice];
        await _subAssemblyRepository.AddMadeIngredientAsync(row.Id, new MadeIngredientDraft(name, category.Id), CancellationToken.None);

        await LoadCommand.Execute();
        SuccessMessage = $"{name} was added to {category.Name}, made from {row.Name}.";
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
        if (Roles.FirstOrDefault(r => r.Id == folder.RoleId) is not { } role)
        {
            return;
        }

        CancelTreeName();
        ListNaming.Start(folder, role.Name, RoleHint);
    }

    /// <summary>
    /// The list's New Folder: a "New folder" row after the roles, picked, with its name ready to be typed, as in a file
    /// explorer; the name typed adds a role (<see cref="CommitTreeNameAsync"/>). The search is cleared first, so the row shows.
    /// </summary>
    private void BeginNewFolder() => BeginNewFolderWith(SubAssemblyFolderNode.NewFolderTitle);

    private void BeginNewFolderWith(string text)
    {
        CancelTreeName();
        SearchText = string.Empty;
        ListNaming.Start(SubAssemblyTree.BeginNewFolder(), text, RoleHint);
    }

    /// <summary>What the name box on a role's folder says when pointed at: the prompts used to say it.</summary>
    private const string RoleHint =
        "A role: a folder here, and a section in the recipes that use it. Its place and whether products own it are set in Settings, on the Roles tab.";

    /// <summary>
    /// New and New … Here: <see cref="StartNewSubAssembly"/>, and the new sub-assembly's row in its role's folder, picked,
    /// with its name ready to be typed, as a new file in a file explorer. The name typed goes into the form
    /// (<see cref="CommitTreeNameAsync"/>), and the row stays until Save files the sub-assembly or the form leaves it. The
    /// search is cleared first, so the row shows. With no role to put it in (none yet), the form starts as before.
    /// </summary>
    private void NewSubAssemblyInList(SubAssemblyRole? role = null)
    {
        CancelTreeName();
        SearchText = string.Empty;
        StartNewSubAssembly(role);
        if (EditingRole is not { } inRole)
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
    /// Takes the new sub-assembly's row out of the list (<see cref="NewSubAssemblyInList"/>), and the rows picked, and stops
    /// the naming in it: the form no longer shows the new sub-assembly, or it's saved and has a row of its own. The form is
    /// left as it is.
    /// </summary>
    private void EndNewSubAssemblyRow() => ListNaming.EndNewRow(SubAssemblyTree.PendingSubAssembly, SubAssemblyTree.EndNewSubAssembly);

    /// <summary>Whether a row of the list is a new sub-assembly's or a new folder's, still to be named.</summary>
    protected override bool IsNewRow(object node) => node is SubAssemblyLeafNode { IsNew: true } or SubAssemblyFolderNode { IsNewFolder: true };

    /// <summary>
    /// What a name typed into the list's box does (<see cref="ListFormViewModel{TSnapshot}.CommitTreeNameAsync"/>), once
    /// it's taken for one. A sub-assembly's or a role's is renamed at once; a new folder's adds a role after the others, or
    /// picks the one of that name already; a new sub-assembly's goes into the form, as typed there, and its row takes it: it
    /// needs a line before Save can file it.
    /// </summary>
    protected override async Task CommitTreeNameToAsync(TreeNameEdit edit, string name, string text)
    {
        switch (edit.Node)
        {
            case SubAssemblyLeafNode { IsNew: true } row:
                NameNewSubAssembly(row, name);
                break;

            case SubAssemblyLeafNode leaf:
                await RunSafelyAsync(() => RenameSubAssemblyToAsync(leaf.Row, name));
                if (ErrorMessage is not null && SubAssemblyTree.Find(leaf.Row.Id) is { } stillListed)
                {
                    ListNaming.Edit = edit with { Node = stillListed, InitialText = text };
                }

                break;

            case SubAssemblyFolderNode { IsNewFolder: true } folder:
                ListNaming.EndNewRow(folder, SubAssemblyTree.EndNewFolder);
                await RunSafelyAsync(() => AddRoleAsync(name));
                if (ErrorMessage is not null)
                {
                    BeginNewFolderWith(text);
                }

                break;

            case SubAssemblyFolderNode folder:
                await RunSafelyAsync(() => RenameRoleToAsync(folder, name));
                if (ErrorMessage is not null && Roles.Any(r => r.Id == folder.RoleId))
                {
                    ListNaming.Edit = edit with { InitialText = text };
                }

                break;
        }
    }

    /// <summary>
    /// What Esc does to the row named (<see cref="ListFormViewModel{TSnapshot}.CancelTreeName"/>), once its box is closed:
    /// a new sub-assembly's or folder's row goes, with nothing saved. The form stays on the new sub-assembly, blank, as New
    /// leaves it.
    /// </summary>
    protected override void TreeNameCancelled(object node)
    {
        switch (node)
        {
            case SubAssemblyLeafNode { IsNew: true }:
                EndNewSubAssemblyRow();
                ApplyRefresh(() => EditingName = string.Empty);
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
        AsUndoStep(() => EditingName = name);
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
        await LoadCommand.Execute();
        if (_editingId == row.Id)
        {
            HeaderTitle = name;
            ApplyRefresh(() => EditingName = name);
            History.RewriteAll(snapshot => snapshot with { Name = name });
        }
    }

    /// <summary>
    /// Adds a role named <paramref name="name"/> after the others, then picks its folder. A name that's a role already picks
    /// that one's folder instead. A name refused shows why.
    /// </summary>
    private async Task AddRoleAsync(string name)
    {
        var role = Roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            var added = new SubAssemblyRole { Name = name };
            var validation = _roleValidator.Validate(added);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            // AddAsync fills in the saved Id, which the copy read back is found by.
            await _roleRepository.AddAsync(added, CancellationToken.None);
            await LoadCommand.Execute();
            role = Roles.FirstOrDefault(r => r.Id == added.Id);
        }

        if (role is not null && SubAssemblyTree.Folder(role.Id) is { } folder && SubAssemblyTree.Roots.Contains(folder))
        {
            SelectedTreeNode = folder;
        }
    }

    /// <summary>Renames the folder's role to <paramref name="name"/>, keeping its place and whether products own it. A name refused shows why.</summary>
    private async Task RenameRoleToAsync(SubAssemblyFolderNode folder, string name)
    {
        if (Roles.FirstOrDefault(r => r.Id == folder.RoleId) is not { } role)
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
            ErrorMessage = validation.ToString(" ");
            return;
        }

        // Everything is read again, the sub-assemblies with it, which carry their role's name. The Role picker keeps the
        // role it holds (LoadCommand), under its new name.
        await _roleRepository.UpdateAsync(renamed, CancellationToken.None);
        await LoadCommand.Execute();
    }

    /// <summary>
    /// The list's Delete Role, once it's confirmed: only for a role no sub-assembly in the list is in, and never the
    /// Packaging role. The menu greys it out, or leaves it out, otherwise. A role a product's own sub-assembly is in, which
    /// the list doesn't show, is refused by the database, and the screen says so.
    /// </summary>
    private Task DeleteRoleAsync(SubAssemblyFolderNode folder)
    {
        if (Roles.FirstOrDefault(r => r.Id == folder.RoleId) is not { } role
            || role.IsPackaging
            || Rows.Any(r => r.SubAssembly.RoleId == role.Id))
        {
            return Task.CompletedTask;
        }

        return RunWithConfirmationAsync(
            $"Delete the '{role.Name}' role? It holds no sub-assemblies here. This can't be undone.",
            async () =>
            {
                await _roleRepository.DeleteAsync(role.Id, CancellationToken.None);

                // A new sub-assembly's form may have had it picked: the undo steps forget it, so none puts back a role
                // there's no longer any of to save.
                await LoadCommand.Execute();
                History.RewriteAll(snapshot => snapshot.RoleId == role.Id ? snapshot with { RoleId = null } : snapshot);
            });
    }
}
