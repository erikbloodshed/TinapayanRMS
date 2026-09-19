using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The Settings dialog's Costing Defaults tab: the labor, overhead, administrative, markup and VAT percentages a new
/// product's costing starts with (ProductsViewModel). Saving them changes no saved product.
/// </summary>
public sealed class CostingDefaultsViewModel : ViewModelBase
{
    private readonly ICostingDefaultsRepository _repository;
    private readonly IValidator<CostingParameters> _validator;

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

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }

    public CostingDefaultsViewModel(ICostingDefaultsRepository repository, IValidator<CostingParameters> validator, ILogger logger)
        : base(logger)
    {
        _repository = repository;
        _validator = validator;

        // What's saved replaces the fields: an unsaved edit doesn't outlive the dialog it was typed in.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var defaults = await _repository.GetAsync(CancellationToken.None);
            EditingLaborPercent = defaults.LaborPercent;
            EditingOverheadPercent = defaults.OverheadPercent;
            EditingAdministrativePercent = defaults.AdministrativePercent;
            EditingMarginPercent = defaults.MarginPercent;
            EditingVatPercent = defaults.VatPercent;
        });

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
                    LaborPercent = EditingLaborPercent,
                    OverheadPercent = EditingOverheadPercent,
                    AdministrativePercent = EditingAdministrativePercent,
                    MarginPercent = EditingMarginPercent,
                    VatPercent = EditingVatPercent,
                },
                CancellationToken.None);
            SuccessMessage = "Costing defaults saved. New products start with them.";
        }));
    }
}
