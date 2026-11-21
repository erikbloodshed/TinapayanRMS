using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UseCases.Printing;

/// <summary>
/// What the Print dialog reads: its lists, as saved now, and the products of a set, priced as saved. A read its token cut
/// short ends in an <see cref="OperationCanceledException"/>, whatever the database's driver reported, so a preparation a
/// newer one replaced isn't taken for a failure.
/// </summary>
public interface IPrintQueries
{
    /// <summary>
    /// Every product in use, the folders and the categories, as they're saved now, read side by side: the Print list, its
    /// categories, and the products a Print on Products ticks.
    /// </summary>
    Task<PrintLists> ReadListsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="products"/> as they're saved, read together (in the same dozen or so queries however many there are)
    /// and each priced at <paramref name="basis"/>, in the same order: the pages of a set.
    /// </summary>
    Task<IReadOnlyList<SavedProduct>> PriceAsSavedAsync(IReadOnlyList<Product> products, CostBasis basis, CancellationToken cancellationToken);
}

/// <summary>What the Print dialog's lists are made from: the products in use, the folders and the categories.</summary>
public sealed record PrintLists(IReadOnlyList<Product> Products, IReadOnlyList<ProductFolder> Folders, IReadOnlyList<ProductCategory> Categories);

/// <inheritdoc cref="IPrintQueries"/>
public sealed class PrintQueries(
    IProductRepository productRepository,
    IProductFolderRepository folderRepository,
    IProductCategoryRepository categoryRepository,
    SavedProductReader savedProducts) : IPrintQueries
{
    public Task<PrintLists> ReadListsAsync(CancellationToken cancellationToken) => CancelledAsCancellation(
        async () =>
        {
            var productsRead = productRepository.GetAllAsync(cancellationToken);
            var foldersRead = folderRepository.GetAllAsync(cancellationToken);
            var categoriesRead = categoryRepository.GetAllAsync(cancellationToken);
            await Task.WhenAll(productsRead, foldersRead, categoriesRead);
            return new PrintLists(await productsRead, await foldersRead, await categoriesRead);
        },
        cancellationToken);

    public Task<IReadOnlyList<SavedProduct>> PriceAsSavedAsync(IReadOnlyList<Product> products, CostBasis basis, CancellationToken cancellationToken) =>
        CancelledAsCancellation(() => savedProducts.ReadAllAsync(products, basis, cancellationToken), cancellationToken);

    /// <summary>
    /// Runs <paramref name="read"/>, a failure once <paramref name="cancellationToken"/> is cancelled reported as the
    /// cancellation it is: a driver can report a command cancelled mid-read as its own exception (Npgsql's).
    /// </summary>
    private static async Task<T> CancelledAsCancellation<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The read was cancelled.", ex, cancellationToken);
        }
    }
}
