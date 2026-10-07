using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
/// half filled in; Save checks them all first. A standard unit of the base unit's kind (a kilogram of an ingredient) takes
/// its fixed multiplier as it's picked, and its multiplier can't be typed (StandardUnitRules).
/// </summary>
public sealed class RawMaterialUnitRow : ReactiveObject
{
    /// <summary>Looks up a unit of measure by id as the screen has it.</summary>
    private readonly Func<int, UnitOfMeasure?>? _findUnitOfMeasure;

    /// <summary>A standard unit's fixed multiplier for the screen's material as it now is, or null for one that's typed.</summary>
    private readonly Func<UnitOfMeasure, decimal?>? _standardMultiplier;

    /// <summary>An empty row, for the grid to compare an add row against (controls:GridAddNewRow.DropWhenUntouched).</summary>
    public RawMaterialUnitRow()
        : this(new RawMaterialUnit(), string.Empty, findUnitOfMeasure: null, standardMultiplier: null)
    {
    }

    /// <param name="findUnitOfMeasure">Resolves a picked unit of measure, so a row names its unit as soon as it's picked, even in the add row.</param>
    /// <param name="standardMultiplier">A standard unit's fixed multiplier for the material (StandardUnitRules), or null to type it.</param>
    public RawMaterialUnitRow(
        RawMaterialUnit unit,
        string baseUnitAbbreviation,
        Func<int, UnitOfMeasure?>? findUnitOfMeasure,
        Func<UnitOfMeasure, decimal?>? standardMultiplier)
    {
        Unit = unit;
        _baseUnitAbbreviation = baseUnitAbbreviation;
        _findUnitOfMeasure = findUnitOfMeasure;
        _standardMultiplier = standardMultiplier;
        ApplyStandardMultiplier();
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
            ApplyStandardMultiplier();
        }
    }

    /// <summary>Points the row at the screen's current copy of its unit of measure, so one renamed in Settings shows its new name.</summary>
    public void RefreshUnitOfMeasure()
    {
        if (UnitOfMeasureId is { } id && _findUnitOfMeasure?.Invoke(id) is { } refreshed && !ReferenceEquals(refreshed, Unit.UnitOfMeasure))
        {
            Unit.UnitOfMeasure = refreshed;
            RaiseUnitOfMeasureChanged();
            ApplyStandardMultiplier();
        }
    }

    /// <summary>The fixed multiplier of the row's unit when it's a standard unit of the base unit's kind; null while it's typed.</summary>
    public decimal? StandardMultiplier => Unit.UnitOfMeasure is { } unitOfMeasure ? _standardMultiplier?.Invoke(unitOfMeasure) : null;

    /// <summary>Whether the multiplier cell can be typed into (controls:GridCellEditing.AllowWhen): not for a standard unit.</summary>
    public bool CanTypeMultiplier => StandardMultiplier is null;

    /// <summary>
    /// Gives a standard unit its fixed multiplier: when it's picked, renamed into one in Settings, or the material's base
    /// unit or type changes under it. Anything else keeps the multiplier typed.
    /// </summary>
    public void ApplyStandardMultiplier()
    {
        if (StandardMultiplier is { } standard)
        {
            BaseUnitsPerUnit = standard;
        }

        this.RaisePropertyChanged(nameof(CanTypeMultiplier));
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
public sealed class RawMaterialsViewModel : ViewModelBase, IUndoScreen, IDisposable, IBrandPriceRowHost, ICategoryPickerScreen, IRecordScreen
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
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categoryOptions;

    /// <summary>Every raw material, for the usage example's picker.</summary>
    public ReadOnlyObservableCollection<RawMaterial> RawMaterials => _rawMaterials;

    /// <summary>The materials the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<RawMaterial> VisibleRawMaterials => _visibleRawMaterials;

    /// <summary>The Raw Materials list (MaterialListCard): VisibleRawMaterials filed in a folder for their type, then their category.</summary>
    public RawMaterialTree RawMaterialTree { get; }

    /// <summary>The view state key the open folders of the Raw Materials list are kept under (IViewStateStore).</summary>
    public const string OpenFoldersKey = "RawMaterials.OpenFolders";

    private readonly IViewStateStore _viewStateStore;
    private readonly ICostingDefaultsRepository _costingDefaultsRepository;

    // What a new material's Cost Buffer % starts at (Settings → Costing Defaults), as of the last LoadCommand.
    private decimal _defaultCostBufferPercent = CostingDefaults.DefaultCostBufferPercent;

    /// <summary>The open folders last written to the view state, so a write is only made for a change.</summary>
    private IReadOnlyList<string> _writtenOpenFolders;

    private readonly IDisposable _openFoldersWriter;

    /// <summary>Held while writing the open folders, which the throttled writer does off the UI thread and Dispose on it.</summary>
    private readonly Lock _openFoldersLock = new();

    /// <summary>
    /// The rows picked in the Raw Materials list, bound to the tree's SelectedItems, which it shares: one row, or several
    /// with Ctrl or Shift held. Its first is <see cref="SelectedTreeNode"/>. The tree changes it as rows are clicked, and
    /// this screen as SelectedRawMaterial changes (SyncTreeSelection), as the Products screen does its own.
    /// </summary>
    public ObservableCollection<object> SelectedTreeNodes { get; } = [];

    private object? _selectedTreeNode;
    /// <summary>
    /// The first row picked in the list: a material's (RawMaterialLeafNode), a folder's, or null. Picking a material's row
    /// picks the material; picking a folder's leaves the material showing as it is, and so do more rows picked with Ctrl.
    /// SelectedRawMaterial changed any other way (Back and Forward, a Save, New) moves the row to it. Setting it picks that
    /// row alone, as a click does.
    /// </summary>
    public object? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (value is null)
            {
                if (SelectedTreeNodes.Count > 0)
                {
                    SelectedTreeNodes.Clear();
                }
            }
            else if (SelectedTreeNodes.Count != 1 || !ReferenceEquals(SelectedTreeNodes[0], value))
            {
                SelectedTreeNodes.Clear();
                SelectedTreeNodes.Add(value);
            }
        }
    }

    /// <summary>The materials picked in the list, in the order they were picked.</summary>
    public IReadOnlyList<RawMaterialLeafNode> SelectedTreeMaterials => [.. SelectedTreeNodes.OfType<RawMaterialLeafNode>()];

    /// <summary>
    /// Set while the list's selection changes for a reason that's no pick of the user's: SyncTreeSelection moving it to
    /// SelectedRawMaterial, or the tree dropping a row from it as the row moves to another folder.
    /// </summary>
    private bool _isSyncingTreeSelection;

    private string? _moveNotice;
    /// <summary>
    /// What the last move in the list did, "Moved Flour to Ingredients › Dry Goods", on the list's notice bar with Undo
    /// beside it (UndoMoveCommand); null once it's undone or dismissed. The bar hides itself after a few seconds.
    /// </summary>
    public string? MoveNotice
    {
        get => _moveNotice;
        private set => this.RaiseAndSetIfChanged(ref _moveNotice, value);
    }

    /// <summary>Where the materials the last move took were before it: what Undo on the notice puts back.</summary>
    private IReadOnlyList<RawMaterialMove> _moveUndo = [];

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
    /// What the Category picker lists: the categories of the material's type. A name that isn't one of them is added
    /// by typing it (CommitCategoryTextAsync).
    /// </summary>
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
    /// What's typed into the Category picker (bound to its Text): settled into a category, or a new one, once the
    /// picker is left (CommitCategoryTextAsync).
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

    /// <summary>A material <see cref="OpenRawMaterial"/> was asked for before the list held it, for LoadCommand to open once it does.</summary>
    private int? _rawMaterialToOpen;

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
        set => this.RaiseAndSetIfChanged(ref _editingCategory, value);
    }

    /// <summary>The category the picker held when it took the cursor, for a cancelled prompt to put back (BeginCategoryEdit).</summary>
    private RawMaterialCategory? _categoryBeforeEdit;

    /// <summary>Set while CommitCategoryTextAsync runs: its prompt takes the cursor from the picker, which would settle it again.</summary>
    private bool _isCommittingCategory;

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

    /// <summary>
    /// The material's cost buffer; empty saves as 0%, since the box shows 0.00 when cleared. A new one starts at Settings'
    /// default (<see cref="CostingDefaults.CostBufferPercent"/>).
    /// </summary>
    private decimal? _editingCostBufferPercent = CostingDefaults.DefaultCostBufferPercent;
    public decimal? EditingCostBufferPercent
    {
        get => _editingCostBufferPercent;
        set => this.RaiseAndSetIfChanged(ref _editingCostBufferPercent, value);
    }

    /// <summary>
    /// The Cost Buffer % box. Typing it works the buffered cost out afresh: the dearest brand, as the grid now is, with
    /// the percent on top (nothing to work out from while no brand is priced). The other way round, the % follows the
    /// fixed amount over the dearest brand as prices are typed (UpdatePurchaseCostTexts).
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
    /// Purchased only: the fixed buffered cost per base unit, which brand prices don't move (the Cost Buffer % follows
    /// them instead). Typed, or worked out from the dearest brand when the percent is typed; a cleared box saves as 0,
    /// which Save then works out afresh from the prices (as for a material's first price).
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
    /// EditingBufferedCost in BufferedCostUnit, rounded for show as the screen's other costs are: to the centavo on an
    /// alternative unit (₱0.0518948/g is ₱1,297.37 a 25 kg bag), and to 4 decimals on the base unit, where a cost is a
    /// fraction of a centavo (₱0.0525/g would otherwise read as ₱0.05/g, and a new material starts there).
    /// What's typed is divided back to the base unit at full precision, so it isn't rounded, and the Cost Buffer % becomes
    /// what it is over the dearest brand, to the 4 decimals the percent keeps: ₱1,300 a bag over ₱1,250 is 4%.
    /// </summary>
    public decimal? EditingBufferedCostInUnit
    {
        get => EditingBufferedCost is { } cost
            ? Math.Round(cost * BufferedCostUnitSize(), IsBufferedCostOnBaseUnit ? 4 : 2)
            : null;
        set
        {
            // UpdatePurchaseCostTexts, which the change below reaches, works the % out from the new amount.
            EditingBufferedCost = value is { } typed ? typed / BufferedCostUnitSize() : null;
        }
    }

    /// <summary>How many base units BufferedCostUnit holds: 1 for the base unit.</summary>
    private decimal BufferedCostUnitSize() =>
        BufferedCostUnit is { Key: not PurchaseUnitChoice.BaseUnitKey } unit
        && UnitRows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unit.Key)?.Unit is { BaseUnitsPerUnit: > 0 } row
            ? row.BaseUnitsPerUnit
            : 1m;

    /// <summary>Whether BufferedCostUnit is the base unit, whose costs are shown to 4 decimals rather than to the centavo.</summary>
    private bool IsBufferedCostOnBaseUnit => BufferedCostUnit is null or { Key: PurchaseUnitChoice.BaseUnitKey };

    /// <summary>A cost per base unit in BufferedCostUnit, as the screen shows it: "0.0525" a gram, "1,312.50" a bag.</summary>
    private string FormatInBufferedCostUnit(decimal costPerBaseUnit) =>
        IsBufferedCostOnBaseUnit ? $"{costPerBaseUnit:N4}" : $"{costPerBaseUnit * BufferedCostUnitSize():N2}";

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

    /// <summary>The details tabs along the bottom: Cost Setup, Alternative Units, Usage Example, Used By, then Price History.</summary>
    public const int PriceHistoryTab = 4;

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

    private readonly ObservableAsPropertyHelper<string> _baseUnitName;
    /// <summary>The form's base unit name: "gram" for an ingredient, e.g. "centimeter" for ribbon.</summary>
    public string BaseUnitName => _baseUnitName.Value;

    private readonly ObservableAsPropertyHelper<string> _baseUnitAbbreviation;
    public string BaseUnitAbbreviation => _baseUnitAbbreviation.Value;

    private readonly ObservableAsPropertyHelper<string> _savedBaseUnitText;
    /// <summary>
    /// The Base Unit tile: "gram (g)" for an ingredient, and a packaging material's base unit as last saved, e.g.
    /// "centimeter (cm)", or "—" until its first Save. The unit is picked in the fields row (EditingBaseUnit), so
    /// the tile shows what's saved, as the cost tiles beside it do (2026-09-26, at the user's request).
    /// </summary>
    public string SavedBaseUnitText => _savedBaseUnitText.Value;

    private string _currentCostText = "—";
    /// <summary>
    /// The saved cost in the Purchase Cost Setup's Per unit (BufferedCostUnit): "0.0372" a gram, to 4 decimals, or
    /// "930.00" a bag, to 2; "—" for a material not saved yet.
    /// </summary>
    public string CurrentCostText
    {
        get => _currentCostText;
        private set => this.RaiseAndSetIfChanged(ref _currentCostText, value);
    }

    private string _bufferedCostText = "—";
    /// <summary>The saved buffered cost, as CurrentCostText: "0.0391" a gram, "976.50" a bag, or "—" for a material not saved yet.</summary>
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

    /// <summary>The day the material's cost last changed, or "—" for one whose cost has never moved.</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }


    private decimal _bomTotalWeight;
    /// <summary>
    /// Made only: the batch's yield as the form has it, Σ BOM grams less the weight loss, so it follows the grid and
    /// the Weight Loss % box as they're typed into. 0 while the BOM is empty.
    /// </summary>
    public decimal BomTotalWeight
    {
        get => _bomTotalWeight;
        private set => this.RaiseAndSetIfChanged(ref _bomTotalWeight, value);
    }

    private decimal _bomTotalCost;
    /// <summary>
    /// Made only: what the form's BOM costs at its components' buffered costs — the sum of the grid's Cost column.
    /// BomTotalCost ÷ BomTotalWeight is the buffered cost per gram Save stores, since a made ingredient adds no
    /// buffer of its own, which is why the header shows the two beside Buffered Cost per g.
    /// </summary>
    public decimal BomTotalCost
    {
        get => _bomTotalCost;
        private set => this.RaiseAndSetIfChanged(ref _bomTotalCost, value);
    }

    // Alternative Units: typed straight into the grid; Remove acts on the row picked.
    private RawMaterialUnitRow? _selectedUnitRow;
    public RawMaterialUnitRow? SelectedUnitRow
    {
        get => _selectedUnitRow;
        set => this.RaiseAndSetIfChanged(ref _selectedUnitRow, value);
    }

    /// <summary>
    /// The row the grid's add row edits (controls:GridAddNewRow.ItemFactory): one that names its unit as soon as it's
    /// picked and shows the current base unit, which a row the grid made itself couldn't.
    /// </summary>
    public Func<RawMaterialUnitRow> NewUnitRow => () => new RawMaterialUnitRow(new RawMaterialUnit(), BaseUnitAbbreviation, FindUnitOfMeasure, StandardMultiplierOf);

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
    /// <summary>What the dearest brand comes to per base unit, recalculated as the grid is typed into, e.g. "0.0372".</summary>
    public string PurchaseCostText
    {
        get => _purchaseCostText;
        private set => this.RaiseAndSetIfChanged(ref _purchaseCostText, value);
    }

    private string _purchaseBufferedCostText = string.Empty;
    /// <summary>What EditingBufferedCost comes to over the dearest brand, e.g. "0.0391/g is 5.11% over the dearest brand's 0.0372/g.", or empty while unpriced.</summary>
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
    /// <summary>The dearest brand per base unit as the grid is typed into, which the buffer percent goes on top of; null while unpriced.</summary>
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
    public ReactiveCommand<RxVoid, RxVoid> DeleteUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateExampleCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveBrandPriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveBomLineCommand { get; }

    /// <summary>Files materials under another category, saved at once: a drag in the list, or its menu's Move To.</summary>
    public ReactiveCommand<IReadOnlyList<RawMaterialMove>, RxVoid> MoveMaterialsCommand { get; }

    /// <summary>Puts back where the last move took the materials from (the notice bar's Undo).</summary>
    public ReactiveCommand<RxVoid, RxVoid> UndoMoveCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> DismissMoveNoticeCommand { get; }

    /// <summary>
    /// The list's New … Here: a new material of the folder's type, in its category when it's a category's folder. With no
    /// folder (the list's empty space), a new ingredient, as New starts one.
    /// </summary>
    public ReactiveCommand<RawMaterialFolderNode?, RxVoid> NewMaterialInCommand { get; }

    /// <summary>The list's Rename: asks for a new name and saves it at once.</summary>
    public ReactiveCommand<RawMaterialLeafNode, RxVoid> RenameMaterialCommand { get; }

    /// <summary>The list's Delete, as the header's: asks first.</summary>
    public ReactiveCommand<RawMaterialLeafNode, RxVoid> DeleteMaterialCommand { get; }

    /// <summary>The list's New Category, for materials of the type given.</summary>
    public ReactiveCommand<RawMaterialType, RxVoid> NewCategoryCommand { get; }

    public ReactiveCommand<RawMaterialFolderNode, RxVoid> RenameCategoryCommand { get; }

    /// <summary>The list's Delete Category, offered only for a category no material is in.</summary>
    public ReactiveCommand<RawMaterialFolderNode, RxVoid> DeleteCategoryCommand { get; }

    /// <summary>Opens every folder of a type's, or of both types' with no folder given.</summary>
    public ReactiveCommand<RawMaterialFolderNode?, RxVoid> ExpandAllCommand { get; }

    /// <summary>Closes every category folder of a type's, or of both types' with no folder given.</summary>
    public ReactiveCommand<RawMaterialFolderNode?, RxVoid> CollapseAllCommand { get; }

    /// <summary>
    /// A name asked for by the list's menu: a material's new name, or a category's. Output: the name, or null if the
    /// prompt was cancelled. The View shows a TextPromptWindow for it.
    /// </summary>
    public Interaction<TextPromptRequest, string?> PromptForName { get; } = new();

    private IReadOnlyList<LinkingProduct> _usedBy = [];
    /// <summary>
    /// The products the material showing goes into, by name (IRawMaterialRepository.GetUsedByAsync): one tile each on the
    /// Used By tab, which opens it. Empty for a new material.
    /// </summary>
    public IReadOnlyList<LinkingProduct> UsedBy
    {
        get => _usedBy;
        private set => this.RaiseAndSetIfChanged(ref _usedBy, value);
    }

    private string _usedByText = NotUsedText;
    /// <summary>The caption under the Used By tiles: what they mean for a change of cost, or that nothing uses the material.</summary>
    public string UsedByText
    {
        get => _usedByText;
        private set => this.RaiseAndSetIfChanged(ref _usedByText, value);
    }

    private const string NotUsedText = "No product uses it yet.";

    private const string UsedText =
        "These products use it in their recipe or packaging, directly or through a made or linked ingredient, so a change to its cost reaches each of them.";

    /// <summary>A Used By tile's click: asks for the product with this id to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ReactiveCommand<int, RxVoid> OpenProductCommand { get; }

    private readonly Subject<int> _productOpenRequests = new();

    /// <summary>
    /// The products Used By tiles ask to open, by id. The main window, which owns the tabs, subscribes and opens each on
    /// the Products screen, as it does for Sub-Assemblies' tiles.
    /// </summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>
    /// The form's undo and redo stacks, the same shape as Products'. One step is one finished change. The price
    /// history is left out: its Delete writes to the database on the spot, so there is nothing unsaved to put back.
    /// Cleared whenever the form stops being the one it recorded: another material picked, a new one, or a Save.
    /// </summary>
    public UndoHistory<RawMaterialFormSnapshot> History { get; }

    public ReactiveCommand<RxVoid, RxVoid> UndoCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RedoCommand { get; }

    /// <summary>
    /// Input: the name typed into the Category picker that isn't a category yet, which the prompt starts with so it
    /// needn't be typed twice. Output: the name to add it as, or null if it was cancelled. The View registers a
    /// handler that shows a prompt dialog (Views/TextPromptWindow).
    /// </summary>
    public Interaction<string, string?> PromptForNewCategory { get; } = new();

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
        IViewStateStore viewStateStore,
        ICostingDefaultsRepository costingDefaultsRepository,
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
        _viewStateStore = viewStateStore;
        _costingDefaultsRepository = costingDefaultsRepository;

        // The list's folders open as they were left last time, and the ones open are written for next time once they've
        // settled after a change, off the UI thread, as on the Products screen; Dispose writes any change still waiting.
        RawMaterialTree = new RawMaterialTree(viewStateStore.ReadList(OpenFoldersKey));

        // As they start, so nothing is written until a folder is opened or closed, not even the first time.
        _writtenOpenFolders = RawMaterialTree.OpenFolderKeys();
        _openFoldersWriter = RawMaterialTree.ExpansionChanged
            .Select(_ => RawMaterialTree.OpenFolderKeys())
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(WriteOpenFolders);

        SelectedTreeNodes.CollectionChanged += (_, _) => TreeSelectionChanged();

        var byName = SortExpressionComparer<RawMaterial>.Ascending(m => m.Name);

        // The list's tree follows both lists once each change has reached them: a material added, removed, renamed, moved
        // or filtered out, and a category added, renamed or deleted. The categories are bound first, as the tree reads both.
        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe(_ => RefreshTree());

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
            .Subscribe(_ => RefreshTree());

        // The search decides whether empty folders show, even when it leaves the same materials showing.
        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => RefreshTree());

        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Subscribe(_ => SyncTreeSelection());

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

        // The picker offers only the categories of the material's own type, which is all it can be saved in.
        _categoryCache.Connect()
            .Filter(this.WhenAnyValue(x => x.EditingType).Select(type => (Func<RawMaterialCategory, bool>)(c => c.Type == type)))
            .SortAndBind(out _categoryOptions, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();

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

        _baseUnitName = this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit,
                (type, baseUnit) => BaseUnitText.NameOf(type, baseUnit))
            .ToProperty(this, x => x.BaseUnitName, "gram");

        _baseUnitAbbreviation = this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit,
                (type, baseUnit) => BaseUnitText.AbbreviationOf(type, baseUnit))
            .ToProperty(this, x => x.BaseUnitAbbreviation, "g");

        _savedBaseUnitText = this.WhenAnyValue(x => x.EditingType, x => x.SelectedRawMaterial, SavedBaseUnitTextOf)
            .ToProperty(this, x => x.SavedBaseUnitText, "gram (g)");

        // Nothing is recorded until a material's details have arrived, or while the screen is being written to
        // wholesale: the brand prices and the BOM land a moment after the pick (AreDetailsReady), and a step taken
        // before they do would put the empty grids back over them.
        History = new UndoHistory<RawMaterialFormSnapshot>(
            CaptureSnapshot, (before, after) => before.ShowsSameAs(after), () => AreDetailsReady && !IsBusyApplying);

        UndoCommand = ReactiveCommand.Create(() => { TryUndo(); }, History.WhenAnyValue(h => h.CanUndo));
        RedoCommand = ReactiveCommand.Create(() => { TryRedo(); }, History.WhenAnyValue(h => h.CanRedo));

        // Save offers itself only when the form holds something the saved material doesn't. Measured on every
        // change rather than only when an undo step settles: a step closes on the way out of a field, and a Save
        // button still disabled under the cursor would swallow the very click that enabled it. A grid's add row
        // is the same story the other way round - the grid raises nothing when it commits, so the step stays open.
        this.Changed.Subscribe(_ => History.RefreshDirty());
        WatchForSave(UnitRows);
        WatchForSave(BrandPriceRows);
        WatchForSave(BomLineEdits);

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

        // Each brand's buffered price follows the fixed buffered cost. Only typing the percent moves the amount (TypedCostBufferPercent).
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

        // A made ingredient starts at 3% weight loss (2026-09-21, at the user's request: a small shrink is the
        // normal case in the shop), and the box stays freely editable — typing 0 is a legitimate custom value.
        // Only when the box is empty, so a saved material keeps what it was saved with.
        this.WhenAnyValue(x => x.EditingSource)
            .Where(source => source == RawMaterialSource.Made && !IsBusyApplying)
            .Subscribe(_ => EditingWeightLossPercent ??= 3m);

        // The made-ingredient tiles follow the Weight Loss % box, which moves the yield the cost is spread over.
        this.WhenAnyValue(x => x.EditingWeightLossPercent)
            .Where(_ => !IsBusyApplying)
            .Subscribe(_ => RecomputeBomTotals());

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
        // second pick never gets the first one's details added to its own. One that fails shows why, and the next pick
        // loads as usual.
        LoadLatest(this.WhenAnyValue(x => x.SelectedRawMaterial).Where(_ => !IsBusyApplying), LoadDetailsAsync);

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

        // The header's Total Weight and Total Cost follow the BOM as it's typed into: Cost covers both a component
        // picked and grams typed, as the sub-assembly form's batch totals do.
        BomLineEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.Cost)
            .Where(_ => !IsRestoring)
            .Subscribe(_ => RecomputeBomTotals());

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
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var rawMaterials = await _rawMaterialRepository.GetAllAsync(CancellationToken.None);
            var costingDefaults = await _costingDefaultsRepository.GetAsync(CancellationToken.None);

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

                // Resolve points each row at the refreshed component, so this is what carries a component repriced
                // elsewhere into the header's totals: the change set above is suppressed while a refresh applies.
                RecomputeBomTotals();

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

                RefreshDefaultCostBuffer(costingDefaults.CostBufferPercent);

                var previousExample = ExampleRawMaterial;
                ExampleRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == exampleMaterialId);
                if (!ReferenceEquals(previousExample, ExampleRawMaterial))
                {
                    ReplaceExampleUnitOptions(ExampleRawMaterial, exampleUnit);
                    ExampleResult = null;
                }
            });

            // Products may have started or stopped using the material on another tab. Kept only if the details
            // showing are still that material's once the read is back.
            if (_detailMaterialId is { } detailMaterialId)
            {
                var usedBy = await _rawMaterialRepository.GetUsedByAsync(detailMaterialId, CancellationToken.None);
                if (_detailMaterialId == detailMaterialId)
                {
                    ShowUsedBy(usedBy);
                }
            }

            // Outside ApplyRefresh, whose selection changes only re-point: this one has to load the material's form.
            if (_rawMaterialToOpen is { } rawMaterialId)
            {
                _rawMaterialToOpen = null;
                TrySelectRawMaterial(rawMaterialId);
            }
        }));

        NewCommand = ReactiveCommand.Create(() => ClearForm());

        OpenProductCommand = ReactiveCommand.Create<int>(_productOpenRequests.OnNext);

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
        }),
        History.WhenAnyValue(h => h.IsDirty));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => DeleteRawMaterialAsync(SelectedRawMaterial!),
            this.WhenAnyValue(x => x.SelectedRawMaterial).Select(material => material is not null));

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

        RemoveBomLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => BomLineEdits.Remove(SelectedBomLineEdit!)),
            this.WhenAnyValue(x => x.SelectedBomLineEdit).Select(line => line is not null));

        MoveMaterialsCommand = ReactiveCommand.CreateFromTask<IReadOnlyList<RawMaterialMove>>(
            moves => RunSafelyAsync(() => MoveMaterialsAsync(moves, isUndo: false)));
        var hasMoveNotice = this.WhenAnyValue(x => x.MoveNotice).Select(notice => notice is not null);
        UndoMoveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(() => MoveMaterialsAsync(_moveUndo, isUndo: true)), hasMoveNotice);
        DismissMoveNoticeCommand = ReactiveCommand.Create(() => { MoveNotice = null; });
        NewMaterialInCommand = ReactiveCommand.Create<RawMaterialFolderNode?>(folder => ClearForm(
            folder?.Type ?? RawMaterialType.Ingredient, folder?.CategoryId));
        RenameMaterialCommand = ReactiveCommand.CreateFromTask<RawMaterialLeafNode>(RenameMaterialAsync);
        DeleteMaterialCommand = ReactiveCommand.CreateFromTask<RawMaterialLeafNode>(leaf => DeleteRawMaterialAsync(leaf.RawMaterial));
        NewCategoryCommand = ReactiveCommand.CreateFromTask<RawMaterialType>(NewCategoryAsync);
        RenameCategoryCommand = ReactiveCommand.CreateFromTask<RawMaterialFolderNode>(RenameCategoryAsync);
        DeleteCategoryCommand = ReactiveCommand.CreateFromTask<RawMaterialFolderNode>(DeleteCategoryAsync);
        ExpandAllCommand = ReactiveCommand.Create<RawMaterialFolderNode?>(RawMaterialTree.ExpandAll);
        CollapseAllCommand = ReactiveCommand.Create<RawMaterialFolderNode?>(RawMaterialTree.CollapseAll);
    }

    /// <summary>
    /// "Egg costs 0.0900/g, buffered 0.0990/g. At 70% of its weight for 30% of its cost, this costs 0.0386/g, buffered 0.0424/g."
    /// Worked out with the same rule the cascade saves with, from the linked-to ingredient's stored costs: the
    /// same share of its true cost and of its (fixed) buffered cost.
    /// </summary>
    private static string LinkedCostPreview(RawMaterial? linkedTo, decimal? weightShare, decimal? costShare)
    {
        if (linkedTo is null)
        {
            return "Pick the ingredient this one is a part of.";
        }

        var linkedToText = $"{linkedTo.Name} costs {linkedTo.CostPerBaseUnit:N4}/g, buffered {linkedTo.BufferedCostPerBaseUnit:N4}/g.";
        if (weightShare is not (> 0 and <= 100) || costShare is not (> 0 and <= 100))
        {
            return $"{linkedToText} Enter this part's share of its weight and of its cost.";
        }

        var cost = RawMaterialRules.LinkedCost(linkedTo.CostPerBaseUnit, weightShare.Value, costShare.Value);
        var buffered = RawMaterialRules.LinkedCost(linkedTo.BufferedCostPerBaseUnit, weightShare.Value, costShare.Value);
        return $"{linkedToText} At {weightShare:0.##}% of its weight for {costShare:0.##}% of its cost, this costs {cost:N4}/g, buffered {buffered:N4}/g.";
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

    /// <summary>
    /// Follows <paramref name="rows"/> for Save's sake: a row added, removed or replaced can be something to
    /// save. Watched through the collection's own PropertyChanged rather than its CollectionChanged, because an
    /// ObservableCollection only enforces its re-entrancy guard once more than one handler is listening — and
    /// these collections are changed from inside their own notifications, so a second CollectionChanged listener
    /// would turn a change the guard tolerates today into an exception. What's typed into a row reaches Save
    /// another way: the grid closes the cell's undo step, which re-reads whether there's anything to save.
    /// </summary>
    private void WatchForSave(INotifyPropertyChanged rows) => rows.PropertyChanged += (_, _) => History.RefreshDirty();

    void IUndoRecorder.BeginUndoStep() => History.Begin();

    void IUndoRecorder.EndUndoStep() => History.End();

    bool IUndoRecorder.IsEditPending
    {
        get => History.IsEditPending;
        set => History.IsEditPending = value;
    }

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
                        FindUnitOfMeasure,
                        StandardMultiplierOf));
                }
            });

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
        // Once, from the restored form: the change set and the Weight Loss % subscription are both suppressed while
        // a restore applies, so without this the tiles would keep the figures from before the undo.
        RecomputeBomTotals();
    }

    /// <summary>
    /// An empty form, for New and after Delete. It starts as a purchased ingredient, the usual kind, but
    /// the category, brand prices and the usage example's material start empty, so none is saved or
    /// costed by accident. The numbers keep a value: the cost buffer is Settings' default, and the usage example's
    /// quantity stays. The list's New … Here starts it as <paramref name="type"/> in <paramref name="categoryId"/>
    /// instead, set before the form is settled, so it isn't taken for an edit to save.
    /// </summary>
    private void ClearForm(RawMaterialType type = RawMaterialType.Ingredient, int? categoryId = null)
    {
        History.Clear();
        _editingId = 0;
        SelectedRawMaterial = null;
        CanChangeTypeAndSource = true;
        EditingName = string.Empty;
        EditingType = type;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId && c.Type == type);
        EditingSource = RawMaterialSource.Purchased;
        EditingWeightLossPercent = null;
        EditingLinkedTo = null;
        EditingWeightSharePercent = null;
        EditingCostSharePercent = null;
        EditingCostBufferPercent = _defaultCostBufferPercent;
        EditingBufferedCost = null;
        EditingBaseUnit = null;
        ExampleRawMaterial = null;
        ClearDetails();
    }

    /// <summary>
    /// Takes up Settings' cost buffer for a new material as LoadCommand read it. A new material still showing the one it
    /// started with takes the new one, and what Save measures against follows it, since the change isn't the user's; one
    /// whose buffer was typed keeps it.
    /// </summary>
    private void RefreshDefaultCostBuffer(decimal defaultCostBufferPercent)
    {
        var untouchedNewMaterial = _editingId == 0 && EditingCostBufferPercent == _defaultCostBufferPercent;
        _defaultCostBufferPercent = defaultCostBufferPercent;
        if (untouchedNewMaterial)
        {
            EditingCostBufferPercent = defaultCostBufferPercent;
            History.Rebase((settled, now) => settled with { CostBufferPercent = now.CostBufferPercent });
        }
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

    /// <summary>A material's price history, brand prices, BOM lines and the products using it, as LoadDetailsAsync reads them.</summary>
    private sealed record RawMaterialDetail(
        IReadOnlyList<RawMaterialPriceHistory> PriceHistory,
        IReadOnlyList<RawMaterialBrandPrice> BrandPrices,
        IReadOnlyList<IngredientBomLine> BomLines,
        IReadOnlyList<LinkingProduct> UsedBy)
    {
        public static readonly RawMaterialDetail None = new([], [], [], []);
    }

    /// <summary>
    /// Only the repositories: this runs off the UI thread. A purchased material has prices, a made one a BOM, and a linked
    /// one neither; any of them can be used by products.
    /// </summary>
    private async Task<RawMaterialDetail> ReadDetailAsync(RawMaterial material, CancellationToken cancellationToken)
    {
        var usedBy = await _rawMaterialRepository.GetUsedByAsync(material.Id, cancellationToken);
        return material.Source switch
        {
            RawMaterialSource.Purchased => new RawMaterialDetail(
                await _priceHistoryRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                await _brandPriceRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                [],
                usedBy),
            RawMaterialSource.Made => new RawMaterialDetail([], [], await _bomLineRepository.GetForIngredientAsync(material.Id, cancellationToken), usedBy),
            _ => RawMaterialDetail.None with { UsedBy = usedBy },
        };
    }

    /// <summary>
    /// Puts <paramref name="material"/>'s details in place of whatever the grids held, in one pass, so nothing is drawn in
    /// between, along with what Save compares the BOM against. None for no material.
    /// </summary>
    private void ShowDetails(RawMaterial? material, RawMaterialDetail detail)
    {
        ReplacePriceHistory(material, detail.PriceHistory);
        ShowUpdatedAt(material);

        BomLineEdits.Clear();
        BomLineEdits.AddRange(detail.BomLines.Select(l => new IngredientLineRow(l.ComponentIngredient!, l.Grams)));
        _savedBomLines = CurrentBomLines();

        BrandPriceRows.Clear();
        BrandPriceRows.AddRange(detail.BrandPrices.Select(p =>
            new BrandPriceRow(this, p.Brand, p.Unit?.UnitOfMeasureId ?? PurchaseUnitChoice.BaseUnitKey, p.Price)));
        _savedBrandPriceCount = detail.BrandPrices.Count;

        ShowUsedBy(detail.UsedBy);

        UpdateCanChangeBaseUnit();
        RebuildBufferedCostUnitOptions();
        BufferedCostUnit = DearestBrandUnit();
        _detailMaterialId = material?.Id;
        AreDetailsReady = true;
        RecomputeBomTotals();

        // Everything the form holds is now the material's own, so this is what Save's dirty check measures
        // against. Taken here rather than where the form is filled in: the units, brand prices and BOM land a
        // moment after the rest, and a form measured before them would read as changed when they arrived.
        History.MarkClean();
    }

    private void ShowUsedBy(IReadOnlyList<LinkingProduct> products)
    {
        UsedBy = products;
        UsedByText = products.Count == 0 ? NotUsedText : UsedText;
    }

    /// <summary>The Price History grid, newest first.</summary>
    private void ReplacePriceHistory(RawMaterial? material, IReadOnlyList<RawMaterialPriceHistory> entries)
    {
        var baseUnitName = material is null ? string.Empty : BaseUnitText.NameOf(material);
        PriceHistory.Clear();
        PriceHistory.AddRange(entries.Select(e => new RawMaterialPriceRow(e, baseUnitName)));
    }

    /// <summary>
    /// The Last Updated tile: the day the material's cost last changed, and nothing else (2026-09-21, at the
    /// user's request, replacing a two-line tile that showed the save date over the cost date). Until CostUpdatedAt
    /// was stored the tile read the newest price history entry's date, which a made or linked ingredient never
    /// has, so it showed "—" for them however recently their cost had moved. The material also carries UpdatedAt,
    /// the day the form last saved it; nothing shows it.
    /// </summary>
    private void ShowUpdatedAt(RawMaterial? material) =>
        LastUpdatedText = material?.CostUpdatedAt?.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) ?? "—";

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

    /// <summary>
    /// Tells each row the base unit, which its multiplier is in, without replacing the rows the grid is showing. A standard
    /// unit's fixed multiplier follows it: a new packaging material's inch is 25.4 over millimetres, 2.54 over centimetres.
    /// </summary>
    private void UpdateUnitRowsBaseUnit()
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit);
        foreach (var row in UnitRows)
        {
            row.BaseUnitAbbreviation = baseUnitAbbreviation;
            row.ApplyStandardMultiplier();
        }
    }

    private void ReplaceUnitRows(IReadOnlyList<RawMaterialUnit> units, RawMaterialUnit? selectedUnit)
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit);
        SyncUnitRows(() =>
        {
            SelectedUnitRow = null;
            UnitRows.Clear();
            UnitRows.AddRange(units.Select(u => new RawMaterialUnitRow(u, baseUnitAbbreviation, FindUnitOfMeasure, StandardMultiplierOf)));
            SelectedUnitRow = UnitRows.FirstOrDefault(r => ReferenceEquals(r.Unit, selectedUnit));
        });
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

    /// <summary>A standard unit's fixed multiplier for the material being edited (StandardUnitRules), or null for one that's typed.</summary>
    private decimal? StandardMultiplierOf(UnitOfMeasure unit) => StandardUnitRules.BaseUnitsPerUnit(unit, EditingType, EditingBaseUnit);

    /// <summary>
    /// What an edit in the Alternative Units grid does: a brand bought in a unit whose multiplier changed there is repriced
    /// straight away.
    /// </summary>
    private void OnUnitRowsEdited()
    {
        RefreshBrandPriceRows();
        UpdatePurchaseCostTexts();
    }

    /// <summary>
    /// The checks the Alternative Units form's Apply used to make, now that units are typed straight into the grid: every row has a
    /// unit and a positive multiplier, none is the base unit or repeats another's unit. Throws
    /// InvalidOperationException for the first problem, which RunSafelyAsync shows.
    /// </summary>
    private void EnsureValidUnitRows()
    {
        var units = UnitRows.Select(r => r.Unit).ToList();
        foreach (var unit in units)
        {
            if (unit.UnitOfMeasureId <= 0)
            {
                throw new BusinessRuleException("Pick a unit for every alternative unit, or remove the empty row.");
            }

            if (unit.BaseUnitsPerUnit <= 0)
            {
                // A unit added by picking it on a brand row gets its multiplier from that row's quantity.
                var where = _brandAddedUnits.Contains(unit) ? "Qty per Purchase Unit" : "Alternative Units";
                throw new BusinessRuleException($"Enter how many {BaseUnitAbbreviation} are in one {unit.UnitOfMeasure?.Name ?? "unit"} ({where}).");
            }

            var validation = _unitValidator.Validate(unit);
            if (!validation.IsValid)
            {
                throw new BusinessRuleException(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            }

            RawMaterialRules.EnsureNotTheBaseUnit(EditingType == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null, unit.UnitOfMeasureId);
            RawMaterialRules.EnsureUnitNotAlreadyAdded(units.Any(u => !ReferenceEquals(u, unit) && u.UnitOfMeasureId == unit.UnitOfMeasureId));
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

    bool IBrandPriceRowHost.IsMultiplierFixed(int unitOfMeasureId) =>
        UnitRows.FirstOrDefault(r => r.Unit.UnitOfMeasureId == unitOfMeasureId) is { CanTypeMultiplier: false };

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
    /// multiplier typed as the row's quantity; and one added that way that no row is in
    /// any more goes. <paramref name="row"/> may still be the grid's add row, which isn't in BrandPriceRows yet.
    /// </summary>
    void IBrandPriceRowHost.OnUnitPicked(BrandPriceRow row)
    {
        DropUnusedAddedUnits(row);

        if (row.UnitKey is not { } key || key == PurchaseUnitChoice.BaseUnitKey || ((IBrandPriceRowHost)this).FindUnit(key) is not null)
        {
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
    /// SavedBaseUnitText. The saved base unit is looked up in the Units of Measure list, as the form's picker is, since
    /// the list keeps it current through a rename; the material's own BaseUnit is the fallback before the list loads.
    /// </summary>
    private string SavedBaseUnitTextOf(RawMaterialType type, RawMaterial? saved)
    {
        if (type != RawMaterialType.Packaging)
        {
            return "gram (g)";
        }

        var unit = saved?.BaseUnitId is { } id ? UnitsOfMeasure.FirstOrDefault(u => u.Id == id) ?? saved.BaseUnit : null;
        return unit is null ? "—" : $"{unit.Name} ({unit.Abbreviation})";
    }

    /// <summary>
    /// The header's saved costs in the Per unit: to 4 decimals on the base unit, whose costs are fractions of a centavo,
    /// and to the centavo on any other.
    /// </summary>
    private void UpdateHeaderCostTexts()
    {
        HeaderCostUnitText = BufferedCostUnit?.Abbreviation ?? BaseUnitAbbreviation;
        CurrentCostText = SelectedRawMaterial is { } material ? FormatInBufferedCostUnit(material.CostPerBaseUnit) : "—";

        // A made ingredient's buffered cost follows the form's BOM instead of the saved row (2026-09-21, at the
        // user's request), so the Total Cost and Total Weight tiles beside it always divide out to it as the grid
        // is typed into; Save then stores the same figure. Every other material shows what was last saved.
        BufferedCostText = IsEditingMade
            ? FormatInBufferedCostUnit(BomTotalWeight > 0 ? BomTotalCost / BomTotalWeight : 0m)
            : SelectedRawMaterial is { } saved ? FormatInBufferedCostUnit(saved.BufferedCostPerBaseUnit) : "—";
    }

    /// <summary>
    /// The made-ingredient tiles, worked out from the FORM's BOM rows and Weight Loss % box: the yield comes from
    /// IngredientBomRules.CalculateCost, the same formula IngredientCostPropagator saves with, so the tiles and the
    /// stored cost can't drift apart, and the cost is the sum of the rows' own Cost — the grid's Cost column, at the
    /// components' buffered costs. A row with no component picked counts for neither. Left alone until the details
    /// are this material's (AreDetailsReady), since a pick loads the form before the grids.
    /// </summary>
    private void RecomputeBomTotals()
    {
        if (!AreDetailsReady)
        {
            return;
        }

        var (outputGrams, _) = IngredientBomRules.CalculateCost(
            [.. BomLineEdits
                .Where(l => l.Ingredient is not null)
                .Select(l => (Grams: l.Grams ?? 0m, ComponentCostPerGram: l.Ingredient!.BufferedCostPerBaseUnit))],
            EditingWeightLossPercent ?? 0m);

        BomTotalWeight = outputGrams;
        BomTotalCost = BomLineEdits.Sum(l => l.Cost);
        UpdateHeaderCostTexts();
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
        DearestPurchaseCost = cost;
        PurchaseCostText = $"{cost:N4}";
        if (EditingBufferedCost is > 0 and var buffered)
        {
            var over = RawMaterialRules.CostBufferPercentOf(cost, buffered);
            // The % box is what the fixed amount now comes to over the dearest brand, so a price change moves the % while
            // the amount stays put (2026-09-21, at the user's request). Held to the 0-100 the box, the validator and the
            // check constraint allow, so a buffer that's gone (or more than double) pins it; the line below and the
            // warning say where it really stands. Typing the % still works the other way, through TypedCostBufferPercent.
            EditingCostBufferPercent = Math.Clamp(Math.Round(over, 4), 0m, 100m);
            PurchaseBufferedCostText = $"{FormatInBufferedCostUnit(buffered)}/{unit} is {over:0.##}% over the dearest brand's {FormatInBufferedCostUnit(cost)}/{unit}.";
            // Compared to the 4 places costs per base unit are shown to: a saved buffered cost comes back cut to its
            // column's 20 places, so a repeating one (170/240) would otherwise read as a hair under an unchanged dearest brand.
            BufferedCostWarningText = Math.Round(buffered, 4) < Math.Round(cost, 4)
                ?"The dearest brand now costs more than the buffered cost. Type the Cost Buffer % again to put the buffer back on top."
                : string.Empty;
        }
        else
        {
            PurchaseBufferedCostText = $"Save works it out as {FormatInBufferedCostUnit(RawMaterialRules.BufferedCost(cost, EditingCostBufferPercent ?? 0m))}/{unit}, the dearest brand with the buffer on top.";
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
                throw new BusinessRuleException(brand is null
                    ? "Pick the purchase unit every price is for, or remove the empty row."
                    : $"Pick the purchase unit '{brand}' is priced in.");
            }

            if (key != PurchaseUnitChoice.BaseUnitKey && ((IBrandPriceRowHost)this).FindUnit(key) is null)
            {
                throw new BusinessRuleException(
                    $"{(brand is null ? "A price" : $"'{brand}'")} is in a unit this material doesn't have. Pick another, or add it in Alternative Units.");
            }

            var validation = _brandPriceValidator.Validate(new RawMaterialBrandPrice { Brand = brand, Price = row.Price ?? 0m });
            if (!validation.IsValid)
            {
                throw new BusinessRuleException(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
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

    /// <summary>
    /// Opens a material as picking it in the list would, for another screen (the main window's Back and Forward, and a
    /// double-clicked row of Reports' Raw Materials Price Summary). A search hiding it is cleared, so the list shows the
    /// row picked. A list that doesn't hold it yet (the screen not loaded since, or ever) opens it at the end of the next
    /// LoadCommand, which showing the screen runs.
    /// </summary>
    public void OpenRawMaterial(int rawMaterialId)
    {
        _rawMaterialToOpen = TrySelectRawMaterial(rawMaterialId) ? null : rawMaterialId;
    }

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: opens the material the name means best.</summary>
    public bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(RawMaterials, material => material.Name, text) is not { } material)
        {
            return false;
        }

        OpenRawMaterial(material.Id);
        return true;
    }

    private bool TrySelectRawMaterial(int rawMaterialId)
    {
        if (RawMaterials.FirstOrDefault(m => m.Id == rawMaterialId) is not { } material)
        {
            return false;
        }

        if (!VisibleRawMaterials.Contains(material))
        {
            SearchText = string.Empty;
        }

        SelectedRawMaterial = material;
        return true;
    }

    public void BeginCategoryEdit()
    {
        if (!_isCommittingCategory)
        {
            _categoryBeforeEdit = EditingCategory;
        }
    }

    /// <summary>
    /// ICategoryPickerScreen: a name that isn't one of the material type's categories is offered as a new one, saved
    /// for that type and picked, so filing a material under a new category doesn't mean a trip to Settings and back.
    /// A cancelled prompt puts back the category the picker had; a name that turns out to be listed is picked instead.
    /// </summary>
    public async Task CommitCategoryTextAsync()
    {
        var typed = CategorySearchText.Trim();
        if (_isCommittingCategory || typed.Length == 0 || IsNamed(EditingCategory, typed))
        {
            return;
        }

        _isCommittingCategory = true;
        try
        {
            if (CategoryOptions.FirstOrDefault(c => IsNamed(c, typed)) is { } listed)
            {
                AsUndoStep(() => EditingCategory = listed);
                return;
            }

            var name = (await PromptForNewCategory.Handle(typed))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                AsUndoStep(() => EditingCategory = _categoryBeforeEdit);
            }
            else if (CategoryOptions.FirstOrDefault(c => IsNamed(c, name)) is { } named)
            {
                AsUndoStep(() => EditingCategory = named);
            }
            else
            {
                await AddCategoryAsync(name);
            }

            // The picker shows the category it now holds rather than what was typed: the new one, or the one put back.
            CategorySearchText = EditingCategory?.Name ?? string.Empty;
        }
        finally
        {
            _isCommittingCategory = false;
        }

        static bool IsNamed(RawMaterialCategory? category, string name) =>
            string.Equals(category?.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Saves a category of the material's own type, the only kind the picker offers it, and picks it. A refused name
    /// puts back the category the picker had.
    /// </summary>
    private async Task AddCategoryAsync(string name)
    {
        var added = false;
        await RunSafelyAsync(async () =>
        {
            var category = new RawMaterialCategory { Name = name, Type = EditingType };

            var validation = _categoryValidator.Validate(category);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            // AddAsync fills in the saved Id, which the reloaded copy to pick is found by.
            await _categoryRepository.AddAsync(category, CancellationToken.None);
            await LoadCommand.Execute();
            AsUndoStep(() => EditingCategory = Categories.FirstOrDefault(c => c.Id == category.Id));
            SuccessMessage = $"{category.Name} was added.";
            added = true;
        });

        if (!added)
        {
            AsUndoStep(() => EditingCategory = _categoryBeforeEdit);
        }
    }

    /// <summary>
    /// Deletes <paramref name="material"/> once it's confirmed: the header's Delete, and the list's. The form empties if it
    /// was showing it.
    /// </summary>
    private Task DeleteRawMaterialAsync(RawMaterial material) =>
        RunWithConfirmationAsync($"Delete '{material.Name}'? This can't be undone.", async () =>
        {
            await _rawMaterialRepository.DeleteAsync(material.Id, CancellationToken.None);
            if (SelectedRawMaterial?.Id == material.Id || _editingId == material.Id)
            {
                ClearForm();
            }

            await LoadCommand.Execute();
        });

    private void RefreshTree()
    {
        // The tree drops a row from its selection as the row leaves its folder, which is no pick of the user's: with
        // several picked, the first left would otherwise become SelectedTreeNode, and its material the one picked.
        _isSyncingTreeSelection = true;
        try
        {
            RawMaterialTree.Refresh(Categories, VisibleRawMaterials, isFiltered: SearchText.Trim().Length > 0);
        }
        finally
        {
            _isSyncingTreeSelection = false;
        }

        // A folder picked stays picked. Otherwise the row goes back to the material picked: one moved to another folder
        // leaves the tree's selection as it goes, and one a search hid gets its row back when the search ends.
        if (SelectedTreeNode is not RawMaterialFolderNode)
        {
            SyncTreeSelection();
        }
    }

    /// <summary>
    /// Puts SelectedRawMaterial's row first among the rows picked in the list, and so makes it SelectedTreeNode: alone,
    /// unless it's picked already, when the rows picked with it stay. Nothing picked, or a search hiding it, picks no row.
    /// </summary>
    private void SyncTreeSelection()
    {
        var row = SelectedRawMaterial is { } material ? RawMaterialTree.Find(material.Id) : null;
        var at = row is null ? -1 : SelectedTreeNodes.IndexOf(row);
        if (at == 0)
        {
            return;
        }

        _isSyncingTreeSelection = true;
        try
        {
            if (at > 0)
            {
                SelectedTreeNodes.Move(at, 0);
            }
            else
            {
                SelectedTreeNode = row;
            }
        }
        finally
        {
            _isSyncingTreeSelection = false;
        }
    }

    /// <summary>
    /// The list's selection changed: by a click (or several, with Ctrl or Shift), or by this screen. Its first row is
    /// SelectedTreeNode, and a material's row picked by the user picks the material.
    /// </summary>
    private void TreeSelectionChanged()
    {
        var first = SelectedTreeNodes.Count > 0 ? SelectedTreeNodes[0] : null;
        this.RaisePropertyChanged(nameof(SelectedTreeMaterials));
        if (ReferenceEquals(first, _selectedTreeNode))
        {
            return;
        }

        _selectedTreeNode = first;
        this.RaisePropertyChanged(nameof(SelectedTreeNode));

        // The tree calls this in the middle of changing SelectedItems: picking the material leads to SyncTreeSelection,
        // which then finds its row first already and leaves the collection alone, as it must mid-change.
        if (!_isSyncingTreeSelection && first is RawMaterialLeafNode leaf && leaf.RawMaterial.Id != SelectedRawMaterial?.Id)
        {
            SelectedRawMaterial = leaf.RawMaterial;
        }
    }

    /// <summary>
    /// Saves <paramref name="moves"/> and files the materials in their new folders. On the material the form shows, the
    /// Category picker follows, and every undo step takes the new category as given, so no undo puts the old one back for a
    /// Save to write; whatever else is typed there stays unsaved.
    /// </summary>
    private async Task MoveMaterialsAsync(IReadOnlyList<RawMaterialMove> moves, bool isUndo)
    {
        if (moves.Count == 0)
        {
            return;
        }

        // Where each is now, which Undo puts back, and the rows picked, which the tree drops as they move.
        var undo = moves
            .Select(m => _rawMaterialCache.Lookup(m.RawMaterialId))
            .Where(found => found.HasValue)
            .Select(found => new RawMaterialMove(found.Value.Id, found.Value.CategoryId))
            .ToList();
        var picked = SelectedTreeNodes.ToList();

        await _rawMaterialRepository.MoveAsync(moves, CancellationToken.None);

        // The load only re-points the selection at the fresh copy: the form isn't loaded again, so what's typed stays.
        await LoadCommand.Execute();
        if (moves.FirstOrDefault(m => m.RawMaterialId == _editingId) is { } onForm)
        {
            ApplyRefresh(() => EditingCategory = Categories.FirstOrDefault(c => c.Id == onForm.CategoryId));
            History.RewriteAll(snapshot => snapshot with { CategoryId = onForm.CategoryId });
        }

        RestoreTreeSelection(picked);

        _moveUndo = isUndo ? [] : undo;
        MoveNotice = isUndo ? null : MoveNoticeFor(moves);
    }

    /// <summary>Picks again the rows of <paramref name="picked"/> the tree dropped from its selection as they moved.</summary>
    private void RestoreTreeSelection(IReadOnlyList<object> picked)
    {
        _isSyncingTreeSelection = true;
        try
        {
            foreach (var row in picked.Where(r => r is not RawMaterialLeafNode leaf || RawMaterialTree.Find(leaf.RawMaterial.Id) == leaf))
            {
                if (!SelectedTreeNodes.Contains(row))
                {
                    SelectedTreeNodes.Add(row);
                }
            }
        }
        finally
        {
            _isSyncingTreeSelection = false;
        }
    }

    /// <summary>
    /// What the notice bar says a move did: "Moved Flour to Ingredients › Dry Goods", "Moved 3 materials to Ingredients ›
    /// Dry Goods", or, for materials sent to different categories, "Moved 3 materials".
    /// </summary>
    private string MoveNoticeFor(IReadOnlyList<RawMaterialMove> moves)
    {
        var what = moves.Count == 1 && _rawMaterialCache.Lookup(moves[0].RawMaterialId) is { HasValue: true } one ? one.Value.Name : $"{moves.Count} materials";
        var categoryId = moves[0].CategoryId;
        return moves.All(m => m.CategoryId == categoryId) && Categories.FirstOrDefault(c => c.Id == categoryId) is { } category
            ? $"Moved {what} to {RawMaterialFolderNode.TypeTitle(category.Type)} › {category.Name}"
            : $"Moved {what}";
    }

    // Segoe Fluent Icons glyphs for the list's menu, as the Products list's menu has them.
    private const string AddGlyph = "";
    private const string NewFolderGlyph = "";
    private const string MoveGlyph = "";
    private const string RenameGlyph = "";
    private const string DeleteGlyph = "";

    /// <summary>
    /// The menu a right-click opens on the list (MaterialListCard): on <paramref name="node"/>'s row, which the View picks
    /// first (keeping the rows picked with it, when it was one of them), or on the list's empty space when it's null. A
    /// material's menu covers every material picked with it.
    /// </summary>
    public IReadOnlyList<TreeMenuItem> TreeMenuFor(RawMaterialTreeNode? node) => node switch
    {
        RawMaterialLeafNode leaf => MaterialsMenu(SelectedTreeMaterials is var picked && picked.Contains(leaf) ? picked : [leaf]),
        RawMaterialFolderNode { IsTypeFolder: true } typeFolder => TypeFolderMenu(typeFolder),
        RawMaterialFolderNode folder => CategoryFolderMenu(folder),
        _ => EmptySpaceMenu(),
    };

    /// <summary>
    /// The item a key runs with the one row picked, as that row's menu shows the key beside it (F2, Del); null when the
    /// menu has none, or when several rows are picked.
    /// </summary>
    public TreeMenuItem? TreeShortcut(string gesture) =>
        SelectedTreeNodes is [RawMaterialTreeNode node]
            ? TreeMenuFor(node).FirstOrDefault(item => item.Gesture == gesture && item.IsEnabled)
            : null;

    /// <summary>"Ingredient" or "Packaging Material": what a New … Here item makes.</summary>
    private static string MaterialNoun(RawMaterialType type) => type == RawMaterialType.Ingredient ? "Ingredient" : "Packaging Material";

    private List<TreeMenuItem> MaterialsMenu(IReadOnlyList<RawMaterialLeafNode> leaves)
    {
        var materials = leaves.Select(l => l.RawMaterial).ToList();
        var isOne = materials.Count == 1;
        List<TreeMenuItem> items = [];

        // A material only goes in a category of its own type, so there's nowhere to move ingredients and packaging together.
        if (materials.Select(m => m.Type).Distinct().ToList() is [var type])
        {
            List<TreeMenuItem> folders =
            [
                .. Categories.Where(c => c.Type == type).Select(c =>
                {
                    var moves = RawMaterialTree.MovesTo(c, materials);
                    return new TreeMenuItem(c.Name, MoveMaterialsCommand, moves) { IsEnabled = moves.Count > 0 };
                }),
            ];
            items.Add(new($"Move {(isOne ? string.Empty : $"{materials.Count} Materials ")}to")
            {
                Glyph = MoveGlyph,
                Items = folders,
                IsEnabled = folders.Any(f => f.IsEnabled),
            });
        }

        if (isOne)
        {
            var leaf = leaves[0];
            items.AddRange(
            [
                TreeMenuItem.Separator,
                new("Rename…", RenameMaterialCommand, leaf) { Gesture = "F2", Glyph = RenameGlyph },
                TreeMenuItem.Separator,
                new("Delete", DeleteMaterialCommand, leaf) { Gesture = "Del", Glyph = DeleteGlyph },
            ]);
        }

        return items;
    }

    private List<TreeMenuItem> TypeFolderMenu(RawMaterialFolderNode typeFolder) =>
    [
        new($"New {MaterialNoun(typeFolder.Type)} Here", NewMaterialInCommand, typeFolder) { Glyph = AddGlyph },
        new("New Category…", NewCategoryCommand, typeFolder.Type) { Glyph = NewFolderGlyph },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand, typeFolder),
        new("Collapse All", CollapseAllCommand, typeFolder),
    ];

    private List<TreeMenuItem> CategoryFolderMenu(RawMaterialFolderNode folder)
    {
        // Every material in it, not just a search's matches: the database won't delete a category anything is in.
        var holds = RawMaterials.Count(m => m.CategoryId == folder.CategoryId);
        return
        [
            new($"New {MaterialNoun(folder.Type)} Here", NewMaterialInCommand, folder) { Glyph = AddGlyph },
            TreeMenuItem.Separator,
            new("New Category…", NewCategoryCommand, folder.Type) { Glyph = NewFolderGlyph },
            new("Rename Category…", RenameCategoryCommand, folder) { Gesture = "F2", Glyph = RenameGlyph },
            new(holds == 0 ? "Delete Category" : $"Delete Category (holds {holds})", DeleteCategoryCommand, folder)
            {
                Gesture = "Del",
                Glyph = DeleteGlyph,
                IsEnabled = holds == 0,
            },
        ];
    }

    private List<TreeMenuItem> EmptySpaceMenu() =>
    [
        new("New Raw Material", NewMaterialInCommand) { Glyph = AddGlyph },
        new("New Ingredient Category…", NewCategoryCommand, RawMaterialType.Ingredient) { Glyph = NewFolderGlyph },
        new("New Packaging Category…", NewCategoryCommand, RawMaterialType.Packaging) { Glyph = NewFolderGlyph },
        TreeMenuItem.Separator,
        new("Expand All", ExpandAllCommand),
        new("Collapse All", CollapseAllCommand),
    ];

    /// <summary>
    /// The list's Rename: asks for the new name, and saves it at once. On the material the form shows, the name box takes
    /// it, and the form's undo steps take it as given, as a move gives them its category: whatever else is typed there
    /// stays to be saved, and no undo brings the old name back for a Save to write.
    /// </summary>
    private async Task RenameMaterialAsync(RawMaterialLeafNode leaf)
    {
        var material = leaf.RawMaterial;
        var name = (await PromptForName.Handle(new TextPromptRequest("Rename Raw Material", "Name", material.Name)))?.Trim();
        if (string.IsNullOrEmpty(name) || string.Equals(name, material.Name, StringComparison.Ordinal))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            await _rawMaterialRepository.RenameAsync(material.Id, name, CancellationToken.None);
            await LoadCommand.Execute();
            if (_editingId == material.Id)
            {
                ApplyRefresh(() => EditingName = name);
                History.RewriteAll(snapshot => snapshot with { Name = name });
            }
        });
    }

    /// <summary>
    /// The list's New Category: asks for a name and adds a category for <paramref name="type"/>, then picks its folder. A
    /// name that's one of that type's categories already picks that one's folder instead.
    /// </summary>
    private async Task NewCategoryAsync(RawMaterialType type)
    {
        var name = (await PromptForName.Handle(new TextPromptRequest(
            "New Category", "Name", string.Empty, $"A folder in {RawMaterialFolderNode.TypeTitle(type)}.")))?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var category = Categories.FirstOrDefault(c => c.Type == type && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (category is null)
            {
                var added = new RawMaterialCategory { Name = name, Type = type };
                var validation = _categoryValidator.Validate(added);
                if (!validation.IsValid)
                {
                    ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                    return;
                }

                // AddAsync fills in the saved Id, which the copy read back is found by.
                await _categoryRepository.AddAsync(added, CancellationToken.None);
                await LoadCommand.Execute();
                category = Categories.FirstOrDefault(c => c.Id == added.Id);
            }

            if (category is not null
                && RawMaterialTree.CategoryFolder(category.Id) is { } folder
                && RawMaterialTree.TypeFolder(type).Children.Contains(folder))
            {
                SelectedTreeNode = folder;
            }
        });
    }

    /// <summary>The list's Rename Category: renames the category, keeping its type.</summary>
    private async Task RenameCategoryAsync(RawMaterialFolderNode folder)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category)
        {
            return;
        }

        var name = (await PromptForName.Handle(new TextPromptRequest("Rename Category", "Name", category.Name)))?.Trim();
        if (string.IsNullOrEmpty(name) || string.Equals(name, category.Name, StringComparison.Ordinal))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var renamed = new RawMaterialCategory { Id = category.Id, Name = name, Type = category.Type };
            var validation = _categoryValidator.Validate(renamed);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            // Everything is read again, the materials with it, which carry their category's name. The Category picker keeps
            // the category it holds (LoadCommand), under its new name.
            await _categoryRepository.UpdateAsync(renamed, CancellationToken.None);
            await LoadCommand.Execute();
        });
    }

    /// <summary>
    /// The list's Delete Category, once it's confirmed: only for a category no material is in, which is all the database
    /// allows (a material always has a category). The menu greys it out otherwise.
    /// </summary>
    private Task DeleteCategoryAsync(RawMaterialFolderNode folder)
    {
        if (Categories.FirstOrDefault(c => c.Id == folder.CategoryId) is not { } category
            || RawMaterials.Any(m => m.CategoryId == category.Id))
        {
            return Task.CompletedTask;
        }

        return RunWithConfirmationAsync(
            $"Delete the '{category.Name}' category? It holds no materials. This can't be undone.",
            async () =>
            {
                await _categoryRepository.DeleteAsync(category.Id, CancellationToken.None);

                // A new material's form may have had it picked: the undo steps forget it, so none puts back a category
                // there's no longer any of to save.
                await LoadCommand.Execute();
                History.RewriteAll(snapshot => snapshot.CategoryId == category.Id ? snapshot with { CategoryId = null } : snapshot);
            });
    }

    /// <summary>Writes the list's open folders for next time, when they differ from what was written last.</summary>
    private void WriteOpenFolders(IReadOnlyList<string> openFolders)
    {
        lock (_openFoldersLock)
        {
            if (!openFolders.SequenceEqual(_writtenOpenFolders, StringComparer.Ordinal))
            {
                _viewStateStore.WriteList(OpenFoldersKey, openFolders);
                _writtenOpenFolders = openFolders;
            }
        }
    }

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        _openFoldersWriter.Dispose();
        WriteOpenFolders(RawMaterialTree.OpenFolderKeys());
        RawMaterialTree.Dispose();

        _rawMaterialCache.Dispose();
        _categoryCache.Dispose();
        _productOpenRequests.Dispose();
    }
}
