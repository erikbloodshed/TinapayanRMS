using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>Settings' Units of Measure tab: each unit's name and abbreviation.</summary>
public sealed class UnitsOfMeasureState(IUnitOfMeasureRepository repository, IValidator<UnitOfMeasure> validator, ILogger logger)
    : LookupListState<UnitOfMeasure>("Unit", u => u.Id, SortExpressionComparer<UnitOfMeasure>.Ascending(u => u.Name), validator, logger)
{
    private string _editingAbbreviation = string.Empty;
    public string EditingAbbreviation
    {
        get => _editingAbbreviation;
        set => RaiseAndSetIfChanged(ref _editingAbbreviation, value);
    }

    protected override Task<IReadOnlyList<UnitOfMeasure>> ReadAllAsync() => repository.GetAllAsync(CancellationToken.None);

    protected override Task AddAsync(UnitOfMeasure item) => repository.AddAsync(item, CancellationToken.None);

    protected override Task UpdateAsync(UnitOfMeasure item) => repository.UpdateAsync(item, CancellationToken.None);

    protected override Task DeleteAsync(int id) => repository.DeleteAsync(id, CancellationToken.None);

    protected override string DeleteQuestion(UnitOfMeasure item) => $"Delete '{item.Name}'? This can't be undone.";

    protected override void LoadIntoForm(UnitOfMeasure item)
    {
        EditingName = item.Name;
        EditingAbbreviation = item.Abbreviation;
    }

    protected override UnitOfMeasure FormItem() => new() { Id = EditingId, Name = EditingName, Abbreviation = EditingAbbreviation };

    protected override void ClearFields() => EditingAbbreviation = string.Empty;
}
