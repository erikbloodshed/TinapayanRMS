using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// What the Products list's category filter shows (ProductsState.CategoryFilter): every product, a category's, or the
/// uncategorized ones, each in its folder. A category isn't a folder: its products are found wherever they're filed.
/// </summary>
public closed record ProductCategoryFilter
{
    /// <summary>What the picker shows for it.</summary>
    public abstract string Label { get; }

    /// <summary>Whether it shows <paramref name="product"/>.</summary>
    public abstract bool Shows(Product product);

    public sealed override string ToString() => Label;

    /// <summary>Every product: no filter.</summary>
    public sealed record All : ProductCategoryFilter
    {
        public override string Label => "All categories";

        public override bool Shows(Product product) => true;
    }

    /// <summary>The products in one category.</summary>
    public sealed record Category(int Id, string Name) : ProductCategoryFilter
    {
        public override string Label => Name;

        public override bool Shows(Product product) => product.CategoryId == Id;
    }

    /// <summary>The products with no category.</summary>
    public sealed record Uncategorized : ProductCategoryFilter
    {
        public override string Label => ProductRules.UncategorizedTitle;

        public override bool Shows(Product product) => product.CategoryId is null;
    }

    /// <summary>The picker's choices: every product, each of <paramref name="categories"/> in their order, then the uncategorized.</summary>
    public static IReadOnlyList<ProductCategoryFilter> ChoicesFor(IEnumerable<ProductCategory> categories) =>
        [new All(), .. categories.Select(c => new Category(c.Id, c.Name)), new Uncategorized()];
}
