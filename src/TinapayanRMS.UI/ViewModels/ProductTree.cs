using System.Collections.ObjectModel;
using System.Globalization;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>A row of the Products list's tree (ProductTree): a folder, or a product in one.</summary>
public closed class ProductTreeNode : ReactiveObject
{
    public abstract string Title { get; }

    /// <summary>The Segoe Fluent Icons glyph before the title, or empty for none.</summary>
    public abstract string Glyph { get; }

    public abstract bool IsFolder { get; }

    /// <summary>One of the two top-level folders, Production and Research and Development, which hold the category folders.</summary>
    public abstract bool IsStage { get; }

    /// <summary>A row at the top of the list, shown in bold: a stage's folder, or the Deleted folder.</summary>
    public virtual bool IsTopLevel => IsStage;

    /// <summary>How many products a folder holds, shown after its title; always 0 for a product, which shows none.</summary>
    public virtual int ProductCount => 0;

    /// <summary>Which image goes before a product's name (ProductListCard); always none for a folder.</summary>
    public virtual ProductIcon IconKind => ProductIcon.None;

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
/// The full-colour image the Products list puts before a product's name (Assets/Icons/*_64px.png): one each for Breads, Loaves, Cakes, Pastries and Rolls, and a bag of breads for a
/// composite whatever its category.
/// </summary>
public enum ProductIcon
{
    None,
    Breads,
    Loaves,
    Cakes,
    Pastries,
    Rolls,
    Composite,
}

/// <summary>
/// A folder of the Products list: a stage's (Production, Research and Development), or a product category's inside
/// one, or the stage's Uncategorized; or the Deleted folder after the stages, holding the deleted products. The tree finds its rows in <see cref="Children"/> through a HierarchyPropertyDescriptor
/// for this type alone (ProductListCard). A product has no children at all: the tree gives an expander to every row whose
/// children are an observable collection, even an empty one.
/// </summary>
public sealed class ProductFolderNode : ProductTreeNode, IListFolder
{
    private ProductFolderNode(ProductStage stage, int? categoryId, bool isStage, string title, bool isDeletedFolder = false)
    {
        Stage = stage;
        CategoryId = categoryId;
        IsStage = isStage;
        IsDeletedFolder = isDeletedFolder;
        _title = title;
        Children.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(Glyph));
    }

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
    /// "Production/Uncategorized", "Production/12" for category 12, and the same under "ResearchAndDevelopment"; "Deleted"
    /// for the Deleted folder.
    /// </summary>
    public string Key =>
        IsDeletedFolder ? DeletedTitle
        : IsStage ? Stage.ToString()
        : $"{Stage}/{CategoryId?.ToString(CultureInfo.InvariantCulture) ?? ProductRules.UncategorizedTitle}";

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
    /// Segoe Fluent Icons' FolderOpen while the folder is open on its rows, and Folder otherwise, empty ones included; the
    /// Deleted folder's is always Delete, the bin.
    /// </summary>
    public override string Glyph => IsDeletedFolder ? "" : IsExpanded && Children.Count > 0 ? "" : "";

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
            this.RaisePropertyChanged(nameof(Glyph));
        }
    }

    /// <summary>
    /// Tells the tree again whether the folder is open. The tree reads IsExpanded as it changes, but not for a folder
    /// shown again after a search hid it, nor for one that was empty when it opened (it can't open a folder with
    /// nothing in it) and has rows now: without this, an open folder would come back closed.
    /// </summary>
    internal void AnnounceExpanded() => this.RaisePropertyChanged(nameof(IsExpanded));
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
                this.RaisePropertyChanged(nameof(IconKind));
            }
        }
    }

    public override string Title => Product.Name;

    public override string Glyph => string.Empty;

    /// <summary>
    /// A composite's present, whatever its category, else its category's, matched by name whatever the case; none for
    /// any other category, or no category.
    /// </summary>
    public override ProductIcon IconKind =>
        Product.IsComposite
            ? ProductIcon.Composite
            : Product.Category?.Name.Trim().ToUpperInvariant() switch
            {
                "BREADS" => ProductIcon.Breads,
                "LOAVES" => ProductIcon.Loaves,
                "CAKES" => ProductIcon.Cakes,
                "PASTRIES" => ProductIcon.Pastries,
                "ROLLS" => ProductIcon.Rolls,
                _ => ProductIcon.None,
            };

    public override bool IsFolder => false;

    public override bool IsStage => false;

    /// <summary>It's in the Deleted folder: it can be restored or deleted permanently, and nothing else.</summary>
    public bool IsDeleted => Product.IsDeleted;
}

/// <summary>
/// The Products list as a tree: a folder for each stage, Production then Research and Development, holding a folder
/// for every product category (by name) and then Uncategorized, each holding its products (by name); then the Deleted
/// folder, holding the deleted products (by name). Refresh merges rather than rebuilds: every folder and product keeps its
/// node, which the tree's selection and open folders are tied to.
/// </summary>
public sealed class ProductTree : IDisposable
{
    private readonly Dictionary<int, ProductLeafNode> _leaves = [];

    /// <summary>Every folder each stage has, shown or not: one per category, and its Uncategorized.</summary>
    private readonly Dictionary<ProductFolderNode, StageFolders> _foldersByRoot = [];

    /// <summary>Which folders start open, and every one opened or closed since.</summary>
    private readonly FolderMemory _memory;

    /// <summary>The first Refresh has made the category folders, so <see cref="ClosedFolderKeys"/> reads the folders.</summary>
    private bool _isFilled;

    /// <param name="memory">
    /// The folders left closed last time (<see cref="ClosedFolderKeys"/>, by <see cref="ProductFolderNode.Key"/>); null for
    /// none kept, when every folder starts open but Deleted.
    /// </param>
    public ProductTree(FolderMemory? memory = null)
    {
        _memory = memory ?? new FolderMemory();
        Stages = [_memory.Track(ProductFolderNode.ForStage(ProductStage.Production)), _memory.Track(ProductFolderNode.ForStage(ProductStage.ResearchAndDevelopment))];
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
    public ProductLeafNode? Find(int productId) => _leaves.GetValueOrDefault(productId);

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

    public void Dispose() => _memory.Dispose();

    /// <summary>
    /// Where dropping <paramref name="product"/> on <paramref name="target"/> files it: a category folder or Uncategorized
    /// sets its stage and category, a stage's folder its stage alone, and another product the folder that one is in.
    /// Null when that's where it is already, or there's nothing to drop on. A deleted product goes nowhere, and nothing goes
    /// into the Deleted folder by a move: Delete and Restore do that.
    /// </summary>
    public static ProductMove? MoveTo(ProductTreeNode? target, Product product)
    {
        (ProductStage Stage, int? CategoryId)? place = product.IsDeleted ? null : target switch
        {
            ProductFolderNode { IsDeletedFolder: true } or ProductLeafNode { IsDeleted: true } => null,
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
    /// <paramref name="products"/> in the order the list shows them, folders opened: Production's, then Research and
    /// Development's; in each, a category's at a time by the category's name, then the uncategorized; in each folder,
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

        foreach (var product in products.Concat(deleted))
        {
            if (_leaves.TryGetValue(product.Id, out var leaf))
            {
                leaf.Product = product;
            }
            else
            {
                _leaves[product.Id] = new ProductLeafNode(product);
            }
        }

        foreach (var gone in _leaves.Keys.Except(products.Concat(deleted).Select(p => p.Id)).ToList())
        {
            _leaves.Remove(gone);
        }

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

            return wanted;
        });

        // The top-level folders with nothing in them yet: on the screen showing at startup, the tree binds before the first
        // load has filled them, and holds them closed however they're set (see below).
        var emptyRoots = Stages.Where(r => r.Children.Count == 0).ToHashSet();

        // Everything leaving the tree goes before anything arrives, so a product moving from one folder to another, or to
        // and from Deleted, is never in both at once.
        var deletedRows = RowsOf(deleted);
        TreeRows.RemoveMissing(DeletedFolder.Children, deletedRows);
        foreach (var (root, wanted) in layout)
        {
            TreeRows.RemoveMissing(root.Children, Shown(wanted));
            foreach (var (folder, rows) in wanted)
            {
                TreeRows.RemoveMissing(folder.Children, rows);
            }
        }

        // Deleted holds its products straight, whatever their stage and category. It opens on a search's matches, as the
        // stages' folders do, and the tree is told again when it gets its first rows open (see below).
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

        foreach (var (root, wanted) in layout)
        {
            var shown = Shown(wanted);
            var added = TreeRows.Arrange(root.Children, shown);
            List<ProductFolderNode> filled = [];
            foreach (var (folder, rows) in wanted)
            {
                if (folder.Children.Count == 0 && rows.Count > 0)
                {
                    filled.Add(folder);
                }

                TreeRows.Arrange(folder.Children, rows);
                folder.ShowCount(rows.Count);
            }

            root.ShowCount(wanted.Sum(f => f.Rows.Count));

            // Every match shows. The folders stay open once the search is cleared, as they would in a file explorer.
            if (isFiltered)
            {
                root.IsExpanded = true;
                foreach (var folder in shown.Cast<ProductFolderNode>())
                {
                    folder.IsExpanded = true;
                }
            }

            // The tree is told again about a folder shown again, and one that has just got its first rows: it can't open a
            // folder with nothing in it, and doesn't look at it again when rows arrive. The screen's load makes every
            // folder empty and fills it after, since the categories come in before the products.
            if (emptyRoots.Contains(root) && root.Children.Count > 0 && root.IsExpanded)
            {
                root.AnnounceExpanded();
            }

            foreach (var folder in added.Cast<ProductFolderNode>().Union(filled).Where(f => f.IsExpanded))
            {
                folder.AnnounceExpanded();
            }
        }

        _isFilled = true;

        List<ProductTreeNode> RowsOf(IEnumerable<Product> inFolder) => [.. inFolder.Select(p => _leaves[p.Id])];

        List<ProductTreeNode> Shown(List<(ProductFolderNode Folder, List<ProductTreeNode> Rows)> wanted) =>
            [.. wanted.Where(f => !isFiltered || f.Rows.Count > 0).Select(f => f.Folder)];
    }

    private sealed class StageFolders(ProductFolderNode uncategorized)
    {
        public Dictionary<int, ProductFolderNode> ByCategory { get; } = [];

        public ProductFolderNode Uncategorized { get; } = uncategorized;

        /// <summary>Every folder in the stage: each category's, then Uncategorized.</summary>
        public IEnumerable<ProductFolderNode> All => ByCategory.Values.Append(Uncategorized);
    }
}
