using System.Collections.ObjectModel;
using System.Globalization;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>A row of the Raw Materials list's tree (RawMaterialTree): a folder, or a material in one.</summary>
public closed class RawMaterialTreeNode : ReactiveObject
{
    public abstract string Title { get; }

    /// <summary>The Segoe Fluent Icons glyph before the title, or empty for none.</summary>
    public abstract string Glyph { get; }

    public abstract bool IsFolder { get; }

    /// <summary>One of the two top-level folders, Ingredients and Packaging, which hold the category folders.</summary>
    public abstract bool IsTypeFolder { get; }

    /// <summary>How many materials a folder holds, shown after its title; always 0 for a material, which shows none.</summary>
    public virtual int MaterialCount => 0;
}

/// <summary>
/// A folder of the Raw Materials list: a type's (Ingredients, Packaging), or a category's inside the type it's for, a Made
/// category's among the ingredient ones. There's no Uncategorized, since every material has a category. The tree finds
/// its rows in <see cref="Children"/> through a HierarchyPropertyDescriptor for this type alone (MaterialListCard), as the
/// Products list does (ProductFolderNode).

/// </summary>
public sealed class RawMaterialFolderNode : RawMaterialTreeNode, ITreeFolder<RawMaterialTreeNode>
{
    private RawMaterialFolderNode(RawMaterialType type, int? categoryId, RawMaterialCategoryType? categoryType, string title)
    {
        Type = type;
        CategoryId = categoryId;
        CategoryType = categoryType;
        _title = title;
        Children.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(Glyph));
    }

    /// <summary>A type's folder.</summary>
    public static RawMaterialFolderNode ForType(RawMaterialType type) => new(type, null, null, TypeTitle(type));

    public static RawMaterialFolderNode ForCategory(RawMaterialCategory category) =>
        new(RawMaterialRules.MaterialTypeOf(category.Type), category.Id, category.Type, category.Name);

    /// <summary>What a type's folder reads: "Ingredients" or "Packaging".</summary>
    public static string TypeTitle(RawMaterialType type) => type == RawMaterialType.Ingredient ? "Ingredients" : "Packaging";

    /// <summary>The type of material the folder holds: a Made category's is Ingredient, since it's in the Ingredients folder.</summary>
    public RawMaterialType Type { get; }

    /// <summary>The category a category folder holds, or null for a type's folder.</summary>
    public int? CategoryId { get; }

    /// <summary>The type of the category a category folder holds, or null for a type's folder.</summary>
    public RawMaterialCategoryType? CategoryType { get; }

    public override bool IsTypeFolder => CategoryId is null;

    /// <summary>A category's folder, which can be renamed or deleted (the category with it), as a type's can't.</summary>
    public bool IsCategory => CategoryId is not null;

    /// <summary>
    /// What names the folder from one run to the next, for remembering whether it was left closed: "Ingredient",
    /// "Packaging", and "Ingredient/12" for category 12.
    /// </summary>
    public string Key => CategoryId is { } id ? $"{Type}/{id.ToString(CultureInfo.InvariantCulture)}" : Type.ToString();

    /// <summary>The materials the folder shows: none for a type's folder, whose rows are folders.</summary>
    public IEnumerable<RawMaterialLeafNode> Materials => Children.OfType<RawMaterialLeafNode>();

    private int _materialCount;
    /// <summary>The materials it shows (a type's: in all its folders), or while a search filters the list, the matches.</summary>
    public override int MaterialCount => _materialCount;

    internal void ShowCount(int count)
    {
        if (_materialCount != count)
        {
            _materialCount = count;
            this.RaisePropertyChanged(nameof(MaterialCount));
        }
    }

    private string _title;
    public override string Title => _title;

    /// <summary>A category renamed keeps its folder, under its new name.</summary>
    internal void Rename(string title)
    {
        if (_title != title)
        {
            _title = title;
            this.RaisePropertyChanged(nameof(Title));
        }
    }

    /// <summary>Segoe Fluent Icons' FolderOpen while the folder is open on its rows, and Folder otherwise, empty ones included.</summary>
    public override string Glyph => IsExpanded && Children.Count > 0 ? "" : "";

    public override bool IsFolder => true;

    public ObservableCollection<RawMaterialTreeNode> Children { get; } = [];

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

    /// <summary>Tells the tree again whether the folder is open (ITreeFolder.AnnounceExpanded says when, and why).</summary>
    internal void AnnounceExpanded() => this.RaisePropertyChanged(nameof(IsExpanded));

    void ITreeFolder<RawMaterialTreeNode>.ShowCount(int count) => ShowCount(count);

    void ITreeFolder<RawMaterialTreeNode>.AnnounceExpanded() => AnnounceExpanded();
}

/// <summary>
/// A material in its folder. There's one for each material, kept while the material is listed, so a fresh copy of it
/// (after a load, or a move) updates it rather than replacing it, and a material moved to another folder takes it along.
/// </summary>
public sealed class RawMaterialLeafNode(RawMaterial rawMaterial) : RawMaterialTreeNode
{
    private RawMaterial _rawMaterial = rawMaterial;
    public RawMaterial RawMaterial
    {
        get => _rawMaterial;
        internal set
        {
            if (!ReferenceEquals(_rawMaterial, value))
            {
                _rawMaterial = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(Title));
            }
        }
    }

    public override string Title => RawMaterial.Name;

    public override string Glyph => string.Empty;

    public override bool IsFolder => false;

    public override bool IsTypeFolder => false;
}

/// <summary>
/// The Raw Materials list as a tree: a folder for each type, Ingredients then Packaging, holding a folder for every
/// category of that type (by name), each holding its materials (by name). Refresh merges rather than rebuilds, as
/// ProductTree's does: every folder and material keeps its node, which the tree's selection and open folders are tied to.
/// </summary>
public sealed class RawMaterialTree : IDisposable
{
    private readonly TreeLeaves<RawMaterial, RawMaterialLeafNode> _leaves = new(m => m.Id, m => new RawMaterialLeafNode(m), (leaf, m) => leaf.RawMaterial = m);

    /// <summary>Every category's folder, shown or not, by category id: each is in its own type's folder.</summary>
    private readonly Dictionary<int, RawMaterialFolderNode> _categoryFolders = [];

    /// <summary>Which folders start open, and every one opened or closed since.</summary>
    private readonly FolderMemory _memory;

    /// <summary>
    /// A Refresh with something in it has made the category folders, so <see cref="ClosedFolderKeys"/> reads the folders. The
    /// screen refreshes its tree, empty, as it's made, which doesn't count: disposed before a load, it would keep the types alone.
    /// </summary>
    private bool _isFilled;

    /// <param name="memory">
    /// The folders left closed last time (<see cref="ClosedFolderKeys"/>, by <see cref="RawMaterialFolderNode.Key"/>); null
    /// for none kept, when every folder starts open.
    /// </param>
    public RawMaterialTree(FolderMemory? memory = null)
    {
        _memory = memory ?? new FolderMemory();
        Roots = [_memory.Track(RawMaterialFolderNode.ForType(RawMaterialType.Ingredient)), _memory.Track(RawMaterialFolderNode.ForType(RawMaterialType.Packaging))];
    }

    /// <summary>The two type folders, Ingredients first. They're always there.</summary>
    public IReadOnlyList<RawMaterialFolderNode> Roots { get; }

    /// <summary>A folder opened or closed, by a click, a search or Expand All: for remembering them (ClosedFolderKeys).</summary>
    public IObservable<RxVoid> ExpansionChanged => _memory.ExpansionChanged;

    /// <summary>The material's row, or null while it isn't listed (a search hides it, or it isn't loaded).</summary>
    public RawMaterialLeafNode? Find(int rawMaterialId) => _leaves.Find(rawMaterialId);

    /// <summary>The type's folder.</summary>
    public RawMaterialFolderNode TypeFolder(RawMaterialType type) => Roots.Single(r => r.Type == type);

    /// <summary>The category's folder, shown or not; null for one it has no folder for yet.</summary>
    public RawMaterialFolderNode? CategoryFolder(int categoryId) => _categoryFolders.GetValueOrDefault(categoryId);

    /// <summary>
    /// Every folder closed now, types included, by <see cref="RawMaterialFolderNode.Key"/>: what to start with next time.
    /// Before the first Refresh, which makes the category folders, the ones it was given.
    /// </summary>
    public IReadOnlyList<string> ClosedFolderKeys() => _memory.ClosedKeys(Roots.Concat(_categoryFolders.Values), _isFilled);

    /// <summary>Opens every folder in <paramref name="typeFolder"/>'s type, and the type's folder itself; in both types when it's null.</summary>
    public void ExpandAll(RawMaterialFolderNode? typeFolder)
    {
        foreach (var root in TypesOf(typeFolder))
        {
            root.IsExpanded = true;
            foreach (var folder in FoldersIn(root.Type))
            {
                folder.IsExpanded = true;
            }
        }
    }

    /// <summary>
    /// Closes every folder in <paramref name="typeFolder"/>'s type, or in both types when it's null. The types stay open,
    /// so their category folders show, closed.
    /// </summary>
    public void CollapseAll(RawMaterialFolderNode? typeFolder)
    {
        foreach (var root in TypesOf(typeFolder))
        {
            foreach (var folder in FoldersIn(root.Type))
            {
                folder.IsExpanded = false;
            }
        }
    }

    private IEnumerable<RawMaterialFolderNode> TypesOf(RawMaterialFolderNode? typeFolder) =>
        typeFolder is null ? Roots : [TypeFolder(typeFolder.Type)];

    private IEnumerable<RawMaterialFolderNode> FoldersIn(RawMaterialType type) => _categoryFolders.Values.Where(f => f.Type == type);

    public void Dispose() => _memory.Dispose();

    /// <summary>
    /// Where dropping <paramref name="material"/> on <paramref name="target"/> files it: a category folder of its own type
    /// (RawMaterialRules.CategoryTypeFor) puts it in that category, and a material in such a folder in the folder that one
    /// is in. Null when that's where it is already, for a type's folder (which has no category to give), for a category of
    /// another type (a material's type and source are fixed, so a made ingredient only ever goes in a Made one), or with
    /// nothing to drop on.
    /// </summary>
    public static RawMaterialMove? MoveTo(RawMaterialTreeNode? target, RawMaterial material)
    {
        (RawMaterialCategoryType Type, int CategoryId)? place = target switch
        {
            RawMaterialFolderNode { CategoryId: { } categoryId, CategoryType: { } categoryType } => (categoryType, categoryId),
            RawMaterialLeafNode leaf => (
                leaf.RawMaterial.Category?.Type ?? RawMaterialRules.CategoryTypeFor(leaf.RawMaterial.Type, leaf.RawMaterial.Source),
                leaf.RawMaterial.CategoryId),
            // A type's own folder holds categories, not materials; null is a drop on nothing.
            RawMaterialFolderNode or null => null,
        };

        return place is { } to
            && to.Type == RawMaterialRules.CategoryTypeFor(material.Type, material.Source)
            && to.CategoryId != material.CategoryId
            ? new RawMaterialMove(material.Id, to.CategoryId)
            : null;
    }

    /// <summary>
    /// What dropping several materials on <paramref name="target"/> does: <see cref="MoveTo(RawMaterialTreeNode?, RawMaterial)"/>
    /// for each, leaving out any that are there already. Empty when none of them would move.
    /// </summary>
    public static IReadOnlyList<RawMaterialMove> MovesTo(RawMaterialTreeNode? target, IEnumerable<RawMaterial> materials) =>
        [.. materials.Select(m => MoveTo(target, m)).OfType<RawMaterialMove>()];

    /// <summary>
    /// Filing each of <paramref name="materials"/> under <paramref name="category"/>, leaving out any there already and
    /// any that can't go in a category of its type: the list's Move To.
    /// </summary>
    public static IReadOnlyList<RawMaterialMove> MovesTo(RawMaterialCategory category, IEnumerable<RawMaterial> materials) =>
        [.. from material in materials
            where RawMaterialRules.CategoryTypeFor(material.Type, material.Source) == category.Type && material.CategoryId != category.Id
            select new RawMaterialMove(material.Id, category.Id)];

    /// <summary>
    /// Brings the tree in line with <paramref name="materials"/> (in the order the list shows them) and
    /// <paramref name="categories"/>. Every category has a folder in its type's, so an empty one can still be dropped on;
    /// while a search filters the list (<paramref name="isFiltered"/>), only the folders holding a match show, open.
    /// </summary>
    public void Refresh(IReadOnlyList<RawMaterialCategory> categories, IReadOnlyList<RawMaterial> materials, bool isFiltered)
    {
        // A material's category is read with it, so one added since the categories were loaded still gets its folder.
        var allCategories = categories
            .Concat(materials.Select(m => m.Category).OfType<RawMaterialCategory>())
            .DistinctBy(c => c.Id)
            .OrderBy(c => c.Name, StringComparer.CurrentCulture)
            .ToList();
        var categoryIds = allCategories.Select(c => c.Id).ToHashSet();

        _leaves.Merge(materials);

        foreach (var category in allCategories)
        {
            if (_categoryFolders.TryGetValue(category.Id, out var folder) && folder.CategoryType == category.Type)
            {
                folder.Rename(category.Name);
            }
            else
            {
                // New, or a category whose type was changed in Settings (only while everything in it fits the new one): its
                // folder is made again, in the other type's folder when it's packaging now or no longer is.
                _categoryFolders[category.Id] = _memory.Track(RawMaterialFolderNode.ForCategory(category));
            }
        }

        // A deleted category's folder goes for good.
        foreach (var id in _categoryFolders.Keys.Where(id => !categoryIds.Contains(id)).ToList())
        {
            _categoryFolders.Remove(id);
        }

        var byCategory = materials.ToLookup(m => m.CategoryId);

        // Each type's folders as they should show, and each folder's materials. A material whose category isn't among the
        // folders (it can't happen while the foreign key holds) isn't shown rather than put in the wrong one.
        var layout = Roots.ToDictionary(root => root, root =>
            allCategories
                .Where(c => RawMaterialRules.MaterialTypeOf(c.Type) == root.Type)
                .Select(c => (Folder: _categoryFolders[c.Id], Rows: RowsOf(byCategory[c.Id].Where(m => m.Type == root.Type))))
                .ToList());

        // Everything leaving the tree goes before anything arrives, so a material moving from one folder to another is
        // never in both at once.
        foreach (var (root, wanted) in layout)
        {
            TreeRows.RemoveLeaving(root.Children, wanted, isFiltered);
        }

        foreach (var (root, wanted) in layout)
        {
            TreeRows.LayTopFolder(root, wanted, isFiltered);
        }

        _isFilled |= allCategories.Count > 0 || materials.Count > 0;

        List<RawMaterialTreeNode> RowsOf(IEnumerable<RawMaterial> inFolder) => [.. _leaves.RowsOf(inFolder)];
    }
}
