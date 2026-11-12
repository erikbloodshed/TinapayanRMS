using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// The Products list's folders (ProductFolder): their names, where each sits in the tree of folders, and the order the
/// list shows and prints products in. Every walk up or down the tree stops where a folder comes round again, so a loop
/// left by an edit outside the app can't hang it.
/// </summary>
public static class ProductFolderRules
{
    public const int MaxNameLength = 100;

    /// <summary>What separates the folders of a path: "Production › Breads › Holiday".</summary>
    public const string PathSeparator = " › ";

    /// <summary>
    /// A folder's name as it's saved: without spaces at either end, refused when there's nothing else or it's too long.
    /// PostgreSQL's collation counts a space at the end, so "Breads " would get past the unique index beside "Breads".
    /// </summary>
    public static string StoredName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new BusinessRuleException("Every folder needs a name.");
        }

        return trimmed.Length <= MaxNameLength
            ? trimmed
            : throw new BusinessRuleException($"A folder's name can be at most {MaxNameLength} characters.");
    }

    /// <summary>
    /// The ids of <paramref name="folderId"/> and of every folder inside it, however deep: what moves, prints or is
    /// counted with it.
    /// </summary>
    public static IReadOnlySet<int> SubtreeIds(IEnumerable<ProductFolder> folders, int folderId)
    {
        var children = folders.Where(f => f.ParentId is not null).ToLookup(f => f.ParentId!.Value, f => f.Id);
        HashSet<int> ids = [folderId];
        var pending = new Stack<int>([folderId]);
        while (pending.TryPop(out var id))
        {
            foreach (var child in children[id])
            {
                if (ids.Add(child))
                {
                    pending.Push(child);
                }
            }
        }

        return ids;
    }

    /// <summary>Whether <paramref name="folderId"/> is <paramref name="ancestorId"/> or inside it, however deep.</summary>
    public static bool IsInside(IEnumerable<ProductFolder> folders, int folderId, int ancestorId)
    {
        var byId = folders.ToDictionary(f => f.Id);
        HashSet<int> seen = [];
        for (int? id = folderId; id is { } current && seen.Add(current); id = byId.GetValueOrDefault(current)?.ParentId)
        {
            if (current == ancestorId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The names of the folders from the top of the stage down to <paramref name="folderId"/>, itself last: none for null,
    /// or for a folder that isn't there.
    /// </summary>
    public static IReadOnlyList<string> PathOf(IEnumerable<ProductFolder> folders, int? folderId)
    {
        var byId = folders.ToDictionary(f => f.Id);
        List<string> names = [];
        HashSet<int> seen = [];
        for (var folder = folderId is { } id ? byId.GetValueOrDefault(id) : null;
             folder is not null && seen.Add(folder.Id);
             folder = folder.ParentId is { } parentId ? byId.GetValueOrDefault(parentId) : null)
        {
            names.Add(folder.Name);
        }

        names.Reverse();
        return names;
    }

    /// <summary>Where a folder, or the top of a stage, is, as the list's notices and printouts name it: "Production › Breads › Holiday".</summary>
    public static string PathTitle(ProductStage stage, IEnumerable<string> folderNames) =>
        string.Join(PathSeparator, folderNames.Prepend(ProductRules.StageTitle(stage)));

    /// <summary><see cref="PathTitle(ProductStage, IEnumerable{string})"/> for <paramref name="folderId"/> in <paramref name="stage"/>.</summary>
    public static string PathTitle(IEnumerable<ProductFolder> folders, ProductStage stage, int? folderId) =>
        PathTitle(stage, PathOf(folders, folderId));

    /// <summary>
    /// <paramref name="products"/> in the order the list shows them with every folder open: Production's, then
    /// Innovation's; in each folder, and at the top of each stage, its folders first, each with all it holds, then its own
    /// products; folders and products by name. A product whose folder isn't among <paramref name="folders"/> is at the top
    /// of its stage. The order a set of products prints in.
    /// </summary>
    public static IReadOnlyList<Product> InListOrder(IEnumerable<Product> products, IEnumerable<ProductFolder> folders)
    {
        var folderList = folders.ToList();
        var byId = folderList.ToDictionary(f => f.Id);
        var productsIn = products.ToLookup(p => p.FolderId is { } id && byId.ContainsKey(id) ? (int?)id : null);
        var subfolders = folderList
            .Where(f => f.ParentId is { } parentId && byId.ContainsKey(parentId))
            .ToLookup(f => f.ParentId!.Value);
        var topFolders = folderList.Where(f => f.ParentId is not { } parentId || !byId.ContainsKey(parentId)).ToList();

        List<Product> ordered = [];
        HashSet<int> seen = [];
        foreach (var stage in (ProductStage[])[ProductStage.Production, ProductStage.Innovation])
        {
            foreach (var folder in ByName(topFolders.Where(f => f.Stage == stage)))
            {
                Add(folder);
            }

            ordered.AddRange(ByProductName(productsIn[null].Where(p => p.Stage == stage)));
        }

        return ordered;

        void Add(ProductFolder folder)
        {
            if (!seen.Add(folder.Id))
            {
                return;
            }

            foreach (var child in ByName(subfolders[folder.Id]))
            {
                Add(child);
            }

            ordered.AddRange(ByProductName(productsIn[folder.Id]));
        }

        static IEnumerable<ProductFolder> ByName(IEnumerable<ProductFolder> list) =>
            list.OrderBy(f => f.Name, StringComparer.CurrentCulture).ThenBy(f => f.Id);

        static IEnumerable<Product> ByProductName(IEnumerable<Product> list) =>
            list.OrderBy(p => p.Name, StringComparer.CurrentCulture).ThenBy(p => p.Id);
    }

    /// <summary>A folder can't go inside itself, or inside a folder of its own.</summary>
    public static void EnsureNotInsideItself(IEnumerable<ProductFolder> folders, int folderId, int? newParentId)
    {
        if (newParentId is { } parentId && IsInside(folders, parentId, folderId))
        {
            throw new BusinessRuleException("A folder can't go inside itself.");
        }
    }

    /// <summary>The refusal of a name another folder beside it has: "Production › Breads already has a folder named Holiday."</summary>
    public static BusinessRuleException NameTaken(string parentPath, string name) =>
        new($"{parentPath} already has a folder named {name}.");

    public const string MissingFolderMessage = "The folder to move it to no longer exists.";
}
