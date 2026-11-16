using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// What Promote to Production, Send Back to Innovation and a folder's Promote All ask for (ProductsViewModel.MoveToStageCommand):
/// the products, and the stage they go to.
/// </summary>
public sealed record StageMoveRequest(IReadOnlyList<Product> Products, ProductStage Stage);

/// <summary>What Undo on the Products list's notice bar puts back: products moved, or a folder.</summary>
internal closed record MoveUndo
{
    /// <summary>
    /// The products moved, each where it was before (<paramref name="Back"/>), and the folders a Promote or Send Back made
    /// for them (<paramref name="MadeFolderIds"/>), taken away again while they hold nothing.
    /// </summary>
    public sealed record Products(IReadOnlyList<ProductMove> Back, IReadOnlyList<int> MadeFolderIds) : MoveUndo;

    /// <summary>A folder moved, where it was before.</summary>
    public sealed record Folder(ProductFolderMove Back) : MoveUndo;
}
