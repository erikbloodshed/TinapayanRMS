namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A choice in Raw Materials' Cost From picker (2026-10-29): the brand a purchased material's cost comes from, or null for the
/// dearest brand. A brand picked whose prices have all gone is still offered, saying the dearest is used meanwhile.
/// </summary>
public sealed record CostFromOption(string? Brand, bool HasPrice = true)
{
    public string Label => Brand is null ? "Dearest brand" : HasPrice ? Brand : $"{Brand} (no price: the dearest is used)";
}
