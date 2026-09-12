using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

public class UnitsOfMeasureViewModel : ViewModelBase
{
    private readonly IUnitOfMeasureRepository _unitOfMeasureRepository;
    private readonly IValidator<UnitOfMeasure> _unitOfMeasureValidator;
    private readonly SourceCache<UnitOfMeasure, int> _unitCache = new(u => u.Id);
    private readonly ReadOnlyObservableCollection<UnitOfMeasure> _units;

    public ReadOnlyObservableCollection<UnitOfMeasure> Units => _units;

    private UnitOfMeasure? _selectedUnit;
    public UnitOfMeasure? SelectedUnit
    {
        get => _selectedUnit;
        set => this.RaiseAndSetIfChanged(ref _selectedUnit, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private string _editingAbbreviation = string.Empty;
    public string EditingAbbreviation
    {
        get => _editingAbbreviation;
        set => this.RaiseAndSetIfChanged(ref _editingAbbreviation, value);
    }

    private bool _editingIsLengthUnit;
    public bool EditingIsLengthUnit
    {
        get => _editingIsLengthUnit;
        set => this.RaiseAndSetIfChanged(ref _editingIsLengthUnit, value);
    }

    private decimal? _editingMillimetersPerUnit;
    public decimal? EditingMillimetersPerUnit
    {
        get => _editingMillimetersPerUnit;
        set => this.RaiseAndSetIfChanged(ref _editingMillimetersPerUnit, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public UnitsOfMeasureViewModel(IUnitOfMeasureRepository unitOfMeasureRepository, IValidator<UnitOfMeasure> unitOfMeasureValidator, ILogger logger)
        : base(logger)
    {
        _unitOfMeasureRepository = unitOfMeasureRepository;
        _unitOfMeasureValidator = unitOfMeasureValidator;

        _unitCache.Connect()
            .SortAndBind(out _units, SortExpressionComparer<UnitOfMeasure>.Ascending(u => u.Name))
            .Subscribe();

        this.WhenAnyValue(x => x.SelectedUnit)
            .Where(unit => unit is not null)
            .Subscribe(unit =>
            {
                _editingId = unit!.Id;
                EditingName = unit.Name;
                EditingAbbreviation = unit.Abbreviation;
                EditingIsLengthUnit = unit.IsLengthUnit;
                EditingMillimetersPerUnit = unit.MillimetersPerUnit;
            });

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var units = await _unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            _unitCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(units);
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedUnit = null;
            _editingId = 0;
            EditingName = string.Empty;
            EditingAbbreviation = string.Empty;
            EditingIsLengthUnit = false;
            EditingMillimetersPerUnit = null;
        });

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var unit = new UnitOfMeasure
            {
                Id = _editingId,
                Name = EditingName,
                Abbreviation = EditingAbbreviation,
                IsLengthUnit = EditingIsLengthUnit,
                MillimetersPerUnit = EditingIsLengthUnit ? EditingMillimetersPerUnit : null,
            };

            var validation = _unitOfMeasureValidator.Validate(unit);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingId == 0)
            {
                await _unitOfMeasureRepository.AddAsync(unit, CancellationToken.None);
            }
            else
            {
                await _unitOfMeasureRepository.UpdateAsync(unit, CancellationToken.None);
            }

            await LoadCommand.Execute();
            SelectedUnit = null;
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedUnit!.Name}'? This can't be undone.", async () =>
            {
                await _unitOfMeasureRepository.DeleteAsync(SelectedUnit!.Id, CancellationToken.None);
                SelectedUnit = null;
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedUnit).Select(unit => unit is not null));
    }
}
