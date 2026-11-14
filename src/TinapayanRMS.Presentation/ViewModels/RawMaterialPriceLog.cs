using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>What the Price History tab asks of its screen, on top of what any part of its form asks.</summary>
public interface IRawMaterialPriceLogHost : IFormPartHost
{
    /// <summary>The material whose history shows.</summary>
    RawMaterial? SelectedRawMaterial { get; }

    /// <summary>An entry was deleted: a packaging material with none left may have its base unit changed again.</summary>
    void PricesDeleted();
}

/// <summary>
/// The Raw Materials screen's Price History tab (through RawMaterialsViewModel.PriceLog): a purchased material's price
/// changes, newest first, and Delete for one. The history is only a log, so deleting an entry leaves the material's cost and
/// the form as they are.
/// </summary>
public sealed class RawMaterialPriceLog : ReactiveObject
{
    /// <param name="host">The screen, which asks before an entry is deleted, and follows its base unit lock.</param>
    public RawMaterialPriceLog(IRawMaterialPriceLogHost host, IRawMaterialPriceHistoryRepository priceHistoryRepository)
    {
        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => host.RunWithConfirmationAsync("Delete this price history entry? The material's cost stays as it is. This can't be undone.", async () =>
            {
                await priceHistoryRepository.DeleteAsync(SelectedEntry!.Entry.Id, CancellationToken.None);

                // The history is only a log, so the cost stays and the form keeps its unsaved edits. Only the history reloads.
                var material = host.SelectedRawMaterial!;
                Show(material, await priceHistoryRepository.GetForRawMaterialAsync(material.Id, CancellationToken.None));
                host.PricesDeleted();
            }),
            this.WhenAnyValue(x => x.SelectedEntry).Select(row => row is not null));
    }

    /// <summary>The price history, newest first.</summary>
    public ObservableCollection<RawMaterialPriceRow> Entries { get; } = [];

    private RawMaterialPriceRow? _selectedEntry;
    public RawMaterialPriceRow? SelectedEntry
    {
        get => _selectedEntry;
        set => this.RaiseAndSetIfChanged(ref _selectedEntry, value);
    }

    /// <summary>Deletes the entry picked, once it's confirmed.</summary>
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    /// <summary><paramref name="material"/>'s price history, newest first, in place of what showed; none for no material.</summary>
    public void Show(RawMaterial? material, IReadOnlyList<RawMaterialPriceHistory> entries)
    {
        var baseUnitName = material is null ? string.Empty : BaseUnitText.NameOf(material);
        Entries.Clear();
        Entries.AddRange(entries.Select(e => new RawMaterialPriceRow(e, baseUnitName)));
    }
}
