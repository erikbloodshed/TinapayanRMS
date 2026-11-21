using DynamicData.Binding;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UseCases.Products;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// Settings' Product Categories tab: what the Products list groups products under, a list of its own
/// apart from raw material categories (RawMaterialCategoriesState).
/// A category can set the Wastage % its products are priced with in place of the shop-wide one.
/// </summary>
public sealed class ProductCategoriesState(IProductCategoryEditor editor, ILogger logger)
    : LookupListState<ProductCategory>("Category", c => c.Id, SortExpressionComparer<ProductCategory>.Ascending(c => c.Name), editor, logger)
{
    private decimal? _editingWastagePercent;

    /// <summary>The Wastage % the category's products are priced with; null (blank) for the shop-wide one.</summary>
    public decimal? EditingWastagePercent
    {
        get => _editingWastagePercent;
        set => RaiseAndSetIfChanged(ref _editingWastagePercent, value);
    }

    protected override string DeleteQuestion(ProductCategory item) =>
        $"Delete the '{item.Name}' category? Its products are left uncategorized. This can't be undone.";

    protected override void LoadIntoForm(ProductCategory item)
    {
        EditingName = item.Name;
        EditingWastagePercent = item.WastagePercent;
    }

    protected override ProductCategory FormItem() =>
        new() { Id = EditingId, Name = EditingName.Trim(), WastagePercent = EditingWastagePercent };

    protected override void ClearFields() => EditingWastagePercent = null;
}
