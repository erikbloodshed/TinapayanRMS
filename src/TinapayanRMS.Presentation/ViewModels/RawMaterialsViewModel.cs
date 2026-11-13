using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Raw Materials screen: ingredients and packaging in one list. A material's type decides which
/// parts of its details apply — an ingredient is measured in grams and can be made from a sub-assembly, a
/// packaging material has a base unit of its own and is always purchased. Its details, alternative
/// units, brand prices and a made ingredient's sub-assembly and add-ins are all edited in memory, and Save
/// writes them together. The sub-assembly's own lines are only shown: they're written on the Sub-Assemblies screen.
/// </summary>
public sealed partial class RawMaterialsViewModel : ListFormViewModel<RawMaterialFormSnapshot>, IDisposable, IBrandPriceRowHost, IRecordScreen, IListTreeScreen
{
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IRawMaterialPriceHistoryRepository _priceHistoryRepository;
    private readonly IRawMaterialBrandPriceRepository _brandPriceRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _subAssemblyLineRepository;
    private readonly IRawMaterialCategoryRepository _categoryRepository;
    private readonly IValidator<RawMaterialCategory> _categoryValidator;
    private readonly IValidator<RawMaterial> _rawMaterialValidator;
    private readonly IValidator<RawMaterialUnit> _unitValidator;
    private readonly IValidator<RawMaterialBrandPrice> _brandPriceValidator;
    private readonly SourceCache<RawMaterial, int> _rawMaterialCache = new(m => m.Id);
    private ReadOnlyObservableCollection<RawMaterial> _rawMaterials;
    private ReadOnlyObservableCollection<RawMaterial> _visibleRawMaterials;
    private ReadOnlyObservableCollection<RawMaterial> _ingredients;
    private ReadOnlyObservableCollection<RawMaterial> _linkTargets;
    private ReadOnlyObservableCollection<RawMaterial> _addInOptions;
    private readonly SourceCache<RawMaterialCategory, int> _categoryCache = new(c => c.Id);
    private ReadOnlyObservableCollection<RawMaterialCategory> _categories;
    private ReadOnlyObservableCollection<RawMaterialCategory> _categoryOptions;

    /// <summary>Every raw material, for the usage example's picker.</summary>
    public ReadOnlyObservableCollection<RawMaterial> RawMaterials => _rawMaterials;

    /// <summary>The materials the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<RawMaterial> VisibleRawMaterials => _visibleRawMaterials;

    /// <summary>The Raw Materials list (MaterialListCard): VisibleRawMaterials filed in a folder for their type, then their category.</summary>
    public RawMaterialTree RawMaterialTree { get; }

    /// <summary>The view state key the closed folders of the Raw Materials list are kept under (IViewStateStore).</summary>
    public const string ClosedFoldersKey = "RawMaterials.ClosedFolders";

    /// <summary>
    /// The view state key the list kept its open folders under, before it kept the closed ones: read while there are no
    /// closed ones kept, so the list opens as it was left (FolderMemory.Read).
    /// </summary>
    public const string OpenFoldersKey = "RawMaterials.OpenFolders";

    private readonly ICostingDefaultsRepository _costingDefaultsRepository;

    // What a new material's Cost Buffer % starts at (Settings → Costing Defaults), as of the last LoadCommand.
    private decimal _defaultCostBufferPercent = CostingDefaults.DefaultCostBufferPercent;

    private readonly FolderStateWriter _closedFoldersWriter;

    /// <summary>The materials picked in the list, in the order they were picked.</summary>
    public IReadOnlyList<RawMaterialLeafNode> SelectedTreeMaterials => [.. SelectedTreeNodes.OfType<RawMaterialLeafNode>().Where(l => !l.IsNew)];

    /// <summary>Where the materials the last move took were before it: what Undo on the notice puts back.</summary>
    private IReadOnlyList<RawMaterialMove> _moveUndo = [];

    /// <summary>
    /// The ingredients only: packaging never goes in a recipe. Resolves a BOM line's IngredientId and a saved
    /// LinkedToId back to their material; a picker that shouldn't offer the material being edited uses
    /// AddInOptions or LinkTargets instead.
    /// </summary>
    public ReadOnlyObservableCollection<RawMaterial> Ingredients => _ingredients;

    /// <summary>The ingredients a linked ingredient can be linked to: every one but the material being edited.</summary>
    public ReadOnlyObservableCollection<RawMaterial> LinkTargets => _linkTargets;

    /// <summary>The Add-Ins table's ingredient picker's list: every ingredient but the made one being edited — a made ingredient can't be its own add-in.</summary>
    public ReadOnlyObservableCollection<RawMaterial> AddInOptions => _addInOptions;

    public ObservableCollection<RawMaterialPriceRow> PriceHistory { get; } = [];

    /// <summary>
    /// The sub-assemblies a made ingredient can be made from, for the Recipe card's picker: every recipe on the
    /// Sub-Assemblies screen, with its role, by name. Not a packaging set, and never a product's own, which that screen
    /// doesn't list (SubAssemblyRules.EnsureLinkableByMadeIngredient).
    /// </summary>
    public ObservableCollection<SubAssembly> SubAssemblyOptions { get; } = [];

    private SubAssembly? _editingSubAssembly;
    /// <summary>Made only: the sub-assembly it's made from, the recipe its add-ins go on top of. Picked from <see cref="SubAssemblyOptions"/>.</summary>
    public SubAssembly? EditingSubAssembly
    {
        get => _editingSubAssembly;
        set
        {
            if (EqualityComparer<SubAssembly?>.Default.Equals(_editingSubAssembly, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _editingSubAssembly, value);

            // Another sub-assembly picked after Edit settles it, and the badge comes back.
            if (value is not null && IsChangingSubAssembly)
            {
                IsChangingSubAssembly = false;
            }
        }
    }

    private bool _isChangingSubAssembly;
    /// <summary>
    /// The Recipe card's Edit was pressed: the Made from picker shows again in place of the badge, until another sub-assembly
    /// is picked or Cancel is pressed, so the recipe it's made from isn't changed by accident.
    /// </summary>
    public bool IsChangingSubAssembly
    {
        get => _isChangingSubAssembly;
        private set => this.RaiseAndSetIfChanged(ref _isChangingSubAssembly, value);
    }

    private ObservableAsPropertyHelper<bool> _isPickingSubAssembly;
    /// <summary>The Made from picker shows: no sub-assembly is picked yet, or Edit was pressed. Otherwise the badge and Edit show.</summary>
    public bool IsPickingSubAssembly => _isPickingSubAssembly.Value;

    private ObservableAsPropertyHelper<bool> _isShowingSubAssemblyBadge;
    /// <summary>The picked sub-assembly shows as a badge that opens it, with Edit beside it, so a stray click or scroll can't change it.</summary>
    public bool IsShowingSubAssemblyBadge => _isShowingSubAssemblyBadge.Value;

    private ObservableAsPropertyHelper<string> _subAssemblyBadgeText;
    /// <summary>"Butter Icing Base, from Sub-Assemblies", as a linked section's badge reads on the Products screen.</summary>
    public string SubAssemblyBadgeText => _subAssemblyBadgeText.Value;

    /// <summary>The badge's Edit: shows the Made from picker again (<see cref="IsChangingSubAssembly"/>).</summary>
    public ReactiveCommand<RxVoid, RxVoid> StartChangingSubAssemblyCommand { get; private set; }

    /// <summary>The picker's Cancel: puts the badge back, keeping the sub-assembly as it is.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CancelChangingSubAssemblyCommand { get; private set; }

    /// <summary>
    /// The lines of <see cref="EditingSubAssembly"/>, as it's saved on the Sub-Assemblies screen, for show: read-only here,
    /// since that screen is the one place the recipe is written. Read again whenever the pick changes or the screen loads.
    /// </summary>
    public ObservableCollection<IngredientLineRow> RecipeLines { get; } = [];

    /// <summary>
    /// The made ingredient's own extra ingredients on top of its sub-assembly's recipe, typed straight into the Add-Ins
    /// table (IngredientAddInLine).
    /// </summary>
    public ObservableCollection<IngredientLineRow> AddInEdits { get; } = [];

    /// <summary>The line the Add-Ins table's add row edits: it prices itself as its ingredient is picked, before it's committed.</summary>
    public Func<IngredientLineRow> NewAddIn => () => new IngredientLineRow(id => Ingredients.FirstOrDefault(m => m.Id == id));

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

    /// <summary>
    /// Each material picked, by the user or for them (Open, Save's reload), nothing picked included: what loads a material
    /// into the form. Fed by <see cref="SelectedRawMaterial"/>'s setter alone, starting with nothing picked.
    /// </summary>
    private readonly BehaviorSubject<RawMaterial?> _picks = new(null);

    /// <summary>
    /// The material the form is about. Setting it is a pick, which loads that material into the form (_picks); a load that
    /// only finds a fresh copy of it re-points it quietly (<see cref="RepointSelection"/>), leaving what's typed alone.
    /// </summary>
    public RawMaterial? SelectedRawMaterial
    {
        get => _selectedRawMaterial;
        set
        {
            if (EqualityComparer<RawMaterial?>.Default.Equals(_selectedRawMaterial, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedRawMaterial, value);
            _picks.OnNext(value);
        }
    }

    /// <summary>The selection pointed at <paramref name="fresh"/>, a load's copy of the material picked: shown, not picked again.</summary>
    private void RepointSelection(RawMaterial? fresh) =>
        this.RaiseAndSetIfChanged(ref _selectedRawMaterial, fresh, nameof(SelectedRawMaterial));

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

    /// <summary>
    /// A material for LoadCommand to open at its end: one <see cref="OpenRawMaterial"/> was asked for before the list held
    /// it, or the one in the screen's address (<see cref="OpenRawMaterialOnLoad"/>). New drops it.
    /// </summary>
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
    /// Trimmed, as Save stores it, and case-insensitive, as the collation the unique index relies on is. Fixing
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
        set
        {
            if (_editingType == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _editingType, value);
            TypeEdited(value);
            BaseUnitEdited();
        }
    }

    private RawMaterialCategory? _editingCategory;
    public RawMaterialCategory? EditingCategory
    {
        get => _editingCategory;
        set => this.RaiseAndSetIfChanged(ref _editingCategory, value);
    }

    /// <summary>The Category picker's naming of a new category (BeginCategoryEdit, CommitCategoryTextAsync).</summary>
    private readonly CategoryPicker<RawMaterialCategory> _categoryPicker;

    private RawMaterialSource _editingSource = RawMaterialSource.Purchased;
    public RawMaterialSource EditingSource
    {
        get => _editingSource;
        set
        {
            if (_editingSource == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _editingSource, value);

            // A made ingredient starts at 3% weight loss, since a small shrink is the normal
            // case in the shop, and the box stays freely editable — typing 0 is a legitimate custom value. Only when the
            // box is empty, so a saved material keeps what it was saved with.
            if (value == RawMaterialSource.Made)
            {
                EditingWeightLossPercent ??= 3m;
            }
        }
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

    private decimal? _editingTargetCostBufferPercent = CostingDefaults.DefaultCostBufferPercent;
    /// <summary>
    /// Purchased only: the buffer last typed, which the Cost Buffer % box drifts from as prices move while the
    /// buffered cost stays fixed. Re-apply Target types it again.
    /// </summary>
    public decimal? EditingTargetCostBufferPercent
    {
        get => _editingTargetCostBufferPercent;
        set
        {
            this.RaiseAndSetIfChanged(ref _editingTargetCostBufferPercent, value);
            this.RaisePropertyChanged(nameof(TargetBufferText));
            this.RaisePropertyChanged(nameof(IsBufferOffTarget));
        }
    }

    private string? _editingPreferredBrand;

    /// <summary>
    /// The brand a purchased material's cost comes from, as Save will keep it: null for the dearest brand. The
    /// costs and texts below follow it at once, as they follow the grid (UpdatePurchaseCostTexts).
    /// </summary>
    public string? EditingPreferredBrand
    {
        get => _editingPreferredBrand;
        set
        {
            if (_editingPreferredBrand == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _editingPreferredBrand, value);
            this.RaisePropertyChanged(nameof(CostFrom));
            _updates.Run(UpdatePurchaseCostTexts);
        }
    }

    /// <summary>The Basis picker's choices: the dearest brand, then each brand in the grid (RebuildCostFromOptions).</summary>
    public ObservableCollection<CostFromOption> CostFromOptions { get; } = [new(null)];

    /// <summary>The Basis picker's pick, <see cref="EditingPreferredBrand"/> as one of its choices.</summary>
    public CostFromOption? CostFrom
    {
        get => CostFromOptions.FirstOrDefault(o => string.Equals(o.Brand, EditingPreferredBrand, StringComparison.OrdinalIgnoreCase)) ?? CostFromOptions[0];
        set
        {
            // While the choices are replaced, the picker writes back an empty pick, as the Per picker does.
            if (_rebuildingCostFromOptions || value is null)
            {
                return;
            }

            EditingPreferredBrand = value.Brand;
        }
    }

    private bool _rebuildingCostFromOptions;

    /// <summary>"Target 10%", beside the Cost Buffer % box, while the buffer has drifted from it.</summary>
    public string TargetBufferText => string.Create(CultureInfo.CurrentCulture, $"Target {EditingTargetCostBufferPercent ?? 0m:0.##}%");

    /// <summary>The buffer has drifted from its target as prices moved: Re-apply Target shows.</summary>
    public bool IsBufferOffTarget =>
        DearestPurchaseCost is not null
        && EditingBufferedCost is > 0m
        && RawMaterialRules.IsOffTarget(EditingCostBufferPercent ?? 0m, EditingTargetCostBufferPercent ?? 0m);

    /// <summary>Types the target buffer into the Cost Buffer % box again, which puts it back on top of the dearest brand; one undo step.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ReapplyTargetBufferCommand { get; private set; }

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
            EditingTargetCostBufferPercent = value ?? 0m;
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
        set
        {
            if (EqualityComparer<UnitOfMeasure?>.Default.Equals(_editingBaseUnit, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _editingBaseUnit, value);
            BaseUnitEdited();
        }
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

    private ObservableAsPropertyHelper<bool> _isEditingIngredient;
    public bool IsEditingIngredient => _isEditingIngredient.Value;

    private ObservableAsPropertyHelper<bool> _isEditingPackaging;
    public bool IsEditingPackaging => _isEditingPackaging.Value;

    private ObservableAsPropertyHelper<bool> _isEditingMade;
    /// <summary>A made ingredient: it has a sub-assembly, add-ins and a weight loss percent instead of a purchase price.</summary>
    public bool IsEditingMade => _isEditingMade.Value;

    private ObservableAsPropertyHelper<bool> _isEditingLinked;
    /// <summary>A linked ingredient: its cost is a share of another ingredient's, instead of a price or a BOM.</summary>
    public bool IsEditingLinked => _isEditingLinked.Value;

    private ObservableAsPropertyHelper<string> _linkedCostPreviewText;
    /// <summary>What the link works out to as it's filled in: the linked-to ingredient's cost per gram, and this part's.</summary>
    public string LinkedCostPreviewText => _linkedCostPreviewText.Value;

    private ObservableAsPropertyHelper<string> _linkedPartsText;
    /// <summary>The linked-to ingredient's other parts, and how much of its weight and cost all of them take with this one.</summary>
    public string LinkedPartsText => _linkedPartsText.Value;

    private ObservableAsPropertyHelper<bool> _showPurchaseSetup;
    /// <summary>A purchased material, saved or still being filled in, so a new one can be priced before its first Save.</summary>
    public bool ShowPurchaseSetup => _showPurchaseSetup.Value;

    private ObservableAsPropertyHelper<bool> _showPriceHistory;
    /// <summary>A saved purchased material: a new one has no price history yet.</summary>
    public bool ShowPriceHistory => _showPriceHistory.Value;

    /// <summary>The details tabs along the bottom: Cost Setup, Alternative Units, Usage Example, Dependents, then Price History.</summary>
    public const int PriceHistoryTab = 4;

    /// <summary>Dependents: the products the material goes into, with what it makes up of each one's material cost.</summary>
    public const int DependentsTab = 3;

    /// <summary>The details tab showing. Moves to Cost Setup when Price History is about to hide (ShowPriceHistory).</summary>
    private int _selectedDetailsTab;
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set => this.RaiseAndSetIfChanged(ref _selectedDetailsTab, value);
    }

    private ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The summary header's big title: the material's name, or "New raw material".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private ObservableAsPropertyHelper<string> _baseUnitName;
    /// <summary>The form's base unit name: "gram" for an ingredient, e.g. "centimeter" for ribbon.</summary>
    public string BaseUnitName => _baseUnitName.Value;

    private ObservableAsPropertyHelper<string> _baseUnitAbbreviation;
    public string BaseUnitAbbreviation => _baseUnitAbbreviation.Value;

    private ObservableAsPropertyHelper<string> _savedBaseUnitText;
    /// <summary>
    /// The Base Unit tile: "gram (g)" for an ingredient, and a packaging material's base unit as last saved, e.g.
    /// "centimeter (cm)", or "—" until its first Save. The unit is picked in the fields row (EditingBaseUnit), so
    /// the tile shows what's saved, as the cost tiles beside it do.
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
    /// Made only: the batch's yield as the form has it, the sub-assembly's grams and the add-ins' less the weight loss, so
    /// it follows the pick, the Add-Ins table and the Weight Loss % box as they change. 0 with no sub-assembly picked.
    /// </summary>
    public decimal BomTotalWeight
    {
        get => _bomTotalWeight;
        private set => this.RaiseAndSetIfChanged(ref _bomTotalWeight, value);
    }

    private decimal _bomTotalCost;
    /// <summary>
    /// Made only: what the form's BOM costs at its components' buffered costs — the sum of both tables' Cost columns, the
    /// recipe's and the add-ins'.
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

    // Add-Ins (made ingredients): ingredients and grams on top of the sub-assembly, typed straight into their table.
    private IngredientLineRow? _selectedAddInEdit;
    public IngredientLineRow? SelectedAddInEdit
    {
        get => _selectedAddInEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedAddInEdit, value);
    }

    /// <summary>The add-ins as last loaded, so Save only sends them when they were changed.</summary>
    private List<(int IngredientId, decimal Grams)> _savedAddIns = [];

    private RawMaterialPriceRow? _selectedPriceRow;
    public RawMaterialPriceRow? SelectedPriceRow
    {
        get => _selectedPriceRow;
        set => this.RaiseAndSetIfChanged(ref _selectedPriceRow, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteUnitCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateExampleCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveBrandPriceCommand { get; private set; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveAddInCommand { get; private set; }

    /// <summary>Files materials under another category, saved at once: a drag in the list, or its menu's Move To.</summary>
    public ReactiveCommand<IReadOnlyList<RawMaterialMove>, RxVoid> MoveMaterialsCommand { get; private set; }

    /// <summary>Puts back where the last move took the materials from (the notice bar's Undo).</summary>
    public ReactiveCommand<RxVoid, RxVoid> UndoMoveCommand { get; private set; }

    /// <summary>
    /// The list's New … Here: a new material of the folder's type, in its category when it's a category's folder. With no
    /// folder (the list's empty space), a new ingredient, as New starts one. Like New, it adds the material's row to the list
    /// for its name to be typed into (TreeNameEdit).
    /// </summary>
    public ReactiveCommand<RawMaterialFolderNode?, RxVoid> NewMaterialInCommand { get; private set; }

    /// <summary>The list's Rename: the material's name becomes a box to type into, in place, and the name typed is saved at once.</summary>
    public ReactiveCommand<RawMaterialLeafNode, RxVoid> RenameMaterialCommand { get; private set; }

    /// <summary>
    /// The list's Copy: a new material with the material's type, category and base unit, named "… Copy", and nothing else.
    /// It isn't saved until Save is clicked.
    /// </summary>
    public ReactiveCommand<RawMaterialLeafNode, RxVoid> CopyMaterialCommand { get; private set; }

    /// <summary>The list's Delete, as the header's: asks first.</summary>
    public ReactiveCommand<RawMaterialLeafNode, RxVoid> DeleteMaterialCommand { get; private set; }

    /// <summary>
    /// The list's New Folder, for materials of the type given: a "New folder" row for its name to be typed into
    /// (TreeNameEdit), and the name typed adds the category.
    /// </summary>
    public ReactiveCommand<RawMaterialCategoryType, RxVoid> NewCategoryCommand { get; private set; }

    /// <summary>The list's Rename Category: the folder's name becomes a box to type into, in place, and the name typed renames the category.</summary>
    public ReactiveCommand<RawMaterialFolderNode, RxVoid> RenameCategoryCommand { get; private set; }

    /// <summary>The list's Delete Category, offered only for a category no material is in.</summary>
    public ReactiveCommand<RawMaterialFolderNode, RxVoid> DeleteCategoryCommand { get; private set; }

    /// <summary>Opens every folder of a type's, or of both types' with no folder given.</summary>
    public ReactiveCommand<RawMaterialFolderNode?, RxVoid> ExpandAllCommand { get; private set; }

    /// <summary>Closes every category folder of a type's, or of both types' with no folder given.</summary>
    public ReactiveCommand<RawMaterialFolderNode?, RxVoid> CollapseAllCommand { get; private set; }

    private IReadOnlyList<LinkingProduct> _dependents = [];
    /// <summary>
    /// The products the material showing goes into, by name (IRawMaterialRepository.GetDependentsAsync): one tile each on the
    /// Dependents tab, which opens it. Empty for a new material.
    /// </summary>
    public IReadOnlyList<LinkingProduct> Dependents
    {
        get => _dependents;
        private set => this.RaiseAndSetIfChanged(ref _dependents, value);
    }

    private string _dependentsText = NotUsedText;
    /// <summary>The caption under the Dependents tiles: what they mean for a change of cost, or that nothing uses the material.</summary>
    public string DependentsText
    {
        get => _dependentsText;
        private set => this.RaiseAndSetIfChanged(ref _dependentsText, value);
    }

    private const string NotUsedText = "No product uses it yet.";

    private const string UsedText =
        "These products use it in their recipe or packaging, directly or through a made or linked ingredient, so a change to its cost reaches each of them.";

    /// <summary>A Dependents tile's click: asks for the product with this id to be opened, through <see cref="ProductOpenRequests"/>.</summary>
    public ReactiveCommand<int, RxVoid> OpenProductCommand { get; private set; }

    private readonly Subject<int> _productOpenRequests = new();

    /// <summary>
    /// The products Dependents tiles ask to open, by id. The main window, which owns the tabs, subscribes and opens each on
    /// the Products screen, as it does for Sub-Assemblies' tiles.
    /// </summary>
    public IObservable<int> ProductOpenRequests => _productOpenRequests;

    /// <summary>The Recipe card's badge: asks for the sub-assembly the made ingredient is made from to be opened, through <see cref="SubAssemblyOpenRequests"/>.</summary>
    public ReactiveCommand<RxVoid, RxVoid> OpenSubAssemblyCommand { get; private set; }

    private readonly Subject<int> _subAssemblyOpenRequests = new();

    /// <summary>
    /// The sub-assemblies the Recipe card asks to open, by id. The main window subscribes and opens each on the
    /// Sub-Assemblies screen, where its lines are edited, as it does for a linked badge on Products.
    /// </summary>
    public IObservable<int> SubAssemblyOpenRequests => _subAssemblyOpenRequests;


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
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository subAssemblyLineRepository,
        IRawMaterialCategoryRepository categoryRepository,
        IValidator<RawMaterialCategory> categoryValidator,
        IValidator<RawMaterial> rawMaterialValidator,
        IValidator<RawMaterialUnit> unitValidator,
        IValidator<RawMaterialBrandPrice> brandPriceValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        IViewStateStore viewStateStore,
        ICostingDefaultsRepository costingDefaultsRepository,
        ILogger logger,
        MaterialCostShares? costShares = null)
        : base(logger)
    {
        _categoryPicker = new CategoryPicker<RawMaterialCategory>(
            () => EditingCategory, category => AsUndoStep(() => EditingCategory = category), () => CategoryOptions, c => c.Name,
            () => CategorySearchText, text => CategorySearchText = text, async typed => await PromptForNewCategory.Handle(typed),
            SaveNewCategoryAsync, RunSafelyAsync, message => SuccessMessage = message, ex => ShowFailure(ex));
        _costShares = costShares;
        _rawMaterialRepository = rawMaterialRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _brandPriceRepository = brandPriceRepository;
        _bomLineRepository = bomLineRepository;
        _subAssemblyRepository = subAssemblyRepository;
        _subAssemblyLineRepository = subAssemblyLineRepository;
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;
        _rawMaterialValidator = rawMaterialValidator;
        _unitValidator = unitValidator;
        _brandPriceValidator = brandPriceValidator;
        _costingDefaultsRepository = costingDefaultsRepository;

        // The list's folders open as they were left last time, a folder new since included, and the ones closed are written
        // for next time (FolderStateWriter), as on the Products screen; Dispose writes any change still waiting.
        RawMaterialTree = new RawMaterialTree(FolderMemory.Read(viewStateStore, ClosedFoldersKey, OpenFoldersKey));
        _closedFoldersWriter = new FolderStateWriter(viewStateStore, ClosedFoldersKey, RawMaterialTree.ExpansionChanged, RawMaterialTree.ClosedFolderKeys);

        // The subscriptions, in the order they were made: two following one property run in that order, some count on it
        // (WireForm), and several run as they're made, WireDetails' included, so each comes after what it reads.
        WireLists();
        WireForm();
        WireBaseUnitNames();
        WireUnsavedChanges();
        WireCosts();
        WireRecipe();
        WireUnitViews();
        WireDetails();
        WireUnitRows();
        WireAddIns();
        WireExample();
        WireBufferTarget();

        // The commands. Making one runs nothing but its can-execute flag, so they can come in any order.
        WireLoadCommand(unitOfMeasureRepository);
        WireRecordCommands();
        WireListCommands();
        WireUnitCommands();
        WireCostCommands();
        WireRecipeCommands();
    }

    /// <summary>The screen's derived updates, run as their inputs change, or once at the end of an undo's restore.</summary>
    private readonly DerivedUpdates _updates = new();

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        _closedFoldersWriter.Dispose();
        RawMaterialTree.Dispose();

        _rawMaterialCache.Dispose();
        _categoryCache.Dispose();
        _productOpenRequests.Dispose();
        _costSharesWanted.Dispose();
        _picks.Dispose();
    }
}
