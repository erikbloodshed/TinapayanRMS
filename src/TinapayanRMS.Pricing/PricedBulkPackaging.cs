using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>
/// A product's bulk packaging, read and priced at one basis: the lines one bulk pack takes (a box several units share, its
/// tape), each priced for the pack, how many sellable units the pack holds, what the pack costs, and the share each unit
/// carries (<see cref="CostPerUnit"/>, ProductRules.CalculateBulkPackagingCost), which joins its packaging cost.
/// </summary>
/// <param name="Lines">Each line for one pack: its quantity and cost are the pack's.</param>
/// <param name="Quantity">How many sellable units one pack holds (Product.BulkPackagingQuantity).</param>
public sealed record PricedBulkPackaging(IReadOnlyList<PricedPackagingLine> Lines, int? Quantity)
{
    /// <summary>What one bulk pack's lines cost together.</summary>
    public decimal PackCost => Lines.Sum(l => l.CostPerUnit);

    /// <summary>What it costs one sellable unit: the pack's cost shared out among the units it holds.</summary>
    public decimal CostPerUnit => ProductRules.CalculateBulkPackagingCost(PackCost, Quantity);

    /// <summary>
    /// A product's saved bulk lines at <paramref name="basis"/>, their materials taken from <paramref name="materialsById"/>
    /// when that has them; null when it has none.
    /// </summary>
    public static PricedBulkPackaging? FromSaved(
        IReadOnlyList<PackagingLine> lines, int? quantity, CostBasis basis, IReadOnlyDictionary<int, RawMaterial>? materialsById = null) =>
        lines.Count == 0 ? null : new([.. lines.Select(l => PricedPackagingLine.FromSaved(l, basis, materialsById))], quantity);
}
