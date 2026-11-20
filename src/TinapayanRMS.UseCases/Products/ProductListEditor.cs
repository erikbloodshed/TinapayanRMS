using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UseCases.Products;

/// <summary>
/// What the Products list does to its products and folders: moves and renames products, promotes them to another stage,
/// and adds, renames, moves and deletes folders. A move or rename over a save made since <c>checks</c> were taken is refused
/// (SavedVersionRules), as a <see cref="BusinessRuleException"/>.
/// </summary>
public interface IProductListEditor
{
    /// <summary>Puts products in other folders or stages.</summary>
    Task MoveAsync(IReadOnlyCollection<ProductMove> moves, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>
    /// Moves products to <paramref name="stage"/>, each into the folder of its path there, made where there's none; what
    /// moved, and the folders made. One already in that stage stays as it is.
    /// </summary>
    Task<StageMove> MoveToStageAsync(
        IReadOnlyCollection<int> productIds, ProductStage stage, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>Renames a product.</summary>
    Task RenameAsync(int productId, string name, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>Adds a folder; it's given its saved id.</summary>
    Task AddFolderAsync(ProductFolder folder, CancellationToken cancellationToken);

    /// <summary>Renames a folder.</summary>
    Task RenameFolderAsync(int folderId, string name, CancellationToken cancellationToken);

    /// <summary>Puts a folder, with all it holds, somewhere else; the products that changed stage with it.</summary>
    Task<IReadOnlyList<int>> MoveFolderAsync(ProductFolderMove move, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken);

    /// <summary>Deletes a folder and nothing in it: what it holds goes up into the folder it was in, or to the top of its stage.</summary>
    Task DeleteFolderAsync(int folderId, CancellationToken cancellationToken);

    /// <summary>Deletes those of <paramref name="folderIds"/> that hold nothing, deepest first: an Undo of a Promote, taking away the folders it made.</summary>
    Task DeleteFoldersIfEmptyAsync(IReadOnlyCollection<int> folderIds, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IProductListEditor"/>
public sealed class ProductListEditor(IProductRepository productRepository, IProductFolderRepository folderRepository) : IProductListEditor
{
    public Task MoveAsync(IReadOnlyCollection<ProductMove> moves, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken) =>
        productRepository.MoveAsync(moves, checks, cancellationToken);

    public Task<StageMove> MoveToStageAsync(
        IReadOnlyCollection<int> productIds, ProductStage stage, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken) =>
        productRepository.MoveToStageAsync(productIds, stage, checks, cancellationToken);

    public Task RenameAsync(int productId, string name, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken) =>
        productRepository.RenameAsync(productId, name, checks, cancellationToken);

    public Task AddFolderAsync(ProductFolder folder, CancellationToken cancellationToken) => folderRepository.AddAsync(folder, cancellationToken);

    public Task RenameFolderAsync(int folderId, string name, CancellationToken cancellationToken) =>
        folderRepository.RenameAsync(folderId, name, cancellationToken);

    public Task<IReadOnlyList<int>> MoveFolderAsync(ProductFolderMove move, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken) =>
        folderRepository.MoveAsync(move, checks, cancellationToken);

    public Task DeleteFolderAsync(int folderId, CancellationToken cancellationToken) => folderRepository.DeleteAsync(folderId, cancellationToken);

    public Task DeleteFoldersIfEmptyAsync(IReadOnlyCollection<int> folderIds, CancellationToken cancellationToken) =>
        folderRepository.DeleteIfEmptyAsync(folderIds, cancellationToken);
}
