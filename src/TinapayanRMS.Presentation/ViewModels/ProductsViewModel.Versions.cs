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

    // What the History tab asks of the screen (IProductVersionHistoryHost): the product showing, the lists as last loaded,
    // and the form a restored version goes into. Its cost basis and Packaging tab are the screen's own.
    Product? IProductVersionHistoryHost.ProductShowing => AreDetailsReady ? SelectedProduct : null;

    IReadOnlyList<Product> IProductVersionHistoryHost.Products => Products;

    IReadOnlyList<RawMaterial> IProductVersionHistoryHost.Ingredients => Ingredients;

    IReadOnlyList<RawMaterial> IProductVersionHistoryHost.PackagingMaterials => PackagingMaterials;

    IReadOnlyList<SubAssemblyRole> IProductVersionHistoryHost.Roles => Roles;

    IReadOnlySet<int> IProductVersionHistoryHost.OwnedSectionIds => _savedRecipe.New.Select(n => n.Id).Where(id => id != 0).ToHashSet();

    string? IProductVersionHistoryHost.SectionLabelOf(string subAssemblyName, SubAssemblyRole? role) => SectionLabelOf(subAssemblyName, role);

    /// <summary>
    /// A version Restore worked out (ProductVersionHistory.RestoreCommand), into the form as unsaved edits in one undo step:
    /// its sections, packaging, products, costing, quantity required and price, over the form as it stands, so the name,
    /// description, category and stage stay.
    /// </summary>
    void IProductVersionHistoryHost.ApplyVersion(RestoredVersion version) => AsUndoStep(() =>
    {
        // The form goes, as any restore closes it: what it held comes back with Undo.
        CloseUsageForm();
        RestoreSnapshot(
            CaptureSnapshot() with
            {
                CurrentPrice = version.CurrentPrice,
                QuantityRequired = version.QuantityRequired,
                IncludesComponentPackaging = version.IncludesComponentPackaging,
                Costing = version.Costing,
                Usages = version.Sections,
                SelectedTab = version.Sections.Count > 0 ? new RecipeTabChoice.Usage(0)
                    : version.IsComposite ? new RecipeTabChoice.Components()
                    : new RecipeTabChoice.Packaging(),
                HasAddSubAssemblyTab = false,
                PackagingLink = version.PackagingLink,
                IsPackagingMoving = false,
                Lines = version.PackagingLines,
                BulkLines = version.BulkLines,
                BulkQuantity = version.BulkQuantity,
                Components = version.Components,
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
}
