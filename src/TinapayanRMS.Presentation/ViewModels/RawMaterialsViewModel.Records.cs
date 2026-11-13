using System.Diagnostics.CodeAnalysis;
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
using TinapayanRMS.Pricing;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.ViewModels;

// Opening, picking, loading, making, saving and deleting materials; the form's cards, its price history and its dependents.
public sealed partial class RawMaterialsViewModel
{
    /// <summary>
    /// The form's cards follow its type and source, the Price History tab a purchased material, and the header its
    /// name.
    /// </summary>
    [MemberNotNull(
        nameof(_isEditingIngredient), nameof(_isEditingPackaging), nameof(_isEditingMade), nameof(_isEditingLinked),
        nameof(_linkedCostPreviewText), nameof(_linkedPartsText), nameof(_showPurchaseSetup), nameof(_showPriceHistory),
        nameof(_headerTitle))]
    private void WireForm()
    {
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
    }

    /// <summary>
    /// A material picked in the list loads into the form; the Dependents tiles' shares follow the tab.
    /// </summary>
    private void WireDetails()
    {
        // A pick loads the material into the form, in these three steps, in this order. Only a pick does (_picks): a load
        // re-points the selection at its fresh copy quietly, so nothing here has to tell the two apart.
        _picks
            .Where(material => material is not null)
            .Subscribe(material => LoadIntoForm(material!));

        _picks.Subscribe(ResetDetails);

        // A newer pick cancels an older one still loading (Switch disposes it, which cancels its token), so a quick second
        // pick never gets the first one's details added to its own. One that fails shows why, and the next pick loads as usual.
        LoadLatest(_picks, LoadDetailsAsync);

        // The Dependents tiles' shares, when the tab shows or its tiles change: a newer ask cancels one still working.
        LoadLatest(
            _costSharesWanted.Merge(this.WhenAnyValue(x => x.SelectedDetailsTab).Select(_ => RxVoid.Default)),
            (_, cancellationToken) => ShowCostSharesAsync(cancellationToken));
    }

    /// <summary>Load: everything the screen shows is read again, and merged in.</summary>
    [MemberNotNull(nameof(LoadCommand))]
    private void WireLoadCommand(IUnitOfMeasureRepository unitOfMeasureRepository)
    {
        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, picker choices and unsaved edits
        // while picking up what changed elsewhere, such as a unit renamed in Settings.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            // All at once, on the thread pool, as Products reads its lists: one after another on the UI thread, each read came
            // back to it before the next began, and waited its turn behind whatever the screen was drawing. The products
            // using the material showing are read with them, since Products may have started or stopped using it meanwhile.
            var detailMaterialId = _detailMaterialId;
            var categoriesRead = Task.Run(() => _categoryRepository.GetAllAsync(CancellationToken.None));
            var unitsRead = Task.Run(() => unitOfMeasureRepository.GetAllAsync(CancellationToken.None));
            var rawMaterialsRead = Task.Run(() => _rawMaterialRepository.GetAllAsync(CancellationToken.None));
            var costingDefaultsRead = Task.Run(() => _costingDefaultsRepository.GetAsync(CancellationToken.None));
            var subAssembliesRead = Task.Run(() => _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None));
            var dependentsRead = detailMaterialId is { } dependentsOf
                ? Task.Run(() => _rawMaterialRepository.GetDependentsAsync(dependentsOf, CancellationToken.None))
                : Task.FromResult<IReadOnlyList<LinkingProduct>>([]);
            await Task.WhenAll(categoriesRead, unitsRead, rawMaterialsRead, costingDefaultsRead, subAssembliesRead, dependentsRead);

            var categories = await categoriesRead;
            var units = await unitsRead;
            var rawMaterials = await rawMaterialsRead;
            var costingDefaults = await costingDefaultsRead;
            var subAssemblies = await subAssembliesRead;

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedRawMaterial?.Id;
            var categoryId = EditingCategory?.Id;
            var linkedToId = EditingLinkedTo?.Id;
            var subAssemblyId = EditingSubAssembly?.Id;
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

                // A packaging set is never a recipe to make an ingredient from.
                SubAssemblyOptions.MergeFrom([.. subAssemblies.Where(s => s.Role is not { IsPackaging: true })], s => s.Id);
                ShowSubAssembly(SubAssemblyOptions.FirstOrDefault(s => s.Id == subAssemblyId));
                var previousBaseUnit = EditingBaseUnit;
                ShowBaseUnit(UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId));

                IngredientLineRow.Resolve(AddInEdits, Ingredients.ToDictionary(m => m.Id));

                // Resolve points each row at the refreshed component, so this is what carries a component repriced
                // elsewhere into the header's totals: the change set above is suppressed while a refresh applies.
                RecomputeBomTotals();

                RepointSelection(RawMaterials.FirstOrDefault(m => m.Id == selectedId));

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

            // The Sub-Assemblies screen may have changed the recipe's lines. Re-pointing the pick reads them only when the
            // sub-assembly's own row changed, so they're read here every time.
            if (EditingSubAssembly is { } recipe)
            {
                await LoadRecipeLinesAsync(recipe, CancellationToken.None);
            }

            // The products using the material, read with the lists: kept only if the details showing are still that material's.
            if (detailMaterialId is { } readFor && _detailMaterialId == readFor)
            {
                ShowDependents(await dependentsRead);
            }

            // Outside ApplyRefresh, whose selection changes only re-point: this one has to load the material's form.
            if (_rawMaterialToOpen is { } rawMaterialId)
            {
                _rawMaterialToOpen = null;
                TrySelectRawMaterial(rawMaterialId);
            }
        }));
    }

    /// <summary>New, Save and Delete, and opening a product that uses the material.</summary>
    [MemberNotNull(nameof(NewCommand), nameof(OpenProductCommand), nameof(SaveCommand), nameof(DeleteCommand))]
    private void WireRecordCommands()
    {
        // A new material replaces the one showing, so its unsaved changes are asked about first.
        NewCommand = ReactiveCommand.CreateFromTask(() => AfterUnsavedChangesAsync(() => NewMaterialInList()));

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
                Name = EditingName.Trim(),
                Type = type,
                CategoryId = EditingCategory?.Id ?? 0,
                Source = source,
                // A cleared box shows 0.00, so it saves as 0%.
                WeightLossPercent = source == RawMaterialSource.Made ? EditingWeightLossPercent ?? 0m : null,
                SubAssemblyId = source == RawMaterialSource.Made ? EditingSubAssembly?.Id : null,
                LinkedToId = source == RawMaterialSource.Linked ? EditingLinkedTo?.Id : null,
                WeightSharePercent = source == RawMaterialSource.Linked ? EditingWeightSharePercent : null,
                CostSharePercent = source == RawMaterialSource.Linked ? EditingCostSharePercent : null,
                BaseUnitId = type == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null,
                // A cleared box shows 0.00 too. A linked ingredient inherits the buffer of the one it's linked to
                // (the repository sets it from the saved one either way).
                CostBufferPercent = source == RawMaterialSource.Linked
                    ? EditingLinkedTo?.CostBufferPercent ?? 0m
                    : EditingCostBufferPercent ?? 0m,
                TargetCostBufferPercent = source == RawMaterialSource.Purchased ? EditingTargetCostBufferPercent ?? 0m : null,
                PreferredBrand = source == RawMaterialSource.Purchased ? EditingPreferredBrand : null,
                // Only a purchased material's is the form's; a made or linked one's is worked out by the cascade.
                BufferedCostPerBaseUnit = source == RawMaterialSource.Purchased ? EditingBufferedCost ?? 0m : 0m,
            };

            var validation = _rawMaterialValidator.Validate(material);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            // The units and brand prices are typed straight into their grids, so a half-filled or repeated one is caught here.
            EnsureValidUnitRows();
            var brandPrices = source == RawMaterialSource.Purchased ? CompleteBrandPrices() : null;

            // The picker already leaves the material being edited out of AddInOptions, so this can't be picked through
            // the UI; caught here with a plainer message than the repository's cycle check gives.
            if (source == RawMaterialSource.Made && AddInEdits.Any(l => l.IngredientId == _editingId))
            {
                ErrorMessage = "An ingredient can't be an add-in of itself.";
                return;
            }

            var addIns = source == RawMaterialSource.Made ? ChangedAddIns() : null;

            var id = await _rawMaterialRepository.SaveAsync(material, [.. UnitRows.Select(r => r.Unit)], brandPrices, addIns, CancellationToken.None);

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
    }

    /// <summary>
    /// New, from the header or the list. A material still waiting for LoadCommand to open it is no longer opened: it would
    /// load over the new form, and a name typed into it since would then be saved as that material's.
    /// </summary>
    private void StartNewMaterial(
        RawMaterialType type = RawMaterialType.Ingredient, int? categoryId = null, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        _rawMaterialToOpen = null;
        ClearForm(type, categoryId, source);
    }

    /// <summary>
    /// Copy, from the list: a new material of <paramref name="material"/>'s type, in its category, with its base unit,
    /// named "… Copy" ("… Copy 2" when that's taken). A made ingredient's copy is a made one, as its category holds only
    /// those; a linked one's is purchased. The name and the base unit come after the form is settled, so the copy is
    /// there to be saved, and leaving it asks first.
    /// </summary>
    private void StartCopyOf(RawMaterial material)
    {
        StartNewMaterial(
            material.Type,
            material.CategoryId,
            material.Source == RawMaterialSource.Made ? RawMaterialSource.Made : RawMaterialSource.Purchased);
        EditingName = CopyNameFor(material.Name);
        if (material.Type == RawMaterialType.Packaging)
        {
            EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == material.BaseUnitId);
        }
    }

    /// <summary>"Disposable Spoon Copy", or the first of "… Copy 2", "… Copy 3" and on that no material has yet.</summary>
    private string CopyNameFor(string name)
    {
        var copy = $"{name} Copy";
        for (var n = 2; RawMaterials.Any(m => string.Equals(m.Name, copy, StringComparison.OrdinalIgnoreCase)); n++)
        {
            copy = $"{name} Copy {n}";
        }

        return copy;
    }

    /// <summary>
    /// An empty form, for New and after Delete. It starts as a purchased ingredient, the usual kind, but
    /// the category, brand prices and the usage example's material start empty, so none is saved or
    /// costed by accident. The numbers keep a value: the cost buffer is Settings' default, and the usage example's
    /// quantity stays. The list's New … Here starts it as <paramref name="type"/> in <paramref name="categoryId"/>
    /// instead, and as a made ingredient (<paramref name="source"/>) in a Made category, set before the form is settled, so
    /// it isn't taken for an edit to save.
    /// </summary>
    private void ClearForm(
        RawMaterialType type = RawMaterialType.Ingredient, int? categoryId = null, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        // The new material's row, if one was named in the list, goes with the form it stood for.
        EndNewMaterialRow();
        History.Clear();
        FormVersion++;
        _editingId = 0;
        SelectedRawMaterial = null;
        CanChangeTypeAndSource = true;
        EditingName = string.Empty;
        EditingType = type;
        // Emptied before the source is set, which gives a made one its starting weight loss.
        EditingWeightLossPercent = null;
        EditingSource = source;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId && c.Type == RawMaterialRules.CategoryTypeFor(type, source));

        IsChangingSubAssembly = false;
        EditingSubAssembly = null;
        EditingLinkedTo = null;
        EditingWeightSharePercent = null;
        EditingCostSharePercent = null;
        EditingCostBufferPercent = _defaultCostBufferPercent;
        EditingTargetCostBufferPercent = _defaultCostBufferPercent;
        EditingPreferredBrand = null;
        EditingBufferedCost = null;
        EditingBaseUnit = null;
        ExampleRawMaterial = null;
        ClearDetails();
    }

    private void LoadIntoForm(RawMaterial material)
    {
        History.Clear();

        // Not for the same material loaded again, as a Save does: the boxes still hold what's its.
        if (material.Id != _editingId)
        {
            FormVersion++;
        }

        _editingId = material.Id;
        CanChangeTypeAndSource = false;
        EditingName = material.Name;
        ShowType(material.Type);
        EditingCategory = Categories.FirstOrDefault(c => c.Id == material.CategoryId);
        ShowSource(material.Source);
        EditingWeightLossPercent = material.WeightLossPercent;
        IsChangingSubAssembly = false;
        ShowSubAssembly(SubAssemblyOptions.FirstOrDefault(s => s.Id == material.SubAssemblyId));
        EditingLinkedTo = Ingredients.FirstOrDefault(m => m.Id == material.LinkedToId);
        EditingWeightSharePercent = material.WeightSharePercent;
        EditingCostSharePercent = material.CostSharePercent;
        EditingCostBufferPercent = material.CostBufferPercent;
        EditingTargetCostBufferPercent = RawMaterialRules.TargetBufferPercentOf(material);
        EditingPreferredBrand = material.PreferredBrand;
        EditingBufferedCost = material.BufferedCostPerBaseUnit;
        ShowBaseUnit(UnitsOfMeasure.FirstOrDefault(u => u.Id == material.BaseUnitId));
    }

    // A material loaded, an undo putting the form back, or a load's fresh copy: each field written and said to have changed
    // without the edit that setting it is (TypeEdited, the made ingredient's starting weight loss, BaseUnitEdited, Edit
    // settled), so nothing follows from it but what's worked out from it.
    private void ShowType(RawMaterialType type) => this.RaiseAndSetIfChanged(ref _editingType, type, nameof(EditingType));

    private void ShowSource(RawMaterialSource source) => this.RaiseAndSetIfChanged(ref _editingSource, source, nameof(EditingSource));

    private void ShowBaseUnit(UnitOfMeasure? unit) => this.RaiseAndSetIfChanged(ref _editingBaseUnit, unit, nameof(EditingBaseUnit));

    private void ShowSubAssembly(SubAssembly? subAssembly) =>
        this.RaiseAndSetIfChanged(ref _editingSubAssembly, subAssembly, nameof(EditingSubAssembly));

    /// <summary>
    /// The type changed by the user, on a new material: a packaging material is always purchased, and an ingredient's base
    /// unit is the gram, so whatever the other type had filled in goes.
    /// </summary>
    private void TypeEdited(RawMaterialType type)
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
    }

    /// <summary>The base unit changed by the user, on a new packaging material: the brand rows priced in it move onto it.</summary>
    private void BaseUnitEdited()
    {
        MoveBrandRowsOntoTheBaseUnit();
        RebuildUnitViews();
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
        SelectedAddInEdit = null;
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
        IReadOnlyList<LinkingProduct> Dependents)
    {
        public static readonly RawMaterialDetail None = new([], [], [], []);
    }

    /// <summary>
    /// Only the repositories: this runs off the UI thread. A purchased material has prices, a made one a BOM, and a linked
    /// one neither; any of them can be used by products.
    /// </summary>
    private async Task<RawMaterialDetail> ReadDetailAsync(RawMaterial material, CancellationToken cancellationToken)
    {
        var dependents = await _rawMaterialRepository.GetDependentsAsync(material.Id, cancellationToken);
        return material.Source switch
        {
            RawMaterialSource.Purchased => new RawMaterialDetail(
                await _priceHistoryRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                await _brandPriceRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                [],
                dependents),
            RawMaterialSource.Made => new RawMaterialDetail([], [], await _bomLineRepository.GetForIngredientAsync(material.Id, cancellationToken), dependents),
            _ => RawMaterialDetail.None with { Dependents = dependents },
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

        // The recipe's own lines show from the sub-assembly picked (LoadRecipeLinesAsync); only the add-ins are the form's.
        AddInEdits.Clear();
        AddInEdits.AddRange(detail.BomLines.Where(l => l.IsAddIn).Select(l => new IngredientLineRow(l.ComponentIngredient!, l.Grams)));
        _savedAddIns = CurrentAddIns();

        BrandPriceRows.Clear();
        BrandPriceRows.AddRange(detail.BrandPrices.Select(p =>
            new BrandPriceRow(this, p.Brand, p.Unit?.UnitOfMeasureId ?? PurchaseUnitChoice.BaseUnitKey, p.Price)));
        _savedBrandPriceCount = detail.BrandPrices.Count;

        ShowDependents(detail.Dependents);

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

    private void ShowDependents(IReadOnlyList<LinkingProduct> products)
    {
        Dependents = products;
        DependentsText = products.Count == 0 ? NotUsedText : UsedText;
        _costSharesWanted.OnNext(RxVoid.Default);
    }

    /// <summary>What each Dependents product's share is worked out by; null in a screen built without it (tests that don't need it).</summary>
    private readonly MaterialCostShares? _costShares;

    /// <summary>Asks for the Dependents tiles' shares again: the tiles changed, or the tab showed.</summary>
    private readonly Subject<RxVoid> _costSharesWanted = new();

    /// <summary>
    /// The Dependents tiles' "38% of materials", worked out only while the tab shows, since it reads every product
    /// the material goes into: the material at no cost against as it is (MaterialCostShares). Kept on the tiles until they change.
    /// </summary>
    private async Task ShowCostSharesAsync(CancellationToken cancellationToken)
    {
        if (_costShares is null
            || SelectedDetailsTab != DependentsTab
            || SelectedRawMaterial is not { } material
            || Dependents is not { Count: > 0 } tiles
            || tiles.All(t => t.CostShareText is not null))
        {
            return;
        }

        var shares = await Task.Run(() => _costShares.ReadAsync(material.Id, [.. tiles.Select(t => t.Id)], cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (ReferenceEquals(Dependents, tiles))
        {
            Dependents = [.. tiles.Select(t => t with { CostShareText = MaterialCostShares.Text(shares.GetValueOrDefault(t.Id)) })];
        }
    }

    /// <summary>The Price History grid, newest first.</summary>
    private void ReplacePriceHistory(RawMaterial? material, IReadOnlyList<RawMaterialPriceHistory> entries)
    {
        var baseUnitName = material is null ? string.Empty : BaseUnitText.NameOf(material);
        PriceHistory.Clear();
        PriceHistory.AddRange(entries.Select(e => new RawMaterialPriceRow(e, baseUnitName)));
    }

    /// <summary>
    /// The Last Updated tile: the day the material's cost last changed (CostUpdatedAt), and nothing else. Not the
    /// newest price history entry's date, which a made or linked ingredient never has, so it would show "—" for them
    /// however recently their cost had moved. The material also carries UpdatedAt,
    /// the day the form last saved it; nothing shows it.
    /// </summary>
    private void ShowUpdatedAt(RawMaterial? material) =>
        LastUpdatedText = material?.CostUpdatedAt?.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) ?? "—";

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

    /// <summary>
    /// Opens a material at the end of the next LoadCommand, for the one in the screen's address as the screen opens:
    /// its list is fresh only then. New clicked meanwhile is the later word, and the material isn't opened over it.
    /// </summary>
    public void OpenRawMaterialOnLoad(int rawMaterialId)
    {
        _rawMaterialToOpen = rawMaterialId;
    }

    /// <summary>
    /// <see cref="OpenRawMaterial"/>, asking first about the unsaved changes of the material showing when another is to
    /// replace it. False when the user stays on it.
    /// </summary>
    public async Task<bool> OpenRawMaterialAsync(int rawMaterialId)
    {
        if (rawMaterialId != SelectedRawMaterial?.Id && !await ResolveUnsavedChangesAsync(revert: false))
        {
            return false;
        }

        OpenRawMaterial(rawMaterialId);
        return true;
    }

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: opens the material the name means best.</summary>
    public bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(RawMaterials, material => material.Name, text) is not { } material)
        {
            return false;
        }

        _ = RunSafelyAsync(() => OpenRawMaterialAsync(material.Id));
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
}
