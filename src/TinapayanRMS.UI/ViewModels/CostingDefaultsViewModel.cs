using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The Settings dialog's Costing Defaults tab: the stage New files a new product in, the labor, overhead, administrative,
/// markup and VAT percentages its costing starts with, the Shrinkage % a new Recipe Structure section starts with
/// (ProductsViewModel), and the Cost Buffer % a new raw material starts with (RawMaterialsViewModel). Saving them changes
/// no saved product or material. Its second card works every stored cost out again (RecalculateCostsCommand).
/// </summary>
public sealed class CostingDefaultsViewModel : ViewModelBase
{
    private readonly ICostingDefaultsRepository _repository;
    private readonly IValidator<CostingParameters> _validator;
    private readonly ICostRecalculator _costRecalculator;

    /// <summary>What the New Products In picker offers, Production first, each with its folder's title.</summary>
    public IReadOnlyList<KeyValuePair<ProductStage, string>> Stages { get; } =
        [.. new[] { ProductStage.Production, ProductStage.ResearchAndDevelopment }.Select(s => KeyValuePair.Create(s, ProductFolderNode.StageTitle(s)))];

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

    private decimal _editingShrinkagePercent = CostingDefaults.DefaultShrinkagePercent;
    /// <summary>What a new section's Shrinkage % starts at: 0 to under 100, as a section's own.</summary>
    public decimal EditingShrinkagePercent
    {
        get => _editingShrinkagePercent;
        set => this.RaiseAndSetIfChanged(ref _editingShrinkagePercent, value);
    }

    private decimal _editingCostBufferPercent = CostingDefaults.DefaultCostBufferPercent;
    /// <summary>What a new raw material's Cost Buffer % starts at: 0 to 100, as a material's own.</summary>
    public decimal EditingCostBufferPercent
    {
        get => _editingCostBufferPercent;
        set => this.RaiseAndSetIfChanged(ref _editingCostBufferPercent, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }

    /// <summary>
    /// Recalculate Costs (2026-10-03): every made and linked ingredient's costs and every sub-assembly's totals worked out
    /// again from what they're made of (ICostRecalculator), for after the database was changed outside the app. Says how
    /// many were out of date.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> RecalculateCostsCommand { get; }

    public CostingDefaultsViewModel(
        ICostingDefaultsRepository repository, IValidator<CostingParameters> validator, ICostRecalculator costRecalculator, ILogger logger)
        : base(logger)
    {
        _repository = repository;
        _validator = validator;
        _costRecalculator = costRecalculator;

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
            EditingShrinkagePercent = defaults.ShrinkagePercent;
            EditingCostBufferPercent = defaults.CostBufferPercent;
        }));

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            // The same checks, and messages, as a product's own costing.
            var validation = _validator.Validate(new CostingParameters
            {
                LaborPercent = EditingLaborPercent,
                OverheadPercent = EditingOverheadPercent,
                AdministrativePercent = EditingAdministrativePercent,
                MarginPercent = EditingMarginPercent,
                VatPercent = EditingVatPercent,
            });
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
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
                    ShrinkagePercent = EditingShrinkagePercent,
                    CostBufferPercent = EditingCostBufferPercent,
                },
                CancellationToken.None);
            SuccessMessage = "Costing defaults saved. New products, sections and raw materials start with them.";
        }));

        RecalculateCostsCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var result = await _costRecalculator.RecalculateAllAsync(CancellationToken.None);
            SuccessMessage = RecalculatedText(result);
        }));
    }

    /// <summary>"Costs recalculated: 1 made or linked ingredient and 2 sub-assemblies were out of date, and are right now."</summary>
    public static string RecalculatedText(CostRecalculation result)
    {
        if (!result.CorrectedAny)
        {
            return "Costs recalculated: every one was already up to date.";
        }

        List<string> parts = [];
        if (result.IngredientsCorrected > 0)
        {
            parts.Add(result.IngredientsCorrected == 1 ? "1 made or linked ingredient" : $"{result.IngredientsCorrected} made or linked ingredients");
        }

        if (result.SubAssembliesCorrected > 0)
        {
            parts.Add(result.SubAssembliesCorrected == 1 ? "1 sub-assembly" : $"{result.SubAssembliesCorrected} sub-assemblies");
        }

        var verb = result.IngredientsCorrected + result.SubAssembliesCorrected == 1 ? "was" : "were";
        return $"Costs recalculated: {string.Join(" and ", parts)} {verb} out of date, and {(verb == "was" ? "is" : "are")} right now.";
    }
}
