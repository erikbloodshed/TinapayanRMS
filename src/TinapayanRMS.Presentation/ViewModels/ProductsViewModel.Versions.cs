using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

// The Clone and History tabs: a product's versions, and putting one back in the form.
public sealed partial class ProductsViewModel
{
    /// <summary>The Clone and History tabs, a saved product's only, and the versions History reads.</summary>
    [MemberNotNull(nameof(_showLinks))]
    private void WireDetailsTabs()
    {
        // A hidden tab can't stay the one showing: leaving a saved product while on Clone or History goes
        // back to Recipe. Subscribed before ShowLinks, so the tab strip moves before the tab hides; a
        // TabControlExt whose selected tab hides picks a neighbour itself.
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is null && SelectedDetailsTab is LinksTab or HistoryTab)
            .Subscribe(_ => SelectedDetailsTab = RecipeTab);

        // The History tab reads the product's versions while it shows, once per product: as it's picked, and as another
        // product's details arrive under it. A Save reads them again (RequestVersions(force: true)).
        this.WhenAnyValue(x => x.SelectedDetailsTab, x => x.AreDetailsReady, x => x.SelectedProduct)
            .Subscribe(_ => RequestVersions());
        LoadLatest(_versionReads, (productId, cancellationToken) => VersionHistory.ReadAsync(
            productId, () => SelectedProduct?.Id == productId, cancellationToken));

        _showLinks =this.WhenAnyValue(x => x.SelectedProduct)
            .Select(product => product is not null)
            .ToProperty(this, x => x.ShowLinks);
    }

    /// <summary>Restore, from the History tab.</summary>
    [MemberNotNull(nameof(RestoreVersionCommand))]
    private void WireVersionCommands()
    {
        // Restore puts the picked version into the form, as Revert puts the saved product there, but as an undo step of
        // unsaved edits rather than by reloading: Save keeps it, and Undo takes it back.
        RestoreVersionCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(() => VersionHistory.SelectedRow is { } row ? RestoreVersionAsync(row) : Task.CompletedTask),
            VersionHistory.WhenAnyValue(h => h.SelectedRow).Select(row => row is not null));
    }

    /// <summary>
    /// Reads the picked product's versions for the History tab, when it shows and they aren't showing already;
    /// <paramref name="force"/> reads them again anyway (a Save has just recorded one).
    /// </summary>
    private void RequestVersions(bool force = false)
    {
        if (SelectedDetailsTab == HistoryTab
            && AreDetailsReady
            && SelectedProduct is { } product
            && product.Id == _detailProductId
            && (force || VersionHistory.ProductId != product.Id))
        {
            _versionReads.OnNext(product.Id);
        }
    }

    /// <summary>
    /// Puts <paramref name="row"/>'s version into the form as unsaved edits, in one undo step: its sections, packaging,
    /// products, costing percentages, quantity required, current price and whether a composite includes its products'
    /// packaging. The name, description, category and stage stay. A section of the product's own keeps its sub-assembly
    /// when the product still owns it; a linked one is linked again as it is now, since Save never writes a version's lines
    /// into a sub-assembly others may share; one deleted since comes back as a new section with the version's lines (its
    /// add-ins among them). Packaging links what it linked while that's still there, else takes the lines it showed as the
    /// product's own. Refused, with nothing changed, while a material, role or product it needs is gone.
    /// </summary>
    private async Task RestoreVersionAsync(ProductVersionRow row)
    {
        if (!AreDetailsReady || SelectedProduct is not { } product || VersionHistory.ProductId != product.Id)
        {
            return;
        }

        var content = row.Content;
        if (content.IsComposite != product.IsComposite)
        {
            throw new BusinessRuleException($"{row.Title} is of another kind of product, so it can't be restored here.");
        }

        // What it linked, as it is now: its linked sections' sub-assemblies and its packaging set, and their lines.
        IReadOnlyCollection<int> linkedIds =
        [
            .. content.Sections.Where(s => !s.IsOwn).Select(s => s.SubAssemblyId)
                .Concat(content.PackagingLink?.SubAssemblyId is { } linkedSetId ? [linkedSetId] : Enumerable.Empty<int>())
                .Distinct(),
        ];
        var subAssembliesRead = Task.Run(() => _subAssemblyRepository.GetByIdsAsync(linkedIds, CancellationToken.None));
        var linesRead = Task.Run(() => _subAssemblyLineRepository.GetForSubAssembliesAsync(linkedIds, CancellationToken.None));
        await Task.WhenAll(subAssembliesRead, linesRead);
        var subAssembliesNow = (await subAssembliesRead).ToDictionary(s => s.Id);
        var linesNow = (await linesRead).ToLookup(l => l.SubAssemblyId);

        PackagingLinkTarget? link = null;
        if (content.PackagingLink is { } versionLink)
        {
            if (versionLink.SubAssemblyId is { } setId && subAssembliesNow.TryGetValue(setId, out var set))
            {
                link = new PackagingLinkTarget.Set(setId, set.Name);
            }
            else if (versionLink.ProductId is { } linkedProductId && Products.FirstOrDefault(p => p.Id == linkedProductId) is { } linkedProduct)
            {
                link = new PackagingLinkTarget.OtherProduct(linkedProductId, linkedProduct.Name);
            }
        }

        // Everything it needs that's gone, named, before anything changes.
        var materialNames = content.Materials.ToDictionary(m => m.Id, m => m.Name);
        string MaterialName(int id) => materialNames.GetValueOrDefault(id) ?? $"Material {id}";
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        var packagingById = PackagingMaterials.ToDictionary(m => m.Id);
        bool IsRelinked(VersionSection section) => !section.IsOwn && subAssembliesNow.ContainsKey(section.SubAssemblyId);
        List<string> missing = [];
        foreach (var section in content.Sections)
        {
            var relinked = IsRelinked(section);
            if (!relinked && Roles.All(r => r.Id != section.Role.Id))
            {
                missing.Add($"the {section.Role.Name} role");
            }

            missing.AddRange((relinked ? Enumerable.Empty<int>() : section.Lines.Select(l => l.IngredientId))
                .Concat(section.AddIns.Select(a => a.IngredientId))
                .Where(id => !ingredientsById.ContainsKey(id))
                .Select(MaterialName));
        }

        // The lines it owned, unless a link that's still there shows them, and its bulk packaging, its own either way.
        missing.AddRange(content.PackagingLines
            .Where(l => (link is null || l.IsBulk) && !packagingById.ContainsKey(l.MaterialId))
            .Select(l => MaterialName(l.MaterialId)));

        missing.AddRange(content.Components.Where(c => Products.All(p => p.Id != c.ProductId)).Select(c => c.ProductName));
        if (missing.Distinct().ToList() is { Count: > 0 } gone)
        {
            throw new BusinessRuleException(
                $"{row.Title} can't be restored: it needs {string.Join(", ", gone)}, which {(gone.Count == 1 ? "is" : "are")} no longer there.");
        }

        // The lines a link shows: those showing already when it's the link the form has, else as it has them now. The bulk
        // packaging is the version's own, whatever it linked.
        IReadOnlyList<PackagingLineState> packagingLines;
        IReadOnlyList<PackagingLineState> bulkLines = [.. content.PackagingLines.Where(l => l.IsBulk).Select(l => OwnPackagingLineOf(l, content, packagingById))];
        if (link is null)
        {
            packagingLines = [.. content.PackagingLines.Where(l => !l.IsBulk).Select(l => OwnPackagingLineOf(l, content, packagingById))];
        }
        else if (link.Key == Packaging.LinkTarget?.Key)
        {
            packagingLines = [.. Packaging.LineEdits.Select(CaptureLine)];
        }
        else
        {
            packagingLines = link switch
            {
                PackagingLinkTarget.Set(var setId, _) => [.. (await Task.Run(() => _subAssemblyRepository.GetPackagingLinesAsync(setId, CancellationToken.None)))
                    .Select(l => new PackagingLineState(l.PackagingMaterialId, l.UnitId, l.QtyPerSet))],
                PackagingLinkTarget.OtherProduct(var linkedProductId, _) => [.. (await Task.Run(() => _packagingLineRepository.GetEffectiveForProductAsync(linkedProductId, CancellationToken.None)))
                    .Select(l => PackagingLineRow.FromSaved(l))
                    .Select(CaptureLine)],
            };
        }

        // Another product picked while that was read: the version isn't this one's.
        if (SelectedProduct?.Id != product.Id || !AreDetailsReady)
        {
            return;
        }

        HashSet<int> ownedNow = [.. _savedRecipe.New.Select(n => n.Id).Where(id => id != 0)];
        List<string> changedSinceLinks = [];
        List<ProductUsageEdit> sections = [];
        foreach (var section in content.Sections)
        {
            if (IsRelinked(section))
            {
                var linked = subAssembliesNow[section.SubAssemblyId];
                var current = linesNow[linked.Id].ToList();
                if (!HasSameLines(current, section.Lines))
                {
                    changedSinceLinks.Add(linked.Name);
                }

                var linkedRole = Roles.FirstOrDefault(r => r.Id == linked.RoleId) ?? linked.Role;
                sections.Add(new ProductUsageEdit(
                    linked.Id,
                    linkedRole,
                    [.. current.Select(l => new SectionLine(
                        ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    section.AuthoredField,
                    section.AuthoredValue,
                    linked.Name,
                    [.. section.AddIns.Select(a => new SectionLine(ingredientsById[a.IngredientId], a.Grams))],
                    SectionLabelOf(linked.Name, linkedRole),
                    linkedOwner: PricedSection.LinkedOwnerOf(linked)));
                continue;
            }

            // The product's own, or a linked one deleted since: the version's lines, and a linked one's add-ins with them,
            // since a section of its own has none.
            var keepsId = section.IsOwn && ownedNow.Contains(section.SubAssemblyId);
            sections.Add(new ProductUsageEdit(
                keepsId ? section.SubAssemblyId : 0,
                Roles.First(r => r.Id == section.Role.Id),
                [
                    .. section.Lines.Select(l => new SectionLine(ingredientsById[l.IngredientId], l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)),
                    .. section.AddIns.Select(a => new SectionLine(ingredientsById[a.IngredientId], a.Grams)),
                ],
                section.AuthoredField,
                section.AuthoredValue,
                linkedName: null,
                name: section.LabelIn(content.ProductName, content.Schema),
                isOwn: section.IsOwn && !keepsId));
        }

        sections.Sort(ProductUsageEdit.RecipeOrder);
        var costing = content.Costing ?? new VersionCosting(0m, 0m, 0m, 0m);
        AsUndoStep(() =>
        {
            // The form goes, as any restore closes it: what it held comes back with Undo.
            CloseUsageForm();
            RestoreSnapshot(
                CaptureSnapshot() with
                {
                    CurrentPrice = content.CurrentPrice,
                    QuantityRequired = content.QuantityRequired,
                    IncludesComponentPackaging = content.IncludesComponentPackaging,
                    Costing = (costing.LaborPercent, costing.OverheadPercent, costing.AdministrativePercent, costing.MarginPercent),
                    Usages = sections,
                    SelectedTab = sections.Count > 0 ? new RecipeTabChoice.Usage(0)
                        : product.IsComposite ? new RecipeTabChoice.Components()
                        : new RecipeTabChoice.Packaging(),
                    HasAddSubAssemblyTab = false,
                    PackagingLink = link,
                    IsPackagingMoving = false,
                    Lines = packagingLines,
                    BulkLines = bulkLines,
                    BulkQuantity = bulkLines.Count > 0 ? content.BulkPackagingQuantity : null,
                    Components = [.. content.Components.Select(c => new ComponentLineState(c.ProductId, c.Quantity))],
                    IsUsageFormOpen = false,
                    IsAddingUsage = false,
                    EditingUsageIndex = null,
                    FormLines = [],
                    FormAddIns = [],
                },
                null);

            // The section tab showing opens its form, as showing it would: the restore puts tabs back without opening any,
            // and a section's tab with its form closed showed an empty form (no lines, a 0 g batch).
            if (SelectedRecipeTab is SubAssemblyRecipeTab { Usage: var shown })
            {
                OpenUsageForm(shown);
            }

            // The form closing and opening isn't the user's change, as it isn't when a tab change does it: only the recipe,
            // packaging, costing and price the version put back count for Save. Without this, a version the same as the
            // saved product (one whose only difference is a linked sub-assembly's lines, kept as they are now) left Save lit.
            RebaseSettledForm();
        });

        List<string> notes = [$"{row.Title} is in the form. Save to keep it, or Undo to put the form back."];
        if (changedSinceLinks.Count > 0)
        {
            notes.Add($"{string.Join(", ", changedSinceLinks)} {(changedSinceLinks.Count == 1 ? "has" : "have")} changed on Sub-Assemblies since, and {(changedSinceLinks.Count == 1 ? "is" : "are")} linked as {(changedSinceLinks.Count == 1 ? "it is" : "they are")} now.");
        }

        if (content.PackagingLink is { } wasLinked && link is null)
        {
            notes.Add($"{wasLinked.Name} is gone, so the packaging it showed comes back as the product's own.");
        }

        SuccessMessage = string.Join(" ", notes);
    }

    /// <summary>
    /// A version's packaging line as the product's own: in the unit it was in, or in base units when the material has lost
    /// that unit since.
    /// </summary>
    private static PackagingLineState OwnPackagingLineOf(
        VersionPackagingLine line, ProductVersionContent content, Dictionary<int, RawMaterial> packagingById)
    {
        if (line.UnitId is { } unitId && packagingById[line.MaterialId].Units.All(u => u.Id != unitId))
        {
            var baseUnitsPerUnit = content.Materials.FirstOrDefault(m => m.Id == line.MaterialId)?.Units.FirstOrDefault(u => u.Id == unitId)?.BaseUnitsPerUnit ?? 1m;
            return new PackagingLineState(line.MaterialId, null, UnitConversionRules.ToBaseQuantity(line.QtyPerUnit, baseUnitsPerUnit));
        }

        return new PackagingLineState(line.MaterialId, line.UnitId, line.QtyPerUnit);
    }

    /// <summary>Whether a sub-assembly's lines now are the ones a version saw: the same ingredients, grams and base ticks, in order.</summary>
    private static bool HasSameLines(List<SubAssemblyLine> now, IReadOnlyList<VersionLine> then) =>
        now.Count == then.Count
        && now.Zip(then).All(pair =>
            pair.First.IngredientId == pair.Second.IngredientId
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase);
}
