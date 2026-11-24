namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// A line of a product's section that is another of the same product's sections, in grams: a sponge on the dough's lines.
/// It costs its grams at that section's cost per gram, and the section it names keeps what's left of its batch for pieces
/// of its own, if it has a piece size (ProductRules.EnsureValidSectionLines; Pricing's SectionUses).
/// <para>
/// It belongs to the usage it's a line of, as an add-in does, not to the sub-assembly: it's the product's, and a section
/// is the product's alone, so the Sub-Assemblies screen and the stored-cost cascade never see it. In a section the product
/// owns it's among the lines; in a linked one, among the add-ins.
/// </para>
/// </summary>
public sealed class UsageSectionLine
{
    public int Id { get; set; }

    /// <summary>The usage of the section it's a line of: the dough.</summary>
    public int UsageId { get; set; }

    public ProductSubAssemblyUsage? Usage { get; set; }

    /// <summary>The sub-assembly of the product's other section it uses: the sponge.</summary>
    public int SectionSubAssemblyId { get; set; }

    /// <summary>The amount, always in grams.</summary>
    public decimal Grams { get; set; }

    /// <summary>
    /// The baker's percentage typed for it, or null when its grams were. As on a sub-assembly's line, a typed percentage
    /// stays put and its grams follow the base (SubAssemblyLine.AuthoredBakersPercent).
    /// </summary>
    public decimal? AuthoredBakersPercent { get; set; }

    /// <summary>
    /// Where it sits in its section's table, from 0, among the ingredient lines (or, in a linked section, the add-ins), whose
    /// own SortOrder keeps their order between them: it's put back at this place when the two are read together.
    /// </summary>
    public int Position { get; set; }
}
