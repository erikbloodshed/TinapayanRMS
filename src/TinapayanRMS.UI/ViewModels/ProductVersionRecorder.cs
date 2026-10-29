using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>A product as it was saved before a save that may change it: its snapshot, and when it was last saved.</summary>
public sealed record CapturedProduct(ProductVersionContent Content, DateTime? UpdatedAt);

/// <summary>
/// What <see cref="ProductVersionRecorder.CaptureAsync"/> read before a save: each product the save may change besides
/// the one being saved, as it stood.
/// </summary>
public sealed record ProductVersionCapture(IReadOnlyDictionary<int, CapturedProduct> Products)
{
    public static ProductVersionCapture Empty { get; } = new(new Dictionary<int, CapturedProduct>());
}

/// <summary>
/// Records versions of the products a save changes without being their own Save: those linking a sub-assembly saved on
/// the Sub-Assemblies screen, or sharing lines, or linking the packaging, of a product saved on the Products screen.
/// Read before the save (<see cref="CaptureAsync"/>) and again after it (<see cref="RecordChangesAsync"/>), each one
/// whose recipe changed gets a version with a note saying where the change came from, and one with no versions yet
/// first gets how it stood before, as its own first Save would record. And a clone's or a variant's first version
/// (<see cref="RecordMadeFromAsync"/>).
/// </summary>
public sealed class ProductVersionRecorder(
    SavedProductReader savedProducts,
    IProductRepository productRepository,
    IProductVersionRepository versionRepository)
{
    /// <summary>
    /// The products that read through these sub-assemblies, or through <paramref name="productIds"/>' sections and
    /// packaging (IProductRepository.GetProductsReadingAsync), as they're saved now; <paramref name="productIds"/>
    /// themselves are left out, since their own Save records them.
    /// </summary>
    public async Task<ProductVersionCapture> CaptureAsync(
        IReadOnlyCollection<int> subAssemblyIds, IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        var readers = await productRepository.GetProductsReadingAsync(subAssemblyIds, productIds, cancellationToken);
        return new ProductVersionCapture(await SnapshotAsync([.. readers.Except(productIds)], cancellationToken));
    }

    /// <summary>
    /// Reads the captured products again and records a version, noted <paramref name="note"/> and dated
    /// <paramref name="savedAt"/>, of each one whose recipe or price differs from how it was captured, all in one write
    /// (IProductVersionRepository.AddRangeAsync). Returns the names of the products that got one, by name.
    /// </summary>
    public async Task<IReadOnlyList<string>> RecordChangesAsync(
        ProductVersionCapture before, string note, DateTime savedAt, CancellationToken cancellationToken)
    {
        if (before.Products.Count == 0)
        {
            return [];
        }

        var after = await SnapshotAsync([.. before.Products.Keys], cancellationToken);
        List<ProductVersionDraft> changed = [];
        foreach (var (productId, now) in after)
        {
            var was = before.Products[productId];
            if (!ProductVersionRules.IsSameRecipe(was.Content, now.Content))
            {
                changed.Add(new ProductVersionDraft(productId, now.Content, savedAt, note)
                {
                    Baseline = new(productId, was.Content, was.UpdatedAt ?? savedAt, ProductVersionRules.BaselineNote),
                });
            }
        }

        if (changed.Count == 0)
        {
            return [];
        }

        var recorded = await versionRepository.AddRangeAsync(changed, cancellationToken);
        return
        [
            .. changed.Zip(recorded)
                .Where(c => c.Second is not null)
                .Select(c => c.First.Content.ProductName)
                .Order(StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <summary>
    /// Records the first version of a product just made from another, a clone or a variant, so its history starts with
    /// where it came from: noted "<paramref name="madeAs"/> Almond Cheese Bread, version 4", with the source's version
    /// number when the source as saved is its latest version, and without one otherwise (no versions yet, or changed
    /// since without a Save that recorded one).
    /// </summary>
    public async Task RecordMadeFromAsync(
        int productId, int sourceProductId, string madeAs, DateTime savedAt, CancellationToken cancellationToken)
    {
        var snapshots = await SnapshotAsync([productId, sourceProductId], cancellationToken);
        if (!snapshots.TryGetValue(productId, out var made) || !snapshots.TryGetValue(sourceProductId, out var source))
        {
            return;
        }

        var versions = await versionRepository.GetForProductAsync(sourceProductId, cancellationToken);
        var latest = versions.Count > 0 ? versions[0] : null;
        var note = latest is not null && IsLatest(latest, source.Content)
            ? $"{madeAs} {source.Content.ProductName}, version {latest.Number}"
            : $"{madeAs} {source.Content.ProductName}";
        await versionRepository.AddAsync(productId, made.Content, savedAt, note, cancellationToken);

        // A latest version that can't be read isn't the source as it is: the note goes without a number.
        static bool IsLatest(ProductVersion latest, ProductVersionContent source)
        {
            try
            {
                return ProductVersionRules.IsSameRecipe(ProductVersionContent.FromJson(latest.Content), source);
            }
            catch (BusinessRuleException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// What a save that recorded versions of <paramref name="names"/> adds to its message: "A version was recorded for
    /// Cheese Roll and Pandesal.", with the first three named and the rest counted. Null when it recorded none.
    /// </summary>
    public static string? RecordedMessage(IReadOnlyList<string> names)
    {
        const int Named = 3;
        if (names.Count == 0)
        {
            return null;
        }

        var listed = names.Count <= Named
            ? JoinNames(names)
            : $"{string.Join(", ", names.Take(Named))} and {names.Count - Named} more";
        return $"A version was recorded for {listed}.";

        static string JoinNames(IReadOnlyList<string> names) => names.Count == 1
            ? names[0]
            : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
    }

    private async Task<Dictionary<int, CapturedProduct>> SnapshotAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return [];
        }

        // One after the other: a Save captures and records inside its unit of work, whose reads share one connection.
        var products = await productRepository.GetByIdsAsync(productIds, cancellationToken);
        var details = await savedProducts.ReadDetailsAsync(productIds, cancellationToken);
        return products.ToDictionary(
            p => p.Id,
            p => new CapturedProduct(ProductVersionSnapshot.From(p, details[p.Id]), p.UpdatedAt));
    }
}
