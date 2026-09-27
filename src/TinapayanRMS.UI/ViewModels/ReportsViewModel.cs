using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>One point on the price-trend chart: a purchase-unit price on a given date.</summary>
public class PricePoint(DateTime date, decimal price)
{
    public DateTime Date { get; } = date;

    public decimal Price { get; } = price;
}

/// <summary>One bar-group on the margin overview chart — a product's cost and selling price, computed live from today's data, never historized.</summary>
public class MarginRow(string productName, decimal baseCost, decimal sellingPrice)
{
    public string ProductName { get; } = productName;

    public decimal BaseCost { get; } = baseCost;

    public decimal SellingPrice { get; } = sellingPrice;
}

public class ReportsViewModel : ReactiveObject
{
    private readonly IRawMaterialPriceHistoryRepository _priceHistoryRepository;
    private readonly IProductRepository _productRepository;
    private readonly SavedProductReader _savedProducts;

    // True while LoadCommand merges the material list, so re-pointing the selection doesn't also
    // start its own chart reload.
    private bool _isRefreshing;

    /// <summary>The purchased raw materials, ingredients and packaging alike: a made ingredient has no price history.</summary>
    public ObservableCollection<RawMaterial> RawMaterials { get; } = [];

    public ObservableCollection<PricePoint> PricePoints { get; } = [];

    public ObservableCollection<MarginRow> MarginRows { get; } = [];

    private RawMaterial? _selectedRawMaterial;
    public RawMaterial? SelectedRawMaterial
    {
        get => _selectedRawMaterial;
        set => this.RaiseAndSetIfChanged(ref _selectedRawMaterial, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }

    public ReportsViewModel(
        IRawMaterialPriceHistoryRepository priceHistoryRepository,
        IRawMaterialRepository rawMaterialRepository,
        IProductRepository productRepository,
        SavedProductReader savedProducts)
    {
        _priceHistoryRepository = priceHistoryRepository;
        _productRepository = productRepository;
        _savedProducts = savedProducts;

        // Skips while LoadCommand merges, which reloads the chart itself once it's done.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(_ => !_isRefreshing)
            .Select(_ => Observable.FromAsync(LoadPricePointsAsync))
            .Switch()
            .Subscribe();

        // Runs whenever the tab is shown (MainWindow). It merges the material list rather than
        // replacing it, so the chosen material survives, and redraws both charts from current prices.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var rawMaterials = await rawMaterialRepository.GetAllAsync(CancellationToken.None);
            var marginRows = await CalculateMarginRowsAsync();

            var rawMaterialId = SelectedRawMaterial?.Id;

            _isRefreshing = true;
            try
            {
                RawMaterials.MergeFrom([.. rawMaterials.Where(m => m.Source == RawMaterialSource.Purchased).OrderBy(m => m.Name)], m => m.Id);

                // The first one only until something has been chosen.
                SelectedRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == rawMaterialId) ?? RawMaterials.FirstOrDefault();

                MarginRows.Clear();
                MarginRows.AddRange(marginRows);
            }
            finally
            {
                _isRefreshing = false;
            }

            await LoadPricePointsAsync();
        });
    }

    private async Task LoadPricePointsAsync()
    {
        PricePoints.Clear();

        IReadOnlyList<(DateOnly Date, decimal Price)> entries = SelectedRawMaterial is null
            ? []
            : [.. (await _priceHistoryRepository.GetForRawMaterialAsync(SelectedRawMaterial.Id, CancellationToken.None)).Select(h => (h.Date, h.Price))];

        // Repositories return newest-first (for a price-history grid); a trend line needs
        // oldest-first so it reads left to right.
        foreach (var (date, price) in entries.OrderBy(e => e.Date))
        {
            PricePoints.Add(new PricePoint(date.ToDateTime(TimeOnly.MinValue), price));
        }
    }

    /// <summary>
    /// Every product's base cost and selling price as it's saved, priced as the Products screen prices it once opened
    /// (SavedProductReader, which the screen and Print read through too).
    /// </summary>
    private async Task<List<MarginRow>> CalculateMarginRowsAsync()
    {
        var marginRows = new List<MarginRow>();

        var products = await _productRepository.GetAllAsync(CancellationToken.None);
        foreach (var product in products)
        {
            var costs = (await _savedProducts.ReadAsync(product, CancellationToken.None)).Costs;
            marginRows.Add(new MarginRow(product.Name, costs.BaseCost, costs.SellingPrice));
        }

        return marginRows;
    }
}
