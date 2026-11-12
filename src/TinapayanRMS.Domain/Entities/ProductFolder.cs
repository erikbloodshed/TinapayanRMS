using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// A folder of the Products list, made and named by the user, as in a file explorer: it holds products (Product.FolderId)
/// and other folders (ParentId), as deep as wanted. It isn't a category: a product's category is its own, and the list's
/// category filter finds a category's products wherever they're filed. A folder is in one stage, as is everything in it
/// (ProductFolderRules), and moving it to the other stage takes the folders and products in it along.
/// </summary>
public sealed class ProductFolder
{
    public int Id { get; set; }

    /// <summary>Unique among the folders beside it (the same stage and parent), whatever its case.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The stage it's in: its parent's, and every product's in it.</summary>
    public ProductStage Stage { get; set; }

    /// <summary>The folder it's in, or null for one at the top of its stage.</summary>
    public int? ParentId { get; set; }
}
