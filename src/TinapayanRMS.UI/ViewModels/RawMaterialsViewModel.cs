using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A row in the Alternative Units grid and a box in the Unit Conversion Overview: one of the form's
/// alternative units, already saved (its Unit has an Id) or not yet (Id 0). Typed straight into: the grid's
/// unit cell sets <see cref="UnitOfMeasureId"/>, which the row resolves to its unit of measure, and the multiplier
/// cell sets <see cref="BaseUnitsPerUnit"/>. Both write through to <see cref="Unit"/>, which Save writes. A row can be
/// half filled in; Save checks them all first.
/// </summary>
public sealed class RawMaterialUnitRow : ReactiveObject
{
    /// <summary>Looks up a unit of measure by id as the screen has it.</summary>
    private readonly Func<int, UnitOfMeasure?>? _findUnitOfMeasure;

    /// <summary>An empty row, for the grid to compare an add row against (controls:GridAddNewRow.DropWhenUntouched).</summary>
    public RawMaterialUnitRow()
        : this(new RawMaterialUnit(), string.Empty, findUnitOfMeasure: null)
    {
    }

    /// <param name="findUnitOfMeasure">Resolves a picked unit of measure, so a row names its unit as soon as it's picked, even in the add row.</param>
    public RawMaterialUnitRow(RawMaterialUnit unit, string baseUnitAbbreviation, Func<int, UnitOfMeasure?>? findUnitOfMeasure)
    {
        Unit = unit;
        _baseUnitAbbreviation = baseUnitAbbreviation;
        _findUnitOfMeasure = findUnitOfMeasure;
    }

    public RawMaterialUnit Unit { get; }

    /// <summary>The unit of measure picked in the grid; null until one is.</summary>
    public int? UnitOfMeasureId
    {
        get => Unit.UnitOfMeasureId > 0 ? Unit.UnitOfMeasureId : null;
        set
        {
            if (value == UnitOfMeasureId)
            {
                return;
            }

            Unit.UnitOfMeasureId = value ?? 0;
            Unit.UnitOfMeasure = value is { } id ? _findUnitOfMeasure?.Invoke(id) : null;
            this.RaisePropertyChanged();
            RaiseUnitOfMeasureChanged();
        }
    }

    /// <summary>Points the row at the screen's current copy of its unit of measure, so one renamed in Settings shows its new name.</summary>
    public void RefreshUnitOfMeasure()
    {
        if (UnitOfMeasureId is { } id && _findUnitOfMeasure?.Invoke(id) is { } refreshed && !ReferenceEquals(refreshed, Unit.UnitOfMeasure))
        {
            Unit.UnitOfMeasure = refreshed;
            RaiseUnitOfMeasureChanged();
        }
    }

    public string UnitName => Unit.UnitOfMeasure?.Name ?? "?";

    /// <summary>Empty until a unit is picked, so the add row stays blank.</summary>
    public string Abbreviation => Unit.UnitOfMeasure?.Abbreviation ?? string.Empty;

    /// <summary>How many base units one of this unit holds: 25,000 for a 25 kg bag of an ingredient.</summary>
    public decimal BaseUnitsPerUnit
    {
        get => Unit.BaseUnitsPerUnit;
        set
        {
            if (value == Unit.BaseUnitsPerUnit)
            {
                return;
            }

            Unit.BaseUnitsPerUnit = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(BaseEquivalentText));
        }
    }

    private string _baseUnitAbbreviation;
    /// <summary>The base unit's abbreviation, which a new packaging material can still change.</summary>
    public string BaseUnitAbbreviation
    {
        get => _baseUnitAbbreviation;
        set
        {
            this.RaiseAndSetIfChanged(ref _baseUnitAbbreviation, value);
            this.RaisePropertyChanged(nameof(BaseEquivalentText));
        }
    }

    /// <summary>E.g. "25,000 g" or "1,000 cm".</summary>
    public string BaseEquivalentText => $"{Unit.BaseUnitsPerUnit:#,0.####} {BaseUnitAbbreviation}";

    private void RaiseUnitOfMeasureChanged()
    {
        this.RaisePropertyChanged(nameof(UnitName));
        this.RaisePropertyChanged(nameof(Abbreviation));
    }
}

/// <summary>A row in the Price History grid: the entry, the unit it was priced in, and what that comes to per base unit.</summary>
public sealed class RawMaterialPriceRow(RawMaterialPriceHistory entry, string baseUnitName)
{
    public RawMaterialPriceHistory Entry { get; } = entry;

    public DateOnly Date => Entry.Date;

    public string UnitName => Entry.Unit?.UnitOfMeasure?.Name ?? baseUnitName;

    public string Brand => Entry.Brand ?? string.Empty;

    public decimal Price => Entry.Price;

    public decimal CostPerBaseUnit => UnitConversionRules.CostPerBaseUnit(Entry.Price, Entry.Unit?.BaseUnitsPerUnit);
}

/// <summary>What the Usage Example worked out: the calculated (true) cost, and that with the material's buffer on top.</summary>
public sealed record UsageExampleResult(
    string Title,
    decimal QuantityInBaseUnits,
    string BaseUnitAbbreviation,
    decimal CostPerBaseUnit,
    decimal TotalCost,
    decimal BufferPercent,
    decimal BufferedTotalCost);

/// <summary>
/// The Raw Materials screen: ingredients and packaging in one list. A material's type decides which
/// parts of its details apply — an ingredient is measured in grams and can be made from a BOM, a
/// packaging material has a base unit of its own and is always purchased. Its details, alternative
/// units, brand prices and BOM are all edited in memory, and Save writes them together.
/// </summary>
public sealed class RawMaterialsViewModel : ViewModelBase, IUndoScreen, IDisposable, IBrandPriceRowHost
{
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IRawMaterialPriceHistoryRepository _priceHistoryRepository;
    private readonly IRawMaterialBrandPriceRepository _brandPriceRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly IRawMaterialCategoryRepository _categoryRepository;
    private readonly IValidator<RawMaterialCategory> _categoryValidator;
    private readonly IValidator<RawMaterial> _rawMaterialValidator;
    private readonly IValidator<RawMaterialUnit> _unitValidator;
    private readonly IValidator<RawMaterialBrandPrice> _brandPriceValidator;
    private readonly SourceCache<RawMaterial, int> _rawMaterialCache = new(m => m.Id);
    private readonly ReadOnlyObservableCollection<RawMaterial> _rawMaterials;
    private readonly ReadOnlyObservableCollection<RawMaterial> _visibleRawMaterials;
    private readonly ReadOnlyObservableCollection<RawMaterial> _ingredients;
    private readonly ReadOnlyObservableCollection<RawMaterial> _linkTargets;
    private readonly ReadOnlyObservableCollection<RawMaterial> _bomComponentOptions;
    private readonly SourceCache<RawMaterialCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categories;
    private readonly SourceCache<RawMaterialCategory, int> _addNewCategoryOption = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categoryOptions;

    /// <summary>Every raw material, for the usage example's picker.</summary>
    public ReadOnlyObservableCollection<RawMaterial> RawMaterials => _rawMaterials;

    /// <summary>The materials the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<RawMaterial> VisibleRawMaterials => _visibleRawMaterials;

    /// <summary>
    /// The ingredients only: packaging never goes in a recipe. Resolves a BOM line's IngredientId and a saved
    /// LinkedToId back to their material; a picker that shouldn't offer the material being edited uses
    /// BomComponentOptions or LinkTargets instead.
    /// </summary>
    public ReadOnlyObservableCollection<RawMaterial> Ingredients => _ingredients;

    /// <summary>The ingredients a linked ingredient can be linked to: every one but the material being edited.</summary>
    public ReadOnlyObservableCollection<RawMaterial> LinkTargets => _linkTargets;

    /// <summary>The Bill of Materials component picker's list: every ingredient but the made one being edited — a made ingredient can't be its own component.</summary>
    public ReadOnlyObservableCollection<RawMaterial> BomComponentOptions => _bomComponentOptions;

    public ObservableCollection<RawMaterialPriceRow> PriceHistory { get; } = [];

    public ObservableCollection<IngredientLineRow> BomLineEdits { get; } = [];

    /// <summary>The line the BOM table's add row edits: it prices itself as its component is picked, before it's committed.</summary>
    public Func<IngredientLineRow> NewBomLine => () => new IngredientLineRow(id => Ingredients.FirstOrDefault(m => m.Id == id));

    public ReadOnlyObservableCollection<RawMaterialCategory> Categories => _categories;

    /// <summary>
    /// The Category picker's "&lt; Add New .. &gt;" row: a category-shaped stand-in for adding one, not a category
    /// itself. Its Id tells it apart from a saved category (a positive Id) and an unsaved one (0), and it's kept out
    /// of Categories, so the lookups and the refresh merge that work over real categories never see it.
    /// </summary>
    public const int AddNewCategoryId = -1;

    public static readonly RawMaterialCategory AddNewCategory = new() { Id = AddNewCategoryId, Name = "< Add New .. >" };

    /// <summary>What the Category picker lists: the categories of the material's type, with the "&lt; Add New .. &gt;" row above them.</summary>
    public ReadOnlyObservableCollection<RawMaterialCategory> CategoryOptions => _categoryOptions;

    /// <summary>Every unit of measure, for the base unit picker and the Alternative Units grid's unit cell.</summary>
    public ObservableCollection<UnitOfMeasure> UnitsOfMeasure { get; } = [];

    /// <summary>The form's alternative units, saved or not. Their units are the form's own copies, edited in place.</summary>
    public ObservableCollection<RawMaterialUnitRow> UnitRows { get; } = [];

    /// <summary>The Purchase Cost Setup grid's unit choices: the base unit, then every unit of measure it can be bought in.</summary>
    public ObservableCollection<PurchaseUnitChoice> PurchaseUnitOptions { get; } = [];

    public ObservableCollection<RawMaterialUnitOption> ExampleUnitOptions { get; } = [];

    public IReadOnlyList<RawMaterialType> Types { get; } = Enum.GetValues<RawMaterialType>();

    public IReadOnlyList<RawMaterialSource> Sources { get; } = Enum.GetValues<RawMaterialSource>();

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    private string _categorySearchText = string.Empty;
    /// <summary>
    /// What's typed into the Category picker (bound to its Text). Renames the "&lt; Add New .. &gt;" row to include
    /// it, so the row always survives the picker's own AutoComplete filtering — see the subscription in the
    /// constructor that reacts to this.
    /// </summary>
    public string CategorySearchText
    {
        get => _categorySearchText;
        set => this.RaiseAndSetIfChanged(ref _categorySearchText, value);
    }

    private RawMaterial? _selectedRawMaterial;
    public RawMaterial? SelectedRawMaterial
    {
        get => _selectedRawMaterial;
        set => this.RaiseAndSetIfChanged(ref _selectedRawMaterial, value);
    }

    /// <summary>The material whose price history, brand prices and BOM the grids hold; null for none (a new material).</summary>
    private int? _detailMaterialId;

    private bool _areDetailsReady = true;
    /// <summary>
    /// False from picking another material until its details (price history, brand prices, BOM) arrive, while the previous
    /// material's still show: the details tabs ignore the mouse, and Save waits, since it would write the previous
    /// material's brand prices or BOM under this one.
    /// </summary>
    public bool AreDetailsReady
    {
        get => _areDetailsReady;
        private set => this.RaiseAndSetIfChanged(ref _areDetailsReady, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    /// <summary>
    /// Flags EditingName as taken the moment it's left (SummaryHeaderCard's NameTextBox LostFocus), rather than
    /// waiting for Save to hit the same message from the unique-index violation (ViewModelBase.RunSafelyAsync).
    /// Case-insensitive and trimmed, matching the database's default collation the unique index relies on. Fixing
    /// the name and leaving the field again clears the message this raised, without touching an unrelated one.
    /// </summary>
    public void CheckNameAvailability()
    {
        var name = EditingName.Trim();
        var isTaken = name.Length > 0
            && RawMaterials.Any(m => m.Id != _editingId && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

        if (isTaken)
        {
            ErrorMessage = "That name is already in use.";
        }
        else if (ErrorMessage == "That name is already in use.")
        {
            ErrorMessage = null;
        }
    }

    private RawMaterialType _editingType = RawMaterialType.Ingredient;
    public RawMaterialType EditingType
    {
        get => _editingType;
        set => this.RaiseAndSetIfChanged(ref _editingType, value);
    }

    private RawMaterialCategory? _editingCategory;
    public RawMaterialCategory? EditingCategory
    {
        get => _editingCategory;
        set
        {
            // The "< Add New .. >" row isn't a category to file the material under: AddCategoryCommand takes over,
            // and puts a category back in the picker once it's done. By id, not ReferenceEquals(value, AddNewCategory):
            // the row is replaced (not mutated) every time CategorySearchText changes, so it's rarely still that exact
            // instance by the time it's picked.
            if (value?.Id == AddNewCategoryId)
            {
                AddCategoryCommand.Execute().Subscribe();
                return;
            }

            this.RaiseAndSetIfChanged(ref _editingCategory, value);
        }
    }

    private RawMaterialSource _editingSource = RawMaterialSource.Purchased;
    public RawMaterialSource EditingSource
    {
        get => _editingSource;
        set => this.RaiseAndSetIfChanged(ref _editingSource, value);
    }

    private decimal? _editingWeightLossPercent;
    public decimal? EditingWeightLossPercent
    {
        get => _editingWeightLossPercent;
        set => this.RaiseAndSetIfChanged(ref _editingWeightLossPercent, value);
    }

    /// <summary>Linked only: the ingredient this one is a part of.</summary>
    private RawMaterial? _editingLinkedTo;
    public RawMaterial? EditingLinkedTo
    {
        get => _editingLinkedTo;
        set => this.RaiseAndSetIfChanged(ref _editingLinkedTo, value);
    }

    /// <summary>Linked only: how much of the linked-to ingredient's weight this part is.</summary>
    private decimal? _editingWeightSharePercent;
    public decimal? EditingWeightSharePercent
    {
        get => _editingWeightSharePercent;
        set => this.RaiseAndSetIfChanged(ref _editingWeightSharePercent, value);
    }

    /// <summary>Linked only: how much of the linked-to ingredient's cost this part carries.</summary>
    private decimal? _editingCostSharePercent;
    public decimal? EditingCostSharePercent
    {
        get => _editingCostSharePercent;
        set => this.RaiseAndSetIfChanged(ref _editingCostSharePercent, value);
    }

    /// <summary>The material's cost buffer; empty saves as 0%, since the box shows 0.00 when cleared.</summary>
    private decimal? _editingCostBufferPercent = 0m;
    public decimal? EditingCostBufferPercent
    {
        get => _editingCostBufferPercent;
        set => this.RaiseAndSetIfChanged(ref _editingCostBufferPercent, value);
    }

    /// <summary>
    /// The Cost Buffer % box. Typing it works the buffered cost out afresh: the dearest brand, as the grid now is, with
    /// the percent on top (nothing to work out from while no brand is priced). Loading a material sets
    /// EditingCostBufferPercent itself, so the saved buffered cost stays as it was.
    /// </summary>
    public decimal? TypedCostBufferPercent
    {
        get => EditingCostBufferPercent;
        set
        {
            EditingCostBufferPercent = value;
            if (DearestLiveCost() is { } dearest)
            {
                EditingBufferedCost = RawMaterialRules.BufferedCost(dearest, value ?? 0m);
            }
        }
    }

    /// <summary>
    /// Purchased only: the fixed buffered cost per base unit, which brand prices don't move. Typed, worked out from the
    /// dearest brand when the percent is typed, or by Recalculate; a cleared box saves as 0, which Save then works out
    /// afresh from the prices (as for a material's first price).
    /// </summary>
    private decimal? _editingBufferedCost;
    public decimal? EditingBufferedCost
    {
        get => _editingBufferedCost;
        set => this.RaiseAndSetIfChanged(ref _editingBufferedCost, value);
    }

    /// <summary>
    /// The units the buffered cost can be shown and typed in: the base unit, then the form's alternative units that have
    /// a multiplier. Left alone when nothing changed, so the picker keeps its selection.
    /// </summary>
    public ObservableCollection<PurchaseUnitChoice> BufferedCostUnitOptions { get; } = [];

    private PurchaseUnitChoice? _bufferedCostUnit;
    /// <summary>
    /// The unit the Buffered Cost box is in, for show only: the buffered cost is kept per base unit. It starts on the
    /// dearest brand's unit, and falls back to the base unit when its unit goes.
    /// </summary>
    public PurchaseUnitChoice? BufferedCostUnit
    {
        get => _bufferedCostUnit;
        set
        {
            // While the choices are replaced, the picker writes back an empty pick; the rebuild sets the pick itself.
            if (_rebuildingBufferedCostUnits)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _bufferedCostUnit, value);
            UpdatePurchaseCostTexts();
        }
    }

    /// <summary>
    /// Set while RebuildBufferedCostUnitOptions replaces the choices. Clearing them makes the picker write back an empty
    /// pick, whose texts update would rebuild the choices again, halfway through, until the stack overflowed.
    /// </summary>
    private bool _rebuildingBufferedCostUnits;

    /// <summary>
    /// EditingBufferedCost in BufferedCostUnit, rounded to the centavo for show: ₱0.0518948/g is ₱1,297.37 a 25 kg bag.
    /// What's typed is divided back to the base unit at full precision, so it isn't rounded, and the Cost Buffer % becomes
    /// what it is over the dearest brand, to the 4 decimals the percent keeps: ₱1,300 a bag over ₱1,250 is 4%.
    /// </summary>
    public decimal? EditingBufferedCostInUnit
    {
        get => EditingBufferedCost is { } cost ? Math.Round(cost * BufferedCostUnitSize(), 2) : null;
        set
        {
            EditingBufferedCost = value is { } typed ? typed / BufferedCostUnitSize() : null;
            if (EditingBufferedCost is { } buffered && DearestLiveCost() is { } dearest)
            {
                EditingCostBufferPercent = Math.Round(RawMaterialRules.CostBufferPercentOf(dearest, buffered), 4);
            }
        }
    }

    /// <summary>How many base units BufferedCostUnit holds: 1 for the base unit.</summary>
    private decimal BufferedCostUnitSize() =>
        BufferedCostUnit is { Key: not PurchaseUnitChoice.BaseUnitKey } unit
        && UnitRows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unit.Key)?.Unit is { BaseUnitsPerUnit: > 0 } row
            ? row.BaseUnitsPerUnit
            : 1m;

    /// <summary>Packaging only; an ingredient's base unit is always the gram.</summary>
    private UnitOfMeasure? _editingBaseUnit;
    public UnitOfMeasure? EditingBaseUnit
    {
        get => _editingBaseUnit;
        set => this.RaiseAndSetIfChanged(ref _editingBaseUnit, value);
    }

    /// <summary>Only a new material's type and source can be picked: both are fixed once saved (RawMaterialRules).</summary>
    private bool _canChangeTypeAndSource = true;
    public bool CanChangeTypeAndSource
    {
        get => _canChangeTypeAndSource;
        private set => this.RaiseAndSetIfChanged(ref _canChangeTypeAndSource, value);
    }

    /// <summary>
    /// A packaging material's base unit can be picked while it's new, or has no saved alternative units
    /// or prices yet (RawMaterialRules.EnsureBaseUnitCanChange; the repository also checks packaging
    /// lines). Unsaved units don't count: they're saved against whichever base unit is picked then.
    /// </summary>
    private bool _canChangeBaseUnit = true;
    public bool CanChangeBaseUnit
    {
        get => _canChangeBaseUnit;
        private set => this.RaiseAndSetIfChanged(ref _canChangeBaseUnit, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isEditingIngredient;
    public bool IsEditingIngredient => _isEditingIngredient.Value;

    private readonly ObservableAsPropertyHelper<bool> _isEditingPackaging;
    public bool IsEditingPackaging => _isEditingPackaging.Value;

    private readonly ObservableAsPropertyHelper<bool> _isEditingMade;
    /// <summary>A made ingredient: it has a weight loss percent and a BOM instead of a purchase price.</summary>
    public bool IsEditingMade => _isEditingMade.Value;

    private readonly ObservableAsPropertyHelper<bool> _isEditingLinked;
    /// <summary>A linked ingredient: its cost is a share of another ingredient's, instead of a price or a BOM.</summary>
    public bool IsEditingLinked => _isEditingLinked.Value;

    private readonly ObservableAsPropertyHelper<string> _linkedCostPreviewText;
    /// <summary>What the link works out to as it's filled in: the linked-to ingredient's cost per gram, and this part's.</summary>
    public string LinkedCostPreviewText => _linkedCostPreviewText.Value;

    private readonly ObservableAsPropertyHelper<string> _linkedPartsText;
    /// <summary>The linked-to ingredient's other parts, and how much of its weight and cost all of them take with this one.</summary>
    public string LinkedPartsText => _linkedPartsText.Value;

    private readonly ObservableAsPropertyHelper<bool> _showPurchaseSetup;
    /// <summary>A purchased material, saved or still being filled in, so a new one can be priced before its first Save.</summary>
    public bool ShowPurchaseSetup => _showPurchaseSetup.Value;

    private readonly ObservableAsPropertyHelper<bool> _showPriceHistory;
    /// <summary>A saved purchased material: a new one has no price history yet.</summary>
    public bool ShowPriceHistory => _showPriceHistory.Value;

    /// <summary>The details tabs along the bottom: Cost Setup, Alternative Units, Usage Example, then Price History.</summary>
    public const int PriceHistoryTab = 3;

    /// <summary>The details tab showing. Moves to Cost Setup when Price History is about to hide (ShowPriceHistory).</summary>
    private int _selectedDetailsTab;
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set => this.RaiseAndSetIfChanged(ref _selectedDetailsTab, value);
    }

    private readonly ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The summary header's big title: the material's name, or "New raw material".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private readonly ObservableAsPropertyHelper<string> _headerInitial;
    public string HeaderInitial => _headerInitial.Value;

    private readonly ObservableAsPropertyHelper<string> _baseUnitName;
    /// <summary>The form's base unit name: "gram" for an ingredient, e.g. "centimeter" for ribbon.</summary>
    public string BaseUnitName => _baseUnitName.Value;

    private readonly ObservableAsPropertyHelper<string> _baseUnitAbbreviation;
    public string BaseUnitAbbreviation => _baseUnitAbbreviation.Value;

    private string _currentCostText = "—";
    /// <summary>
    /// The saved cost in the Purchase Cost Setup's Per unit (BufferedCostUnit): "₱0.0372" a gram, to 4 decimals, or
    /// "₱930.00" a bag, to 2; "—" for a material not saved yet.
    /// </summary>
    public string CurrentCostText
    {
        get => _currentCostText;
        private set => this.RaiseAndSetIfChanged(ref _currentCostText, value);
    }

    private string _bufferedCostText = "—";
    /// <summary>The saved buffered cost, as CurrentCostText: "₱0.0391" a gram, "₱976.50" a bag, or "—" for a material not saved yet.</summary>
    public string BufferedCostText
    {
        get => _bufferedCostText;
        private set => this.RaiseAndSetIfChanged(ref _bufferedCostText, value);
    }

    private string _headerCostUnitText = "g";
    /// <summary>The unit the header's costs are in, for their labels: the Per unit's abbreviation, e.g. "g" or "bag".</summary>
    public string HeaderCostUnitText
    {
        get => _headerCostUnitText;
        private set => this.RaiseAndSetIfChanged(ref _headerCostUnitText, value);
    }

    /// <summary>The latest price's date, or "—" for an unpriced or made material.</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }

    // Alternative Units: typed straight into the grid; Remove acts on the row picked.
    private RawMaterialUnitRow? _selectedUnitRow;
    public RawMaterialUnitRow? SelectedUnitRow
    {
        get => _selectedUnitRow;
        set => this.RaiseAndSetIfChanged(ref _selectedUnitRow, value);
    }

    /// <summary>Whether the form can take another alternative unit (UnitConversionRules.MaxAlternativeUnits); the grid's add row shows while it can.</summary>
    private bool _canAddUnit = true;
    public bool CanAddUnit
    {
        get => _canAddUnit;
        private set => this.RaiseAndSetIfChanged(ref _canAddUnit, value);
    }

    /// <summary>
    /// The row the grid's add row edits (controls:GridAddNewRow.ItemFactory): one that names its unit as soon as it's
    /// picked and shows the current base unit, which a row the grid made itself couldn't.
    /// </summary>
    public Func<RawMaterialUnitRow> NewUnitRow => () => new RawMaterialUnitRow(new RawMaterialUnit(), BaseUnitAbbreviation, FindUnitOfMeasure);

    /// <summary>Set while the screen itself replaces or changes the unit rows, so only the user's grid edits count as edits.</summary>
    private bool _syncingUnitRows;

    // Purchase Cost Setup: a brand per row, typed straight into the grid; Remove acts on the row picked.
    /// <summary>The form's brand prices, saved or not. The material is costed at the dearest per base unit.</summary>
    public ObservableCollection<BrandPriceRow> BrandPriceRows { get; } = [];

    private BrandPriceRow? _selectedBrandPriceRow;
    public BrandPriceRow? SelectedBrandPriceRow
    {
        get => _selectedBrandPriceRow;
        set => this.RaiseAndSetIfChanged(ref _selectedBrandPriceRow, value);
    }

    /// <summary>The row the grid's add row edits (controls:GridAddNewRow.ItemFactory): one that follows the form's units and buffer.</summary>
    public Func<BrandPriceRow> NewBrandPriceRow => () => new BrandPriceRow(this);

    /// <summary>
    /// The alternative units added by picking, on a brand row, a unit the material didn't have. Each goes again once no
    /// row is in it, so trying units out leaves nothing behind.
    /// </summary>
    private readonly HashSet<RawMaterialUnit> _brandAddedUnits = [];

    /// <summary>How many brand prices the material has saved, which fix a packaging material's base unit.</summary>
    private int _savedBrandPriceCount;

    private string _purchaseCostText = "—";
    /// <summary>What the dearest brand comes to per base unit, recalculated as the grid is typed into, e.g. "₱0.0372".</summary>
    public string PurchaseCostText
    {
        get => _purchaseCostText;
        private set => this.RaiseAndSetIfChanged(ref _purchaseCostText, value);
    }

    private string _purchaseBufferedCostText = string.Empty;
    /// <summary>What EditingBufferedCost comes to over the dearest brand, e.g. "₱0.0391/g is 5.11% over the dearest brand's ₱0.0372/g.", or empty while unpriced.</summary>
    public string PurchaseBufferedCostText
    {
        get => _purchaseBufferedCostText;
        private set => this.RaiseAndSetIfChanged(ref _purchaseBufferedCostText, value);
    }

    private string _bufferedCostWarningText = string.Empty;
    /// <summary>Set while the dearest brand costs more per base unit than EditingBufferedCost, so the buffer has been eaten up; empty otherwise.</summary>
    public string BufferedCostWarningText
    {
        get => _bufferedCostWarningText;
        private set => this.RaiseAndSetIfChanged(ref _bufferedCostWarningText, value);
    }

    private decimal? _dearestPurchaseCost;
    /// <summary>The dearest brand per base unit as the grid is typed into, which Recalculate puts the buffer on; null while unpriced.</summary>
    public decimal? DearestPurchaseCost
    {
        get => _dearestPurchaseCost;
        private set => this.RaiseAndSetIfChanged(ref _dearestPurchaseCost, value);
    }

    private string _purchaseCostSourceText = string.Empty;
    /// <summary>Which brand the cost comes from, when there's more than one, e.g. "From Gold Medal, the dearest of 2 brands."</summary>
    public string PurchaseCostSourceText
    {
        get => _purchaseCostSourceText;
        private set => this.RaiseAndSetIfChanged(ref _purchaseCostSourceText, value);
    }

    // Usage Example.
    private RawMaterial? _exampleRawMaterial;
    public RawMaterial? ExampleRawMaterial
    {
        get => _exampleRawMaterial;
        set => this.RaiseAndSetIfChanged(ref _exampleRawMaterial, value);
    }

    private decimal _exampleQuantity = 1m;
    public decimal ExampleQuantity
    {
        get => _exampleQuantity;
        set => this.RaiseAndSetIfChanged(ref _exampleQuantity, value);
    }

    private RawMaterialUnitOption? _exampleUnit;
    public RawMaterialUnitOption? ExampleUnit
    {
        get => _exampleUnit;
        set => this.RaiseAndSetIfChanged(ref _exampleUnit, value);
    }

    private UsageExampleResult? _exampleResult;
    public UsageExampleResult? ExampleResult
    {
        get => _exampleResult;
        private set => this.RaiseAndSetIfChanged(ref _exampleResult, value);
    }

    // Bill of Materials (made ingredients): components and grams, typed straight into its table.
    private IngredientLineRow? _selectedBomLineEdit;
    public IngredientLineRow? SelectedBomLineEdit
    {
        get => _selectedBomLineEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedBomLineEdit, value);
    }

    /// <summary>The BOM lines as last loaded, so Save only sends the BOM when its lines were changed.</summary>
    private List<(int ComponentIngredientId, decimal Grams)> _savedBomLines = [];

    private RawMaterialPriceRow? _selectedPriceRow;
    public RawMaterialPriceRow? SelectedPriceRow
    {
        get => _selectedPriceRow;
        set => this.RaiseAndSetIfChanged(ref _selectedPriceRow, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateExampleCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveBrandPriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RecalculateBufferCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveBomLineCommand { get; }

    /// <summary>
    /// The form's undo and redo stacks, the same shape as Products'. One step is one finished change. The price
    /// history is left out: its Delete writes to the database on the spot, so there is nothing unsaved to put back.
    /// Cleared whenever the form stops being the one it recorded: another material picked, a new one, or a Save.
    /// </summary>
    public UndoHistory<RawMaterialFormSnapshot> History { get; }

    public ReactiveCommand<RxVoid, RxVoid> UndoCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RedoCommand { get; }

    /// <summary>
    /// Input: the label to put over the box, and the value it starts with — what was already typed into the
    /// Category picker, so confirming a category from "&lt; Add New "X" .. &gt;" doesn't mean typing X again.
    /// Output: the name typed for the new category, or null if it was cancelled. The View registers a handler
    /// that shows a prompt dialog (Views/TextPromptWindow).
    /// </summary>
    public Interaction<(string Label, string InitialValue), string?> PromptForNewCategory { get; } = new();

    public RawMaterialsViewModel(
        IRawMaterialRepository rawMaterialRepository,
        IRawMaterialPriceHistoryRepository priceHistoryRepository,
        IRawMaterialBrandPriceRepository brandPriceRepository,
        IIngredientBomLineRepository bomLineRepository,
        IRawMaterialCategoryRepository categoryRepository,
        IValidator<RawMaterialCategory> categoryValidator,
        IValidator<RawMaterial> rawMaterialValidator,
        IValidator<RawMaterialUnit> unitValidator,
        IValidator<RawMaterialBrandPrice> brandPriceValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        ILogger logger)
        : base(logger)
    {
        _rawMaterialRepository = rawMaterialRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _brandPriceRepository = brandPriceRepository;
        _bomLineRepository = bomLineRepository;
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;
        _rawMaterialValidator = rawMaterialValidator;
        _unitValidator = unitValidator;
        _brandPriceValidator = brandPriceValidator;

        var byName = SortExpressionComparer<RawMaterial>.Ascending(m => m.Name);

        _rawMaterialCache.Connect()
            .SortAndBind(out _rawMaterials, byName)
            .Subscribe();

        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<RawMaterial, bool>)(m => text.Length == 0 || m.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        _rawMaterialCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleRawMaterials, byName)
            .Subscribe();

        _rawMaterialCache.Connect()
            .Filter(m => m.Type == RawMaterialType.Ingredient)
            .SortAndBind(out _ingredients, byName)
            .Subscribe();

        // Shared by every picker that must leave out the ingredient being edited: Linked's target and the BOM's component.
        var notTheSelection = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(selected => selected?.Id ?? 0)
            .DistinctUntilChanged()
            .Select(selectedId => (Func<RawMaterial, bool>)(m => m.Type == RawMaterialType.Ingredient && m.Id != selectedId));

        _rawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out _linkTargets, byName)
            .Subscribe();

        _rawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out _bomComponentOptions, byName)
            .Subscribe();

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();

        // The "< Add New .. >" row comes from a cache of its own rather than _categoryCache, which LoadCommand's
        // MergeFrom diffs against the categories it loaded — the row isn't one of them, so it would be dropped.
        _addNewCategoryOption.AddOrUpdate(AddNewCategory);

        // The picker offers only the categories of the material's own type, which is all it can be saved in.
        _categoryCache.Connect()
            .Filter(this.WhenAnyValue(x => x.EditingType).Select(type => (Func<RawMaterialCategory, bool>)(c => c.Type == type)))
            .Or(_addNewCategoryOption.Connect())
            .SortAndBind(out _categoryOptions, SortExpressionComparer<RawMaterialCategory>
                .Ascending(c => c.Id == AddNewCategoryId ? 0 : 1)
                .ThenByAscending(c => c.Name))
            .Subscribe();

        // The picker's own AutoComplete (ComboBoxAdv.FilterItem) re-filters CategoryOptions by the same typed text
        // against every item's Name, including "< Add New .. >" itself — so as soon as nothing matches, it filters
        // that out too and the picker shows "No Records Found" exactly when Add New is the one useful option. There's
        // no per-item exemption from that filter, so the row is renamed to always contain whatever's typed, the same
        // way a search box with a "+ Create 'X'" row does: it can never fail to match, without touching how the real
        // categories are filtered (still entirely AutoCompleteMode="Suggest", untouched). CategorySearchText is bound
        // to the picker's Text (not SelectedItem), and AddOrUpdate replaces the row through the same SourceCache path
        // a real category's rename already flows through, so the picker's own CollectionView sees a proper replace
        // notification — no dependence on RawMaterialCategory raising PropertyChanged, which it doesn't.
        this.WhenAnyValue(x => x.CategorySearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Subscribe(text => _addNewCategoryOption.AddOrUpdate(new RawMaterialCategory
            {
                Id = AddNewCategoryId,
                Name = text.Length == 0 ? "< Add New .. >" : $"< Add New \"{text}\" .. >",
            }));

        // A new material's type changed under a category of the other type: that category is no longer one it can go in.
        this.WhenAnyValue(x => x.EditingType)
            .Where(type => EditingCategory is { } category && category.Type != type)
            .Subscribe(_ => EditingCategory = null);

        _isEditingIngredient = this.WhenAnyValue(x => x.EditingType)
            .Select(type => type == RawMaterialType.Ingredient)
            .ToProperty(this, x => x.IsEditingIngredient);

        _isEditingPackaging = this.WhenAnyValue(x => x.EditingType)
            .Select(type => type == RawMaterialType.Packaging)
            .ToProperty(this, x => x.IsEditingPackaging);

        _isEditingMade = this.WhenAnyValue(x => x.EditingType, x => x.EditingSource,
                (type, source) => type == RawMaterialType.Ingredient && source == RawMaterialSource.Made)
            .ToProperty(this, x => x.IsEditingMade);

        _isEditingLinked = this.WhenAnyValue(x => x.EditingType, x => x.EditingSource,
                (type, source) => type == RawMaterialType.Ingredient && source == RawMaterialSource.Linked)
            .ToProperty(this, x => x.IsEditingLinked);

        _linkedCostPreviewText = this.WhenAnyValue(x => x.EditingLinkedTo, x => x.EditingWeightSharePercent, x => x.EditingCostSharePercent,
                LinkedCostPreview)
            .ToProperty(this, x => x.LinkedCostPreviewText, string.Empty);

        _linkedPartsText = this.WhenAnyValue(x => x.EditingLinkedTo, x => x.EditingWeightSharePercent, x => x.EditingCostSharePercent,
                LinkedPartsSummary)
            .ToProperty(this, x => x.LinkedPartsText, string.Empty);

        // The cards follow the form, not the selection, so a new material can be filled in completely
        // before its one Save.
        _showPurchaseSetup = this.WhenAnyValue(x => x.EditingType, x => x.EditingSource,
                (type, source) => type == RawMaterialType.Packaging || source == RawMaterialSource.Purchased)
            .ToProperty(this, x => x.ShowPurchaseSetup);

        // A hidden tab can't stay the one showing: picking a made or new material while on Price
        // History goes back to Cost Setup. Subscribed before ShowPriceHistory, so the tab strip moves
        // before the tab hides; a TabControlExt whose selected tab hides picks a neighbour itself.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(material => material is not { Source: RawMaterialSource.Purchased } && SelectedDetailsTab == PriceHistoryTab)
            .Subscribe(_ => SelectedDetailsTab = 0);

        _showPriceHistory = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(material => material is { Source: RawMaterialSource.Purchased })
            .ToProperty(this, x => x.ShowPriceHistory);

        _headerTitle = this.WhenAnyValue(x => x.SelectedRawMaterial, x => x.EditingName, (material, name) =>
                material is null && string.IsNullOrWhiteSpace(name) ? "New raw material" : name)
            .ToProperty(this, x => x.HeaderTitle, "New raw material");

        _headerInitial = this.WhenAnyValue(x => x.HeaderTitle)
            .Select(title => string.IsNullOrWhiteSpace(title) ? "?" : title.Trim()[..1].ToUpperInvariant())
            .ToProperty(this, x => x.HeaderInitial, "N");

        _baseUnitName = this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit,
                (type, baseUnit) => BaseUnitText.NameOf(type, baseUnit))
            .ToProperty(this, x => x.BaseUnitName, "gram");

        _baseUnitAbbreviation = this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit,
                (type, baseUnit) => BaseUnitText.AbbreviationOf(type, baseUnit))
            .ToProperty(this, x => x.BaseUnitAbbreviation, "g");

        // Nothing is recorded until a material's details have arrived, or while the screen is being written to
        // wholesale: the brand prices and the BOM land a moment after the pick (AreDetailsReady), and a step taken
        // before they do would put the empty grids back over them.
        History = new UndoHistory<RawMaterialFormSnapshot>(
            CaptureSnapshot, (before, after) => before.ShowsSameAs(after), () => AreDetailsReady && !IsBusyApplying);

        UndoCommand = ReactiveCommand.Create(() => { TryUndo(); }, History.WhenAnyValue(h => h.CanUndo));
        RedoCommand = ReactiveCommand.Create(() => { TryRedo(); }, History.WhenAnyValue(h => h.CanRedo));

        // The header's saved costs; a new Per unit, or a unit's multiplier, updates them through UpdatePurchaseCostTexts.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Subscribe(_ => UpdateHeaderCostTexts());

        // The summary follows the grid as it's typed into: the dearest brand, as Save will cost the material.
        BrandPriceRows.ToObservableChangeSet()
            .AutoRefresh(r => r.Price)
            .AutoRefresh(r => r.Quantity)
            .AutoRefresh(r => r.Brand)
            .Where(_ => !IsRestoring)
            .Subscribe(_ => UpdatePurchaseCostTexts());

        // Each brand's buffered price follows the fixed buffered cost. The percent doesn't move it: it only feeds Recalculate.
        this.WhenAnyValue(x => x.EditingBufferedCost, x => x.EditingCostBufferPercent)
            .Where(_ => !IsRestoring)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(TypedCostBufferPercent));
                RefreshBrandPriceRows();
                UpdatePurchaseCostTexts();
            });

        // A packaging material is always purchased, and an ingredient's base unit is the gram, so
        // switching a new material's type clears whatever the other type had filled in.
        this.WhenAnyValue(x => x.EditingType)
            .Where(_ => !IsBusyApplying)
            .Subscribe(type =>
            {
                if (type == RawMaterialType.Packaging)
                {
                    EditingSource = RawMaterialSource.Purchased;
                    EditingWeightLossPercent = null;
                    EditingLinkedTo = null;
                    EditingWeightSharePercent = null;
                    EditingCostSharePercent = null;
                }
                else
                {
                    EditingBaseUnit = null;
                }
            });

        // A made ingredient starts with no weight loss: 0%, which is what the empty box shows anyway.
        this.WhenAnyValue(x => x.EditingSource)
            .Where(source => source == RawMaterialSource.Made && !IsBusyApplying)
            .Subscribe(_ => EditingWeightLossPercent ??= 0m);

        // The alternative units and the purchase unit choices name the base unit, which a new
        // material can still change.
        this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit)
            .Where(_ => !IsBusyApplying)
            .Subscribe(_ => RebuildUnitViews());

        // Every selection-driven subscription skips while LoadCommand merges in fresh data, which
        // only re-points a selection at its refreshed copy; the form and details must stay as-is.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(material => material is not null && !IsBusyApplying)
            .Subscribe(material => LoadIntoForm(material!));

        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(_ => !IsBusyApplying)
            .Subscribe(ResetDetails);

        // A newer selection cancels an older one still loading (Switch disposes it, which cancels its token), so a quick
        // second pick never gets the first one's details added to its own.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(_ => !IsBusyApplying)
            .Select(material => Observable.FromAsync(cancellationToken => LoadDetailsAsync(material, cancellationToken)))
            .Switch()
            .Subscribe();

        // A unit's multiplier changed in the Alternative Units grid reprices the brands bought in it straight away,
        // and the add row goes once there are two units.
        UnitRows.ToObservableChangeSet()
            .AutoRefresh(r => r.UnitOfMeasureId)
            .AutoRefresh(r => r.BaseUnitsPerUnit)
            .Where(_ => !_syncingUnitRows && !IsBusyApplying)
            .Subscribe(_ => OnUnitRowsEdited());

        // A component picked in the BOM table is pointed at the screen's copy of it, for its name.
        BomLineEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Where(_ => !IsRestoring)
            .Subscribe(_ => IngredientLineRow.Resolve(BomLineEdits, Ingredients.ToDictionary(m => m.Id)));

        this.WhenAnyValue(x => x.ExampleRawMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material => ReplaceExampleUnitOptions(material, kept: null));

        // A worked-out example belongs to the inputs it was worked out from.
        this.WhenAnyValue(x => x.ExampleRawMaterial, x => x.ExampleQuantity, x => x.ExampleUnit)
            .Where(_ => !IsRefreshing)
            .Subscribe(_ => ExampleResult = null);

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, picker choices and unsaved edits
        // while picking up what changed elsewhere, such as a unit renamed in Settings.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var rawMaterials = await _rawMaterialRepository.GetAllAsync(CancellationToken.None);

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedRawMaterial?.Id;
            var categoryId = EditingCategory?.Id;
            var linkedToId = EditingLinkedTo?.Id;
            var baseUnitId = EditingBaseUnit?.Id;
            var exampleMaterialId = ExampleRawMaterial?.Id;
            var exampleUnit = ExampleUnit;

            ApplyRefresh(() =>
            {
                _categoryCache.MergeFrom(categories);
                UnitsOfMeasure.MergeFrom([.. units.OrderBy(u => u.Name)], u => u.Id);
                _rawMaterialCache.MergeFrom(rawMaterials);

                EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
                EditingLinkedTo = Ingredients.FirstOrDefault(m => m.Id == linkedToId);
                var previousBaseUnit = EditingBaseUnit;
                EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId);

                IngredientLineRow.Resolve(BomLineEdits, Ingredients.ToDictionary(m => m.Id));

                SelectedRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == selectedId);

                // The form's alternative units may not be saved yet, so they stay rather than being
                // reloaded from the refreshed material. Each points at its refreshed unit of measure,
                // so one renamed in Settings shows its new name.
                foreach (var row in UnitRows)
                {
                    row.RefreshUnitOfMeasure();
                }

                if (!ReferenceEquals(previousBaseUnit, EditingBaseUnit))
                {
                    UpdateUnitRowsBaseUnit();
                }

                // Every time, so a unit of measure created or renamed in Settings shows as a purchase unit.
                RebuildPurchaseUnitOptions();

                var previousExample = ExampleRawMaterial;
                ExampleRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == exampleMaterialId);
                if (!ReferenceEquals(previousExample, ExampleRawMaterial))
                {
                    ReplaceExampleUnitOptions(ExampleRawMaterial, exampleUnit);
                    ExampleResult = null;
                }
            });
        });

        NewCommand = ReactiveCommand.Create(ClearForm);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            // The picked material's details haven't replaced the previous one's yet: saving now would write that material's
            // brand prices or BOM under this one. The moment passes in a fraction of a second.
            if (!AreDetailsReady)
            {
                return;
            }

            var type = EditingType;
            var source = type == RawMaterialType.Packaging ? RawMaterialSource.Purchased : EditingSource;
            var material = new RawMaterial
            {
                Id = _editingId,
                Name = EditingName,
                Type = type,
                CategoryId = EditingCategory?.Id ?? 0,
                Source = source,
                // A cleared box shows 0.00, so it saves as 0%.
                WeightLossPercent = source == RawMaterialSource.Made ? EditingWeightLossPercent ?? 0m : null,
                LinkedToId = source == RawMaterialSource.Linked ? EditingLinkedTo?.Id : null,
                WeightSharePercent = source == RawMaterialSource.Linked ? EditingWeightSharePercent : null,
                CostSharePercent = source == RawMaterialSource.Linked ? EditingCostSharePercent : null,
                BaseUnitId = type == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null,
                // A cleared box shows 0.00 too. A linked ingredient inherits the buffer of the one it's linked to
                // (the repository sets it from the saved one either way).
                CostBufferPercent = source == RawMaterialSource.Linked
                    ? EditingLinkedTo?.CostBufferPercent ?? 0m
                    : EditingCostBufferPercent ?? 0m,
                // Only a purchased material's is the form's; a made or linked one's is worked out by the cascade.
                BufferedCostPerBaseUnit = source == RawMaterialSource.Purchased ? EditingBufferedCost ?? 0m : 0m,
            };

            var validation = _rawMaterialValidator.Validate(material);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            // The units and brand prices are typed straight into their grids, so a half-filled or repeated one is caught here.
            EnsureValidUnitRows();
            var brandPrices = source == RawMaterialSource.Purchased ? CompleteBrandPrices() : null;

            // The picker already leaves the material being edited out of BomComponentOptions, so this can't be
            // picked through the UI. But a self-reference already in the database (only reachable by editing it
            // directly, never through this app) would otherwise slip past: ChangedBomLines only re-validates a
            // BOM that changed, so an untouched one skips IngredientBomRules.EnsureNoCycle entirely on a Save
            // that touches something else. This runs unconditionally, whether or not the BOM itself changed.
            if (source == RawMaterialSource.Made && BomLineEdits.Any(l => l.IngredientId == _editingId))
            {
                ErrorMessage = "An ingredient can't be a component of itself.";
                return;
            }

            var bomLines = source == RawMaterialSource.Made ? ChangedBomLines() : null;

            var id = await _rawMaterialRepository.SaveAsync(material, [.. UnitRows.Select(r => r.Unit)], brandPrices, bomLines, CancellationToken.None);

            // The list picks up any cost that cascaded to other materials; then the form reloads what
            // was saved, so new units get their ids and the brand prices, price history and BOM show the new rows.
            await LoadCommand.Execute();
            await ReloadSavedRawMaterialAsync(id);
            SuccessMessage = $"{EditingName} was saved.";
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedRawMaterial!.Name}'? This can't be undone.", async () =>
            {
                await _rawMaterialRepository.DeleteAsync(SelectedRawMaterial!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedRawMaterial).Select(material => material is not null));

        // The Category picker's "< Add New .. >" row: names a category, saves it, and picks it for the material
        // being edited, so filing one under a new category doesn't mean a trip to Settings and back.
        AddCategoryCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var name = await PromptForNewCategory.Handle(("Name for the new category:", CategorySearchText.Trim()));

            if (!string.IsNullOrWhiteSpace(name))
            {
                await RunSafelyAsync(async () =>
                {
                    // For the material's own type, the only kind the picker offers it.
                    var category = new RawMaterialCategory { Name = name.Trim(), Type = EditingType };

                    var validation = _categoryValidator.Validate(category);
                    if (!validation.IsValid)
                    {
                        ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                        return;
                    }

                    // AddAsync fills in the saved Id, which the reloaded copy to pick is found by.
                    await _categoryRepository.AddAsync(category, CancellationToken.None);
                    await LoadCommand.Execute();
                    EditingCategory = Categories.FirstOrDefault(c => c.Id == category.Id);
                    SuccessMessage = $"{category.Name} was added.";
                });
            }

            // The picker is still showing the row that was picked, so the category goes back into it: the new one,
            // or the one it had when the prompt was cancelled or the name refused. Pushed back here rather than
            // from the setter, since a value raised during the pick's own binding update is ignored.
            this.RaisePropertyChanged(nameof(EditingCategory));
        });

        var hasSelectedUnitRow = this.WhenAnyValue(x => x.SelectedUnitRow).Select(row => row is not null);

        // Nothing to confirm: the unit only leaves the list, and is deleted when the material is saved.
        DeleteUnitCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                var removed = SelectedUnitRow!.Unit;
                _brandAddedUnits.Remove(removed);
                ReplaceUnitRows([.. UnitRows.Select(r => r.Unit).Where(u => !ReferenceEquals(u, removed))], selectedUnit: null);
                RefreshBrandPriceRows();
                UpdatePurchaseCostTexts();
            }),
            hasSelectedUnitRow);

        CalculateExampleCommand = ReactiveCommand.Create(
            () =>
            {
                var material = ExampleRawMaterial!;
                var unit = ExampleUnit!;
                var baseQuantity = UnitConversionRules.ToBaseQuantity(ExampleQuantity, unit.Unit?.BaseUnitsPerUnit);
                ExampleResult = new UsageExampleResult(
                    $"Cost for {ExampleQuantity:#,0.####} {unit.Abbreviation} of {material.Name}",
                    baseQuantity,
                    BaseUnitText.AbbreviationOf(material),
                    material.CostPerBaseUnit,
                    baseQuantity * material.CostPerBaseUnit,
                    // What the fixed buffered cost comes to over the cost, which drifts from the percent as prices move.
                    material.CostPerBaseUnit > 0
                        ? Math.Round(RawMaterialRules.CostBufferPercentOf(material.CostPerBaseUnit, material.BufferedCostPerBaseUnit), 2)
                        : material.CostBufferPercent,
                    baseQuantity * material.BufferedCostPerBaseUnit);
            },
            this.WhenAnyValue(x => x.ExampleRawMaterial, x => x.ExampleUnit, x => x.ExampleQuantity,
                (material, unit, quantity) => material is not null && unit is not null && quantity > 0));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync("Delete this price history entry? The material's cost stays as it is. This can't be undone.", async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceRow!.Entry.Id, CancellationToken.None);

                // The history is only a log, so the cost stays and the form keeps its unsaved edits. Only the history reloads.
                var material = SelectedRawMaterial!;
                ReplacePriceHistory(material, await _priceHistoryRepository.GetForRawMaterialAsync(material.Id, CancellationToken.None));
                UpdateCanChangeBaseUnit();
            }),
            this.WhenAnyValue(x => x.SelectedPriceRow).Select(row => row is not null));

        // Nothing to confirm: the brand only leaves the grid, and its price goes when the material is saved.
        RemoveBrandPriceCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                BrandPriceRows.Remove(SelectedBrandPriceRow!);
                DropUnusedAddedUnits(pickedRow: null);
            }),
            this.WhenAnyValue(x => x.SelectedBrandPriceRow).Select(row => row is not null));

        // Re-initialises the fixed buffered cost from the dearest brand, as typed, with the buffer percent on top. Save keeps it.
        RecalculateBufferCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => EditingBufferedCost = RawMaterialRules.BufferedCost(DearestPurchaseCost!.Value, EditingCostBufferPercent ?? 0m)),
            this.WhenAnyValue(x => x.DearestPurchaseCost).Select(cost => cost is not null));

        RemoveBomLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => BomLineEdits.Remove(SelectedBomLineEdit!)),
            this.WhenAnyValue(x => x.SelectedBomLineEdit).Select(line => line is not null));
    }

    /// <summary>
    /// "Egg costs ₱0.0900/g, buffered ₱0.0990/g. At 70% of its weight for 30% of its cost, this costs ₱0.0386/g, buffered ₱0.0424/g."
    /// Worked out with the same rule the cascade saves with, from the linked-to ingredient's stored costs: the
    /// same share of its true cost and of its (fixed) buffered cost.
    /// </summary>
    private static string LinkedCostPreview(RawMaterial? linkedTo, decimal? weightShare, decimal? costShare)
    {
        if (linkedTo is null)
        {
            return "Pick the ingredient this one is a part of.";
        }

        var linkedToText = $"{linkedTo.Name} costs ₱{linkedTo.CostPerBaseUnit:N4}/g, buffered ₱{linkedTo.BufferedCostPerBaseUnit:N4}/g.";
        if (weightShare is not (> 0 and <= 100) || costShare is not (> 0 and <= 100))
        {
            return $"{linkedToText} Enter this part's share of its weight and of its cost.";
        }

        var cost = RawMaterialRules.LinkedCost(linkedTo.CostPerBaseUnit, weightShare.Value, costShare.Value);
        var buffered = RawMaterialRules.LinkedCost(linkedTo.BufferedCostPerBaseUnit, weightShare.Value, costShare.Value);
        return $"{linkedToText} At {weightShare:0.##}% of its weight for {costShare:0.##}% of its cost, this costs ₱{cost:N4}/g, buffered ₱{buffered:N4}/g.";
    }

    /// <summary>
    /// "Also linked to Egg: Egg Yolk (30% of the weight, 70% of the cost). With this one, 100% of its weight and 100% of its cost."
    /// The other parts come from the list's copies, so they're as of the last load; Save checks the saved ones.
    /// </summary>
    private string LinkedPartsSummary(RawMaterial? linkedTo, decimal? weightShare, decimal? costShare)
    {
        if (linkedTo is null)
        {
            return string.Empty;
        }

        var others = RawMaterials
            .Where(m => m.LinkedToId == linkedTo.Id && m.Id != _editingId)
            .ToList();
        var totalWeight = others.Sum(m => m.WeightSharePercent ?? 0m) + (weightShare ?? 0m);
        var totalCost = others.Sum(m => m.CostSharePercent ?? 0m) + (costShare ?? 0m);
        var totals = $"{totalWeight:0.##}% of its weight and {totalCost:0.##}% of its cost";

        if (others.Count == 0)
        {
            return $"Nothing else is linked to {linkedTo.Name}. This part takes {totals}.";
        }

        var parts = string.Join(", ", others.Select(m => $"{m.Name} ({m.WeightSharePercent:0.##}% of the weight, {m.CostSharePercent:0.##}% of the cost)"));
        return $"Also linked to {linkedTo.Name}: {parts}. With this one, {totals} (at most 100% each).";
    }

    void IUndoRecorder.BeginUndoStep() => History.Begin();

    void IUndoRecorder.EndUndoStep() => History.End();

    /// <summary>Records <paramref name="change"/> as one undo step, for a command that makes its change in one go.</summary>
    private void AsUndoStep(Action change)
    {
        History.Begin();
        try
        {
            change();
        }
        finally
        {
            History.End();
        }
    }

    /// <summary>Settles whatever step is open, then puts the form back one step.</summary>
    public bool TryUndo()
    {
        History.End();
        if (!History.TryUndo(out var restore))
        {
            return false;
        }

        RestoreSnapshot(restore);
        return true;
    }

    /// <summary>Steps forward again into what the last undo stepped off.</summary>
    public bool TryRedo()
    {
        History.End();
        if (!History.TryRedo(out var restore))
        {
            return false;
        }

        RestoreSnapshot(restore);
        return true;
    }

    private RawMaterialFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        Type = EditingType,
        CategoryId = EditingCategory?.Id,
        Source = EditingSource,
        CanChangeTypeAndSource = CanChangeTypeAndSource,
        BaseUnitId = EditingBaseUnit?.Id,
        WeightLossPercent = EditingWeightLossPercent,
        LinkedToId = EditingLinkedTo?.Id,
        WeightSharePercent = EditingWeightSharePercent,
        CostSharePercent = EditingCostSharePercent,
        CostBufferPercent = EditingCostBufferPercent,
        BufferedCost = EditingBufferedCost,
        BufferedCostUnitKey = BufferedCostUnit?.Key,
        UnitRows =
        [
            .. UnitRows.Select(r => new UnitRowState(
                r.Unit.Id, r.Unit.RawMaterialId, r.UnitOfMeasureId, r.BaseUnitsPerUnit, r.BaseUnitAbbreviation)),
        ],
        BrandPrices = [.. BrandPriceRows.Select(r => new BrandPriceState(r.Brand, r.UnitKey, r.Price))],
        BomLines = [.. BomLineEdits.Select(l => new IngredientLineState(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
    };

    /// <summary>
    /// Puts the form back as <paramref name="snapshot"/> had it. The rows are rebuilt rather than written into: an
    /// IngredientLineRow's setters drop a typed percentage, and a unit row writes straight through to the stored
    /// RawMaterialUnit that Save hands the repository, so each unit comes back as a fresh entity carrying its own
    /// Id - one that lost it would be saved as a new unit and the stored one deleted. The one recompute runs at the
    /// end, outside the guard, so the costs and captions are worked out once from the form that was put back.
    /// </summary>
    private void RestoreSnapshot(RawMaterialFormSnapshot snapshot)
    {
        ErrorMessage = null;

        ApplyRestore(() =>
        {
            SelectedUnitRow = null;
            SelectedBrandPriceRow = null;
            SelectedBomLineEdit = null;

            _editingId = snapshot.EditingId;
            CanChangeTypeAndSource = snapshot.CanChangeTypeAndSource;
            EditingName = snapshot.Name;
            EditingType = snapshot.Type;
            EditingCategory = snapshot.CategoryId is { } categoryId ? Categories.FirstOrDefault(c => c.Id == categoryId) : null;
            EditingSource = snapshot.Source;
            EditingBaseUnit = snapshot.BaseUnitId is { } baseUnitId ? UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId) : null;
            EditingWeightLossPercent = snapshot.WeightLossPercent;
            EditingLinkedTo = snapshot.LinkedToId is { } linkedToId ? Ingredients.FirstOrDefault(m => m.Id == linkedToId) : null;
            EditingWeightSharePercent = snapshot.WeightSharePercent;
            EditingCostSharePercent = snapshot.CostSharePercent;
            EditingCostBufferPercent = snapshot.CostBufferPercent;
            EditingBufferedCost = snapshot.BufferedCost;

            // The alternative units, as fresh entities carrying their ids.
            SyncUnitRows(() =>
            {
                UnitRows.Clear();
                foreach (var unit in snapshot.UnitRows)
                {
                    UnitRows.Add(new RawMaterialUnitRow(
                        new RawMaterialUnit
                        {
                            Id = unit.Id,
                            RawMaterialId = unit.RawMaterialId,
                            UnitOfMeasureId = unit.UnitOfMeasureId ?? 0,
                            UnitOfMeasure = unit.UnitOfMeasureId is { } id ? FindUnitOfMeasure(id) : null,
                            BaseUnitsPerUnit = unit.BaseUnitsPerUnit,
                        },
                        unit.BaseUnitAbbreviation,
                        FindUnitOfMeasure));
                }
            });
            CanAddUnit = UnitRows.Count < UnitConversionRules.MaxAlternativeUnits;

            BrandPriceRows.Clear();
            foreach (var price in snapshot.BrandPrices)
            {
                BrandPriceRows.Add(new BrandPriceRow(this) { Brand = price.Brand, UnitKey = price.UnitKey, Price = price.Price });
            }

            BomLineEdits.Clear();
            foreach (var line in snapshot.BomLines)
            {
                BomLineEdits.Add(IngredientLineRow.Restore(
                    id => Ingredients.FirstOrDefault(m => m.Id == id),
                    baseWeight: 0m,
                    line.IngredientId,
                    line.Grams,
                    line.IsFormulaBase,
                    line.AuthoredBakersPercent));
            }
        });

        // The Per picker's choices are rebuilt from the units above, so the one that was picked is found again here
        // rather than in the guard: RebuildUnitViews replaces the list BufferedCostUnit is chosen from.
        RebuildUnitViews();
        BufferedCostUnit = snapshot.BufferedCostUnitKey is { } unitKey
            ? BufferedCostUnitOptions.FirstOrDefault(o => o.Key == unitKey)
            : null;

        IngredientLineRow.Resolve(BomLineEdits, Ingredients.ToDictionary(m => m.Id));
        RefreshBrandPriceRows();
        UpdatePurchaseCostTexts();
    }

    /// <summary>
    /// An empty form, for New and after Delete. It starts as a purchased ingredient, the usual kind, but
    /// the category, brand prices and the usage example's material start empty, so none is saved or
    /// costed by accident. The numbers keep a value: the cost buffer is 0, and the usage example's
    /// quantity stays.
    /// </summary>
    private void ClearForm()
    {
        History.Clear();
        _editingId = 0;
        SelectedRawMaterial = null;
        CanChangeTypeAndSource = true;
        EditingName = string.Empty;
        EditingType = RawMaterialType.Ingredient;
        EditingCategory = null;
        EditingSource = RawMaterialSource.Purchased;
        EditingWeightLossPercent = null;
        EditingLinkedTo = null;
        EditingWeightSharePercent = null;
        EditingCostSharePercent = null;
        EditingCostBufferPercent = 0m;
        EditingBufferedCost = null;
        EditingBaseUnit = null;
        ExampleRawMaterial = null;
        ClearDetails();
    }

    private void LoadIntoForm(RawMaterial material)
    {
        History.Clear();
        _editingId = material.Id;
        CanChangeTypeAndSource = false;
        EditingName = material.Name;
        EditingType = material.Type;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == material.CategoryId);
        EditingSource = material.Source;
        EditingWeightLossPercent = material.WeightLossPercent;
        EditingLinkedTo = Ingredients.FirstOrDefault(m => m.Id == material.LinkedToId);
        EditingWeightSharePercent = material.WeightSharePercent;
        EditingCostSharePercent = material.CostSharePercent;
        EditingCostBufferPercent = material.CostBufferPercent;
        EditingBufferedCost = material.BufferedCostPerBaseUnit;
        EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == material.BaseUnitId);
    }

    /// <summary>
    /// What follows a new selection: its alternative units, and nothing picked or open. Another material's price history,
    /// brand prices and BOM replace the ones showing when they arrive, in the same pass (LoadDetailsAsync): cleared here,
    /// the grids drew empty in between. Until then they aren't this material's (AreDetailsReady).
    /// </summary>
    private void ResetDetails(RawMaterial? material)
    {
        AreDetailsReady = material is null || material.Id == _detailMaterialId;
        SelectedPriceRow = null;
        SelectedBrandPriceRow = null;
        SelectedBomLineEdit = null;
        LoadUnitRows(material);
        if (material is not null)
        {
            ExampleRawMaterial = material;
        }
    }

    /// <summary>
    /// A new material's details: no alternative units, brand prices, price history or BOM lines. Selecting nothing
    /// clears them only when something was selected, and after an unsaved new material nothing was.
    /// </summary>
    private void ClearDetails()
    {
        ResetDetails(null);
        ShowDetails(null, RawMaterialDetail.None);
    }

    /// <summary>
    /// What a pick shows beyond the list's copy of the material (its price history, brand prices and BOM), read on the thread
    /// pool in one go, so the UI thread comes back once rather than after each read. Only the material picked last is shown:
    /// a newer pick cancels this load, so its details can't join the newer one's.
    /// </summary>
    private async Task LoadDetailsAsync(RawMaterial? material, CancellationToken cancellationToken)
    {
        if (material is null)
        {
            ShowDetails(null, RawMaterialDetail.None);
            return;
        }

        RawMaterialDetail detail;
        try
        {
            detail = await Task.Run(() => ReadDetailAsync(material, cancellationToken), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && material.Id != _detailMaterialId)
        {
            // Nothing of the picked material's came: the previous material's mustn't stay showing under its name.
            ShowDetails(null, RawMaterialDetail.None);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        ShowDetails(material, detail);
    }

    /// <summary>A material's price history, brand prices and BOM lines, as LoadDetailsAsync reads them.</summary>
    private sealed record RawMaterialDetail(
        IReadOnlyList<RawMaterialPriceHistory> PriceHistory,
        IReadOnlyList<RawMaterialBrandPrice> BrandPrices,
        IReadOnlyList<IngredientBomLine> BomLines)
    {
        public static readonly RawMaterialDetail None = new([], [], []);
    }

    /// <summary>Only the repositories: this runs off the UI thread. A purchased material has prices, a made one a BOM, and a linked one neither.</summary>
    private async Task<RawMaterialDetail> ReadDetailAsync(RawMaterial material, CancellationToken cancellationToken) =>
        material.Source switch
        {
            RawMaterialSource.Purchased => new RawMaterialDetail(
                await _priceHistoryRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                await _brandPriceRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                []),
            RawMaterialSource.Made => new RawMaterialDetail([], [], await _bomLineRepository.GetForIngredientAsync(material.Id, cancellationToken)),
            _ => RawMaterialDetail.None,
        };

    /// <summary>
    /// Puts <paramref name="material"/>'s details in place of whatever the grids held, in one pass, so nothing is drawn in
    /// between, along with what Save compares the BOM against. None for no material.
    /// </summary>
    private void ShowDetails(RawMaterial? material, RawMaterialDetail detail)
    {
        ReplacePriceHistory(material, detail.PriceHistory);

        BomLineEdits.Clear();
        BomLineEdits.AddRange(detail.BomLines.Select(l => new IngredientLineRow(l.ComponentIngredient!, l.Grams)));
        _savedBomLines = CurrentBomLines();

        BrandPriceRows.Clear();
        BrandPriceRows.AddRange(detail.BrandPrices.Select(p =>
            new BrandPriceRow(this, p.Brand, p.Unit?.UnitOfMeasureId ?? PurchaseUnitChoice.BaseUnitKey, p.Price)));
        _savedBrandPriceCount = detail.BrandPrices.Count;

        UpdateCanChangeBaseUnit();
        RebuildBufferedCostUnitOptions();
        BufferedCostUnit = DearestBrandUnit();
        _detailMaterialId = material?.Id;
        AreDetailsReady = true;
    }

    /// <summary>The Price History grid, newest first, and the header's Last Updated from its latest entry.</summary>
    private void ReplacePriceHistory(RawMaterial? material, IReadOnlyList<RawMaterialPriceHistory> entries)
    {
        var baseUnitName = material is null ? string.Empty : BaseUnitText.NameOf(material);
        PriceHistory.Clear();
        PriceHistory.AddRange(entries.Select(e => new RawMaterialPriceRow(e, baseUnitName)));
        LastUpdatedText = PriceHistory.FirstOrDefault()?.Date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) ?? "—";
    }

    /// <summary>The form's alternative units, from <paramref name="material"/>'s saved ones (none for a new material).</summary>
    private void LoadUnitRows(RawMaterial? material)
    {
        // Copies, since the form edits its units in place and the cached material must stay as saved.
        // Each points at the Units of Measure list's own object, which refreshes keep current.
        var units = (material?.Units ?? []).Select(u => new RawMaterialUnit
        {
            Id = u.Id,
            RawMaterialId = u.RawMaterialId,
            UnitOfMeasureId = u.UnitOfMeasureId,
            UnitOfMeasure = UnitsOfMeasure.FirstOrDefault(m => m.Id == u.UnitOfMeasureId) ?? u.UnitOfMeasure,
            BaseUnitsPerUnit = u.BaseUnitsPerUnit,
            Description = u.Description,
        });

        _brandAddedUnits.Clear();
        ReplaceUnitRows([.. units], selectedUnit: null);
        RefreshBrandPriceRows();
        UpdateCanChangeBaseUnit();
    }

    /// <summary>Shows the base unit on the form's alternative units and in the purchase unit choices.</summary>
    private void RebuildUnitViews()
    {
        UpdateUnitRowsBaseUnit();
        RebuildPurchaseUnitOptions();
    }

    /// <summary>Tells each row the base unit, which its multiplier is in, without replacing the rows the grid is showing.</summary>
    private void UpdateUnitRowsBaseUnit()
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit);
        foreach (var row in UnitRows)
        {
            row.BaseUnitAbbreviation = baseUnitAbbreviation;
        }
    }

    private void ReplaceUnitRows(IReadOnlyList<RawMaterialUnit> units, RawMaterialUnit? selectedUnit)
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit);
        SyncUnitRows(() =>
        {
            SelectedUnitRow = null;
            UnitRows.Clear();
            UnitRows.AddRange(units.Select(u => new RawMaterialUnitRow(u, baseUnitAbbreviation, FindUnitOfMeasure)));
            SelectedUnitRow = UnitRows.FirstOrDefault(r => ReferenceEquals(r.Unit, selectedUnit));
        });
        CanAddUnit = UnitRows.Count < UnitConversionRules.MaxAlternativeUnits;
    }

    /// <summary>Changes the unit rows for the user without that counting as their edit (OnUnitRowsEdited).</summary>
    private void SyncUnitRows(Action change)
    {
        _syncingUnitRows = true;
        try
        {
            change();
        }
        finally
        {
            _syncingUnitRows = false;
        }
    }

    private UnitOfMeasure? FindUnitOfMeasure(int id) => UnitsOfMeasure.FirstOrDefault(u => u.Id == id);

    /// <summary>
    /// What an edit in the Alternative Units grid does: a brand bought in a unit whose multiplier changed there is repriced
    /// straight away, and the add row goes once there are two units.
    /// </summary>
    private void OnUnitRowsEdited()
    {
        CanAddUnit = UnitRows.Count < UnitConversionRules.MaxAlternativeUnits;
        RefreshBrandPriceRows();
        UpdatePurchaseCostTexts();
    }

    /// <summary>
    /// The checks the Alternative Units form's Apply used to make, now that units are typed straight into the grid: every row has a
    /// unit and a positive multiplier, none is the base unit or repeats another's unit, and there are at most two. Throws
    /// InvalidOperationException for the first problem, which RunSafelyAsync shows.
    /// </summary>
    private void EnsureValidUnitRows()
    {
        var units = UnitRows.Select(r => r.Unit).ToList();
        foreach (var unit in units)
        {
            if (unit.UnitOfMeasureId <= 0)
            {
                throw new InvalidOperationException("Pick a unit for every alternative unit, or remove the empty row.");
            }

            if (unit.BaseUnitsPerUnit <= 0)
            {
                // A unit added by picking it on a brand row gets its multiplier from that row's quantity.
                var where = _brandAddedUnits.Contains(unit) ? "Qty per Purchase Unit" : "Alternative Units";
                throw new InvalidOperationException($"Enter how many {BaseUnitAbbreviation} are in one {unit.UnitOfMeasure?.Name ?? "unit"} ({where}).");
            }

            var validation = _unitValidator.Validate(unit);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            }

            RawMaterialRules.EnsureNotTheBaseUnit(EditingType == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null, unit.UnitOfMeasureId);
            RawMaterialRules.EnsureUnitNotAlreadyAdded(units.Any(u => !ReferenceEquals(u, unit) && u.UnitOfMeasureId == unit.UnitOfMeasureId));
        }

        if (units.Count > UnitConversionRules.MaxAlternativeUnits)
        {
            UnitConversionRules.EnsureCanAddAlternativeUnit(units.Count - 1);
        }
    }

    private void UpdateCanChangeBaseUnit() =>
        CanChangeBaseUnit = SelectedRawMaterial is null
            || (SelectedRawMaterial.Units.Count == 0 && _savedBrandPriceCount == 0 && PriceHistory.Count == 0);

    /// <summary>
    /// The usage example's unit choices for <paramref name="material"/>. Keeps the unit picked before
    /// (<paramref name="kept"/>, matched by id — the base unit's is null) when there was one; otherwise
    /// starts on its largest alternative unit, else its base unit.
    /// </summary>
    private void ReplaceExampleUnitOptions(RawMaterial? material, RawMaterialUnitOption? kept)
    {
        ExampleUnitOptions.Clear();
        ExampleUnitOptions.AddRange(RawMaterialUnitOption.For(material));
        ExampleUnit = (kept is null ? null : ExampleUnitOptions.FirstOrDefault(o => o.UnitId == kept.UnitId))
            ?? ExampleUnitOptions.Skip(1).FirstOrDefault()
            ?? ExampleUnitOptions.FirstOrDefault();
    }

    /// <summary>
    /// The Purchase Cost Setup grid's unit choices: the base unit, then every unit of measure but one that is (or is named
    /// like) the base unit. Left alone when nothing changed, so the grid's open unit cell keeps its list.
    /// </summary>
    private void RebuildPurchaseUnitOptions()
    {
        var baseUnitId = EditingType == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null;
        List<PurchaseUnitChoice> choices =
        [
            new(PurchaseUnitChoice.BaseUnitKey, BaseUnitText.NameOf(EditingType, EditingBaseUnit), BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit)),
            .. UnitsOfMeasure
                .Where(u => u.Id != baseUnitId && !IsNamedLikeTheBaseUnit(u))
                .Select(u => new PurchaseUnitChoice(u.Id, u.Name, u.Abbreviation)),
        ];

        if (!choices.SequenceEqual(PurchaseUnitOptions))
        {
            PurchaseUnitOptions.Clear();
            PurchaseUnitOptions.AddRange(choices);
        }
    }

    private bool IsNamedLikeTheBaseUnit(UnitOfMeasure unit) =>
        string.Equals(unit.Name, BaseUnitText.NameOf(EditingType, EditingBaseUnit), StringComparison.OrdinalIgnoreCase)
        || string.Equals(unit.Abbreviation, BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit), StringComparison.OrdinalIgnoreCase);

    RawMaterialUnit? IBrandPriceRowHost.FindUnit(int unitOfMeasureId) =>
        UnitRows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unitOfMeasureId)?.Unit;

    void IBrandPriceRowHost.SetMultiplier(int unitOfMeasureId, decimal baseUnitsPerUnit)
    {
        if (UnitRows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unitOfMeasureId) is { } row)
        {
            SyncUnitRows(() => row.BaseUnitsPerUnit = baseUnitsPerUnit);
            RefreshBrandPriceRows();
            UpdatePurchaseCostTexts();
        }
    }

    decimal? IBrandPriceRowHost.BufferedCostPerBaseUnit =>
        EditingBufferedCost is > 0 ? EditingBufferedCost
        : DearestLiveCost() is { } cost ? RawMaterialRules.BufferedCost(cost, EditingCostBufferPercent ?? 0m)
        : null;

    /// <summary>
    /// What picking a unit on a brand row does: one the material doesn't have yet is added as an alternative unit, with its
    /// multiplier typed as the row's quantity, while there's room for another; and one added that way that no row is in
    /// any more goes. <paramref name="row"/> may still be the grid's add row, which isn't in BrandPriceRows yet.
    /// </summary>
    void IBrandPriceRowHost.OnUnitPicked(BrandPriceRow row)
    {
        DropUnusedAddedUnits(row);

        if (row.UnitKey is not { } key || key == PurchaseUnitChoice.BaseUnitKey || ((IBrandPriceRowHost)this).FindUnit(key) is not null)
        {
            return;
        }

        if (UnitRows.Count >= UnitConversionRules.MaxAlternativeUnits)
        {
            ErrorMessage = $"A material can have at most {UnitConversionRules.MaxAlternativeUnits} alternative units. "
                + "Pick one it already has, or remove one in Alternative Units first.";
            return;
        }

        var unit = new RawMaterialUnit { UnitOfMeasureId = key, UnitOfMeasure = FindUnitOfMeasure(key) };
        _brandAddedUnits.Add(unit);
        ReplaceUnitRows([.. UnitRows.Select(r => r.Unit), unit], SelectedUnitRow?.Unit);
        RefreshBrandPriceRows();
    }

    /// <summary>
    /// Takes back out of the alternative units each one added by picking it on a brand row that no row, nor
    /// <paramref name="pickedRow"/> (a row still being added), is in any more.
    /// </summary>
    private void DropUnusedAddedUnits(BrandPriceRow? pickedRow)
    {
        var usedKeys = BrandPriceRows.Append(pickedRow).Select(r => r?.UnitKey).ToHashSet();
        var unused = _brandAddedUnits.Where(u => !usedKeys.Contains(u.UnitOfMeasureId)).ToList();
        if (unused.Count == 0)
        {
            return;
        }

        foreach (var unit in unused)
        {
            _brandAddedUnits.Remove(unit);
        }

        ReplaceUnitRows([.. UnitRows.Select(r => r.Unit).Where(u => !unused.Contains(u))], SelectedUnitRow?.Unit);
        RefreshBrandPriceRows();
        UpdatePurchaseCostTexts();
    }

    /// <summary>Shows each brand row's quantity and buffered price as the units and buffer now are.</summary>
    private void RefreshBrandPriceRows()
    {
        foreach (var row in BrandPriceRows)
        {
            row.Refresh();
        }
    }

    /// <summary>
    /// The header's saved costs in the Per unit: to 4 decimals on the base unit, whose costs are fractions of a centavo,
    /// and to the centavo on any other.
    /// </summary>
    private void UpdateHeaderCostTexts()
    {
        var onBaseUnit = BufferedCostUnit is null or { Key: PurchaseUnitChoice.BaseUnitKey };
        var size = BufferedCostUnitSize();
        string Format(decimal costPerBaseUnit) => onBaseUnit ? $"₱{costPerBaseUnit:N4}" : $"₱{costPerBaseUnit * size:N2}";

        HeaderCostUnitText = BufferedCostUnit?.Abbreviation ?? BaseUnitAbbreviation;
        CurrentCostText = SelectedRawMaterial is { } material ? Format(material.CostPerBaseUnit) : "—";
        BufferedCostText = SelectedRawMaterial is { } saved ? Format(saved.BufferedCostPerBaseUnit) : "—";
    }

    /// <summary>
    /// The Buffered Cost unit picker's choices: the base unit, then each alternative unit with a multiplier. A picked unit
    /// that's gone is swapped for the base unit; one that's still there is kept, as the new list's own copy.
    /// </summary>
    private void RebuildBufferedCostUnitOptions()
    {
        List<PurchaseUnitChoice> choices =
        [
            new(PurchaseUnitChoice.BaseUnitKey, BaseUnitName, BaseUnitAbbreviation),
            .. UnitRows
                .Where(r => r.Unit is { UnitOfMeasureId: > 0, BaseUnitsPerUnit: > 0 } && FindUnitOfMeasure(r.Unit.UnitOfMeasureId) is not null)
                .Select(r => FindUnitOfMeasure(r.Unit.UnitOfMeasureId)!)
                .Select(u => new PurchaseUnitChoice(u.Id, u.Name, u.Abbreviation)),
        ];

        if (choices.SequenceEqual(BufferedCostUnitOptions))
        {
            return;
        }

        var picked = BufferedCostUnit;
        _rebuildingBufferedCostUnits = true;
        try
        {
            BufferedCostUnitOptions.Clear();
            BufferedCostUnitOptions.AddRange(choices);
            _bufferedCostUnit = choices.FirstOrDefault(c => c.Key == picked?.Key) ?? choices[0];
            this.RaisePropertyChanged(nameof(BufferedCostUnit));
        }
        finally
        {
            _rebuildingBufferedCostUnits = false;
        }
    }

    /// <summary>The unit of the dearest brand as the grid now is, among the picker's choices; the base unit while unpriced.</summary>
    private PurchaseUnitChoice DearestBrandUnit()
    {
        var priced = BrandPriceRows.Where(r => r.CostPerBaseUnit is not null).ToList();
        var key = RawMaterialRules.FindDearestBrandPrice([.. priced.Select(r => (r.Price!.Value, r.Quantity))]) is { } dearest
            ? priced[dearest].UnitKey
            : null;
        return BufferedCostUnitOptions.FirstOrDefault(c => c.Key == key) ?? BufferedCostUnitOptions[0];
    }

    /// <summary>The dearest brand per base unit as the grid now is, which is what Save costs the material at; null while unpriced.</summary>
    private decimal? DearestLiveCost()
    {
        var priced = BrandPriceRows.Where(r => r.CostPerBaseUnit is not null).ToList();
        return RawMaterialRules.FindDearestBrandPrice([.. priced.Select(r => (r.Price!.Value, r.Quantity))]) is { } dearest
            ? priced[dearest].CostPerBaseUnit
            : null;
    }

    /// <summary>
    /// The summary of the grid: the dearest brand per base unit, as Save will cost the material, and what the fixed
    /// buffered cost comes to over it.
    /// </summary>
    private void UpdatePurchaseCostTexts()
    {
        foreach (var row in BrandPriceRows)
        {
            row.RefreshBufferPercent();
        }

        RebuildBufferedCostUnitOptions();
        this.RaisePropertyChanged(nameof(EditingBufferedCostInUnit));
        UpdateHeaderCostTexts();

        var priced = BrandPriceRows.Where(r => r.CostPerBaseUnit is not null).ToList();
        if (RawMaterialRules.FindDearestBrandPrice([.. priced.Select(r => (r.Price!.Value, r.Quantity))]) is not { } dearest)
        {
            DearestPurchaseCost = null;
            PurchaseCostText = "—";
            PurchaseBufferedCostText = string.Empty;
            BufferedCostWarningText = string.Empty;
            PurchaseCostSourceText = string.Empty;
            return;
        }

        var cost = priced[dearest].CostPerBaseUnit!.Value;
        var unit = BufferedCostUnit?.Abbreviation ?? BaseUnitAbbreviation;
        var size = BufferedCostUnitSize();
        DearestPurchaseCost = cost;
        PurchaseCostText = $"₱{cost:N4}";
        if (EditingBufferedCost is > 0 and var buffered)
        {
            var over = RawMaterialRules.CostBufferPercentOf(cost, buffered);
            PurchaseBufferedCostText = $"₱{buffered * size:N2}/{unit} is {over:0.##}% over the dearest brand's ₱{cost * size:N2}/{unit}.";
            BufferedCostWarningText = buffered < cost
                ? "The dearest brand now costs more than the buffered cost. Recalculate to put the buffer back on top."
                : string.Empty;
        }
        else
        {
            PurchaseBufferedCostText = $"Save works it out as ₱{RawMaterialRules.BufferedCost(cost, EditingCostBufferPercent ?? 0m) * size:N2}/{unit}, the dearest brand with the buffer on top.";
            BufferedCostWarningText = string.Empty;
        }

        PurchaseCostSourceText = priced.Count > 1
            ? $"From {RawMaterialRules.NormalizeBrand(priced[dearest].Brand) ?? "the unnamed brand"}, the dearest of {priced.Count} brands."
            : string.Empty;
    }

    /// <summary>
    /// The brand rows as Save sends them. Each needs a unit and a price, and its quantity when the unit is new; a row
    /// with nothing typed into it is left out. Throws InvalidOperationException for the first problem, which
    /// RunSafelyAsync shows.
    /// </summary>
    private List<(string? Brand, int? UnitOfMeasureId, decimal Price)> CompleteBrandPrices()
    {
        var prices = new List<(string? Brand, int? UnitOfMeasureId, decimal Price)>();
        foreach (var row in BrandPriceRows)
        {
            var brand = RawMaterialRules.NormalizeBrand(row.Brand);
            if (brand is null && row.UnitKey is null && row.Price is null)
            {
                continue;
            }

            if (row.UnitKey is not { } key)
            {
                throw new InvalidOperationException(brand is null
                    ? "Pick the purchase unit every price is for, or remove the empty row."
                    : $"Pick the purchase unit '{brand}' is priced in.");
            }

            if (key != PurchaseUnitChoice.BaseUnitKey && ((IBrandPriceRowHost)this).FindUnit(key) is null)
            {
                throw new InvalidOperationException(
                    $"{(brand is null ? "A price" : $"'{brand}'")} is in a unit this material doesn't have. Pick another, or add it in Alternative Units.");
            }

            var validation = _brandPriceValidator.Validate(new RawMaterialBrandPrice { Brand = brand, Price = row.Price ?? 0m });
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            }

            prices.Add((brand, key == PurchaseUnitChoice.BaseUnitKey ? null : key, row.Price!.Value));
        }

        RawMaterialRules.EnsureDistinctBrandPrices(prices.Select(p => (p.Brand, p.UnitOfMeasureId)));
        return prices;
    }

    /// <summary>The BOM editor's lines when they differ from those last loaded, or null to leave the saved BOM as it is.</summary>
    private List<(int ComponentIngredientId, decimal Grams)>? ChangedBomLines()
    {
        var lines = CurrentBomLines();
        return lines.SequenceEqual(_savedBomLines) ? null : lines;
    }

    /// <summary>The BOM table's lines; a half-filled or repeated one is refused, naming the ingredient being made.</summary>
    private List<(int ComponentIngredientId, decimal Grams)> CurrentBomLines() =>
        [.. IngredientLineRow.CompleteLines(BomLineEdits, EditingName.Trim()).Select(l => (l.Ingredient.Id, l.Grams))];

    /// <summary>
    /// Re-fetches the material just saved and pushes it into the grid's cache and the selection. A fresh
    /// copy is always a new selection, so the form and its details reload from what was saved. A new material,
    /// saved for the first time, is still the one whose details show, not another material: they stay until the
    /// reload replaces them, as an existing one's do.
    /// </summary>
    private async Task ReloadSavedRawMaterialAsync(int id)
    {
        var saved = await _rawMaterialRepository.GetByIdAsync(id, CancellationToken.None);
        if (saved is not null)
        {
            _rawMaterialCache.AddOrUpdate(saved);
            _detailMaterialId = id;
            SelectedRawMaterial = saved;
        }
    }

    public void Dispose()
    {
        _rawMaterialCache.Dispose();
        _categoryCache.Dispose();
        _addNewCategoryOption.Dispose();
    }
}
