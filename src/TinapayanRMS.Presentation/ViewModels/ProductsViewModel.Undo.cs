using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

// What the form's undo history and the unsaved-changes question ask of this screen (RecordFormViewModel).
public sealed partial class ProductsViewModel
{
    /// <summary>Save offers itself whenever the form holds something the saved product doesn't.</summary>
    private void WireUnsavedChanges()
    {
        // Save offers itself only when the form holds something the saved product doesn't. Re-read on every
        // change rather than only when an undo step settles: a step closes on the way out of a field, and a Save
        // button still disabled under the cursor would swallow the very click that enabled it.
        this.Changed.Subscribe(_ => History.RefreshDirty());
        Costing.Changed.Subscribe(_ => History.RefreshDirty());
        Packaging.Changed.Subscribe(_ => History.RefreshDirty());
        ComponentsTable.Changed.Subscribe(_ => History.RefreshDirty());
        UsageForm.Changed.Subscribe(_ => History.RefreshDirty());
        WatchForSave(UsageEdits);
        WatchForSave(Packaging.LineEdits);
        WatchForSave(Packaging.BulkEdits);
        WatchForSave(ComponentsTable.ComponentEdits);
        WatchForSave(UsageForm.FormLines);
        WatchForSave(UsageForm.FormAddIns);
        ((INotifyPropertyChanged)RecipeTabs).PropertyChanged += (_, _) => RebaseSettledForm();
    }

    /// <summary>Nothing is recorded until a product's details have arrived, nor while the screen is being written to wholesale.</summary>
    protected override bool CanRecordUndo => AreDetailsReady && !IsBusyApplying;

    protected override string UnsavedChangesName =>
        !string.IsNullOrWhiteSpace(EditingName) ? EditingName.Trim()
            : SelectedProduct?.Name ?? (IsEditingComposite ? "the new composite product" : "the new product");

    protected override async Task SaveFormAsync() => await SaveCommand.Execute();

    /// <summary>
    /// Puts the form back as the product was last saved: Revert, and Don't Save on leaving the screen. It reads the picked
    /// product's detail again, which replaces the recipe, packaging, packs and costing wholesale, so anything staged but
    /// unsaved goes: a section added or edited in the form included, since the form has no discard of its own. A new
    /// product was never saved, so it goes back to being a blank one.
    /// </summary>
    protected override async Task RevertToSavedAsync()
    {
        if (SelectedProduct is not { } product)
        {
            // A new composite named in the list keeps its row until it's saved: put back blank, it has none.
            EndNewProductRow();
            StartNewProduct(_newProductInDefaultStage ? null : NewProductStage, _newProductCategoryId, IsEditingComposite);
            return;
        }

        History.Clear();
        CloseUsageForm();
        Packaging.SelectedLineEdit = null;
        Packaging.SelectedBulkEdit = null;
        LoadIntoForm(product);
        await LoadProductDetailAsync(product, CancellationToken.None);
    }

    /// <summary>The form as it stands: everything an undo step puts back. See ProductFormSnapshot for what's left out.</summary>
    protected override ProductFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        Description = EditingDescription,
        CurrentPrice = EditingCurrentPrice,
        QuantityRequired = EditingQuantityRequired,
        IncludesComponentPackaging = EditingIncludesComponentPackaging,
        CategoryId = EditingCategory?.Id,
        CloneName = EditingCloneName,
        Costing = Costing.Percentages,
        Usages = [.. UsageEdits],
        SelectedTab = CaptureTabChoice(),
        HasAddSubAssemblyTab = RecipeTabs.Contains(AddSubAssemblyTab),
        PackagingLink = Packaging.LinkTarget,
        IsPackagingMoving = Packaging.IsPackagingMoving,
        Lines = [.. Packaging.LineEdits.Select(CaptureLine)],
        BulkLines = [.. Packaging.BulkEdits.Select(CaptureLine)],
        BulkQuantity = Packaging.BulkQuantity,
        Components = ComponentsTable.Capture(),
        IsUsageFormOpen = UsageForm.IsUsageFormOpen,
        IsAddingUsage = UsageForm.IsAddingUsage,
        UsageFormTitle = UsageForm.UsageFormTitle,
        UsageMode = UsageForm.EditingUsageMode,
        IsFormEditingLines = UsageForm.IsFormEditingLines,
        IsFormPickingSource = UsageForm.IsFormPickingSource,
        EditingUsageIndex = UsageForm.EditingUsage is { } editing && UsageEdits.IndexOf(editing) is var i and >= 0 ? i : null,
        UsageRoleId = UsageForm.EditingUsageRole?.Id,
        UsageName = UsageForm.EditingUsageName,
        UsageSourceKey = UsageForm.EditingUsageSource?.Key,
        FormLines = [.. UsageForm.FormLines.Select(CaptureIngredientLine)],
        FormAddIns = [.. UsageForm.FormAddIns.Select(CaptureIngredientLine)],
        FormFormulaBase = UsageForm.FormFormulaBase,
        UsageAuthoredField = UsageForm.AuthoredField,
        UsageWeightPerPiece = UsageForm.EditingUsageWeightPerPiece,
        UsageYield = UsageForm.EditingUsageYield,
    };

    private static IngredientLineState CaptureIngredientLine(IngredientLineRow row) =>
        new(row.IngredientId, row.Grams, row.IsFormulaBase, row.AuthoredBakersPercent);

    private static PackagingLineState CaptureLine(PackagingLineRow row) =>
        new(row.PackagingMaterialId, row.UnitId, row.QtyPerUnit);

    private RecipeTabChoice? CaptureTabChoice() => SelectedRecipeTab switch
    {
        SubAssemblyRecipeTab tab => new RecipeTabChoice.Usage(RecipeTabs.OfType<SubAssemblyRecipeTab>().ToList().IndexOf(tab)),
        AddSubAssemblyRecipeTab => new RecipeTabChoice.AddSubAssembly(),
        PackagingRecipeTab => new RecipeTabChoice.Packaging(),
        ComponentsRecipeTab => new RecipeTabChoice.Components(),
        null => null,
    };

    /// <summary>
    /// Puts the whole form back as <paramref name="snapshot"/> had it. Order matters throughout, and is explained
    /// step by step below. Every figure the writes would work out again waits for the end (_updates' batch), so the
    /// figures are worked out once from the restored form rather than over and over from a half-restored one.
    /// </summary>
    /// <param name="settledThen">What Save measured against when <paramref name="snapshot"/> was taken, or null.</param>
    protected override void RestoreSnapshot(ProductFormSnapshot snapshot, ProductFormSnapshot? settledThen)
    {
        // A message about a form that no longer exists would outlive what it was about.
        ErrorMessage = null;

        ApplyRestore(() => _updates.Batch(() =>
        {
            // 1. The form goes first. EditingUsage has to be null before the sections are replaced: the tab mirror
            //    refills FormLines from a section that replaces the one being edited, which would undo this restore.
            CloseUsageForm();
            UsageForm.SelectedFormLine = null;
            UsageForm.SelectedFormAddIn = null;
            Packaging.SelectedLineEdit = null;
            Packaging.SelectedBulkEdit = null;
            ComponentsTable.SelectedComponentEdit = null;

            // 2. The product's own fields. The category goes back by id, as the reloaded list's own copy.
            _editingId = snapshot.EditingId;
            EditingName = snapshot.Name;
            EditingDescription = snapshot.Description;
            EditingCurrentPrice = snapshot.CurrentPrice;
            EditingQuantityRequired = snapshot.QuantityRequired;
            EditingIncludesComponentPackaging = snapshot.IncludesComponentPackaging;
            EditingCloneName = snapshot.CloneName;
            _editingCategory = snapshot.CategoryId is { } categoryId ? Categories.FirstOrDefault(c => c.Id == categoryId) : null;
            this.RaisePropertyChanged(nameof(EditingCategory));

            // 3. Costing.
            Costing.Percentages = snapshot.Costing;

            // 4. Packaging. The link as saved is deliberately left alone: it's what Save compares against. Showing the
            //    link ends a Move to Sub-Assemblies, so the move goes back after it.
            Packaging.ShowLink(snapshot.PackagingLink);
            Packaging.IsPackagingMoving = snapshot.IsPackagingMoving;
            Packaging.LineEdits.Clear();
            foreach (var line in snapshot.Lines)
            {
                Packaging.LineEdits.Add(RestoreLine(line));
            }

            Packaging.BulkEdits.Clear();
            foreach (var line in snapshot.BulkLines)
            {
                Packaging.BulkEdits.Add(RestoreLine(line));
            }

            Packaging.BulkQuantity = snapshot.BulkQuantity;

            // A composite's lines, each finding its product and the recipe cost read for it again (a cost not read yet is
            // read now, and prices the line when it arrives).
            ComponentsTable.Restore(snapshot.Components);

            // 5. The sections and their tabs, under _arrangingRecipeTabs so RecipeTabShown short-circuits: without
            //    it, moving the tab back would apply the form the tab is being moved off, which is the very change
            //    being undone.
            _arrangingRecipeTabs = true;
            try
            {
                ReplaceUsageItems(snapshot.Usages);

                if (snapshot.HasAddSubAssemblyTab)
                {
                    InsertAddSubAssemblyTab();
                }
                else
                {
                    RecipeTabs.Remove(AddSubAssemblyTab);
                }

                SelectedRecipeTab = snapshot.SelectedTab switch
                {
                    RecipeTabChoice.Usage(var index) => RecipeTabs.OfType<SubAssemblyRecipeTab>().ElementAtOrDefault(index),
                    RecipeTabChoice.AddSubAssembly => RecipeTabs.Contains(AddSubAssemblyTab) ? AddSubAssemblyTab : null,
                    RecipeTabChoice.Packaging => PackagingTab,
                    RecipeTabChoice.Components => ComponentsTabIfShown,
                    null => null,
                };
            }
            finally
            {
                _arrangingRecipeTabs = false;
            }

            // RecipeTabShown never ran, and it's the only other thing that writes these. Left stale, the next real
            // change of tab would think it was leaving a tab that's gone, and apply the wrong form on the way out.
            _shownRecipeTab = SelectedRecipeTab;
            if (SelectedRecipeTab is not null and not AddSubAssemblyRecipeTab)
            {
                _lastRecipeContentTab = SelectedRecipeTab;
            }

            // 6. The form itself, last. The option lists are rebuilt before the pickers are put back, as a load does,
            //    and RefreshLinkableOptions runs inside the guard: outside it, the source it can null would start a
            //    load that clears the very lines being restored.
            UsageForm.IsAddingUsage = snapshot.IsAddingUsage;
            UsageForm.UsageFormTitle = snapshot.UsageFormTitle;
            UsageForm.RestoreMode(snapshot.UsageMode);
            UsageForm.IsFormEditingLines = snapshot.IsFormEditingLines;
            UsageForm.IsFormPickingSource = snapshot.IsFormPickingSource;

            RefreshLinkableOptions();
            RefreshFormRoleOptions();

            UsageForm.RestorePicks(
                snapshot.UsageRoleId is { } roleId ? UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == roleId) : null,
                snapshot.UsageSourceKey is { } sourceKey ? UsageForm.LinkableSubAssemblies.FirstOrDefault(o => o.Key == sourceKey) : null,
                snapshot.UsageName);

            UsageForm.FormFormulaBase = snapshot.FormFormulaBase;
            RestoreIngredientLines(UsageForm.FormLines, snapshot.FormLines, snapshot.FormFormulaBase);
            RestoreIngredientLines(UsageForm.FormAddIns, snapshot.FormAddIns, snapshot.FormFormulaBase);
            UsageForm.RestorePieceSize(snapshot.UsageAuthoredField, snapshot.UsageWeightPerPiece, snapshot.UsageYield);

            UsageForm.EditingUsage = snapshot.EditingUsageIndex is { } usageIndex && usageIndex < UsageEdits.Count
                ? UsageEdits[usageIndex]
                : null;
            UsageForm.IsUsageFormOpen = snapshot.IsUsageFormOpen;
        }));

        // 8. One pass over the restored form, after the batch. FormLinesChanged is the screen's own "the lines
        //    changed, bring everything into line" entry point: it raises what the form's tables show, works the
        //    derived piece-size figure out again, and runs the single ApplyFormulaBase pass that gives every row
        //    back its BaseWeight and BatchWeight.
        UsageForm.RaiseKindChanged();
        FormLinesChanged();
        RecomputeCosts();

        // 9. What Save measures the form against goes back to where it was when the step was taken, for the form and
        //    tabs only (WithFormOf): the step being undone may have opened another section's form (Make a Copy, Unlink),
        //    which moved it there (RebaseSettledForm), and a restore doesn't rebase, since the form it puts back may
        //    hold typing that isn't in the recipe yet. Taking the settled form of that moment instead keeps both
        //    right: a form put back as it was saved is clean again, and typing put back still counts. The recipe,
        //    costing and the rest stay as settled now, so a costing-defaults rebase since then isn't lost.
        if (settledThen is not null)
        {
            History.Rebase((settled, _) => settled.WithFormOf(settledThen));
        }
    }

    private void RestoreIngredientLines(
        ObservableCollection<IngredientLineRow> rows, IReadOnlyList<IngredientLineState> states, decimal baseWeight)
    {
        rows.Clear();
        foreach (var state in states)
        {
            rows.Add(IngredientLineRow.Restore(
                id => Ingredients.FirstOrDefault(m => m.Id == id),
                baseWeight,
                state.IngredientId,
                state.Grams,
                state.IsFormulaBase,
                state.AuthoredBakersPercent));
        }
    }

    // The material goes on first: it brings the units the row can be in, and a key it doesn't offer falls back
    // to the base unit, which would drop a restored unit set the other way round.
    private PackagingLineRow RestoreLine(PackagingLineState state) =>
        new(FindPackagingMaterial)
        {
            PackagingMaterialId = state.PackagingMaterialId,
            UnitKey = state.UnitId ?? PackagingUnitChoice.BaseUnitKey,
            QtyPerUnit = state.QtyPerUnit,
        };

    /// <summary>
    /// Makes UsageEdits hold <paramref name="sections"/>, section for section and in place: each tab stays and shows its
    /// new section (MirrorUsageTabs), so the tab strip keeps the tabs it has and only adds or takes away the difference,
    /// rather than building a new TabItemExt, template and all, for every section of every product picked. Never Clear(),
    /// which the mirror reads as every tab going. Call it with _arrangingRecipeTabs set, so the strip's own pick as a tab
    /// goes is ignored. It leaves _savedRecipe alone, which is why an undo can use it and ReplaceUsages can't be reused.
    /// </summary>
    private void ReplaceUsageItems(IReadOnlyList<ProductUsageEdit> sections)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (i < UsageEdits.Count)
            {
                UsageEdits[i] = sections[i];
            }
            else
            {
                UsageEdits.Add(sections[i]);
            }
        }

        while (UsageEdits.Count > sections.Count)
        {
            UsageEdits.RemoveAt(UsageEdits.Count - 1);
        }
    }

    /// <summary>A saved usage as its section, with the screen's own copies of its role and ingredients (ProductUsageEdit.FromSaved).</summary>
    private ProductUsageEdit ToUsageEdit(
        ProductSubAssemblyUsage usage,
        IEnumerable<SubAssemblyLine> lines,
        IEnumerable<UsageAddInLine> addIns,
        IReadOnlyDictionary<int, RawMaterial> ingredientsById)
    {
        var subAssembly = usage.SubAssembly!;
        var role = Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
        return ProductUsageEdit.FromSaved(usage, lines, addIns, role, EditingName, ingredientsById);
    }

    /// <summary>
    /// What a linked sub-assembly goes by beside its role in this product's recipe: its name, less the product's own and
    /// the role it was saved under from here ("Ube" for "Cheese Roll Filling: Ube"), as SetRecipeAsync reads it.
    /// </summary>
    private string? SectionLabelOf(string subAssemblyName, SubAssemblyRole? role) =>
        SubAssemblyRules.SectionLabel(subAssemblyName, EditingName, role?.Name ?? string.Empty);
}
