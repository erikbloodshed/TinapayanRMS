namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// What a product's Save was refused over when a role's Product-owned was changed in Settings since the form took the
/// product: the sub-assembly whose owner changed, and whether it's the product's own now or on the Sub-Assemblies screen
/// (SavedVersionRules.OwnershipChanged).
/// </summary>
public sealed record OwnershipChange(int SubAssemblyId, string Name, bool IsOwnNow);
