namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// What a product's per-piece packaging links: another product's packaging, or a standalone packaging set from the
/// Sub-Assemblies screen, one set per piece. Exactly one of the two ids is set.
/// </summary>
public sealed record PackagingLinkTarget(int? ProductId, int? SubAssemblyId, string Name)
{
    /// <summary>One key across both kinds: a set's id, or a product's id negated, so a product and a set sharing an id differ.</summary>
    public int Key => SubAssemblyId ?? -ProductId!.Value;

    /// <summary>The Packaging tab's banner, e.g. "Linked to Gift Box Set (Sub-Assemblies)" or "Linked to Cream Loaf".</summary>
    public string LinkedToText => SubAssemblyId is null ? $"Linked to {Name}" : $"Linked to {Name} ({ProductUsageEdit.StandaloneSourceName})";

    public static PackagingLinkTarget ForProduct(int productId, string name) => new(productId, null, name);

    public static PackagingLinkTarget ForSet(int subAssemblyId, string name) => new(null, subAssemblyId, name);
}
