using System.Collections.ObjectModel;
using System.Globalization;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Configurations;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>A row of the Products list's tree (ProductTree): a folder, or a product in one.</summary>
public closed class ProductTreeNode : ReactiveObject
{
    public abstract string Title { get; }

    /// <summary>The icon before the title, or null for none.</summary>
    public abstract AppIcon? Icon { get; }

    public abstract bool IsFolder { get; }

    /// <summary>One of the two top-level folders, Production and Innovation, which hold the category folders.</summary>
    public abstract bool IsStage { get; }

    /// <summary>A row at the top of the list, shown in bold: a stage's folder, or the Deleted folder.</summary>
    public virtual bool IsTopLevel => IsStage;

    /// <summary>How many products a folder holds, shown after its title; always 0 for a product, which shows none.</summary>
    public virtual int ProductCount => 0;

    private bool _isBelowTargetMarkup;
    /// <summary>
    /// A product whose current price earns less than its target markup (ProductCostSummary.IsBelowTargetMarkup): a flag after
    /// its name. Worked out in the background after each load; never set on a folder.
    /// </summary>
    public bool IsBelowTargetMarkup
    {
        get => _isBelowTargetMarkup;
        set => this.RaiseAndSetIfChanged(ref _isBelowTargetMarkup, value);
    }
}

/// <summary>
/// A folder of the Products list: a stage's (Production, Innovation), or a product category's inside
/// one, or the stage's Uncategorized; or the Deleted folder after the stages, holding the deleted products. The tree finds its rows in <see cref="Children"/> through a HierarchyPropertyDescriptor
/// for this type alone (ProductListCard). A product has no children at all: the tree gives an expander to every row whose
/// children are an observable collection, even an empty one.
/// </summary>
public sealed class ProductFolderNode : ProductTreeNode, ITreeFolder<ProductTreeNode>
{
    private ProductFolderNode(ProductStage stage, int? categoryId, bool isStage, string title, bool isDeletedFolder = false, bool isNewFolder = false)
    {
        Stage = stage;
        CategoryId = categoryId;
        IsStage = isStage;
        IsDeletedFolder = isDeletedFolder;
        IsNewFolder = isNewFolder;
        _title = title;
        Children.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(Icon));
    }

    /// <summary>
    /// The row New Folder adds to a stage while its name is typed into it (ProductTree.BeginNewFolder): no category yet,
    /// until the name typed saves one, and none at all if it's cancelled.
    /// </summary>
    public static ProductFolderNode NewFolder(ProductStage stage) => new(stage, null, isStage: false, NewFolderTitle, isNewFolder: true);

    public const string NewFolderTitle = "New folder";

    /// <summary>The row New Folder adds, named in the list before it's a category: nothing is dropped on it, and it has no menu.</summary>
    public bool IsNewFolder { get; }

    /// <summary>A stage's folder.</summary>
    public static ProductFolderNode ForStage(ProductStage stage) => new(stage, null, isStage: true, ProductRules.StageTitle(stage));

    public static ProductFolderNode ForCategory(ProductStage stage, ProductCategory category) => new(stage, category.Id, isStage: false, category.Name);

    public static ProductFolderNode Uncategorized(ProductStage stage) => new(stage, null, isStage: false, ProductRules.UncategorizedTitle);

    /// <summary>
    /// The Deleted folder, holding the deleted products by name, whatever their stage and category, until each is restored
    /// or deleted permanently. Closed the first time the list shows, as every other folder is open. Its Stage means nothing.
    /// </summary>
    public static ProductFolderNode Deleted() => new(default, null, isStage: false, DeletedTitle, isDeletedFolder: true);

    public const string DeletedTitle = "Deleted";

    public ProductStage Stage { get; }

    /// <summary>The category a category folder holds, or null for a stage's folder and for Uncategorized.</summary>
    public int? CategoryId { get; }

    public override bool IsStage { get; }

    /// <summary>The Deleted folder, after the stages: its products are dragged nowhere and nothing is dropped on it.</summary>
    public bool IsDeletedFolder { get; }

    public override bool IsTopLevel => IsStage || IsDeletedFolder;

    /// <summary>A category's folder, which can be renamed or deleted (the category with it), as a stage's and Uncategorized can't.</summary>
    public bool IsCategory => CategoryId is not null;

    /// <summary>
    /// What names the folder from one run to the next, for remembering whether it was left closed: "Production",
    /// "Production/Uncategorized", "Production/12" for category 12, and the same under "Innovation"; "Deleted"
    /// for the Deleted folder.
    /// </summary>
    public string Key =>
        IsDeletedFolder ? DeletedTitle
        : IsStage ? Stage.ToString()
        : IsNewFolder ? $"{Stage}/New"
        : $"{Stage}/{CategoryId?.ToString(CultureInfo.InvariantCulture) ?? ProductRules.UncategorizedTitle}";

    /// <summary>
    /// A folder's <see cref="Key"/> as kept before Innovation was renamed on 2026-11-09 ("ResearchAndDevelopment",
    /// "ResearchAndDevelopment/12"), as its key now ("Innovation", "Innovation/12"); any other key as it is. Read as kept,
    /// Innovation's folders left closed opened again (FolderMemory.Read).
    /// </summary>
    public static string CurrentFolderKey(string key)
    {
        const string former = ProductStageConverter.FormerInnovationName;
        return key == former ? nameof(ProductStage.Innovation)
            : key.StartsWith(former + "/", StringComparison.Ordinal) ? nameof(ProductStage.Innovation) + key[former.Length..]
            : key;
    }

    /// <summary>The products the folder shows: none for a stage's folder, whose rows are folders.</summary>
    public IEnumerable<ProductLeafNode> Products => Children.OfType<ProductLeafNode>();

    private int _productCount;
    /// <summary>The products it shows (a stage's: in all its folders), or while a search filters the list, the matches.</summary>
    public override int ProductCount => _productCount;

    internal void ShowCount(int count)
    {
        if (_productCount != count)
        {
            _productCount = count;
            this.RaisePropertyChanged(nameof(ProductCount));
        }
    }

    private string _title;
    public override string Title => _title;

    /// <summary>A category renamed in Settings keeps its folder, under its new name.</summary>
    internal void Rename(string title)
    {
        if (_title != title)
        {
            _title = title;
            this.RaisePropertyChanged(nameof(Title));
        }
    }

    /// <summary>
    /// FolderOpen while the folder is open on its rows, and Folder otherwise, empty ones included; the Deleted folder's is
    /// always Delete, the bin.
    /// </summary>
    public override AppIcon? Icon => IsDeletedFolder ? AppIcon.Delete : IsExpanded && Children.Count > 0 ? AppIcon.FolderOpen : AppIcon.Folder;

    public override bool IsFolder => true;

    public ObservableCollection<ProductTreeNode> Children { get; } = [];

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
            this.RaisePropertyChanged(nameof(Icon));
        }
    }

    /// <summary>Tells the tree again whether the folder is open (ITreeFolder.AnnounceExpanded says when, and why).</summary>
    internal void AnnounceExpanded() => this.RaisePropertyChanged(nameof(IsExpanded));

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
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(Title));
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

/// <summary>
/// The Products list as a tree: a folder for each stage, Production then Innovation, holding a folder
/// for every product category (by name) and then Uncategorized, each holding its products (by name); then the Deleted
/// folder, holding the deleted products (by name). Refresh merges rather than rebuilds: every folder and product keeps its
/// node, which the tree's selection and open folders are tied to.
/// </summary>
public sealed class ProductTree : IDisposable
{
    private readonly TreeLeaves<Product, ProductLeafNode> _leaves = new(p => p.Id, p => new ProductLeafNode(p), (leaf, p) => leaf.Product = p);

    /// <summary>Every folder each stage has, shown or not: one per category, and its Uncategorized.</summary>
    private readonly Dictionary<ProductFolderNode, StageFolders> _foldersByRoot = [];

    /// <summary>Which folders start open, and every one opened or closed since.</summary>
    private readonly FolderMemory _memory;

    /// <summary>
    /// A Refresh with something in it has made the category folders, so <see cref="ClosedFolderKeys"/> reads the folders. The
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
        foreach (var root in Stages)
        {
            _foldersByRoot[root] = new StageFolders(_memory.Track(ProductFolderNode.Uncategorized(root.Stage)));
        }

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
    public IObservable<RxVoid> ExpansionChanged => _memory.ExpansionChanged;

    /// <summary>The product's row, or null while it isn't listed (a search hides it, or it isn't loaded).</summary>
    public ProductLeafNode? Find(int productId) => _leaves.Find(productId);

    /// <summary>The stage's folder.</summary>
    public ProductFolderNode StageFolder(ProductStage stage) => Stages.Single(r => r.Stage == stage);

    /// <summary>The category's folder in <paramref name="stage"/>, shown or not; null for one it has no folder for yet.</summary>
    public ProductFolderNode? CategoryFolder(ProductStage stage, int categoryId) =>
        _foldersByRoot[StageFolder(stage)].ByCategory.GetValueOrDefault(categoryId);

    /// <summary>
    /// Every folder closed now, stages and Deleted included, by <see cref="ProductFolderNode.Key"/>: what to start with
    /// next time. Before the first Refresh, which makes the category folders, the ones it was given.
    /// </summary>
    public IReadOnlyList<string> ClosedFolderKeys() =>
        _memory.ClosedKeys(Roots.Concat(_foldersByRoot.Values.SelectMany(f => f.All)), _isFilled);

    /// <summary>
    /// Opens every folder in <paramref name="stage"/>, and the stage itself; in both stages, and the Deleted folder, when
    /// it's null.
    /// </summary>
    public void ExpandAll(ProductFolderNode? stage)
    {
        foreach (var root in StagesOf(stage))
        {
            root.IsExpanded = true;
            foreach (var folder in _foldersByRoot[root].All)
            {
                folder.IsExpanded = true;
            }
        }

        if (stage is null)
        {
            DeletedFolder.IsExpanded = true;
        }
    }

    /// <summary>
    /// Closes every folder in <paramref name="stage"/>, or in both stages, and the Deleted folder, when it's null. The
    /// stages stay open, so their category folders show, closed.
    /// </summary>
    public void CollapseAll(ProductFolderNode? stage)
    {
        foreach (var root in StagesOf(stage))
        {
            foreach (var folder in _foldersByRoot[root].All)
            {
                folder.IsExpanded = false;
            }
        }

        if (stage is null)
        {
            DeletedFolder.IsExpanded = false;
        }
    }

    private IEnumerable<ProductFolderNode> StagesOf(ProductFolderNode? stage) => stage is null ? Stages : [StageFolder(stage.Stage)];

    /// <summary>The row New Folder added, while its name is being typed; null otherwise.</summary>
    public ProductFolderNode? PendingFolder { get; private set; }

    /// <summary>The row New Product added, while its name is being typed and saved; null otherwise.</summary>
    public ProductLeafNode? PendingProduct { get; private set; }

    /// <summary>
    /// Adds a "New folder" row to <paramref name="stage"/>, after its categories and before Uncategorized, for its name to
    /// be typed into; the stage opens to show it. A refresh keeps it there until <see cref="EndNewFolder"/>. Any earlier
    /// one goes first: there's one at a time.
    /// </summary>
    public ProductFolderNode BeginNewFolder(ProductStage stage)
    {
        EndNewFolder();
        var folder = PendingFolder = ProductFolderNode.NewFolder(stage);
        var root = StageFolder(stage);
        var at = root.Children.IndexOf(_foldersByRoot[root].Uncategorized);
        root.Children.Insert(at < 0 ? root.Children.Count : at, folder);
        root.IsExpanded = true;
        return folder;
    }

    /// <summary>Takes away the row <see cref="BeginNewFolder"/> added, if there is one.</summary>
    public void EndNewFolder()
    {
        if (PendingFolder is { } folder)
        {
            PendingFolder = null;
            StageFolder(folder.Stage).Children.Remove(folder);
        }
    }

    /// <summary>
    /// Adds a "New product" row to the folder of <paramref name="stage"/> and <paramref name="categoryId"/> (Uncategorized
    /// for none, or one with no folder), last, for its name to be typed into; its folders open to show it. A refresh keeps
    /// it there, uncounted, until <see cref="EndNewProduct"/>. Any earlier one goes first: there's one at a time.
    /// </summary>
    public ProductLeafNode BeginNewProduct(ProductStage stage, int? categoryId, bool isComposite)
    {
        EndNewProduct();
        var root = StageFolder(stage);
        var folder = FolderOf(root, categoryId);
        var row = PendingProduct = new ProductLeafNode(new Product
        {
            Name = isComposite ? NewCompositeProductTitle : NewProductTitle,
            Stage = stage,
            CategoryId = folder.CategoryId,
            IsComposite = isComposite,
        });
        folder.Children.Add(row);
        root.IsExpanded = true;
        folder.IsExpanded = true;
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
            foreach (var folder in _foldersByRoot.Values.SelectMany(f => f.All))
            {
                folder.Children.Remove(row);
            }
        }
    }

    /// <summary>The folder in <paramref name="root"/> for <paramref name="categoryId"/>: its category's, or Uncategorized for none, or one with no folder.</summary>
    private ProductFolderNode FolderOf(ProductFolderNode root, int? categoryId)
    {
        var folders = _foldersByRoot[root];
        return categoryId is { } id && folders.ByCategory.TryGetValue(id, out var folder) ? folder : folders.Uncategorized;
    }

    public void Dispose() => _memory.Dispose();

    /// <summary>
    /// Where dropping <paramref name="product"/> on <paramref name="target"/> files it: a category folder or Uncategorized
    /// sets its stage and category, a stage's folder its stage alone, and another product the folder that one is in.
    /// Null when that's where it is already, or there's nothing to drop on. A deleted product goes nowhere, and nothing goes
    /// into the Deleted folder by a move: Delete and Restore do that.
    /// </summary>
    public static ProductMove? MoveTo(ProductTreeNode? target, Product product)
    {
        (ProductStage Stage, int? CategoryId)? place = product.IsDeleted || product.Id == 0 ? null : target switch
        {
            ProductFolderNode { IsDeletedFolder: true } or ProductLeafNode { IsDeleted: true } => null,
            ProductFolderNode { IsNewFolder: true } or ProductLeafNode { IsNew: true } => null,
            ProductFolderNode { IsStage: true } stage => (stage.Stage, product.CategoryId),
            ProductFolderNode folder => (folder.Stage, folder.CategoryId),
            ProductLeafNode leaf => (leaf.Product.Stage, leaf.Product.CategoryId),
            null => null,
        };

        return place is { } to && (to.Stage != product.Stage || to.CategoryId != product.CategoryId)
            ? new ProductMove(product.Id, to.Stage, to.CategoryId)
            : null;
    }

    /// <summary>
    /// What dropping several products on <paramref name="target"/> does: <see cref="MoveTo(ProductTreeNode?, Product)"/>
    /// for each, leaving out any that are there already. Empty when none of them would move.
    /// </summary>
    public static IReadOnlyList<ProductMove> MovesTo(ProductTreeNode? target, IEnumerable<Product> products) =>
        [.. products.Select(p => MoveTo(target, p)).OfType<ProductMove>()];

    /// <summary>
    /// Filing each of <paramref name="products"/> under <paramref name="stage"/> and, unless <paramref name="keepCategory"/>,
    /// <paramref name="categoryId"/>, leaving out any that are there already: the list's Move To and Promote.
    /// </summary>
    public static IReadOnlyList<ProductMove> MovesTo(ProductStage stage, int? categoryId, bool keepCategory, IEnumerable<Product> products) =>
        [.. from product in products
            let toCategory = keepCategory ? product.CategoryId : categoryId
            where product.Stage != stage || product.CategoryId != toCategory
            select new ProductMove(product.Id, stage, toCategory)];

    /// <summary>
    /// <paramref name="products"/> in the order the list shows them, folders opened: Production's, then
    /// Innovation's; in each, a category's at a time by the category's name, then the uncategorized; in each folder,
    /// by name. The order a set of products prints in.
    /// </summary>
    public static IReadOnlyList<Product> InListOrder(IEnumerable<Product> products) =>
    [
        .. products
            .OrderBy(p => p.Stage == ProductStage.Production ? 0 : 1)
            .ThenBy(p => p.CategoryId is null ? 1 : 0)
            .ThenBy(p => p.Category?.Name ?? string.Empty, StringComparer.CurrentCulture)
            .ThenBy(p => p.CategoryId)
            .ThenBy(p => p.Name, StringComparer.CurrentCulture),
    ];

    /// <summary>
    /// Brings the tree in line with <paramref name="products"/> (in the order the list shows them),
    /// <paramref name="categories"/> and the <paramref name="deleted"/> products (in that order too; none when it's null).
    /// Every category has a folder in each stage, so an empty one can still be dropped on; while a search filters the list
    /// (<paramref name="isFiltered"/>), only the folders holding a match show, open, and the Deleted folder opens on its own.
    /// </summary>
    public void Refresh(IReadOnlyList<ProductCategory> categories, IReadOnlyList<Product> products, bool isFiltered, IReadOnlyList<Product>? deleted = null)
    {
        deleted ??= [];
        // A product's category is read with it, so one added since the categories were loaded still gets its folder.
        var allCategories = categories
            .Concat(products.Select(p => p.Category).OfType<ProductCategory>())
            .DistinctBy(c => c.Id)
            .OrderBy(c => c.Name, StringComparer.CurrentCulture)
            .ToList();
        var categoryIds = allCategories.Select(c => c.Id).ToHashSet();

        _leaves.Merge(products.Concat(deleted));

        // Each stage's folders as they should show, and each folder's products.
        var layout = Stages.ToDictionary(root => root, root =>
        {
            var folders = _foldersByRoot[root];
            var inStage = products.Where(p => p.Stage == root.Stage).ToList();
            var wanted = new List<(ProductFolderNode Folder, List<ProductTreeNode> Rows)>();
            foreach (var category in allCategories)
            {
                if (folders.ByCategory.TryGetValue(category.Id, out var folder))
                {
                    folder.Rename(category.Name);
                }
                else
                {
                    folder = folders.ByCategory[category.Id] = _memory.Track(ProductFolderNode.ForCategory(root.Stage, category));
                }

                wanted.Add((folder, RowsOf(inStage.Where(p => p.CategoryId == category.Id))));
            }

            wanted.Add((folders.Uncategorized, RowsOf(inStage.Where(p => p.CategoryId is not { } id || !categoryIds.Contains(id)))));

            // A deleted category's folder goes for good.
            foreach (var id in folders.ByCategory.Keys.Where(id => !categoryIds.Contains(id)).ToList())
            {
                folders.ByCategory.Remove(id);
            }

            // The rows being named stay where they were added: a new folder before Uncategorized, a new product last in
            // its folder.
            if (PendingFolder is { } newFolder && newFolder.Stage == root.Stage)
            {
                wanted.Insert(wanted.Count - 1, (newFolder, []));
            }

            if (PendingProduct is { } newProduct && newProduct.Product.Stage == root.Stage)
            {
                var into = FolderOf(root, newProduct.Product.CategoryId);
                wanted.Single(w => w.Folder == into).Rows.Add(newProduct);
            }

            return wanted;
        });

        // Everything leaving the tree goes before anything arrives, so a product moving from one folder to another, or to
        // and from Deleted, is never in both at once.
        var deletedRows = RowsOf(deleted);
        TreeRows.RemoveMissing(DeletedFolder.Children, deletedRows);
        foreach (var (root, wanted) in layout)
        {
            TreeRows.RemoveLeaving(root.Children, wanted, isFiltered);
        }

        // Deleted holds its products straight, whatever their stage and category. It opens on a search's matches, as the
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

        // The screen's load makes every folder empty and fills it after, since the categories come in before the products.
        foreach (var (root, wanted) in layout)
        {
            TreeRows.LayTopFolder(root, wanted, isFiltered);
        }

        // A new product isn't one yet: its folder and stage count only the saved ones.
        if (PendingProduct is { } pending
            && Stages.Single(r => r.Stage == pending.Product.Stage) is var pendingRoot
            && FolderOf(pendingRoot, pending.Product.CategoryId) is var pendingFolder
            && pendingFolder.Children.Contains(pending))
        {
            pendingFolder.ShowCount(pendingFolder.ProductCount - 1);
            pendingRoot.ShowCount(pendingRoot.ProductCount - 1);
        }

        _isFilled |= allCategories.Count > 0 || products.Count > 0 || deleted.Count > 0;

        List<ProductTreeNode> RowsOf(IEnumerable<Product> inFolder) => [.. _leaves.RowsOf(inFolder)];
    }

    private sealed class StageFolders(ProductFolderNode uncategorized)
    {
        public Dictionary<int, ProductFolderNode> ByCategory { get; } = [];

        public ProductFolderNode Uncategorized { get; } = uncategorized;

        /// <summary>Every folder in the stage: each category's, then Uncategorized.</summary>
        public IEnumerable<ProductFolderNode> All => ByCategory.Values.Append(Uncategorized);
    }
}
