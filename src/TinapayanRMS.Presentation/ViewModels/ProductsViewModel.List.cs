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

// The list: its tree, search, menus, moves, naming in place and categories.
public sealed partial class ProductsViewModel
{
    /// <summary>
    /// The list's tree follows the products, the categories and the search, and its selection the product showing; the
    /// flags for a price short of its target markup.
    /// </summary>
    [MemberNotNull(nameof(_products), nameof(_categories), nameof(_visibleProducts))]
    private void WireList()
    {
        SelectedTreeNodes.CollectionChanged += (_, _) => TreeSelectionChanged();

        var byName = SortExpressionComparer<Product>.Ascending(p => p.Name);

        _productCache.Connect()
            .SortAndBind(out _products, byName)
            .Subscribe();

        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<Product, bool>)(p => text.Length == 0 || p.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        // The list's tree follows both lists once each change has reached them: a product added, removed, renamed, moved or
        // filtered out, and a category added, renamed or deleted. The categories are bound first, as the tree reads both.
        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<ProductCategory>.Ascending(c => c.Name))
            .Subscribe(_ => RefreshProductTree());

        _productCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleProducts, byName)
            .Subscribe(_ => RefreshProductTree());

        // The search decides whether empty folders show, even when it leaves the same products showing. A name being typed
        // into a row is given up first: the search may hide the row.
        this.WhenAnyValue(x => x.SearchText)
            .Skip(1)
            .Subscribe(_ => CancelTreeName());
        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => RefreshProductTree());

        // A product opened, by its Save included, ends the new product's row: the form no longer shows the new one.
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is not null)
            .Subscribe(_ => EndNewProductRow());
        this.WhenAnyValue(x => x.SelectedProduct)
            .Subscribe(_ => SyncTreeSelection());

        // The list's flags for a price short of its target markup, after each load: a newer load cancels one still working.
        LoadLatest(_markupChecks, FlagBelowTargetMarkupAsync);
    }

    /// <summary>The list's commands, each run on the folder or product its menu was opened on (TreeMenuFor).</summary>
    [MemberNotNull(
        nameof(MoveProductsCommand), nameof(ApplyCostingDefaultsCommand), nameof(UndoMoveCommand),
        nameof(DismissMoveNoticeCommand), nameof(NewProductInCommand), nameof(RenameProductCommand),
        nameof(CloneProductCommand), nameof(CreateVariantOfCommand), nameof(DeleteProductCommand),
        nameof(RestoreProductCommand), nameof(DeletePermanentlyCommand), nameof(NewCategoryCommand),
        nameof(RenameCategoryCommand), nameof(DeleteCategoryCommand), nameof(ExpandAllCommand),
        nameof(PrintFolderCommand), nameof(PrintAllCommand), nameof(CollapseAllCommand))]
    private void WireListCommands()
    {
        // The list's commands, each run on the folder or product its menu was opened on (TreeMenuFor).
        MoveProductsCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<ProductMove>>(
            moves => RunSafelyAsync(() => MoveProductsAsync(moves, isUndo: false)));
        ApplyCostingDefaultsCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<Product>>(ApplyCostingDefaultsAsync);
        var hasMoveNotice = this.WhenAnyValue(x => x.MoveNotice).Select(notice => notice is not null);
        UndoMoveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(() => MoveProductsAsync(_moveUndo, isUndo: true)), hasMoveNotice);
        DismissMoveNoticeCommand = ReactiveCommand.Create(() => { MoveNotice = null; });
        NewProductInCommand = ReactiveCommand.CreateFromTask<ProductFolderNode?>(folder => AfterUnsavedChangesAsync(
            () => NewProductInList(folder?.Stage, folder?.CategoryId)));
        RenameProductCommand = ReactiveCommand.Create<ProductLeafNode>(BeginRenameProduct);
        CloneProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => MakeFromWithPromptAsync(
            leaf,
            new TextPromptRequest("Clone Product", "Name", $"{leaf.Product.Name} Copy", $"A copy of {leaf.Product.Name} as last saved, filed in Innovation."),
            _productCloner.CloneAsync,
            ClonedFrom));
        CreateVariantOfCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => MakeFromWithPromptAsync(
            leaf,
            new TextPromptRequest("Create Variant", "Name", $"{leaf.Product.Name} Copy", $"A new product that links {leaf.Product.Name}'s recipe and packaging, filed in Innovation."),
            _productCloner.CreateVariantAsync,
            VariantOf));
        DeleteProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => DeleteProductAsync(leaf.Product));
        RestoreProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => RestoreProductAsync(leaf.Product));
        DeletePermanentlyCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => DeletePermanentlyAsync(leaf.Product));
        NewCategoryCommand = ReactiveCommand.Create<ProductFolderNode?>(BeginNewFolder);
        RenameCategoryCommand = ReactiveCommand.Create<ProductFolderNode>(BeginRenameCategory);
        DeleteCategoryCommand = ReactiveCommand.CreateFromTask<ProductFolderNode>(DeleteCategoryAsync);
        ExpandAllCommand = ReactiveCommand.Create<ProductFolderNode?>(ProductTree.ExpandAll);
        PrintFolderCommand = ReactiveCommand.Create<ProductFolderNode>(folder => _printRequests.OnNext(new PrintScope.Folder(folder)));
        PrintAllCommand = ReactiveCommand.Create(() => _printRequests.OnNext(new PrintScope.AllProducts()));
        CollapseAllCommand = ReactiveCommand.Create<ProductFolderNode?>(ProductTree.CollapseAll);
    }

    private void RefreshProductTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its product the one picked.
        var search = SearchText.Trim();
        _treeSelection.Quietly(() => ProductTree.Refresh(
            Categories,
            VisibleProducts,
            isFiltered: search.Length > 0,
            [.. DeletedProducts.Where(p => search.Length == 0 || p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))]));

        // A folder picked stays picked, and so does a deleted product, which the form never shows. Otherwise the row goes
        // back to the product picked: a product moved to another folder leaves the tree's selection as it goes, and one a
        // search hid gets its row back when the search ends.
        if (SelectedTreeNode is not (ProductFolderNode or ProductLeafNode { IsDeleted: true }))
        {
            SyncTreeSelection();
        }
    }

    /// <summary>
    /// Puts SelectedProduct's row first among the rows picked in the list, and so makes it SelectedTreeNode: alone, unless
    /// it's picked already, when the rows picked with it stay. Nothing picked, or a search hiding it, picks no row.
    /// </summary>
    private void SyncTreeSelection() =>
        _treeSelection.BringFirst(SelectedProduct is { } product ? ProductTree.Find(product.Id) : ProductTree.PendingProduct);

    /// <summary>
    /// The list's selection changed: by a click (or several, with Ctrl or Shift), or by this screen. Its first row is
    /// SelectedTreeNode, and a product's row picked by the user picks the product.
    /// </summary>
    private void TreeSelectionChanged()
    {
        var first = SelectedTreeNodes.Count > 0 ? SelectedTreeNodes[0] : null;
        this.RaisePropertyChanged(nameof(SelectedTreeProducts));
        if (ReferenceEquals(first, _selectedTreeNode))
        {
            return;
        }

        _selectedTreeNode = first;
        this.RaisePropertyChanged(nameof(SelectedTreeNode));

        // The tree calls this in the middle of changing SelectedItems: picking the product leads to SyncTreeSelection,
        // which then finds its row first already and leaves the collection alone, as it must mid-change.
        // A deleted product's row is picked as a folder's is, leaving the form as it is: it's there to restore.
        // So is the new product's, which is the form's already.
        if (!_treeSelection.IsSyncing && first is ProductLeafNode { IsDeleted: false, IsNew: false } leaf && leaf.Product.Id != SelectedProduct?.Id)
        {
            PickFromList(leaf.Product.Id, () => SelectedProduct = leaf.Product, SyncTreeSelection, OpenProductAsync);
        }
    }

    private async Task MoveProductsAsync(IReadOnlyList<ProductMove> moves, bool isUndo)
    {
        if (moves.Count == 0)
        {
            return;
        }

        // Where each is now, which Undo puts back, and the rows picked, which the tree drops as they move.
        var undo = moves
            .Select(m => _productCache.Lookup(m.ProductId))
            .Where(found => found.HasValue)
            .Select(found => new ProductMove(found.Value.Id, found.Value.Stage, found.Value.CategoryId))
            .ToList();
        var picked = SelectedTreeNodes.ToList();

        await _productRepository.MoveAsync(moves, CancellationToken.None);
        var moved = await _productRepository.GetByIdsAsync([.. moves.Select(m => m.ProductId)], CancellationToken.None);

        // The list files them in their new folders. A selection is only re-pointed at the fresh copy, as a load re-points
        // it: the form isn't loaded again, so whatever's typed into it stays.
        var onForm = moved.FirstOrDefault(p => p.Id == _editingId);
        ApplyRefresh(() =>
        {
            _productCache.AddOrUpdate(moved);
            if (SelectedProduct is { } selected && moved.FirstOrDefault(p => p.Id == selected.Id) is { } fresh)
            {
                RepointSelection(fresh);
            }

            if (onForm is not null)
            {
                EditingCategory = Categories.FirstOrDefault(c => c.Id == onForm.CategoryId);
                ShowUpdatedAt(onForm.UpdatedAt);
            }
        });

        // The new category is saved, not typed: nothing for Save to write, and no undo step that puts the old one back.
        if (onForm is not null)
        {
            History.RewriteAll(snapshot => snapshot with { CategoryId = onForm.CategoryId });
        }

        // Picks again the rows the tree dropped from its selection as they moved.
        _treeSelection.Restore(picked, r => r is not ProductLeafNode leaf || ProductTree.Find(leaf.Product.Id) == leaf);

        _moveUndo = isUndo ? [] : undo;
        MoveNotice = isUndo ? null : MoveNoticeFor(moves);
    }

    /// <summary>
    /// What the notice bar says a move did: "Moved Pandesal to Production › Breads", "Moved 3 products to Production ›
    /// Breads", "Moved 3 products to Production" (a Promote, each keeping its category), or "Moved 3 products".
    /// </summary>
    private string MoveNoticeFor(IReadOnlyList<ProductMove> moves)
    {
        var what = moves.Count == 1 && _productCache.Lookup(moves[0].ProductId) is { HasValue: true } one ? one.Value.Name : $"{moves.Count} products";
        var first = moves[0];
        if (moves.All(m => m.Stage == first.Stage && m.CategoryId == first.CategoryId))
        {
            var folder = first.CategoryId is { } id ? Categories.FirstOrDefault(c => c.Id == id)?.Name : ProductRules.UncategorizedTitle;
            return $"Moved {what} to {ProductRules.StageTitle(first.Stage)} › {folder}";
        }

        return moves.All(m => m.Stage == first.Stage)
            ? $"Moved {what} to {ProductRules.StageTitle(first.Stage)}"
            : $"Moved {what}";
    }

    /// <summary>
    /// The menu a right-click opens on the list (ProductList): on <paramref name="node"/>'s row, which the list picks
    /// first (keeping the rows picked with it, when it was one of them), or on the list's empty space when it's null. A
    /// product's menu covers every product picked with it.
    /// </summary>
    public IReadOnlyList<TreeMenuItem> TreeMenuFor(ProductTreeNode? node) => node switch
    {
        ProductLeafNode { IsDeleted: true } leaf => DeletedProductMenu(leaf),
        ProductFolderNode { IsDeletedFolder: true } or ProductFolderNode { IsNewFolder: true } or ProductLeafNode { IsNew: true } => [],
        ProductLeafNode leaf => ProductsMenu(SelectedTreeProducts is var picked && picked.Contains(leaf) ? picked : [leaf]),
        ProductFolderNode { IsStage: true } stage => StageMenu(stage),
        ProductFolderNode folder => FolderMenu(folder),
        _ => EmptySpaceMenu(),
    };

    /// <summary>
    /// The item a key runs with the one row picked, as that row's menu shows the key beside it (F2, Del); null when the
    /// menu has none, or when several rows are picked.
    /// </summary>
    public TreeMenuItem? TreeShortcut(string gesture) =>
        SelectedTreeNodes is [ProductTreeNode node]
            ? TreeMenuFor(node).FirstOrDefault(item => item.Gesture == gesture && item.IsEnabled)
            : null;

    IReadOnlyList<TreeMenuItem> IListTreeScreen.TreeMenuFor(object? node) => node is null or ProductTreeNode ? TreeMenuFor(node as ProductTreeNode) : [];

    /// <summary>Only products can be dragged, one or several: the folders are fixed, and a deleted product only comes back by Restore.</summary>
    bool IListTreeScreen.CanDrag(IEnumerable<object> nodes) => nodes.All(node => node is ProductLeafNode { IsDeleted: false, IsNew: false });

    bool IListTreeScreen.CanDrop(IEnumerable<object> nodes, object? target) => DropMoves(nodes, target).Count > 0;

    IObservable<object?> IListTreeScreen.Drop(IEnumerable<object> nodes, object? target)
    {
        var moves = DropMoves(nodes, target);
        return moves.Count == 0
            ? Observable.Return<object?>(null)
            : MoveProductsCommand.Execute(moves).Select(_ => (object?)ProductTree.Find(moves[0].ProductId));
    }

    /// <summary>What dropping <paramref name="nodes"/> on <paramref name="target"/> would do (ProductTree.MovesTo): none unless every one is a record.</summary>
    private static IReadOnlyList<ProductMove> DropMoves(IEnumerable<object> nodes, object? target) =>
        nodes.ToList() is { Count: > 0 } dragging && dragging.All(node => node is ProductLeafNode)
            ? ProductTree.MovesTo(target as ProductTreeNode, dragging.Cast<ProductLeafNode>().Select(leaf => leaf.Product))
            : [];

    private List<TreeMenuItem> ProductsMenu(IReadOnlyList<ProductLeafNode> leaves)
    {
        var products = leaves.Select(l => l.Product).ToList();
        var isOne = products.Count == 1;
        var toProduction = ProductTree.MovesTo(ProductStage.Production, null, keepCategory: true, products);
        var toInnovation = ProductTree.MovesTo(ProductStage.Innovation, null, keepCategory: true, products);

        List<TreeMenuItem> items = [];
        if (toProduction.Count > 0)
        {
            items.Add(new($"Promote {HowMany(toProduction)}to Production", MoveProductsCommand, toProduction) { Icon = AppIcon.MoveUp });
        }

        if (toInnovation.Count > 0)
        {
            items.Add(new($"Send {HowMany(toInnovation)}Back to Innovation", MoveProductsCommand, toInnovation) { Icon = AppIcon.MoveDown });
        }

        items.Add(new($"Move {(isOne ? string.Empty : $"{products.Count} Products ")}to")
        {
            Icon = AppIcon.Move,
            Items = [MoveToStage(ProductStage.Production), MoveToStage(ProductStage.Innovation)],
        });
        items.Add(new($"Apply Costing Defaults{(isOne ? string.Empty : $" to {products.Count} Products")}…", ApplyCostingDefaultsCommand, products)
        {
            Icon = AppIcon.Calculate,
        });

        if (isOne)
        {
            var leaf = leaves[0];
            items.AddRange(
            [
                TreeMenuItem.Separator,
                new("Rename", RenameProductCommand, leaf) { Gesture = "F2", Icon = AppIcon.Rename },
                new("Clone…", CloneProductCommand, leaf) { Icon = AppIcon.Copy },
                new("Create Variant…", CreateVariantOfCommand, leaf) { Icon = AppIcon.Link },
                TreeMenuItem.Separator,
                new("Delete", DeleteProductCommand, leaf) { Gesture = "Del", Icon = AppIcon.Delete },
            ]);
        }

        return items;

        // "3 Products " while several are picked, counting only those that would move; nothing for one.
        string HowMany(IReadOnlyList<ProductMove> moves) =>
            isOne ? string.Empty : $"{moves.Count} {(moves.Count == 1 ? "Product" : "Products")} ";

        // A stage's folders, each moving every product picked that isn't in it already, and greyed when none would move.
        TreeMenuItem MoveToStage(ProductStage stage)
        {
            List<TreeMenuItem> folders =
            [
                .. Categories.Select(c => MoveToFolder(c.Name, c.Id)),
                MoveToFolder(ProductRules.UncategorizedTitle, null),
            ];
            return new(ProductRules.StageTitle(stage)) { Items = folders, IsEnabled = folders.Any(f => f.IsEnabled) };

            TreeMenuItem MoveToFolder(string title, int? categoryId)
            {
                var moves = ProductTree.MovesTo(stage, categoryId, keepCategory: false, products);
                return new(title, MoveProductsCommand, moves) { IsEnabled = moves.Count > 0 };
            }
        }
    }

    /// <summary>A product in the Deleted folder: it can only come back, or go for good.</summary>
    private List<TreeMenuItem> DeletedProductMenu(ProductLeafNode leaf) =>
    [
        new("Restore", RestoreProductCommand, leaf) { Icon = AppIcon.Undo },
        TreeMenuItem.Separator,
        new("Delete Permanently", DeletePermanentlyCommand, leaf) { Gesture = "Del", Icon = AppIcon.Delete },
    ];

    private List<TreeMenuItem> StageMenu(ProductFolderNode stage) =>
    [
        new("New Product Here", NewProductInCommand, stage) { Icon = AppIcon.Add },
        new("New Composite Product Here", NewCompositeProductInCommand, stage) { Icon = AppIcon.Composite },
        new("New Folder", NewCategoryCommand, stage) { Icon = AppIcon.NewFolder },
        TreeMenuItem.Separator,
        new("Print Stage", PrintFolderCommand, stage) { Icon = AppIcon.Print },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand, stage),
        new("Collapse All", CollapseAllCommand, stage),
    ];

    private List<TreeMenuItem> FolderMenu(ProductFolderNode folder)
    {
        List<TreeMenuItem> items =
        [
            new("New Product Here", NewProductInCommand, folder) { Icon = AppIcon.Add },
            new("New Composite Product Here", NewCompositeProductInCommand, folder) { Icon = AppIcon.Composite },
        ];

        // What the folder shows: with a search on, its matches.
        var toProduction = ProductTree.MovesTo(ProductStage.Production, null, keepCategory: true, folder.Products.Select(l => l.Product));
        if (toProduction.Count > 0)
        {
            items.Add(new("Promote All to Production", MoveProductsCommand, toProduction) { Icon = AppIcon.MoveUp });
        }

        items.Add(new("Print Folder", PrintFolderCommand, folder) { Icon = AppIcon.Print });

        if (folder.IsCategory)
        {
            items.AddRange(
            [
                TreeMenuItem.Separator,
                new("New Folder", NewCategoryCommand, folder) { Icon = AppIcon.NewFolder },
                new("Rename Category", RenameCategoryCommand, folder) { Gesture = "F2", Icon = AppIcon.Rename },
                new("Delete Category", DeleteCategoryCommand, folder) { Gesture = "Del", Icon = AppIcon.Delete },
            ]);
        }

        return items;
    }

    private List<TreeMenuItem> EmptySpaceMenu() =>
    [
        new("New Product", NewProductInCommand) { Icon = AppIcon.Add },
        new("New Composite Product", NewCompositeProductInCommand) { Icon = AppIcon.Composite },
        new("New Folder", NewCategoryCommand) { Icon = AppIcon.NewFolder },
        TreeMenuItem.Separator,
        new("Print All Products", PrintAllCommand) { Icon = AppIcon.Print },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand),
        new("Collapse All", CollapseAllCommand),
    ];

    /// <summary>
    /// The list's Rename: the product's name becomes a box to type into, in place (TreeNameEdit), and the name typed is
    /// saved at once (<see cref="RenameProductToAsync"/>). A deleted product isn't renamed.
    /// </summary>
    private void BeginRenameProduct(ProductLeafNode leaf)
    {
        if (leaf.IsDeleted || leaf.IsNew)
        {
            return;
        }

        CancelTreeName();
        _treeNaming.Start(leaf, leaf.Product.Name);
    }

    /// <summary>
    /// The list's Rename Category: the folder's name becomes a box to type into, in place, and the name typed renames the
    /// category (<see cref="RenameCategoryToAsync"/>), which is its folder in both stages.
    /// </summary>
    private void BeginRenameCategory(ProductFolderNode folder)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return;
        }

        CancelTreeName();
        _treeNaming.Start(folder, category.Name, CategoryHint);
    }

    /// <summary>
    /// The list's New Folder: a "New folder" row in a stage, picked, with its name ready to be typed, as in a file explorer;
    /// the name typed adds a category (<see cref="CommitTreeNameAsync"/>). The stage is <paramref name="from"/>'s; from the
    /// list's empty space, the stage of the row picked, or Production. The search is cleared first, so the row shows.
    /// </summary>
    private void BeginNewFolder(ProductFolderNode? from)
    {
        var stage = from?.Stage
            ?? (SelectedTreeNode as ProductTreeNode) switch
            {
                ProductFolderNode { IsDeletedFolder: false } folder => folder.Stage,
                ProductLeafNode { IsDeleted: false } leaf => leaf.Product.Stage,
                _ => ProductStage.Production,
            };
        BeginNewFolderIn(stage, ProductFolderNode.NewFolderTitle);
    }

    private void BeginNewFolderIn(ProductStage stage, string text)
    {
        CancelTreeName();
        SearchText = string.Empty;
        _treeNaming.Start(ProductTree.BeginNewFolder(stage), text, CategoryHint);
    }

    /// <summary>What the name box on a category's folder says when pointed at: the prompts used to say it.</summary>
    private const string CategoryHint = "A category is a folder in both Production and Innovation.";

    /// <summary>
    /// The name typed into the list's box (<see cref="TreeNameEdit"/>), on Enter or on leaving it: saves it, at once, as the
    /// row's. A product's or a category's is renamed; a new folder's adds a category, or picks the one of that name there
    /// already; a new product's is saved with the form as it is, which is the costing defaults and nothing else, then
    /// opened. An empty name, or the name it had, is an Esc (<see cref="CancelTreeName"/>). A name refused shows why, and
    /// the box comes back with it, to be put right. A second call for the same box, as Enter's box is left, does nothing.
    /// </summary>
    public Task CommitTreeNameAsync(string text) => _treeNaming.CommitAsync(text);

    /// <summary>Whether a row of the list is a new product's or a new folder's, still to be named.</summary>
    private static bool IsNewRow(object node) => node is ProductLeafNode { IsNew: true } or ProductFolderNode { IsNewFolder: true };

    /// <summary>What a name typed into the list's box does (<see cref="CommitTreeNameAsync"/>), once it's taken for one.</summary>
    private async Task CommitTreeNameToAsync(TreeNameEdit edit, string name, string text)
    {
        switch (edit.Node)
        {
            case ProductLeafNode { IsNew: true, Product.IsComposite: true } row:
                NameNewComposite(row, name);
                break;

            case ProductLeafNode { IsNew: true } row:
                await SaveNewProductAsync(name);
                if (ProductTree.PendingProduct == row)
                {
                    _treeNaming.Edit = edit with { InitialText = text };
                }

                break;

            case ProductLeafNode leaf:
                await RunSafelyAsync(() => RenameProductToAsync(leaf, name));
                if (ErrorMessage is not null && ProductTree.Find(leaf.Product.Id) is { } stillListed)
                {
                    _treeNaming.Edit = edit with { Node = stillListed, InitialText = text };
                }

                break;

            case ProductFolderNode { IsNewFolder: true } folder:
                _treeNaming.EndNewRow(folder, ProductTree.EndNewFolder);
                ProductCategory? category = null;
                await RunSafelyAsync(async () =>
                {
                    category = Categories.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? await SaveNewCategoryAsync(name);
                });
                if (category is not null && ProductTree.CategoryFolder(folder.Stage, category.Id) is { } saved)
                {
                    SelectedTreeNode = saved;
                }
                else if (ErrorMessage is not null)
                {
                    BeginNewFolderIn(folder.Stage, text);
                }

                break;

            case ProductFolderNode folder:
                await RunSafelyAsync(() => RenameCategoryToAsync(folder, name));
                if (ErrorMessage is not null && Categories.Any(c => c.Id == folder.CategoryId))
                {
                    _treeNaming.Edit = edit with { InitialText = text };
                }

                break;
        }
    }

    /// <summary>
    /// Esc in the list's name box: the name stays as it was, and a new product's or folder's row goes, with nothing saved.
    /// The form stays on the new product, blank, as New leaves it.
    /// </summary>
    public void CancelTreeName() => _treeNaming.Cancel();

    /// <summary>What Esc does to the row named (<see cref="CancelTreeName"/>), once its box is closed.</summary>
    private void TreeNameCancelled(object node)
    {
        switch (node)
        {
            case ProductLeafNode { IsNew: true }:
                EndNewProductRow();
                ApplyRefresh(() => EditingName = string.Empty);
                break;
            case ProductFolderNode { IsNewFolder: true } folder:
                _treeNaming.EndNewRow(folder, ProductTree.EndNewFolder);
                break;
        }
    }

    /// <summary>
    /// The name typed into a new product's row: the form, as New left it, is saved under it, as Save saves it, with its
    /// first version, and opened, and the row gives way to the product's own (EndNewProductRow, as it opens). Refused (a
    /// name taken), the row stays, and why shows.
    /// </summary>
    private async Task SaveNewProductAsync(string name)
    {
        // Not an edit of the user's: there's no undo step for it, and nothing unsaved to ask about if the save is refused.
        ApplyRefresh(() => EditingName = name);
        await RunSafelyAsync(_saveForm);
        if (_editingId == 0 && ErrorMessage is null)
        {
            // Save turned it down without a reason of its own (the details weren't ready yet).
            ErrorMessage = $"{name} couldn't be saved yet. Press Enter to try again.";
        }
    }

    /// <summary>
    /// The name typed into a new composite's row. A composite can't be saved until it holds two products, so the name goes
    /// into the form, as typed there (an undo step, which Save is offered for), and the row keeps it until Save files the
    /// composite, or the form leaves it, unsaved.
    /// </summary>
    private void NameNewComposite(ProductLeafNode row, string name)
    {
        var recorder = (IUndoRecorder)this;
        recorder.BeginUndoStep();
        EditingName = name;
        recorder.EndUndoStep();
        row.Product = new Product
        {
            Name = name,
            Stage = row.Product.Stage,
            CategoryId = row.Product.CategoryId,
            IsComposite = true,
        };
        SuccessMessage = $"Add the products {name} holds in its Products table, then Save it.";
    }

    /// <summary>
    /// Saves <paramref name="name"/> as the product's at once. On the product the form shows, the name box takes it, and the
    /// form's undo steps take it as given, as a move gives them its category: whatever else is typed there stays to be
    /// saved, and no undo brings the old name back for a Save to write.
    /// </summary>
    private async Task RenameProductToAsync(ProductLeafNode leaf, string name)
    {
        var product = leaf.Product;
        await _productRepository.RenameAsync(product.Id, name, CancellationToken.None);

        // What's named after it follows: its own sub-assemblies, as other products' pickers list them.
        await LoadCommand.Execute();
        if (_editingId == product.Id)
        {
            // The Clone tab's name follows too, while it's still the one it started with.
            var oldCloneName = $"{product.Name} Copy";
            var newCloneName = $"{name} Copy";
            ApplyRefresh(() =>
            {
                EditingName = name;
                if (EditingCloneName == oldCloneName)
                {
                    EditingCloneName = newCloneName;
                }
            });
            History.RewriteAll(snapshot => snapshot with
            {
                Name = name,
                CloneName = snapshot.CloneName == oldCloneName ? newCloneName : snapshot.CloneName,
            });
        }
    }

    /// <summary>Renames the folder's category to <paramref name="name"/>: its folder in both stages. A name refused shows why.</summary>
    private async Task RenameCategoryToAsync(ProductFolderNode folder, string name)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return;
        }

        var renamed = new ProductCategory { Id = category.Id, Name = name };
        var validation = _categoryValidator.Validate(renamed);
        if (!validation.IsValid)
        {
            ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
            return;
        }

        await _categoryRepository.UpdateAsync(renamed, CancellationToken.None);
        await ReloadCategoriesAsync();
    }

    /// <summary>
    /// The list's Delete Category: once it's confirmed, deletes the category, whose products are left uncategorized, in
    /// whichever stage they're in.
    /// </summary>
    private Task DeleteCategoryAsync(ProductFolderNode folder)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return Task.CompletedTask;
        }

        var count = Products.Count(p => p.CategoryId == category.Id);
        var products = count switch
        {
            0 => "it holds no products",
            1 => "its one product is left uncategorized",
            _ => $"its {count} products are left uncategorized",
        };

        return RunWithConfirmationAsync(
            $"Delete the '{category.Name}' category? Its folder goes from both Production and Innovation, and {products}. This can't be undone.",
            async () =>
            {
                await _categoryRepository.DeleteAsync(category.Id, CancellationToken.None);

                // Its products lost it in the database: every list is read again, and the form's undo steps forget it, so
                // none puts back a category there's no longer any of to save.
                await LoadCommand.Execute();
                History.RewriteAll(snapshot => snapshot.CategoryId == category.Id ? snapshot with { CategoryId = null } : snapshot);
            });
    }

    /// <summary>
    /// Saves a new category and reads the categories again; null, with the reason showing, if its name is refused. Only
    /// the categories are read: nothing else on the screen changes with a new one.
    /// </summary>
    private async Task<ProductCategory?> SaveNewCategoryAsync(string name)
    {
        var category = new ProductCategory { Name = name };
        var validation = _categoryValidator.Validate(category);
        if (!validation.IsValid)
        {
            ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
            return null;
        }

        // AddAsync fills in the saved Id, which the copy read back is found by.
        await _categoryRepository.AddAsync(category, CancellationToken.None);
        await ReloadCategoriesAsync();
        return Categories.FirstOrDefault(c => c.Id == category.Id);
    }

    /// <summary>
    /// Reads the categories again, keeping the Category picker on the one it holds: a picker whose item gets replaced can
    /// clear its own selection mid-merge.
    /// </summary>
    private async Task ReloadCategoriesAsync()
    {
        var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
        var editingId = EditingCategory?.Id;
        ApplyRefresh(() =>
        {
            _categoryCache.MergeFrom(categories);
            EditingCategory = Categories.FirstOrDefault(c => c.Id == editingId);
        });
    }

    public void BeginCategoryEdit()
    {
        if (!_isCommittingCategory)
        {
            _categoryBeforeEdit = EditingCategory;
        }
    }

    /// <summary>
    /// ICategoryPickerScreen, as on Raw Materials: a name that isn't a category yet is offered as a new one, saved and
    /// picked, so a product can go in a new category without a trip to Settings. Save saves the product's choice. A
    /// cancelled prompt puts back the category the picker had; a name that turns out to be listed is picked instead.
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
            if (Categories.FirstOrDefault(c => IsNamed(c, typed)) is { } listed)
            {
                AsUndoStep(() => EditingCategory = listed);
                return;
            }

            var name = (await PromptForNewCategory.Handle(typed))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                AsUndoStep(() => EditingCategory = _categoryBeforeEdit);
            }
            else if (Categories.FirstOrDefault(c => IsNamed(c, name)) is { } named)
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

        static bool IsNamed(ProductCategory? category, string name) =>
            string.Equals(category?.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Saves a category and picks it. A refused name puts back the category the picker had.</summary>
    private async Task AddCategoryAsync(string name)
    {
        var added = false;
        await RunSafelyAsync(async () =>
        {
            if (await SaveNewCategoryAsync(name) is not { } category)
            {
                return;
            }

            AsUndoStep(() => EditingCategory = category);
            SuccessMessage = $"{category.Name} was added.";
            added = true;
        });

        if (!added)
        {
            AsUndoStep(() => EditingCategory = _categoryBeforeEdit);
        }
    }
}
