using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// What the Products list asks of its screen, on top of what any record screen's list asks: the product the form shows and
/// the lists as the screen last loaded them, and what the list's actions do to the form and to the products.
/// </summary>
public interface IProductsListHost : IRecordListHost
{
    /// <summary>The product picked, which the form shows; null for a new one.</summary>
    Product? SelectedProduct { get; }

    /// <summary>The products in the Deleted folder, as last loaded.</summary>
    IReadOnlyList<Product> DeletedProducts { get; }

    /// <summary>The user's folders, in both stages, as last read: the list reads them again after a change of its own.</summary>
    IReadOnlyList<ProductFolder> Folders { get; set; }

    /// <summary>The form is a composite product's.</summary>
    bool IsEditingComposite { get; }

    /// <summary>Reads the screen's lists again (its LoadCommand), keeping the form and what's typed into it.</summary>
    Task LoadAsync();

    /// <summary>A product's row picked in the list: it opens, once the form's unsaved changes are saved or let go.</summary>
    void Pick(Product product);

    /// <summary>A fresh form for a new product (a composite one with <paramref name="isComposite"/>), filed in <paramref name="stage"/> and <paramref name="folderId"/>, or Settings' stage for none.</summary>
    void StartNew(ProductStage? stage, int? folderId, bool isComposite);

    /// <summary>The name typed into the new product's row: the form, as New left it, is saved under it, and opened.</summary>
    Task SaveNewAsync(string name);

    /// <summary>The name typed into a new composite's row goes into the form, as typed there: one undo step.</summary>
    void NameNew(string name);

    /// <summary>The new product's row went with Esc: the name its form was given goes too, as no edit of the user's.</summary>
    void ClearNewName();

    /// <summary><paramref name="product"/>, as it was, renamed in the list, saved: the form and its undo steps take the name as given if it's the one showing.</summary>
    void RenamedOnForm(Product product, string name);

    /// <summary>The products a move took, read again, within the refresh that files them: the form's product is re-pointed at its fresh copy.</summary>
    void ShowMoved(IReadOnlyList<Product> moved);

    /// <summary>Clones <paramref name="product"/> as saved under <paramref name="name"/>, as the Clone tab does, and opens the clone.</summary>
    Task CloneAsync(Product product, string name);

    /// <summary>Makes a variant of <paramref name="product"/> named <paramref name="name"/>, as the Clone tab does, and opens it.</summary>
    Task CreateVariantAsync(Product product, string name);

    /// <summary>Moves the product to the Deleted folder once it's confirmed, as the header's Delete does.</summary>
    Task DeleteAsync(Product product);

    /// <summary>Takes a product out of the Deleted folder and opens it.</summary>
    Task RestoreAsync(Product product);

    /// <summary>Deletes a product in the Deleted folder for good, once it's confirmed.</summary>
    Task DeletePermanentlyAsync(Product product);

    /// <summary>Sets the products' costing to Settings' costing defaults, once it's confirmed.</summary>
    Task ApplyCostingDefaultsAsync(IReadOnlyList<Product> products);

    /// <summary>Asks the Print screen to print <paramref name="scope"/> (the screen's PrintRequests).</summary>
    void Print(PrintScope scope);
}

/// <summary>
/// The Products list (through ProductsViewModel.List; ProductList draws it): every product, filed in its stage and the
/// user's folders, with the Deleted folder last; searched by name and filtered by category; the rows picked, their menus and
/// keys, moves to other folders and the other stage and their Undo, Cut and Paste, the folders themselves, and names typed in
/// place. The products, folders and categories are the screen's, which loads them; what the list's actions do to the
/// product the form shows, or to a product as a record (a clone, a delete), it asks of the screen
/// (<see cref="IProductsListHost"/>).
/// </summary>
public sealed class ProductsList : RecordList, IListTreeScreen, IDisposable
{
    /// <summary>The view state key the closed folders of the Products list are kept under (IViewStateStore).</summary>
    public const string ClosedFoldersKey = "Products.ClosedFolders";

    /// <summary>
    /// The view state key the list kept its open folders under, before it kept the closed ones: read while there are no
    /// closed ones kept, so the list opens as it was left (FolderMemory.Read).
    /// </summary>
    public const string OpenFoldersKey = "Products.OpenFolders";

    /// <summary>What the name box on a folder says when pointed at.</summary>
    private const string FolderHint = "A folder holds products and other folders. It isn't a category: a product's category is set on its form.";

    private readonly IProductsListHost _host;
    private readonly SourceCache<Product, int> _productCache;
    private readonly IReadOnlyList<ProductCategory> _categories;
    private readonly NewProductPlace _newProductPlace;
    private readonly ReadOnlyObservableCollection<Product> _visibleProducts;
    private readonly IProductRepository _productRepository;
    private readonly IProductFolderRepository _folderRepository;
    private readonly FolderStateWriter _closedFoldersWriter;

    /// <summary>Where what the last move took was before it: what Undo on the notice puts back. Null with nothing to undo.</summary>
    private MoveUndo? _moveUndo;

    /// <summary>Each change to the search or the category filter, what the list's filter follows (SearchText, CategoryFilter).</summary>
    private readonly BehaviorSubject<System.Reactive.Unit> _filterChanges = new(System.Reactive.Unit.Default);

    /// <param name="host">The screen, which the list asks for the product its form shows, the lists it loaded, and what to do to them.</param>
    /// <param name="productCache">The screen's products, which the list searches, filters and moves.</param>
    /// <param name="categoryCache">The screen's categories, whose changes the category filter's choices follow.</param>
    /// <param name="categories">Every category, by name, as the screen binds them: the category filter's choices.</param>
    /// <param name="newProductPlace">Where Save files the new product: its row starts there, and a folder deleted moves it.</param>
    public ProductsList(
        IProductsListHost host,
        SourceCache<Product, int> productCache,
        SourceCache<ProductCategory, int> categoryCache,
        IReadOnlyList<ProductCategory> categories,
        NewProductPlace newProductPlace,
        IProductRepository productRepository,
        IProductFolderRepository folderRepository,
        IViewStateStore viewStateStore)
    {
        _host = host;
        _productCache = productCache;
        _categories = categories;
        _newProductPlace = newProductPlace;
        _productRepository = productRepository;
        _folderRepository = folderRepository;

        // The list's folders open as they were left last time, a folder new since included, and the ones closed are written
        // for next time (FolderStateWriter); Dispose writes any change still waiting.
        ProductTree = new ProductTree(FolderMemory.Read(viewStateStore, ClosedFoldersKey, OpenFoldersKey, ProductFolderNode.CurrentFolderKey));
        _closedFoldersWriter = new FolderStateWriter(viewStateStore, ClosedFoldersKey, ProductTree.ExpansionChanged, ProductTree.ClosedFolderKeys);

        // A product shows when its name has what's searched for and the category filter takes it.
        var listFilter = _filterChanges
            .Select(_ => (Text: SearchText.Trim(), Category: CategoryFilter))
            .DistinctUntilChanged()
            .Select(f => (Func<Product, bool>)(p => Matches(p, f.Text, f.Category)));

        // The list's tree follows both lists once each change has reached them: a product added, removed, renamed, moved or
        // filtered out, and a category added, renamed or deleted, which the category filter offers. The screen binds the
        // categories first, so the filter's choices read them changed.
        categoryCache.Connect()
            .Subscribe(_ =>
            {
                RefreshCategoryFilters();
                RefreshTree();
            });

        productCache.Connect()
            .Filter(listFilter)
            .SortAndBind(out _visibleProducts, SortExpressionComparer<Product>.Ascending(p => p.Name))
            .Subscribe(_ => RefreshTree());

        // The search and the category filter decide whether empty folders show, even when they leave the same products
        // showing (FilterChanged).
        RefreshTree();

        // The list's commands, each run on the folder or product its menu was opened on (TreeMenuFor). The moves and their
        // Undo run one at a time, whichever kind (IsMoving).
        MoveProductsCommand = new ScreenCommand<IReadOnlyList<ProductMove>>(
            moves => host.RunSafelyAsync(() => MoveProductsAsync(moves, isUndo: false)), _ => !IsMoving);
        MoveToStageCommand = new ScreenCommand<StageMoveRequest>(request => host.RunSafelyAsync(() => MoveToStageAsync(request)), _ => !IsMoving);
        MoveFolderCommand = new ScreenCommand<ProductFolderMove>(move => host.RunSafelyAsync(() => MoveFolderAsync(move, isUndo: false)), _ => !IsMoving);
        CutCommand = ScreenCommand.Of<IReadOnlyList<ProductTreeNode>>(nodes => CutNodes = CanDrag(nodes) && nodes.Count > 0 ? [.. nodes] : []);
        PasteCommand = new ScreenCommand<ProductTreeNode>(PasteAsync);
        ClearCutCommand = ScreenCommand.Of(() => CutNodes = []);
        ApplyCostingDefaultsCommand = new ScreenCommand<IReadOnlyList<Product>>(host.ApplyCostingDefaultsAsync);
        UndoMoveCommand = new ScreenCommand(() => host.RunSafelyAsync(UndoMoveAsync), () => MoveNotice is not null && !IsMoving);

        // A new product replaces the one showing, so its unsaved changes are asked about first.
        NewProductInCommand = new ScreenCommand<ProductFolderNode?>(folder => host.AfterUnsavedChangesAsync(
            () => NewInList(folder?.Stage, folder?.FolderId)));
        NewCompositeProductInCommand = new ScreenCommand<ProductFolderNode?>(folder => host.AfterUnsavedChangesAsync(
            () => NewInList(folder?.Stage, folder?.FolderId, isComposite: true)));
        RenameProductCommand = ScreenCommand.Of<ProductLeafNode>(BeginRenameProduct);
        CloneProductCommand = new ScreenCommand<ProductLeafNode>(leaf => MakeFromWithPromptAsync(
            leaf,
            new TextPromptRequest("Clone Product", "Name", $"{leaf.Product.Name} Copy", $"A copy of {leaf.Product.Name} as last saved, filed in Innovation, in a folder like its own."),
            host.CloneAsync));
        CreateVariantOfCommand = new ScreenCommand<ProductLeafNode>(leaf => MakeFromWithPromptAsync(
            leaf,
            new TextPromptRequest("Create Variant", "Name", $"{leaf.Product.Name} Copy", $"A new product that links {leaf.Product.Name}'s recipe and packaging, filed in Innovation, in a folder like its own."),
            host.CreateVariantAsync));
        DeleteProductCommand = new ScreenCommand<ProductLeafNode>(leaf => host.DeleteAsync(leaf.Product));
        RestoreProductCommand = new ScreenCommand<ProductLeafNode>(leaf => host.RestoreAsync(leaf.Product));
        DeletePermanentlyCommand = new ScreenCommand<ProductLeafNode>(leaf => host.DeletePermanentlyAsync(leaf.Product));
        NewFolderCommand = ScreenCommand.Of<ProductFolderNode?>(BeginNewFolder);
        RenameFolderCommand = ScreenCommand.Of<ProductFolderNode>(BeginRenameFolder);
        DeleteFolderCommand = new ScreenCommand<ProductFolderNode>(DeleteFolderAsync);
        ExpandAllCommand = ScreenCommand.Of<ProductFolderNode?>(ProductTree.ExpandAll);
        PrintFolderCommand = ScreenCommand.Of<ProductFolderNode>(folder => host.Print(PrintScope.Folder.Of(folder)));
        PrintAllCommand = ScreenCommand.Of(() => host.Print(new PrintScope.AllProducts()));
        CollapseAllCommand = ScreenCommand.Of<ProductFolderNode?>(ProductTree.CollapseAll);
    }

    /// <summary>The Products list (ProductList): VisibleProducts filed in their stage, in the user's folders.</summary>
    public ProductTree ProductTree { get; }

    /// <summary>The products the list shows: those whose name matches SearchText, in the category CategoryFilter picks.</summary>
    public ReadOnlyObservableCollection<Product> VisibleProducts => _visibleProducts;

    /// <summary>The products picked in the list, in the order they were picked, deleted ones left out.</summary>
    public IReadOnlyList<ProductLeafNode> SelectedTreeProducts => [.. SelectedTreeNodes.OfType<ProductLeafNode>().Where(l => !l.IsDeleted && !l.IsNew)];

    private string _searchText = string.Empty;
    /// <summary>What's typed into the list's search: the products whose name holds it show (FilterChanged).</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _searchText, value);
            FilterChanged();
        }
    }

    private ProductCategoryFilter _categoryFilter = new ProductCategoryFilter.All();
    /// <summary>
    /// The list's category filter: every product, or only a category's (or the uncategorized ones), each in its folder,
    /// with only the folders holding one showing, as a search shows its matches. With the search, both have to match.
    /// </summary>
    public ProductCategoryFilter CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            var filter = value ?? new ProductCategoryFilter.All();
            if (EqualityComparer<ProductCategoryFilter>.Default.Equals(_categoryFilter, filter))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _categoryFilter, filter);
            FilterChanged();
        }
    }

    /// <summary>
    /// The search or the category filter changed: the list shows what they take, and they decide whether empty folders
    /// show, even when they leave the same products showing. A name being typed into a row is given up first: either may
    /// hide the row.
    /// </summary>
    private void FilterChanged()
    {
        _filterChanges.OnNext(System.Reactive.Unit.Default);
        CancelTreeName();
        RefreshTree();
    }

    /// <summary>The category filter's choices: All categories, each category by name, then Uncategorized.</summary>
    public ObservableCollection<ProductCategoryFilter> CategoryFilters { get; } = [new ProductCategoryFilter.All(), new ProductCategoryFilter.Uncategorized()];

    /// <summary>Whether the list shows only some products: a search typed, or a category picked.</summary>
    private bool IsListFiltered => SearchText.Trim().Length > 0 || CategoryFilter is not ProductCategoryFilter.All;

    private IReadOnlyList<ProductTreeNode> _cutNodes = [];
    /// <summary>
    /// The rows Cut (Ctrl+X) took, as in a file explorer: products, or one folder of the user's, faded in the list until
    /// Paste (Ctrl+V) moves them, Esc forgets them, or another Cut takes their place. Nothing is saved until the paste.
    /// </summary>
    public IReadOnlyList<ProductTreeNode> CutNodes
    {
        get => _cutNodes;
        private set => this.RaiseAndSetIfChanged(ref _cutNodes, value);
    }

    /// <summary>
    /// Products moved to other folders of the Products list (ProductList), by a drag or Move to: files each in its
    /// folder and stage straight away, with no Save, and says so on the list's notice bar with an Undo (MoveNotice). Its
    /// category stays as it is. The form's Save never writes a saved product's stage or folder, so whatever is typed into
    /// the form stays as it is.
    /// </summary>
    public ScreenCommand<IReadOnlyList<ProductMove>> MoveProductsCommand { get; }

    /// <summary>
    /// Promote to Production, Send Back to Innovation, and a folder's Promote All: files the products in the other stage,
    /// each in the folder at the same path as its own there, made if it isn't (IProductRepository.MoveToStageAsync), with
    /// the notice bar's Undo, which takes away again the folders it made while they're still empty.
    /// </summary>
    public ScreenCommand<StageMoveRequest> MoveToStageCommand { get; }

    /// <summary>
    /// A folder of the user's dragged, or moved by its Move to: puts it, with all it holds, in another folder or at the top
    /// of a stage, straight away (IProductFolderRepository.MoveAsync), with the notice bar's Undo. Moved to the other stage,
    /// the folders and products in it go too.
    /// </summary>
    public ScreenCommand<ProductFolderMove> MoveFolderCommand { get; }

    /// <summary>Cut on the list: the products picked (or the one the menu was opened on), or one folder, wait to be pasted.</summary>
    public ScreenCommand<IReadOnlyList<ProductTreeNode>> CutCommand { get; }

    /// <summary>
    /// Paste on the list: what was cut moves into the folder it was asked on (a stage's, one of the user's, or the folder of
    /// the product it was asked on), as a drop there does, with the notice bar's Undo.
    /// </summary>
    public ScreenCommand<ProductTreeNode> PasteCommand { get; }

    /// <summary>Esc on the list: what was cut is forgotten, and stays where it is.</summary>
    public ScreenCommand ClearCutCommand { get; }

    /// <summary>
    /// Sets the costing of the products picked in the list (or the one its menu was opened on) to Settings' costing
    /// defaults, once it's confirmed, recording a version of each one it changes (VersionedSaves.ApplyCostingAsync).
    /// </summary>
    public ScreenCommand<IReadOnlyList<Product>> ApplyCostingDefaultsCommand { get; }

    /// <summary>Undo on the list's notice bar: puts the products the last move took back where they were.</summary>
    public ScreenCommand UndoMoveCommand { get; }

    /// <summary>
    /// New Product Here on a folder of the list: a new product, as New starts one, filed in that folder's stage and
    /// category. Null, from the list's empty space, is New itself. Like New, it adds the product's row to the list for its
    /// name to be typed into (TreeNameEdit), and the name typed saves it.
    /// </summary>
    public ScreenCommand<ProductFolderNode?> NewProductInCommand { get; }

    /// <summary>New Composite Product Here on a folder of the list, as New Product Here; null, from the list's empty space, is New composite product itself.</summary>
    public ScreenCommand<ProductFolderNode?> NewCompositeProductInCommand { get; }

    /// <summary>
    /// Rename on a product of the list: its name becomes a box to type into, in place (TreeNameEdit), and the name typed is
    /// saved at once, like a move (IProductRepository.RenameAsync).
    /// </summary>
    public ScreenCommand<ProductLeafNode> RenameProductCommand { get; }

    /// <summary>Clone on a product of the list: asks for the new product's name, then clones it as saved, as the Clone tab does.</summary>
    public ScreenCommand<ProductLeafNode> CloneProductCommand { get; }

    /// <summary>Create Variant on a product of the list: asks for the variant's name, then makes it, as the Clone tab does.</summary>
    public ScreenCommand<ProductLeafNode> CreateVariantOfCommand { get; }

    /// <summary>Delete on a product of the list: Delete for that product, asking first.</summary>
    public ScreenCommand<ProductLeafNode> DeleteProductCommand { get; }

    /// <summary>Restore on a product in the Deleted folder: puts it back in its folder, and picks it.</summary>
    public ScreenCommand<ProductLeafNode> RestoreProductCommand { get; }

    /// <summary>Delete Permanently on a product in the Deleted folder: asks first, then deletes it for good.</summary>
    public ScreenCommand<ProductLeafNode> DeletePermanentlyCommand { get; }

    /// <summary>
    /// New Folder on the list: adds a "New folder" row inside the folder it was asked on (a stage's or one of the user's)
    /// for its name to be typed into (TreeNameEdit), and the name typed adds the folder there. From the list's empty space
    /// or its toolbar, it goes in the folder picked, or the folder of the product picked, or at the top of Production.
    /// </summary>
    public ScreenCommand<ProductFolderNode?> NewFolderCommand { get; }

    /// <summary>Rename on one of the user's folders: its name becomes a box to type into, in place, and the name typed renames it.</summary>
    public ScreenCommand<ProductFolderNode> RenameFolderCommand { get; }

    /// <summary>
    /// Delete on one of the user's folders: asks first, then deletes it, and nothing in it: its products and folders go up
    /// into the folder it was in.
    /// </summary>
    public ScreenCommand<ProductFolderNode> DeleteFolderCommand { get; }

    /// <summary>Expand All: opens every folder in the folder given (a stage's or one of the user's), or in both stages.</summary>
    public ScreenCommand<ProductFolderNode?> ExpandAllCommand { get; }

    /// <summary>Collapse All: closes every folder in the folder given, or in both stages, leaving the one given and the stages open.</summary>
    public ScreenCommand<ProductFolderNode?> CollapseAllCommand { get; }

    /// <summary>Print Folder, or Print Stage, on the list's folder: its products, as saved, on the Print tab.</summary>
    public ScreenCommand<ProductFolderNode> PrintFolderCommand { get; }

    /// <summary>Print All Products on the list's empty space: every product, as saved, on the Print tab.</summary>
    public ScreenCommand PrintAllCommand { get; }

    /// <summary>
    /// Input: what the prompt is for, its box's label, the name it starts with, and a line saying more. Output: the name
    /// typed, or null if it was cancelled. The page answers it with a dialog (Prompts). Asked by the list's Clone and
    /// Create Variant; the list names everything else in place (TreeNameEdit).
    /// </summary>
    public Interaction<TextPromptRequest, string?> PromptForName { get; } = new();

    /// <summary>Whether the list shows <paramref name="product"/>: its name has <paramref name="search"/>, and <paramref name="category"/> takes it.</summary>
    private static bool Matches(Product product, string search, ProductCategoryFilter category) =>
        (search.Length == 0 || product.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)) && category.Shows(product);

    /// <summary>
    /// Files the list's rows again from the screen's folders and products, as the search and the category filter show them,
    /// and the Deleted folder's.
    /// </summary>
    public void RefreshTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its product the one picked.
        var search = SearchText.Trim();
        var category = CategoryFilter;
        ListSelection.Quietly(() => ProductTree.Refresh(
            _host.Folders,
            VisibleProducts,
            isFiltered: IsListFiltered,
            [.. _host.DeletedProducts.Where(p => Matches(p, search, category))]));

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
        var choices = ProductCategoryFilter.ChoicesFor(_categories);
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
    public void ClearListFilters()
    {
        SearchText = string.Empty;
        CategoryFilter = new ProductCategoryFilter.All();
    }

    protected override object? ShowingRow => _host.SelectedProduct is { } product ? ProductTree.Find(product.Id) : ProductTree.PendingProduct;

    protected override string PickedRecordsProperty => nameof(SelectedTreeProducts);

    // A product's row picks it. A deleted product's row is picked as a folder's is, leaving the form as it is: it's there to
    // restore. So is the new product's, which is the form's already.
    protected override void PickRowFromList(object row)
    {
        if (row is ProductLeafNode { IsDeleted: false, IsNew: false } leaf && leaf.Product.Id != _host.SelectedProduct?.Id)
        {
            _host.Pick(leaf.Product);
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
        _host.Folders = await _folderRepository.GetAllAsync(CancellationToken.None);
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
        _host.ApplyRefresh(() =>
        {
            _productCache.AddOrUpdate(moved.Where(p => !p.IsDeleted));
            RefreshTree();
            _host.ShowMoved(moved);
        });

        ListSelection.Restore(picked, IsStillListed);
    }

    /// <summary>
    /// A folder of the user's put somewhere else, with all it holds; the list is read again, as the products in it may have
    /// changed stage. Its row keeps its place among the rows picked.
    /// </summary>
    private async Task MoveFolderAsync(ProductFolderMove move, bool isUndo)
    {
        if (_host.Folders.FirstOrDefault(f => f.Id == move.FolderId) is not { } folder)
        {
            return;
        }

        var back = new ProductFolderMove(folder.Id, folder.Stage, folder.ParentId);
        var picked = SelectedTreeNodes.ToList();
        await _folderRepository.MoveAsync(move, CancellationToken.None);
        await _host.LoadAsync();
        if (ProductTree.Folder(folder.Id) is { ParentFolder: { } parent })
        {
            ProductTree.OpenDownTo(parent);
        }

        ListSelection.Restore(picked, IsStillListed);
        _moveUndo = isUndo ? null : new MoveUndo.Folder(back);
        MoveNotice = isUndo ? null : $"Moved {folder.Name} to {ProductFolderRules.PathTitle(_host.Folders, move.Stage, move.ParentId)}";
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
                    _host.Folders = await _folderRepository.GetAllAsync(CancellationToken.None);
                    _host.ApplyRefresh(RefreshTree);
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
            return $"Moved {what} to {ProductFolderRules.PathTitle(_host.Folders, first.Stage, first.FolderId)}";
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
                await MoveProductsCommand.ExecuteAsync(moves);
                break;
            case TreeDrop.Folder(var move):
                await MoveFolderCommand.ExecuteAsync(move);
                break;
        }

        return true;
    }

    /// <summary>A move of the list's, or its Undo, is still saving or reading the list again.</summary>
    /// <summary>
    /// True while a move of the list's, of products or a folder or to the other stage, or its Undo, is still saving or
    /// reading the list again, and every one of them is off till it's done, from a menu, a key, a drop or a paste: each reads
    /// the list as it stands and takes the notice's Undo, so a second started meanwhile would be judged against the list as
    /// it was, and the two would take each other's Undo. Each command alone was off only while it ran itself.
    /// </summary>
    private bool IsMoving =>
        MoveProductsCommand.IsRunning || MoveToStageCommand.IsRunning || MoveFolderCommand.IsRunning || UndoMoveCommand.IsRunning;

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
        var inStage = _host.Folders.Where(f => f.Stage == stage && f.Id != leaveOut).ToLookup(f => f.ParentId);
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

    /// <summary>The list's Clone and Create Variant: asks for the new product's name first, starting from "… Copy".</summary>
    private async Task MakeFromWithPromptAsync(ProductLeafNode leaf, TextPromptRequest prompt, Func<Product, string, Task> make)
    {
        var name = (await PromptForName.Handle(prompt))?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            await make(leaf.Product, name);
        }
    }

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
        ListNaming.Start(leaf, leaf.Product.Name);
    }

    /// <summary>A folder's Rename: its name becomes a box to type into, in place, and the name typed renames it.</summary>
    private void BeginRenameFolder(ProductFolderNode folder)
    {
        if (!folder.IsUserFolder)
        {
            return;
        }

        CancelTreeName();
        ListNaming.Start(folder, folder.Title, FolderHint);
    }

    /// <summary>
    /// The list's New Folder: a "New folder" row inside <paramref name="from"/> (a stage's folder or one of the user's),
    /// picked, with its name ready to be typed, as in a file explorer; the name typed adds the folder
    /// (<see cref="RecordList.CommitTreeNameAsync"/>). From the list's empty space or its toolbar, it goes in the folder
    /// picked, or the folder of the product picked, or at the top of Production. The filters are cleared first, so the row
    /// shows.
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
        ListNaming.Start(ProductTree.BeginNewFolder(stage, parentId), text, FolderHint);
    }

    /// <summary>
    /// New and New Product Here: the screen's new product (<see cref="IProductsListHost.StartNew"/>), and its row in the
    /// list, in the folder Save would file it in, picked, with its name ready to be typed, as a new file in a file explorer.
    /// The name typed saves it (<see cref="RecordList.CommitTreeNameAsync"/>). The search and the category filter are
    /// cleared first, so the row shows.
    /// </summary>
    public void NewInList(ProductStage? stage = null, int? folderId = null, bool isComposite = false)
    {
        CancelTreeName();
        ClearListFilters();
        _host.StartNew(stage, folderId, isComposite);
        var row = ProductTree.BeginNewProduct(_newProductPlace.Stage, _newProductPlace.FolderId, _host.IsEditingComposite);
        ListNaming.Start(row, row.Product.Name);
    }

    /// <summary>
    /// Takes the new product's row out of the list (<see cref="NewInList"/>), and the rows picked, and stops the naming in
    /// it. The form is left as it is.
    /// </summary>
    public void EndNewRow() => ListNaming.EndNewRow(ProductTree.PendingProduct, ProductTree.EndNewProduct);

    /// <summary>Whether a row of the list is a new product's or a new folder's, still to be named.</summary>
    protected override bool IsNewRow(object node) => node is ProductLeafNode { IsNew: true } or ProductFolderNode { IsNewFolder: true };

    /// <summary>
    /// What a name typed into the list's box does (<see cref="RecordList.CommitTreeNameAsync"/>), once it's taken for one:
    /// it's saved, at once, as the row's. A product's or a folder's is renamed; a new folder's adds the folder; a new
    /// product's is saved with the form as it is, which is the costing defaults and nothing else, then opened.
    /// </summary>
    protected override async Task CommitTreeNameToAsync(TreeNameEdit edit, string name, string text)
    {
        switch (edit.Node)
        {
            case ProductLeafNode { IsNew: true, Product.IsComposite: true } row:
                NameNewComposite(row, name);
                break;

            case ProductLeafNode { IsNew: true } row:
                await _host.SaveNewAsync(name);
                if (ProductTree.PendingProduct == row)
                {
                    ListNaming.Edit = edit with { InitialText = text };
                }

                break;

            case ProductLeafNode leaf:
                await _host.RunSafelyAsync(() => RenameProductToAsync(leaf, name));
                if (_host.ErrorMessage is not null && ProductTree.Find(leaf.Product.Id) is { } stillListed)
                {
                    ListNaming.Edit = edit with { Node = stillListed, InitialText = text };
                }

                break;

            case ProductFolderNode { IsNewFolder: true } folder:
                ListNaming.EndNewRow(folder, ProductTree.EndNewFolder);
                var stage = folder.Stage;
                var parentId = folder.ParentFolderId;
                ProductFolder? added = null;
                await _host.RunSafelyAsync(async () =>
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
                else if (_host.ErrorMessage is not null)
                {
                    BeginNewFolderIn(stage, parentId, text);
                }

                break;

            case ProductFolderNode { FolderId: { } folderId }:
                await _host.RunSafelyAsync(async () =>
                {
                    await _folderRepository.RenameAsync(folderId, name, CancellationToken.None);
                    await ReloadFoldersAsync();
                });
                if (_host.ErrorMessage is not null && ProductTree.Folder(folderId) is { } stillThere)
                {
                    ListNaming.Edit = edit with { Node = stillThere, InitialText = text };
                }

                break;
        }
    }

    /// <summary>Reads the folders again, and files the list's rows in them: only the folders change.</summary>
    private async Task ReloadFoldersAsync()
    {
        _host.Folders = await _folderRepository.GetAllAsync(CancellationToken.None);
        _host.ApplyRefresh(RefreshTree);
    }

    /// <summary>
    /// What Esc does to the row named (<see cref="RecordList.CancelTreeName"/>), once its box is closed: a new product's or
    /// folder's row goes, with nothing saved. The form stays on the new product, blank, as New leaves it.
    /// </summary>
    protected override void TreeNameCancelled(object node)
    {
        switch (node)
        {
            case ProductLeafNode { IsNew: true }:
                EndNewRow();
                _host.ClearNewName();
                break;
            case ProductFolderNode { IsNewFolder: true } folder:
                ListNaming.EndNewRow(folder, ProductTree.EndNewFolder);
                break;
        }
    }

    /// <summary>
    /// The name typed into a new composite's row. A composite can't be saved until it holds two products, so the name goes
    /// into the form, as typed there (an undo step, which Save is offered for), and the row keeps it until Save files the
    /// composite, or the form leaves it, unsaved.
    /// </summary>
    private void NameNewComposite(ProductLeafNode row, string name)
    {
        _host.NameNew(name);
        row.Product = new Product
        {
            Name = name,
            Stage = row.Product.Stage,
            FolderId = row.Product.FolderId,
            CategoryId = row.Product.CategoryId,
            IsComposite = true,
        };
        _host.ShowSuccess($"Add the products {name} holds in its Products table, then Save it.");
    }

    /// <summary>
    /// Saves <paramref name="name"/> as the product's at once. On the product the form shows, the name box takes it, and the
    /// form's undo steps take it as given: whatever else is typed there stays to be saved, and no undo brings the old name
    /// back for a Save to write (IProductsListHost.RenamedOnForm).
    /// </summary>
    private async Task RenameProductToAsync(ProductLeafNode leaf, string name)
    {
        var product = leaf.Product;
        await _productRepository.RenameAsync(product.Id, name, CancellationToken.None);

        // What's named after it follows: its own sub-assemblies, as other products' pickers list them.
        await _host.LoadAsync();
        _host.RenamedOnForm(product, name);
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
        var products = _productCache.Items.Count(p => p.FolderId == id);
        var folders = _host.Folders.Count(f => f.ParentId == id);
        var into = ProductFolderRules.PathTitle(_host.Folders, folder.Stage, parentId);
        var holds = (products, folders) switch
        {
            (0, 0) => "It's empty.",
            _ => $"Its {string.Join(" and ", new[] { Counted(products, "product"), Counted(folders, "folder") }.Where(c => c.Length > 0))} "
                + $"{(products + folders == 1 ? "moves" : "move")} up to {into}.",
        };

        return _host.RunWithConfirmationAsync(
            $"Delete the '{folder.Title}' folder? {holds}",
            async () =>
            {
                await _folderRepository.DeleteAsync(id, CancellationToken.None);
                _newProductPlace.FolderDeleted(id, parentId);
                await _host.LoadAsync();
            });

        static string Counted(int count, string what) => count switch
        {
            0 => string.Empty,
            1 => $"1 {what}",
            _ => $"{count} {what}s",
        };
    }

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        _closedFoldersWriter.Dispose();
        ProductTree.Dispose();
        _filterChanges.Dispose();
    }
}
