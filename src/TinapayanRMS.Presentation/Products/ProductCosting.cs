using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// A product's costing as its form edits it (ADR 0002), the Costing part's draft, and its costs (through ProductForm.Costing;
/// the Costing tab and the header draw them):
/// the costing percentages as typed, each metric's amount, the cost summary worked out from them and the recipe, which of
/// the materials' costs every figure is at (<see cref="CostBasis"/>), what the figures are for (the captions), and which
/// materials have risen past their buffered cost. The figures are worked out as they're read, from what the screen gathers
/// (<see cref="ProductCostingInputs"/>), and again once anything they come from changed: the percentages and the cost basis
/// here say so themselves, and the screen says so for the rest (<see cref="InputsChanged"/>). The screen also keeps the undo
/// history, which these percentages are part of. What Save compares against, the costing as loaded, a new product's
/// defaults, and the shop's wastage, VAT and price rounding every product is priced with are here.
/// </summary>
public sealed class ProductCosting : ObservableState
{
    /// <summary>The costs and what goes with them, worked out when read (<see cref="Figures"/>).</summary>
    private readonly Derived<CostingFigures> _figures;

    /// <param name="inputs">What the screen has for the costs now: its recipe, packaging, fields and materials.</param>
    public ProductCosting(Func<ProductCostingInputs> inputs)
    {
        _figures = new Derived<CostingFigures>(() => Figures(inputs()), RaiseFiguresChanged);
    }

    /// <summary>What a new product's costing starts with (Settings → Costing Defaults), as of the screen's last load.</summary>
    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin) _defaults;

    /// <summary>
    /// The shop-wide Wastage % (Settings → Costing Defaults), as of the screen's last load: what a product is priced with
    /// unless its category sets its own.
    /// </summary>
    private decimal _wastagePercent = CostingDefaults.DefaultWastagePercent;

    /// <summary>The shop's VAT (Settings → Costing Defaults), as of the screen's last load: what every product is priced with.</summary>
    private decimal _vatPercent;

    /// <summary>What the rounded selling price is rounded up to a multiple of (CostingDefaults.PriceRoundingStep).</summary>
    private decimal _priceRoundingStep;

    /// <summary>The costing as loaded, so Save only sends it when it changed.</summary>
    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin) _saved;

    private decimal _editingLaborPercent;
    public decimal EditingLaborPercent
    {
        get => _editingLaborPercent;
        set => SetPercent(ref _editingLaborPercent, value);
    }

    private decimal _editingOverheadPercent;
    public decimal EditingOverheadPercent
    {
        get => _editingOverheadPercent;
        set => SetPercent(ref _editingOverheadPercent, value);
    }

    private decimal _editingAdministrativePercent;
    public decimal EditingAdministrativePercent
    {
        get => _editingAdministrativePercent;
        set => SetPercent(ref _editingAdministrativePercent, value);
    }

    private decimal _editingMarginPercent;
    public decimal EditingMarginPercent
    {
        get => _editingMarginPercent;
        set => SetPercent(ref _editingMarginPercent, value);
    }

    /// <summary>A costing percentage set: the product is costed again as it's next read.</summary>
    private void SetPercent(ref decimal field, decimal value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (RaiseAndSetIfChanged(ref field, value, propertyName))
        {
            _figures.Invalidate();
        }
    }

    // Each metric's value box: its percentage as pesos for one sellable unit. Typing an amount sets the percentage that
    // comes to it, which is what's saved; the amount is only ever worked out from it.
    private TypedAmount? _typedLaborAmount;
    private TypedAmount? _typedOverheadAmount;
    private TypedAmount? _typedAdministrativeAmount;
    private TypedAmount? _typedMarkupAmount;

    /// <summary>Labor Metric's value: EditingLaborPercent of the sub-assemblies cost.</summary>
    public decimal EditingLaborAmount
    {
        get => TypedAmount.Shown(_typedLaborAmount, EditingLaborPercent, Costs.SubAssemblies, Costs.Labor);
        set => EditingLaborPercent = TypedAmount.PercentFor(ref _typedLaborAmount, value, Costs.SubAssemblies, EditingLaborPercent);
    }

    /// <summary>Overhead Metric's value: EditingOverheadPercent of the sub-assemblies cost.</summary>
    public decimal EditingOverheadAmount
    {
        get => TypedAmount.Shown(_typedOverheadAmount, EditingOverheadPercent, Costs.SubAssemblies, Costs.Overhead);
        set => EditingOverheadPercent = TypedAmount.PercentFor(ref _typedOverheadAmount, value, Costs.SubAssemblies, EditingOverheadPercent);
    }

    /// <summary>Admin Metric's value: EditingAdministrativePercent of the sub-assemblies cost.</summary>
    public decimal EditingAdministrativeAmount
    {
        get => TypedAmount.Shown(_typedAdministrativeAmount, EditingAdministrativePercent, Costs.SubAssemblies, Costs.Administrative);
        set => EditingAdministrativePercent = TypedAmount.PercentFor(
            ref _typedAdministrativeAmount, value, Costs.SubAssemblies, EditingAdministrativePercent);
    }

    /// <summary>Markup Metric's value: EditingMarginPercent of the base cost.</summary>
    public decimal EditingMarkupAmount
    {
        get => TypedAmount.Shown(_typedMarkupAmount, EditingMarginPercent, Costs.BaseCost, Costs.Markup);
        set => EditingMarginPercent = TypedAmount.PercentFor(ref _typedMarkupAmount, value, Costs.BaseCost, EditingMarginPercent);
    }

    /// <summary>The cost summary, worked out from the screen's recipe and fields and the percentages here as they stand.</summary>
    public ProductCostSummary Costs => _figures.Value.Costs;

    /// <summary>
    /// The figures may have changed: whatever shows them reads them again. The metrics' value boxes read their amounts from
    /// the costs, and the captions the quantity and kind they're for.
    /// </summary>
    private void RaiseFiguresChanged()
    {
        RaisePropertyChanged(nameof(Costs));
        RaisePropertyChanged(nameof(EditingLaborAmount));
        RaisePropertyChanged(nameof(EditingOverheadAmount));
        RaisePropertyChanged(nameof(EditingAdministrativeAmount));
        RaisePropertyChanged(nameof(EditingMarkupAmount));
        RaisePropertyChanged(nameof(BelowCostText));
        RaisePropertyChanged(nameof(NoPieceSizeText));
        RaisePropertyChanged(nameof(CostUnitCaption));
        RaisePropertyChanged(nameof(SuggestedSellingPriceLabel));
        RaisePropertyChanged(nameof(MaterialCostTooltip));
        RaisePropertyChanged(nameof(MetricBaseTooltip));
    }

    /// <summary>Something the costs are worked out from changed (the recipe, the packaging, the product's fields): they're worked out again when next read.</summary>
    internal void InputsChanged() => _figures.Invalidate();

    private CostBasis _costBasis;
    /// <summary>
    /// Which of the materials' costs every figure on the screen is at: buffered, as always, until the header's Unbuffered
    /// button (ProductForm.ToggleCostBasisCommand) switches it to their true costs for a look without the buffer. It
    /// isn't part of the product, so it's no edit: nothing to undo or save, and it stays as it is from one product to the
    /// next until the app closes. Print prints at it too. Only the form switches it (ToggleCostBasis), as it prices its
    /// tables' rows again, so the setter is internal: set anywhere else, the tables would stay at the other basis.
    /// </summary>
    public CostBasis CostBasis
    {
        get => _costBasis;
        internal set
        {
            if (RaiseAndSetIfChanged(ref _costBasis, value))
            {
                _figures.Invalidate();
            }

            RaisePropertyChanged(nameof(IsUnbuffered));
            RaisePropertyChanged(nameof(CostBasisButtonLabel));
            RaisePropertyChanged(nameof(CostBasisButtonTooltip));
            RaisePropertyChanged(nameof(MaterialCostTooltip));
        }
    }

    /// <summary>The screen shows its unbuffered costs: the header's badge says so.</summary>
    public bool IsUnbuffered => CostBasis == CostBasis.Unbuffered;

    /// <summary>
    /// The header's switch, named for the costs a click switches to: "Unbuffered" while
    /// the screen is buffered, "Buffered" while it isn't.
    /// </summary>
    public string CostBasisButtonLabel => IsUnbuffered ? "Buffered" : "Unbuffered";

    /// <summary>What the header's switch does from here.</summary>
    public string CostBasisButtonTooltip => IsUnbuffered
        ? "Shows every cost on this screen, and prints it, at the materials' buffered costs again. Nothing is saved."
        : "Shows every cost on this screen, and prints it, at the materials' true costs, without their cost buffers. Nothing is saved.";

    /// <summary>
    /// The header's "Priced below cost" badge's tooltip, naming the purchased materials in the recipe and packaging that
    /// prices have risen past their fixed buffered cost (RawMaterialRules.IsBufferedBelowCost): the product is priced at the
    /// buffered costs, so under what those cost. Null, and no badge, while none is. A composite's products' materials aren't
    /// in the form, so they aren't counted; each says so when it's opened.
    /// </summary>
    public string? BelowCostText => _figures.Value.BelowCostText;

    /// <summary>
    /// The header's "No piece size" badge's tooltip, naming the recipe's sections with neither a weight per piece nor a yield
    /// (ProductRules.HasPieceSize), which cost the product nothing per piece as the form stands. Null, and no badge, while
    /// every section has one.
    /// </summary>
    public string? NoPieceSizeText => _figures.Value.NoPieceSizeText;

    /// <summary>What the figures on this screen are for: "Per piece", or "Per unit of 8 pieces".</summary>
    public string CostUnitCaption => ProductPrintout.CostUnitCaptionFor(_figures.Value.QuantityRequired, _figures.Value.IsComposite);

    /// <summary>The Costing Summary's price row: "Suggested Selling Price per Piece", or "… per Unit of 8 Pieces".</summary>
    public string SuggestedSellingPriceLabel => ProductPrintout.SuggestedSellingPriceLabelFor(CostUnitCaption);

    /// <summary>The header's Material Cost tooltip: what goes into it, which for a composite is what its products bring.</summary>
    public string MaterialCostTooltip => (_figures.Value.IsComposite
        ? $"{CostUnitCaption}: the recipe cost of the products it holds and its own sub-assemblies, plus its own packaging"
            + (_figures.Value.IncludesComponentPackaging ? " and theirs" : string.Empty)
        : $"{CostUnitCaption}: the share of every sub-assembly it holds, plus its packaging")
        + (IsUnbuffered ? ", at the materials' true costs, without their cost buffers" : ", at the materials' buffered costs");

    /// <summary>What the Costing tab's labor, overhead and admin metrics are a percentage of, for their tooltips.</summary>
    public string MetricBaseTooltip => _figures.Value.IsComposite
        ? $"Of the recipe (ingredients) cost of the products it holds and its own sub-assemblies, packaging not included. {CostUnitCaption}."
        : $"Of the sub-assemblies (ingredients) cost, packaging not included. {CostUnitCaption}.";

    /// <summary>The four percentages as typed, for an undo step to keep and put back (ProductFormSnapshot.Costing).</summary>
    public (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin) Percentages
    {
        get => (EditingLaborPercent, EditingOverheadPercent, EditingAdministrativePercent, EditingMarginPercent);
        set => (EditingLaborPercent, EditingOverheadPercent, EditingAdministrativePercent, EditingMarginPercent) = value;
    }

    /// <summary>Whether the percentages differ from the costing as loaded: what Save sends only then.</summary>
    public bool IsChanged => Percentages != _saved;

    /// <summary>Whether the percentages are still the defaults a new product started with.</summary>
    public bool ShowsDefaults => Percentages == _defaults;

    /// <summary>The percentages as Save writes them.</summary>
    public CostingParameters ToParameters() => new()
    {
        LaborPercent = EditingLaborPercent,
        OverheadPercent = EditingOverheadPercent,
        AdministrativePercent = EditingAdministrativePercent,
        MarginPercent = EditingMarginPercent,
    };

    /// <summary>Shows a product's costing as saved, none for all zero, and makes it what Save compares against.</summary>
    public void Load(CostingParameters? costing)
    {
        Percentages = (
            costing?.LaborPercent ?? 0m,
            costing?.OverheadPercent ?? 0m,
            costing?.AdministrativePercent ?? 0m,
            costing?.MarginPercent ?? 0m);
        _saved = Percentages;
    }

    /// <summary>
    /// A new product's costing starts at the defaults, while what Save compares against stays at none (Load(null)), so
    /// its first Save writes them as its own. Defaults of all zero write nothing, as before there were defaults.
    /// </summary>
    public void ApplyDefaults() => Percentages = _defaults;

    /// <summary>
    /// Takes up the defaults as the screen's load read them: the percentages for the next new product, and the shop's
    /// wastage, VAT and price rounding, which every product is priced with: when those change, the product showing is
    /// priced again, as it's next read.
    /// </summary>
    public void SetDefaults(CostingDefaults defaults)
    {
        _defaults = (defaults.LaborPercent, defaults.OverheadPercent, defaults.AdministrativePercent, defaults.MarginPercent);
        if (_wastagePercent == defaults.WastagePercent && _vatPercent == defaults.VatPercent && _priceRoundingStep == defaults.PriceRoundingStep)
        {
            return;
        }

        _wastagePercent = defaults.WastagePercent;
        _vatPercent = defaults.VatPercent;
        _priceRoundingStep = defaults.PriceRoundingStep;
        _figures.Invalidate();
    }

    /// <summary>
    /// Takes up the defaults as a load read them (<see cref="SetDefaults"/>). A new product still showing the ones it started
    /// with (the screen's first load, or defaults changed in Settings since) takes the new ones; one whose costing was typed
    /// into keeps it. True when the new ones landed on it: not the user's change, so the form moves what Save measures
    /// against along with them.
    /// </summary>
    public bool TakeDefaults(CostingDefaults defaults, bool isNewProduct)
    {
        var untouchedNewProduct = isNewProduct && ShowsDefaults;
        SetDefaults(defaults);
        if (untouchedNewProduct)
        {
            ApplyDefaults();
        }

        return untouchedNewProduct;
    }

    /// <summary>
    /// The cost summary from the screen's recipe and product figures as they are now (<see cref="ProductCostingInputs.Recipe"/>),
    /// with the shop's VAT and price rounding and the product's category's own wastage, or the shop's when it sets none, at
    /// the percentages as typed here; the captions follow the quantity and kind the figures are for, and the below-cost
    /// warning every material the recipe and packaging use.
    /// </summary>
    private CostingFigures Figures(ProductCostingInputs inputs)
    {
        var recipe = inputs.Recipe;
        var costs = new ProductCostSummary(recipe with
        {
            WastagePercent = inputs.CategoryWastagePercent ?? _wastagePercent,
            VatPercent = _vatPercent,
            PriceRoundingStep = _priceRoundingStep,
            LaborPercent = EditingLaborPercent,
            OverheadPercent = EditingOverheadPercent,
            AdministrativePercent = EditingAdministrativePercent,
            MarginPercent = EditingMarginPercent,
        });

        List<string> belowCost =
        [
            .. inputs.Materials
                .Where(RawMaterialRules.IsBufferedBelowCost)
                .Select(m => m.Name)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase),
        ];
        var belowCostText = belowCost.Count switch
        {
            0 => null,
            1 => $"Prices have risen past the buffered cost of {belowCost[0]}, so this product is priced under what it costs. "
                + "Re-apply its target buffer on Raw Materials, or in Reports' Cost Buffers.",
            _ => $"Prices have risen past the buffered costs of {string.Join(", ", belowCost[..^1])} and {belowCost[^1]}, so this "
                + "product is priced under what they cost. Re-apply their target buffers on Raw Materials, or in Reports' Cost Buffers.",
        };

        var unsized = inputs.SectionsWithoutPieceSize;
        var noPieceSizeText = unsized.Count switch
        {
            0 => null,
            1 => $"{unsized[0]} has no weight per piece or yield, so it adds nothing to this product's cost. Type one on its tab.",
            _ => $"{string.Join(", ", unsized.Take(unsized.Count - 1))} and {unsized[^1]} have no weight per piece or yield, so they add "
                + "nothing to this product's cost. Type one on each one's tab.",
        };

        return new CostingFigures(costs, belowCostText, noPieceSizeText, recipe.QuantityRequired, recipe.IsComposite, recipe.ComponentPackaging is not null);
    }

    /// <summary>
    /// The costs worked out together, with what the captions name: the quantity a sellable unit holds, whether it's a
    /// composite, and whether a composite takes its products' packaging too.
    /// </summary>
    private sealed record CostingFigures(
        ProductCostSummary Costs, string? BelowCostText, string? NoPieceSizeText, int QuantityRequired, bool IsComposite, bool IncludesComponentPackaging);
}

/// <summary>
/// What the screen has for a product's costs as they stand (ProductForm.CostingInputs): the recipe's and product's
/// figures, the product's category's own Wastage %, if it sets one, and every material its recipe and packaging use.
/// </summary>
public sealed record ProductCostingInputs(ProductCostInputs Recipe, decimal? CategoryWastagePercent, IReadOnlyList<RawMaterial> Materials)
{
    /// <summary>The form's sections with no weight per piece or yield (ProductRules.HasPieceSize), by name, in recipe order.</summary>
    public IReadOnlyList<string> SectionsWithoutPieceSize { get; init; } = [];
}
