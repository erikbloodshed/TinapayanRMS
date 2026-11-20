using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.RawMaterials;

namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>What a Raw Materials draft asks of the form it's in, on top of the undo recorder its parts record their steps in.</summary>
public interface IRawMaterialDraftHost : IUndoRecorder
{
    /// <summary>The material as saved, picked in the list; null for a new one. The Cost Setup shows its saved costs.</summary>
    RawMaterial? SelectedRawMaterial { get; }

    /// <summary>The details showing are the picked material's: a made ingredient's totals wait until they are.</summary>
    bool AreDetailsReady { get; }

    /// <summary>A load or an undo is writing the form: a unit's multiplier changed then reprices nothing.</summary>
    bool IsBusyApplying { get; }
}

/// <summary>The view's lists a draft refers to, as the last load left them: what its fields and its rows' pickers are one of.</summary>
public sealed record RawMaterialLists(
    IReadOnlyList<RawMaterialCategory> Categories,
    IReadOnlyList<RawMaterial> Ingredients,
    IReadOnlyList<UnitOfMeasure> UnitsOfMeasure);

/// <summary>The checks a draft's tables run on each row as Save reads them (IRawMaterialEditor's).</summary>
public sealed record RawMaterialRowChecks(Action<RawMaterialUnit> Unit, Action<RawMaterialBrandPrice> BrandPrice);

/// <summary>
/// The raw material a Raw Materials form edits (docs/adr/0002-layers-use-cases-and-drafts.md): its own fields (name, type,
/// category, source, weight loss, a linked ingredient's link and shares, a packaging material's base unit), and its three
/// parts, the Cost Setup's brand prices and buffer (<see cref="PurchaseCost"/>), the Alternative Units (<see cref="Units"/>)
/// and a made ingredient's recipe and add-ins (<see cref="Recipe"/>), with the rules that follow them as they're edited and
/// the wiring between them. It's filled from what was saved (<see cref="Show"/>) or emptied for a new material
/// (<see cref="Clear"/>), captured and put back for an undo step (<see cref="Capture"/>, <see cref="Restore"/>), pointed at a
/// load's fresh copies of what it refers to (<see cref="Rebase"/>, ADR 0007), and read as a Save writes it
/// (<see cref="ToMaterial"/>, <see cref="ToSaveRequest"/>). It reads no database and knows no screen: the form around it
/// keeps which material it is, the details it reads, the undo history, Save and the tabs.
/// </summary>
public sealed class RawMaterialDraft : ObservableState, IDisposable, IRawMaterialUnitsHost, IRawMaterialPurchaseCostHost, IRawMaterialRecipeHost
{
    private readonly IRawMaterialDraftHost _host;
    private readonly RawMaterialLists _lists;

    /// <summary>The draft's derived updates, run as their inputs change, or once at the end of a restore.</summary>
    private readonly DerivedUpdates _updates = new();

    public RawMaterialDraft(IRawMaterialDraftHost host, RawMaterialLists lists, RawMaterialRowChecks checks)
    {
        _host = host;
        _lists = lists;

        // The parts, each its own subscriptions to what it holds alone; what reaches from one to another is below.
        Units = new RawMaterialUnits(this, lists.UnitsOfMeasure, checks.Unit);
        PurchaseCost = new RawMaterialPurchaseCost(this, Units, lists.UnitsOfMeasure, checks.BrandPrice, _updates);
        Recipe = new RawMaterialRecipe(this, lists.Ingredients);

        // Every change to anything the draft holds, its tables' cells included, moves its version: what's worked out from
        // the whole draft (whether there's anything to save) hears of it here, in one place. Grams typed into an add-in, or
        // a brand's price, while the cell still has the cursor; not through the totals they move, worked out as they're read.
        PurchaseCost.PropertyChanged += (_, _) => Touch();
        Units.PropertyChanged += (_, _) => Touch();
        Recipe.PropertyChanged += (_, _) => Touch();
        RowChanges.OnAnyChange(Units.Rows, Touch);
        RowChanges.OnAnyChange(PurchaseCost.BrandPriceRows, Touch);
        RowChanges.OnAnyChange(Recipe.AddIns, Touch);

        // The summary follows the brand table as it's typed into: the dearest brand, as Save will cost the material.
        PurchaseCost.BrandPriceRows.ToObservableChangeSet()
            .AutoRefresh(r => r.Price)
            .AutoRefresh(r => r.Quantity)
            .AutoRefresh(r => r.Brand)
            .Subscribe(_ => _updates.Run(PurchaseCost.UpdatePurchaseCostTexts));

        // Each brand's buffered price follows the fixed buffered cost (the Cost Setup's BufferEdited).
        _updates.Run(PurchaseCost.BufferedCostChanged);

        // The alternative units and the purchase unit choices name the base unit (Type's and BaseUnit's setters).
        RebuildUnitViews();

        // A unit's multiplier changed in the Alternative Units table reprices the brands bought in it straight away, and the
        // add row goes once there are two units. Not as a load or an undo writes them, which reprice once at their end.
        Units.Rows.ToObservableChangeSet()
            .AutoRefresh(r => r.UnitOfMeasureId)
            .AutoRefresh(r => r.BaseUnitsPerUnit)
            .Where(_ => !Units.IsSyncing && !_host.IsBusyApplying)
            .Subscribe(_ => RepriceBrands());

        // An ingredient picked in the Add-Ins table is pointed at the screen's copy of it, for its name.
        Recipe.AddIns.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ => _updates.Run(Recipe.ResolveAddIns));
    }

    /// <summary>The Cost Setup tab: the brand prices, the buffered cost and its target, and the header's saved costs.</summary>
    public RawMaterialPurchaseCost PurchaseCost { get; }

    /// <summary>The Alternative Units tab: the units the material is bought or counted in, and how many base units each holds.</summary>
    public RawMaterialUnits Units { get; }

    /// <summary>A made ingredient's recipe: the sub-assembly it's made from, its lines, the add-ins, and the batch's totals.</summary>
    public RawMaterialRecipe Recipe { get; }

    /// <summary>A count that any change to the draft moves, its tables and their cells included.</summary>
    public long Version { get; private set; }

    /// <summary>Raised on every change to the draft, as <see cref="Version"/> moves.</summary>
    public event Action? Changed;

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => RaiseAndSetIfChanged(ref _name, value);
    }

    private RawMaterialType _type = RawMaterialType.Ingredient;

    /// <summary>The material's type, as loaded, put back or picked (<see cref="ChangeType"/>).</summary>
    public RawMaterialType Type
    {
        get => _type;
        private set
        {
            if (RaiseAndSetIfChanged(ref _type, value))
            {
                CategoryTypeChanged();
                RebuildUnitViews();
            }
        }
    }

    /// <summary>The user picks the type, on a new material: what the other type had filled in goes (TypeEdited).</summary>
    public void ChangeType(RawMaterialType type)
    {
        if (type != Type)
        {
            Type = type;
            TypeEdited(type);
            BaseUnitEdited();
        }
    }

    private RawMaterialCategory? _category;
    public RawMaterialCategory? Category
    {
        get => _category;
        set => RaiseAndSetIfChanged(ref _category, value);
    }

    private RawMaterialSource _source = RawMaterialSource.Purchased;

    /// <summary>The material's source, as loaded, put back or picked (<see cref="ChangeSource"/>).</summary>
    public RawMaterialSource Source
    {
        get => _source;
        private set
        {
            if (RaiseAndSetIfChanged(ref _source, value))
            {
                CategoryTypeChanged();
            }
        }
    }

    /// <summary>The user picks the source, on a new material.</summary>
    public void ChangeSource(RawMaterialSource source)
    {
        if (source == Source)
        {
            return;
        }

        Source = source;

        // A made ingredient starts at 3% weight loss, since a small shrink is the normal case in the shop, and the box stays
        // freely editable: typing 0 is a legitimate custom value. Only when the box is empty, so one that holds a figure
        // keeps it.
        if (source == RawMaterialSource.Made)
        {
            WeightLossPercent ??= 3m;
        }
    }

    /// <summary>The kind of category the type and source can go in (RawMaterialRules.CategoryTypeFor): what the Category picker offers.</summary>
    public RawMaterialCategoryType CategoryType => RawMaterialRules.CategoryTypeFor(Type, Source);

    private decimal? _weightLossPercent;
    /// <summary>
    /// Made only: how much of the batch's weight is lost in the making, which its yield is less. A made ingredient's totals
    /// follow it, as it moves the yield the cost is spread over.
    /// </summary>
    public decimal? WeightLossPercent
    {
        get => _weightLossPercent;
        set
        {
            if (RaiseAndSetIfChanged(ref _weightLossPercent, value))
            {
                Recipe.BomInputsChanged();
            }
        }
    }

    private RawMaterial? _linkedTo;
    /// <summary>Linked only: the ingredient this one is a part of.</summary>
    public RawMaterial? LinkedTo
    {
        get => _linkedTo;
        set => RaiseAndSetIfChanged(ref _linkedTo, value);
    }

    private decimal? _weightSharePercent;
    /// <summary>Linked only: how much of the linked-to ingredient's weight this part is.</summary>
    public decimal? WeightSharePercent
    {
        get => _weightSharePercent;
        set => RaiseAndSetIfChanged(ref _weightSharePercent, value);
    }

    private decimal? _costSharePercent;
    /// <summary>Linked only: how much of the linked-to ingredient's cost this part carries.</summary>
    public decimal? CostSharePercent
    {
        get => _costSharePercent;
        set => RaiseAndSetIfChanged(ref _costSharePercent, value);
    }

    private UnitOfMeasure? _baseUnit;

    /// <summary>
    /// Packaging only; an ingredient's base unit is always the gram. As loaded, put back or picked (<see cref="ChangeBaseUnit"/>):
    /// the alternative units and the purchase unit choices name it.
    /// </summary>
    public UnitOfMeasure? BaseUnit
    {
        get => _baseUnit;
        private set
        {
            if (RaiseAndSetIfChanged(ref _baseUnit, value))
            {
                RebuildUnitViews();
            }
        }
    }

    /// <summary>The user picks the base unit, on a new packaging material: the brand rows priced in it move onto it (BaseUnitEdited).</summary>
    public void ChangeBaseUnit(UnitOfMeasure? unit)
    {
        if (unit != BaseUnit)
        {
            BaseUnit = unit;
            BaseUnitEdited();
        }
    }

    private bool _canChangeTypeAndSource = true;
    /// <summary>Only a new material's type and source can be picked: both are fixed once saved (RawMaterialRules).</summary>
    public bool CanChangeTypeAndSource
    {
        get => _canChangeTypeAndSource;
        private set => RaiseAndSetIfChanged(ref _canChangeTypeAndSource, value);
    }

    // The form's cards follow the type and source, not the selection, so a new material can be filled in completely before
    // its one Save.
    public bool IsEditingIngredient => Type == RawMaterialType.Ingredient;

    public bool IsEditingPackaging => Type == RawMaterialType.Packaging;

    /// <summary>A made ingredient: it has a sub-assembly, add-ins and a weight loss percent instead of a purchase price.</summary>
    public bool IsEditingMade => Type == RawMaterialType.Ingredient && Source == RawMaterialSource.Made;

    /// <summary>A linked ingredient: its cost is a share of another ingredient's, instead of a price or a BOM.</summary>
    public bool IsEditingLinked => Type == RawMaterialType.Ingredient && Source == RawMaterialSource.Linked;

    /// <summary>A purchased material, saved or still being filled in, so a new one can be priced before its first Save.</summary>
    public bool ShowPurchaseSetup => Type == RawMaterialType.Packaging || Source == RawMaterialSource.Purchased;

    /// <summary>The base unit's name: "gram" for an ingredient, e.g. "centimeter" for ribbon.</summary>
    public string BaseUnitName => BaseUnitText.NameOf(Type, BaseUnit);

    public string BaseUnitAbbreviation => BaseUnitText.AbbreviationOf(Type, BaseUnit);

    protected override void OnPropertyChanged(string? propertyName) => Touch();

    /// <summary>A saved material as read, everything of its own in place; its details (brand prices, add-ins) land after.</summary>
    public void Show(RawMaterial material)
    {
        CanChangeTypeAndSource = false;
        Name = material.Name;
        Type = material.Type;
        Category = _lists.Categories.FirstOrDefault(c => c.Id == material.CategoryId);
        Source = material.Source;
        WeightLossPercent = material.WeightLossPercent;
        Recipe.IsChangingSubAssembly = false;
        Recipe.SubAssembly = Recipe.SubAssemblyOptions.FirstOrDefault(s => s.Id == material.SubAssemblyId);
        LinkedTo = _lists.Ingredients.FirstOrDefault(m => m.Id == material.LinkedToId);
        WeightSharePercent = material.WeightSharePercent;
        CostSharePercent = material.CostSharePercent;
        PurchaseCost.EditingCostBufferPercent = material.CostBufferPercent;
        PurchaseCost.EditingTargetCostBufferPercent = RawMaterialRules.TargetBufferPercentOf(material);
        PurchaseCost.EditingPreferredBrand = material.PreferredBrand;
        PurchaseCost.EditingBufferedCost = material.BufferedCostPerBaseUnit;
        BaseUnit = _lists.UnitsOfMeasure.FirstOrDefault(u => u.Id == material.BaseUnitId);
    }

    /// <summary>
    /// A new material: <paramref name="type"/> and <paramref name="source"/>, set as the user sets them so nothing of another
    /// kind is left, in <paramref name="categoryId"/> when it's a category of that kind, its cost buffer and target
    /// <paramref name="defaultCostBufferPercent"/> (Settings'), everything else empty.
    /// </summary>
    public void Clear(RawMaterialType type, int? categoryId, RawMaterialSource source, decimal defaultCostBufferPercent)
    {
        CanChangeTypeAndSource = true;
        Name = string.Empty;

        ChangeType(type);

        // Emptied before the source is set, which gives a made one its starting weight loss.
        WeightLossPercent = null;
        ChangeSource(source);
        Category = _lists.Categories.FirstOrDefault(c => c.Id == categoryId && c.Type == RawMaterialRules.CategoryTypeFor(type, source));

        Recipe.IsChangingSubAssembly = false;
        Recipe.SubAssembly = null;
        LinkedTo = null;
        WeightSharePercent = null;
        CostSharePercent = null;
        PurchaseCost.EditingCostBufferPercent = defaultCostBufferPercent;
        PurchaseCost.EditingTargetCostBufferPercent = defaultCostBufferPercent;
        PurchaseCost.EditingPreferredBrand = null;
        PurchaseCost.EditingBufferedCost = null;
        ChangeBaseUnit(null);
    }

    /// <summary>
    /// A load's fresh copies taken in place of what the draft refers to, by id, nothing typed changed (ADR 0007): the category,
    /// the linked-to ingredient, the sub-assembly options and the one picked, the base unit, the add-ins' ingredients and the
    /// alternative units' units of measure, so a rename or a new price elsewhere shows.
    /// </summary>
    public void Rebase(int? categoryId, int? linkedToId, int? subAssemblyId, int? baseUnitId, IReadOnlyList<SubAssembly> subAssemblies)
    {
        Category = _lists.Categories.FirstOrDefault(c => c.Id == categoryId);
        LinkedTo = _lists.Ingredients.FirstOrDefault(m => m.Id == linkedToId);

        // A packaging set is never a recipe to make an ingredient from.
        Recipe.TakeOptions(subAssemblies);
        Recipe.SubAssembly = Recipe.SubAssemblyOptions.FirstOrDefault(s => s.Id == subAssemblyId);
        var previousBaseUnit = BaseUnit;
        BaseUnit = _lists.UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId);

        Recipe.ResolveAddIns();

        // Resolve points each row at the refreshed component; the totals are worked out again from them as they're read.
        Recipe.BomInputsChanged();

        // The alternative units may not be saved yet, so they stay rather than being reloaded from the refreshed material.
        // Each points at its refreshed unit of measure, so one renamed in Settings shows its new name.
        Units.RefreshUnitsOfMeasure();
        if (!ReferenceEquals(previousBaseUnit, BaseUnit))
        {
            Units.UpdateBaseUnit();
        }

        // Every time, so a unit of measure created or renamed in Settings shows as a purchase unit.
        PurchaseCost.RebuildPurchaseUnitOptions();
    }

    /// <summary>The draft as an undo step keeps it, for the material <paramref name="editingId"/>: what it refers to, by id.</summary>
    public RawMaterialFormSnapshot Capture(int editingId) => new()
    {
        EditingId = editingId,
        Name = Name,
        Type = Type,
        CategoryId = Category?.Id,
        Source = Source,
        CanChangeTypeAndSource = CanChangeTypeAndSource,
        BaseUnitId = BaseUnit?.Id,
        WeightLossPercent = WeightLossPercent,
        SubAssemblyId = Recipe.SubAssembly?.Id,
        LinkedToId = LinkedTo?.Id,
        WeightSharePercent = WeightSharePercent,
        CostSharePercent = CostSharePercent,
        CostBufferPercent = PurchaseCost.EditingCostBufferPercent,
        TargetCostBufferPercent = PurchaseCost.EditingTargetCostBufferPercent,
        PreferredBrand = PurchaseCost.EditingPreferredBrand,
        BufferedCost = PurchaseCost.EditingBufferedCost,
        BufferedCostUnitKey = PurchaseCost.BufferedCostUnit?.Key,
        UnitRows = Units.Capture(),
        BrandPrices = PurchaseCost.CaptureBrandPrices(),
        AddIns = Recipe.CaptureAddIns(),
    };

    /// <summary>
    /// Puts the draft back as <paramref name="snapshot"/> had it. The rows are rebuilt rather than written into: an
    /// IngredientLineRow's setters drop a typed percentage, and a unit row writes straight through to the stored
    /// RawMaterialUnit that Save hands the editor, so each unit comes back as a fresh entity carrying its own Id
    /// (RawMaterialUnits.Restore). The fields go back inside <paramref name="guard"/> (the form's ApplyRestore, so nothing
    /// written is taken for the user's), and the one recompute runs after it, so the costs and captions are worked out once
    /// from the whole draft put back.
    /// </summary>
    public void Restore(RawMaterialFormSnapshot snapshot, Action<Action>? guard = null)
    {
        void Put() => _updates.Batch(() =>
        {
            Units.SelectedRow = null;
            PurchaseCost.SelectedBrandPriceRow = null;
            Recipe.SelectedAddIn = null;

            CanChangeTypeAndSource = snapshot.CanChangeTypeAndSource;
            Name = snapshot.Name;
            Type = snapshot.Type;
            Category = snapshot.CategoryId is { } categoryId ? _lists.Categories.FirstOrDefault(c => c.Id == categoryId) : null;
            Source = snapshot.Source;
            BaseUnit = snapshot.BaseUnitId is { } baseUnitId ? _lists.UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId) : null;
            WeightLossPercent = snapshot.WeightLossPercent;
            Recipe.IsChangingSubAssembly = false;
            Recipe.SubAssembly = snapshot.SubAssemblyId is { } subAssemblyId ? Recipe.SubAssemblyOptions.FirstOrDefault(s => s.Id == subAssemblyId) : null;
            LinkedTo = snapshot.LinkedToId is { } linkedToId ? _lists.Ingredients.FirstOrDefault(m => m.Id == linkedToId) : null;
            WeightSharePercent = snapshot.WeightSharePercent;
            CostSharePercent = snapshot.CostSharePercent;
            PurchaseCost.EditingCostBufferPercent = snapshot.CostBufferPercent;
            PurchaseCost.EditingTargetCostBufferPercent = snapshot.TargetCostBufferPercent;
            PurchaseCost.EditingPreferredBrand = snapshot.PreferredBrand;
            PurchaseCost.EditingBufferedCost = snapshot.BufferedCost;

            Units.Restore(snapshot.UnitRows);
            PurchaseCost.RestoreBrandPrices(snapshot.BrandPrices);
            Recipe.RestoreAddIns(snapshot.AddIns);
        });

        if (guard is null)
        {
            Put();
        }
        else
        {
            guard(Put);
        }

        // The Per picker's choices are rebuilt from the units above, so the one that was picked is found again here rather
        // than within the guard: RebuildUnitViews replaces the list BufferedCostUnit is chosen from.
        RebuildUnitViews();
        PurchaseCost.BufferedCostUnit = snapshot.BufferedCostUnitKey is { } unitKey
            ? PurchaseCost.BufferedCostUnitOptions.FirstOrDefault(o => o.Key == unitKey)
            : null;

        Recipe.ResolveAddIns();
        PurchaseCost.RefreshBrandPriceRows();
        PurchaseCost.UpdatePurchaseCostTexts();

        // The Cost Buffer % box shows TypedCostBufferPercent, raised as the buffer changes: an undo that leaves the buffer as
        // it was doesn't, and without this the box would keep the percent from before the undo (undoing a Basis pick, say).
        PurchaseCost.RaiseTypedCostBufferPercent();
    }

    /// <summary>The material as a Save writes it, the material <paramref name="id"/> (0 for a new one), with when the form took it.</summary>
    public RawMaterial ToMaterial(int id, DateTime? savedVersion)
    {
        var source = Type == RawMaterialType.Packaging ? RawMaterialSource.Purchased : Source;
        return new RawMaterial
        {
            Id = id,
            Name = Name.Trim(),
            Type = Type,
            CategoryId = Category?.Id ?? 0,
            Source = source,
            // A cleared box shows 0.00, so it saves as 0%.
            WeightLossPercent = source == RawMaterialSource.Made ? WeightLossPercent ?? 0m : null,
            SubAssemblyId = source == RawMaterialSource.Made ? Recipe.SubAssembly?.Id : null,
            LinkedToId = source == RawMaterialSource.Linked ? LinkedTo?.Id : null,
            WeightSharePercent = source == RawMaterialSource.Linked ? WeightSharePercent : null,
            CostSharePercent = source == RawMaterialSource.Linked ? CostSharePercent : null,
            BaseUnitId = Type == RawMaterialType.Packaging ? BaseUnit?.Id : null,
            // A cleared box shows 0.00 too. A linked ingredient inherits the buffer of the one it's linked to (the editor sets
            // it from the saved one either way).
            CostBufferPercent = source == RawMaterialSource.Linked
                ? LinkedTo?.CostBufferPercent ?? 0m
                : PurchaseCost.EditingCostBufferPercent ?? 0m,
            TargetCostBufferPercent = source == RawMaterialSource.Purchased ? PurchaseCost.EditingTargetCostBufferPercent ?? 0m : null,
            PreferredBrand = source == RawMaterialSource.Purchased ? PurchaseCost.EditingPreferredBrand : null,
            // Only a purchased material's is the draft's; a made or linked one's is worked out by the cascade.
            BufferedCostPerBaseUnit = source == RawMaterialSource.Purchased ? PurchaseCost.EditingBufferedCost ?? 0m : 0m,
            // When it had last been saved as the form took it: a save since, from another PC, refuses this one (SavedVersionRules).
            UpdatedAt = savedVersion,
        };
    }

    /// <summary>
    /// What a Save of <paramref name="material"/> (<see cref="ToMaterial"/>, checked already) writes: its units, a purchased
    /// one's brand prices and a made one's add-ins when they changed. The tables are typed straight into, so a half-filled or
    /// repeated row is refused here (BusinessRuleException), naming it, and so is an add-in that's the material itself.
    /// </summary>
    public RawMaterialSaveRequest ToSaveRequest(RawMaterial material)
    {
        Units.EnsureValid();
        var brandPrices = material.Source == RawMaterialSource.Purchased ? PurchaseCost.CompleteBrandPrices() : null;

        // The picker leaves the material being edited out of its add-in options, so this can't be picked through the screen;
        // caught here with a plainer message than the editor's cycle check gives.
        if (material.Source == RawMaterialSource.Made && Recipe.AddIns.Any(l => l.IngredientId == material.Id))
        {
            throw new BusinessRuleException("An ingredient can't be an add-in of itself.");
        }

        var addIns = material.Source == RawMaterialSource.Made ? Recipe.ChangedAddIns() : null;
        return new RawMaterialSaveRequest(material, [.. Units.Rows.Select(r => r.Unit)], brandPrices, addIns);
    }

    /// <summary>
    /// The type or source changed: the Category picker offers that kind's categories (CategoryType, which the form follows). A
    /// new material's category of another kind is no longer one it can go in, so it goes. Only a new one's: a saved material's
    /// are fixed, and loading one sets its category before its source.
    /// </summary>
    private void CategoryTypeChanged()
    {
        RaisePropertyChanged(nameof(CategoryType));
        if (CanChangeTypeAndSource && Category is { } category && category.Type != CategoryType)
        {
            Category = null;
        }
    }

    /// <summary>
    /// The type changed by the user, on a new material: a packaging material is always purchased, and an ingredient's base
    /// unit is the gram, so whatever the other type had filled in goes.
    /// </summary>
    private void TypeEdited(RawMaterialType type)
    {
        if (type == RawMaterialType.Packaging)
        {
            ChangeSource(RawMaterialSource.Purchased);
            WeightLossPercent = null;
            LinkedTo = null;
            WeightSharePercent = null;
            CostSharePercent = null;
        }
        else
        {
            ChangeBaseUnit(null);
        }
    }

    /// <summary>The base unit changed by the user, on a new packaging material: the brand rows priced in it move onto it.</summary>
    private void BaseUnitEdited()
    {
        PurchaseCost.MoveBrandRowsOntoTheBaseUnit();
        RebuildUnitViews();
    }

    /// <summary>Shows the base unit on the alternative units and in the purchase unit choices.</summary>
    private void RebuildUnitViews()
    {
        Units.UpdateBaseUnit();
        PurchaseCost.RebuildPurchaseUnitOptions();
    }

    /// <summary>A unit's multiplier changed, or a unit went: the brands bought in it are priced again.</summary>
    private void RepriceBrands()
    {
        PurchaseCost.RefreshBrandPriceRows();
        PurchaseCost.UpdatePurchaseCostTexts();
    }

    private void Touch()
    {
        Version++;
        Changed?.Invoke();
    }

    public void Dispose() => Recipe.Dispose();

    // What the Alternative Units tab, the Cost Setup tab and a made ingredient's recipe ask of the draft, their host.
    void IUndoRecorder.BeginUndoStep() => _host.BeginUndoStep();

    void IUndoRecorder.EndUndoStep() => _host.EndUndoStep();

    bool IUndoRecorder.IsEditPending
    {
        get => _host.IsEditPending;
        set => _host.IsEditPending = value;
    }

    int IUndoRecorder.FormVersion => _host.FormVersion;

    RawMaterialType IRawMaterialUnitsHost.EditingType => Type;

    UnitOfMeasure? IRawMaterialUnitsHost.EditingBaseUnit => BaseUnit;

    void IRawMaterialUnitsHost.UnitsChanged() => RepriceBrands();

    RawMaterialType IRawMaterialPurchaseCostHost.EditingType => Type;

    UnitOfMeasure? IRawMaterialPurchaseCostHost.EditingBaseUnit => BaseUnit;

    RawMaterial? IRawMaterialPurchaseCostHost.SelectedRawMaterial => _host.SelectedRawMaterial;

    decimal IRawMaterialPurchaseCostHost.BomTotalWeight => Recipe.BomTotalWeight;

    decimal IRawMaterialPurchaseCostHost.BomTotalCost => Recipe.BomTotalCost;

    bool IRawMaterialRecipeHost.AreDetailsReady => _host.AreDetailsReady;

    decimal? IRawMaterialRecipeHost.WeightLossPercent => WeightLossPercent;

    string IRawMaterialRecipeHost.EditingName => Name;
}
