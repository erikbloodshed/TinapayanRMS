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
/// The Products screen's History tab: the product's versions, newest first, the one picked, and what changed in it,
/// against the version before it or against the product as it's saved now. ProductsViewModel reads the versions and
/// hands them over (<see cref="Show"/>); Restore is the screen's own, since it puts the version into the form.
/// </summary>
public sealed class ProductVersionHistory : ReactiveObject
{
    /// <summary>What a version's changes are measured against: the version before it, or the product now.</summary>
    public static IReadOnlyList<string> CompareChoices { get; } = ["The version before it", "The product as saved now"];

    public const int CompareWithPrevious = 0;

    public const int CompareWithCurrent = 1;

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

    private int _compareIndex;
    /// <summary><see cref="CompareWithPrevious"/> or <see cref="CompareWithCurrent"/>, picked under the table.</summary>
    public int CompareIndex
    {
        get => _compareIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _compareIndex, value);
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
    }

    /// <summary>Nothing showing, until the next product's versions are read.</summary>
    public void Clear()
    {
        ProductId = null;
        _current = null;
        Rows.Clear();
        IsEmpty = true;
        SelectedRow = null;
    }

    private void RefreshChanges()
    {
        if (SelectedRow is not { } row)
        {
            Changes = [];
            ChangesCaption = IsEmpty ? string.Empty : "Pick a version to see what changed in it.";
            return;
        }

        if (CompareIndex == CompareWithCurrent)
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
