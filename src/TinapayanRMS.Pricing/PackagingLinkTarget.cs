using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Pricing;

/// <summary>
/// What a product's per-piece packaging links: a standalone packaging set from the Sub-Assemblies screen
/// (<see cref="Set"/>, one set per sellable unit), or another product's packaging (<see cref="OtherProduct"/>).
/// </summary>
/// <param name="Name">The set's or the product's name, as it was read.</param>
public closed record PackagingLinkTarget(string Name)
{
    /// <summary>One key across both kinds: a set's id, or a product's id negated, so a product and a set sharing an id differ.</summary>
    public int Key => this switch
    {
        Set set => set.SubAssemblyId,
        OtherProduct product => -product.ProductId,
    };

    /// <summary>The Packaging tab's banner, e.g. "Linked to Gift Box Set (Sub-Assemblies)" or "Linked to Cream Loaf".</summary>
    public string LinkedToText => this switch
    {
        Set => $"Linked to {Name} ({PricedSection.StandaloneSourceName})",
        OtherProduct => $"Linked to {Name}",
    };

    /// <summary>The link as Save writes it.</summary>
    public PackagingLinkDraft ToDraft() => this switch
    {
        Set set => new PackagingLinkDraft.Set(set.SubAssemblyId),
        OtherProduct product => new PackagingLinkDraft.OtherProduct(product.ProductId),
    };

    /// <summary>What a saved link links: a set's, or a product's, by the name it was read with.</summary>
    public static PackagingLinkTarget From(PackagingLink link) =>
        link.LinkedSubAssemblyId is { } setId
            ? new Set(setId, link.LinkedSubAssembly?.Name ?? string.Empty)
            : new OtherProduct(link.LinkedProductId!.Value, link.LinkedProduct?.Name ?? string.Empty);

    /// <summary>A packaging set from the Sub-Assemblies screen.</summary>
    public sealed record Set(int SubAssemblyId, string Name) : PackagingLinkTarget(Name);

    /// <summary>Another product's packaging, whatever it is: its own lines, or what it links in turn.</summary>
    public sealed record OtherProduct(int ProductId, string Name) : PackagingLinkTarget(Name);
}
