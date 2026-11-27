using DynamicData.Binding;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UseCases.Products;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// Settings' Product Categories tab: what the Products list groups products under, a list of its own
/// apart from raw material categories (RawMaterialCategoriesState).
/// A category can set the Wastage % its products are priced with in place of the shop-wide one, and the costing
/// percentages its new products start with and Apply Costing Defaults gives them, and the Warn Below % its products
/// are flagged with, each in place of Settings'.
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

    private decimal? _editingLaborPercent;
    /// <summary>The Labor % its new products start with; null (blank) for Settings' (Costing Defaults).</summary>
    public decimal? EditingLaborPercent
    {
        get => _editingLaborPercent;
        set => RaiseAndSetIfChanged(ref _editingLaborPercent, value);
    }

    private decimal? _editingOverheadPercent;
    /// <summary>The Overhead % its new products start with; null (blank) for Settings'.</summary>
    public decimal? EditingOverheadPercent
    {
        get => _editingOverheadPercent;
        set => RaiseAndSetIfChanged(ref _editingOverheadPercent, value);
    }

    private decimal? _editingAdministrativePercent;
    /// <summary>The Administrative % its new products start with; null (blank) for Settings'.</summary>
    public decimal? EditingAdministrativePercent
    {
        get => _editingAdministrativePercent;
        set => RaiseAndSetIfChanged(ref _editingAdministrativePercent, value);
    }

    private decimal? _editingMarkupPercent;
    /// <summary>The Markup % its new products start with; null (blank) for Settings'.</summary>
    public decimal? EditingMarkupPercent
    {
        get => _editingMarkupPercent;
        set => RaiseAndSetIfChanged(ref _editingMarkupPercent, value);
    }

    private decimal? _editingMarkupFloorPercent;
    /// <summary>The Warn Below % its products are flagged with; null (blank) for Settings' (Costing Defaults).</summary>
    public decimal? EditingMarkupFloorPercent
    {
        get => _editingMarkupFloorPercent;
        set => RaiseAndSetIfChanged(ref _editingMarkupFloorPercent, value);
    }

    protected override string DeleteQuestion(ProductCategory item) =>
        $"Delete the '{item.Name}' category? Its products are left uncategorized. This can't be undone.";

    protected override void LoadIntoForm(ProductCategory item)
    {
        EditingName = item.Name;
        EditingWastagePercent = item.WastagePercent;
        EditingLaborPercent = item.LaborPercent;
        EditingOverheadPercent = item.OverheadPercent;
        EditingAdministrativePercent = item.AdministrativePercent;
        EditingMarkupPercent = item.MarkupPercent;
        EditingMarkupFloorPercent = item.MarkupFloorPercent;
    }

    protected override ProductCategory FormItem() => new()
    {
        Id = EditingId,
        Name = EditingName.Trim(),
        WastagePercent = EditingWastagePercent,
        LaborPercent = EditingLaborPercent,
        OverheadPercent = EditingOverheadPercent,
        AdministrativePercent = EditingAdministrativePercent,
        MarkupPercent = EditingMarkupPercent,
        MarkupFloorPercent = EditingMarkupFloorPercent,
    };

    protected override void ClearFields()
    {
        EditingWastagePercent = null;
        EditingLaborPercent = null;
        EditingOverheadPercent = null;
        EditingAdministrativePercent = null;
        EditingMarkupPercent = null;
        EditingMarkupFloorPercent = null;
    }
}
