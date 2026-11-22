using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// Where Save files the new product the Products screen's form holds (through ProductsState.NewProductPlace): its stage
/// and the user's folder, as New, New Product Here and Settings' stage for a new product decide them, following a folder
/// moved to the other stage or deleted since. The list's new row starts there, and the header's tag names it
/// (ProductsState.NewProductTag). Revert keeps it.
/// </summary>
public sealed class NewProductPlace : ObservableState
{
    /// <summary>
    /// Whether the new product goes in Settings' stage rather than a folder's: started by New, not New Product Here. Such a
    /// product follows the setting when it changes (<see cref="TakeDefault"/>).
    /// </summary>
    private bool _inDefaultStage = true;

    /// <summary>The stage New files a product in (Settings → Costing Defaults), as of the last LoadCommand.</summary>
    private ProductStage _defaultStage = CostingDefaults.DefaultNewProductStage;

    private ProductStage _stage = CostingDefaults.DefaultNewProductStage;
    /// <summary>
    /// The stage Save files a new product in: the one Settings → Costing Defaults gives a new product after New
    /// (Production unless it says otherwise), or the stage of the folder New Product Here was picked on (<see cref="FolderId"/>).
    /// </summary>
    public ProductStage Stage
    {
        get => _stage;
        private set => RaiseAndSetIfChanged(ref _stage, value);
    }

    private int? _folderId;
    /// <summary>
    /// The user's folder New Product Here started the product in, which Save files it in; null after New, and for a stage's
    /// folder: the top of the stage.
    /// </summary>
    public int? FolderId
    {
        get => _folderId;
        private set => RaiseAndSetIfChanged(ref _folderId, value);
    }

    /// <summary>
    /// The stage New Product Here gave the product, as it is now, which Revert starts the form again in; null after New,
    /// whose product follows Settings' stage.
    /// </summary>
    public ProductStage? ChosenStage => _inDefaultStage ? null : Stage;

    /// <summary>
    /// A new product goes in <paramref name="stage"/> and the user's folder <paramref name="folderId"/> (New Product Here),
    /// or in Settings' stage and no folder for a null stage (New).
    /// </summary>
    public void StartNew(ProductStage? stage, int? folderId)
    {
        _inDefaultStage = stage is null;
        Stage = stage ?? _defaultStage;
        FolderId = stage is null ? null : folderId;
    }

    /// <summary>
    /// Settings' stage for a new product, as a LoadCommand read it. A new product started by New, while the form holds one
    /// (<paramref name="formIsNew"/>), goes in it whatever was typed, since its stage was never picked.
    /// </summary>
    public void TakeDefault(ProductStage stage, bool formIsNew)
    {
        _defaultStage = stage;
        if (formIsNew && _inDefaultStage)
        {
            Stage = stage;
        }
    }

    /// <summary>
    /// The folders as read again (ProductsState.Folders): the product's folder, moved to the other stage since, takes it
    /// there with it, as Save would file it there.
    /// </summary>
    public void FoldersRead(IReadOnlyList<ProductFolder> folders)
    {
        if (FolderId is { } folderId && folders.FirstOrDefault(f => f.Id == folderId) is { } folder)
        {
            Stage = folder.Stage;
        }
    }

    /// <summary>A folder was deleted: a product started in it goes in the folder it was in, <paramref name="parentId"/>.</summary>
    public void FolderDeleted(int folderId, int? parentId)
    {
        if (FolderId == folderId)
        {
            FolderId = parentId;
        }
    }

    /// <summary>
    /// Where a row of the Products list puts something new started from it (the list's New Product, New Composite Product
    /// and New Folder, from the row picked): in a folder (a stage's: at its top), or in a product's folder and stage; null
    /// for the Deleted folder, a deleted product, a row still being named, or no row, for the caller's own default.
    /// </summary>
    public static (ProductStage Stage, int? FolderId)? Of(object? row) => row switch
    {
        ProductFolderNode { IsDeletedFolder: false, IsNewFolder: false } folder => (folder.Stage, folder.FolderId),
        ProductLeafNode { IsDeleted: false, IsNew: false } leaf => (leaf.Product.Stage, leaf.Product.FolderId),
        _ => null,
    };
}
