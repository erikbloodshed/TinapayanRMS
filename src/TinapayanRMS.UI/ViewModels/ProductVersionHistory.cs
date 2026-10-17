using System.Collections.ObjectModel;
using System.Globalization;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// One version on the Products screen's History tab: when it was saved, its note, and its figures as they were then and
/// at today's prices, at the screen's cost basis.
/// </summary>
public sealed class ProductVersionRow : ReactiveObject
{
    private readonly Action<ProductVersionRow, string?> _noteTyped;

    public ProductVersionRow(ProductVersion version, ProductVersionContent content, Action<ProductVersionRow, string?> noteTyped)
    {
        Id = version.Id;
        Number = version.Number;
        SavedAt = version.SavedAt;
        _note = version.Note;
        Content = content;
        _noteTyped = noteTyped;
    }

    public int Id { get; }

    public int Number { get; }

    public DateTime SavedAt { get; }

    /// <summary>"Version 3", what the screen calls it.</summary>
    public string Title => $"Version {Number}";

    public string SavedText => SavedAt.ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture);

    public ProductVersionContent Content { get; }

    public decimal? CurrentPrice => Content.CurrentPrice;

    private string? _note;
    /// <summary>What the user wrote about it. Typed into the table, it's saved as the cell is left.</summary>
    public string? Note
    {
        get => _note;
        set
        {
            var previous = _note;
            this.RaiseAndSetIfChanged(ref _note, value);
            if (!string.Equals(previous ?? string.Empty, value ?? string.Empty, StringComparison.Ordinal))
            {
                _noteTyped(this, previous);
            }
        }
    }

    /// <summary>Puts the note back as it was, without saving it: a note the save refused.</summary>
    public void RevertNote(string? note)
    {
        _note = note;
        this.RaisePropertyChanged(nameof(Note));
    }

    private ProductCostSummary _then = ProductCostSummary.Empty;
    /// <summary>Its costing with its materials at their costs then.</summary>
    public ProductCostSummary Then
    {
        get => _then;
        private set
        {
            this.RaiseAndSetIfChanged(ref _then, value);
            this.RaisePropertyChanged(nameof(BaseCostThen));
            this.RaisePropertyChanged(nameof(SuggestedPriceThen));
        }
    }

    private ProductCostSummary _today = ProductCostSummary.Empty;
    /// <summary>Its costing with its materials at their costs today.</summary>
    public ProductCostSummary Today
    {
        get => _today;
        private set
        {
            this.RaiseAndSetIfChanged(ref _today, value);
            this.RaisePropertyChanged(nameof(BaseCostToday));
            this.RaisePropertyChanged(nameof(SuggestedPriceToday));
        }
    }

    public decimal BaseCostThen => Then.BaseCost;

    public decimal SuggestedPriceThen => Then.SellingPrice;

    public decimal BaseCostToday => Today.BaseCost;

    public decimal SuggestedPriceToday => Today.SellingPrice;

    /// <summary>Works its figures out again, then and today, at <paramref name="basis"/>.</summary>
    internal void Price(
        int productId,
        CostBasis basis,
        IReadOnlyDictionary<int, RawMaterial> materialsNow,
        IReadOnlyDictionary<int, ComponentCosts> componentCostsNow)
    {
        var (product, detail) = ProductVersionSnapshot.ToDetail(productId, Content);
        Then = SavedProductReader.Build(product, detail, basis).Costs;
        var (_, detailNow) = ProductVersionSnapshot.ToDetail(productId, Content, componentCostsNow);
        Today = SavedProductReader.Build(product, detailNow, basis, materialsNow).Costs;
    }
}

/// <summary>
/// One version on the History tab's chart: its base cost, suggested price and current price as they were when it was
/// saved, at the screen's cost basis. Doubles for the chart, which leaves a gap at NaN: a version with no current price.
/// </summary>
public sealed record VersionTrendPoint(int Number, double BaseCost, double SuggestedPrice, double CurrentPrice)
{
    /// <summary>What the chart's axis shows under it: "V3".</summary>
    public string Label => $"V{Number}";
}

/// <summary>
/// One of Compare With's choices, what the picked version's changes are measured against: the version before it
/// (<see cref="Previous"/>), the product as it's saved now (<see cref="Current"/>), or another version
/// (<see cref="VersionId"/>). Equal by value, so the choice picked is found again in the list built for a new read.
/// </summary>
public sealed record VersionComparison(string Label, int? VersionId = null)
{
    public static VersionComparison Previous { get; } = new("The version before it");

    public static VersionComparison Current { get; } = new("The product as saved now");

    public static VersionComparison With(ProductVersionRow row) => new($"{row.Title}, {row.SavedText}", row.Id);

    public override string ToString() => Label;
}

/// <summary>
/// The Products screen's History tab: the product's versions, newest first, the one picked, and what changed in it,
/// against the version before it, the product as it's saved now, or any other version. ProductsViewModel reads the
/// versions and hands them over (<see cref="Show"/>); Restore is the screen's own, since it puts the version into the form.
/// </summary>
public sealed class ProductVersionHistory : ReactiveObject
{
    private static readonly IReadOnlyList<VersionComparison> FixedChoices = [VersionComparison.Previous, VersionComparison.Current];

    private int _productId;
    private CostBasis _basis;
    private ProductVersionContent? _current;
    private IReadOnlyDictionary<int, RawMaterial> _materialsNow = new Dictionary<int, RawMaterial>();
    private IReadOnlyDictionary<int, ComponentCosts> _componentCostsNow = new Dictionary<int, ComponentCosts>();

    public ObservableCollection<ProductVersionRow> Rows { get; } = [];

    /// <summary>Whose versions are showing, or null when none are (none read yet, or a new product).</summary>
    public int? ProductId { get; private set; }

    private ProductVersionRow? _selectedRow;
    public ProductVersionRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRow, value);
            RefreshChanges();
        }
    }

    private IReadOnlyList<VersionComparison> _compareChoices = FixedChoices;
    /// <summary>Compare With's choices: the version before it, the product as saved now, then each version, newest first.</summary>
    public IReadOnlyList<VersionComparison> CompareChoices
    {
        get => _compareChoices;
        private set => this.RaiseAndSetIfChanged(ref _compareChoices, value);
    }

    private VersionComparison _compareWith = VersionComparison.Previous;
    /// <summary>
    /// What the picked version is compared with, picked under the table. Null is ignored: the picker sends it while its
    /// choices are replaced.
    /// </summary>
    public VersionComparison? CompareWith
    {
        get => _compareWith;
        set
        {
            if (value is null)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _compareWith, value);
            RefreshChanges();
        }
    }

    private IReadOnlyList<string> _changes = [];
    /// <summary>What changed, one line each (ProductVersionRules.Changes).</summary>
    public IReadOnlyList<string> Changes
    {
        get => _changes;
        private set => this.RaiseAndSetIfChanged(ref _changes, value);
    }

    private string _changesCaption = string.Empty;
    /// <summary>What <see cref="Changes"/> compares, or why there's nothing to list.</summary>
    public string ChangesCaption
    {
        get => _changesCaption;
        private set => this.RaiseAndSetIfChanged(ref _changesCaption, value);
    }

    private IReadOnlyList<VersionTrendPoint> _trend = [];
    /// <summary>The chart's points, oldest version first: how the product's cost and prices moved from one version to the next.</summary>
    public IReadOnlyList<VersionTrendPoint> Trend
    {
        get => _trend;
        private set
        {
            this.RaiseAndSetIfChanged(ref _trend, value);
            this.RaisePropertyChanged(nameof(IsTrendShown));
            this.RaisePropertyChanged(nameof(IsTrendHintShown));
        }
    }

    /// <summary>The chart shows from the second version on: one point is no trend.</summary>
    public bool IsTrendShown => Trend.Count >= 2;

    /// <summary>Until then, a line in its place says when it will.</summary>
    public bool IsTrendHintShown => !IsTrendShown;

    private bool _isEmpty = true;
    /// <summary>No version to show: the product hasn't been saved since versions began to be kept.</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => this.RaiseAndSetIfChanged(ref _isEmpty, value);
    }

    /// <summary>
    /// Shows <paramref name="rows"/>, newest first, as <paramref name="productId"/>'s, priced at <paramref name="basis"/>.
    /// <paramref name="current"/> is the product as it's saved now; <paramref name="materialsNow"/> and
    /// <paramref name="componentCostsNow"/> price each version at today's costs. The version picked before stays picked.
    /// </summary>
    public void Show(
        int productId,
        IReadOnlyList<ProductVersionRow> rows,
        ProductVersionContent current,
        IReadOnlyDictionary<int, RawMaterial> materialsNow,
        IReadOnlyDictionary<int, ComponentCosts> componentCostsNow,
        CostBasis basis)
    {
        var pickedId = ProductId == productId ? SelectedRow?.Id : null;
        _productId = productId;
        _current = current;
        _materialsNow = materialsNow;
        _componentCostsNow = componentCostsNow;
        _basis = basis;
        foreach (var row in rows)
        {
            row.Price(productId, basis, materialsNow, componentCostsNow);
        }

        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        ProductId = productId;
        IsEmpty = Rows.Count == 0;
        RefreshTrend();
        ShowCompareChoices();
        SelectedRow = Rows.FirstOrDefault(r => r.Id == pickedId) ?? Rows.FirstOrDefault();
    }

    /// <summary>Every version's figures again at <paramref name="basis"/>: the screen switched between buffered and unbuffered costs.</summary>
    public void Reprice(CostBasis basis)
    {
        _basis = basis;
        foreach (var row in Rows)
        {
            row.Price(_productId, basis, _materialsNow, _componentCostsNow);
        }

        RefreshTrend();
    }

    /// <summary>Nothing showing, until the next product's versions are read.</summary>
    public void Clear()
    {
        ProductId = null;
        _current = null;
        Rows.Clear();
        IsEmpty = true;
        RefreshTrend();
        ShowCompareChoices();
        SelectedRow = null;
    }

    private void RefreshTrend() =>
        Trend =
        [
            .. Rows.Reverse().Select(r => new VersionTrendPoint(
                r.Number,
                (double)r.BaseCostThen,
                (double)r.SuggestedPriceThen,
                r.CurrentPrice is { } price ? (double)price : double.NaN)),
        ];

    /// <summary>
    /// The choices for the versions showing. The one picked stays when it's still among them (another version of the
    /// same product), else it's the version before it again.
    /// </summary>
    private void ShowCompareChoices()
    {
        CompareChoices = [.. FixedChoices, .. Rows.Select(VersionComparison.With)];
        _compareWith = CompareChoices.Contains(_compareWith) ? _compareWith : VersionComparison.Previous;
        this.RaisePropertyChanged(nameof(CompareWith));
    }

    private void RefreshChanges()
    {
        if (SelectedRow is not { } row)
        {
            Changes = [];
            ChangesCaption = IsEmpty ? string.Empty : "Pick a version to see what changed in it.";
            return;
        }

        if (_compareWith.VersionId is { } otherId && Rows.FirstOrDefault(r => r.Id == otherId) is { } other)
        {
            if (other == row)
            {
                Changes = [];
                ChangesCaption = $"Pick another version in Compare With to see what's different from {row.Title.ToLowerInvariant()}.";
                return;
            }

            var (older, newer) = other.Number < row.Number ? (other, row) : (row, other);
            Changes = ProductVersionRules.Changes(older.Content, newer.Content);
            ChangesCaption = Changes.Count == 0
                ? $"{older.Title} and {newer.Title.ToLowerInvariant()} have the same recipe and price."
                : $"From {older.Title.ToLowerInvariant()} to {newer.Title.ToLowerInvariant()}:";
            return;
        }

        if (_compareWith == VersionComparison.Current)
        {
            Changes = _current is null ? [] : ProductVersionRules.Changes(row.Content, _current);
            ChangesCaption = Changes.Count == 0
                ? $"The product as saved now has the same recipe and price as {row.Title.ToLowerInvariant()}."
                : $"From {row.Title.ToLowerInvariant()} to the product as saved now:";
            return;
        }

        var index = Rows.IndexOf(row);
        if (index + 1 >= Rows.Count)
        {
            Changes = [];
            ChangesCaption = $"{row.Title} is the first: there's no version before it to compare with.";
            return;
        }

        var previous = Rows[index + 1];
        Changes = ProductVersionRules.Changes(previous.Content, row.Content);
        ChangesCaption = $"From {previous.Title.ToLowerInvariant()} to {row.Title.ToLowerInvariant()}:";
    }
}
