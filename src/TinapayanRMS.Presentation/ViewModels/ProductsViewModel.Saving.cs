using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using DynamicData;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;
using TinapayanRMS.Pricing.Versions;

namespace TinapayanRMS.Presentation.ViewModels;

// Save and Revert.
public sealed partial class ProductsViewModel
{
    /// <summary>Save and Revert.</summary>
    [MemberNotNull(nameof(_saveForm), nameof(SaveCommand), nameof(RevertCommand))]
    private void WireSaveCommands()
    {
        _saveForm = async () =>
        {
            // The picked product's details haven't replaced the previous one's yet: saving now would write that recipe
            // under this product's name. Save is off then, since the history records nothing until they come, so nothing
            // is unsaved (UndoHistory's canRecord); this is the backstop for an Execute that doesn't ask.
            if (!AreDetailsReady)
            {
                return;
            }

            // A form left open is part of what's being saved, unless it's an add form nothing was entered in, which
            // has nothing to save. Its sub-assembly's tab shows, as Apply would show it, so an add made this way
            // stays the tab showing through the reload.
            if (UsageForm.IsUsageFormOpen && !IsUsageFormUntouched())
            {
                SelectRecipeTab(TabFor(ApplyUsageForm()));
            }

            var product = new Product
            {
                Id = _editingId,
                Name = EditingName.Trim(),
                Description = string.IsNullOrWhiteSpace(EditingDescription) ? null : EditingDescription.Trim(),
                CategoryId = EditingCategory?.Id,
                QuantityRequired = EditingQuantityRequired,
                CurrentPrice = EditingCurrentPrice,

                // Only a new product's is written: a saved one's changes by moving it in the list.
                Stage = NewProductStage,
                FolderId = NewProductFolderId,

                // A new one's to say; a saved one's is the kind it was saved as, which the form never changes.
                IsComposite = IsEditingComposite,
                IncludesComponentPackaging = IsEditingComposite && EditingIncludesComponentPackaging,
            };

            var validation = _productValidator.Validate(product);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            var costing = Costing.ToParameters();
            var costingValidation = _costingParametersValidator.Validate(costing);
            if (!costingValidation.IsValid)
            {
                ErrorMessage = costingValidation.ToString(" ");
                return;
            }

            var recipe = CurrentRecipe();
            if (!await ConfirmSharedLinesAsync())
            {
                return;
            }

            // Written with its versions, all or nothing (VersionedSaves): a saved product with none yet first gets how it
            // stood until now, so there's one to go back to; then the product as saved; and the products sharing its
            // sections' lines or linking its packaging each get one if this Save changes them. Only linked lines edited
            // here and the packaging reach those, so a Save of neither reads none: a price-only Save would
            // otherwise spend half a second reading every product sharing its dough, twice.
            var packagingLink = Packaging.ChangedLink();
            var packagingLines = Packaging.ChangedLines();
            var bulkToSave = Packaging.ChangedBulk();
            var changesSharing = UsageEdits.Any(u => u.AreLinkedLinesEdited) || packagingLines is not null || packagingLink is not null;
            var recipeToSave = IsSameRecipe(recipe, _savedRecipe) ? null : recipe;
            var costingToSave = Costing.IsChanged ? costing : null;
            var saved = await Task.Run(() => _versionedSaves.SaveProductAsync(
                product, recipeToSave, packagingLink, packagingLines, bulkToSave, costingToSave, changesSharing, DateTime.Now, CancellationToken.None));

            // What's saved is the new starting point: stepping back past it would show changes already written.
            // Save's own reload keeps the product selected, so ClearDetails doesn't run and wouldn't clear this.
            History.Clear();

            // The list picks up the new name; then the form shows what was saved, which is what the next Save compares
            // against.
            await LoadCommand.Execute();
            ShowSavedProduct(saved.Product, saved.Detail);

            // Read again now if the History tab shows; otherwise the versions it holds are out of date, and go, so showing it
            // reads them, so a Save made from the Recipe tab doesn't leave the tab without the new version.
            if (SelectedDetailsTab == HistoryTab)
            {
                RequestVersions(force: true);
            }
            else
            {
                VersionHistory.Clear();
            }

            if (ProductVersionRecorder.RecordedMessage(saved.SharingVersioned) is { } message)
            {
                SuccessMessage = message;
            }
        };
        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSaveAsync(_saveForm), History.WhenAnyValue(h => h.IsDirty));

        // Revert puts the form back as the product was last saved (RevertToSavedAsync), once it's confirmed.
        RevertCommand = ReactiveCommand.CreateFromTask(() => RunWithConfirmationAsync(
            "Undo every change made since this product was last saved?",
            async () =>
            {
                await RevertToSavedAsync();
                if (SelectedProduct is { } product)
                {
                    SuccessMessage = $"{product.Name} was put back as it was last saved.";
                }
            }));
    }

    /// <summary>
    /// Asks before Save changes the lines of a linked sub-assembly that other products use too, or a made ingredient is made
    /// from, naming them as they are now rather than as last loaded. True, without asking, when nothing else would change.
    /// </summary>
    private async Task<bool> ConfirmSharedLinesAsync()
    {
        var edited = UsageEdits.Where(u => u.AreLinkedLinesEdited).ToList();
        if (edited.Count == 0)
        {
            return true;
        }

        var dependents = await _subAssemblyRepository.GetStandaloneLinkedByAsync(CancellationToken.None);
        var madeFrom = await _subAssemblyRepository.GetMadeIngredientsBySubAssemblyAsync(CancellationToken.None);
        var shared = edited
            .Select(u => (u.LinkedName, Others: (IReadOnlyList<string>)
            [
                .. OtherProductsUsing(u.SubAssemblyId, dependents),
                .. (madeFrom.GetValueOrDefault(u.SubAssemblyId) ?? []).Select(m => m.Name),
            ]))
            .Where(s => s.Others.Count > 0)
            .Select(s => $"{s.LinkedName}, also used by {string.Join(", ", s.Others)}")
            .ToList();
        if (shared.Count == 0)
        {
            return true;
        }

        return await ConfirmDelete.Handle(
            $"Save changes the lines of {string.Join("; and ", shared)}. Their recipes and costs change with it. Save anyway?");
    }

    /// <summary>
    /// The recipe as Save writes it: the new sections with their lines, and the sub-assemblies it links; and a composite's
    /// products, each with how many go in (a line half filled in is refused: ProductComponentRow.CompleteLines).
    /// </summary>
    private RecipeDraft CurrentRecipe() =>
        new(
            [.. UsageEdits.Where(u => u.IsEditable).Select(u => new NewSubAssemblyDraft(
                u.RoleId, [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], u.AuthoredField, u.AuthoredValue, u.Name)
            {
                // The product's own keeps its id, so Save rewrites it in place; a new section's is 0.
                Id = u.SubAssemblyId,
                Placement = u.ToMadeIngredient is { } made ? new SectionPlacement.MoveToInHouse(made)
                    : u.IsReleasing ? new SectionPlacement.MoveToSubAssemblies()
                    : u.IsOwned ? new SectionPlacement.ProductsOwn()
                    : new SectionPlacement.ByRole(),
            })],
            [.. UsageEdits.Where(u => u.IsLinked).Select(u => new LinkedUsageDraft(u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)
            {
                AddIns = [.. u.AddIns.Select(a => (a.Ingredient.Id, a.Grams))],

                // Only lines changed here are written, so a product's Save doesn't put back lines the Sub-Assemblies
                // screen has changed since.
                Lines = u.AreLinkedLinesEdited
                    ? [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))]
                    : null,
            })])
        {
            Components = IsEditingComposite ? [.. ProductComponentRow.CompleteLines(ComponentsTable.ComponentEdits).Select(c => (c.Product.Id, c.Quantity))] : [],
        };

    /// <summary>
    /// Whether two recipes would save the same: the same sub-assemblies, roles, lines, add-ins and piece
    /// sizes, and the same products at the same quantities, in the same order. A linked draft is compared field by
    /// field rather than as a record, as a new one already is: a record's own Equals compares its AddIns list by
    /// reference, which no two recipes built separately ever share, so Save would write the recipe every time.
    /// </summary>
    private static bool IsSameRecipe(RecipeDraft a, RecipeDraft b) =>
        a.Components.SequenceEqual(b.Components)
        && a.Linked.Count == b.Linked.Count
        && a.Linked.Zip(b.Linked).All(pair =>
            pair.First.SubAssemblyId == pair.Second.SubAssemblyId
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.AddIns.SequenceEqual(pair.Second.AddIns)
            && (pair.First.Lines is null) == (pair.Second.Lines is null)
            && (pair.First.Lines ?? []).SequenceEqual(pair.Second.Lines ?? []))
        && a.New.Count == b.New.Count
        && a.New.Zip(b.New).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Placement == pair.Second.Placement

            && pair.First.RoleId == pair.Second.RoleId
            && pair.First.SectionName == pair.Second.SectionName
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.Lines.SequenceEqual(pair.Second.Lines));

    /// <summary>
    /// Selects the product as Save left it, the fresh copy, so the form shows (and compares against) what was saved:
    /// <paramref name="saved"/> and <paramref name="detail"/>, as the Save read them back. A new product, saved for the
    /// first time, is still the recipe showing, not another product's: it keeps the tab it showed, as an existing one does.
    ///
    /// The detail goes in with the selection, so it shows in one pass. Picked with the detail still to read, the same
    /// product closed its form (ResetDetails) and drew the screen that way until the read came back and opened it again:
    /// a flicker on every Save.
    /// </summary>
    private void ShowSavedProduct(Product saved, ProductDetail detail)
    {
        _productCache.AddOrUpdate(saved);
        _detailProductId = saved.Id;
        _preloadedDetail = (saved.Id, detail);
        try
        {
            SelectedProduct = saved;
        }
        finally
        {
            _preloadedDetail = null;
        }
    }
}
