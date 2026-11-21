using DynamicData.Binding;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UseCases.Settings;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>Settings' Units of Measure tab: each unit's name and abbreviation.</summary>
public sealed class UnitsOfMeasureState(ILookupListEditor<UnitOfMeasure> editor, ILogger logger)
    : LookupListState<UnitOfMeasure>("Unit", u => u.Id, SortExpressionComparer<UnitOfMeasure>.Ascending(u => u.Name), editor, logger)
{
    private string _editingAbbreviation = string.Empty;
    public string EditingAbbreviation
    {
        get => _editingAbbreviation;
        set => RaiseAndSetIfChanged(ref _editingAbbreviation, value);
    }

    protected override string DeleteQuestion(UnitOfMeasure item) => $"Delete '{item.Name}'? This can't be undone.";

    protected override void LoadIntoForm(UnitOfMeasure item)
    {
        EditingName = item.Name;
        EditingAbbreviation = item.Abbreviation;
    }

    protected override UnitOfMeasure FormItem() => new() { Id = EditingId, Name = EditingName, Abbreviation = EditingAbbreviation };

    protected override void ClearFields() => EditingAbbreviation = string.Empty;
}
