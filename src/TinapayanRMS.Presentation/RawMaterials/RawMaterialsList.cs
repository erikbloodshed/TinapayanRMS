using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>
/// What the Raw Materials list asks of its screen, on top of what any record screen's list asks: the material the form
/// shows, and what the list's actions do to it.
/// </summary>
public interface IRawMaterialsListHost : IRecordListHost
{
    /// <summary>The material picked, which the form shows; null for a new one.</summary>
    RawMaterial? SelectedRawMaterial { get; }

    /// <summary>The material the form holds: 0 for a new one not saved yet.</summary>
    int EditingId { get; }

    /// <summary>The category the form's material is in, which a new one's row is filed under.</summary>
    RawMaterialCategory? EditingCategory { get; }

    /// <summary>Reads the screen's lists again (its LoadCommand), keeping the form and what's typed into it.</summary>
    Task LoadAsync();

    /// <summary>A material's row picked in the list: it opens, once the form's unsaved changes are saved or let go.</summary>
    void Pick(RawMaterial material);

    /// <summary>A fresh form for a new material of <paramref name="type"/>, in <paramref name="categoryId"/>, made or purchased as <paramref name="source"/> says.</summary>
    void StartNew(RawMaterialType type, int? categoryId, RawMaterialSource source);

    /// <summary>A fresh form for a copy of <paramref name="material"/>: its type, category and base unit, named "… Copy".</summary>
    void StartCopyOf(RawMaterial material);

    /// <summary>Deletes the material once it's confirmed, as the header's Delete does.</summary>
    Task DeleteAsync(RawMaterial material);

    /// <summary>The name typed into the new material's row goes into the form, as typed there: one undo step.</summary>
    void NameNew(string name);

    /// <summary>The new material's row went with Esc: the name its form was given goes too, as no edit of the user's.</summary>
    void ClearNewName();

    /// <summary>The material the form shows was renamed in the list, saved: the form and its undo steps take the name as given.</summary>
    void RenamedOnForm(string name);

    /// <summary>The material the form shows was moved to another category, saved: the form and its undo steps take it as given.</summary>
    void MovedOnForm(int categoryId);

    /// <summary>A category was deleted: the form's undo steps forget it, so none puts back a category there's no longer any of.</summary>
    void CategoryDeleted(int categoryId);
}

/// <summary>
/// The Raw Materials list (through RawMaterialsState.List; RawMaterialsListCard draws it): the materials, searched by name and
/// filed in a folder for their type, then their category; the rows picked, its menus and keys, moves and their Undo, naming
/// in place, and the categories as its folders. The materials and categories are the screen's, which its form's pickers
/// read too; what the list's actions do to the material the form shows, it asks of the screen
/// (<see cref="IRawMaterialsListHost"/>).
/// </summary>
public sealed class RawMaterialsList : RecordList, IListTreeScreen, IDisposable
{
    /// <summary>The view state key the closed folders of the Raw Materials list are kept under (IViewStateStore).</summary>
    public const string ClosedFoldersKey = "RawMaterials.ClosedFolders";

    /// <summary>
    /// The view state key the list kept its open folders under, before it kept the closed ones: read while there are no
    /// closed ones kept, so the list opens as it was left (FolderMemory.Read).
    /// </summary>
    public const string OpenFoldersKey = "RawMaterials.OpenFolders";

    private readonly IRawMaterialsListHost _host;
    private readonly SourceCache<RawMaterial, int> _rawMaterialCache;
    private readonly ReadOnlyObservableCollection<RawMaterial> _rawMaterials;
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categories;
    private readonly ReadOnlyObservableCollection<RawMaterial> _visibleRawMaterials;
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IRawMaterialCategoryRepository _categoryRepository;
    private readonly IValidator<RawMaterialCategory> _categoryValidator;
    private readonly FolderStateWriter _closedFoldersWriter;

    /// <summary>Each search typed, what the list's filter follows: the materials whose name holds it.</summary>
    private readonly BehaviorSubject<string> _searches = new(string.Empty);

    /// <summary>Where the materials the last move took were before it: what Undo on the notice puts back.</summary>
    private IReadOnlyList<RawMaterialMove> _moveUndo = [];

    /// <param name="host">The screen, which the list asks for the material its form shows, and what to do to it.</param>
    /// <param name="rawMaterialCache">The screen's materials, which the list searches and moves.</param>
    /// <param name="categoryCache">The screen's categories, whose changes the list's folders follow.</param>
    /// <param name="rawMaterials">Every material, by name, as the screen binds them.</param>
    /// <param name="categories">Every category, by name, as the screen binds them: the list's folders.</param>
    public RawMaterialsList(
        IRawMaterialsListHost host,
        SourceCache<RawMaterial, int> rawMaterialCache,
        SourceCache<RawMaterialCategory, int> categoryCache,
        ReadOnlyObservableCollection<RawMaterial> rawMaterials,
        ReadOnlyObservableCollection<RawMaterialCategory> categories,
        IRawMaterialRepository rawMaterialRepository,
        IRawMaterialCategoryRepository categoryRepository,
        IValidator<RawMaterialCategory> categoryValidator,
        IViewStateStore viewStateStore)
    {
        _host = host;
        _rawMaterialCache = rawMaterialCache;
        _rawMaterials = rawMaterials;
        _categories = categories;
        _rawMaterialRepository = rawMaterialRepository;
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;

        // The list's folders open as they were left last time, a folder new since included, and the ones closed are written
        // for next time (FolderStateWriter), as on the Products screen; Dispose writes any change still waiting.
        RawMaterialTree = new RawMaterialTree(FolderMemory.Read(viewStateStore, ClosedFoldersKey, OpenFoldersKey));
        _closedFoldersWriter = new FolderStateWriter(viewStateStore, ClosedFoldersKey, RawMaterialTree.ExpansionChanged, RawMaterialTree.ClosedFolderKeys);

        // The list's tree follows both lists once each change has reached them: a category added, renamed or deleted (the
        // screen binds the categories first, so the tree reads them changed), and a material added, removed, renamed,
        // moved or filtered out.
        categoryCache.Connect()
            .Subscribe(_ => RefreshTree());

        var nameFilter = _searches
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<RawMaterial, bool>)(m => text.Length == 0 || m.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        rawMaterialCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleRawMaterials, SortExpressionComparer<RawMaterial>.Ascending(m => m.Name))
            .Subscribe(_ => RefreshTree());

        // The search decides whether empty folders show, even when it leaves the same materials showing (SearchText).
        RefreshTree();

        MoveMaterialsCommand = new ScreenCommand<IReadOnlyList<RawMaterialMove>>(
            moves => host.RunSafelyAsync(() => MoveMaterialsAsync(moves, isUndo: false)));
        UndoMoveCommand = new ScreenCommand(() => host.RunSafelyAsync(() => MoveMaterialsAsync(_moveUndo, isUndo: true)), () => MoveNotice is not null);
        NewMaterialInCommand = new ScreenCommand<RawMaterialFolderNode?>(folder => host.AfterUnsavedChangesAsync(() => NewInList(
            folder?.Type ?? RawMaterialType.Ingredient,
            folder?.CategoryId,
            folder?.CategoryType == RawMaterialCategoryType.Made ? RawMaterialSource.Made : RawMaterialSource.Purchased)));
        RenameMaterialCommand = ScreenCommand.Of<RawMaterialLeafNode>(BeginRenameMaterial);
        CopyMaterialCommand = new ScreenCommand<RawMaterialLeafNode>(async leaf =>
        {
            await host.AfterUnsavedChangesAsync(() => host.StartCopyOf(leaf.RawMaterial));
            Copied?.Invoke();
        });
        DeleteMaterialCommand = new ScreenCommand<RawMaterialLeafNode>(leaf => host.DeleteAsync(leaf.RawMaterial));
        NewCategoryCommand = ScreenCommand.Of<RawMaterialCategoryType>(BeginNewFolder);
        RenameCategoryCommand = ScreenCommand.Of<RawMaterialFolderNode>(BeginRenameCategory);
        DeleteCategoryCommand = new ScreenCommand<RawMaterialFolderNode>(DeleteCategoryAsync);
        ExpandAllCommand = ScreenCommand.Of<RawMaterialFolderNode?>(RawMaterialTree.ExpandAll);
        CollapseAllCommand = ScreenCommand.Of<RawMaterialFolderNode?>(RawMaterialTree.CollapseAll);
    }

    /// <summary>The materials the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<RawMaterial> VisibleRawMaterials => _visibleRawMaterials;

    /// <summary>The list's tree (RawMaterialsListCard draws it): VisibleRawMaterials filed in a folder for their type, then their category.</summary>
    public RawMaterialTree RawMaterialTree { get; }

    private string _searchText = string.Empty;
    /// <summary>
    /// What's typed into the list's search: the materials whose name holds it show. It decides whether empty folders show
    /// too, even when it leaves the same materials showing, and a name being typed into a row is given up first, since the
    /// search may hide the row.
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

    /// <summary>The materials picked in the list, in the order they were picked.</summary>
    public IReadOnlyList<RawMaterialLeafNode> SelectedTreeMaterials => [.. SelectedTreeNodes.OfType<RawMaterialLeafNode>().Where(l => !l.IsNew)];

    /// <summary>Files materials under another category, saved at once: a drag in the list, or its menu's Move To.</summary>
    public ScreenCommand<IReadOnlyList<RawMaterialMove>> MoveMaterialsCommand { get; }

    /// <summary>Puts back where the last move took the materials from (the notice bar's Undo).</summary>
    public ScreenCommand UndoMoveCommand { get; }

    /// <summary>
    /// The list's New … Here: a new material of the folder's type, in its category when it's a category's folder. With no
    /// folder (the list's empty space), a new ingredient, as New starts one. Like New, it adds the material's row to the list
    /// for its name to be typed into (TreeNameEdit).
    /// </summary>
    public ScreenCommand<RawMaterialFolderNode?> NewMaterialInCommand { get; }

    /// <summary>The list's Rename: the material's name becomes a box to type into, in place, and the name typed is saved at once.</summary>
    public ScreenCommand<RawMaterialLeafNode> RenameMaterialCommand { get; }

    /// <summary>
    /// The list's Copy: a new material with the material's type, category and base unit, named "… Copy", and nothing else.
    /// It isn't saved until Save is clicked.
    /// </summary>
    public ScreenCommand<RawMaterialLeafNode> CopyMaterialCommand { get; }

    /// <summary>A Copy has run: the form holds the copy, whose name is there to change (the page puts the cursor in it).</summary>
    public event Action? Copied;

    /// <summary>The list's Delete, as the header's: asks first.</summary>
    public ScreenCommand<RawMaterialLeafNode> DeleteMaterialCommand { get; }

    /// <summary>
    /// The list's New Folder, for materials of the type given: a "New folder" row for its name to be typed into
    /// (TreeNameEdit), and the name typed adds the category.
    /// </summary>
    public ScreenCommand<RawMaterialCategoryType> NewCategoryCommand { get; }

    /// <summary>The list's Rename Category: the folder's name becomes a box to type into, in place, and the name typed renames the category.</summary>
    public ScreenCommand<RawMaterialFolderNode> RenameCategoryCommand { get; }

    /// <summary>The list's Delete Category, offered only for a category no material is in.</summary>
    public ScreenCommand<RawMaterialFolderNode> DeleteCategoryCommand { get; }

    /// <summary>Opens every folder of a type's, or of both types' with no folder given.</summary>
    public ScreenCommand<RawMaterialFolderNode?> ExpandAllCommand { get; }

    /// <summary>Closes every category folder of a type's, or of both types' with no folder given.</summary>
    public ScreenCommand<RawMaterialFolderNode?> CollapseAllCommand { get; }

    private void RefreshTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its material the one picked.
        ListSelection.Quietly(() => RawMaterialTree.Refresh(_categories, VisibleRawMaterials, isFiltered: SearchText.Trim().Length > 0));

        // A folder picked stays picked. Otherwise the row goes back to the material picked: one moved to another folder
        // leaves the tree's selection as it goes, and one a search hid gets its row back when the search ends.
        if (SelectedTreeNode is not RawMaterialFolderNode)
        {
            SyncTreeSelection();
        }
    }

    protected override object? ShowingRow => _host.SelectedRawMaterial is { } material ? RawMaterialTree.Find(material.Id) : RawMaterialTree.PendingMaterial;

    protected override string PickedRecordsProperty => nameof(SelectedTreeMaterials);

    // A material's row picks it; the new material's row is the form's already.
    protected override void PickRowFromList(object row)
    {
        if (row is RawMaterialLeafNode { IsNew: false } leaf && leaf.RawMaterial.Id != _host.SelectedRawMaterial?.Id)
        {
            _host.Pick(leaf.RawMaterial);
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
        await _host.LoadAsync();
        if (moves.FirstOrDefault(m => m.RawMaterialId == _host.EditingId) is { } onForm)
        {
            _host.MovedOnForm(onForm.CategoryId);
        }

        // Picks again the rows the tree dropped from its selection as they moved.
        ListSelection.Restore(picked, r => r is not RawMaterialLeafNode leaf || RawMaterialTree.Find(leaf.RawMaterial.Id) == leaf);

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
        return moves.All(m => m.CategoryId == categoryId) && _categories.FirstOrDefault(c => c.Id == categoryId) is { } category
            ? $"Moved {what} to {RawMaterialFolderNode.TypeTitle(RawMaterialRules.MaterialTypeOf(category.Type))} › {category.Name}"
            : $"Moved {what}";
    }

    /// <summary>
    /// The menu a right-click opens on the list (RawMaterialsListCard): on <paramref name="node"/>'s row, which the list picks
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
    bool IListTreeScreen.CanDrag(object node) => node is RawMaterialLeafNode { IsNew: false };

    bool IListTreeScreen.CanDrop(IEnumerable<object> nodes, object? target) => DropMoves(nodes, target).Count > 0;

    async Task<object?> IListTreeScreen.DropAsync(IEnumerable<object> nodes, object? target)
    {
        var moves = DropMoves(nodes, target);
        if (moves.Count == 0 || !MoveMaterialsCommand.CanExecute(moves))
        {
            return null;
        }

        await MoveMaterialsCommand.ExecuteAsync(moves);
        return RawMaterialTree.Find(moves[0].RawMaterialId);
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
                .. _categories.Where(c => c.Type == type).Select(c =>
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
        var holds = _rawMaterials.Count(m => m.CategoryId == folder.CategoryId);
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
        ListNaming.Start(leaf, leaf.RawMaterial.Name);
    }

    /// <summary>
    /// The list's Rename Category: the folder's name becomes a box to type into, in place, and the name typed renames the
    /// category (<see cref="RenameCategoryToAsync"/>), keeping its type.
    /// </summary>
    private void BeginRenameCategory(RawMaterialFolderNode folder)
    {
        if (_categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return;
        }

        CancelTreeName();
        ListNaming.Start(folder, category.Name);
    }

    /// <summary>
    /// The list's New Folder: a "New folder" row last in the type's folder for <paramref name="type"/>, picked, with its
    /// name ready to be typed, as in a file explorer; the name typed adds a category of that type
    /// (<see cref="RecordList.CommitTreeNameAsync"/>). The search is cleared first, so the row shows.
    /// </summary>
    private void BeginNewFolder(RawMaterialCategoryType type) => BeginNewFolderIn(type, RawMaterialFolderNode.NewFolderTitle);

    private void BeginNewFolderIn(RawMaterialCategoryType type, string text)
    {
        CancelTreeName();
        SearchText = string.Empty;
        ListNaming.Start(RawMaterialTree.BeginNewFolder(type), text, NewFolderHint(type));
    }

    /// <summary>What the name box on a new folder says when pointed at: the prompts used to say it.</summary>
    private static string NewFolderHint(RawMaterialCategoryType type)
    {
        var typeTitle = RawMaterialFolderNode.TypeTitle(RawMaterialRules.MaterialTypeOf(type));
        return type == RawMaterialCategoryType.Made ? $"A folder in {typeTitle}, for made ingredients only." : $"A folder in {typeTitle}.";
    }

    /// <summary>
    /// New and New … Here: a fresh form for a new material (<see cref="IRawMaterialsListHost.StartNew"/>), and its row in the
    /// list, in its category's folder (with none yet, from the header or the list's empty space, last in its type's folder),
    /// picked, with its name ready to be typed, as a new file in a file explorer. The name typed goes into the form
    /// (<see cref="RecordList.CommitTreeNameAsync"/>), and the row stays until Save files the material or the form leaves it.
    /// The search is cleared first, so the row shows.
    /// </summary>
    public void NewInList(
        RawMaterialType type = RawMaterialType.Ingredient, int? categoryId = null, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        CancelTreeName();
        SearchText = string.Empty;
        _host.StartNew(type, categoryId, source);
        var title = NewMaterialTitle(type, source);
        var row = RawMaterialTree.BeginNewMaterial(new RawMaterial
        {
            Name = title,
            Type = type,
            Source = source,
            CategoryId = _host.EditingCategory?.Id ?? 0,
        });
        ListNaming.Start(row, title);
    }

    /// <summary>"New ingredient", "New made ingredient" or "New packaging material": what a new material's row reads until it's named.</summary>
    private static string NewMaterialTitle(RawMaterialType type, RawMaterialSource source) =>
        type == RawMaterialType.Packaging ? "New packaging material"
        : source == RawMaterialSource.Made ? "New made ingredient"
        : "New ingredient";

    /// <summary>
    /// Takes the new material's row out of the list (<see cref="NewInList"/>), and the rows picked, and stops the naming in
    /// it: the form no longer shows the new material, or it's saved and has a row of its own. The form is left as it is.
    /// </summary>
    public void EndNewRow() => ListNaming.EndNewRow(RawMaterialTree.PendingMaterial, RawMaterialTree.EndNewMaterial);

    /// <summary>Whether a row of the list is a new material's or a new folder's, still to be named.</summary>
    protected override bool IsNewRow(object node) => node is RawMaterialLeafNode { IsNew: true } or RawMaterialFolderNode { IsNewFolder: true };

    /// <summary>
    /// What a name typed into the list's box does (<see cref="RecordList.CommitTreeNameAsync"/>), once it's taken for one. A
    /// material's or a category's is renamed at once; a new folder's adds a category of its type, or picks the one of that
    /// name there already; a new material's goes into the form, as typed there, and its row takes it: a material needs more
    /// than a name before Save can file it.
    /// </summary>
    protected override async Task CommitTreeNameToAsync(TreeNameEdit edit, string name, string text)
    {
        switch (edit.Node)
        {
            case RawMaterialLeafNode { IsNew: true } row:
                NameNewMaterial(row, name);
                break;

            case RawMaterialLeafNode leaf:
                await _host.RunSafelyAsync(() => RenameMaterialToAsync(leaf, name));
                if (_host.ErrorMessage is not null && RawMaterialTree.Find(leaf.RawMaterial.Id) is { } stillListed)
                {
                    ListNaming.Edit = edit with { Node = stillListed, InitialText = text };
                }

                break;

            case RawMaterialFolderNode { IsNewFolder: true, CategoryType: { } type } folder:
                ListNaming.EndNewRow(folder, RawMaterialTree.EndNewFolder);
                await _host.RunSafelyAsync(() => AddCategoryAsync(type, name));
                if (_host.ErrorMessage is not null)
                {
                    BeginNewFolderIn(type, text);
                }

                break;

            case RawMaterialFolderNode folder:
                await _host.RunSafelyAsync(() => RenameCategoryToAsync(folder, name));
                if (_host.ErrorMessage is not null && _categories.Any(c => c.Id == folder.CategoryId))
                {
                    ListNaming.Edit = edit with { InitialText = text };
                }

                break;
        }
    }

    /// <summary>
    /// What Esc does to the row named (<see cref="RecordList.CancelTreeName"/>), once its box is closed: a new material's or
    /// folder's row goes, with nothing saved. The form stays on the new material, blank, as New leaves it.
    /// </summary>
    protected override void TreeNameCancelled(object node)
    {
        switch (node)
        {
            case RawMaterialLeafNode { IsNew: true }:
                EndNewRow();
                _host.ClearNewName();
                break;
            case RawMaterialFolderNode { IsNewFolder: true } folder:
                ListNaming.EndNewRow(folder, RawMaterialTree.EndNewFolder);
                break;
        }
    }

    /// <summary>
    /// The name typed into a new material's row: it goes into the form, as typed there (an undo step, which Save is offered
    /// for), and the row takes it, until Save files the material or the form leaves it.
    /// </summary>
    private void NameNewMaterial(RawMaterialLeafNode row, string name)
    {
        _host.NameNew(name);
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
        await _host.LoadAsync();
        if (_host.EditingId == material.Id)
        {
            _host.RenamedOnForm(name);
        }
    }

    /// <summary>
    /// Adds a category of <paramref name="type"/> named <paramref name="name"/>, then picks its folder. A name that's one of
    /// that type's categories already picks that one's folder instead. A name refused shows why.
    /// </summary>
    private async Task AddCategoryAsync(RawMaterialCategoryType type, string name)
    {
        var category = _categories.FirstOrDefault(c => c.Type == type && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            var added = new RawMaterialCategory { Name = name, Type = type };
            var validation = _categoryValidator.Validate(added);
            if (!validation.IsValid)
            {
                _host.ShowError(validation.ToString(" "));
                return;
            }

            // AddAsync fills in the saved Id, which the copy read back is found by.
            await _categoryRepository.AddAsync(added, CancellationToken.None);
            await _host.LoadAsync();
            category = _categories.FirstOrDefault(c => c.Id == added.Id);
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
        if (_categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return;
        }

        var renamed = new RawMaterialCategory { Id = category.Id, Name = name, Type = category.Type };
        var validation = _categoryValidator.Validate(renamed);
        if (!validation.IsValid)
        {
            _host.ShowError(validation.ToString(" "));
            return;
        }

        // Everything is read again, the materials with it, which carry their category's name. The Category picker keeps
        // the category it holds (the screen's LoadCommand), under its new name.
        await _categoryRepository.UpdateAsync(renamed, CancellationToken.None);
        await _host.LoadAsync();
    }

    /// <summary>
    /// The list's Delete Category, once it's confirmed: only for a category no material is in, which is all the database
    /// allows (a material always has a category). The menu greys it out otherwise.
    /// </summary>
    private Task DeleteCategoryAsync(RawMaterialFolderNode folder)
    {
        if (_categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category
            || _rawMaterials.Any(m => m.CategoryId == category.Id))
        {
            return Task.CompletedTask;
        }

        return _host.RunWithConfirmationAsync(
            $"Delete the '{category.Name}' category? It holds no materials. This can't be undone.",
            async () =>
            {
                await _categoryRepository.DeleteAsync(category.Id, CancellationToken.None);

                // A new material's form may have had it picked: the undo steps forget it, so none puts back a category
                // there's no longer any of to save.
                await _host.LoadAsync();
                _host.CategoryDeleted(category.Id);
            });
    }

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        _closedFoldersWriter.Dispose();
        RawMaterialTree.Dispose();
        _searches.Dispose();
    }
}
