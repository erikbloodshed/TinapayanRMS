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

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Settings dialog's Units of Measure tab. Picking a unit loads it for editing; New, or finishing
/// a Save or Delete, clears the form, so the next Save always adds a unit rather than changing the
/// last one touched.
/// </summary>
public sealed class UnitsOfMeasureViewModel : ViewModelBase, IDisposable
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

    private readonly ObservableAsPropertyHelper<string> _formTitle;
    /// <summary>"New Unit", or "Edit Unit" while one is picked, so it's clear what Save will do.</summary>
    public string FormTitle => _formTitle.Value;

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

        _formTitle = this.WhenAnyValue(x => x.SelectedUnit)
            .Select(unit => unit is null ? "New Unit" : "Edit Unit")
            .ToProperty(this, x => x.FormTitle, "New Unit");

        // Skips while LoadCommand merges, which only re-points the selection at its refreshed copy.
        this.WhenAnyValue(x => x.SelectedUnit)
            .Where(unit => unit is not null && !IsRefreshing)
            .Subscribe(unit =>
            {
                _editingId = unit!.Id;
                EditingName = unit.Name;
                EditingAbbreviation = unit.Abbreviation;
            });

        // Runs each time Settings opens and after this tab's own saves. It merges rather than
        // replaces, so the selection and any unsaved edits survive.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            var units = await _unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var selectedId = SelectedUnit?.Id;

            ApplyRefresh(() =>
            {
                _unitCache.MergeFrom(units);
                SelectedUnit = Units.FirstOrDefault(u => u.Id == selectedId);
            });
        }));

        NewCommand = ReactiveCommand.Create(ClearForm);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var unit = new UnitOfMeasure
            {
                Id = _editingId,
                Name = EditingName,
                Abbreviation = EditingAbbreviation,
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

            ClearForm();
            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedUnit!.Name}'? This can't be undone.", async () =>
            {
                await _unitOfMeasureRepository.DeleteAsync(SelectedUnit!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedUnit).Select(unit => unit is not null));
    }

    private void ClearForm()
    {
        SelectedUnit = null;
        _editingId = 0;
        EditingName = string.Empty;
        EditingAbbreviation = string.Empty;
    }

    public void Dispose() => _unitCache.Dispose();
}
