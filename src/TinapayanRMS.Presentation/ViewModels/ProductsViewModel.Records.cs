using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

// Opening, picking and loading products, and making, cloning and deleting them.
public sealed partial class ProductsViewModel
{
    /// <summary>The header's title, and where a new product goes.</summary>
    [MemberNotNull(nameof(_headerTitle), nameof(_newProductTag))]
    private void WireHeader()
    {
        _headerTitle = this.WhenAnyValue(x => x.SelectedProduct, x => x.EditingName, x => x.IsEditingComposite, (product, name, isComposite) =>
                product is null && string.IsNullOrWhiteSpace(name) ? (isComposite ? "New composite product" : "New product") : name)
            .ToProperty(this, x => x.HeaderTitle, "New product");

        // Its folder's path as it is now: renamed or moved since New Product Here, it says so (Folders).
        _newProductTag = this.WhenAnyValue(x => x.SelectedProduct, x => x.NewProductStage, x => x.NewProductFolderId, x => x.Folders, (product, stage, folderId, folders) =>
                product is null ? $"Goes in {ProductFolderRules.PathTitle(folders, stage, folderId)}" : null)
            .ToProperty(this, x => x.NewProductTag);
    }

    /// <summary>A product picked in the list loads into the form.</summary>
    private void WirePicks()
    {
        // A pick loads the product into the form, in these three steps, in this order. Only a pick does (_picks): a load
        // re-points the selection at its fresh copy quietly, so nothing here has to tell the two apart.
        // Another product's name and price land in the form ahead of its details, so its history goes first, as
        // ClearDetailsForAnother would take it anyway: measured against the previous product's saved form, they read
        // as unsaved changes, and Save lit up until the details arrived — a flicker on every pick.
        _picks
            .Where(product => product is not null)
            .Subscribe(product =>
            {
                if (product!.Id != _detailProductId)
                {
                    History.Clear();
                }

                LoadIntoForm(product);
            });

        _picks.Subscribe(ResetDetails);

        // A newer pick cancels an older one still loading; one that fails shows why, and the next pick loads as usual.
        LoadLatest(_picks, LoadProductDetailAsync);
    }

    /// <summary>Load: everything the screen shows is read again, and merged in.</summary>
    [MemberNotNull(nameof(LoadCommand))]
    private void WireLoadCommand(ISubAssemblyRoleRepository subAssemblyRoleRepository, IRawMaterialRepository rawMaterialRepository)
    {
        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, pickers and unsaved edits while
        // picking up what changed elsewhere: a repriced ingredient, or a linked sub-assembly its owner
        // changed.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            // A linked section reads a sub-assembly kept on the Sub-Assemblies screen, which can change there (its
            // lines, name or role), so those are fetched again; the product's own and new sections only need their
            // ingredients' new costs. Every saved one is fetched: a role made product-owned, or no longer, in Settings
            // turns a linked one into the product's own, or back.
            var detailProductId = _detailProductId;
            HashSet<int> linkedIds = [.. UsageEdits.Where(u => u.SubAssemblyId > 0).Select(u => u.SubAssemblyId)];

            // A composite's products can have changed elsewhere since they were read (a recipe, an ingredient's price, a
            // quantity required), so what each brings is read again; its lines may hold unsaved edits and stay as they are.
            List<int> componentIds = [.. ComponentsTable.ProductIds];

            // All at once, on the thread pool: each read is mostly a round trip's wait, so one after another they took
            // several times as long as the slowest of them, and the UI thread came back after each to start the next.
            // Each has its own context (AddDbContextFactory), and none touches ViewModel state.
            var rolesRead = Task.Run(() => subAssemblyRoleRepository.GetAllAsync(CancellationToken.None));
            var categoriesRead = Task.Run(() => _categoryRepository.GetAllAsync(CancellationToken.None));
            var foldersRead = Task.Run(() => _folderRepository.GetAllAsync(CancellationToken.None));
            var rawMaterialsRead = Task.Run(() => rawMaterialRepository.GetAllAsync(CancellationToken.None));
            var productsRead = Task.Run(() => _productRepository.GetAllAsync(CancellationToken.None));
            var deletedRead = Task.Run(() => _productRepository.GetDeletedAsync(CancellationToken.None));
            var linkableRead = Task.Run(() => _subAssemblyRepository.GetLinkableAsync(CancellationToken.None));
            var standaloneRead = Task.Run(() => _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None));
            var costingDefaultsRead = Task.Run(() => _costingDefaultsRepository.GetAsync(CancellationToken.None));
            var linkedSubAssembliesRead = Task.Run(() => _subAssemblyRepository.GetByIdsAsync(linkedIds, CancellationToken.None));
            var linkedLinesRead = Task.Run(() => _subAssemblyLineRepository.GetForSubAssembliesAsync(linkedIds, CancellationToken.None));
            var packagingLinkRead = detailProductId is { } linkProductId
                ? Task.Run(() => _packagingLinkRepository.GetForProductAsync(linkProductId, CancellationToken.None))
                : Task.FromResult<PackagingLink?>(null);
            var componentLinksRead = Task.Run(() => _productRepository.GetAllComponentsAsync(CancellationToken.None));
            var componentCostsRead = Task.Run(() => _savedProducts.ReadComponentCostsAsync(componentIds, CancellationToken.None));
            await Task.WhenAll(
                rolesRead, categoriesRead, foldersRead, rawMaterialsRead, productsRead, deletedRead, linkableRead, standaloneRead, costingDefaultsRead,
                linkedSubAssembliesRead, linkedLinesRead, packagingLinkRead, componentLinksRead, componentCostsRead);

            var roles = await rolesRead;
            var categories = await categoriesRead;
            var folders = await foldersRead;
            var rawMaterials = await rawMaterialsRead;
            var products = await productsRead;
            var deleted = await deletedRead;
            var linkable = await linkableRead;
            var packagingSets = (await standaloneRead).Where(s => s.Role?.IsPackaging == true).ToList();
            var costingDefaults = await costingDefaultsRead;
            var linkedSubAssemblies = await linkedSubAssembliesRead;
            var linkedLines = await linkedLinesRead;
            var packagingLink = await packagingLinkRead;
            var componentLinks = await componentLinksRead;
            var componentCosts = await componentCostsRead;

            // Linked packaging is kept elsewhere (another product, or a set on the Sub-Assemblies screen) and can
            // have changed there, so it's fetched again — but only while the form still shows the link as saved. The
            // product's own lines aren't, since they may hold unsaved edits, and nor is a link picked but not saved.
            var savedLink = packagingLink is null ? null : PackagingLinkTarget.From(packagingLink);
            var showsSavedLink = savedLink is not null && Packaging.ShowsSavedLink(savedLink);
            var linkedPackagingLines = showsSavedLink
                ? await _packagingLineRepository.GetEffectiveForProductAsync(packagingLink!.ProductId, CancellationToken.None)
                : null;

            // A role made product-owned in Settings, or no longer, since the product was loaded: a section it linked is its
            // own now, or one of its own is linked now. With nothing typed into the form, the product is read again, so what
            // Save compares against is how it's saved now; with something typed, RefreshRecipeRows turns the section round
            // in place, and the next Save writes it as it shows.
            var ownershipChanged = UsageEdits.Any(u => linkedSubAssemblies.FirstOrDefault(s => s.Id == u.SubAssemblyId) is { } saved
                && (saved.OwnerProductId is not null && saved.OwnerProductId == detailProductId) != u.IsOwned);
            var reloadDetails = ownershipChanged && !History.IsDirty && detailProductId is not null;

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedProduct?.Id;
            var categoryId = EditingCategory?.Id;
            var usageRoleId = UsageForm.EditingUsageRole?.Id;
            var usageSourceKey = UsageForm.EditingUsageSource?.Key;

            ApplyRefresh(() =>
            {
                Roles.MergeFrom(roles, r => r.Id);
                _categoryCache.MergeFrom(categories);
                EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
                Ingredients.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
                PackagingMaterials.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);
                // The Deleted folder and the folders first, so the tree, refreshed as the products merge, files a product
                // deleted, restored or moved since in its new place in one go. The tree is refreshed again for a change to
                // those alone.
                DeletedProducts = deleted;
                Folders = folders;
                _productCache.MergeFrom(products);
                RefreshProductTree();
                _markupChecks.OnNext(products);
                _linkable = linkable;
                _packagingSets = packagingSets;
                RefreshCostingDefaults(costingDefaults);

                // A composite's lines point at the fresh products (a rename shows) at their fresh recipe costs.
                ComponentsTable.TakeFresh(componentCosts, componentLinks);
                RefreshComponentOptions();

                RepointSelection(Products.FirstOrDefault(p => p.Id == selectedId));
                if (SelectedProduct is not null)
                {
                    ShowUpdatedAt(SelectedProduct.UpdatedAt);
                }

                if (detailProductId == _detailProductId && linkedPackagingLines is not null)
                {
                    // A renamed product or set reads right in the banner too.
                    Packaging.ShowLink(savedLink);
                    Packaging.LoadLines(linkedPackagingLines);
                }

                RefreshRecipeRows(linkedSubAssemblies, linkedLines);
                RefreshFormLines();
                RefreshLinkableOptions();

                // An add form opened before the roles were loaded takes the first role it can have, as it would have.
                UsageForm.ShowRole(UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == usageRoleId)
                    ?? (usageRoleId is null && UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage ? UsageForm.FormRoleOptions.FirstOrDefault() : null));
                UsageForm.ShowSource(UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.Key == usageSourceKey));

                UsageForm.Recompute();
                RecomputeCosts();
            });

            if (reloadDetails && SelectedProduct is { } reloaded && reloaded.Id == detailProductId)
            {
                await LoadProductDetailAsync(reloaded, CancellationToken.None);
            }

            // Outside ApplyRefresh, whose selection changes only re-point: this one has to load the product's form.
            if (_productToOpen is { } productId)
            {
                _productToOpen = null;
                TrySelectProduct(productId);
            }

            if (_subAssemblyToStartFrom is { } subAssemblyId)
            {
                _subAssemblyToStartFrom = null;
                StartNewProductLinking(subAssemblyId);
            }

            // A save elsewhere can have recorded a version of this product since (a sub-assembly it links, saved on the
            // Sub-Assemblies screen): a History tab showing reads its versions again, and one hidden when it next shows.
            if (SelectedDetailsTab == HistoryTab)
            {
                RequestVersions(force: true);
            }
            else
            {
                VersionHistory.Clear();
            }
        }));
    }

    /// <summary>New, Delete, Clone and Create Variant.</summary>
    [MemberNotNull(
        nameof(NewCommand), nameof(NewCompositeCommand), nameof(NewCompositeProductInCommand), nameof(DeleteCommand),
        nameof(CloneCommand), nameof(CreateVariantCommand))]
    private void WireRecordCommands()
    {
        // A new product replaces the one showing, so its unsaved changes are asked about first.
        // Each adds the new product's row to the list, for its name to be typed into.
        NewCommand = ReactiveCommand.CreateFromTask(() => AfterUnsavedChangesAsync(() => NewProductInList()));
        NewCompositeCommand = ReactiveCommand.CreateFromTask(() => AfterUnsavedChangesAsync(() => NewProductInList(isComposite: true)));
        NewCompositeProductInCommand = ReactiveCommand.CreateFromTask<ProductFolderNode?>(folder => AfterUnsavedChangesAsync(() => NewProductInList(
            folder?.Stage, folder?.FolderId, isComposite: true)));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => DeleteProductAsync(SelectedProduct!),
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        // A clone copies the product as saved, and a variant links it as saved: unsaved edits stay with this one.
        var canMakeFromSelected = this.WhenAnyValue(
            x => x.SelectedProduct, x => x.EditingCloneName, (product, name) => product is not null && !string.IsNullOrWhiteSpace(name));
        CloneCommand = ReactiveCommand.CreateFromTask(() => MakeFromAsync(SelectedProduct!.Id, EditingCloneName, _productCloner.CloneAsync, ClonedFrom), canMakeFromSelected);
        CreateVariantCommand = ReactiveCommand.CreateFromTask(() => MakeFromAsync(SelectedProduct!.Id, EditingCloneName, _productCloner.CreateVariantAsync, VariantOf), canMakeFromSelected);
    }

    /// <summary>
    /// Makes a product named <paramref name="name"/> from <paramref name="sourceProductId"/> as saved (a clone or a
    /// variant) with its first version, noted "<paramref name="madeAs"/>" the source, both or neither
    /// (VersionedSaves.MakeProductFromAsync), and opens it. Opening it replaces the product showing, so that one's unsaved
    /// changes are asked about first: saved, they're in what's copied.
    /// </summary>
    private async Task MakeFromAsync(int sourceProductId, string name, Func<int, string, CancellationToken, Task<int>> make, string madeAs)
    {
        if (!await ResolveUnsavedChangesAsync(revert: false))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var newId = await Task.Run(() => _versionedSaves.MakeProductFromAsync(
                sourceProductId, name, make, madeAs, DateTime.Now, CancellationToken.None));

            await LoadCommand.Execute();
            SelectedProduct = Products.FirstOrDefault(p => p.Id == newId);
        });
    }

    /// <summary>The list's Clone and Create Variant: asks for the new product's name first, starting from "… Copy".</summary>
    private async Task MakeFromWithPromptAsync(
        ProductLeafNode leaf, TextPromptRequest prompt, Func<int, string, CancellationToken, Task<int>> make, string madeAs)
    {
        var name = (await PromptForName.Handle(prompt))?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            await MakeFromAsync(leaf.Product.Id, name, make, madeAs);
        }
    }

    /// <summary>
    /// Moves <paramref name="product"/> to the Deleted folder once it's confirmed. The form starts a new product if it was
    /// showing this one, as it was after the header's Delete, which only ever deletes the product showing.
    /// </summary>
    private Task DeleteProductAsync(Product product) =>
        RunWithConfirmationAsync(
            $"Delete '{product.Name}'? It moves to the Deleted folder at the bottom of the list, where it can be restored as it is.",
            async () =>
            {
                await _productRepository.DeleteAsync(product.Id, CancellationToken.None);
                if (SelectedProduct?.Id == product.Id || _editingId == product.Id)
                {
                    StartNewProduct();
                }

                await LoadCommand.Execute();
            });

    /// <summary>
    /// Takes <paramref name="product"/> out of the Deleted folder, back to its own, and opens it, as a click on it would,
    /// asking about the unsaved changes of the product showing first.
    /// </summary>
    private async Task RestoreProductAsync(Product product)
    {
        if (!await ResolveUnsavedChangesAsync(revert: false))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            await _productRepository.RestoreAsync(product.Id, CancellationToken.None);
            await LoadCommand.Execute();
            TrySelectProduct(product.Id);
        });
    }

    /// <summary>Deletes <paramref name="product"/>, from the Deleted folder, for good once it's confirmed.</summary>
    private Task DeletePermanentlyAsync(Product product) =>
        RunWithConfirmationAsync(
            product.IsComposite
                ? $"Delete '{product.Name}' permanently? The products it holds stay. This can't be undone."
                : $"Delete '{product.Name}' permanently? Its own sub-assemblies go with it; the ones it links stay on the Sub-Assemblies screen. This can't be undone.",
            async () =>
            {
                await _productRepository.DeletePermanentlyAsync(product.Id, CancellationToken.None);
                await LoadCommand.Execute();
            });

    /// <summary>The Last Updated figure: the day and the time; "—" for a product never saved since it was added.</summary>
    private void ShowUpdatedAt(DateTime? updatedAt) =>
        LastUpdatedText = ProductPrintout.LastUpdatedTextFor(updatedAt);

    private void LoadIntoForm(Product product)
    {
        // Not for the same product loaded again, as a Save does: the boxes still hold what's its.
        if (product.Id != _editingId)
        {
            FormVersion++;
        }

        _editingId = product.Id;
        EditingName = product.Name;
        EditingDescription = product.Description ?? string.Empty;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == product.CategoryId);
        EditingQuantityRequired = product.QuantityRequired;
        EditingIncludesComponentPackaging = product.IncludesComponentPackaging;
        EditingCurrentPrice = product.CurrentPrice;
        ShowUpdatedAt(product.UpdatedAt);
        EditingCloneName = $"{product.Name} Copy";
    }

    /// <summary>
    /// What follows a new selection: nothing picked or open. Another product's details wait for its own to
    /// arrive and go with them (AreDetailsReady); the same product picked again (a fresh copy after Save) keeps
    /// showing until its reload, and none picked clears them.
    /// </summary>
    private void ResetDetails(Product? product)
    {
        // Another product's details replace these when they arrive, in the same pass (ClearDetailsForAnother).
        // Cleared here, the screen drew an empty product in between: a flicker, with the recipe's frame
        // collapsing and growing back.
        if (product is not null && product.Id != _detailProductId)
        {
            AreDetailsReady = false;
            return;
        }

        AreDetailsReady = true;
        CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        Packaging.SelectedBulkEdit = null;

        if (product?.Id != _detailProductId)
        {
            ClearDetails();
        }
    }

    /// <summary>
    /// What ResetDetails leaves for the picked product's details to replace: the previous product's. Its recipe's tabs
    /// stay for the picked recipe's sections to take over (ReplaceUsages), unless nothing is coming to replace them.
    /// </summary>
    private void ClearDetailsForAnother(bool keepRecipeTabs)
    {
        CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        Packaging.SelectedBulkEdit = null;
        VersionHistory.Clear();
        ClearDetails(keepRecipeTabs);
    }

    /// <summary>
    /// Opens a product as picking it in the list would, for another screen (Sub-Assemblies' Dependents tiles). A search
    /// hiding it is cleared, so the list shows the row picked. A list that doesn't hold it yet (the screen not loaded
    /// since, or ever) opens it at the end of the next LoadCommand, which showing the screen runs.
    /// </summary>
    public void OpenProduct(int productId)
    {
        _productToOpen = TrySelectProduct(productId) ? null : productId;
    }

    /// <summary>
    /// Opens a product at the end of the next LoadCommand, for the one in the screen's address as the screen opens: its
    /// list is fresh only then. New clicked meanwhile is the later word, and the product isn't opened over it.
    /// </summary>
    public void OpenProductOnLoad(int productId)
    {
        _productToOpen = productId;
    }

    /// <summary>
    /// <see cref="OpenProduct"/>, asking first about the unsaved changes of the product showing when another is to replace
    /// it; a deleted one is only picked in the Deleted folder, and replaces nothing. False when the user stays on it.
    /// </summary>
    public async Task<bool> OpenProductAsync(int productId)
    {
        if (productId != SelectedProduct?.Id
            && DeletedProducts.All(p => p.Id != productId)
            && !await ResolveUnsavedChangesAsync(revert: false))
        {
            return false;
        }

        OpenProduct(productId);
        return true;
    }

    /// <summary>
    /// Starts a new product that links a standalone sub-assembly, for Sub-Assemblies' New Product. It waits for the next
    /// LoadCommand, which showing the screen runs, so the sub-assembly is found as it's saved now: it may have been made or
    /// renamed there since this screen last loaded.
    /// </summary>
    public void StartNewProductFrom(int subAssemblyId)
    {
        _productToOpen = null;
        _subAssemblyToStartFrom = subAssemblyId;
    }

    /// <summary><see cref="StartNewProductFrom"/>, asking first about the unsaved changes of the product showing. False when the user stays on it.</summary>
    public async Task<bool> StartNewProductFromAsync(int subAssemblyId)
    {
        if (!await ResolveUnsavedChangesAsync(revert: false))
        {
            return false;
        }

        StartNewProductFrom(subAssemblyId);
        return true;
    }

    /// <summary>
    /// A new product named after the sub-assembly, with the Link form open on it, as Add Sub-Assembly's Link an existing
    /// one would have it: its lines load, and the piece size is left to type. Like New, it replaces whatever the form
    /// showed and has nothing to undo; it's measured against a blank product, so Save is offered straight away.
    /// </summary>
    private void StartNewProductLinking(int subAssemblyId)
    {
        if (_linkable.FirstOrDefault(s => s.Id == subAssemblyId && s.OwnerProductId is null && s.Role?.IsPackaging != true) is not { } subAssembly)
        {
            ErrorMessage = "That sub-assembly is no longer on the Sub-Assemblies screen.";
            return;
        }

        StartNewProduct();
        EditingName = subAssembly.Name;
        InsertAddSubAssemblyTab();
        SelectRecipeTab(AddSubAssemblyTab);
        UsageForm.EditingUsageMode = UsageFormMode.Link;
        UsageForm.EditingUsageSource = UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.SubAssembly?.Id == subAssemblyId);
        History.RefreshDirty();
    }

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: opens the product the name means best.</summary>
    public bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(Products, product => product.Name, text) is not { } product)
        {
            return false;
        }

        _ = RunSafelyAsync(() => OpenProductAsync(product.Id));
        return true;
    }

    private bool TrySelectProduct(int productId)
    {
        // A deleted one (a Dependents tile names it as deleted) has its row picked in the Deleted folder, opened; the form stays.
        if (DeletedProducts.Any(p => p.Id == productId))
        {
            if (ProductTree.Find(productId) is null)
            {
                ClearListFilters();
            }

            ProductTree.DeletedFolder.IsExpanded = true;
            SelectedTreeNode = ProductTree.Find(productId);
            return true;
        }

        if (Products.FirstOrDefault(p => p.Id == productId) is not { } product)
        {
            return false;
        }

        if (!VisibleProducts.Contains(product))
        {
            ClearListFilters();
        }

        SelectedProduct = product;
        return true;
    }

    /// <summary>
    /// New, from the header or the list. A product still waiting for LoadCommand to open it, or a sub-assembly waiting
    /// for it to start a product from, no longer is: it would load over the new form, and a name typed into it since
    /// would then be saved as that product's.
    /// </summary>
    private void NewProduct(ProductStage? stage = null, int? folderId = null, bool isComposite = false)
    {
        _productToOpen = null;
        _subAssemblyToStartFrom = null;
        StartNewProduct(stage, folderId, isComposite);
    }

    /// <summary>
    /// New and New Product Here: <see cref="NewProduct"/>, and the new product's row in the list, in the folder Save would
    /// file it in, picked, with its name ready to be typed, as a new file in a file explorer. The name typed saves it
    /// (<see cref="CommitTreeNameAsync"/>). The search and the category filter are cleared first, so the row shows.
    /// </summary>
    private void NewProductInList(ProductStage? stage = null, int? folderId = null, bool isComposite = false)
    {
        CancelTreeName();
        ClearListFilters();
        NewProduct(stage, folderId, isComposite);
        var row = ProductTree.BeginNewProduct(NewProductStage, NewProductFolderId, IsEditingComposite);
        _treeNaming.Start(row, row.Product.Name);
    }

    /// <summary>
    /// Takes the new product's row out of the list (<see cref="NewProductInList"/>), and the rows picked, and stops the
    /// naming in it. The form is left as it is.
    /// </summary>
    private void EndNewProductRow() => _treeNaming.EndNewRow(ProductTree.PendingProduct, ProductTree.EndNewProduct);

    /// <summary>
    /// A new product: no name, recipe, packaging or links, its costing at the defaults, and nothing selected. Save files it
    /// in <paramref name="stage"/>, or Settings' stage for a new product when it's null, in the user's folder
    /// <paramref name="folderId"/>, or at the top of the stage for none; it has no category until one is picked.
    /// <paramref name="isComposite"/> makes it a composite, which opens on its empty Products table.
    /// </summary>
    private void StartNewProduct(ProductStage? stage = null, int? folderId = null, bool isComposite = false)
    {
        _newProductInDefaultStage = stage is null;
        NewProductStage = stage ?? _defaultNewProductStage;
        NewProductFolderId = stage is null ? null : folderId;
        SelectedProduct = null;
        FormVersion++;
        _editingId = 0;
        EditingName = string.Empty;
        EditingDescription = string.Empty;
        EditingCategory = null;
        EditingQuantityRequired = 1;
        EditingIncludesComponentPackaging = false;
        EditingCurrentPrice = null;
        ShowUpdatedAt(null);
        EditingCloneName = string.Empty;

        // After an unsaved new product nothing was selected, so the selection subscriptions don't run. The kind goes
        // first, so the tab ClearDetails picks is the new kind's.
        CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        Packaging.SelectedBulkEdit = null;
        SetComposite(isComposite);
        ClearDetails();
        RefreshComponentOptions();
        Costing.ApplyDefaults();

        // The blank form is what Save's dirty check measures against, so nothing is offered until it's filled in.
        History.MarkClean();
    }

    /// <summary>
    /// Empties the recipe, packaging, costing and links, and what Save compares them against. With
    /// <paramref name="keepRecipeTabs"/>, the recipe's sections and the Packaging tab stay for another product's to
    /// replace in the same pass: the tab strip then keeps its tabs rather than building a new one for each.
    /// </summary>
    private void ClearDetails(bool keepRecipeTabs = false)
    {
        // The history is this product's form; another product's steps would put back a recipe that isn't its.
        History.Clear();

        _detailProductId = null;

        _arrangingRecipeTabs = true;
        try
        {
            if (!keepRecipeTabs)
            {
                UsageEdits.Clear();
            }

            RecipeTabs.Remove(AddSubAssemblyTab);
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }

        Packaging.Clear();

        // A composite's lines, and what its products were read to bring, are that composite's.
        ComponentsTable.Clear();
        _savedRecipe = new RecipeDraft([], []);
        Costing.Load(null);
        RefreshLinkableOptions();
        RecomputeCosts();

        // A picked product's sub-assemblies are on their way, and pick the tab when they arrive.
        _isRecipeClearedForAnother = SelectedProduct is not null;
        // A blank form opens on its first tab, a composite's on its Products, whatever showed before.
        if (SelectedProduct is null)
        {
            ReselectRecipeTab(previous: null, previousUsage: null, previousIds: []);
        }
    }

    private async Task LoadProductDetailAsync(Product? product, CancellationToken cancellationToken)
    {
        if (product is null)
        {
            // Nothing picked: the form is a blank one, and there's nothing to save until it's typed into.
            History.MarkClean();
            return;
        }

        // Read on the thread pool, so the UI thread comes back once, when all of it has arrived, instead of
        // after each read to start the next. The reads touch no ViewModel state; everything below them does.
        // Save's reload has read it already: then this runs through without awaiting, in the pass that
        // picked the product, so the form isn't drawn closed (ResetDetails) before it opens again.
        ProductDetail detail;
        try
        {
            detail = _preloadedDetail is { } preloaded && preloaded.ProductId == product.Id
                ? preloaded.Detail
                : await Task.Run(() => _savedProducts.ReadDetailAsync(product.Id, cancellationToken), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && product.Id != _detailProductId)
        {
            // Nothing of the picked product's came: the previous product's mustn't stay showing under its name.
            ClearDetailsForAnother(keepRecipeTabs: false);
            AreDetailsReady = true;
            throw;
        }

        // Only the product picked last is shown; a newer pick cancels this load.
        cancellationToken.ThrowIfCancellationRequested();

        // Out with the previous product's details and in with these, in one pass, so nothing is drawn between them. Its
        // recipe's tabs stay, for this recipe's sections to take over.
        if (product.Id != _detailProductId)
        {
            ClearDetailsForAnother(keepRecipeTabs: true);
        }

        // Whose details these are, before they're ready: the History tab reads the versions as AreDetailsReady turns true,
        // only when they're the picked product's (RequestVersions). Set after, a product picked on that tab showed none.
        _detailProductId = product.Id;
        AreDetailsReady = true;

        // A composite's Products tab (or none) goes in before the tabs are rebuilt, and its lines before ReplaceUsages
        // settles what Save compares the recipe against, which they're part of.
        SetComposite(product.IsComposite);
        ComponentsTable.Load(detail.Components);

        // The packaging first, so the recipe's tabs, rebuilt next, can reselect the Packaging tab if it showed.
        Packaging.LoadLink(detail.PackagingLink);
        Packaging.LoadLines(detail.Lines);
        Packaging.LoadBulk(detail.BulkLines, product.BulkPackagingQuantity);
        ReplaceUsages(detail.Usages, detail.SubAssemblyLines, detail.AddIns);
        Costing.Load(detail.Costing);
        RefreshLinkableOptions();
        RefreshComponentOptions();
        RecomputeCosts();
        History.MarkClean();

        // Read and priced all the same, so it can be put right here; Save refuses it until one product is taken out of another.
        if (detail.HoldsItself)
        {
            ErrorMessage = CompositeProductRules.HoldsItselfText(product.Name);
        }
        else if (detail.PackagingLinkedRoundInALoop)
        {
            ErrorMessage = PackagingLinkRules.LinkedRoundInALoopText(product.Name);
        }
    }
}
