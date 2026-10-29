using ReactiveUI;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The Products screen's Costing tab (CostingCard, through ProductsViewModel.Costing, since 2026-10-28): the costing
/// percentages as typed, each metric's amount, and the cost summary worked out from them and the recipe. The screen
/// supplies the recipe's figures and says when (<see cref="Recompute"/>); it also keeps the undo history, which these
/// percentages are part of. What Save compares against, the costing as loaded, and a new product's defaults are here.
/// </summary>
public sealed class ProductCosting : ReactiveObject
{
    /// <summary>What a new product's costing starts with (Settings → Costing Defaults), as of the screen's last load.</summary>
    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) _defaults;

    /// <summary>The costing as loaded, so Save only sends it when it changed.</summary>
    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) _saved;

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

    private decimal _editingVatPercent;
    public decimal EditingVatPercent
    {
        get => _editingVatPercent;
        set => this.RaiseAndSetIfChanged(ref _editingVatPercent, value);
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

    /// <summary>The five percentages as typed, for an undo step to keep and put back (ProductFormSnapshot.Costing).</summary>
    public (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) Percentages
    {
        get => (EditingLaborPercent, EditingOverheadPercent, EditingAdministrativePercent, EditingMarginPercent, EditingVatPercent);
        set => (EditingLaborPercent, EditingOverheadPercent, EditingAdministrativePercent, EditingMarginPercent, EditingVatPercent) = value;
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
        VatPercent = EditingVatPercent,
    };

    /// <summary>Shows a product's costing as saved, none for all zero, and makes it what Save compares against.</summary>
    public void Load(CostingParameters? costing)
    {
        Percentages = (
            costing?.LaborPercent ?? 0m,
            costing?.OverheadPercent ?? 0m,
            costing?.AdministrativePercent ?? 0m,
            costing?.MarginPercent ?? 0m,
            costing?.VatPercent ?? 0m);
        _saved = Percentages;
    }

    /// <summary>
    /// A new product's costing starts at the defaults, while what Save compares against stays at none (Load(null)), so
    /// its first Save writes them as its own. Defaults of all zero write nothing, as before there were defaults.
    /// </summary>
    public void ApplyDefaults() => Percentages = _defaults;

    /// <summary>Takes up the defaults as the screen's load read them, for the next new product.</summary>
    public void SetDefaults(CostingDefaults defaults) =>
        _defaults = (defaults.LaborPercent, defaults.OverheadPercent, defaults.AdministrativePercent, defaults.MarginPercent, defaults.VatPercent);

    /// <summary>
    /// The cost summary from the recipe's figures, as the screen has them now, and the percentages as typed. A composite's
    /// <paramref name="parts"/> are its products' lines, then its own sections; <paramref name="componentPackaging"/> is its
    /// products' packaging when it takes theirs, else null.
    /// </summary>
    public void Recompute(
        IReadOnlyList<(string Name, decimal CostPerPiece)> parts,
        int quantityRequired,
        IReadOnlyList<decimal> packaging,
        decimal? currentPrice,
        bool isComposite,
        bool hasOwnSections,
        decimal? componentPackaging,
        decimal wastagePercent,
        decimal priceRoundingStep = 0m) =>
        Costs = new ProductCostSummary(
            parts,
            quantityRequired,
            packaging,
            EditingLaborPercent,
            EditingOverheadPercent,
            EditingAdministrativePercent,
            EditingMarginPercent,
            EditingVatPercent,
            currentPrice,
            isComposite,
            hasOwnSections,
            componentPackaging,
            wastagePercent,
            priceRoundingStep);
}
