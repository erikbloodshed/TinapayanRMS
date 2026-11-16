using System.Collections.ObjectModel;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// Settings' Units of Measure tab. Picking a unit loads it for editing; New, or finishing
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
        set
        {
            // Not while LoadCommand merges, which only re-points the selection at its refreshed copy.
            if (RaiseAndSetIfChanged(ref _selectedUnit, value) && value is { } unit && !IsRefreshing)
            {
                _editingId = unit.Id;
                EditingName = unit.Name;
                EditingAbbreviation = unit.Abbreviation;
            }
        }
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => RaiseAndSetIfChanged(ref _editingName, value);
    }

    private string _editingAbbreviation = string.Empty;
    public string EditingAbbreviation
    {
        get => _editingAbbreviation;
        set => RaiseAndSetIfChanged(ref _editingAbbreviation, value);
    }

    /// <summary>"New Unit", or "Edit Unit" while one is picked, so it's clear what Save will do.</summary>
    public string FormTitle => SelectedUnit is null ? "New Unit" : "Edit Unit";

    public ScreenCommand LoadCommand { get; }
    public ScreenCommand NewCommand { get; }
    public ScreenCommand SaveCommand { get; }
    public ScreenCommand DeleteCommand { get; }

    public UnitsOfMeasureViewModel(IUnitOfMeasureRepository unitOfMeasureRepository, IValidator<UnitOfMeasure> unitOfMeasureValidator, ILogger logger)
        : base(logger)
    {
        _unitOfMeasureRepository = unitOfMeasureRepository;
        _unitOfMeasureValidator = unitOfMeasureValidator;

        _unitCache.Connect()
            .SortAndBind(out _units, SortExpressionComparer<UnitOfMeasure>.Ascending(u => u.Name))
            .Subscribe();

        // Runs each time Settings opens and after this tab's own saves. It merges rather than
        // replaces, so the selection and any unsaved edits survive.
        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(async () =>
        {
            var units = await _unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var selectedId = SelectedUnit?.Id;

            ApplyRefresh(() =>
            {
                _unitCache.MergeFrom(units);
                SelectedUnit = Units.FirstOrDefault(u => u.Id == selectedId);
            });
        }));

        NewCommand = ScreenCommand.Of(ClearForm);

        SaveCommand = new ScreenCommand(() => RunSafelyAsync(async () =>
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
                ErrorMessage = validation.ToString(" ");
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
            await LoadCommand.ExecuteAsync();
        }));

        DeleteCommand = new ScreenCommand(
            () => RunWithConfirmationAsync($"Delete '{SelectedUnit!.Name}'? This can't be undone.", async () =>
            {
                await _unitOfMeasureRepository.DeleteAsync(SelectedUnit!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.ExecuteAsync();
            }),
            () => SelectedUnit is not null);
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
