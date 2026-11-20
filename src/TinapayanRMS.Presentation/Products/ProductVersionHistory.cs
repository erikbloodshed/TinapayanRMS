using System.Collections.ObjectModel;
using System.Globalization;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing.Versions;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.Products;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// One version on the Products screen's History tab: when it was saved, its note, and its figures as they were then and
/// at today's prices, at the screen's cost basis.
/// </summary>
public sealed class ProductVersionRow : ObservableRow
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
            RaiseAndSetIfChanged(ref _note, value);
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
        RaisePropertyChanged(nameof(Note));
    }

    private ProductCostSummary _then = ProductCostSummary.Empty;
    /// <summary>Its costing with its materials at their costs then.</summary>
    public ProductCostSummary Then
    {
        get => _then;
        private set
        {
            RaiseAndSetIfChanged(ref _then, value);
            RaisePropertyChanged(nameof(BaseCostThen));
            RaisePropertyChanged(nameof(SuggestedPriceThen));
        }
    }

    private ProductCostSummary _today = ProductCostSummary.Empty;
    /// <summary>Its costing with its materials at their costs today.</summary>
    public ProductCostSummary Today
    {
        get => _today;
        private set
        {
            RaiseAndSetIfChanged(ref _today, value);
            RaisePropertyChanged(nameof(BaseCostToday));
            RaisePropertyChanged(nameof(SuggestedPriceToday));
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
        IReadOnlyDictionary<int, ComponentCosts> componentCostsNow,
        decimal wastagePercentNow,
        decimal vatPercentNow)
    {
        var (product, detail) = ProductVersionSnapshot.ToDetail(productId, Content);
        Then = SavedProductReader.Build(product, detail, basis).Costs;
        var (_, detailNow) = ProductVersionSnapshot.ToDetail(productId, Content, componentCostsNow, wastagePercentNow, vatPercentNow);
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
/// A version as History's Restore puts it into the form (<see cref="ProductVersionHistory.RestoreCommand"/>): its
/// sections, packaging, products, costing, quantity required and price, worked out against what the screen has now, for
/// the screen to put in place as one undo step (<see cref="IProductVersionHistoryHost.ApplyVersion"/>). The name,
/// description, category and stage aren't in it: a restore leaves them as they are.
/// </summary>
public sealed record RestoredVersion(
    bool IsComposite,
    IReadOnlyList<ProductUsageEdit> Sections,
    PackagingLinkTarget? PackagingLink,
    IReadOnlyList<PackagingLineSnapshot> PackagingLines,
    IReadOnlyList<PackagingLineSnapshot> BulkLines,
    int? BulkQuantity,
    IReadOnlyList<ComponentLineSnapshot> Components,
    (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin) Costing,
    int QuantityRequired,
    decimal? CurrentPrice,
    bool IncludesComponentPackaging);

/// <summary>
/// What the History tab (<see cref="ProductVersionHistory"/>) asks of the Products screen, besides its runners and
/// messages: the product showing, the costs it shows, its own sections as saved and the form, which a restored version is
/// put into. The screen implements it explicitly.
/// </summary>
public interface IProductVersionHistoryHost : IScreenPartHost
{
    /// <summary>The product the form shows, once its details have arrived; null for a new product, or while they're on their way.</summary>
    Product? ProductShowing { get; }

    /// <summary>Which of the materials' costs the screen shows, buffered or not: what a version's figures are priced at.</summary>
    CostBasis CostBasis { get; }

    /// <summary>The sub-assemblies of the product's own sections as saved: a version's section of its own still among them keeps its sub-assembly.</summary>
    IReadOnlySet<int> OwnedSectionIds { get; }

    /// <summary>The Packaging tab, whose lines a version that links the same packaging keeps as they show.</summary>
    ProductPackaging Packaging { get; }

    /// <summary>What a linked sub-assembly goes by beside its role in this product's recipe ("Ube" for "Cheese Roll Filling: Ube").</summary>
    string? SectionLabelOf(string subAssemblyName, SubAssemblyRole? role);

    /// <summary>Puts <paramref name="version"/> into the form as one undo step, the section form closed, as Undo can take back.</summary>
    void ApplyVersion(RestoredVersion version);
}

/// <summary>
/// The Products screen's History tab: the product's versions, newest first, the one picked, and what changed in it,
/// against the version before it, the product as it's saved now, or any other version. It reads them itself
/// (<see cref="ReadAsync"/>, which the screen asks for while the tab shows), saves the notes typed into them, works out
/// what Restore puts into the form, which the screen puts there (<see cref="IProductVersionHistoryHost.ApplyVersion"/>),
/// and puts the version picked on paper (<see cref="PrintoutOf"/>).
/// </summary>
public sealed class ProductVersionHistory : ObservableState
{
    private static readonly IReadOnlyList<VersionComparison> FixedChoices = [VersionComparison.Previous, VersionComparison.Current];

    private readonly IProductVersionHistoryHost _host;
    private readonly IReadOnlyList<Product> _products;
    private readonly IReadOnlyList<RawMaterial> _ingredients;
    private readonly IReadOnlyList<RawMaterial> _packagingMaterials;
    private readonly IReadOnlyList<SubAssemblyRole> _roles;
    private readonly IProductQueries _queries;
    private readonly IProductEditor _editor;

    private int _productId;
    private CostBasis _basis;
    private ProductVersionContent? _current;

    /// <summary>The shop-wide wastage as it is now, which the product as it's saved now was read with: what today's figures add.</summary>
    private decimal WastageNow => _current?.WastagePercent ?? 0m;

    /// <summary>The shop's VAT as it is now, which the product as it's saved now was read with: what today's figures add.</summary>
    private decimal VatNow => _current?.VatThen ?? 0m;
    private IReadOnlyDictionary<int, RawMaterial> _materialsNow = new Dictionary<int, RawMaterial>();
    private IReadOnlyDictionary<int, ComponentCosts> _componentCostsNow = new Dictionary<int, ComponentCosts>();

    /// <param name="host">The Products screen: the product showing, its runners and messages, and its form.</param>
    /// <param name="products">Every product in use, as the screen last loaded them: a version's products, and another product its packaging linked.</param>
    /// <param name="ingredients">The ingredients as the screen last loaded them, at today's costs.</param>
    /// <param name="packagingMaterials">The packaging materials as the screen last loaded them, at today's costs.</param>
    /// <param name="roles">The sub-assembly roles as the screen last loaded them.</param>
    /// <param name="queries">What reads the versions, the product as saved now, and what a version linked as it is now.</param>
    /// <param name="editor">What saves a version's note.</param>
    public ProductVersionHistory(
        IProductVersionHistoryHost host,
        IReadOnlyList<Product> products,
        IReadOnlyList<RawMaterial> ingredients,
        IReadOnlyList<RawMaterial> packagingMaterials,
        IReadOnlyList<SubAssemblyRole> roles,
        IProductQueries queries,
        IProductEditor editor)
    {
        _host = host;
        _products = products;
        _ingredients = ingredients;
        _packagingMaterials = packagingMaterials;
        _roles = roles;
        _queries = queries;
        _editor = editor;

        // Restore puts the picked version into the form, as Revert puts the saved product there, but as an undo step of
        // unsaved edits rather than by reloading: Save keeps it, and Undo takes it back.
        RestoreCommand = new ScreenCommand(
            () => host.RunSafelyAsync(() => SelectedRow is { } row ? RestoreAsync(row) : Task.CompletedTask),
            () => SelectedRow is not null);
    }

    /// <summary>
    /// Restore: the picked version's recipe, packaging, costing and price into the form, as unsaved edits in one undo step
    /// (<see cref="RestoreAsync"/>).
    /// </summary>
    public ScreenCommand RestoreCommand { get; }

    public ObservableCollection<ProductVersionRow> Rows { get; } = [];

    /// <summary>Whose versions are showing, or null when none are (none read yet, or a new product).</summary>
    public int? ProductId { get; private set; }

    private ProductVersionRow? _selectedRow;
    public ProductVersionRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            RaiseAndSetIfChanged(ref _selectedRow, value);
            RefreshChanges();
        }
    }

    private IReadOnlyList<VersionComparison> _compareChoices = FixedChoices;
    /// <summary>Compare With's choices: the version before it, the product as saved now, then each version, newest first.</summary>
    public IReadOnlyList<VersionComparison> CompareChoices
    {
        get => _compareChoices;
        private set => RaiseAndSetIfChanged(ref _compareChoices, value);
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

            RaiseAndSetIfChanged(ref _compareWith, value);
            RefreshChanges();
        }
    }

    private IReadOnlyList<string> _changes = [];
    /// <summary>
    /// What changed, one line each: the product's changes (ProductVersionRules.Changes), then the shop's figures it was priced
    /// with that changed between them (ProductVersionRules.ShopChanges), which move its price too.
    /// </summary>
    public IReadOnlyList<string> Changes
    {
        get => _changes;
        private set => RaiseAndSetIfChanged(ref _changes, value);
    }

    private static IReadOnlyList<string> ChangesBetween(ProductVersionContent older, ProductVersionContent newer) =>
        [.. ProductVersionRules.Changes(older, newer), .. ProductVersionRules.ShopChanges(older, newer)];

    private string _changesCaption = string.Empty;
    /// <summary>What <see cref="Changes"/> compares, or why there's nothing to list.</summary>
    public string ChangesCaption
    {
        get => _changesCaption;
        private set => RaiseAndSetIfChanged(ref _changesCaption, value);
    }

    private IReadOnlyList<VersionTrendPoint> _trend = [];
    /// <summary>The chart's points, oldest version first: how the product's cost and prices moved from one version to the next.</summary>
    public IReadOnlyList<VersionTrendPoint> Trend
    {
        get => _trend;
        private set
        {
            RaiseAndSetIfChanged(ref _trend, value);
            RaisePropertyChanged(nameof(IsTrendShown));
            RaisePropertyChanged(nameof(IsTrendHintShown));
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
        private set => RaiseAndSetIfChanged(ref _isEmpty, value);
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
            row.Price(productId, basis, materialsNow, componentCostsNow, WastageNow, VatNow);
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
            row.Price(_productId, basis, _materialsNow, _componentCostsNow, WastageNow, VatNow);
        }

        RefreshTrend();
    }

    /// <summary>
    /// Reads <paramref name="productId"/>'s versions and shows them (<see cref="Show"/>), each priced then and today: the
    /// versions, the product as it's saved now (what a version is compared with), and its products' costs now, read off the
    /// UI thread. Shows nothing when <paramref name="stillWanted"/> says the screen has moved on meanwhile.
    /// </summary>
    public async Task ReadAsync(int productId, Func<bool> stillWanted, CancellationToken cancellationToken)
    {
        if (await Task.Run(() => _queries.ReadHistoryAsync(productId, cancellationToken), cancellationToken) is not { } history)
        {
            return;
        }

        List<(ProductVersion Version, ProductVersionContent Content)> contents =
            [.. history.Versions.Select(v => (v, ProductVersionContent.FromJson(v.Content)))];
        IReadOnlyCollection<int> componentIds = [.. contents.SelectMany(c => c.Content.Components.Select(p => p.ProductId)).Distinct()];
        var componentCostsNow = await Task.Run(() => _queries.ReadComponentCostsAsync(componentIds, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // The product picked since, or the tab left: what was read is someone else's, or not wanted.
        if (!stillWanted())
        {
            return;
        }

        Show(
            productId,
            [.. contents.Select(c => new ProductVersionRow(c.Version, c.Content, NoteTyped))],
            ProductVersionSnapshot.From(history.Product, history.Detail),
            MaterialsNow(),
            componentCostsNow,
            _host.CostBasis);
    }

    /// <summary>Today's materials, the screen's own, by id: what today's figures price a version at.</summary>
    private Dictionary<int, RawMaterial> MaterialsNow()
    {
        Dictionary<int, RawMaterial> materialsNow = [];
        foreach (var material in _ingredients.Concat(_packagingMaterials))
        {
            materialsNow.TryAdd(material.Id, material);
        }

        return materialsNow;
    }

    /// <summary>A note typed into the table, saved as the cell is left; one refused goes back as it was.</summary>
    private void NoteTyped(ProductVersionRow row, string? previous) =>
        _ = _host.RunSafelyAsync(async () =>
        {
            try
            {
                await _editor.SetVersionNoteAsync(row.Id, row.Note, CancellationToken.None);
                var stored = ProductVersionRules.NormalizeNote(row.Note);
                if (!string.Equals(stored, row.Note, StringComparison.Ordinal))
                {
                    row.RevertNote(stored);
                }
            }
            catch (Exception)
            {
                row.RevertNote(previous);
                throw;
            }
        });

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
        RaisePropertyChanged(nameof(CompareWith));
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
            Changes = ChangesBetween(older.Content, newer.Content);
            ChangesCaption = Changes.Count == 0
                ? $"{older.Title} and {newer.Title.ToLowerInvariant()} have the same recipe and price."
                : $"From {older.Title.ToLowerInvariant()} to {newer.Title.ToLowerInvariant()}:";
            return;
        }

        if (_compareWith == VersionComparison.Current)
        {
            Changes = _current is null ? [] : ChangesBetween(row.Content, _current);
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
        Changes = ChangesBetween(previous.Content, row.Content);
        ChangesCaption = $"From {previous.Title.ToLowerInvariant()} to {row.Title.ToLowerInvariant()}:";
    }

    /// <summary>
    /// Puts <paramref name="row"/>'s version into the form as unsaved edits, in one undo step: its sections, packaging,
    /// products, costing percentages, quantity required, current price and whether a composite includes its products'
    /// packaging. The name, description, category and stage stay. A section of the product's own keeps its sub-assembly
    /// when the product still owns it; a linked one is linked again as it is now, since Save never writes a version's lines
    /// into a sub-assembly others may share; one deleted since comes back as a new section with the version's lines (its
    /// add-ins among them). Packaging links what it linked while that's still there, else takes the lines it showed as the
    /// product's own. Refused, with nothing changed, while a material, role or product it needs is gone.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1502:Avoid excessive complexity", Justification = "Baseline of 2026-11-20 (ADR 0004): moves to a use case and a draft as its screen is migrated (ADR 0002).")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "Baseline of 2026-11-20 (ADR 0004): moves to a use case and a draft as its screen is migrated (ADR 0002).")]
    private async Task RestoreAsync(ProductVersionRow row)
    {
        if (_host.ProductShowing is not { } product || ProductId != product.Id)
        {
            return;
        }

        var content = row.Content;
        if (content.IsComposite != product.IsComposite)
        {
            throw new BusinessRuleException($"{row.Title} is of another kind of product, so it can't be restored here.");
        }

        // What it linked, as it is now: its linked sections' sub-assemblies and its packaging set, and their lines.
        IReadOnlyCollection<int> linkedIds =
        [
            .. content.Sections.Where(s => !s.IsOwn).Select(s => s.SubAssemblyId)
                .Concat(content.PackagingLink?.SubAssemblyId is { } linkedSetId ? [linkedSetId] : Enumerable.Empty<int>())
                .Distinct(),
        ];
        var linkedNow = await _queries.ReadLinkedSectionsAsync(linkedIds, CancellationToken.None);
        var subAssembliesNow = linkedNow.SubAssemblies.ToDictionary(s => s.Id);
        var linesNow = linkedNow.Lines.ToLookup(l => l.SubAssemblyId);

        PackagingLinkTarget? link = null;
        if (content.PackagingLink is { } versionLink)
        {
            if (versionLink.SubAssemblyId is { } setId && subAssembliesNow.TryGetValue(setId, out var set))
            {
                link = new PackagingLinkTarget.Set(setId, set.Name);
            }
            else if (versionLink.ProductId is { } linkedProductId && _products.FirstOrDefault(p => p.Id == linkedProductId) is { } linkedProduct)
            {
                link = new PackagingLinkTarget.OtherProduct(linkedProductId, linkedProduct.Name);
            }
        }

        // Everything it needs that's gone, named, before anything changes.
        var materialNames = content.Materials.ToDictionary(m => m.Id, m => m.Name);
        string MaterialName(int id) => materialNames.GetValueOrDefault(id) ?? $"Material {id}";
        var ingredientsById = _ingredients.ToDictionary(m => m.Id);
        var packagingById = _packagingMaterials.ToDictionary(m => m.Id);
        bool IsRelinked(VersionSection section) => !section.IsOwn && subAssembliesNow.ContainsKey(section.SubAssemblyId);
        List<string> missing = [];
        foreach (var section in content.Sections)
        {
            var relinked = IsRelinked(section);
            if (!relinked && _roles.All(r => r.Id != section.Role.Id))
            {
                missing.Add($"the {section.Role.Name} role");
            }

            missing.AddRange((relinked ? Enumerable.Empty<int>() : section.Lines.Select(l => l.IngredientId))
                .Concat(section.AddIns.Select(a => a.IngredientId))
                .Where(id => !ingredientsById.ContainsKey(id))
                .Select(MaterialName));
        }

        // The lines it owned, unless a link that's still there shows them, and its bulk packaging, its own either way.
        missing.AddRange(content.PackagingLines
            .Where(l => (link is null || l.IsBulk) && !packagingById.ContainsKey(l.MaterialId))
            .Select(l => MaterialName(l.MaterialId)));

        missing.AddRange(content.Components.Where(c => _products.All(p => p.Id != c.ProductId)).Select(c => c.ProductName));
        if (missing.Distinct().ToList() is { Count: > 0 } gone)
        {
            throw new BusinessRuleException(
                $"{row.Title} can't be restored: it needs {string.Join(", ", gone)}, which {(gone.Count == 1 ? "is" : "are")} no longer there.");
        }

        // The lines a link shows: those showing already when it's the link the form has, else as it has them now. The bulk
        // packaging is the version's own, whatever it linked.
        IReadOnlyList<PackagingLineSnapshot> packagingLines;
        IReadOnlyList<PackagingLineSnapshot> bulkLines = [.. content.PackagingLines.Where(l => l.IsBulk).Select(l => OwnPackagingLineOf(l, content, packagingById))];
        if (link is null)
        {
            packagingLines = [.. content.PackagingLines.Where(l => !l.IsBulk).Select(l => OwnPackagingLineOf(l, content, packagingById))];
        }
        else if (link.Key == _host.Packaging.LinkTarget?.Key)
        {
            packagingLines = [.. _host.Packaging.LineEdits.Select(PackagingLineSnapshot.Of)];
        }
        else
        {
            packagingLines = link switch
            {
                PackagingLinkTarget.Set(var setId, _) => [.. (await Task.Run(() => _queries.ReadPackagingSetLinesAsync(setId, CancellationToken.None)))
                    .Select(l => new PackagingLineSnapshot(l.PackagingMaterialId, l.UnitId, l.QtyPerSet))],
                PackagingLinkTarget.OtherProduct(var linkedProductId, _) => [.. (await Task.Run(() => _queries.ReadEffectivePackagingAsync(linkedProductId, CancellationToken.None)))
                    .Select(l => PackagingLineRow.FromSaved(l))
                    .Select(PackagingLineSnapshot.Of)],
            };
        }

        // Another product picked while that was read: the version isn't this one's.
        if (_host.ProductShowing?.Id != product.Id)
        {
            return;
        }

        var ownedNow = _host.OwnedSectionIds;
        List<string> changedSinceLinks = [];
        List<ProductUsageEdit> sections = [];
        foreach (var section in content.Sections)
        {
            if (IsRelinked(section))
            {
                var linked = subAssembliesNow[section.SubAssemblyId];
                var current = linesNow[linked.Id].ToList();
                if (!HasSameLines(current, section.Lines))
                {
                    changedSinceLinks.Add(linked.Name);
                }

                var linkedRole = _roles.FirstOrDefault(r => r.Id == linked.RoleId) ?? linked.Role;
                sections.Add(new ProductUsageEdit(
                    linked.Id,
                    linkedRole,
                    [.. current.Select(l => new SectionLine(
                        ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    section.AuthoredField,
                    section.AuthoredValue,
                    linked.Name,
                    [.. section.AddIns.Select(a => new SectionLine(ingredientsById[a.IngredientId], a.Grams))],
                    _host.SectionLabelOf(linked.Name, linkedRole),
                    linkedOwner: PricedSection.LinkedOwnerOf(linked)));
                continue;
            }

            // The product's own, or a linked one deleted since: the version's lines, and a linked one's add-ins with them,
            // since a section of its own has none.
            var keepsId = section.IsOwn && ownedNow.Contains(section.SubAssemblyId);
            sections.Add(new ProductUsageEdit(
                keepsId ? section.SubAssemblyId : 0,
                _roles.First(r => r.Id == section.Role.Id),
                [
                    .. section.Lines.Select(l => new SectionLine(ingredientsById[l.IngredientId], l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)),
                    .. section.AddIns.Select(a => new SectionLine(ingredientsById[a.IngredientId], a.Grams)),
                ],
                section.AuthoredField,
                section.AuthoredValue,
                linkedName: null,
                name: section.LabelIn(content.ProductName, content.Schema),
                isOwn: section.IsOwn && !keepsId));
        }

        sections.Sort(ProductUsageEdit.RecipeOrder);
        var costing = content.Costing ?? new VersionCosting(0m, 0m, 0m, 0m);
        _host.ApplyVersion(new RestoredVersion(
            product.IsComposite,
            sections,
            link,
            packagingLines,
            bulkLines,
            bulkLines.Count > 0 ? content.BulkPackagingQuantity : null,
            [.. content.Components.Select(c => new ComponentLineSnapshot(c.ProductId, c.Quantity))],
            (costing.LaborPercent, costing.OverheadPercent, costing.AdministrativePercent, costing.MarginPercent),
            content.QuantityRequired,
            content.CurrentPrice,
            content.IncludesComponentPackaging));

        List<string> notes = [$"{row.Title} is in the form. Save to keep it, or Undo to put the form back."];
        if (changedSinceLinks.Count > 0)
        {
            notes.Add($"{string.Join(", ", changedSinceLinks)} {(changedSinceLinks.Count == 1 ? "has" : "have")} changed on Sub-Assemblies since, and {(changedSinceLinks.Count == 1 ? "is" : "are")} linked as {(changedSinceLinks.Count == 1 ? "it is" : "they are")} now.");
        }

        if (content.PackagingLink is { } wasLinked && link is null)
        {
            notes.Add($"{wasLinked.Name} is gone, so the packaging it showed comes back as the product's own.");
        }

        _host.ShowSuccess(string.Join(" ", notes));
    }

    /// <summary>
    /// A version's packaging line as the product's own: in the unit it was in, or in base units when the material has lost
    /// that unit since.
    /// </summary>
    private static PackagingLineSnapshot OwnPackagingLineOf(
        VersionPackagingLine line, ProductVersionContent content, Dictionary<int, RawMaterial> packagingById)
    {
        if (line.UnitId is { } unitId && packagingById[line.MaterialId].Units.All(u => u.Id != unitId))
        {
            var baseUnitsPerUnit = content.Materials.FirstOrDefault(m => m.Id == line.MaterialId)?.Units.FirstOrDefault(u => u.Id == unitId)?.BaseUnitsPerUnit ?? 1m;
            return new PackagingLineSnapshot(line.MaterialId, null, UnitConversionRules.ToBaseQuantity(line.QtyPerUnit, baseUnitsPerUnit));
        }

        return new PackagingLineSnapshot(line.MaterialId, line.UnitId, line.QtyPerUnit);
    }

    /// <summary>Whether a sub-assembly's lines now are the ones a version saw: the same ingredients, grams and base ticks, in order.</summary>
    private static bool HasSameLines(List<SubAssemblyLine> now, IReadOnlyList<VersionLine> then) =>
        now.Count == then.Count
        && now.Zip(then).All(pair =>
            pair.First.IngredientId == pair.Second.IngredientId
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase);

    /// <summary>
    /// <paramref name="row"/>'s version on paper, for Print on the History tab: its sections, packaging and costing as that
    /// Save left them, priced at its costs then, as the table's Then columns are, at the screen's cost basis. Titled with the
    /// version, dated when it was saved, and marked in the footer as from the history; the category and stage are
    /// <paramref name="product"/>'s now, which a version doesn't keep.
    /// </summary>
    public ProductPrintout PrintoutOf(ProductVersionRow row, Product product)
    {
        var (versionProduct, detail) = ProductVersionSnapshot.ToDetail(product.Id, row.Content);
        return SavedProductReader.Build(versionProduct, detail, _host.CostBasis).ToPrintout(DateTime.Now) with
        {
            ProductName = $"{row.Content.ProductName} ({row.Title})",
            CategoryName = product.Category?.Name ?? ProductRules.UncategorizedTitle,
            StageTitle = ProductRules.StageTitle(product.Stage),
            LastUpdatedText = row.SavedText,
            SaveNote = ProductPrintout.VersionNote,
        };
    }
}
