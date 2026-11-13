using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using DynamicData;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

// The recipe's tabs and sections, a composite's products, and the packaging.
public sealed partial class ProductsViewModel
{
    /// <summary>The recipe's tabs follow its sections as they're added, taken out and moved.</summary>
    private void WireRecipeTabLists()
    {
        // The Packaging tab is always there, last: the packaging per piece is typed, linked and copied on the tab itself,
        // and the bulk packaging typed there, never through Add Sub-Assembly, which is for recipe sections.
        RecipeTabs.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasRecipeSections));
        RecipeTabs.Add(PackagingTab);

        // The blank form shows it from the start, so its tables are there to type into before anything else happens.
        SelectedRecipeTab = PackagingTab;
        UsageEdits.CollectionChanged += (_, e) =>
        {
            MirrorUsageTabs(e);
            MarkRepeatedRoles();
        };
    }

    /// <summary>
    /// Which of the recipe's tables shows: the section form, the packaging, or a composite's products.
    /// </summary>
    [MemberNotNull(nameof(_isUsageFormShowing), nameof(_isPackagingShowing), nameof(_isComponentsShowing))]
    private void WireRecipeTabFlags()
    {
        _isUsageFormShowing = this.WhenAnyValue(
                x => x.SelectedRecipeTab,
                x => x.UsageForm.IsUsageFormOpen,
                (tab, isFormOpen) => tab is SubAssemblyRecipeTab || (tab is AddSubAssemblyRecipeTab && isFormOpen))
            .ToProperty(this, x => x.IsUsageFormShowing);

        _isPackagingShowing = this.WhenAnyValue(x => x.SelectedRecipeTab)
            .Select(tab => tab is PackagingRecipeTab)
            .ToProperty(this, x => x.IsPackagingShowing);

        _isComponentsShowing = this.WhenAnyValue(x => x.SelectedRecipeTab)
            .Select(tab => tab is ComponentsRecipeTab)
            .ToProperty(this, x => x.IsComponentsShowing);
    }

    /// <summary>A recipe tab picked: leaving one applies its form, and showing one opens its form.</summary>
    private void WireRecipeTabPicks()
    {
        this.WhenAnyValue(x => x.SelectedRecipeTab)
            .Subscribe(RecipeTabPicked);
    }

    /// <summary>The recipe's commands: its sections', a composite's products' and its packaging's.</summary>
    [MemberNotNull(
        nameof(RemoveComponentCommand), nameof(OpenSubAssemblyCommand), nameof(OpenLinkedSourceCommand),
        nameof(AddUsageCommand), nameof(EditUsageCommand), nameof(MakeCopyCommand), nameof(UnlinkCommand),
        nameof(MoveToSubAssembliesCommand), nameof(MoveToInHouseCommand), nameof(RemoveUsageCommand),
        nameof(CopyPackagingCommand), nameof(MovePackagingToSubAssembliesCommand), nameof(RemoveLineCommand),
        nameof(RemoveBulkLineCommand), nameof(StartLinkingPackagingCommand), nameof(StartCopyingPackagingCommand),
        nameof(CancelPickingPackagingCommand), nameof(RemovePackagingLinkCommand))]
    private void WireRecipeCommands()
    {
        RemoveComponentCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => ComponentsTable.ComponentEdits.Remove(ComponentsTable.SelectedComponentEdit!)),
            ComponentsTable.WhenAnyValue(c => c.SelectedComponentEdit).Select(row => row is not null));

        OpenSubAssemblyCommand = ReactiveCommand.Create<int>(_subAssemblyOpenRequests.OnNext);
        OpenLinkedSourceCommand = ReactiveCommand.Create<ProductUsageEdit>(usage =>
        {
            if (usage.LinkedOwner is { } owner)
            {
                OpenProduct(owner.Id);
            }
            else
            {
                _subAssemblyOpenRequests.OnNext(usage.SubAssemblyId);
            }
        });

        // Add Sub-Assembly opens a New Sub-Assembly tab, after the sub-assemblies' and before Packaging's, with a fresh
        // add form in it, for a recipe section: packaging is typed, linked or copied on the Packaging tab itself. The form
        // showing is applied first, as leaving its tab would apply it; one a rule refuses keeps its tab, with the
        // message, and nothing opens. The tab goes again once another shows (RecipeTabShown).
        //
        // Its parameter is the way in, one per item on the drop-down. The tab is shown first and the mode set after:
        // showing it starts the form in New (RecipeTabShown), and the mode landing on it starts the form again for a
        // Link or a Copy (the EditingUsageMode subscription), so one path serves all three, in one undo step.
        //
        // The button stays live on the add tab, since the drop-down is the only place the way in can be changed now
        // that the form has no picker of its own. An item picked over an add form nothing was entered in only changes
        // its way, keeping the tab; over one already typed into it does what a second Add Sub-Assembly always did —
        // applies what's there and starts another, in the way picked.
        AddUsageCommand = ReactiveCommand.Create<UsageFormMode>(
            mode =>
            {
                if (SelectedRecipeTab is AddSubAssemblyRecipeTab && IsUsageFormUntouched())
                {
                    AsUndoStep(() => UsageForm.EditingUsageMode = mode);
                    return;
                }

                if (!CommitUsageFormOnLeave())
                {
                    return;
                }

                AsUndoStep(() =>
                {
                    InsertAddSubAssemblyTab();
                    SelectRecipeTab(AddSubAssemblyTab);
                    UsageForm.EditingUsageMode = mode;
                });
            });

        // A sub-assembly's tab is its form, so editing one is just showing its tab (RecipeTabShown opens the form).
        EditUsageCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => SelectRecipeTab(TabFor(usage)));

        // A linked sub-assembly becomes a new section in the same role and at the same piece size, whose lines the form
        // edits; Save makes it the product's own in a product-owned role, and otherwise keeps it on the Sub-Assemblies
        // screen as a sub-assembly of its own, and links that instead.
        MakeCopyCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => AsUndoStep(() => ReplaceUsage(usage, CopyOfLinked(usage, isOwn: false))));

        // The same copy, but the product's own whatever its role: Save keeps it with the product, off the
        // Sub-Assemblies screen, and the one it was linked to stays there as it is.
        UnlinkCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => AsUndoStep(() => ReplaceUsage(usage, CopyOfLinked(usage, isOwn: true))));

        // Anything typed into its form goes in first, so what moves is what shows; a form a rule refuses stays as it
        // is, with the message. It's still edited here until Save, which moves it and links it.
        MoveToSubAssembliesCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => AsUndoStep(() =>
        {
            var wasEditing = ReferenceEquals(usage, UsageForm.EditingUsage);
            var applied = usage;
            if (wasEditing)
            {
                try
                {
                    ErrorMessage = null;
                    applied = ApplyUsageForm();
                }
                catch (BusinessRuleException ex)
                {
                    ErrorMessage = ex.Message;
                    return;
                }
            }

            var moving = applied.Released();
            ReplaceInRecipeOrder(applied, moving);
            RecomputeCosts();
            if (wasEditing)
            {
                OpenUsageForm(moving);
            }
        }));

        MoveToInHouseCommand = ReactiveCommand.CreateFromTask<ProductUsageEdit>(usage => RunSafelyAsync(() => MoveToInHouseAsync(usage)));

        // Delete Sub-Assembly takes out the one whose tab is showing. Nothing to confirm: it only leaves the recipe,
        // with Save, and the sub-assembly stays on the Sub-Assemblies screen. The Packaging tab isn't a sub-assembly: its
        // lines are taken out with Remove Line, and a link with Remove Link.
        RemoveUsageCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(() =>
            {
                var usage = ((SubAssemblyRecipeTab)SelectedRecipeTab!).Usage;
                AsUndoStep(() =>
                {
                    // Whatever was typed into its form goes with it, rather than being applied on the way out.
                    if (ReferenceEquals(usage, UsageForm.EditingUsage))
                    {
                        CloseUsageForm();
                    }

                    // Its neighbour's tab shows instead: the next sub-assembly's, else the one before, else a composite's
                    // Products, else Packaging.
                    var index = UsageEdits.IndexOf(usage);
                    var neighbour = index + 1 < UsageEdits.Count ? UsageEdits[index + 1] : index > 0 ? UsageEdits[index - 1] : null;
                    SelectRecipeTab(neighbour is null ? ComponentsTabIfShown ?? PackagingTab : TabFor(neighbour));

                    UsageEdits.Remove(usage);
                    RefreshLinkableOptions();
                    RecomputeCosts();
                });
            }),
            this.WhenAnyValue(x => x.SelectedRecipeTab).Select(tab => tab is SubAssemblyRecipeTab));

        // The linked packaging's lines, as they show, become the product's own; Save drops the link and writes them.
        CopyPackagingCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => Packaging.ShowLink(null)),
            Packaging.WhenAnyValue(p => p.IsPackagingLinked));

        // The link and the lines it shows go, leaving none for each piece; Save drops the link. The bulk packaging stays.
        RemovePackagingLinkCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                Packaging.SelectedLineEdit = null;
                Packaging.LineEdits.Clear();
                Packaging.ShowLink(null);
                RecomputeCosts();
            }),
            Packaging.WhenAnyValue(p => p.IsPackagingLinked));

        // Link and Copy open the Packaging tab's picker; picking in it does the rest (ApplyPackagingSourceAsync). Opening
        // or closing it changes nothing Save writes, so neither is an undo step.
        StartLinkingPackagingCommand = ReactiveCommand.Create(StartLinkingPackaging);
        StartCopyingPackagingCommand = ReactiveCommand.Create(StartCopyingPackaging);
        CancelPickingPackagingCommand = ReactiveCommand.Create(Packaging.EndPickingSource);
        LoadLatest(
            Packaging.WhenAnyValue(p => p.PickedSource).Where(source => source is not null && Packaging.IsPickingSource).Select(source => source!),
            ApplyPackagingSourceAsync);

        // The product's own packaging lines go to the Sub-Assemblies screen as a packaging set on Save, which it links.
        // They're typed straight into the tab rather than through a form, so they're checked now instead: a line half
        // filled in, or none at all, is refused with the message, as a recipe section's form a rule refuses is. They're
        // still typed into until Save.
        MovePackagingToSubAssembliesCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                ErrorMessage = null;
                int lineCount;
                try
                {
                    lineCount = Packaging.CurrentLines().Count;
                }
                catch (BusinessRuleException ex)
                {
                    ErrorMessage = ex.Message;
                    return;
                }

                if (lineCount == 0)
                {
                    ErrorMessage = "Add the packaging materials one piece takes first: a packaging set needs at least one.";
                    return;
                }

                Packaging.IsPackagingMoving = true;
            }),
            Packaging.WhenAnyValue(p => p.CanMovePackaging));

        RemoveLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => Packaging.LineEdits.Remove(Packaging.SelectedLineEdit!)),
            Packaging.WhenAnyValue(p => p.SelectedLineEdit, p => p.IsPackagingLinked, (line, linked) => line is not null && !linked));

        RemoveBulkLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => Packaging.BulkEdits.Remove(Packaging.SelectedBulkEdit!)),
            Packaging.WhenAnyValue(p => p.SelectedBulkEdit).Select(line => line is not null));
    }

    /// <summary>
    /// A linked section as a new one whose lines the form edits, at the same role and piece size (Make a Copy, Unlink):
    /// <paramref name="isOwn"/> makes it the product's own whatever its role.
    /// </summary>
    private static ProductUsageEdit CopyOfLinked(ProductUsageEdit usage, bool isOwn) =>
        // Its add-ins become plain lines of the copy: the batch it was showing is the batch it keeps.
        // Never a duplicate ingredient, since an add-in can't be one the recipe already has.
        // In a role the recipe has more than one in, it keeps what it went by, so it's still told apart.
        new(0, usage.Role, [.. usage.Lines, .. usage.AddIns], usage.AuthoredField, usage.AuthoredValue,
            linkedName: null, name: usage.IsRoleRepeated ? usage.Label : null, isOwn: isOwn);

    /// <summary>
    /// Makes the form a composite's, with its Products tab first, before its sections' tabs, or a product of
    /// sub-assemblies', without it. The tab strip's own pick as the tab goes is ignored.
    /// </summary>
    private void SetComposite(bool isComposite)
    {
        IsEditingComposite = isComposite;
        if (isComposite == RecipeTabs.Contains(ComponentsTab))
        {
            return;
        }

        _arrangingRecipeTabs = true;
        try
        {
            if (isComposite)
            {
                RecipeTabs.Insert(0, ComponentsTab);
            }
            else
            {
                RecipeTabs.Remove(ComponentsTab);
            }
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }
    }

    /// <summary>A composite's Products tab, or null for a product made of sub-assemblies: the tab to fall back on before Packaging.</summary>
    private RecipeTab? ComponentsTabIfShown => RecipeTabs.Contains(ComponentsTab) ? ComponentsTab : null;

    /// <summary>Where the sections' tabs start in RecipeTabs: after a composite's Products tab, which comes first.</summary>
    private int SectionTabOffset => RecipeTabs.Count > 0 && RecipeTabs[0] == ComponentsTab ? 1 : 0;

    /// <summary>New Sub-Assembly's tab, after a composite's Products and the sections', before Packaging; nothing if it's there.</summary>
    private void InsertAddSubAssemblyTab()
    {
        if (!RecipeTabs.Contains(AddSubAssemblyTab))
        {
            RecipeTabs.Insert(SectionTabOffset + UsageEdits.Count, AddSubAssemblyTab);
        }
    }

    /// <summary>What the Products table's product cell offers, for the product showing (a new one can hold any).</summary>
    private void RefreshComponentOptions() => ComponentsTable.RefreshOptions(_detailProductId ?? 0, Products);

    /// <summary>
    /// Link on the Packaging tab: the picker offers the Sub-Assemblies screen's packaging sets, less the one linked now.
    /// With none, it says how to make one. Another product's packaging is only copied, never linked.
    /// </summary>
    private void StartLinkingPackaging()
    {
        var linkedKey = Packaging.LinkTarget?.Key;
        List<LinkableSubAssemblyOption> options = [.. _packagingSets.Select(s => new LinkableSubAssemblyOption(s)).Where(o => o.Key != linkedKey)];
        if (options.Count == 0)
        {
            ErrorMessage = _packagingSets.Count == 0
                ? "There's no packaging set to link yet. Make one on the Sub-Assemblies screen, or type the lines here and press Move to Sub-Assemblies."
                : "This product links the only packaging set there is.";
            return;
        }

        ErrorMessage = null;
        Packaging.StartPickingSource(PackagingSourceMode.Link, options);
    }

    /// <summary>Copy on the Packaging tab: the picker offers the packaging sets, then every other product's packaging.</summary>
    private void StartCopyingPackaging()
    {
        ErrorMessage = null;
        Packaging.StartPickingSource(
            PackagingSourceMode.Copy,
            [
                .. _packagingSets.Select(s => new LinkableSubAssemblyOption(s)),
                .. Products.Where(p => p.Id != _detailProductId).Select(p => new LinkableSubAssemblyOption(p)),
            ]);
    }

    /// <summary>
    /// The packaging picked on the Packaging tab, once its lines are in: a set's lines, or another product's packaging as
    /// it shows (through any link of its own). Linked, they show read-only and follow the set; copied, they're this
    /// product's own to type into. Either takes the place of the packaging per piece there was, as one undo step, and Save
    /// writes it; the bulk packaging stays. Packaging with no lines is refused, and the picker stays open for another.
    /// </summary>
    private async Task ApplyPackagingSourceAsync(LinkableSubAssemblyOption source, CancellationToken cancellationToken)
    {
        var target = source.PackagingTarget!;
        IReadOnlyList<PackagingLineRow> rows = target switch
        {
            PackagingLinkTarget.Set(var setId, _) => [.. (await _subAssemblyRepository.GetPackagingLinesAsync(setId, cancellationToken))
                .Select(l => new PackagingLineRow(FindPackagingMaterial(l.PackagingMaterialId) ?? l.PackagingMaterial!, l.QtyPerSet, l.UnitId))],
            PackagingLinkTarget.OtherProduct(var productId, _) =>
                [.. (await _packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken)).Select(PackagingLineRow.FromSaved)],
        };
        cancellationToken.ThrowIfCancellationRequested();

        // The picker moved on while the lines loaded (cancelled, another pick, or another product): they're stale.
        if (!ReferenceEquals(Packaging.PickedSource, source))
        {
            return;
        }

        var linking = Packaging.SourceMode == PackagingSourceMode.Link;
        if (rows.Count == 0)
        {
            ErrorMessage = $"{target.Name} has no packaging for each piece to {(linking ? "link" : "copy")}. Pick another.";
            return;
        }

        ErrorMessage = null;
        AsUndoStep(() =>
        {
            Packaging.EndPickingSource();
            Packaging.ShowLines(rows);
            Packaging.ShowLink(linking ? target : null);
            RecomputeCosts();
        });
    }

    /// <summary>The saved usages as sections, in recipe order; they're also what Save compares the recipe against.</summary>
    private void ReplaceUsages(
        IReadOnlyList<ProductSubAssemblyUsage> usages, IReadOnlyList<SubAssemblyLine> subAssemblyLines, IReadOnlyList<UsageAddInLine> addIns)
    {
        var linesBySubAssembly = subAssemblyLines.ToLookup(l => l.SubAssemblyId);

        // Add-ins key on the usage, not the sub-assembly: they're this product's own, so two products
        // linking the same one have different sets.
        var addInsByUsage = addIns.ToLookup(a => a.UsageId);
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        List<ProductUsageEdit> sections =
        [
            .. usages
                .Select(u => ToUsageEdit(u, linesBySubAssembly[u.SubAssemblyId], addInsByUsage[u.Id], ingredientsById))
                .Order(ProductUsageEdit.RecipeOrder),
        ];

        // Another product's recipe opens on its first tab; the same product's, reloaded, keeps the tab it showed. Its
        // section is read now, before the tabs take on the new sections.
        var previousTab = _isRecipeClearedForAnother ? null : SelectedRecipeTab;
        var previousUsage = (previousTab as SubAssemblyRecipeTab)?.Usage;
        HashSet<int> previousIds = [.. UsageEdits.Select(u => u.SubAssemblyId)];
        _isRecipeClearedForAnother = false;

        _arrangingRecipeTabs = true;
        try
        {
            ReplaceUsageItems(sections);
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }

        _savedRecipe = CurrentRecipe();
        ReselectRecipeTab(previousTab, previousUsage, previousIds);
    }

    /// <summary>
    /// Keeps RecipeTabs' sub-assembly tabs in step with UsageEdits, index for index after a composite's Products tab
    /// (SectionTabOffset). A replaced section keeps
    /// its tab, and the form follows it when it was the one being edited (a refresh swaps in a copy with new
    /// costs), so neither a refresh nor an Apply moves the tab showing or loses the form's place.
    /// </summary>
    private void MirrorUsageTabs(NotifyCollectionChangedEventArgs e)
    {
        var offset = SectionTabOffset;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (var i = 0; i < e.NewItems!.Count; i++)
                {
                    RecipeTabs.Insert(offset + e.NewStartingIndex + i, new SubAssemblyRecipeTab((ProductUsageEdit)e.NewItems[i]!));
                }

                break;

            case NotifyCollectionChangedAction.Remove:
                for (var i = 0; i < e.OldItems!.Count; i++)
                {
                    RecipeTabs.RemoveAt(offset + e.OldStartingIndex);
                }

                break;

            case NotifyCollectionChangedAction.Replace:
            {
                var replacement = (ProductUsageEdit)e.NewItems![0]!;
                var previous = (ProductUsageEdit)e.OldItems![0]!;
                ((SubAssemblyRecipeTab)RecipeTabs[offset + e.NewStartingIndex]).Usage = replacement;
                if (ReferenceEquals(previous, UsageForm.EditingUsage))
                {
                    if (replacement.IsEditable != previous.IsEditable)
                    {
                        // Its role was made product-owned in Settings, or no longer is: the form opens on it again, its
                        // lines now the product's to type into, or the Sub-Assemblies screen's to show.
                        OpenUsageForm(replacement);
                    }
                    else
                    {
                        UsageForm.EditingUsage = replacement;

                        // A linked one's lines as they now are, from the Sub-Assemblies screen or changed here and applied;
                        // lines typed into the form and not applied yet stay, repriced by RefreshFormLines.
                        if (replacement.IsLinked && IsFormShowing(previous.Lines))
                        {
                            ShowFormLines(replacement.Lines);
                        }
                    }
                }

                break;
            }

            case NotifyCollectionChangedAction.Move:
                RecipeTabs.Move(offset + e.OldStartingIndex, offset + e.NewStartingIndex);
                break;

            default:
                while (RecipeTabs.Count > offset && RecipeTabs[offset] is SubAssemblyRecipeTab)
                {
                    RecipeTabs.RemoveAt(offset);
                }

                break;
        }
    }

    private SubAssemblyRecipeTab TabFor(ProductUsageEdit usage) =>
        RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => ReferenceEquals(t.Usage, usage));

    /// <summary>
    /// Shows <paramref name="tab"/>, and does what showing it does even when it was already showing. The second
    /// RecipeTabShown comes once the tab strip has moved to the tab too, so it's the one that can take New
    /// Sub-Assembly's tab away.
    /// </summary>
    private void SelectRecipeTab(RecipeTab? tab)
    {
        _selectingRecipeTab = true;
        try
        {
            SelectedRecipeTab = tab;
        }
        finally
        {
            _selectingRecipeTab = false;
        }

        RecipeTabShown(tab);
    }

    /// <summary>
    /// A tab was picked in the strip, or SelectedRecipeTab set. A pick the screen makes itself (SelectRecipeTab,
    /// or the tabs rearranged) belongs to whatever step it's part of. The user's own runs between steps: the WPF app
    /// this screen began in selected a tab as it took the cursor, before the field it took it from heard so (TabItem.OnPreviewGotKeyboardFocus),
    /// so that field's step is still open while the form changes over to the other tab's section, and closed after,
    /// it would record the change of section as an edit. So the step closes on the tab being left, keeping what
    /// was typed there, and another opens on the tab arrived at, for the field to close.
    /// </summary>
    private void RecipeTabPicked(RecipeTab? tab)
    {
        if (_selectingRecipeTab || _arrangingRecipeTabs || _revertingRecipeTab || IsBusyApplying || !History.IsStepOpen)
        {
            RecipeTabShown(tab);
            return;
        }

        History.End();
        try
        {
            RecipeTabShown(tab);
        }
        finally
        {
            History.Begin();
        }
    }

    /// <summary>
    /// A tab was picked, by the user or the screen. Leaving one applies the form it was showing, so nothing typed
    /// is lost; a rule refusing it keeps the tab it was on, with the message shown. A sub-assembly's tab then
    /// opens its form, which is all it shows, and New Sub-Assembly opens a form to add one (a form already adding
    /// one is left as it is). So a form still open is always one Save should apply. New Sub-Assembly's tab only
    /// lasts while it shows: once another does, its add has been applied or had nothing in it, and it goes.
    /// </summary>
    private void RecipeTabShown(RecipeTab? tab)
    {
        if (_arrangingRecipeTabs || _revertingRecipeTab)
        {
            return;
        }

        if (!ReferenceEquals(_shownRecipeTab, tab) && !CommitUsageFormOnLeave())
        {
            _revertingRecipeTab = true;
            try
            {
                SelectedRecipeTab = _shownRecipeTab;
            }
            finally
            {
                _revertingRecipeTab = false;
            }

            return;
        }

        _shownRecipeTab = tab;

        switch (tab)
        {
            case SubAssemblyRecipeTab subAssembly:
                _lastRecipeContentTab = tab;
                if (!ReferenceEquals(UsageForm.EditingUsage, subAssembly.Usage))
                {
                    OpenUsageForm(subAssembly.Usage);
                }

                break;

            case AddSubAssemblyRecipeTab:
                if (!UsageForm.IsUsageFormOpen || !UsageForm.IsAddingUsage)
                {
                    StartAddingUsage();
                }

                break;

            case PackagingRecipeTab or ComponentsRecipeTab:
                _lastRecipeContentTab = tab;
                break;

            case null:
                break;
        }

        // Not while SelectRecipeTab's pick is still reaching the tab strip, which would still be showing it.
        if (tab is not AddSubAssemblyRecipeTab && !_selectingRecipeTab)
        {
            RemoveAddSubAssemblyTab();
        }
    }

    /// <summary>Takes New Sub-Assembly's tab out of the strip, if it's there, with the strip's own pick ignored.</summary>
    private void RemoveAddSubAssemblyTab()
    {
        if (!RecipeTabs.Contains(AddSubAssemblyTab))
        {
            return;
        }

        _arrangingRecipeTabs = true;
        try
        {
            RecipeTabs.Remove(AddSubAssemblyTab);
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }
    }

    /// <summary>
    /// After the sections were rebuilt (a product loaded, reloaded after Save, or cleared for a new one): the
    /// tab that showed if it's still there (the same sub-assembly's, a composite's Products, or Packaging), else the first
    /// tab: a composite's Products, else the first sub-assembly's, which is the first in role order, else Packaging. Another product's
    /// recipe has no tab that showed (<paramref name="previous"/> is null), so it opens on its first.
    /// </summary>
    /// <param name="previousIds">The sub-assemblies the recipe used before, which a new section saved since isn't among.</param>
    private void ReselectRecipeTab(RecipeTab? previous, ProductUsageEdit? previousUsage, HashSet<int> previousIds)
    {
        RecipeTab? tab = previous switch
        {
            PackagingRecipeTab => PackagingTab,
            ComponentsRecipeTab => ComponentsTabIfShown,
            _ => null,
        };
        if (previousUsage is not null)
        {
            var tabs = RecipeTabs.OfType<SubAssemblyRecipeTab>();

            // A linked sub-assembly is known by its id. A new section saved since is linked now, under an id the recipe
            // didn't have, in its role, and by the name it went by when it can be.
            tab = previousUsage.SubAssemblyId > 0
                ? tabs.FirstOrDefault(t => t.Usage.SubAssemblyId == previousUsage.SubAssemblyId)
                : tabs.Where(t => t.Usage.RoleId == previousUsage.RoleId && !previousIds.Contains(t.Usage.SubAssemblyId))
                    .OrderBy(t => t.Usage.Name == previousUsage.Name ? 0 : 1)
                    .FirstOrDefault();
        }

        // A product with neither shows Packaging, which is always there, and the card says how to start. A composite
        // always has its Products.
        SelectRecipeTab(tab ?? ComponentsTabIfShown ?? RecipeTabs.OfType<SubAssemblyRecipeTab>().FirstOrDefault<RecipeTab>() ?? PackagingTab);
    }

    /// <summary>
    /// The sub-assemblies the form can link or copy. Linking offers only the Sub-Assemblies screen's (standalone) ones the
    /// recipe doesn't already link, never anything a product keeps; copying offers every other one. Never packaging,
    /// which the Packaging tab links and copies itself. A pick that's no longer offered is cleared. The roles the form
    /// offers follow the recipe too (RefreshFormRoleOptions).
    /// </summary>
    private void RefreshLinkableOptions()
    {
        var linking = UsageForm.EditingUsageMode == UsageFormMode.Link;
        var usedIds = UsageEdits.Select(u => u.SubAssemblyId).ToHashSet();

        // A composite's own sections are only in product-owned roles, so it's offered those.
        UsageForm.LinkableSubAssemblies.MergeFrom(
            [
                .. _linkable
                    .Where(s => (!IsEditingComposite || s.Role?.IsProductOwned == true) && (linking
                        ? s.OwnerProductId is null && !usedIds.Contains(s.Id)
                        : s.OwnerProductId is null || s.OwnerProductId != _detailProductId))
                    .Select(s => new LinkableSubAssemblyOption(s)),
            ],
            o => o.Key);

        if (UsageForm.EditingUsageSource is { } picked && !UsageForm.LinkableSubAssemblies.Contains(picked))
        {
            UsageForm.ShowSource(null);
        }

        RefreshFormRoleOptions();
    }

    /// <summary>
    /// The roles the form offers (FormRoleOptions): every recipe role, as a recipe can have several sub-assemblies in one,
    /// told apart by their names. The role picked stays picked while it's still offered: the picker can drop its pick as
    /// its list changes under it.
    /// </summary>
    private void RefreshFormRoleOptions()
    {
        var pickedRoleId = UsageForm.EditingUsageRole?.Id;
        // Never the Packaging role: packaging isn't a recipe section, and the Packaging tab takes it. A composite's
        // sections are only in product-owned roles (CompositeProductRules.EnsureSectionRoles).
        UsageForm.FormRoleOptions.MergeFrom(
            [.. Roles.Where(r => !r.IsPackaging && (!IsEditingComposite || r.IsProductOwned))],
            r => r.Id);
        UsageForm.ShowRole(UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == pickedRoleId));
    }

    /// <summary>
    /// Points the recipe's sections, saved or not, at the refreshed ingredients and roles, and a linked
    /// section at its refreshed sub-assembly and lines, keeping each section's own piece size, so costs
    /// changed on other tabs reach the recipe and the cost summary. Only a section that shows something
    /// different is rebuilt. The packaging rows are pointed at their refreshed materials the same way.
    /// </summary>
    private void RefreshRecipeRows(IReadOnlyList<SubAssembly> linkedSubAssemblies, IReadOnlyList<SubAssemblyLine> linkedLines)
    {
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        var linkedById = linkedSubAssemblies.ToDictionary(s => s.Id);
        var linkedLinesById = linkedLines.ToLookup(l => l.SubAssemblyId);

        var changed = false;
        for (var i = 0; i < UsageEdits.Count; i++)
        {
            var edit = UsageEdits[i];
            ProductUsageEdit fresh;
            var saved = edit.SubAssemblyId > 0 ? linkedById.GetValueOrDefault(edit.SubAssemblyId) : null;
            var isOwnNow = saved is not null && saved.OwnerProductId is { } ownerId && ownerId == _detailProductId;
            if (saved is not null && edit.IsLinked && isOwnNow)
            {
                // Its role was made product-owned: the product owns it now, with any add-ins it had merged into its lines.
                var role = Roles.FirstOrDefault(r => r.Id == saved.RoleId) ?? saved.Role;
                fresh = new ProductUsageEdit(
                    saved.Id,
                    role,
                    [.. linkedLinesById[saved.Id].Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    edit.AuthoredField,
                    edit.AuthoredValue,
                    linkedName: null,
                    name: saved.SectionName);
            }
            else if (edit.IsLinked || (edit.IsOwned && saved is not null && !isOwnNow))
            {
                // A linked one, or one of its own whose role is no longer product-owned, which it links now.
                if (saved is not { } subAssembly)
                {
                    continue;
                }

                // Its lines, name and role come fresh from the Sub-Assemblies screen; its add-ins are this product's own,
                // so they only need their ingredients' new costs. Lines changed here and not saved yet stay, repriced.
                var role = Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
                fresh = edit.WithLinked(
                    subAssembly.Name,
                    SectionLabelOf(subAssembly.Name, role),
                    role,
                    [.. linkedLinesById[subAssembly.Id].Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    edit.AreLinkedLinesEdited ? Repriced(edit.Lines, ingredientsById) : null,
                    Repriced(edit.AddIns, ingredientsById),
                    PricedSection.LinkedOwnerOf(subAssembly));
            }
            else
            {
                fresh = edit.With(Roles.FirstOrDefault(r => r.Id == edit.RoleId) ?? edit.Role, Repriced(edit.Lines, ingredientsById));
            }

            if (!fresh.ShowsSameAs(edit))
            {
                UsageEdits[i] = fresh;
                changed = true;
            }
        }

        // A renamed role or sub-assembly can move a section.
        if (changed)
        {
            var sorted = UsageEdits.Order(ProductUsageEdit.RecipeOrder).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                var from = UsageEdits.IndexOf(sorted[i]);
                if (from != i)
                {
                    UsageEdits.Move(from, i);
                }
            }
        }

        // The packaging lines at their materials' refreshed costs and units, in place, so a line being typed isn't lost.
        PackagingLineRow.Resolve(Packaging.AllLineEdits, PackagingMaterials.ToDictionary(m => m.Id));
    }

    /// <summary>
    /// The form's lines and add-ins at their ingredients' refreshed costs. It reprices in place rather than
    /// refilling the tables, so a refresh arriving while an add-in is being typed doesn't throw it away.
    /// </summary>
    private void RefreshFormLines()
    {
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        IngredientLineRow.Resolve(UsageForm.FormLines, ingredientsById);
        IngredientLineRow.Resolve(UsageForm.FormAddIns, ingredientsById);
    }

    /// <summary>Puts <paramref name="lines"/> in the form's table, in place of whatever it held.</summary>
    private void ShowFormLines(IEnumerable<SectionLine> lines)
    {
        UsageForm.FormLines.Clear();
        UsageForm.FormLines.AddRange(lines.Select(l => new IngredientLineRow(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)));
    }

    /// <summary>Whether the form's lines table still shows <paramref name="lines"/>, with nothing typed into it since.</summary>
    private bool IsFormShowing(IReadOnlyList<SectionLine> lines) =>
        UsageForm.FormLines.Count == lines.Count
        && UsageForm.FormLines.Zip(lines).All(pair =>
            pair.First.IngredientId == pair.Second.Ingredient.Id
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent);

    /// <summary>The same for the add-ins table under it.</summary>
    private void ShowFormAddIns(IEnumerable<SectionLine> addIns)
    {
        UsageForm.FormAddIns.Clear();
        UsageForm.FormAddIns.AddRange(addIns.Select(a => new IngredientLineRow(a.Ingredient, a.Grams)));
    }
}
