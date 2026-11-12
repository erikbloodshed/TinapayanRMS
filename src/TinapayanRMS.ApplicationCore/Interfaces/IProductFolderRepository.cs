using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A folder of the Products list put somewhere else: the stage it goes in and the folder it goes inside, or null for the
/// top of the stage.
/// </summary>
public sealed record ProductFolderMove(int FolderId, ProductStage Stage, int? ParentId);

/// <summary>
/// The Products list's folders (ProductFolder), which hold products and other folders as a file explorer's do. A folder's
/// name is unique among the folders beside it, whatever its case; a folder is in its parent's stage, and holds products of
/// its own stage only (ProductFolderRules).
/// </summary>
public interface IProductFolderRepository
{
    /// <summary>Every folder, in both stages, by name.</summary>
    Task<IReadOnlyList<ProductFolder>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds the folder, its name trimmed, inside its parent (in the parent's stage) or at the top of its stage, and gives it
    /// its id. Refused if the name is taken beside it or the parent doesn't exist.
    /// </summary>
    Task AddAsync(ProductFolder folder, CancellationToken cancellationToken);

    /// <summary>Renames the folder and nothing else. Refused if the name is taken beside it.</summary>
    Task RenameAsync(int id, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Puts the folder, with all it holds, inside another folder or at the top of a stage. Moved to the other stage, every
    /// folder in it and every product in those (deleted ones too) goes with it, each product's UpdatedAt stamped. Refused
    /// if it would go inside itself, the name is taken there, or the folder to go in is gone or in the other stage.
    /// Returns the ids of the products whose stage changed.
    /// </summary>
    Task<IReadOnlyList<int>> MoveAsync(ProductFolderMove move, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the folder and nothing in it: its products (deleted ones too) and folders go up into the folder it was in,
    /// or to the top of its stage. Refused, with nothing changed, if a folder in it has a name taken there.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes each of these folders that holds nothing, no product (deleted or not) and no folder, deepest first, so a
    /// folder emptied by deleting the one in it goes too; one that holds something, or is gone, is left. An Undo of a
    /// Promote, taking away the folders it made.
    /// </summary>
    Task DeleteIfEmptyAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken);
}
