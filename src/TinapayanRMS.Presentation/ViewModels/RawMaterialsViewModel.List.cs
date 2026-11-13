using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Presentation.ViewModels;

// The list: its tree, search, menus, moves, naming in place and categories, and the pickers' lists.
public sealed partial class RawMaterialsViewModel
{
    /// <summary>
    /// The list's tree follows the materials, the categories and the search, and its selection the material showing;
    /// the pickers' lists follow the materials too.
    /// </summary>
    [MemberNotNull(
        nameof(_categories), nameof(_rawMaterials), nameof(_visibleRawMaterials), nameof(_ingredients),
        nameof(_linkTargets), nameof(_addInOptions), nameof(_categoryOptions))]
    private void WireLists()
    {
        SelectedTreeNodes.CollectionChanged += (_, _) => TreeSelectionChanged();

        var byName = SortExpressionComparer<RawMaterial>.Ascending(m => m.Name);

        // The list's tree follows both lists once each change has reached them: a material added, removed, renamed, moved
        // or filtered out, and a category added, renamed or deleted. The categories are bound first, as the tree reads both.
        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe(_ => RefreshTree());

        _rawMaterialCache.Connect()
            .SortAndBind(out _rawMaterials, byName)
            .Subscribe();

        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<RawMaterial, bool>)(m => text.Length == 0 || m.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        _rawMaterialCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleRawMaterials, byName)
            .Subscribe(_ => RefreshTree());

        // The search decides whether empty folders show, even when it leaves the same materials showing. A name being typed
        // into a row is given up first: the search may hide the row.
        this.WhenAnyValue(x => x.SearchText)
            .Skip(1)
            .Subscribe(_ => CancelTreeName());
        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => RefreshTree());

        // A material opened, by its Save included, ends the new material's row: the form no longer shows the new one.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(material => material is not null)
            .Subscribe(_ => EndNewMaterialRow());
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Subscribe(_ => SyncTreeSelection());

        _rawMaterialCache.Connect()
            .Filter(m => m.Type == RawMaterialType.Ingredient)
            .SortAndBind(out _ingredients, byName)
            .Subscribe();

        // Shared by every picker that must leave out the ingredient being edited: Linked's target and an add-in.
        var notTheSelection = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(selected => selected?.Id ?? 0)
            .DistinctUntilChanged()
            .Select(selectedId => (Func<RawMaterial, bool>)(m => m.Type == RawMaterialType.Ingredient && m.Id != selectedId));

        _rawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out _linkTargets, byName)
            .Subscribe();

        _rawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out _addInOptions, byName)
            .Subscribe();

        // The picker offers only the categories of the material's own type, which is all it can be saved in: a made
        // ingredient's are the Made ones (RawMaterialRules.CategoryTypeFor).
        var categoryType = this.WhenAnyValue(x => x.EditingType, x => x.EditingSource, RawMaterialRules.CategoryTypeFor);
        _categoryCache.Connect()
            .Filter(categoryType.Select(type => (Func<RawMaterialCategory, bool>)(c => c.Type == type)))
            .SortAndBind(out _categoryOptions, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();

        // A new material's type or source changed under a category of another type: that category is no longer one it can
        // go in. Only a new one's: a saved material's are fixed, and loading one sets its category before its source.
        categoryType
            .Where(type => CanChangeTypeAndSource && EditingCategory is { } category && category.Type != type)
            .Subscribe(_ => EditingCategory = null);
    }

    /// <summary>The list's commands, each run on the folder or material its menu was opened on (TreeMenuFor).</summary>
    [MemberNotNull(
        nameof(MoveMaterialsCommand), nameof(UndoMoveCommand), nameof(DismissMoveNoticeCommand),
        nameof(NewMaterialInCommand), nameof(RenameMaterialCommand), nameof(CopyMaterialCommand),
        nameof(DeleteMaterialCommand), nameof(NewCategoryCommand), nameof(RenameCategoryCommand),
        nameof(DeleteCategoryCommand), nameof(ExpandAllCommand), nameof(CollapseAllCommand))]
    private void WireListCommands()
    {
        MoveMaterialsCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<RawMaterialMove>>(
            moves => RunSafelyAsync(() => MoveMaterialsAsync(moves, isUndo: false)));
        var hasMoveNotice = this.WhenAnyValue(x => x.MoveNotice).Select(notice => notice is not null);
        UndoMoveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(() => MoveMaterialsAsync(_moveUndo, isUndo: true)), hasMoveNotice);
        DismissMoveNoticeCommand = ReactiveCommand.Create(() => { MoveNotice = null; });
        NewMaterialInCommand = ReactiveCommand.CreateFromTask<RawMaterialFolderNode?>(folder => AfterUnsavedChangesAsync(() => NewMaterialInList(
            folder?.Type ?? RawMaterialType.Ingredient,
            folder?.CategoryId,
            folder?.CategoryType == RawMaterialCategoryType.Made ? RawMaterialSource.Made : RawMaterialSource.Purchased)));
        RenameMaterialCommand = ReactiveCommand.Create<RawMaterialLeafNode>(BeginRenameMaterial);
        CopyMaterialCommand = ReactiveCommand.CreateFromTask<RawMaterialLeafNode>(leaf => AfterUnsavedChangesAsync(() => StartCopyOf(leaf.RawMaterial)));
        DeleteMaterialCommand = ReactiveCommand.CreateFromTask<RawMaterialLeafNode>(leaf => DeleteRawMaterialAsync(leaf.RawMaterial));
        NewCategoryCommand = ReactiveCommand.Create<RawMaterialCategoryType>(BeginNewFolder);
        RenameCategoryCommand = ReactiveCommand.Create<RawMaterialFolderNode>(BeginRenameCategory);
        DeleteCategoryCommand = ReactiveCommand.CreateFromTask<RawMaterialFolderNode>(DeleteCategoryAsync);
        ExpandAllCommand = ReactiveCommand.Create<RawMaterialFolderNode?>(RawMaterialTree.ExpandAll);
        CollapseAllCommand = ReactiveCommand.Create<RawMaterialFolderNode?>(RawMaterialTree.CollapseAll);
    }

    public void BeginCategoryEdit()
    {
        if (!_isCommittingCategory)
        {
            _categoryBeforeEdit = EditingCategory;
        }
    }

    /// <summary>
    /// ICategoryPickerScreen: a name that isn't one of the material type's categories is offered as a new one, saved
    /// for that type and picked, so filing a material under a new category doesn't mean a trip to Settings and back.
    /// A cancelled prompt puts back the category the picker had; a name that turns out to be listed is picked instead.
    /// </summary>
    public async Task CommitCategoryTextAsync()
    {
        var typed = CategorySearchText.Trim();
        if (_isCommittingCategory || typed.Length == 0 || IsNamed(EditingCategory, typed))
        {
            return;
        }

        _isCommittingCategory = true;
        try
        {
            if (CategoryOptions.FirstOrDefault(c => IsNamed(c, typed)) is { } listed)
            {
                AsUndoStep(() => EditingCategory = listed);
                return;
            }

            var name = (await PromptForNewCategory.Handle(typed))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                AsUndoStep(() => EditingCategory = _categoryBeforeEdit);
            }
            else if (CategoryOptions.FirstOrDefault(c => IsNamed(c, name)) is { } named)
            {
                AsUndoStep(() => EditingCategory = named);
            }
            else
            {
                await AddCategoryAsync(name);
            }

            // The picker shows the category it now holds rather than what was typed: the new one, or the one put back.
            CategorySearchText = EditingCategory?.Name ?? string.Empty;
        }
        catch (Exception ex)
        {
            // Nothing awaits this: it's the picker's, from the keyboard or the focus leaving.
            ShowFailure(ex);
        }
        finally
        {
            _isCommittingCategory = false;
        }

        static bool IsNamed(RawMaterialCategory? category, string name) =>
            string.Equals(category?.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Saves a category of the material's own type, the only kind the picker offers it (a Made one for a made ingredient),
    /// and picks it. A refused name puts back the category the picker had.
    /// </summary>
    private async Task AddCategoryAsync(string name)
    {
        var added = false;
        await RunSafelyAsync(async () =>
        {
            var category = new RawMaterialCategory { Name = name, Type = RawMaterialRules.CategoryTypeFor(EditingType, EditingSource) };


            var validation = _categoryValidator.Validate(category);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            // AddAsync fills in the saved Id, which the reloaded copy to pick is found by.
            await _categoryRepository.AddAsync(category, CancellationToken.None);
            await LoadCommand.Execute();
            AsUndoStep(() => EditingCategory = Categories.FirstOrDefault(c => c.Id == category.Id));
            SuccessMessage = $"{category.Name} was added.";
            added = true;
        });

        if (!added)
        {
            AsUndoStep(() => EditingCategory = _categoryBeforeEdit);
        }
    }

    private void RefreshTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its material the one picked.
        _treeSelection.Quietly(() => RawMaterialTree.Refresh(Categories, VisibleRawMaterials, isFiltered: SearchText.Trim().Length > 0));

        // A folder picked stays picked. Otherwise the row goes back to the material picked: one moved to another folder
        // leaves the tree's selection as it goes, and one a search hid gets its row back when the search ends.
        if (SelectedTreeNode is not RawMaterialFolderNode)
        {
            SyncTreeSelection();
        }
    }

    /// <summary>
    /// Puts SelectedRawMaterial's row first among the rows picked in the list, and so makes it SelectedTreeNode: alone,
    /// unless it's picked already, when the rows picked with it stay. Nothing picked, or a search hiding it, picks no row.
    /// </summary>
    private void SyncTreeSelection() =>
        _treeSelection.BringFirst(SelectedRawMaterial is { } material ? RawMaterialTree.Find(material.Id) : RawMaterialTree.PendingMaterial);

    /// <summary>
    /// The list's selection changed: by a click (or several, with Ctrl or Shift), or by this screen. Its first row is
    /// SelectedTreeNode, and a material's row picked by the user picks the material.
    /// </summary>
    private void TreeSelectionChanged()
    {
        var first = SelectedTreeNodes.Count > 0 ? SelectedTreeNodes[0] : null;
        this.RaisePropertyChanged(nameof(SelectedTreeMaterials));
        if (ReferenceEquals(first, _selectedTreeNode))
        {
            return;
        }

        _selectedTreeNode = first;
        this.RaisePropertyChanged(nameof(SelectedTreeNode));

        // The tree calls this in the middle of changing SelectedItems: picking the material leads to SyncTreeSelection,
        // which then finds its row first already and leaves the collection alone, as it must mid-change.
        // The new material's row is the form's already.
        if (!_treeSelection.IsSyncing && first is RawMaterialLeafNode { IsNew: false } leaf && leaf.RawMaterial.Id != SelectedRawMaterial?.Id)
        {
            PickFromList(leaf.RawMaterial.Id, () => SelectedRawMaterial = leaf.RawMaterial, SyncTreeSelection, OpenRawMaterialAsync);
        }
    }

    /// <summary>
    /// Saves <paramref name="moves"/> and files the materials in their new folders. On the material the form shows, the
    /// Category picker follows, and every undo step takes the new category as given, so no undo puts the old one back for a
    /// Save to write; whatever else is typed there stays unsaved.
    /// </summary>
    private async Task MoveMaterialsAsync(IReadOnlyList<RawMaterialMove> moves, bool isUndo)
    {
        if (moves.Count == 0)
        {
            return;
        }

        // Where each is now, which Undo puts back, and the rows picked, which the tree drops as they move.
        var undo = moves
            .Select(m => _rawMaterialCache.Lookup(m.RawMaterialId))
            .Where(found => found.HasValue)
            .Select(found => new RawMaterialMove(found.Value.Id, found.Value.CategoryId))
            .ToList();
        var picked = SelectedTreeNodes.ToList();

        await _rawMaterialRepository.MoveAsync(moves, CancellationToken.None);

        // The load only re-points the selection at the fresh copy: the form isn't loaded again, so what's typed stays.
        await LoadCommand.Execute();
        if (moves.FirstOrDefault(m => m.RawMaterialId == _editingId) is { } onForm)
        {
            ApplyRefresh(() => EditingCategory = Categories.FirstOrDefault(c => c.Id == onForm.CategoryId));
            History.RewriteAll(snapshot => snapshot with { CategoryId = onForm.CategoryId });
        }

        // Picks again the rows the tree dropped from its selection as they moved.
        _treeSelection.Restore(picked, r => r is not RawMaterialLeafNode leaf || RawMaterialTree.Find(leaf.RawMaterial.Id) == leaf);

        _moveUndo = isUndo ? [] : undo;
        MoveNotice = isUndo ? null : MoveNoticeFor(moves);
    }

    /// <summary>
    /// What the notice bar says a move did: "Moved Flour to Ingredients › Dry Goods", "Moved 3 materials to Ingredients ›
    /// Dry Goods", or, for materials sent to different categories, "Moved 3 materials".
    /// </summary>
    private string MoveNoticeFor(IReadOnlyList<RawMaterialMove> moves)
    {
        var what = moves.Count == 1 && _rawMaterialCache.Lookup(moves[0].RawMaterialId) is { HasValue: true } one ? one.Value.Name : $"{moves.Count} materials";
        var categoryId = moves[0].CategoryId;
        return moves.All(m => m.CategoryId == categoryId) && Categories.FirstOrDefault(c => c.Id == categoryId) is { } category
            ? $"Moved {what} to {RawMaterialFolderNode.TypeTitle(RawMaterialRules.MaterialTypeOf(category.Type))} › {category.Name}"
            : $"Moved {what}";
    }

    /// <summary>
    /// The menu a right-click opens on the list (MaterialList): on <paramref name="node"/>'s row, which the list picks
    /// first (keeping the rows picked with it, when it was one of them), or on the list's empty space when it's null. A
    /// material's menu covers every material picked with it.
    /// </summary>
    public IReadOnlyList<TreeMenuItem> TreeMenuFor(RawMaterialTreeNode? node) => node switch
    {
        RawMaterialLeafNode { IsNew: true } or RawMaterialFolderNode { IsNewFolder: true } => [],
        RawMaterialLeafNode leaf => MaterialsMenu(SelectedTreeMaterials is var picked && picked.Contains(leaf) ? picked : [leaf]),
        RawMaterialFolderNode { IsTypeFolder: true } typeFolder => TypeFolderMenu(typeFolder),
        RawMaterialFolderNode folder => CategoryFolderMenu(folder),
        _ => EmptySpaceMenu(),
    };

    /// <summary>
    /// The item a key runs with the one row picked, as that row's menu shows the key beside it (F2, Del); null when the
    /// menu has none, or when several rows are picked.
    /// </summary>
    public TreeMenuItem? TreeShortcut(string gesture) =>
        SelectedTreeNodes is [RawMaterialTreeNode node]
            ? TreeMenuFor(node).FirstOrDefault(item => item.Gesture == gesture && item.IsEnabled)
            : null;

    IReadOnlyList<TreeMenuItem> IListTreeScreen.TreeMenuFor(object? node) => node is null or RawMaterialTreeNode ? TreeMenuFor(node as RawMaterialTreeNode) : [];

    /// <summary>Only materials can be dragged, one or several: the folders are fixed.</summary>
    bool IListTreeScreen.CanDrag(IEnumerable<object> nodes) => nodes.All(node => node is RawMaterialLeafNode { IsNew: false });

    bool IListTreeScreen.CanDrop(IEnumerable<object> nodes, object? target) => DropMoves(nodes, target).Count > 0;

    IObservable<object?> IListTreeScreen.Drop(IEnumerable<object> nodes, object? target)
    {
        var moves = DropMoves(nodes, target);
        return moves.Count == 0
            ? Observable.Return<object?>(null)
            : MoveMaterialsCommand.Execute(moves).Select(_ => (object?)RawMaterialTree.Find(moves[0].RawMaterialId));
    }

    /// <summary>What dropping <paramref name="nodes"/> on <paramref name="target"/> would do (RawMaterialTree.MovesTo): none unless every one is a record.</summary>
    private static IReadOnlyList<RawMaterialMove> DropMoves(IEnumerable<object> nodes, object? target) =>
        nodes.ToList() is { Count: > 0 } dragging && dragging.All(node => node is RawMaterialLeafNode)
            ? RawMaterialTree.MovesTo(target as RawMaterialTreeNode, dragging.Cast<RawMaterialLeafNode>().Select(leaf => leaf.RawMaterial))
            : [];

    /// <summary>"Ingredient", "Made Ingredient" (in a Made category's folder) or "Packaging Material": what a New … Here item makes.</summary>
    private static string MaterialNoun(RawMaterialFolderNode folder) => folder switch
    {
        { CategoryType: RawMaterialCategoryType.Made } => "Made Ingredient",
        { Type: RawMaterialType.Ingredient } => "Ingredient",
        _ => "Packaging Material",
    };

    private List<TreeMenuItem> MaterialsMenu(IReadOnlyList<RawMaterialLeafNode> leaves)
    {
        var materials = leaves.Select(l => l.RawMaterial).ToList();
        var isOne = materials.Count == 1;
        List<TreeMenuItem> items = [];

        // A material only goes in a category of its own type, so there's nowhere to move ingredients and packaging together,
        // or made ingredients and the others.
        if (materials.Select(m => RawMaterialRules.CategoryTypeFor(m.Type, m.Source)).Distinct().ToList() is [var type])
        {
            List<TreeMenuItem> folders =
            [
                .. Categories.Where(c => c.Type == type).Select(c =>
                {
                    var moves = RawMaterialTree.MovesTo(c, materials);
                    return new TreeMenuItem(c.Name, MoveMaterialsCommand, moves) { IsEnabled = moves.Count > 0 };
                }),
            ];
            items.Add(new($"Move {(isOne ? string.Empty : $"{materials.Count} Materials ")}to")
            {
                Icon = AppIcon.Move,
                Items = folders,
                IsEnabled = folders.Any(f => f.IsEnabled),
            });
        }

        if (isOne)
        {
            var leaf = leaves[0];
            items.AddRange(
            [
                TreeMenuItem.Separator,
                new("Rename", RenameMaterialCommand, leaf) { Gesture = "F2", Icon = AppIcon.Rename },
                new("Copy", CopyMaterialCommand, leaf) { Icon = AppIcon.Copy },
                TreeMenuItem.Separator,
                new("Delete", DeleteMaterialCommand, leaf) { Gesture = "Del", Icon = AppIcon.Delete },
            ]);
        }

        return items;
    }

    /// <summary>A type's folder's menu. Ingredients' adds a Made category too, whose folder goes in it.</summary>
    private List<TreeMenuItem> TypeFolderMenu(RawMaterialFolderNode typeFolder)
    {
        var isIngredients = typeFolder.Type == RawMaterialType.Ingredient;
        List<TreeMenuItem> items =
        [
            new($"New {MaterialNoun(typeFolder)} Here", NewMaterialInCommand, typeFolder) { Icon = AppIcon.Add },
            new("New Folder", NewCategoryCommand, isIngredients ? RawMaterialCategoryType.Ingredient : RawMaterialCategoryType.Packaging)
            {
                Icon = AppIcon.NewFolder,
            },
        ];
        if (isIngredients)
        {
            items.Add(new("New Made Folder", NewCategoryCommand, RawMaterialCategoryType.Made) { Icon = AppIcon.NewFolder });
        }

        items.AddRange(
        [
            TreeMenuItem.Separator,
            new("Expand All", ExpandAllCommand, typeFolder),
            new("Collapse All", CollapseAllCommand, typeFolder),
        ]);
        return items;
    }

    private List<TreeMenuItem> CategoryFolderMenu(RawMaterialFolderNode folder)
    {
        // Every material in it, not just a search's matches: the database won't delete a category anything is in.
        var holds = RawMaterials.Count(m => m.CategoryId == folder.CategoryId);
        return
        [
            new($"New {MaterialNoun(folder)} Here", NewMaterialInCommand, folder) { Icon = AppIcon.Add },
            TreeMenuItem.Separator,
            new("New Folder", NewCategoryCommand, folder.CategoryType!.Value) { Icon = AppIcon.NewFolder },
            new("Rename Category", RenameCategoryCommand, folder) { Gesture = "F2", Icon = AppIcon.Rename },
            new(holds == 0 ? "Delete Category" : $"Delete Category (holds {holds})", DeleteCategoryCommand, folder)
            {
                Gesture = "Del",
                Icon = AppIcon.Delete,
                IsEnabled = holds == 0,
            },
        ];
    }

    private List<TreeMenuItem> EmptySpaceMenu() =>
    [
        new("New Raw Material", NewMaterialInCommand) { Icon = AppIcon.Add },
        new("New Ingredient Folder", NewCategoryCommand, RawMaterialCategoryType.Ingredient) { Icon = AppIcon.NewFolder },
        new("New Made Folder", NewCategoryCommand, RawMaterialCategoryType.Made) { Icon = AppIcon.NewFolder },
        new("New Packaging Folder", NewCategoryCommand, RawMaterialCategoryType.Packaging) { Icon = AppIcon.NewFolder },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand),
        new("Collapse All", CollapseAllCommand),
    ];

    /// <summary>
    /// The list's Rename: the material's name becomes a box to type into, in place (TreeNameEdit), and the name typed is
    /// saved at once (<see cref="RenameMaterialToAsync"/>).
    /// </summary>
    private void BeginRenameMaterial(RawMaterialLeafNode leaf)
    {
        if (leaf.IsNew)
        {
            return;
        }

        CancelTreeName();
        _treeNaming.Start(leaf, leaf.RawMaterial.Name);
    }

    /// <summary>
    /// The list's Rename Category: the folder's name becomes a box to type into, in place, and the name typed renames the
    /// category (<see cref="RenameCategoryToAsync"/>), keeping its type.
    /// </summary>
    private void BeginRenameCategory(RawMaterialFolderNode folder)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return;
        }

        CancelTreeName();
        _treeNaming.Start(folder, category.Name);
    }

    /// <summary>
    /// The list's New Folder: a "New folder" row last in the type's folder for <paramref name="type"/>, picked, with its
    /// name ready to be typed, as in a file explorer; the name typed adds a category of that type
    /// (<see cref="CommitTreeNameAsync"/>). The search is cleared first, so the row shows.
    /// </summary>
    private void BeginNewFolder(RawMaterialCategoryType type) => BeginNewFolderIn(type, RawMaterialFolderNode.NewFolderTitle);

    private void BeginNewFolderIn(RawMaterialCategoryType type, string text)
    {
        CancelTreeName();
        SearchText = string.Empty;
        _treeNaming.Start(RawMaterialTree.BeginNewFolder(type), text, NewFolderHint(type));
    }

    /// <summary>What the name box on a new folder says when pointed at: the prompts used to say it.</summary>
    private static string NewFolderHint(RawMaterialCategoryType type)
    {
        var typeTitle = RawMaterialFolderNode.TypeTitle(RawMaterialRules.MaterialTypeOf(type));
        return type == RawMaterialCategoryType.Made ? $"A folder in {typeTitle}, for made ingredients only." : $"A folder in {typeTitle}.";
    }

    /// <summary>
    /// New and New … Here: <see cref="StartNewMaterial"/>, and the new material's row in the list, in its category's folder
    /// (with none yet, from the header or the list's empty space, last in its type's folder), picked, with its name ready to
    /// be typed, as a new file in a file explorer. The name typed goes into the form (<see cref="CommitTreeNameAsync"/>),
    /// and the row stays until Save files the material or the form leaves it. The search is cleared first, so the row shows.
    /// </summary>
    private void NewMaterialInList(
        RawMaterialType type = RawMaterialType.Ingredient, int? categoryId = null, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        CancelTreeName();
        SearchText = string.Empty;
        StartNewMaterial(type, categoryId, source);
        var title = NewMaterialTitle(type, source);
        var row = RawMaterialTree.BeginNewMaterial(new RawMaterial
        {
            Name = title,
            Type = type,
            Source = source,
            CategoryId = EditingCategory?.Id ?? 0,
        });
        _treeNaming.Start(row, title);
    }

    /// <summary>"New ingredient", "New made ingredient" or "New packaging material": what a new material's row reads until it's named.</summary>
    private static string NewMaterialTitle(RawMaterialType type, RawMaterialSource source) =>
        type == RawMaterialType.Packaging ? "New packaging material"
        : source == RawMaterialSource.Made ? "New made ingredient"
        : "New ingredient";

    /// <summary>
    /// Takes the new material's row out of the list (<see cref="NewMaterialInList"/>), and the rows picked, and stops the
    /// naming in it: the form no longer shows the new material, or it's saved and has a row of its own. The form is left
    /// as it is.
    /// </summary>
    private void EndNewMaterialRow() => _treeNaming.EndNewRow(RawMaterialTree.PendingMaterial, RawMaterialTree.EndNewMaterial);

    /// <summary>
    /// The name typed into the list's box (<see cref="TreeNameEdit"/>), on Enter or on leaving it. A material's or a
    /// category's is renamed at once; a new folder's adds a category of its type, or picks the one of that name there
    /// already; a new material's goes into the form, as typed there, and its row takes it: a material needs more than a
    /// name before Save can file it. An empty name, or the name it had, is an Esc (<see cref="CancelTreeName"/>). A name
    /// refused shows why, and the box comes back with it. A second call for the same box, as Enter's box is left, does nothing.
    /// </summary>
    public Task CommitTreeNameAsync(string text) => _treeNaming.CommitAsync(text);

    /// <summary>Whether a row of the list is a new material's or a new folder's, still to be named.</summary>
    private static bool IsNewRow(object node) => node is RawMaterialLeafNode { IsNew: true } or RawMaterialFolderNode { IsNewFolder: true };

    /// <summary>What a name typed into the list's box does (<see cref="CommitTreeNameAsync"/>), once it's taken for one.</summary>
    private async Task CommitTreeNameToAsync(TreeNameEdit edit, string name, string text)
    {
        switch (edit.Node)
        {
            case RawMaterialLeafNode { IsNew: true } row:
                NameNewMaterial(row, name);
                break;

            case RawMaterialLeafNode leaf:
                await RunSafelyAsync(() => RenameMaterialToAsync(leaf, name));
                if (ErrorMessage is not null && RawMaterialTree.Find(leaf.RawMaterial.Id) is { } stillListed)
                {
                    _treeNaming.Edit = edit with { Node = stillListed, InitialText = text };
                }

                break;

            case RawMaterialFolderNode { IsNewFolder: true, CategoryType: { } type } folder:
                _treeNaming.EndNewRow(folder, RawMaterialTree.EndNewFolder);
                await RunSafelyAsync(() => AddCategoryAsync(type, name));
                if (ErrorMessage is not null)
                {
                    BeginNewFolderIn(type, text);
                }

                break;

            case RawMaterialFolderNode folder:
                await RunSafelyAsync(() => RenameCategoryToAsync(folder, name));
                if (ErrorMessage is not null && Categories.Any(c => c.Id == folder.CategoryId))
                {
                    _treeNaming.Edit = edit with { InitialText = text };
                }

                break;
        }
    }

    /// <summary>
    /// Esc in the list's name box: the name stays as it was, and a new material's or folder's row goes, with nothing saved.
    /// The form stays on the new material, blank, as New leaves it.
    /// </summary>
    public void CancelTreeName() => _treeNaming.Cancel();

    /// <summary>What Esc does to the row named (<see cref="CancelTreeName"/>), once its box is closed.</summary>
    private void TreeNameCancelled(object node)
    {
        switch (node)
        {
            case RawMaterialLeafNode { IsNew: true }:
                EndNewMaterialRow();
                ApplyRefresh(() => EditingName = string.Empty);
                break;
            case RawMaterialFolderNode { IsNewFolder: true } folder:
                _treeNaming.EndNewRow(folder, RawMaterialTree.EndNewFolder);
                break;
        }
    }

    /// <summary>
    /// The name typed into a new material's row: it goes into the form, as typed there (an undo step, which Save is offered
    /// for), and the row takes it, until Save files the material or the form leaves it.
    /// </summary>
    private void NameNewMaterial(RawMaterialLeafNode row, string name)
    {
        AsUndoStep(() => EditingName = name);
        var material = row.RawMaterial;
        row.RawMaterial = new RawMaterial { Name = name, Type = material.Type, Source = material.Source, CategoryId = material.CategoryId };
    }

    /// <summary>
    /// Saves <paramref name="name"/> as the material's at once. On the material the form shows, the name box takes it, and
    /// the form's undo steps take it as given, as a move gives them its category: whatever else is typed there stays to be
    /// saved, and no undo brings the old name back for a Save to write.
    /// </summary>
    private async Task RenameMaterialToAsync(RawMaterialLeafNode leaf, string name)
    {
        var material = leaf.RawMaterial;
        await _rawMaterialRepository.RenameAsync(material.Id, name, CancellationToken.None);
        await LoadCommand.Execute();
        if (_editingId == material.Id)
        {
            ApplyRefresh(() => EditingName = name);
            History.RewriteAll(snapshot => snapshot with { Name = name });
        }
    }

    /// <summary>
    /// Adds a category of <paramref name="type"/> named <paramref name="name"/>, then picks its folder. A name that's one of
    /// that type's categories already picks that one's folder instead. A name refused shows why.
    /// </summary>
    private async Task AddCategoryAsync(RawMaterialCategoryType type, string name)
    {
        var category = Categories.FirstOrDefault(c => c.Type == type && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            var added = new RawMaterialCategory { Name = name, Type = type };
            var validation = _categoryValidator.Validate(added);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            // AddAsync fills in the saved Id, which the copy read back is found by.
            await _categoryRepository.AddAsync(added, CancellationToken.None);
            await LoadCommand.Execute();
            category = Categories.FirstOrDefault(c => c.Id == added.Id);
        }

        if (category is not null
            && RawMaterialTree.CategoryFolder(category.Id) is { } folder
            && RawMaterialTree.TypeFolder(RawMaterialRules.MaterialTypeOf(type)).Children.Contains(folder))
        {
            SelectedTreeNode = folder;
        }
    }

    /// <summary>Renames the folder's category to <paramref name="name"/>, keeping its type. A name refused shows why.</summary>
    private async Task RenameCategoryToAsync(RawMaterialFolderNode folder, string name)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return;
        }

        var renamed = new RawMaterialCategory { Id = category.Id, Name = name, Type = category.Type };
        var validation = _categoryValidator.Validate(renamed);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ToString(" ");
            return;
        }

        // Everything is read again, the materials with it, which carry their category's name. The Category picker keeps
        // the category it holds (LoadCommand), under its new name.
        await _categoryRepository.UpdateAsync(renamed, CancellationToken.None);
        await LoadCommand.Execute();
    }

    /// <summary>
    /// The list's Delete Category, once it's confirmed: only for a category no material is in, which is all the database
    /// allows (a material always has a category). The menu greys it out otherwise.
    /// </summary>
    private Task DeleteCategoryAsync(RawMaterialFolderNode folder)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category
            || RawMaterials.Any(m => m.CategoryId == category.Id))
        {
            return Task.CompletedTask;
        }

        return RunWithConfirmationAsync(
            $"Delete the '{category.Name}' category? It holds no materials. This can't be undone.",
            async () =>
            {
                await _categoryRepository.DeleteAsync(category.Id, CancellationToken.None);

                // A new material's form may have had it picked: the undo steps forget it, so none puts back a category
                // there's no longer any of to save.
                await LoadCommand.Execute();
                History.RewriteAll(snapshot => snapshot.CategoryId == category.Id ? snapshot with { CategoryId = null } : snapshot);
            });
    }
}
