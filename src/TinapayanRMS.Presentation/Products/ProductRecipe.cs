using System.Collections.ObjectModel;
using System.Collections.Specialized;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.Products;

namespace TinapayanRMS.Presentation.Products;

/// <summary>What the Products screen's recipe asks of its screen, on top of what any part of its form asks.</summary>
public interface IProductRecipeHost : IFormPartHost, IProductRecipeDraftHost
{
    /// <summary>
    /// The form's undo history: a section applied, as a tab is left or the form is printed, is one step measured by what it
    /// did to the recipe, and the form opening on another section moves what Save measures against with it.
    /// </summary>
    UndoHistory<ProductFormSnapshot> History { get; }

    /// <summary>Every sub-assembly that could be linked or copied, as the screen last loaded them.</summary>
    IReadOnlyList<SubAssembly> Linkable { get; }

    /// <summary>
    /// Counts the loads that changed <see cref="Linkable"/> or the roles: the section form's choices are built from them,
    /// and built again only when this has moved since (ProductsState.LinkableVersion).
    /// </summary>
    int LinkableVersion { get; }

    /// <summary>The recipe changed: the product's costs are worked out again, from it as it now is, as they're next read.</summary>
    void InvalidateCosts();

    /// <summary>Opens a product on this screen, as another screen's link would (a variant's linked section's owner).</summary>
    void OpenProduct(int productId);

    /// <summary>Opens a sub-assembly on the Sub-Assemblies screen (the screen's SubAssemblyOpenRequests).</summary>
    void OpenSubAssembly(int subAssemblyId);
}

/// <summary>
/// The Products screen's recipe (through ProductsState.Recipe; RecipeStructure draws it): the sub-assemblies it holds,
/// its sections, each with a tab of its own, in recipe order (RecipeOrder), between a composite's Products tab and the
/// Packaging tab, which every product has; and the section form (ProductUsageForm) that adds, links, copies and edits them,
/// opened by its tab and applied as the tab is left. It keeps what Save compares the recipe against, and works out the
/// recipe Save writes. What spans the form (the undo history, the costs, the lists loaded) it asks of the screen
/// (<see cref="IProductRecipeHost"/>); the screen wires the section form's picks and lines to it.
/// </summary>
public sealed class ProductRecipe : ObservableState
{
    private readonly IProductRecipeHost _host;
    private readonly ProductUsageForm _usageForm;
    private readonly IReadOnlyList<RawMaterial> _ingredients;
    private readonly IReadOnlyList<SubAssemblyRole> _roles;
    private readonly IProductQueries _queries;

    /// <summary>The tab whose form is open: the one a change of tab is leaving, whose form is applied on the way out.</summary>
    private RecipeTab? _shownRecipeTab;

    /// <summary>
    /// The recipe showing was cleared for another product's, which opens on its first tab once it arrives
    /// (ShowSaved). What the tab strip was left on as the tabs went says nothing: that's always Packaging.
    /// </summary>
    private bool _isRecipeClearedForAnother;

    /// <param name="host">The screen: its undo history, what can be linked, the product's name and costs.</param>
    /// <param name="usageForm">The section form, which the recipe opens on a section's tab and applies as the tab is left.</param>
    /// <param name="draft">The recipe's sections and what Save writes of them, with the ingredients and roles they point at.</param>
    /// <param name="queries">What reads a sub-assembly's lines for a section, and the Made categories for Make In-House Ingredient.</param>
    public ProductRecipe(
        IProductRecipeHost host,
        ProductUsageForm usageForm,
        ProductRecipeDraft draft,
        IProductQueries queries)
    {
        _host = host;
        _usageForm = usageForm;
        Draft = draft;
        _ingredients = draft.Ingredients;
        _roles = draft.Roles;
        _queries = queries;

        // The recipe's tabs follow its sections as they're added, taken out and moved. The Packaging tab is always there,
        // last: the packaging per piece is typed, linked and copied on the tab itself, and the bulk packaging typed there,
        // never through Add Sub-Assembly, which is for recipe sections.
        RecipeTabs.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(HasRecipeSections));
        RecipeTabs.Add(PackagingTab);

        // The blank form shows it from the start, so its tables are there to type into before anything else happens. Picked
        // below, once the form's own tabs are wired up.
        _selectedRecipeTab = PackagingTab;
        // The tabs follow the sections as they change, before the draft marks the ones sharing a role, and read their
        // headers again after (ProductRecipeDraft.SectionsChanged, RepeatedRolesMarked).
        draft.SectionsChanged += MirrorUsageTabs;
        draft.RepeatedRolesMarked += RefreshTabHeaders;

        // A recipe tab shown: leaving one applies its form, and showing one opens its form (RecipeTabShown). The Packaging
        // tab the blank form starts on, now.
        RecipeTabShown(SelectedRecipeTab);

        // The sections' commands. The packaging's are the Packaging tab's own, and a composite's products' the Products
        // table's.
        OpenLinkedSourceCommand = ScreenCommand.Of<ProductUsageEdit>(usage =>
        {
            if (usage.LinkedOwner is { } owner)
            {
                host.OpenProduct(owner.Id);
            }
            else
            {
                host.OpenSubAssembly(usage.SubAssemblyId);
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
        AddUsageCommand = ScreenCommand.Of<UsageFormMode>(
            mode =>
            {
                if (SelectedRecipeTab is AddSubAssemblyRecipeTab && IsUsageFormUntouched())
                {
                    host.AsUndoStep(() => usageForm.PickMode(mode));
                    return;
                }

                if (!CommitUsageFormOnLeave())
                {
                    return;
                }

                host.AsUndoStep(() =>
                {
                    InsertAddSubAssemblyTab();
                    SelectRecipeTab(AddSubAssemblyTab);
                    usageForm.PickMode(mode);
                });
            });

        // A sub-assembly's tab is its form, so editing one is just showing its tab (RecipeTabShown opens the form).
        EditUsageCommand = ScreenCommand.Of<ProductUsageEdit>(usage => SelectRecipeTab(TabFor(usage)));

        // A linked sub-assembly becomes a new section in the same role and at the same piece size, whose lines the form
        // edits; Save makes it the product's own in a product-owned role, and otherwise keeps it on the Sub-Assemblies
        // screen as a sub-assembly of its own, and links that instead.
        MakeCopyCommand = ScreenCommand.Of<ProductUsageEdit>(usage => host.AsUndoStep(() => ReplaceUsage(usage, ProductRecipeDraft.CopyOfLinked(usage, isOwn: false))));

        // The same copy, but the product's own whatever its role: Save keeps it with the product, off the
        // Sub-Assemblies screen, and the one it was linked to stays there as it is.
        UnlinkCommand = ScreenCommand.Of<ProductUsageEdit>(usage => host.AsUndoStep(() => ReplaceUsage(usage, ProductRecipeDraft.CopyOfLinked(usage, isOwn: true))));

        // Anything typed into its form goes in first, so what moves is what shows; a form a rule refuses stays as it
        // is, with the message. It's still edited here until Save, which moves it and links it.
        MoveToSubAssembliesCommand = ScreenCommand.Of<ProductUsageEdit>(usage => host.AsUndoStep(() =>
        {
            var wasEditing = ReferenceEquals(usage, usageForm.EditingUsage);
            var applied = usage;
            if (wasEditing)
            {
                try
                {
                    host.ShowError(null);
                    applied = ApplyUsageForm();
                }
                catch (BusinessRuleException ex)
                {
                    host.ShowError(ex.Message);
                    return;
                }
            }

            var moving = applied.Released();
            Draft.ReplaceInRecipeOrder(applied, moving);
            host.InvalidateCosts();
            if (wasEditing)
            {
                OpenUsageForm(moving);
            }
        }));

        MoveToInHouseCommand = new ScreenCommand<ProductUsageEdit>(usage => host.RunSafelyAsync(() => MoveToInHouseAsync(usage)));

        // Delete Sub-Assembly takes out the one whose tab is showing. Nothing to confirm: it only leaves the recipe,
        // with Save, and the sub-assembly stays on the Sub-Assemblies screen. The Packaging tab isn't a sub-assembly: its
        // lines are taken out with Remove Line, and a link with Remove Link.
        RemoveUsageCommand = new ScreenCommand(
            () => host.RunSafelyAsync(() =>
            {
                var usage = ((SubAssemblyRecipeTab)SelectedRecipeTab!).Usage;
                host.AsUndoStep(() =>
                {
                    // Whatever was typed into its form goes with it, rather than being applied on the way out.
                    if (ReferenceEquals(usage, usageForm.EditingUsage))
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
                    host.InvalidateCosts();
                });
                return Task.CompletedTask;
            }),
            () => SelectedRecipeTab is SubAssemblyRecipeTab);
    }

    /// <summary>The recipe's sections and what it was saved as: what Save writes of it (<see cref="ProductRecipeDraft"/>).</summary>
    public ProductRecipeDraft Draft { get; }

    /// <summary>Every sub-assembly, saved or not, its own and linked, in recipe order (the draft's).</summary>
    public ObservableCollection<ProductUsageEdit> UsageEdits => Draft.UsageEdits;

    /// <summary>
    /// The Recipe Structure's tabs: a composite's ComponentsTab first, then a sub-assembly's for each of UsageEdits, in
    /// the same order (MirrorUsageTabs, SectionTabOffset), then AddSubAssemblyTab while a sub-assembly is being added,
    /// then PackagingTab, always last and always there, so bulk packaging can be typed into with no packaging for each piece.
    /// A new product has only Packaging, and a new composite its Products and Packaging. Only this part changes them: the
    /// tab strip (RecipeStructure) draws them, and picks one when it's clicked.
    /// </summary>
    public ObservableCollection<RecipeTab> RecipeTabs { get; } = [];

    /// <summary>The recipe has a tab besides Packaging (a section's, New Sub-Assembly's, or a composite's Products); with none, the card says how to start.</summary>
    public bool HasRecipeSections => RecipeTabs.Any(t => t is not PackagingRecipeTab);

    /// <summary>The New Sub-Assembly tab, in RecipeTabs only while Add Sub-Assembly's form is open in it.</summary>
    public AddSubAssemblyRecipeTab AddSubAssemblyTab { get; } = new();

    public PackagingRecipeTab PackagingTab { get; } = new();

    /// <summary>A composite's Products tab, in RecipeTabs only while the product showing is a composite (ShowComponentsTab).</summary>
    public ComponentsRecipeTab ComponentsTab { get; } = new();

    private RecipeTab? _selectedRecipeTab;
    /// <summary>
    /// The Recipe Structure tab showing: picked in the strip (<see cref="PickRecipeTab"/>), shown by the screen
    /// (SelectRecipeTab), or put back by an undo. Showing New Sub-Assembly opens the form that adds one.
    /// </summary>
    public RecipeTab? SelectedRecipeTab
    {
        get => _selectedRecipeTab;
        private set => RaiseAndSetIfChanged(ref _selectedRecipeTab, value);
    }

    /// <summary>
    /// The sub-assembly form shows under the Recipe Structure's tabs: on a sub-assembly's tab, and on New
    /// Sub-Assembly's while its form is open. There's one form for them all (RecipeStructure).
    /// </summary>
    public bool IsUsageFormShowing =>
        SelectedRecipeTab is SubAssemblyRecipeTab || (SelectedRecipeTab is AddSubAssemblyRecipeTab && _usageForm.IsUsageFormOpen);

    /// <summary>The packaging shows under the Recipe Structure's tabs: on the Packaging tab.</summary>
    public bool IsPackagingShowing => SelectedRecipeTab is PackagingRecipeTab;

    /// <summary>A composite's Products table shows under the Recipe Structure's tabs: on its Products tab.</summary>
    public bool IsComponentsShowing => SelectedRecipeTab is ComponentsRecipeTab;

    /// <summary>Adds a sub-assembly the way the parameter says: Add Sub-Assembly's drop-down carries one item per way.</summary>
    public ScreenCommand<UsageFormMode> AddUsageCommand { get; }

    public ScreenCommand<ProductUsageEdit> EditUsageCommand { get; }

    public ScreenCommand<ProductUsageEdit> MakeCopyCommand { get; }

    /// <summary>Unlink on a linked section: it becomes a copy of the product's own, in whatever role, and the link goes with Save.</summary>
    public ScreenCommand<ProductUsageEdit> UnlinkCommand { get; }

    /// <summary>
    /// Move to Sub-Assemblies on one of the product's own: Save puts it on the Sub-Assemblies screen, keeping its name and
    /// lines, and links it. Unlink the other way round.
    /// </summary>
    public ScreenCommand<ProductUsageEdit> MoveToSubAssembliesCommand { get; }

    /// <summary>
    /// Move to In-House on one of the product's own: asks for the made ingredient's name and Made category, then Save moves
    /// the section to the Sub-Assemblies screen, links it there, and adds that ingredient, made from it.
    /// </summary>
    public ScreenCommand<ProductUsageEdit> MoveToInHouseCommand { get; }

    /// <summary>Delete Sub-Assembly: takes the sub-assembly whose tab is showing out of the recipe.</summary>
    public ScreenCommand RemoveUsageCommand { get; }

    /// <summary>
    /// A linked recipe section's badge clicked: opens where it's kept, the product whose own it is for a variant's link
    /// (IProductRecipeHost.OpenProduct), and otherwise the Sub-Assemblies screen, as the screen's OpenSubAssemblyCommand does.
    /// </summary>
    public ScreenCommand<ProductUsageEdit> OpenLinkedSourceCommand { get; }

    /// <summary>
    /// Input: Move to In-House's prompt, its name box starting with the section's saved name and its picker on a Made
    /// category. Output: the name typed and the category picked, or null if it was cancelled. The page answers it with a
    /// dialog with a picker (Prompts).
    /// </summary>
    public Interaction<ChoicePromptRequest, ChoicePromptResult?> PromptForMadeIngredient { get; } = new();

    /// <summary>
    /// A composite's Products tab, first, before its sections' tabs, for a composite; none for a product of sub-assemblies.
    /// The screen sets IsEditingComposite first.
    /// </summary>
    internal void ShowComponentsTab(bool isComposite)
    {
        if (isComposite == RecipeTabs.Contains(ComponentsTab))
        {
            return;
        }

        if (isComposite)
        {
            RecipeTabs.Insert(0, ComponentsTab);
        }
        else
        {
            RecipeTabs.Remove(ComponentsTab);
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

    /// <summary>New Sub-Assembly's tab, shown, with a fresh add form in it: a new product started linking a sub-assembly.</summary>
    internal void OpenAddSubAssemblyTab()
    {
        InsertAddSubAssemblyTab();
        SelectRecipeTab(AddSubAssemblyTab);
    }

    /// <summary>The saved usages as sections, in recipe order; they're also what Save compares the recipe against.</summary>
    internal void ShowSaved(
        IReadOnlyList<ProductSubAssemblyUsage> usages, IReadOnlyList<SubAssemblyLine> subAssemblyLines, IReadOnlyList<UsageAddInLine> addIns)
    {
        // Another product's recipe opens on its first tab; the same product's, reloaded, keeps the tab it showed. Its
        // section is read now, before the tabs take on the new sections.
        var previousTab = _isRecipeClearedForAnother ? null : SelectedRecipeTab;
        var previousUsage = (previousTab as SubAssemblyRecipeTab)?.Usage;
        HashSet<int> previousIds = [.. UsageEdits.Select(u => u.SubAssemblyId)];
        _isRecipeClearedForAnother = false;

        Draft.Show(usages, subAssemblyLines, addIns);
        ReselectRecipeTab(previousTab, previousUsage, previousIds);
    }

    /// <summary>
    /// Empties the recipe and what Save compares it against. With <paramref name="keepRecipeTabs"/>, the sections and their
    /// tabs stay for another product's to replace in the same pass (ShowSaved): the tab strip then keeps its tabs rather
    /// than building a new one for each.
    /// </summary>
    internal void Clear(bool keepRecipeTabs)
    {
        Draft.Clear(keepSections: keepRecipeTabs);
        RemoveAddSubAssemblyTab();
    }

    /// <summary>
    /// After the form was cleared: a picked product's sub-assemblies are on their way (<paramref name="forAnother"/>), and
    /// pick the tab when they arrive; a blank form opens on its first tab, a composite's on its Products, whatever showed
    /// before.
    /// </summary>
    internal void AfterClear(bool forAnother)
    {
        _isRecipeClearedForAnother = forAnother;
        if (!forAnother)
        {
            ReselectRecipeTab(previous: null, previousUsage: null, previousIds: []);
        }
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
                if (ReferenceEquals(previous, _usageForm.EditingUsage))
                {
                    if (replacement.IsEditable != previous.IsEditable)
                    {
                        // Its role was made product-owned in Settings, or no longer is: the form opens on it again, its
                        // lines now the product's to type into, or the Sub-Assemblies screen's to show.
                        OpenUsageForm(replacement);
                    }
                    else
                    {
                        _usageForm.EditingUsage = replacement;

                        // A linked one's lines as they now are, from the Sub-Assemblies screen or changed here and applied;
                        // lines typed into the form and not applied yet stay, repriced by UsageForm.ResolveRows.
                        if (replacement.IsLinked && _usageForm.IsShowing(previous.Lines))
                        {
                            _usageForm.ShowLines(replacement.Lines);
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
    /// The screen shows <paramref name="tab"/>, as part of whatever undo step it's taking, and does what showing it does
    /// even when it was already showing.
    /// </summary>
    private void SelectRecipeTab(RecipeTab? tab)
    {
        SelectedRecipeTab = tab;
        RecipeTabShown(tab);
    }

    /// <summary>
    /// The user picks <paramref name="tab"/> in the strip; picking the one showing does nothing. The pick runs between
    /// undo steps: should a step still be open (a field that hasn't yet heard it lost the cursor), it closes on the tab
    /// being left, keeping what was typed there, and another opens on the tab arrived at, for the field to close. Left
    /// open over the change of tab, the step would record the form changing over to the other tab's section as an edit.
    /// </summary>
    public void PickRecipeTab(RecipeTab? tab)
    {
        if (Equals(tab, SelectedRecipeTab))
        {
            return;
        }

        SelectedRecipeTab = tab;
        var history = _host.History;
        if (!history.IsStepOpen)
        {
            RecipeTabShown(tab);
            return;
        }

        history.End();
        try
        {
            RecipeTabShown(tab);
        }
        finally
        {
            history.Begin();
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
        if (!ReferenceEquals(_shownRecipeTab, tab) && !CommitUsageFormOnLeave())
        {
            SelectedRecipeTab = _shownRecipeTab;
            return;
        }

        _shownRecipeTab = tab;

        switch (tab)
        {
            case SubAssemblyRecipeTab subAssembly:
                if (!ReferenceEquals(_usageForm.EditingUsage, subAssembly.Usage))
                {
                    OpenUsageForm(subAssembly.Usage);
                }

                break;

            case AddSubAssemblyRecipeTab:
                if (!_usageForm.IsUsageFormOpen || !_usageForm.IsAddingUsage)
                {
                    StartAddingUsage();
                }

                break;

            case PackagingRecipeTab or ComponentsRecipeTab or null:
                break;
        }

        if (tab is not AddSubAssemblyRecipeTab)
        {
            RemoveAddSubAssemblyTab();
        }
    }

    /// <summary>Takes New Sub-Assembly's tab out of the strip, if it's there.</summary>
    private void RemoveAddSubAssemblyTab() => RecipeTabs.Remove(AddSubAssemblyTab);

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
    /// The view's lists the section form's choices (<see cref="RefreshLinkableOptions"/>) were last built from
    /// (IProductRecipeHost.LinkableVersion). The view's load merges into every open tab, and building the choices again,
    /// every sub-assembly's option compared with the last, was most of what each tab's merge cost: so a load builds them
    /// only when those lists changed. What the recipe holds changing builds them at once, as it always has.
    /// </summary>
    private int _optionsBuiltFrom = -1;

    /// <summary>The view's load is merged: the section form's choices follow the lists it read, if they changed.</summary>
    internal void RefreshLinkableOptionsAfterLoad()
    {
        if (_optionsBuiltFrom != _host.LinkableVersion)
        {
            RefreshLinkableOptions();
        }
    }

    /// <summary>
    /// The sub-assemblies the form can link or copy. Linking offers only the Sub-Assemblies screen's (standalone) ones the
    /// recipe doesn't already link, never anything a product keeps; copying offers every other one. Never packaging,
    /// which the Packaging tab links and copies itself. A pick that's no longer offered is cleared. The roles the form
    /// offers follow the recipe too (RefreshFormRoleOptions).
    /// </summary>
    internal void RefreshLinkableOptions()
    {
        _optionsBuiltFrom = _host.LinkableVersion;
        var linking = _usageForm.EditingUsageMode == UsageFormMode.Link;
        var usedIds = UsageEdits.Select(u => u.SubAssemblyId).ToHashSet();
        var isComposite = _host.IsEditingComposite;
        var productId = _host.DetailProductId;

        // A composite's own sections are only in product-owned roles, so it's offered those.
        _usageForm.LinkableSubAssemblies.MergeFrom(
            [
                .. _host.Linkable
                    .Where(s => (!isComposite || s.Role?.IsProductOwned == true) && (linking
                        ? s.OwnerProductId is null && !usedIds.Contains(s.Id)
                        : s.OwnerProductId is null || s.OwnerProductId != productId))
                    .Select(s => new LinkableSubAssemblyOption(s)),
            ],
            o => o.Key);

        if (_usageForm.EditingUsageSource is { } picked && !_usageForm.LinkableSubAssemblies.Contains(picked))
        {
            _usageForm.EditingUsageSource = null;
        }

        RefreshFormRoleOptions();
    }

    /// <summary>
    /// The roles the form offers (FormRoleOptions): every recipe role, as a recipe can have several sub-assemblies in one,
    /// told apart by their names. The role picked stays picked while it's still offered: the picker can drop its pick as
    /// its list changes under it.
    /// </summary>
    internal void RefreshFormRoleOptions()
    {
        var pickedRoleId = _usageForm.EditingUsageRole?.Id;
        var isComposite = _host.IsEditingComposite;
        // Never the Packaging role: packaging isn't a recipe section, and the Packaging tab takes it. A composite's
        // sections are only in product-owned roles (CompositeProductRules.EnsureSectionRoles).
        _usageForm.FormRoleOptions.MergeFrom(
            [.. _roles.Where(r => !r.IsPackaging && (!isComposite || r.IsProductOwned))],
            r => r.Id);
        _usageForm.EditingUsageRole = _usageForm.FormRoleOptions.FirstOrDefault(r => r.Id == pickedRoleId);
    }

    /// <summary>
    /// <paramref name="snapshot"/>, an undo step's recipe, with its linked sections as a load just found them saved where
    /// they're kept (as <see cref="ProductRecipeDraft.RefreshRows"/> brought them into the form), and the section form open on one of them
    /// showing its lines as saved now, unless they're typed into there. What the product holds of its own (a piece size,
    /// add-ins, lines edited here) stays as the step had it. An undo or a redo then never puts back a sub-assembly's lines as
    /// they were before it was saved on the Sub-Assemblies screen.
    /// </summary>
    internal RecipeSnapshot WithLinkedAsSavedNow(RecipeSnapshot snapshot, IReadOnlyList<SubAssembly> linkedSubAssemblies, IReadOnlyList<SubAssemblyLine> linkedLines)
    {
        var ingredientsById = _ingredients.ToDictionary(m => m.Id);
        var linkedById = linkedSubAssemblies.ToDictionary(s => s.Id);
        var linkedLinesById = linkedLines.ToLookup(l => l.SubAssemblyId);
        var productId = _host.DetailProductId;
        List<ProductUsageEdit> usages = [.. snapshot.Usages];
        var form = snapshot.Form;
        var changed = false;
        for (var i = 0; i < usages.Count; i++)
        {
            var edit = usages[i];
            if (!edit.IsLinked
                || linkedById.GetValueOrDefault(edit.SubAssemblyId) is not { } saved
                || (saved.OwnerProductId is { } ownerId && ownerId == productId))
            {
                continue;
            }

            var fresh = Draft.AsSavedNow(edit, saved, linkedLinesById, ingredientsById);
            if (fresh.ShowsSameAs(edit))
            {
                continue;
            }

            usages[i] = fresh;
            changed = true;
            if (form is { IsAdding: false, EditingUsageIndex: { } at } && at == i && !edit.AreLinkedLinesEdited && ShowsLines(form.Lines, edit.Lines))
            {
                IReadOnlyList<IngredientLineSnapshot> lines =
                    [.. fresh.Lines.Select(l => new IngredientLineSnapshot(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
                // The figure typed stays and the other follows the new batch, as the form's own does (FollowPieceSize).
                var batchWeight = lines.Sum(l => l.Grams ?? 0m) + form.AddIns.Sum(l => l.Grams ?? 0m);
                var typed = form.AuthoredField == UsageAuthoredField.WeightPerPiece ? form.WeightPerPiece : form.Yield;
                var (weightPerPiece, yield) = ProductRules.ResolvePieceSize(form.AuthoredField, typed, batchWeight);
                form = form with
                {
                    Title = EditTitleOf(fresh),
                    Lines = lines,
                    FormulaBase = SubAssemblyRules.FormulaBaseWeight([.. lines.Select(l => (l.Grams ?? 0m, l.IsFormulaBase))]),
                    WeightPerPiece = form.AuthoredField == UsageAuthoredField.WeightPerPiece ? form.WeightPerPiece : Math.Round(weightPerPiece, 2, MidpointRounding.ToEven),
                    Yield = form.AuthoredField == UsageAuthoredField.Yield ? form.Yield : Math.Round(yield, 2, MidpointRounding.ToEven),
                };
            }
        }

        return changed ? snapshot with { Usages = usages, Form = form } : snapshot;
    }

    /// <summary>Whether the form's lines, as a step kept them, are <paramref name="lines"/>: nothing typed over them there.</summary>
    private static bool ShowsLines(IReadOnlyList<IngredientLineSnapshot> form, IReadOnlyList<SectionLine> lines) =>
        form.Count == lines.Count
        && form.Zip(lines).All(pair =>
            pair.First.IngredientId == pair.Second.Ingredient.Id
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent);

    /// <summary>The add form, fresh for its mode: a new sub-assembly's name, role and lines, or the pick of another product's.</summary>
    internal void StartUsageFormMode()
    {
        var mode = _usageForm.EditingUsageMode;
        _usageForm.IsFormEditingLines = mode != UsageFormMode.Link;
        _usageForm.IsFormPickingSource = mode != UsageFormMode.New;

        // The title is the one thing on the form that says which way in was picked, now that the picker that used to
        // say it is on Add Sub-Assembly's drop-down instead. Set here, not in StartAddingUsage, so it follows another
        // item picked while the form is open. A new one has none: its "Moves to Sub-Assemblies on Save" badge
        // (IsFormNewMovingToSubAssemblies) takes the title's place, and in a product-owned role nothing does.
        _usageForm.UsageFormTitle = mode switch
        {
            UsageFormMode.Link => "Link an existing sub-assembly",
            UsageFormMode.Copy => "Copy an existing sub-assembly",
            _ => string.Empty,
        };
        _usageForm.FormLines.Clear();
        _usageForm.FormAddIns.Clear();
        _usageForm.SelectedFormLine = null;
        _usageForm.SelectedFormAddIn = null;
        RefreshLinkableOptions();

        // The first role the recipe doesn't have yet, so sections can be added in role order without picking.
        var takenRoleIds = UsageEdits.Select(u => u.RoleId).ToHashSet();
        _usageForm.PickRole(_usageForm.FormRoleOptions.FirstOrDefault(r => !takenRoleIds.Contains(r.Id)) ?? _usageForm.FormRoleOptions.FirstOrDefault());
        _usageForm.EditingUsageName = null;
        _usageForm.PickSource(null);
        _usageForm.StartPieceSize();
    }

    /// <summary>
    /// Links <paramref name="target"/> in place of the linked section the form shows, as one undo step, keeping what's
    /// this product's: the piece size as the form has it, and the add-ins, less any the new recipe
    /// already has (a toast names them). Lines changed here but not saved go with the old link. The section keeps its
    /// tab, which takes the new one's role and name; Save writes the new link.
    /// </summary>
    internal async Task ChangeLinkAsync(LinkableSubAssemblyOption target, CancellationToken cancellationToken)
    {
        if (_usageForm.EditingUsage is not { IsLinked: true } linked || target.SubAssembly is not { } subAssembly)
        {
            return;
        }

        var saved = await _queries.ReadSubAssemblyLinesAsync(subAssembly.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // The form moved on while the lines loaded: another section, or the change was cancelled.
        if (!_usageForm.IsChangingLink || !ReferenceEquals(_usageForm.EditingUsage, linked) || !ReferenceEquals(_usageForm.ChangeLinkTarget, target))
        {
            return;
        }

        var ingredientsById = _ingredients.ToDictionary(m => m.Id);
        IReadOnlyList<SectionLine> lines =
            [.. saved.Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
        var role = _roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
        try
        {
            _host.ShowError(null);
            if (lines.Count == 0)
            {
                throw new BusinessRuleException($"{subAssembly.Name} has no ingredients yet. Add them on the Sub-Assemblies screen first.");
            }

            var lineIngredientIds = lines.Select(l => l.Ingredient.Id).ToHashSet();
            var addIns = IngredientLineRow.CompleteLines(_usageForm.FormAddIns, subAssembly.Name);
            var dropped = addIns.Where(a => lineIngredientIds.Contains(a.Ingredient.Id)).Select(a => a.Ingredient.Name).ToList();
            var replacement = new ProductUsageEdit(
                subAssembly.Id,
                role,
                lines,
                _usageForm.AuthoredField,
                _usageForm.AuthoredValue,
                subAssembly.Name,
                [.. addIns.Where(a => !lineIngredientIds.Contains(a.Ingredient.Id)).Select(a => new SectionLine(a.Ingredient, a.Grams))],
                Draft.SectionLabelOf(subAssembly.Name, role));
            Draft.EnsureSectionsDistinct(replacement, linked);

            _host.AsUndoStep(() => ReplaceUsage(linked, replacement));
            if (dropped.Count > 0)
            {
                _host.ShowSuccess($"Now linked to {subAssembly.Name}. {string.Join(", ", dropped)} left the add-ins: {subAssembly.Name} already has {(dropped.Count == 1 ? "it" : "them")}.");
            }
        }
        catch (BusinessRuleException ex)
        {
            _host.ShowError(ex.Message);
        }
    }

    /// <summary>
    /// After a sub-assembly was picked to link or copy: its lines, for the batch weight, and for a copy
    /// also its role, all editable from then on.
    /// </summary>
    internal async Task LoadUsageSourceAsync(LinkableSubAssemblyOption? source, CancellationToken cancellationToken)
    {
        IReadOnlyList<SectionLine> lines = [];
        if (source?.SubAssembly is { } subAssembly)
        {
            var saved = await _queries.ReadSubAssemblyLinesAsync(subAssembly.Id, cancellationToken);
            var ingredientsById = _ingredients.ToDictionary(m => m.Id);
            lines = [.. saved.Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams))];
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The form moved on while the lines loaded (applied, cancelled, or another pick): they're stale.
        if (!_usageForm.IsUsageFormOpen || !_usageForm.IsAddingUsage || !ReferenceEquals(_usageForm.EditingUsageSource, source))
        {
            return;
        }

        _usageForm.ShowLines(lines);

        // A copy takes its source's role. Not its name, which is another product's and role's ("Cheese Roll Filling:
        // Cheese"): Save names the copy after this product.
        if (_usageForm.EditingUsageMode == UsageFormMode.Copy && source is not null)
        {
            _usageForm.PickRole(_usageForm.FormRoleOptions.FirstOrDefault(r => r.Id == source.SubAssembly!.RoleId) ?? _usageForm.EditingUsageRole);
            _usageForm.EditingUsageName = null;
        }

        FormLinesChanged();
    }

    /// <summary>
    /// Move to In-House on one of the product's own: asks for the made ingredient's name, starting with the name the
    /// section is saved under, and its Made category, starting on In-House when there's one by that name. Then, in one undo
    /// step, it applies whatever is typed into the section's form, so what moves is what shows, and marks the section,
    /// which Save turns into the ingredient. With no Made category, it says where to add one. A name another ingredient
    /// has, or another section here is moving under, is refused at once; Save checks it against every raw material.
    /// </summary>
    private async Task MoveToInHouseAsync(ProductUsageEdit usage)
    {
        _host.ShowError(null);
        var categories = await _queries.ReadMadeCategoriesAsync(CancellationToken.None);
        if (categories.Count == 0)
        {
            _host.ShowError(MadeIngredientPrompt.NoMadeCategoryMessage);
            return;
        }

        var wasEditing = ReferenceEquals(usage, _usageForm.EditingUsage);
        var initialName = SubAssemblyRules.StandaloneNameFor(_host.EditingName.Trim(), usage.RoleName, wasEditing ? _usageForm.EditingUsageName : usage.Name);
        var answer = await PromptForMadeIngredient.Handle(MadeIngredientPrompt.RequestFor(
            "Move to In-House",
            initialName,
            categories,
            "Save puts this section on the Sub-Assemblies screen, keeping its name and lines, links this product to it there, and adds a made ingredient made from it, with no weight loss, so other recipes can use it as an ingredient."));
        if (answer is null)
        {
            return;
        }

        var name = answer.Text.Trim();
        if (_ingredients.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            || UsageEdits.Any(u => !ReferenceEquals(u, usage) && string.Equals(u.ToMadeIngredient?.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _host.ShowError(MadeIngredientPrompt.NameTakenMessage(name));
            return;
        }

        var made = new MadeIngredientDraft(name, categories[answer.Choice].Id);
        _host.AsUndoStep(() =>
        {
            var applied = usage;
            if (wasEditing)
            {
                try
                {
                    applied = ApplyUsageForm();
                }
                catch (BusinessRuleException ex)
                {
                    _host.ShowError(ex.Message);
                    return;
                }
            }

            var moving = applied.MovedToInHouse(made);
            Draft.ReplaceInRecipeOrder(applied, moving);
            _host.InvalidateCosts();
            if (wasEditing)
            {
                OpenUsageForm(moving);
            }
        });
    }

    /// <summary>
    /// Puts the form's sub-assembly in the recipe and closes the form, without moving the tab showing: the caller
    /// decides which tab follows, since Apply shows the sub-assembly's own and a tab change shows the one left for.
    /// An edit that changes nothing is left alone, so merely looking at a section doesn't churn the recipe.
    /// </summary>
    private ProductUsageEdit ApplyUsageForm()
    {
        var authoredValue = _usageForm.AuthoredValue;
        ProductUsageEdit usage;

        if (_usageForm.EditingUsage is { IsLinked: true } linked)
        {
            // Its lines are typed into as a new section's are, and Save writes them where it's kept.
            var lines = IngredientLineRow.CompleteLines(_usageForm.FormLines, linked.DisplayName);
            if (lines.Count == 0)
            {
                throw new BusinessRuleException($"Add the ingredients that go into {linked.DisplayName}, in grams.");
            }

            IReadOnlyList<SectionLine> lineEdits = [.. lines.Select(l => new SectionLine(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
            usage = linked.WithLinkedForm(
                lineEdits, _usageForm.AuthoredField, authoredValue, CompleteAddIns(linked.DisplayName, lineEdits));
        }
        else if (_usageForm.IsFormEditingLines)
        {
            var role = _usageForm.EditingUsageRole
                ?? throw new BusinessRuleException("Pick the sub-assembly's role. It's what the sub-assembly is called.");

            var lines = IngredientLineRow.CompleteLines(_usageForm.FormLines, role.Name);
            if (lines.Count == 0)
            {
                throw new BusinessRuleException($"Add the ingredients that go into the {role.Name}, in grams.");
            }

            // One of the product's own keeps its id, so Save rewrites it in place; a new one has none yet. One unlinked
            // and not saved yet stays the product's own, and one on its way to the Sub-Assemblies screen stays so.
            usage = new ProductUsageEdit(
                _usageForm.EditingUsage is { IsOwned: true } own ? own.SubAssemblyId : 0, role, [.. lines.Select(l => new SectionLine(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], _usageForm.AuthoredField, authoredValue, linkedName: null, name: _usageForm.EditingUsageName, isOwn: _usageForm.EditingUsage?.IsOwned == true, isReleasing: _usageForm.EditingUsage?.IsReleasing == true, toMadeIngredient: _usageForm.EditingUsage?.ToMadeIngredient);
        }
        else
        {
            var source = _usageForm.EditingUsageSource?.SubAssembly
                ?? throw new BusinessRuleException("Pick the sub-assembly to link, or cancel the form.");
            var sourceName = source.Name;

            // The table's read-only lines are the ones LoadUsageSourceAsync loaded for this very pick; a pick
            // whose lines are still on their way has none yet, and linking it would make a batch of nothing.
            if (_usageForm.FormLines.Count == 0)
            {
                throw new BusinessRuleException($"{sourceName} is still loading its ingredients. Try again in a moment.");
            }

            IReadOnlyList<SectionLine> sourceLines =
                [.. _usageForm.FormLines.Where(l => l.Ingredient is not null).Select(l => new SectionLine(l.Ingredient!, l.Grams ?? 0m, l.IsFormulaBase, l.AuthoredBakersPercent))];

            var role = _roles.FirstOrDefault(r => r.Id == source.RoleId) ?? source.Role;
            usage = new ProductUsageEdit(
                source.Id,
                role,
                sourceLines,
                _usageForm.AuthoredField,
                authoredValue,
                source.Name,
                CompleteAddIns(sourceName, sourceLines),
                Draft.SectionLabelOf(source.Name, role));
        }

        if (_usageForm.EditingUsage is { } unchanged && usage.ShowsSameAs(unchanged))
        {
            CloseUsageForm();
            return unchanged;
        }

        // Sections in one role told apart by their names. A linked one being edited keeps its role and name, which are
        // the Sub-Assemblies screen's to change; and a section left as it was returned above, so it can still be looked
        // through, and Save says what's wrong when it's next changed.
        if (_usageForm.EditingUsage is not { IsLinked: true })
        {
            Draft.EnsureSectionsDistinct(usage, _usageForm.EditingUsage);
        }

        if (_usageForm.EditingUsage is { } editing)
        {
            Draft.ReplaceInRecipeOrder(editing, usage);
        }
        else
        {
            Draft.InsertInRecipeOrder(usage);
        }

        CloseUsageForm();
        RefreshLinkableOptions();
        _host.InvalidateCosts();
        return usage;
    }

    /// <summary>
    /// Applies the form of a tab being left, so nothing typed into it is lost (the grids commit the same way when
    /// focus leaves them). False when a rule refused it: the message shows and the caller stays on the tab.
    /// </summary>
    private bool CommitUsageFormOnLeave()
    {
        if (!_usageForm.IsUsageFormOpen)
        {
            return true;
        }

        // An add form nothing was entered in has nothing to apply: it just closes, and its tab goes.
        if (IsUsageFormUntouched())
        {
            CloseUsageForm();
            return true;
        }

        // Applying a section is one undo step, wherever it's applied from: a tab left, or a Save. A refused one
        // changes only the message and whether the add-ins table is open, neither of which counts as a change, so
        // the step settles into nothing of its own accord. An apply closes the form, which isn't a change either:
        // the step is measured by what it did to the recipe, as Save's is (WithFormOf), so a tab left with nothing
        // typed into its form records nothing.
        var history = _host.History;
        history.Begin();
        try
        {
            _host.ShowError(null);
            ApplyUsageForm();
            return true;
        }
        catch (BusinessRuleException ex)
        {
            _host.ShowError(ex.Message);
            return false;
        }
        finally
        {
            history.End((before, now) => before.WithFormOf(now));
        }
    }

    /// <summary>
    /// Applies a form left open, as Save does, so what's typed into it is in the recipe, and shows its section's tab with
    /// the form open on it again. One undo step, measured as leaving its tab measures one: by what it did to the recipe.
    /// An add form nothing was entered in is left as it is. A rule refusing the form throws, and the form stays as typed.
    /// </summary>
    internal void ApplyOpenForm()
    {
        if (!_usageForm.IsUsageFormOpen || IsUsageFormUntouched())
        {
            return;
        }

        var history = _host.History;
        history.Begin();
        try
        {
            SelectRecipeTab(TabFor(ApplyUsageForm()));
        }
        finally
        {
            history.End((before, now) => before.WithFormOf(now));
        }
    }

    /// <summary>
    /// Save's apply: a form left open is part of what's being saved, unless it's an add form nothing was entered in, which
    /// has nothing to save. Its sub-assembly's tab shows, as Apply would show it, so an add made this way stays the tab
    /// showing through the reload. A rule refusing the form throws, and the form stays as typed.
    /// </summary>
    internal void ApplyFormForSave()
    {
        if (_usageForm.IsUsageFormOpen && !IsUsageFormUntouched())
        {
            SelectRecipeTab(TabFor(ApplyUsageForm()));
        }
    }

    /// <summary>
    /// The add-ins table as lines, refused if one is an ingredient <paramref name="baseLines"/> already has.
    /// The repository checks the same rule as it writes them; this is what puts the message on the screen
    /// the moment Apply is pressed, or the tab is left.
    /// </summary>
    private IReadOnlyList<SectionLine> CompleteAddIns(string subAssemblyName, IReadOnlyList<SectionLine> baseLines)
    {
        var addIns = IngredientLineRow.CompleteLines(_usageForm.FormAddIns, subAssemblyName);
        ProductRules.EnsureValidAddIns(
            [.. addIns.Select(a => (a.Ingredient.Id, a.Grams, a.Ingredient.Name))],
            subAssemblyName,
            [.. baseLines.Select(l => l.Ingredient.Id)]);

        return [.. addIns.Select(a => new SectionLine(a.Ingredient, a.Grams))];
    }

    /// <summary>
    /// Each section's tab reads what it's called again: whether the recipe has others in its role, which the draft marks
    /// first (ProductRecipeDraft.MarkRepeatedRoles), names it too ("Filling: Ube"). Run whenever the recipe's sections change.
    /// </summary>
    private void RefreshTabHeaders()
    {
        foreach (var tab in RecipeTabs.OfType<SubAssemblyRecipeTab>())
        {
            tab.RefreshHeader();
        }
    }

    /// <summary>Closes the section form, as a restore, another product or a section taken out closes it.</summary>
    internal void CloseUsageForm()
    {
        _usageForm.IsUsageFormOpen = false;
        _usageForm.EditingUsage = null;
        RebaseSettledForm();
    }

    /// <summary>
    /// The sub-assembly form now holds another section's values, or none, without anything having been edited,
    /// so what Save measures itself against follows it: clicking through the recipe's tabs isn't a change to
    /// write. Only the form and the tabs move (ProductFormSnapshot.WithFormOf) - the recipe a tab left has
    /// already been applied into the sections by then, and those are measured as they were saved.
    ///
    /// Not while a restore is putting a form back: there the form's values are the user's, stepped back to.
    /// </summary>
    internal void RebaseSettledForm()
    {
        if (!_host.IsBusyApplying)
        {
            _host.History.Rebase((settled, now) => settled.WithFormOf(now));
        }
    }

    /// <summary>The section form's title over <paramref name="usage"/>: "Edit Dough", a linked one with where it's kept.</summary>
    private static string EditTitleOf(ProductUsageEdit usage) =>
        usage.IsEditable ? $"Edit {usage.SectionName}" : $"Edit {usage.SectionName} ({usage.LinkedFromText})";

    /// <summary>The form on <paramref name="usage"/>, as it was last applied: the whole of its tab.</summary>
    internal void OpenUsageForm(ProductUsageEdit usage)
    {
        _usageForm.EditingUsage = usage;
        _usageForm.IsAddingUsage = false;
        _usageForm.UsageFormTitle = EditTitleOf(usage);
        _usageForm.IsFormEditingLines = usage.IsEditable;
        _usageForm.IsFormPickingSource = false;

        // Shown, not picked: the form edits a section now, not adding one, so nothing a pick sets off has anything to do.
        _usageForm.EditingUsageSource = null;
        RefreshFormRoleOptions();
        _usageForm.EditingUsageRole = _usageForm.FormRoleOptions.FirstOrDefault(r => r.Id == usage.RoleId) ?? usage.Role;
        _usageForm.EditingUsageName = usage.Name;

        // A linked one's lines show too, read-only: they're the Sub-Assemblies screen's. What is this product's is its piece
        // size and its add-ins, which go in the table under them, open if there are any.
        _usageForm.ShowLines(usage.Lines);
        _usageForm.ShowAddIns(usage.AddIns);
        _usageForm.SelectedFormLine = null;
        _usageForm.SelectedFormAddIn = null;
        _usageForm.ShowPieceSize(usage.AuthoredField, usage.AuthoredValue);
        _usageForm.IsUsageFormOpen = true;
        RebaseSettledForm();
    }

    /// <summary>
    /// A fresh form to add a sub-assembly, in place of any the form had open. It starts in New, and AddUsageCommand
    /// sets the way its drop-down item asked for straight after, which starts the form again in it.
    /// </summary>
    private void StartAddingUsage()
    {
        _usageForm.EditingUsage = null;
        _usageForm.IsAddingUsage = true;
        _usageForm.PickMode(UsageFormMode.New);
        _usageForm.IsUsageFormOpen = true;
        StartUsageFormMode();
        RebaseSettledForm();
    }

    /// <summary>
    /// The add form as it opened: nothing picked to link or copy, no line, and the piece size it started at. Its role
    /// alone isn't an entry.
    /// </summary>
    private bool IsUsageFormUntouched() =>
        _usageForm.IsAddingUsage
        && _usageForm.EditingUsageSource is null
        && string.IsNullOrWhiteSpace(_usageForm.EditingUsageName)
        && _usageForm.FormLines.All(l => l.IngredientId is null && l.Grams == 0)
        && _usageForm.FormAddIns.All(l => l.IngredientId is null && l.Grams == 0)
        && _usageForm.IsPieceSizeAsStarted(_usageForm.EditingUsageRole);

    /// <summary>A line picked, typed or deleted in the form's table: it's priced, and the batch and preview follow.</summary>
    internal void FormLinesEdited()
    {
        IngredientLineRow.Resolve(_usageForm.FormLines, _ingredients.ToDictionary(m => m.Id));
        FormLinesChanged();
    }

    /// <summary>An add-in picked, typed or deleted: it's priced, and the batch it joins follows.</summary>
    internal void FormAddInsEdited()
    {
        IngredientLineRow.Resolve(_usageForm.FormAddIns, _ingredients.ToDictionary(m => m.Id));
        FormLinesChanged();
    }

    /// <summary>The form's mode, role or source changed: what it shows follows.</summary>
    internal void FormKindChanged()
    {
        _usageForm.RaiseKindChanged();
        _usageForm.Recompute();
    }

    /// <summary>The form's lines changed, or its source's were loaded: the batch weight, the other piece-size figure and the preview follow.</summary>
    internal void FormLinesChanged()
    {
        _usageForm.RaiseLinesChanged();
        _usageForm.FollowPieceSize();
    }

    private void ReplaceUsage(ProductUsageEdit old, ProductUsageEdit replacement)
    {
        var wasEditing = ReferenceEquals(old, _usageForm.EditingUsage);
        if (wasEditing)
        {
            CloseUsageForm();
        }

        Draft.ReplaceInRecipeOrder(old, replacement);
        RefreshLinkableOptions();
        _host.InvalidateCosts();

        // Its tab is its form, so the form opens again on what took its place.
        if (wasEditing)
        {
            OpenUsageForm(replacement);
        }
    }

    /// <summary>
    /// The recipe as one undo step keeps it (ProductFormSnapshot.Recipe): its sections, the tab showing, whether New
    /// Sub-Assembly's tab is there, and the section form, the section it edits by its place among the sections.
    /// </summary>
    internal RecipeSnapshot Capture() => new()
    {
        Usages = [.. UsageEdits],
        SelectedTab = CaptureTabChoice(),
        HasAddSubAssemblyTab = RecipeTabs.Contains(AddSubAssemblyTab),
        Form = new SectionFormSnapshot
        {
            IsOpen = _usageForm.IsUsageFormOpen,
            IsAdding = _usageForm.IsAddingUsage,
            Title = _usageForm.UsageFormTitle,
            Mode = _usageForm.EditingUsageMode,
            IsEditingLines = _usageForm.IsFormEditingLines,
            IsPickingSource = _usageForm.IsFormPickingSource,
            EditingUsageIndex = _usageForm.EditingUsage is { } editing && UsageEdits.IndexOf(editing) is var i and >= 0 ? i : null,
            RoleId = _usageForm.EditingUsageRole?.Id,
            Name = _usageForm.EditingUsageName,
            SourceKey = _usageForm.EditingUsageSource?.Key,
            Lines = [.. _usageForm.FormLines.Select(CaptureIngredientLine)],
            AddIns = [.. _usageForm.FormAddIns.Select(CaptureIngredientLine)],
            FormulaBase = _usageForm.FormFormulaBase,
            AuthoredField = _usageForm.AuthoredField,
            WeightPerPiece = _usageForm.EditingUsageWeightPerPiece,
            Yield = _usageForm.EditingUsageYield,
        },
    };

    private static IngredientLineSnapshot CaptureIngredientLine(IngredientLineRow row) =>
        new(row.IngredientId, row.Grams, row.IsFormulaBase, row.AuthoredBakersPercent);

    /// <summary>
    /// The first thing an undo does (the screen's RestoreSnapshot): the section form closes, with no row picked in its
    /// tables. EditingUsage has to be null before the sections are replaced: the tab mirror refills the form's lines from a
    /// section that replaces the one being edited, which would undo the restore.
    /// </summary>
    internal void CloseForRestore()
    {
        CloseUsageForm();
        _usageForm.SelectedFormLine = null;
        _usageForm.SelectedFormAddIn = null;
    }

    /// <summary>
    /// Puts the recipe back as <paramref name="snapshot"/> had it, for an undo, once the screen has put back the product's
    /// fields, the costing, the packaging and a composite's products: the sections and their tabs first
    /// (<see cref="RestoreSections"/>), then the form itself, last. The option lists are rebuilt before the pickers are put
    /// back, as a load does. Everything is written, never picked, so nothing a pick sets off (a source's lines loading over
    /// the very lines being restored, the piece size a role starts at) runs.
    /// </summary>
    internal void Restore(RecipeSnapshot snapshot)
    {
        RestoreSections(snapshot.Usages, snapshot.HasAddSubAssemblyTab, snapshot.SelectedTab);

        var form = snapshot.Form;
        _usageForm.IsAddingUsage = form.IsAdding;
        _usageForm.UsageFormTitle = form.Title;
        _usageForm.EditingUsageMode = form.Mode;
        _usageForm.IsFormEditingLines = form.IsEditingLines;
        _usageForm.IsFormPickingSource = form.IsPickingSource;

        RefreshLinkableOptions();
        RefreshFormRoleOptions();

        _usageForm.EditingUsageRole = form.RoleId is { } roleId ? _usageForm.FormRoleOptions.FirstOrDefault(r => r.Id == roleId) : null;
        _usageForm.EditingUsageSource = form.SourceKey is { } sourceKey ? _usageForm.LinkableSubAssemblies.FirstOrDefault(o => o.Key == sourceKey) : null;
        _usageForm.EditingUsageName = form.Name;

        _usageForm.FormFormulaBase = form.FormulaBase;
        RestoreIngredientLines(_usageForm.FormLines, form.Lines, form.FormulaBase);
        RestoreIngredientLines(_usageForm.FormAddIns, form.AddIns, form.FormulaBase);
        _usageForm.RestorePieceSize(form.AuthoredField, form.WeightPerPiece, form.Yield);

        _usageForm.EditingUsage = form.EditingUsageIndex is { } usageIndex && usageIndex < UsageEdits.Count
            ? UsageEdits[usageIndex]
            : null;
        _usageForm.IsUsageFormOpen = form.IsOpen;
    }

    private void RestoreIngredientLines(
        ObservableCollection<IngredientLineRow> rows, IReadOnlyList<IngredientLineSnapshot> states, decimal baseWeight)
    {
        rows.Clear();
        foreach (var state in states)
        {
            rows.Add(IngredientLineRow.Restore(
                id => _ingredients.FirstOrDefault(m => m.Id == id),
                baseWeight,
                state.IngredientId,
                state.Grams,
                state.IsFormulaBase,
                state.AuthoredBakersPercent));
        }
    }

    /// <summary>The tab showing, as an undo step keeps it (RecipeSnapshot.SelectedTab).</summary>
    private RecipeTabChoice? CaptureTabChoice() => SelectedRecipeTab switch
    {
        SubAssemblyRecipeTab tab => new RecipeTabChoice.Usage(RecipeTabs.OfType<SubAssemblyRecipeTab>().ToList().IndexOf(tab)),
        AddSubAssemblyRecipeTab => new RecipeTabChoice.AddSubAssembly(),
        PackagingRecipeTab => new RecipeTabChoice.Packaging(),
        ComponentsRecipeTab => new RecipeTabChoice.Components(),
        null => null,
    };

    /// <summary>
    /// An undo's sections and their tabs, put back. The tab is written, not shown (RecipeTabShown): showing it would apply
    /// the form of the tab being moved off, which is the very change being undone.
    /// </summary>
    private void RestoreSections(IReadOnlyList<ProductUsageEdit> usages, bool hasAddSubAssemblyTab, RecipeTabChoice? tab)
    {
        Draft.ReplaceUsageItems(usages);

        if (hasAddSubAssemblyTab)
        {
            InsertAddSubAssemblyTab();
        }
        else
        {
            RecipeTabs.Remove(AddSubAssemblyTab);
        }

        SelectedRecipeTab = tab switch
        {
            RecipeTabChoice.Usage(var index) => RecipeTabs.OfType<SubAssemblyRecipeTab>().ElementAtOrDefault(index),
            RecipeTabChoice.AddSubAssembly => RecipeTabs.Contains(AddSubAssemblyTab) ? AddSubAssemblyTab : null,
            RecipeTabChoice.Packaging => PackagingTab,
            RecipeTabChoice.Components => ComponentsTabIfShown,
            null => null,
        };

        // RecipeTabShown never ran, and it's the only other thing that writes this. Left stale, the next real
        // change of tab would think it was leaving a tab that's gone, and apply the wrong form on the way out.
        _shownRecipeTab = SelectedRecipeTab;
    }

}
