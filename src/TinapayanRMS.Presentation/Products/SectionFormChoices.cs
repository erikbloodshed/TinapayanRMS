using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// What the section form (<see cref="ProductUsageForm"/>) offers to pick from, built from the recipe and the lists the
/// screen last loaded: the sub-assemblies to link or copy, the roles, and the sections the one it edits can go into whole.
/// The recipe (<see cref="ProductRecipe"/>) asks for them again as what they're built from changes.
/// </summary>
internal sealed class SectionFormChoices(IProductRecipeHost host, ProductUsageForm form, ProductRecipeDraft draft)
{
    /// <summary>
    /// The view's lists the choices were last built from (IProductRecipeHost.LinkableVersion). The view's load merges into
    /// every open tab, and building the choices again, every sub-assembly's option compared with the last, was most of what
    /// each tab's merge cost: so a load builds them only when those lists changed. What the recipe holds changing builds them
    /// at once, as it always has.
    /// </summary>
    private int _builtFrom = -1;

    /// <summary>
    /// The view's load is merged: the choices follow the lists it read, if they changed, the lines' picker too, as a role
    /// may go into other sections now, or no longer.
    /// </summary>
    public void RefreshAfterLoad()
    {
        if (_builtFrom != host.LinkableVersion)
        {
            Refresh();
            SectionFormCapture.FollowSections(form, draft);
        }
    }

    /// <summary>
    /// The sub-assemblies the form can link or copy. Linking offers only the Sub-Assemblies screen's (standalone) ones the
    /// recipe doesn't already link, never anything a product keeps; copying offers every other one. Never packaging,
    /// which the Packaging tab links and copies itself. A pick that's no longer offered is cleared. The roles the form
    /// offers follow the recipe too (<see cref="RefreshRoles"/>).
    /// </summary>
    public void Refresh()
    {
        _builtFrom = host.LinkableVersion;
        var linking = form.EditingUsageMode == UsageFormMode.Link;
        var usedIds = draft.UsageEdits.Select(u => u.SubAssemblyId).ToHashSet();
        var isComposite = host.IsEditingComposite;
        var productId = host.DetailProductId;

        // A composite's own sections are only in product-owned roles, so it's offered those.
        form.LinkableSubAssemblies.MergeFrom(
            [
                .. host.Linkable
                    .Where(s => (!isComposite || s.Role?.IsProductOwned == true) && (linking
                        ? s.OwnerProductId is null && !usedIds.Contains(s.Id)
                        : s.OwnerProductId is null || s.OwnerProductId != productId))
                    .Select(s => new LinkableSubAssemblyOption(s)),
            ],
            o => o.Key);

        if (form.EditingUsageSource is { } picked && !form.LinkableSubAssemblies.Contains(picked))
        {
            form.EditingUsageSource = null;
        }

        RefreshRoles();
    }

    /// <summary>
    /// The roles the form offers (FormRoleOptions): every recipe role, as a recipe can have several sub-assemblies in one,
    /// told apart by their names. The role picked stays picked while it's still offered: the picker can drop its pick as
    /// its list changes under it.
    /// </summary>
    public void RefreshRoles()
    {
        var pickedRoleId = form.EditingUsageRole?.Id;
        var isComposite = host.IsEditingComposite;
        // Never the Packaging role: packaging isn't a recipe section, and the Packaging tab takes it. A composite's
        // sections are only in product-owned roles (CompositeProductRules.EnsureSectionRoles).
        form.FormRoleOptions.MergeFrom(
            [.. draft.Roles.Where(r => !r.IsPackaging && (!isComposite || r.IsProductOwned))],
            r => r.Id);
        form.EditingUsageRole = form.FormRoleOptions.FirstOrDefault(r => r.Id == pickedRoleId);
    }
}
