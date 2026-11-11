using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A choice in the Link and Copy picker: a recipe sub-assembly, "Chocolate Glaze — Sub-Assemblies (Glaze)", or
/// packaging: a packaging set, "Gift Box Set — Sub-Assemblies (Packaging)", or another product's, "Packaging — Cream Loaf".
/// </summary>
public sealed class LinkableSubAssemblyOption
{
    /// <summary>A sub-assembly: a recipe, or a packaging set (one in the Packaging role).</summary>
    public LinkableSubAssemblyOption(SubAssembly subAssembly)
    {
        SubAssembly = subAssembly;
        if (subAssembly.Role?.IsPackaging == true)
        {
            PackagingTarget = new PackagingLinkTarget.Set(subAssembly.Id, subAssembly.Name);
        }
    }

    /// <summary>Another product's packaging, whatever it is: its own lines, or what it links in turn.</summary>
    public LinkableSubAssemblyOption(Product product)
    {
        PackagingTarget = new PackagingLinkTarget.OtherProduct(product.Id, product.Name);
    }

    /// <summary>The sub-assembly, or null for another product's packaging.</summary>
    public SubAssembly? SubAssembly { get; }

    /// <summary>What the product's packaging would link or copy, or null for a recipe.</summary>
    public PackagingLinkTarget? PackagingTarget { get; }

    public bool IsPackaging => PackagingTarget is not null;

    /// <summary>A sub-assembly's id, or another product's id negated: one key for the picker's merge.</summary>
    public int Key => SubAssembly?.Id ?? PackagingTarget!.Key;

    public string DisplayName => SubAssembly is null
        ? $"Packaging — {PackagingTarget!.Name}"
        : $"{SubAssembly.Name} — {SubAssembly.OwnerProduct?.Name ?? PricedSection.StandaloneSourceName} ({SubAssembly.Role?.Name})";

    /// <summary>What the Packaging tab's Link and Copy picker shows for packaging: "Gift Box Set — packaging set", "Cream Loaf — product".</summary>
    public string PackagingSourceName => SubAssembly is null ? $"{PackagingTarget!.Name} — product" : $"{SubAssembly.Name} — packaging set";
}
