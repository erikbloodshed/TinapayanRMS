using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Configurations;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.Products;

/// <summary>A row of the Products list's tree (ProductTree): a folder, or a product in one.</summary>
public closed class ProductTreeNode : ListTreeNode
{
    /// <summary>One of the two top-level folders, Production and Innovation, which hold the user's folders.</summary>
    public abstract bool IsStage { get; }

    /// <summary>A row at the top of the list, shown in bold: a stage's folder, or the Deleted folder.</summary>
    public virtual bool IsTopLevel => IsStage;

    private bool _isBelowTargetMarkup;
    /// <summary>
    /// A product whose current price earns less than its target markup (ProductCostSummary.IsBelowTargetMarkup): a flag after
    /// its name. Worked out in the background after each load; never set on a folder.
    /// </summary>
    public bool IsBelowTargetMarkup
    {
        get => _isBelowTargetMarkup;
        set => RaiseAndSetIfChanged(ref _isBelowTargetMarkup, value);
    }

    private bool _hasSectionWithoutPieceSize;
    /// <summary>
    /// A product with a recipe section that has no weight per piece or yield (SavedProduct.SectionsWithoutPieceSize), which
    /// costs it nothing per piece: a flag after its name. Worked out with <see cref="IsBelowTargetMarkup"/>; never set on a folder.
    /// </summary>
    public bool HasSectionWithoutPieceSize
    {
        get => _hasSectionWithoutPieceSize;
        set => RaiseAndSetIfChanged(ref _hasSectionWithoutPieceSize, value);
    }
}

/// <summary>
/// A folder of the Products list: a stage's (Production, Innovation), or one of the user's (ProductFolder) inside a stage
/// or inside another of theirs, as deep as wanted; or the Deleted folder after the stages, holding the deleted products.
/// The list finds its rows in <see cref="Children"/>: its folders first, by name, then its products, by name. A product has
/// no children at all.
/// </summary>
public sealed class ProductFolderNode : ProductTreeNode, ITreeFolder<ProductTreeNode>
{
    private ProductFolderNode(ProductStage stage, int? folderId, bool isStage, string title, bool isDeletedFolder = false, bool isNewFolder = false)
    {
        _stage = stage;
        FolderId = folderId;
        IsStage = isStage;
        IsDeletedFolder = isDeletedFolder;
        IsNewFolder = isNewFolder;
        _title = title;
        Children.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(Icon));
    }

    /// <summary>
    /// The row New Folder adds inside <paramref name="parent"/> (a stage's folder or one of the user's) while its name is
    /// typed into it (ProductTree.BeginNewFolder): no folder yet, until the name typed saves one, and none at all if it's
    /// cancelled.
    /// </summary>
    public static ProductFolderNode NewFolder(ProductFolderNode parent) =>
        new(parent.Stage, null, isStage: false, NewFolderTitle, isNewFolder: true) { ParentFolder = parent };

    public const string NewFolderTitle = "New folder";

    /// <summary>The row New Folder adds, named in the list before it's a folder: nothing is dropped on it, and it has no menu.</summary>
    public bool IsNewFolder { get; }

    /// <summary>A stage's folder.</summary>
    public static ProductFolderNode ForStage(ProductStage stage) => new(stage, null, isStage: true, ProductRules.StageTitle(stage));

    /// <summary>One of the user's folders.</summary>
    public static ProductFolderNode ForFolder(ProductFolder folder) => new(folder.Stage, folder.Id, isStage: false, folder.Name);

    /// <summary>
    /// The Deleted folder, holding the deleted products by name, whatever their stage and folder, until each is restored
    /// or deleted permanently. Closed the first time the list shows, as every other folder is open. Its Stage means nothing.
    /// </summary>
    public static ProductFolderNode Deleted() => new(default, null, isStage: false, DeletedTitle, isDeletedFolder: true);

    public const string DeletedTitle = "Deleted";

    private ProductStage _stage;
    /// <summary>The stage it's in: a folder moved to the other stage keeps its row, in its new place.</summary>
    public ProductStage Stage
    {
        get => _stage;
        internal set => RaiseAndSetIfChanged(ref _stage, value);
    }

    /// <summary>The user's folder (ProductFolder) it shows, or null for a stage's folder, Deleted, and a new folder's row.</summary>
    public int? FolderId { get; }

    /// <summary>One of the user's folders: it can be renamed, moved and deleted, as a stage's and Deleted can't.</summary>
    public bool IsUserFolder => FolderId is not null;

    /// <summary>The folder it's in: its stage's, or another of the user's; null for a stage and for Deleted.</summary>
    public ProductFolderNode? ParentFolder { get; internal set; }

    /// <summary>The user's folder it's in, or null at the top of its stage: where a folder made in it, or moved beside it, goes.</summary>
    public int? ParentFolderId => ParentFolder?.FolderId;

    public override bool IsStage { get; }

    /// <summary>The Deleted folder, after the stages: its products are dragged nowhere and nothing is dropped on it.</summary>
    public bool IsDeletedFolder { get; }

    public override bool IsTopLevel => IsStage || IsDeletedFolder;

    /// <summary>
    /// What names the folder from one run to the next, for remembering whether it was left closed: "Production",
    /// "Innovation", "Folder/12" for the user's folder 12, and "Deleted".
    /// </summary>
    public string Key =>
        IsDeletedFolder ? DeletedTitle
        : IsStage ? Stage.ToString()
        : IsNewFolder ? "Folder/New"
        : $"Folder/{FolderId?.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// A folder's <see cref="Key"/> as kept before Innovation was renamed on 2026-11-09 ("ResearchAndDevelopment"), as its
    /// key now ("Innovation"); any other key as it is. Read as kept, Innovation left closed would open again
    /// (FolderMemory.Read). The category folders' keys kept before folders were the user's own ("Production/12") match no
    /// folder now, and are forgotten.
    /// </summary>
    public static string CurrentFolderKey(string key)
    {
        const string former = ProductStageConverter.FormerInnovationName;
        return key == former ? nameof(ProductStage.Innovation)
            : key.StartsWith(former + "/", StringComparison.Ordinal) ? nameof(ProductStage.Innovation) + key[former.Length..]
            : key;
    }

    /// <summary>The products it shows itself, not those in the folders in it.</summary>
    public IEnumerable<ProductLeafNode> Products => Children.OfType<ProductLeafNode>();

    /// <summary>The folders it shows.</summary>
    public IEnumerable<ProductFolderNode> Folders => Children.OfType<ProductFolderNode>();

    /// <summary>The products it shows, and those every folder in it shows, however deep.</summary>
    public IEnumerable<ProductLeafNode> AllProducts => Products.Concat(Folders.SelectMany(f => f.AllProducts));

    /// <summary>Whether it's <paramref name="folder"/> or inside it, however deep.</summary>
    public bool IsInside(ProductFolderNode folder)
    {
        for (var at = this; at is not null; at = at.ParentFolder)
        {
            if (ReferenceEquals(at, folder))
            {
                return true;
            }
        }

        return false;
    }

    private int _count;
    /// <summary>The products it holds, in the folders in it too, or while the list is filtered, the matches.</summary>
    public override int Count => _count;

    internal void ShowCount(int count) => RaiseAndSetIfChanged(ref _count, count, nameof(Count));

    private string _title;
    public override string Title => _title;

    /// <summary>A folder renamed keeps its row, under its new name.</summary>
    internal void Rename(string title) => RaiseAndSetIfChanged(ref _title, title, nameof(Title));

    /// <summary>
    /// FolderOpen while the folder is open on its rows, and Folder otherwise, empty ones included; the Deleted folder's is
    /// always Delete, the bin.
    /// </summary>
    public override AppIcon? Icon => IsDeletedFolder ? AppIcon.Delete : IsExpanded && Children.Count > 0 ? AppIcon.FolderOpen : AppIcon.Folder;

    public override bool IsFolder => true;

    public ObservableCollection<ProductTreeNode> Children { get; } = [];

    private bool _isExpanded;
    /// <summary>
    /// The folder is open. The list reads and writes it, and it's kept here, so a folder stays as it was left through a
    /// refresh, or while a search hides it.
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

    void ITreeFolder<ProductTreeNode>.ShowCount(int count) => ShowCount(count);

    void ITreeFolder<ProductTreeNode>.AnnounceExpanded() => AnnounceExpanded();
}

/// <summary>
/// A product in its folder. There's one for each product, kept while the product is listed, so a fresh copy of the
/// product (after a load, or a move) updates it rather than replacing it, and a product moved to another folder takes
/// it along.
/// </summary>
public sealed class ProductLeafNode(Product product) : ProductTreeNode
{
    private Product _product = product;
    public Product Product
    {
        get => _product;
        internal set
        {
            if (!ReferenceEquals(_product, value))
            {
                _product = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(Title));
            }
        }
    }

    public override string Title => Product.Name;

    public override AppIcon? Icon => null;

    public override bool IsFolder => false;

    public override bool IsStage => false;

    /// <summary>It's in the Deleted folder: it can be restored or deleted permanently, and nothing else.</summary>
    public bool IsDeleted => Product.IsDeleted;

    /// <summary>
    /// The row New Product adds while its name is typed into it (ProductTree.BeginNewProduct), over the product the form
    /// starts: not saved, so it has no menu, isn't dragged, and picking it loads nothing.
    /// </summary>
    public bool IsNew => Product.Id == 0;
}

/// <summary>What dropping rows of the Products list on another does (ProductTree.DropFor).</summary>
public closed record TreeDrop
{
    /// <summary>Products dragged: each one's move, those already there left out.</summary>
    public sealed record Products(IReadOnlyList<ProductMove> Moves) : TreeDrop;

    /// <summary>One folder dragged, with all it holds.</summary>
    public sealed record Folder(ProductFolderMove Move) : TreeDrop;
}

/// <summary>
/// The Products list as a tree, as a file explorer shows folders: a folder for each stage, Production then Innovation,
/// holding the user's folders (ProductFolder), as deep as they go, and the products filed at the top of the stage; each of
/// those folders its own folders first, by name, then its products, by name. Then the Deleted folder, holding the deleted
/// products (by name). Refresh merges rather than rebuilds: every folder and product keeps its node, which the tree's
/// selection and open folders are tied to, through a rename, a move or a move to the other stage.
/// </summary>
public sealed class ProductTree : IDisposable
{
    private readonly TreeLeaves<Product, ProductLeafNode> _leaves = new(p => p.Id, p => new ProductLeafNode(p), (leaf, p) => leaf.Product = p);

    /// <summary>The user's folders, shown or not, by id.</summary>
    private readonly Dictionary<int, ProductFolderNode> _folders = [];

    /// <summary>Which folders start open, and every one opened or closed since.</summary>
    private readonly FolderMemory _memory;

    /// <summary>
    /// A Refresh with something in it has made the folders, so <see cref="ClosedFolderKeys"/> reads the folders. The
    /// screen refreshes its tree, empty, as it's made, which doesn't count: disposed before a load, it would keep the stages alone.
    /// </summary>
    private bool _isFilled;

    /// <param name="memory">
    /// The folders left closed last time (<see cref="ClosedFolderKeys"/>, by <see cref="ProductFolderNode.Key"/>); null for
    /// none kept, when every folder starts open but Deleted.
    /// </param>
    public ProductTree(FolderMemory? memory = null)
    {
        _memory = memory ?? new FolderMemory();
        Stages = [_memory.Track(ProductFolderNode.ForStage(ProductStage.Production)), _memory.Track(ProductFolderNode.ForStage(ProductStage.Innovation))];
        DeletedFolder = _memory.Track(ProductFolderNode.Deleted(), startsOpen: false);
        Roots = [.. Stages, DeletedFolder];
    }

    /// <summary>The list's top-level folders: the two stages, Production first, then Deleted. They're always there.</summary>
    public IReadOnlyList<ProductFolderNode> Roots { get; }

    /// <summary>The two stage folders, Production first.</summary>
    public IReadOnlyList<ProductFolderNode> Stages { get; }

    /// <summary>The Deleted folder, after the stages, holding the deleted products.</summary>
    public ProductFolderNode DeletedFolder { get; }

    /// <summary>A folder opened or closed, by a click, a search or Expand All: for remembering them (ClosedFolderKeys).</summary>
    public IObservable<Unit> ExpansionChanged => _memory.ExpansionChanged;

    /// <summary>The product's row, or null while it isn't listed (a filter hides it, or it isn't loaded).</summary>
    public ProductLeafNode? Find(int productId) => _leaves.Find(productId);

    /// <summary>The stage's folder.</summary>
    public ProductFolderNode StageFolder(ProductStage stage) => Stages.Single(r => r.Stage == stage);

    /// <summary>The user's folder's row, shown or not; null for one the list hasn't been given.</summary>
    public ProductFolderNode? Folder(int folderId) => _folders.GetValueOrDefault(folderId);

    /// <summary>
    /// The row a product of <paramref name="stage"/> filed in <paramref name="folderId"/> shows in: that folder's, or the
    /// stage's for none, or one the list hasn't been given.
    /// </summary>
    public ProductFolderNode FolderOf(ProductStage stage, int? folderId) =>
        folderId is { } id && _folders.TryGetValue(id, out var folder) ? folder : StageFolder(stage);

    /// <summary>Where a folder of the list is, as the notices name it: "Production › Breads › Holiday", or "Production" for a stage.</summary>
    public static string PathTitle(ProductFolderNode folder)
    {
        List<string> names = [];
        ProductFolderNode? at = folder;
        for (; at is { IsStage: false, IsDeletedFolder: false }; at = at.ParentFolder)
        {
            names.Add(at.Title);
        }

        names.Reverse();
        return ProductFolderRules.PathTitle(at?.Stage ?? folder.Stage, names);
    }

    /// <summary>
    /// Every folder closed now, stages, the user's folders and Deleted, by <see cref="ProductFolderNode.Key"/>: what to start
    /// with next time. Before the first Refresh, which makes the folders, the ones it was given.
    /// </summary>
    public IReadOnlyList<string> ClosedFolderKeys() => _memory.ClosedKeys(Roots.Concat(_folders.Values), _isFilled);

    /// <summary>
    /// Opens <paramref name="from"/> and every folder in it, however deep: a stage's or one of the user's; both stages and the
    /// Deleted folder when it's null.
    /// </summary>
    public void ExpandAll(ProductFolderNode? from)
    {
        foreach (var folder in FoldersFrom(from).Concat(from is null ? Stages : [from]))
        {
            folder.IsExpanded = true;
        }

        if (from is null)
        {
            DeletedFolder.IsExpanded = true;
        }
    }

    /// <summary>
    /// Closes every folder in <paramref name="from"/>, however deep, or in both stages, and the Deleted folder, when it's
    /// null. <paramref name="from"/> stays open, and so do the stages, so the folders in them show, closed.
    /// </summary>
    public void CollapseAll(ProductFolderNode? from)
    {
        foreach (var folder in FoldersFrom(from))
        {
            folder.IsExpanded = false;
        }

        if (from is null)
        {
            DeletedFolder.IsExpanded = false;
        }
    }

    /// <summary>The user's folders inside <paramref name="from"/> (a stage's or one of theirs), shown or not; all of them for null.</summary>
    private IEnumerable<ProductFolderNode> FoldersFrom(ProductFolderNode? from) =>
        from is null || from.IsDeletedFolder
            ? (from is null ? _folders.Values : [])
            : _folders.Values.Where(f => !ReferenceEquals(f, from) && f.IsInside(from));

    /// <summary>The row New Folder added, while its name is being typed; null otherwise.</summary>
    public ProductFolderNode? PendingFolder { get; private set; }

    /// <summary>The row New Product added, while its name is being typed and saved; null otherwise.</summary>
    public ProductLeafNode? PendingProduct { get; private set; }

    /// <summary>
    /// Adds a "New folder" row inside the user's folder <paramref name="parentFolderId"/>, or at the top of
    /// <paramref name="stage"/> for none, after the folders there and before the products, for its name to be typed into;
    /// the folders it's in open to show it. A refresh keeps it there until <see cref="EndNewFolder"/>. Any earlier one goes
    /// first: there's one at a time.
    /// </summary>
    public ProductFolderNode BeginNewFolder(ProductStage stage, int? parentFolderId)
    {
        EndNewFolder();
        var parent = FolderOf(stage, parentFolderId);
        var folder = PendingFolder = ProductFolderNode.NewFolder(parent);
        var at = parent.Children.TakeWhile(row => row is ProductFolderNode).Count();
        parent.Children.Insert(at, folder);
        OpenDownTo(parent);
        return folder;
    }

    /// <summary>Takes away the row <see cref="BeginNewFolder"/> added, if there is one.</summary>
    public void EndNewFolder()
    {
        if (PendingFolder is { } folder)
        {
            PendingFolder = null;
            folder.ParentFolder?.Children.Remove(folder);
        }
    }

    /// <summary>
    /// Adds a "New product" row to the user's folder <paramref name="folderId"/>, or the top of <paramref name="stage"/> for
    /// none (or one the list hasn't been given), last, for its name to be typed into; the folders it's in open to show it.
    /// A refresh keeps it there, uncounted, until <see cref="EndNewProduct"/>. Any earlier one goes first: there's one at a time.
    /// </summary>
    public ProductLeafNode BeginNewProduct(ProductStage stage, int? folderId, bool isComposite)
    {
        EndNewProduct();
        var folder = FolderOf(stage, folderId);
        var row = PendingProduct = new ProductLeafNode(new Product
        {
            Name = isComposite ? NewCompositeProductTitle : NewProductTitle,
            Stage = folder.Stage,
            FolderId = folder.FolderId,
            IsComposite = isComposite,
        });
        folder.Children.Add(row);
        OpenDownTo(folder);
        return row;
    }

    public const string NewProductTitle = "New product";

    public const string NewCompositeProductTitle = "New composite product";

    /// <summary>Takes away the row <see cref="BeginNewProduct"/> added, if there is one.</summary>
    public void EndNewProduct()
    {
        if (PendingProduct is { } row)
        {
            PendingProduct = null;
            foreach (var folder in Stages.Concat(_folders.Values))
            {
                folder.Children.Remove(row);
            }
        }
    }

    /// <summary>Opens <paramref name="folder"/> and every folder it's in, so what's in it shows.</summary>
    public static void OpenDownTo(ProductFolderNode folder)
    {
        for (var at = folder; at is not null; at = at.ParentFolder)
        {
            at.IsExpanded = true;
        }
    }

    public void Dispose() => _memory.Dispose();

    /// <summary>
    /// Where dropping <paramref name="product"/> on <paramref name="target"/> files it: a stage's folder at the top of that
    /// stage, one of the user's folders in that folder (and its stage), and another product in the folder that one is in.
    /// Null when that's where it is already, or there's nothing to drop on. A deleted product goes nowhere, and nothing goes
    /// into the Deleted folder by a move: Delete and Restore do that.
    /// </summary>
    public static ProductMove? MoveTo(ProductTreeNode? target, Product product)
    {
        (ProductStage Stage, int? FolderId)? place = product.IsDeleted || product.Id == 0 ? null : target switch
        {
            ProductFolderNode { IsDeletedFolder: true } or ProductLeafNode { IsDeleted: true } => null,
            ProductFolderNode { IsNewFolder: true } or ProductLeafNode { IsNew: true } => null,
            ProductFolderNode folder => (folder.Stage, folder.FolderId),
            ProductLeafNode leaf => (leaf.Product.Stage, leaf.Product.FolderId),
            null => null,
        };

        return place is { } to && (to.Stage != product.Stage || to.FolderId != product.FolderId)
            ? new ProductMove(product.Id, to.Stage, to.FolderId)
            : null;
    }

    /// <summary>
    /// What dropping several products on <paramref name="target"/> does: <see cref="MoveTo(ProductTreeNode?, Product)"/>
    /// for each, leaving out any that are there already. Empty when none of them would move.
    /// </summary>
    public static IReadOnlyList<ProductMove> MovesTo(ProductTreeNode? target, IEnumerable<Product> products) =>
        [.. products.Select(p => MoveTo(target, p)).OfType<ProductMove>()];

    /// <summary>
    /// Filing each of <paramref name="products"/> in the user's folder <paramref name="folderId"/>, or at the top of
    /// <paramref name="stage"/> for none, leaving out any that are there already: the list's Move to.
    /// </summary>
    public static IReadOnlyList<ProductMove> MovesTo(ProductStage stage, int? folderId, IEnumerable<Product> products) =>
        [.. from product in products
            where product.Stage != stage || product.FolderId != folderId
            select new ProductMove(product.Id, stage, folderId)];

    /// <summary>
    /// Where dropping the user's <paramref name="folder"/> on <paramref name="target"/> puts it: in a stage's folder, at
    /// the top of that stage; in another of the user's folders, inside it; on a product, beside it, in the folder it's in.
    /// Null for itself or a folder inside it, where it is already, the Deleted folder or a row being named.
    /// </summary>
    public static ProductFolderMove? FolderMoveTo(ProductTreeNode? target, ProductFolderNode folder)
    {
        if (folder.FolderId is not { } folderId)
        {
            return null;
        }

        (ProductStage Stage, int? ParentId)? place = target switch
        {
            ProductFolderNode { IsDeletedFolder: true } or ProductLeafNode { IsDeleted: true } => null,
            ProductFolderNode { IsNewFolder: true } or ProductLeafNode { IsNew: true } => null,
            ProductFolderNode { IsStage: true } stage => (stage.Stage, null),
            ProductFolderNode into when !into.IsInside(folder) => (into.Stage, into.FolderId),
            ProductLeafNode leaf when leaf.Product.FolderId is not { } id || !Holds(folder, id) => (leaf.Product.Stage, leaf.Product.FolderId),
            ProductFolderNode or ProductLeafNode or null => null,
        };

        return place is { } to && (to.Stage != folder.Stage || to.ParentId != folder.ParentFolderId)
            ? new ProductFolderMove(folderId, to.Stage, to.ParentId)
            : null;

        // A folder dropped on a product inside it would go inside itself. A product shows only with every folder it's in,
        // so the folders the dragged one shows are enough to look in.
        static bool Holds(ProductFolderNode dragged, int id) => dragged.FolderId == id || dragged.Folders.Any(f => Holds(f, id));
    }

    /// <summary>
    /// What dropping <paramref name="dragged"/> on <paramref name="target"/> (or the list's empty space, null) does:
    /// products move into the folder (<see cref="MovesTo(ProductTreeNode?, IEnumerable{Product})"/>), and one folder of
    /// the user's moves with all it holds (<see cref="FolderMoveTo"/>). Null when nothing would move, or what's dragged is
    /// neither: products and folders together, or several folders.
    /// </summary>
    public static TreeDrop? DropFor(IReadOnlyList<ProductTreeNode> dragged, ProductTreeNode? target) => dragged switch
    {
        [ProductFolderNode folder] => FolderMoveTo(target, folder) is { } move ? new TreeDrop.Folder(move) : null,
        { Count: > 0 } when dragged.All(n => n is ProductLeafNode) =>
            MovesTo(target, dragged.Cast<ProductLeafNode>().Select(l => l.Product)) is { Count: > 0 } moves ? new TreeDrop.Products(moves) : null,
        _ => null,
    };

    /// <summary>
    /// Brings the tree in line with the user's <paramref name="folders"/>, the <paramref name="products"/> it shows and the
    /// <paramref name="deleted"/> products (none when it's null). Every folder shows, so an empty one can still be dropped
    /// on; while the list is filtered (<paramref name="isFiltered"/>: a search, or a category picked), only the folders
    /// holding a match, however deep, show, open, and the Deleted folder opens on its own.
    /// </summary>
    public void Refresh(IReadOnlyList<ProductFolder> folders, IReadOnlyList<Product> products, bool isFiltered, IReadOnlyList<Product>? deleted = null)
    {
        deleted ??= [];
        _leaves.Merge(products.Concat(deleted));
        MergeFolders(folders);

        // Each product's row goes in its folder, or the top of its stage for none, or a folder the list hasn't been given.
        var productsIn = products
            .Select(p => (Row: _leaves[p.Id], In: FolderOf(p.Stage, p.FolderId)))
            .ToLookup(p => p.In, p => p.Row);

        // How many products each folder holds, the folders in it included: whether it shows while the list is filtered.
        Dictionary<ProductFolderNode, int> counts = [];
        foreach (var stage in Stages)
        {
            Count(stage);
        }

        // Each folder's rows as they should show: its folders, then the new folder's row, then its products, then the new
        // product's row; the rows being named stay where they were added.
        Dictionary<ProductFolderNode, List<ProductTreeNode>> wanted = [];
        foreach (var container in Stages.Concat(_folders.Values))
        {
            List<ProductTreeNode> rows =
            [
                .. SubfoldersOf(container).Where(f => !isFiltered || counts[f] > 0).OrderBy(f => f.Title, StringComparer.CurrentCulture).ThenBy(f => f.FolderId),
            ];
            if (PendingFolder is { } newFolder && ReferenceEquals(newFolder.ParentFolder, container))
            {
                rows.Add(newFolder);
            }

            rows.AddRange(productsIn[container].OrderBy(l => l.Product.Name, StringComparer.CurrentCulture).ThenBy(l => l.Product.Id));
            if (PendingProduct is { } newProduct && ReferenceEquals(FolderOf(newProduct.Product.Stage, newProduct.Product.FolderId), container))
            {
                rows.Add(newProduct);
            }

            wanted[container] = rows;
        }

        // Everything leaving a folder goes before anything arrives, so a product or folder moving from one to another, or
        // to and from Deleted, is never in two at once.
        var deletedRows = _leaves.RowsOf(deleted).ToList();
        TreeRows.RemoveMissing(DeletedFolder.Children, deletedRows);
        foreach (var (container, rows) in wanted)
        {
            TreeRows.RemoveMissing(container.Children, rows);
        }

        // Deleted holds its products straight, whatever their stage and folder. It opens on a filter's matches, as the
        // stages' folders do, and the tree is told again when it gets its first rows open (ITreeFolder.AnnounceExpanded).
        var deletedWasEmpty = DeletedFolder.Children.Count == 0;
        TreeRows.Arrange(DeletedFolder.Children, deletedRows);
        DeletedFolder.ShowCount(deletedRows.Count);
        if (isFiltered && deletedRows.Count > 0)
        {
            DeletedFolder.IsExpanded = true;
        }

        if (deletedWasEmpty && deletedRows.Count > 0 && DeletedFolder.IsExpanded)
        {
            DeletedFolder.AnnounceExpanded();
        }

        // Each stage, from the top down. An open folder just shown, or just given its first rows, is announced once
        // everything is in place, each before the folders in it: the screen showing at startup binds its tree before the
        // first load has filled it, and the tree holds an empty folder closed however it's set.
        List<ProductFolderNode> toAnnounce = [];
        foreach (var stage in Stages)
        {
            Lay(stage, isAdded: false);
        }

        foreach (var folder in toAnnounce)
        {
            folder.AnnounceExpanded();
        }

        _isFilled |= folders.Count > 0 || products.Count > 0 || deleted.Count > 0;

        int Count(ProductFolderNode container)
        {
            var count = productsIn[container].Count() + SubfoldersOf(container).Sum(Count);
            counts[container] = count;
            return count;
        }

        void Lay(ProductFolderNode container, bool isAdded)
        {
            var wasEmpty = container.Children.Count == 0;
            var added = TreeRows.Arrange(container.Children, wanted[container]).ToHashSet();
            container.ShowCount(counts[container]);
            if (isFiltered)
            {
                container.IsExpanded = true;
            }

            if (container.IsExpanded && ((wasEmpty && container.Children.Count > 0) || isAdded))
            {
                toAnnounce.Add(container);
            }

            foreach (var folder in container.Folders.Where(f => !f.IsNewFolder))
            {
                Lay(folder, added.Contains(folder));
            }
        }
    }

    /// <summary>The user's folders whose rows go in <paramref name="container"/>, shown or not.</summary>
    private IEnumerable<ProductFolderNode> SubfoldersOf(ProductFolderNode container) =>
        _folders.Values.Where(f => ReferenceEquals(f.ParentFolder, container));

    /// <summary>
    /// Gives each of the user's folders its row, made once and kept, renamed, re-staged and put in its parent as it is now;
    /// a folder gone loses its row. A folder whose parent the list hasn't been given, or one in a loop of parents left by an
    /// edit outside the app, shows at the top of its stage.
    /// </summary>
    private void MergeFolders(IReadOnlyList<ProductFolder> folders)
    {
        var byId = folders.ToDictionary(f => f.Id);
        foreach (var gone in _folders.Keys.Where(id => !byId.ContainsKey(id)).ToList())
        {
            var node = _folders[gone];
            _folders.Remove(gone);
            node.ParentFolder?.Children.Remove(node);
            node.Children.Clear();
            node.ParentFolder = null;
        }

        foreach (var folder in folders)
        {
            if (_folders.TryGetValue(folder.Id, out var node))
            {
                node.Rename(folder.Name);
            }
            else
            {
                _folders[folder.Id] = _memory.Track(ProductFolderNode.ForFolder(folder));
            }
        }

        foreach (var folder in folders)
        {
            var node = _folders[folder.Id];
            var parent = folder.ParentId is { } parentId && byId.ContainsKey(parentId) && !ProductFolderRules.IsInLoop(byId, folder.Id)
                ? _folders[parentId]
                : null;

            // A folder is in its parent's stage; one at the top in its own.
            node.Stage = parent is null ? folder.Stage : TopOf(folder.Id).Stage;
            node.ParentFolder = parent ?? StageFolder(node.Stage);
        }

        ProductFolder TopOf(int id)
        {
            var at = byId[id];
            while (at.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
            {
                at = parent;
            }

            return at;
        }
    }
}
