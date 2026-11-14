using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Products screen's costing and costs (through ProductsViewModel.Costing; the Costing tab and the header draw them):
/// the costing percentages as typed, each metric's amount, the cost summary worked out from them and the recipe, which of
/// the materials' costs every figure is at (<see cref="CostBasis"/>), what the figures are for (the captions), and which
/// materials have risen past their buffered cost. The screen supplies the recipe's figures and says when
/// (<see cref="Recompute"/>); it also keeps the undo history, which these percentages are part of. What Save compares
/// against, the costing as loaded, a new product's defaults, and the shop's wastage, VAT and price rounding every product is
/// priced with are here.
/// </summary>
public sealed class ProductCosting : ReactiveObject
{
    // What the captions name, as the last Recompute was given it: the quantity a sellable unit holds, whether it's a
    // composite, and whether a composite takes its products' packaging too.
    private int _quantityRequired = 1;
    private bool _isComposite;
    private bool _includesComponentPackaging;

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
        set => this.RaiseAndSetIfChanged(ref _editingLaborPercent, value);
    }

    private decimal _editingOverheadPercent;
    public decimal EditingOverheadPercent
    {
        get => _editingOverheadPercent;
        set => this.RaiseAndSetIfChanged(ref _editingOverheadPercent, value);
    }

    private decimal _editingAdministrativePercent;
    public decimal EditingAdministrativePercent
    {
        get => _editingAdministrativePercent;
        set => this.RaiseAndSetIfChanged(ref _editingAdministrativePercent, value);
    }

    private decimal _editingMarginPercent;
    public decimal EditingMarginPercent
    {
        get => _editingMarginPercent;
        set => this.RaiseAndSetIfChanged(ref _editingMarginPercent, value);
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

    private ProductCostSummary _costs = ProductCostSummary.Empty;
    /// <summary>The cost summary, as <see cref="Recompute"/> last worked it out.</summary>
    public ProductCostSummary Costs
    {
        get => _costs;
        private set
        {
            this.RaiseAndSetIfChanged(ref _costs, value);

            // The metrics' value boxes read the amounts from here: a percentage, the recipe or another metric changed.
            this.RaisePropertyChanged(nameof(EditingLaborAmount));
            this.RaisePropertyChanged(nameof(EditingOverheadAmount));
            this.RaisePropertyChanged(nameof(EditingAdministrativeAmount));
            this.RaisePropertyChanged(nameof(EditingMarkupAmount));
        }
    }

    private CostBasis _costBasis;
    /// <summary>
    /// Which of the materials' costs every figure on the screen is at: buffered, as always, until the header's Unbuffered
    /// button (ProductsViewModel.ToggleCostBasisCommand) switches it to their true costs for a look without the buffer. It
    /// isn't part of the product, so it's no edit: nothing to undo or save, and it stays as it is from one product to the
    /// next until the app closes. Print prints at it too. Only the screen switches it, as it prices its tables' rows again.
    /// </summary>
    public CostBasis CostBasis
    {
        get => _costBasis;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _costBasis, value);
            this.RaisePropertyChanged(nameof(IsUnbuffered));
            this.RaisePropertyChanged(nameof(CostBasisButtonLabel));
            this.RaisePropertyChanged(nameof(CostBasisButtonTooltip));
            this.RaisePropertyChanged(nameof(MaterialCostTooltip));
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

    private string? _belowCostText;
    /// <summary>
    /// The header's "Priced below cost" badge's tooltip, naming the purchased materials in the recipe and packaging that
    /// prices have risen past their fixed buffered cost (RawMaterialRules.IsBufferedBelowCost): the product is priced at the
    /// buffered costs, so under what those cost. Null, and no badge, while none is. A composite's products' materials aren't
    /// in the form, so they aren't counted; each says so when it's opened.
    /// </summary>
    public string? BelowCostText
    {
        get => _belowCostText;
        private set => this.RaiseAndSetIfChanged(ref _belowCostText, value);
    }

    /// <summary>What the figures on this screen are for: "Per piece", or "Per unit of 8 pieces".</summary>
    public string CostUnitCaption => ProductPrintout.CostUnitCaptionFor(_quantityRequired, _isComposite);

    /// <summary>The Costing Summary's price row: "Suggested Selling Price per Piece", or "… per Unit of 8 Pieces".</summary>
    public string SuggestedSellingPriceLabel => ProductPrintout.SuggestedSellingPriceLabelFor(CostUnitCaption);

    /// <summary>The header's Material Cost tooltip: what goes into it, which for a composite is what its products bring.</summary>
    public string MaterialCostTooltip => (_isComposite
        ? $"{CostUnitCaption}: the recipe cost of the products it holds and its own sub-assemblies, plus its own packaging"
            + (_includesComponentPackaging ? " and theirs" : string.Empty)
        : $"{CostUnitCaption}: the share of every sub-assembly it holds, plus its packaging")
        + (IsUnbuffered ? ", at the materials' true costs, without their cost buffers" : ", at the materials' buffered costs");

    /// <summary>What the Costing tab's labor, overhead and admin metrics are a percentage of, for their tooltips.</summary>
    public string MetricBaseTooltip => _isComposite
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
    /// wastage, VAT and price rounding, which every product is priced with. True when those shop figures changed, so the
    /// product showing is priced again.
    /// </summary>
    public bool SetDefaults(CostingDefaults defaults)
    {
        _defaults = (defaults.LaborPercent, defaults.OverheadPercent, defaults.AdministrativePercent, defaults.MarginPercent);
        if (_wastagePercent == defaults.WastagePercent && _vatPercent == defaults.VatPercent && _priceRoundingStep == defaults.PriceRoundingStep)
        {
            return false;
        }

        _wastagePercent = defaults.WastagePercent;
        _vatPercent = defaults.VatPercent;
        _priceRoundingStep = defaults.PriceRoundingStep;
        return true;
    }

    /// <summary>
    /// The cost summary from <paramref name="recipe"/>, the recipe and product figures as the screen has them now, with
    /// the shop's VAT and price rounding and <paramref name="categoryWastagePercent"/>, the product's category's own
    /// wastage, or the shop's when it sets none, at the percentages as typed here; the captions follow the quantity and
    /// kind the figures are for (in <paramref name="recipe"/>), and the below-cost warning <paramref name="materials"/>,
    /// every material the recipe and packaging use.
    /// </summary>
    public void Recompute(ProductCostInputs recipe, decimal? categoryWastagePercent, IEnumerable<RawMaterial> materials)
    {
        var captionsChange = recipe.QuantityRequired != _quantityRequired
            || recipe.IsComposite != _isComposite
            || (recipe.ComponentPackaging is not null) != _includesComponentPackaging;
        _quantityRequired = recipe.QuantityRequired;
        _isComposite = recipe.IsComposite;
        _includesComponentPackaging = recipe.ComponentPackaging is not null;

        Costs = new ProductCostSummary(recipe with
        {
            WastagePercent = categoryWastagePercent ?? _wastagePercent,
            VatPercent = _vatPercent,
            PriceRoundingStep = _priceRoundingStep,
            LaborPercent = EditingLaborPercent,
            OverheadPercent = EditingOverheadPercent,
            AdministrativePercent = EditingAdministrativePercent,
            MarginPercent = EditingMarginPercent,
        });

        List<string> belowCost =
        [
            .. materials
                .Where(RawMaterialRules.IsBufferedBelowCost)
                .Select(m => m.Name)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase),
        ];
        BelowCostText = belowCost.Count switch
        {
            0 => null,
            1 => $"Prices have risen past the buffered cost of {belowCost[0]}, so this product is priced under what it costs. "
                + "Re-apply its target buffer on Raw Materials, or in Reports' Cost Buffers.",
            _ => $"Prices have risen past the buffered costs of {string.Join(", ", belowCost[..^1])} and {belowCost[^1]}, so this "
                + "product is priced under what they cost. Re-apply their target buffers on Raw Materials, or in Reports' Cost Buffers.",
        };

        if (captionsChange)
        {
            this.RaisePropertyChanged(nameof(CostUnitCaption));
            this.RaisePropertyChanged(nameof(SuggestedSellingPriceLabel));
            this.RaisePropertyChanged(nameof(MaterialCostTooltip));
            this.RaisePropertyChanged(nameof(MetricBaseTooltip));
        }
    }
}
