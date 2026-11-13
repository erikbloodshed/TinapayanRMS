using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Presentation.ViewModels;

// The list: its tree, search, category filter, menus, moves, folders, naming in place and categories.
public sealed partial class ProductsViewModel
{
    /// <summary>
    /// The list's tree follows the products, the folders, the categories, the search and the category filter, and its
    /// selection the product showing; the flags for a price short of its target markup.
    /// </summary>
    [MemberNotNull(nameof(_products), nameof(_categories), nameof(_visibleProducts))]
    private void WireList()
    {
        SelectedTreeNodes.CollectionChanged += (_, _) => TreeSelectionChanged();

        var byName = SortExpressionComparer<Product>.Ascending(p => p.Name);

        _productCache.Connect()
            .SortAndBind(out _products, byName)
            .Subscribe();

        // A product shows when its name has what's searched for and the category filter takes it.
        var listFilter = this.WhenAnyValue(x => x.SearchText, x => x.CategoryFilter, (text, category) => (Text: text.Trim(), Category: category))
            .DistinctUntilChanged()
            .Select(f => (Func<Product, bool>)(p => Matches(p, f.Text, f.Category)));

        // The list's tree follows both lists once each change has reached them: a product added, removed, renamed, moved or
        // filtered out, and a category added, renamed or deleted, which the category filter offers. The categories are
        // bound first, as the filter's choices read them.
        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<ProductCategory>.Ascending(c => c.Name))
            .Subscribe(_ =>
            {
                RefreshCategoryFilters();
                RefreshProductTree();
            });

        _productCache.Connect()
            .Filter(listFilter)
            .SortAndBind(out _visibleProducts, byName)
            .Subscribe(_ => RefreshProductTree());

        // The search and the category filter decide whether empty folders show, even when they leave the same products
        // showing. A name being typed into a row is given up first: either may hide the row.
        this.WhenAnyValue(x => x.SearchText, x => x.CategoryFilter)
            .Skip(1)
            .Subscribe(_ => CancelTreeName());
        this.WhenAnyValue(x => x.SearchText, x => x.CategoryFilter)
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

    /// <summary>Whether the list shows <paramref name="product"/>: its name has <paramref name="search"/>, and <paramref name="category"/> takes it.</summary>
    private static bool Matches(Product product, string search, ProductCategoryFilter category) =>
        (search.Length == 0 || product.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)) && category.Shows(product);

    /// <summary>The list's commands, each run on the folder or product its menu was opened on (TreeMenuFor).</summary>
    [MemberNotNull(
        nameof(MoveProductsCommand), nameof(MoveToStageCommand), nameof(MoveFolderCommand), nameof(ApplyCostingDefaultsCommand),
        nameof(UndoMoveCommand), nameof(DismissMoveNoticeCommand), nameof(NewProductInCommand), nameof(RenameProductCommand),
        nameof(CloneProductCommand), nameof(CreateVariantOfCommand), nameof(DeleteProductCommand),
        nameof(RestoreProductCommand), nameof(DeletePermanentlyCommand), nameof(NewFolderCommand),
        nameof(RenameFolderCommand), nameof(DeleteFolderCommand), nameof(ExpandAllCommand),
        nameof(PrintFolderCommand), nameof(PrintAllCommand), nameof(CollapseAllCommand), nameof(CutCommand),
        nameof(PasteCommand), nameof(ClearCutCommand))]
    private void WireListCommands()
    {
        // The list's commands, each run on the folder or product its menu was opened on (TreeMenuFor). The moves and their
        // Undo run one at a time, whichever kind (_noMoveRunning).
        MoveProductsCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<ProductMove>>(
            moves => RunSafelyAsync(() => MoveProductsAsync(moves, isUndo: false)), _noMoveRunning);
        MoveToStageCommand = ReactiveCommand.CreateFromTask<StageMoveRequest>(request => RunSafelyAsync(() => MoveToStageAsync(request)), _noMoveRunning);
        MoveFolderCommand = ReactiveCommand.CreateFromTask<ProductFolderMove>(move => RunSafelyAsync(() => MoveFolderAsync(move, isUndo: false)), _noMoveRunning);
        CutCommand = ReactiveCommand.Create<IReadOnlyList<ProductTreeNode>>(nodes => CutNodes = CanDrag(nodes) && nodes.Count > 0 ? [.. nodes] : []);
        PasteCommand = ReactiveCommand.CreateFromTask<ProductTreeNode>(PasteAsync);
        ClearCutCommand = ReactiveCommand.Create(() => { CutNodes = []; });
        ApplyCostingDefaultsCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<Product>>(ApplyCostingDefaultsAsync);
        var hasMoveNotice = this.WhenAnyValue(x => x.MoveNotice).Select(notice => notice is not null);
        UndoMoveCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(UndoMoveAsync), Observable.CombineLatest(hasMoveNotice, _noMoveRunning, (hasNotice, free) => hasNotice && free));
        Observable.CombineLatest(
                MoveProductsCommand.IsExecuting,
                MoveToStageCommand.IsExecuting,
                MoveFolderCommand.IsExecuting,
                UndoMoveCommand.IsExecuting,
                (products, toStage, folder, undo) => !(products || toStage || folder || undo))
            .Subscribe(_noMoveRunning);
        DismissMoveNoticeCommand = ReactiveCommand.Create(() => { MoveNotice = null; });
        NewProductInCommand = ReactiveCommand.CreateFromTask<ProductFolderNode?>(folder => AfterUnsavedChangesAsync(
            () => NewProductInList(folder?.Stage, folder?.FolderId)));
        RenameProductCommand = ReactiveCommand.Create<ProductLeafNode>(BeginRenameProduct);
        CloneProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => MakeFromWithPromptAsync(
            leaf,
            new TextPromptRequest("Clone Product", "Name", $"{leaf.Product.Name} Copy", $"A copy of {leaf.Product.Name} as last saved, filed in Innovation, in a folder like its own."),
            _productCloner.CloneAsync,
            ClonedFrom));
        CreateVariantOfCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => MakeFromWithPromptAsync(
            leaf,
            new TextPromptRequest("Create Variant", "Name", $"{leaf.Product.Name} Copy", $"A new product that links {leaf.Product.Name}'s recipe and packaging, filed in Innovation, in a folder like its own."),
            _productCloner.CreateVariantAsync,
            VariantOf));
        DeleteProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => DeleteProductAsync(leaf.Product));
        RestoreProductCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => RestoreProductAsync(leaf.Product));
        DeletePermanentlyCommand = ReactiveCommand.CreateFromTask<ProductLeafNode>(leaf => DeletePermanentlyAsync(leaf.Product));
        NewFolderCommand = ReactiveCommand.Create<ProductFolderNode?>(BeginNewFolder);
        RenameFolderCommand = ReactiveCommand.Create<ProductFolderNode>(BeginRenameFolder);
        DeleteFolderCommand = ReactiveCommand.CreateFromTask<ProductFolderNode>(DeleteFolderAsync);
        ExpandAllCommand = ReactiveCommand.Create<ProductFolderNode?>(ProductTree.ExpandAll);
        PrintFolderCommand = ReactiveCommand.Create<ProductFolderNode>(folder => _printRequests.OnNext(PrintScope.Folder.Of(folder)));
        PrintAllCommand = ReactiveCommand.Create(() => _printRequests.OnNext(new PrintScope.AllProducts()));
        CollapseAllCommand = ReactiveCommand.Create<ProductFolderNode?>(ProductTree.CollapseAll);
    }

    private void RefreshProductTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its product the one picked.
        var search = SearchText.Trim();
        var category = CategoryFilter;
        _treeSelection.Quietly(() => ProductTree.Refresh(
            Folders,
            VisibleProducts,
            isFiltered: IsListFiltered,
            [.. DeletedProducts.Where(p => Matches(p, search, category))]));

        // A folder picked stays picked, and so does a deleted product, which the form never shows. Otherwise the row goes
        // back to the product picked: a product moved to another folder leaves the tree's selection as it goes, and one a
        // filter hid gets its row back when the filter ends.
        if (SelectedTreeNode is not (ProductFolderNode or ProductLeafNode { IsDeleted: true }))
        {
            SyncTreeSelection();
        }
    }

    /// <summary>
    /// The category filter's choices follow the categories: one renamed keeps its place picked, under its new name, and the
    /// filter on one deleted goes back to All categories.
    /// </summary>
    private void RefreshCategoryFilters()
    {
        var choices = ProductCategoryFilter.ChoicesFor(Categories);
        if (choices.SequenceEqual(CategoryFilters))
        {
            return;
        }

        var picked = CategoryFilter switch
        {
            ProductCategoryFilter.Category category => choices.OfType<ProductCategoryFilter.Category>().FirstOrDefault(c => c.Id == category.Id),
            ProductCategoryFilter.All or ProductCategoryFilter.Uncategorized => CategoryFilter,
        };
        CategoryFilters.Clear();
        foreach (var choice in choices)
        {
            CategoryFilters.Add(choice);
        }

        CategoryFilter = picked ?? new ProductCategoryFilter.All();
    }

    /// <summary>Shows every product again: the search cleared, and the category filter on All categories. For a row the list has to show.</summary>
    private void ClearListFilters()
    {
        SearchText = string.Empty;
        CategoryFilter = new ProductCategoryFilter.All();
    }

    /// <summary>
    /// Puts SelectedProduct's row first among the rows picked in the list, and so makes it SelectedTreeNode: alone, unless
    /// it's picked already, when the rows picked with it stay. Nothing picked, or a filter hiding it, picks no row.
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

    /// <summary>Whether a row picked before a move is still the list's own row, to pick again after it.</summary>
    private bool IsStillListed(object row) => row switch
    {
        ProductLeafNode leaf => ProductTree.Find(leaf.Product.Id) == leaf,
        ProductFolderNode { FolderId: { } id } folder => ProductTree.Folder(id) == folder,
        _ => true,
    };

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
            .Select(found => new ProductMove(found.Value.Id, found.Value.Stage, found.Value.FolderId))
            .ToList();
        var picked = SelectedTreeNodes.ToList();

        await _productRepository.MoveAsync(moves, CancellationToken.None);
        await ShowMovedAsync([.. moves.Select(m => m.ProductId)], picked);

        _moveUndo = isUndo ? null : new MoveUndo.Products(undo, []);
        MoveNotice = isUndo ? null : MoveNoticeFor(moves);
    }

    /// <summary>
    /// Promote, Send Back and Promote All: the products not in <see cref="StageMoveRequest.Stage"/> already go there, each
    /// to the folder at its own folder's path, made if it isn't there.
    /// </summary>
    private async Task MoveToStageAsync(StageMoveRequest request)
    {
        var products = request.Products.Where(p => p.Stage != request.Stage).ToList();
        if (products.Count == 0)
        {
            return;
        }

        var before = products
            .Select(p => _productCache.Lookup(p.Id) is { HasValue: true } found ? found.Value : p)
            .ToDictionary(p => p.Id, p => new ProductMove(p.Id, p.Stage, p.FolderId));
        var picked = SelectedTreeNodes.ToList();

        var moved = await _productRepository.MoveToStageAsync([.. products.Select(p => p.Id)], request.Stage, CancellationToken.None);
        // Read again whether or not this move made any: the folder it found may be one another computer made.
        Folders = await _folderRepository.GetAllAsync(CancellationToken.None);
        await ShowMovedAsync([.. products.Select(p => p.Id)], picked);

        // What the database moved, not what this screen thought would move: one already in that stage there (moved from
        // another computer since this list was read) is left as it is, so neither the notice nor Undo counts it.
        if (moved.Moves.Count == 0)
        {
            return;
        }

        _moveUndo = new MoveUndo.Products([.. moved.Moves.Select(m => before[m.ProductId])], moved.MadeFolderIds);
        MoveNotice = MoveNoticeFor(moved.Moves);
    }

    /// <summary>
    /// The products moved, read again and filed in their new folders. A selection is only re-pointed at the fresh copy, as
    /// a load re-points it: the form isn't loaded again, so whatever's typed into it stays, and no undo step changes, since
    /// Save never writes a saved product's stage or folder. Then the rows the tree dropped from its selection as they moved
    /// are picked again.
    /// </summary>
    private async Task ShowMovedAsync(IReadOnlyCollection<int> productIds, IReadOnlyList<object> picked)
    {
        var moved = await _productRepository.GetByIdsAsync(productIds, CancellationToken.None);
        var onForm = moved.FirstOrDefault(p => p.Id == _editingId);
        ApplyRefresh(() =>
        {
            _productCache.AddOrUpdate(moved.Where(p => !p.IsDeleted));
            RefreshProductTree();
            if (SelectedProduct is { } selected && moved.FirstOrDefault(p => p.Id == selected.Id) is { } fresh)
            {
                RepointSelection(fresh);
            }

            if (onForm is not null)
            {
                ShowUpdatedAt(onForm.UpdatedAt);
            }
        });

        _treeSelection.Restore(picked, IsStillListed);
    }

    /// <summary>
    /// A folder of the user's put somewhere else, with all it holds; the list is read again, as the products in it may have
    /// changed stage. Its row keeps its place among the rows picked.
    /// </summary>
    private async Task MoveFolderAsync(ProductFolderMove move, bool isUndo)
    {
        if (Folders.FirstOrDefault(f => f.Id == move.FolderId) is not { } folder)
        {
            return;
        }

        var back = new ProductFolderMove(folder.Id, folder.Stage, folder.ParentId);
        var picked = SelectedTreeNodes.ToList();
        await _folderRepository.MoveAsync(move, CancellationToken.None);
        await LoadCommand.Execute();
        if (ProductTree.Folder(folder.Id) is { ParentFolder: { } parent })
        {
            ProductTree.OpenDownTo(parent);
        }

        _treeSelection.Restore(picked, IsStillListed);
        _moveUndo = isUndo ? null : new MoveUndo.Folder(back);
        MoveNotice = isUndo ? null : $"Moved {folder.Name} to {ProductFolderRules.PathTitle(Folders, move.Stage, move.ParentId)}";
    }

    /// <summary>
    /// Undo on the notice bar: puts back what the last move took. Products go back to their folders, and the folders a
    /// Promote made for them go again while they hold nothing; a folder goes back where it was.
    /// </summary>
    private async Task UndoMoveAsync()
    {
        switch (_moveUndo)
        {
            case MoveUndo.Products(var back, var madeFolderIds):
                await MoveProductsAsync(back, isUndo: true);
                if (madeFolderIds.Count > 0)
                {
                    await _folderRepository.DeleteIfEmptyAsync(madeFolderIds, CancellationToken.None);
                    Folders = await _folderRepository.GetAllAsync(CancellationToken.None);
                    ApplyRefresh(RefreshProductTree);
                }

                break;

            case MoveUndo.Folder(var back):
                await MoveFolderAsync(back, isUndo: true);
                break;

            case null:
                break;
        }

        _moveUndo = null;
        MoveNotice = null;
    }

    /// <summary>
    /// What the notice bar says a move did: "Moved Pandesal to Production › Breads", "Moved 3 products to Production ›
    /// Breads", "Moved 3 products to Production" (a Promote from several folders), or "Moved 3 products".
    /// </summary>
    private string MoveNoticeFor(IReadOnlyList<ProductMove> moves)
    {
        var what = moves.Count == 1 && _productCache.Lookup(moves[0].ProductId) is { HasValue: true } one ? one.Value.Name : $"{moves.Count} products";
        var first = moves[0];
        if (moves.All(m => m.Stage == first.Stage && m.FolderId == first.FolderId))
        {
            return $"Moved {what} to {ProductFolderRules.PathTitle(Folders, first.Stage, first.FolderId)}";
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
        null => EmptySpaceMenu(),
    };

    /// <summary>
    /// The item a key runs with the row picked, as that row's menu shows the key beside it: F2 and Del with one row picked
    /// alone (with several, a key could take the wrong one); Ctrl+X and Ctrl+V with the first row picked, whose menu covers
    /// every product picked with it, as in a file explorer; Esc forgets what was cut. Null when there's nothing to run.
    /// </summary>
    public TreeMenuItem? TreeShortcut(string gesture) => gesture switch
    {
        "Esc" => CutNodes.Count > 0 ? new TreeMenuItem("Cancel Cut", ClearCutCommand) : null,
        "Ctrl+X" or "Ctrl+V" => SelectedTreeNodes is [ProductTreeNode first, ..] ? Item(first, gesture) : null,
        _ => SelectedTreeNodes is [ProductTreeNode node] ? Item(node, gesture) : null,
    };

    private TreeMenuItem? Item(ProductTreeNode node, string gesture) =>
        TreeMenuFor(node).FirstOrDefault(item => item.Gesture == gesture && item.IsEnabled);

    IReadOnlyList<TreeMenuItem> IListTreeScreen.TreeMenuFor(object? node) => node is null or ProductTreeNode ? TreeMenuFor(node as ProductTreeNode) : [];

    /// <summary>
    /// What can be dragged: products, one or several, or one of the user's folders alone, with all it holds. A stage and the
    /// Deleted folder are fixed, and a deleted product only comes back by Restore.
    /// </summary>
    public static bool CanDrag(IEnumerable<ProductTreeNode> nodes) => nodes.ToList() switch
    {
        [ProductFolderNode { IsUserFolder: true }] => true,
        var dragging => dragging.All(node => node is ProductLeafNode { IsDeleted: false, IsNew: false }),
    };

    // A row a drag starts on is dragged with the rows picked, as a file explorer does: those it allows (products, or one
    // folder of the user's alone) are what's dropped, and DropFor refuses a folder with products.
    bool IListTreeScreen.CanDrag(object node) => node is ProductTreeNode row && CanDrag([row]);

    bool IListTreeScreen.CanDrop(IEnumerable<object> nodes, object? target) => DropFor(nodes, target) is not null;

    async Task<object?> IListTreeScreen.DropAsync(IEnumerable<object> nodes, object? target)
    {
        // Paste runs the same drop, so the guard is DropAsync's, the one place for it.
        if (DropFor(nodes, target) is not { } drop || !await DropAsync(drop))
        {
            return null;
        }

        return drop switch
        {
            TreeDrop.Products(var moves) => ProductTree.Find(moves[0].ProductId),
            TreeDrop.Folder(var move) => ProductTree.Folder(move.FolderId),
            _ => null,
        };
    }

    /// <summary>What dropping <paramref name="nodes"/> on <paramref name="target"/> would do (ProductTree.DropFor).</summary>
    private static TreeDrop? DropFor(IEnumerable<object> nodes, object? target) =>
        nodes.ToList() is { } dragging && dragging.All(n => n is ProductTreeNode)
            ? ProductTree.DropFor([.. dragging.Cast<ProductTreeNode>()], target as ProductTreeNode)
            : null;

    /// <summary>
    /// Runs what dropping rows of the list does, or a paste: the products' moves, or the folder's. False, having done
    /// nothing, while an earlier move is still saving (IListTreeScreen.DropAsync says why).
    /// </summary>
    private async Task<bool> DropAsync(TreeDrop drop)
    {
        if (IsMoving)
        {
            return false;
        }

        switch (drop)
        {
            case TreeDrop.Products(var moves):
                await MoveProductsCommand.Execute(moves);
                break;
            case TreeDrop.Folder(var move):
                await MoveFolderCommand.Execute(move);
                break;
        }

        return true;
    }

    /// <summary>
    /// False while a move of the list's, of products or a folder or to the other stage, or its Undo, is still saving or
    /// reading the list again, and every one of them is off till it's done, from a menu, a key, a drop or a paste: each reads
    /// the list as it stands and takes the notice's Undo, so a second started meanwhile would be judged against the list as
    /// it was, and the two would take each other's Undo. Each command alone was off only while it ran itself.
    /// </summary>
    private readonly BehaviorSubject<bool> _noMoveRunning = new(true);

    /// <summary>A move of the list's, or its Undo, is still saving or reading the list again.</summary>
    private bool IsMoving => !_noMoveRunning.Value;

    private List<TreeMenuItem> ProductsMenu(IReadOnlyList<ProductLeafNode> leaves)
    {
        var products = leaves.Select(l => l.Product).ToList();
        var isOne = products.Count == 1;
        var toProduction = products.Where(p => p.Stage != ProductStage.Production).ToList();
        var toInnovation = products.Where(p => p.Stage != ProductStage.Innovation).ToList();

        List<TreeMenuItem> items = [];
        if (toProduction.Count > 0)
        {
            items.Add(new($"Promote {HowMany(toProduction.Count)}to Production", MoveToStageCommand, new StageMoveRequest(toProduction, ProductStage.Production))
            {
                Icon = AppIcon.MoveUp,
            });
        }

        if (toInnovation.Count > 0)
        {
            items.Add(new($"Send {HowMany(toInnovation.Count)}Back to Innovation", MoveToStageCommand, new StageMoveRequest(toInnovation, ProductStage.Innovation))
            {
                Icon = AppIcon.MoveDown,
            });
        }

        items.Add(new($"Move {(isOne ? string.Empty : $"{products.Count} Products ")}to")
        {
            Icon = AppIcon.Move,
            Items = [MoveProductsTo(ProductStage.Production), MoveProductsTo(ProductStage.Innovation)],
        });
        items.Add(new($"Cut{(isOne ? string.Empty : $" {products.Count} Products")}", CutCommand, (IReadOnlyList<ProductTreeNode>)[.. leaves])
        {
            Gesture = "Ctrl+X",
            Icon = AppIcon.Cut,
        });
        items.Add(PasteItem(leaves[0]));
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
        string HowMany(int count) => isOne ? string.Empty : $"{count} {(count == 1 ? "Product" : "Products")} ";

        // Each place moving every product picked that isn't in it already, greyed when none would move.
        TreeMenuItem MoveProductsTo(ProductStage stage) => MoveToMenu(stage, folderId =>
        {
            var moves = ProductTree.MovesTo(stage, folderId, products);
            return new(string.Empty, MoveProductsCommand, moves) { IsEnabled = moves.Count > 0 };
        });
    }

    /// <summary>
    /// A stage's places in a Move to menu: the top of the stage, then its folders, each holding the folders in it in a
    /// submenu that starts with the folder itself. <paramref name="itemFor"/> makes what moving to a folder (null for the
    /// top of the stage) runs; <paramref name="leaveOut"/> is a folder not offered, with every folder in it: the one being
    /// moved. Greyed when none of its places are open.
    /// </summary>
    private TreeMenuItem MoveToMenu(ProductStage stage, Func<int?, TreeMenuItem> itemFor, int? leaveOut = null)
    {
        var inStage = Folders.Where(f => f.Stage == stage && f.Id != leaveOut).ToLookup(f => f.ParentId);
        var title = ProductRules.StageTitle(stage);
        List<TreeMenuItem> items = [itemFor(null) with { Header = title }];
        if (FolderItems(null) is { Count: > 0 } top)
        {
            items.Add(TreeMenuItem.Separator);
            items.AddRange(top);
        }

        return new(title) { Items = items, IsEnabled = items.Any(i => i.IsEnabled && !i.IsSeparator) };

        // A folder with none in it is an item; one with folders in it a submenu. Only those reached from the top of the
        // stage are offered, so a loop of folders left by an edit outside the app is never walked.
        List<TreeMenuItem> FolderItems(int? parentId) =>
        [
            .. inStage[parentId]
                .OrderBy(f => f.Name, StringComparer.CurrentCulture)
                .Select(f =>
                {
                    var item = itemFor(f.Id) with { Header = f.Name };
                    var inside = FolderItems(f.Id);
                    return inside.Count == 0
                        ? item
                        : new TreeMenuItem(f.Name) { Items = [item, TreeMenuItem.Separator, .. inside], IsEnabled = item.IsEnabled || inside.Any(i => i.IsEnabled) };
                }),
        ];
    }

    /// <summary>
    /// Paste on <paramref name="target"/>'s menu: what was cut moves into it (a stage's folder or one of the user's), or into
    /// the folder of the product it is; greyed with nothing cut, or nothing that would move there.
    /// </summary>
    private TreeMenuItem PasteItem(ProductTreeNode target) =>
        new("Paste", PasteCommand, target) { Gesture = "Ctrl+V", Icon = AppIcon.Paste, IsEnabled = PasteFor(target) is not null };

    /// <summary>What pasting onto <paramref name="target"/> would do: a drop there of what was cut and is still listed.</summary>
    private TreeDrop? PasteFor(ProductTreeNode target) =>
        CutNodes.Where(IsStillListed).ToList() is { Count: > 0 } cut ? ProductTree.DropFor(cut, target) : null;

    /// <summary>
    /// Moves what was cut onto <paramref name="target"/>, as a drop there would, and forgets it. Something no longer listed
    /// (deleted since, or a folder gone) is left out; nothing that would move leaves the cut as it was.
    /// </summary>
    private async Task PasteAsync(ProductTreeNode target)
    {
        if (PasteFor(target) is not { } drop || IsMoving)
        {
            return;
        }

        CutNodes = [];
        await DropAsync(drop);
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
        new("New Folder", NewFolderCommand, stage) { Icon = AppIcon.NewFolder },
        TreeMenuItem.Separator,
        PasteItem(stage),
        new("Print Stage", PrintFolderCommand, stage) { Icon = AppIcon.Print },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand, stage),
        new("Collapse All", CollapseAllCommand, stage),
    ];

    /// <summary>One of the user's folders.</summary>
    private List<TreeMenuItem> FolderMenu(ProductFolderNode folder)
    {
        List<TreeMenuItem> items =
        [
            new("New Product Here", NewProductInCommand, folder) { Icon = AppIcon.Add },
            new("New Composite Product Here", NewCompositeProductInCommand, folder) { Icon = AppIcon.Composite },
            new("New Folder", NewFolderCommand, folder) { Icon = AppIcon.NewFolder },
            TreeMenuItem.Separator,
        ];

        // What the folder shows, the folders in it included: with a filter on, its matches.
        var toProduction = folder.AllProducts.Where(l => !l.IsNew).Select(l => l.Product).Where(p => p.Stage != ProductStage.Production).ToList();
        if (toProduction.Count > 0)
        {
            items.Add(new("Promote All to Production", MoveToStageCommand, new StageMoveRequest(toProduction, ProductStage.Production))
            {
                Icon = AppIcon.MoveUp,
            });
        }

        var folderId = folder.FolderId!.Value;
        items.AddRange(
        [
            new("Move to") { Icon = AppIcon.Move, Items = [MoveFolderTo(ProductStage.Production), MoveFolderTo(ProductStage.Innovation)] },
            new("Cut", CutCommand, (IReadOnlyList<ProductTreeNode>)[folder]) { Gesture = "Ctrl+X", Icon = AppIcon.Cut },
            PasteItem(folder),
            new("Print Folder", PrintFolderCommand, folder) { Icon = AppIcon.Print },
            TreeMenuItem.Separator,
            new("Rename", RenameFolderCommand, folder) { Gesture = "F2", Icon = AppIcon.Rename },
            new("Delete", DeleteFolderCommand, folder) { Gesture = "Del", Icon = AppIcon.Delete },
            TreeMenuItem.Separator,
            new("Expand All", ExpandAllCommand, folder),
            new("Collapse All", CollapseAllCommand, folder),
        ]);
        return items;

        // Every place but itself and the folders in it, greyed where it is already.
        TreeMenuItem MoveFolderTo(ProductStage stage) => MoveToMenu(
            stage,
            parentId => new(string.Empty, MoveFolderCommand, new ProductFolderMove(folderId, stage, parentId))
            {
                IsEnabled = stage != folder.Stage || parentId != folder.ParentFolderId,
            },
            leaveOut: folderId);
    }

    private List<TreeMenuItem> EmptySpaceMenu() =>
    [
        new("New Product", NewProductInCommand) { Icon = AppIcon.Add },
        new("New Composite Product", NewCompositeProductInCommand) { Icon = AppIcon.Composite },
        new("New Folder", NewFolderCommand) { Icon = AppIcon.NewFolder },
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

    /// <summary>A folder's Rename: its name becomes a box to type into, in place, and the name typed renames it.</summary>
    private void BeginRenameFolder(ProductFolderNode folder)
    {
        if (!folder.IsUserFolder)
        {
            return;
        }

        CancelTreeName();
        _treeNaming.Start(folder, folder.Title, FolderHint);
    }

    /// <summary>
    /// The list's New Folder: a "New folder" row inside <paramref name="from"/> (a stage's folder or one of the user's),
    /// picked, with its name ready to be typed, as in a file explorer; the name typed adds the folder
    /// (<see cref="CommitTreeNameAsync"/>). From the list's empty space or its toolbar, it goes in the folder picked, or the
    /// folder of the product picked, or at the top of Production. The filters are cleared first, so the row shows.
    /// </summary>
    private void BeginNewFolder(ProductFolderNode? from)
    {
        var (stage, parentId) = from is { IsDeletedFolder: false, IsNewFolder: false }
            ? (from.Stage, from.FolderId)
            : (SelectedTreeNode as ProductTreeNode) switch
            {
                ProductFolderNode { IsDeletedFolder: false, IsNewFolder: false } folder => (folder.Stage, folder.FolderId),
                ProductLeafNode { IsDeleted: false, IsNew: false } leaf => (leaf.Product.Stage, leaf.Product.FolderId),
                ProductFolderNode or ProductLeafNode or null => (ProductStage.Production, (int?)null),
            };
        BeginNewFolderIn(stage, parentId, ProductFolderNode.NewFolderTitle);
    }

    private void BeginNewFolderIn(ProductStage stage, int? parentId, string text)
    {
        CancelTreeName();
        ClearListFilters();
        _treeNaming.Start(ProductTree.BeginNewFolder(stage, parentId), text, FolderHint);
    }

    /// <summary>What the name box on a folder says when pointed at.</summary>
    private const string FolderHint = "A folder holds products and other folders. It isn't a category: a product's category is set on its form.";

    /// <summary>
    /// The name typed into the list's box (<see cref="TreeNameEdit"/>), on Enter or on leaving it: saves it, at once, as the
    /// row's. A product's or a folder's is renamed; a new folder's adds the folder; a new product's is saved with the form
    /// as it is, which is the costing defaults and nothing else, then opened. An empty name, or the name it had, is an Esc
    /// (<see cref="CancelTreeName"/>). A name refused shows why, and the box comes back with it, to be put right. A second
    /// call for the same box, as Enter's box is left, does nothing.
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
                var stage = folder.Stage;
                var parentId = folder.ParentFolderId;
                ProductFolder? added = null;
                await RunSafelyAsync(async () =>
                {
                    var adding = new ProductFolder { Name = name, Stage = stage, ParentId = parentId };
                    await _folderRepository.AddAsync(adding, CancellationToken.None);
                    await ReloadFoldersAsync();
                    added = adding;
                });
                if (added is not null && ProductTree.Folder(added.Id) is { } saved)
                {
                    SelectedTreeNode = saved;
                }
                else if (ErrorMessage is not null)
                {
                    BeginNewFolderIn(stage, parentId, text);
                }

                break;

            case ProductFolderNode { FolderId: { } folderId }:
                await RunSafelyAsync(async () =>
                {
                    await _folderRepository.RenameAsync(folderId, name, CancellationToken.None);
                    await ReloadFoldersAsync();
                });
                if (ErrorMessage is not null && ProductTree.Folder(folderId) is { } stillThere)
                {
                    _treeNaming.Edit = edit with { Node = stillThere, InitialText = text };
                }

                break;
        }
    }

    /// <summary>Reads the folders again, and files the list's rows in them: only the folders change.</summary>
    private async Task ReloadFoldersAsync()
    {
        Folders = await _folderRepository.GetAllAsync(CancellationToken.None);
        ApplyRefresh(RefreshProductTree);
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
        AsUndoStep(() => EditingName = name);
        row.Product = new Product
        {
            Name = name,
            Stage = row.Product.Stage,
            FolderId = row.Product.FolderId,
            CategoryId = row.Product.CategoryId,
            IsComposite = true,
        };
        SuccessMessage = $"Add the products {name} holds in its Products table, then Save it.";
    }

    /// <summary>
    /// Saves <paramref name="name"/> as the product's at once. On the product the form shows, the name box takes it, and the
    /// form's undo steps take it as given: whatever else is typed there stays to be saved, and no undo brings the old name
    /// back for a Save to write.
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

    /// <summary>
    /// A folder's Delete: once it's confirmed, deletes the folder and nothing in it. Its products (deleted ones too) and
    /// folders go up into the folder it was in, or the top of its stage; a new product started in it goes there with them.
    /// </summary>
    private Task DeleteFolderAsync(ProductFolderNode folder)
    {
        if (folder.FolderId is not { } id)
        {
            return Task.CompletedTask;
        }

        var parentId = folder.ParentFolderId;
        var products = Products.Count(p => p.FolderId == id);
        var folders = Folders.Count(f => f.ParentId == id);
        var into = ProductFolderRules.PathTitle(Folders, folder.Stage, parentId);
        var holds = (products, folders) switch
        {
            (0, 0) => "It's empty.",
            _ => $"Its {string.Join(" and ", new[] { Counted(products, "product"), Counted(folders, "folder") }.Where(c => c.Length > 0))} "
                + $"{(products + folders == 1 ? "moves" : "move")} up to {into}.",
        };

        return RunWithConfirmationAsync(
            $"Delete the '{folder.Title}' folder? {holds}",
            async () =>
            {
                await _folderRepository.DeleteAsync(id, CancellationToken.None);
                if (NewProductFolderId == id)
                {
                    NewProductFolderId = parentId;
                }

                await LoadCommand.Execute();
            });

        static string Counted(int count, string what) => count switch
        {
            0 => string.Empty,
            1 => $"1 {what}",
            _ => $"{count} {what}s",
        };
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
            ErrorMessage = validation.ToString(" ");
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
