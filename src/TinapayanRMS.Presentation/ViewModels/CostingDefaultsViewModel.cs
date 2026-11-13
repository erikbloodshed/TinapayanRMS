using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Settings dialog's Costing Defaults tab: the stage New files a new product in, the labor, overhead, administrative,
/// markup and VAT percentages its costing starts with, the Cost Buffer % a new raw material starts with
/// (RawMaterialsViewModel), and the shop-wide Wastage % every product is priced with. Saving the defaults changes no saved
/// product or material, but the wastage reprices every product. Its second card works every stored cost out again
/// (RecalculateCostsCommand).
/// </summary>
public sealed class CostingDefaultsViewModel : ViewModelBase
{
    private readonly ICostingDefaultsRepository _repository;
    private readonly IValidator<CostingParameters> _validator;
    private readonly ICostRecalculator _costRecalculator;

    /// <summary>What the New Products In picker offers, Production first, each with its folder's title.</summary>
    public IReadOnlyList<KeyValuePair<ProductStage, string>> Stages { get; } =
        [.. new[] { ProductStage.Production, ProductStage.Innovation }.Select(s => KeyValuePair.Create(s, ProductRules.StageTitle(s)))];

    private ProductStage _editingNewProductStage = CostingDefaults.DefaultNewProductStage;
    /// <summary>The stage the Products screen's New files a product in.</summary>
    public ProductStage EditingNewProductStage
    {
        get => _editingNewProductStage;
        set => this.RaiseAndSetIfChanged(ref _editingNewProductStage, value);
    }

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
    /// <summary>The markup on cost, shown as Markup % (CostingParameters.MarginPercent).</summary>
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

    private decimal _editingWastagePercent = CostingDefaults.DefaultWastagePercent;
    /// <summary>The shop-wide Wastage %, 0 to 100: a share of every product's ingredient cost, added to its costing.</summary>
    public decimal EditingWastagePercent
    {
        get => _editingWastagePercent;
        set => this.RaiseAndSetIfChanged(ref _editingWastagePercent, value);
    }

    private decimal _editingCostBufferPercent = CostingDefaults.DefaultCostBufferPercent;
    /// <summary>What a new raw material's Cost Buffer % starts at: 0 to 100, as a material's own.</summary>
    public decimal EditingCostBufferPercent
    {
        get => _editingCostBufferPercent;
        set => this.RaiseAndSetIfChanged(ref _editingCostBufferPercent, value);
    }

    /// <summary>What Price Rounding offers: none, then each step CostingRules.PriceRoundingSteps allows.</summary>
    public IReadOnlyList<PriceRoundingOption> PriceRoundingOptions { get; } =
        [.. CostingRules.PriceRoundingSteps.Select(step => new PriceRoundingOption(step, step == 0m ? "None" : $"Up to the next {step:0.00}"))];

    private PriceRoundingOption _editingPriceRounding = null!;
    /// <summary>What a suggested price is rounded up to a multiple of, for the rounded figures beside the exact ones.</summary>
    public PriceRoundingOption EditingPriceRounding
    {
        get => _editingPriceRounding;
        set => this.RaiseAndSetIfChanged(ref _editingPriceRounding, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }

    /// <summary>
    /// Recalculate Costs: every made and linked ingredient's costs worked out again from what they're made of
    /// (ICostRecalculator), for after the database was changed outside the app. Says how many were out of date.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> RecalculateCostsCommand { get; }

    public CostingDefaultsViewModel(
        ICostingDefaultsRepository repository, IValidator<CostingParameters> validator, ICostRecalculator costRecalculator, ILogger logger)
        : base(logger)
    {
        _repository = repository;
        _validator = validator;
        _costRecalculator = costRecalculator;
        EditingPriceRounding = PriceRoundingOptions[0];

        // What's saved replaces the fields: an unsaved edit doesn't outlive the dialog it was typed in.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            var defaults = await _repository.GetAsync(CancellationToken.None);
            EditingNewProductStage = defaults.NewProductStage;
            EditingLaborPercent = defaults.LaborPercent;
            EditingOverheadPercent = defaults.OverheadPercent;
            EditingAdministrativePercent = defaults.AdministrativePercent;
            EditingMarginPercent = defaults.MarginPercent;
            EditingVatPercent = defaults.VatPercent;
            EditingWastagePercent = defaults.WastagePercent;
            EditingCostBufferPercent = defaults.CostBufferPercent;
            EditingPriceRounding = PriceRoundingOptions.FirstOrDefault(o => o.Step == defaults.PriceRoundingStep) ?? PriceRoundingOptions[0];
        }));

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            // The same checks, and messages, as a product's own costing; the VAT and the rest are the repository's to check.
            var validation = _validator.Validate(new CostingParameters
            {
                LaborPercent = EditingLaborPercent,
                OverheadPercent = EditingOverheadPercent,
                AdministrativePercent = EditingAdministrativePercent,
                MarginPercent = EditingMarginPercent,
            });
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            await _repository.SetAsync(
                new CostingDefaults
                {
                    NewProductStage = EditingNewProductStage,
                    LaborPercent = EditingLaborPercent,
                    OverheadPercent = EditingOverheadPercent,
                    AdministrativePercent = EditingAdministrativePercent,
                    MarginPercent = EditingMarginPercent,
                    VatPercent = EditingVatPercent,
                    WastagePercent = EditingWastagePercent,
                    CostBufferPercent = EditingCostBufferPercent,
                    PriceRoundingStep = EditingPriceRounding.Step,
                },
                CancellationToken.None);
            SuccessMessage = "Costing defaults saved. New products and raw materials start with them, and every product is priced with the VAT and the wastage.";
        }));

        RecalculateCostsCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var result = await _costRecalculator.RecalculateAllAsync(CancellationToken.None);
            SuccessMessage = RecalculatedText(result);
        }));
    }

    /// <summary>"Costs recalculated: 2 made or linked ingredients were out of date, and are right now."</summary>
    public static string RecalculatedText(CostRecalculation result) => result.IngredientsCorrected switch
    {
        0 => "Costs recalculated: every one was already up to date.",
        1 => "Costs recalculated: 1 made or linked ingredient was out of date, and is right now.",
        var count => $"Costs recalculated: {count} made or linked ingredients were out of date, and are right now.",
    };
}

/// <summary>A choice of Settings' Price Rounding: the step, and how it reads, "Up to the next 0.25".</summary>
public sealed record PriceRoundingOption(decimal Step, string Label)
{
    public override string ToString() => Label;
}
