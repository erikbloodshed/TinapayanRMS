using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

// The section form: a sub-assembly added, linked, copied or edited, with its lines and add-ins.
public sealed partial class ProductsViewModel
{
    /// <summary>
    /// The section form: its way in, its lines priced as they're typed, and the sub-assembly picked to link or copy.
    /// </summary>
    private void WireUsageForm()
    {
        // The sub-assembly form: where its sub-assembly comes from, then the one it links or copies. The form starts
        // again empty in its new way, which is nothing to save yet, as StartAddingUsage's fresh form isn't. Picks only
        // (ProductUsageForm.ModePicks and the rest): an undo putting the form back, or the options rebuilt, isn't one.
        UsageForm.ModePicks
            .Where(_ => UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage)
            .Subscribe(_ =>
            {
                StartUsageFormMode();
                RebaseSettledForm();
            });

        // Every row put in a lines table, however it got there (a product read, an undo, a form opened), is priced at the
        // screen's costs, buffered or not; the add rows' factories see to a row still being typed.
        UsageForm.FormLines.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = CostBasis).Subscribe();
        UsageForm.FormAddIns.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = CostBasis).Subscribe();
        Packaging.LineEdits.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = CostBasis).Subscribe();
        Packaging.BulkEdits.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = CostBasis).Subscribe();

        // A line picked, typed or deleted in the form's table: it's priced, and the batch and preview follow.
        UsageForm.FormLines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .AutoRefresh(l => l.IsFormulaBase)
            .Subscribe(_ => _updates.Run(FormLinesEdited));

        // An add-in picked, typed or deleted: it's priced, and the batch it joins follows.
        UsageForm.FormAddIns.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ => _updates.Run(FormAddInsEdited));

        // The preview, and whether Save moves a new one to the Sub-Assemblies screen, follow the form's mode, role and source.
        this.WhenAnyValue(x => x.UsageForm.IsAddingUsage, x => x.UsageForm.EditingUsageMode, x => x.UsageForm.EditingUsageRole, x => x.UsageForm.EditingUsageSource)
            .Subscribe(_ => _updates.Run(FormKindChanged));

        LoadLatest(
            UsageForm.SourcePicks.Where(_ => UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage),
            LoadUsageSourceAsync);

        // A sub-assembly picked to link instead relinks the section once its lines are in; a later pick overtakes it.
        LoadLatest(
            UsageForm.LinkTargetPicks
                .Where(target => target is not null && UsageForm.IsChangingLink)
                .Select(target => target!),
            ChangeLinkAsync);

        // The add form's piece size starts as its role has it (StartingPieceSize), so it follows a role picked, or a
        // copy's taken, while it's still what the role before started it at: neither figure typed.
        UsageForm.RolePicks
            .Where(pick => UsageForm.IsUsageFormOpen && UsageForm.IsAddingUsage && IsPieceSizeAsStarted(pick.Before))
            .Subscribe(_ => StartPieceSize());
    }

    /// <summary>The section form's commands.</summary>
    [MemberNotNull(
        nameof(StartChangingLinkCommand), nameof(CancelChangingLinkCommand), nameof(RemoveFormLineCommand),
        nameof(RemoveFormAddInCommand))]
    private void WireUsageFormCommands()
    {
        StartChangingLinkCommand = ReactiveCommand.Create(StartChangingLink, this.WhenAnyValue(x => x.UsageForm.IsEditingLinkedUsage));
        CancelChangingLinkCommand = ReactiveCommand.Create(() => { UsageForm.IsChangingLink = false; });

        RemoveFormLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => UsageForm.FormLines.Remove(UsageForm.SelectedFormLine!)),
            this.WhenAnyValue(x => x.UsageForm.SelectedFormLine).Select(line => line is not null));

        RemoveFormAddInCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => UsageForm.FormAddIns.Remove(UsageForm.SelectedFormAddIn!)),
            this.WhenAnyValue(x => x.UsageForm.SelectedFormAddIn).Select(line => line is not null));
    }

    /// <summary>The add form, fresh for its mode: a new sub-assembly's name, role and lines, or the pick of another product's.</summary>
    private void StartUsageFormMode()
    {
        var mode = UsageForm.EditingUsageMode;
        UsageForm.IsFormEditingLines = mode != UsageFormMode.Link;
        UsageForm.IsFormPickingSource = mode != UsageFormMode.New;

        // The title is the one thing on the form that says which way in was picked, now that the picker that used to
        // say it is on Add Sub-Assembly's drop-down instead. Set here, not in StartAddingUsage, so it follows another
        // item picked while the form is open. A new one has none: its "Moves to Sub-Assemblies on Save" badge
        // (IsFormNewMovingToSubAssemblies) takes the title's place, and in a product-owned role nothing does.
        UsageForm.UsageFormTitle = mode switch
        {
            UsageFormMode.Link => "Link an existing sub-assembly",
            UsageFormMode.Copy => "Copy an existing sub-assembly",
            _ => string.Empty,
        };
        UsageForm.FormLines.Clear();
        UsageForm.FormAddIns.Clear();
        UsageForm.SelectedFormLine = null;
        UsageForm.SelectedFormAddIn = null;
        RefreshLinkableOptions();

        // The first role the recipe doesn't have yet, so sections can be added in role order without picking.
        var takenRoleIds = UsageEdits.Select(u => u.RoleId).ToHashSet();
        UsageForm.EditingUsageRole = UsageForm.FormRoleOptions.FirstOrDefault(r => !takenRoleIds.Contains(r.Id)) ?? UsageForm.FormRoleOptions.FirstOrDefault();
        UsageForm.EditingUsageName = null;
        UsageForm.EditingUsageSource = null;
        StartPieceSize();
    }

    /// <summary>
    /// The piece size the add form starts at in <paramref name="role"/>. A section made in a product-owned role
    /// (SubAssemblyRole.IsProductOwned), new or copied, is written for the one piece it goes into, so it starts at a
    /// yield of 1: its weight per piece is the whole batch, and follows the lines as they're typed. Any other starts
    /// with no size. So does a link, whose batch is the Sub-Assemblies screen's and shared.
    /// </summary>
    private (UsageAuthoredField Field, decimal Value) StartingPieceSize(SubAssemblyRole? role) =>
        UsageForm.EditingUsageMode != UsageFormMode.Link && role is { IsProductOwned: true }
            ? (UsageAuthoredField.Yield, 1m)
            : (UsageAuthoredField.WeightPerPiece, 0m);

    /// <summary>Shows the piece size the add form's role starts at.</summary>
    private void StartPieceSize()
    {
        var (field, value) = StartingPieceSize(UsageForm.EditingUsageRole);
        UsageForm.ShowPieceSize(field, value);
    }

    /// <summary>
    /// Whether the add form's piece size is still the one <paramref name="role"/> starts it at, with neither figure
    /// typed. Worked out from the form, not remembered, so it holds for a form an undo put back. No size is no size,
    /// whichever box it was left in.
    /// </summary>
    private bool IsPieceSizeAsStarted(SubAssemblyRole? role)
    {
        var (field, value) = StartingPieceSize(role);
        return UsageForm.AuthoredValue == value && (value == 0 || UsageForm.AuthoredField == field);
    }

    /// <summary>The badge's Edit: the picker, holding what the section could link instead, in the badge's place.</summary>
    private void StartChangingLink()
    {
        var usedIds = UsageEdits.Select(u => u.SubAssemblyId).ToHashSet();
        UsageForm.ChangeLinkOptions.MergeFrom(
            [.. _linkable
                .Where(s => s.OwnerProductId is null && s.Role?.IsPackaging != true && !usedIds.Contains(s.Id))
                .Select(s => new LinkableSubAssemblyOption(s))],
            o => o.Key);

        if (UsageForm.ChangeLinkOptions.Count == 0)
        {
            ErrorMessage = "There's no other sub-assembly to link: every one on the Sub-Assemblies screen is in this recipe already.";
            return;
        }

        ErrorMessage = null;
        UsageForm.ChangeLinkTarget = null;
        UsageForm.IsChangingLink = true;
    }

    /// <summary>
    /// Links <paramref name="target"/> in place of the linked section the form shows, as one undo step, keeping what's
    /// this product's: the piece size as the form has it, and the add-ins, less any the new recipe
    /// already has (a toast names them). Lines changed here but not saved go with the old link. The section keeps its
    /// tab, which takes the new one's role and name; Save writes the new link.
    /// </summary>
    private async Task ChangeLinkAsync(LinkableSubAssemblyOption target, CancellationToken cancellationToken)
    {
        if (UsageForm.EditingUsage is not { IsLinked: true } linked || target.SubAssembly is not { } subAssembly)
        {
            return;
        }

        var saved = await _subAssemblyLineRepository.GetForSubAssembliesAsync([subAssembly.Id], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // The form moved on while the lines loaded: another section, or the change was cancelled.
        if (!UsageForm.IsChangingLink || !ReferenceEquals(UsageForm.EditingUsage, linked) || !ReferenceEquals(UsageForm.ChangeLinkTarget, target))
        {
            return;
        }

        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        IReadOnlyList<SectionLine> lines =
            [.. saved.Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
        var role = Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
        try
        {
            ErrorMessage = null;
            if (lines.Count == 0)
            {
                throw new BusinessRuleException($"{subAssembly.Name} has no ingredients yet. Add them on the Sub-Assemblies screen first.");
            }

            var lineIngredientIds = lines.Select(l => l.Ingredient.Id).ToHashSet();
            var addIns = IngredientLineRow.CompleteLines(UsageForm.FormAddIns, subAssembly.Name);
            var dropped = addIns.Where(a => lineIngredientIds.Contains(a.Ingredient.Id)).Select(a => a.Ingredient.Name).ToList();
            var replacement = new ProductUsageEdit(
                subAssembly.Id,
                role,
                lines,
                UsageForm.AuthoredField,
                UsageForm.AuthoredValue,
                subAssembly.Name,
                [.. addIns.Where(a => !lineIngredientIds.Contains(a.Ingredient.Id)).Select(a => new SectionLine(a.Ingredient, a.Grams))],
                SectionLabelOf(subAssembly.Name, role));
            EnsureSectionsDistinct(replacement, linked);

            AsUndoStep(() => ReplaceUsage(linked, replacement));
            if (dropped.Count > 0)
            {
                SuccessMessage = $"Now linked to {subAssembly.Name}. {string.Join(", ", dropped)} left the add-ins: {subAssembly.Name} already has {(dropped.Count == 1 ? "it" : "them")}.";
            }
        }
        catch (BusinessRuleException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// After a sub-assembly was picked to link or copy: its lines, for the batch weight, and for a copy
    /// also its role, all editable from then on.
    /// </summary>
    private async Task LoadUsageSourceAsync(LinkableSubAssemblyOption? source, CancellationToken cancellationToken)
    {
        IReadOnlyList<SectionLine> lines = [];
        if (source?.SubAssembly is { } subAssembly)
        {
            var saved = await _subAssemblyLineRepository.GetForSubAssembliesAsync([subAssembly.Id], cancellationToken);
            var ingredientsById = Ingredients.ToDictionary(m => m.Id);
            lines = [.. saved.Select(l => new SectionLine(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams))];
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The form moved on while the lines loaded (applied, cancelled, or another pick): they're stale.
        if (!UsageForm.IsUsageFormOpen || !UsageForm.IsAddingUsage || !ReferenceEquals(UsageForm.EditingUsageSource, source))
        {
            return;
        }

        ShowFormLines(lines);

        // A copy takes its source's role. Not its name, which is another product's and role's ("Cheese Roll Filling:
        // Cheese"): Save names the copy after this product.
        if (UsageForm.EditingUsageMode == UsageFormMode.Copy && source is not null)
        {
            UsageForm.EditingUsageRole = UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == source.SubAssembly!.RoleId) ?? UsageForm.EditingUsageRole;
            UsageForm.EditingUsageName = null;
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
        ErrorMessage = null;
        var categories = await MadeIngredientPrompt.MadeCategoriesAsync(_rawMaterialCategoryRepository);
        if (categories.Count == 0)
        {
            ErrorMessage = MadeIngredientPrompt.NoMadeCategoryMessage;
            return;
        }

        var wasEditing = ReferenceEquals(usage, UsageForm.EditingUsage);
        var initialName = SubAssemblyRules.StandaloneNameFor(EditingName.Trim(), usage.RoleName, wasEditing ? UsageForm.EditingUsageName : usage.Name);
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
        if (Ingredients.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            || UsageEdits.Any(u => !ReferenceEquals(u, usage) && string.Equals(u.ToMadeIngredient?.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = MadeIngredientPrompt.NameTakenMessage(name);
            return;
        }

        var made = new MadeIngredientDraft(name, categories[answer.Choice].Id);
        AsUndoStep(() =>
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
                    ErrorMessage = ex.Message;
                    return;
                }
            }

            var moving = applied.MovedToInHouse(made);
            ReplaceInRecipeOrder(applied, moving);
            RecomputeCosts();
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
        var authoredValue = UsageForm.AuthoredValue;
        ProductUsageEdit usage;

        if (UsageForm.EditingUsage is { IsLinked: true } linked)
        {
            // Its lines are typed into as a new section's are, and Save writes them where it's kept.
            var lines = IngredientLineRow.CompleteLines(UsageForm.FormLines, linked.DisplayName);
            if (lines.Count == 0)
            {
                throw new BusinessRuleException($"Add the ingredients that go into {linked.DisplayName}, in grams.");
            }

            IReadOnlyList<SectionLine> lineEdits = [.. lines.Select(l => new SectionLine(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
            usage = linked.WithLinkedForm(
                lineEdits, UsageForm.AuthoredField, authoredValue, CompleteAddIns(linked.DisplayName, lineEdits));
        }
        else if (UsageForm.IsFormEditingLines)
        {
            var role = UsageForm.EditingUsageRole
                ?? throw new BusinessRuleException("Pick the sub-assembly's role. It's what the sub-assembly is called.");

            var lines = IngredientLineRow.CompleteLines(UsageForm.FormLines, role.Name);
            if (lines.Count == 0)
            {
                throw new BusinessRuleException($"Add the ingredients that go into the {role.Name}, in grams.");
            }

            // One of the product's own keeps its id, so Save rewrites it in place; a new one has none yet. One unlinked
            // and not saved yet stays the product's own, and one on its way to the Sub-Assemblies screen stays so.
            usage = new ProductUsageEdit(
                UsageForm.EditingUsage is { IsOwned: true } own ? own.SubAssemblyId : 0, role, [.. lines.Select(l => new SectionLine(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], UsageForm.AuthoredField, authoredValue, linkedName: null, name: UsageForm.EditingUsageName, isOwn: UsageForm.EditingUsage?.IsOwned == true, isReleasing: UsageForm.EditingUsage?.IsReleasing == true, toMadeIngredient: UsageForm.EditingUsage?.ToMadeIngredient);
        }
        else
        {
            var source = UsageForm.EditingUsageSource?.SubAssembly
                ?? throw new BusinessRuleException("Pick the sub-assembly to link, or cancel the form.");
            var sourceName = source.Name;

            // The table's read-only lines are the ones LoadUsageSourceAsync loaded for this very pick; a pick
            // whose lines are still on their way has none yet, and linking it would make a batch of nothing.
            if (UsageForm.FormLines.Count == 0)
            {
                throw new BusinessRuleException($"{sourceName} is still loading its ingredients. Try again in a moment.");
            }

            IReadOnlyList<SectionLine> sourceLines =
                [.. UsageForm.FormLines.Where(l => l.Ingredient is not null).Select(l => new SectionLine(l.Ingredient!, l.Grams ?? 0m, l.IsFormulaBase, l.AuthoredBakersPercent))];

            var role = Roles.FirstOrDefault(r => r.Id == source.RoleId) ?? source.Role;
            usage = new ProductUsageEdit(
                source.Id,
                role,
                sourceLines,
                UsageForm.AuthoredField,
                authoredValue,
                source.Name,
                CompleteAddIns(sourceName, sourceLines),
                SectionLabelOf(source.Name, role));
        }

        if (UsageForm.EditingUsage is { } unchanged && usage.ShowsSameAs(unchanged))
        {
            CloseUsageForm();
            return unchanged;
        }

        // Sections in one role told apart by their names. A linked one being edited keeps its role and name, which are
        // the Sub-Assemblies screen's to change; and a section left as it was returned above, so it can still be looked
        // through, and Save says what's wrong when it's next changed.
        if (UsageForm.EditingUsage is not { IsLinked: true })
        {
            EnsureSectionsDistinct(usage, UsageForm.EditingUsage);
        }

        if (UsageForm.EditingUsage is { } editing)
        {
            ReplaceInRecipeOrder(editing, usage);
        }
        else
        {
            InsertInRecipeOrder(usage);
        }

        CloseUsageForm();
        RefreshLinkableOptions();
        RecomputeCosts();
        return usage;
    }

    /// <summary>
    /// Applies the form of a tab being left, so nothing typed into it is lost (the grids commit the same way when
    /// focus leaves them). False when a rule refused it: the message shows and the caller stays on the tab.
    /// </summary>
    private bool CommitUsageFormOnLeave()
    {
        if (!UsageForm.IsUsageFormOpen)
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
        History.Begin();
        try
        {
            ErrorMessage = null;
            ApplyUsageForm();
            return true;
        }
        catch (BusinessRuleException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            History.End((before, now) => before.WithFormOf(now));
        }
    }

    /// <summary>
    /// Applies a form left open, as Save does, so what's typed into it is in the recipe, and shows its section's tab with
    /// the form open on it again. One undo step, measured as leaving its tab measures one: by what it did to the recipe.
    /// An add form nothing was entered in is left as it is. A rule refusing the form throws, and the form stays as typed.
    /// </summary>
    private void ApplyOpenForm()
    {
        if (!UsageForm.IsUsageFormOpen || IsUsageFormUntouched())
        {
            return;
        }

        History.Begin();
        try
        {
            SelectRecipeTab(TabFor(ApplyUsageForm()));
        }
        finally
        {
            History.End((before, now) => before.WithFormOf(now));
        }
    }

    /// <summary>
    /// The add-ins table as lines, refused if one is an ingredient <paramref name="baseLines"/> already has.
    /// The repository checks the same rule as it writes them; this is what puts the message on the screen
    /// the moment Apply is pressed, or the tab is left.
    /// </summary>
    private IReadOnlyList<SectionLine> CompleteAddIns(string subAssemblyName, IReadOnlyList<SectionLine> baseLines)
    {
        var addIns = IngredientLineRow.CompleteLines(UsageForm.FormAddIns, subAssemblyName);
        ProductRules.EnsureValidAddIns(
            [.. addIns.Select(a => (a.Ingredient.Id, a.Grams, a.Ingredient.Name))],
            subAssemblyName,
            [.. baseLines.Select(l => l.Ingredient.Id)]);

        return [.. addIns.Select(a => new SectionLine(a.Ingredient, a.Grams))];
    }

    /// <summary>
    /// Refuses <paramref name="usage"/> in place of <paramref name="replacing"/> when the recipe could no longer tell its
    /// sections apart: two in one role by the same name, or both without one (SubAssemblyRules.EnsureSectionsDistinct),
    /// or a name too long. SetRecipeAsync checks the same as it saves; this is what puts the message up the moment
    /// Apply is pressed, or the tab is left.
    /// </summary>
    private void EnsureSectionsDistinct(ProductUsageEdit usage, ProductUsageEdit? replacing)
    {
        List<ProductUsageEdit> sections = [.. UsageEdits.Where(u => !ReferenceEquals(u, replacing)), usage];
        SubAssemblyRules.EnsureSectionsDistinct([.. sections.Select(u => (u.RoleId, u.RoleName, u.Label))]);
        SubAssemblyRules.EnsureValidSectionName(usage.IsEditable ? usage.Name : null);
    }

    /// <summary>
    /// Tells each section whether the recipe has others in its role, so it's called by its name too ("Filling: Ube"),
    /// and its tab reads so. Run whenever the recipe's sections change.
    /// </summary>
    private void MarkRepeatedRoles()
    {
        var countsByRole = UsageEdits.CountBy(u => u.RoleId).ToDictionary();
        foreach (var usage in UsageEdits)
        {
            usage.IsRoleRepeated = countsByRole[usage.RoleId] > 1;
        }

        foreach (var tab in RecipeTabs.OfType<SubAssemblyRecipeTab>())
        {
            tab.RefreshHeader();
        }
    }

    private void CloseUsageForm()
    {
        UsageForm.IsUsageFormOpen = false;
        UsageForm.EditingUsage = null;
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
    private void RebaseSettledForm()
    {
        if (!IsBusyApplying)
        {
            History.Rebase((settled, now) => settled.WithFormOf(now));
        }
    }

    /// <summary>The form on <paramref name="usage"/>, as it was last applied: the whole of its tab.</summary>
    private void OpenUsageForm(ProductUsageEdit usage)
    {
        UsageForm.EditingUsage = usage;
        UsageForm.IsAddingUsage = false;
        UsageForm.UsageFormTitle = usage.IsEditable ? $"Edit {usage.SectionName}" : $"Edit {usage.SectionName} ({usage.LinkedFromText})";
        UsageForm.IsFormEditingLines = usage.IsEditable;
        UsageForm.IsFormPickingSource = false;
        UsageForm.EditingUsageSource = null;
        RefreshFormRoleOptions();
        UsageForm.EditingUsageRole = UsageForm.FormRoleOptions.FirstOrDefault(r => r.Id == usage.RoleId) ?? usage.Role;
        UsageForm.EditingUsageName = usage.Name;

        // A linked one's lines show too, read-only: they're the Sub-Assemblies screen's. What is this product's is its piece
        // size and its add-ins, which go in the table under them, open if there are any.
        ShowFormLines(usage.Lines);
        ShowFormAddIns(usage.AddIns);
        UsageForm.SelectedFormLine = null;
        UsageForm.SelectedFormAddIn = null;
        UsageForm.ShowPieceSize(usage.AuthoredField, usage.AuthoredValue);
        UsageForm.IsUsageFormOpen = true;
        RebaseSettledForm();
    }

    /// <summary>
    /// A fresh form to add a sub-assembly, in place of any the form had open. It starts in New, and AddUsageCommand
    /// sets the way its drop-down item asked for straight after, which starts the form again in it.
    /// </summary>
    private void StartAddingUsage()
    {
        UsageForm.EditingUsage = null;
        UsageForm.IsAddingUsage = true;
        UsageForm.EditingUsageMode = UsageFormMode.New;
        UsageForm.IsUsageFormOpen = true;
        StartUsageFormMode();
        RebaseSettledForm();
    }

    /// <summary>
    /// The add form as it opened: nothing picked to link or copy, no line, and the piece size it started at. Its role
    /// alone isn't an entry.
    /// </summary>
    private bool IsUsageFormUntouched() =>
        UsageForm.IsAddingUsage
        && UsageForm.EditingUsageSource is null
        && string.IsNullOrWhiteSpace(UsageForm.EditingUsageName)
        && UsageForm.FormLines.All(l => l.IngredientId is null && l.Grams == 0)
        && UsageForm.FormAddIns.All(l => l.IngredientId is null && l.Grams == 0)
        && IsPieceSizeAsStarted(UsageForm.EditingUsageRole);

    /// <summary>The screen's derived updates, run as their inputs change, or once at the end of a restore.</summary>
    private readonly DerivedUpdates _updates = new();

    /// <summary>A line picked, typed or deleted in the form's table: it's priced, and the batch and preview follow.</summary>
    private void FormLinesEdited()
    {
        IngredientLineRow.Resolve(UsageForm.FormLines, Ingredients.ToDictionary(m => m.Id));
        FormLinesChanged();
    }

    /// <summary>An add-in picked, typed or deleted: it's priced, and the batch it joins follows.</summary>
    private void FormAddInsEdited()
    {
        IngredientLineRow.Resolve(UsageForm.FormAddIns, Ingredients.ToDictionary(m => m.Id));
        FormLinesChanged();
    }

    /// <summary>The form's mode, role or source changed: what it shows follows.</summary>
    private void FormKindChanged()
    {
        UsageForm.RaiseKindChanged();
        UsageForm.Recompute();
    }

    /// <summary>A packaging line picked, typed or deleted in its table: it's priced, and the product's costs follow.</summary>
    private void PackagingLinesEdited()
    {
        PackagingLineRow.Resolve(Packaging.AllLineEdits, PackagingMaterials.ToDictionary(m => m.Id));
        Packaging.RaiseCostsChanged();
        RecomputeCosts();
    }

    /// <summary>
    /// A composite's line picked, typed or deleted in its Products table: it's priced once its product's recipe cost has
    /// been read (a new pick's is read then), and the composite's costs follow.
    /// </summary>
    private void ComponentsEdited()
    {
        ComponentsTable.ResolveRows();
        ComponentsTable.ReadMissingCosts();
        RecomputeCosts();
    }

    /// <summary>The form's lines changed, or its source's were loaded: the batch weight, the other piece-size figure and the preview follow.</summary>
    private void FormLinesChanged()
    {
        UsageForm.RaiseLinesChanged();
        UsageForm.FollowPieceSize();
    }

    private void InsertInRecipeOrder(ProductUsageEdit usage)
    {
        var index = 0;
        while (index < UsageEdits.Count && ProductUsageEdit.RecipeOrder.Compare(UsageEdits[index], usage) <= 0)
        {
            index++;
        }

        UsageEdits.Insert(index, usage);
    }

    private void ReplaceUsage(ProductUsageEdit old, ProductUsageEdit replacement)
    {
        var wasEditing = ReferenceEquals(old, UsageForm.EditingUsage);
        if (wasEditing)
        {
            CloseUsageForm();
        }

        ReplaceInRecipeOrder(old, replacement);
        RefreshLinkableOptions();
        RecomputeCosts();

        // Its tab is its form, so the form opens again on what took its place.
        if (wasEditing)
        {
            OpenUsageForm(replacement);
        }
    }

    /// <summary>
    /// Puts <paramref name="replacement"/> in <paramref name="old"/>'s place, then moves it to where it sorts
    /// (a new name or role can move it). Replacing rather than removing keeps its tab, and the tab showing.
    /// </summary>
    private void ReplaceInRecipeOrder(ProductUsageEdit old, ProductUsageEdit replacement)
    {
        var index = UsageEdits.IndexOf(old);
        if (index < 0)
        {
            InsertInRecipeOrder(replacement);
            return;
        }

        UsageEdits[index] = replacement;
        var sortedIndex = UsageEdits.Count(u => !ReferenceEquals(u, replacement) && ProductUsageEdit.RecipeOrder.Compare(u, replacement) <= 0);
        if (sortedIndex != index)
        {
            UsageEdits.Move(index, sortedIndex);
        }
    }
}
