using System.Text.Json;
using System.Text.Json.Serialization;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A product as one Save left it, for its version history (ProductVersion.Content): its recipe, packaging, costing and
/// price, and every material it used with that moment's costs. It points at nothing it can't do without, so a material,
/// sub-assembly, role or product deleted since leaves it readable; the ids are kept for Restore and for telling sections
/// and lines apart. The name, description and category are kept for display only: they aren't part of a version's
/// recipe (ProductVersionRules.Changes).
/// </summary>
public sealed record ProductVersionContent
{
    /// <summary>
    /// The shape written today. A later change of shape bumps it, so an old snapshot can still be read. 2: each section
    /// keeps its label (VersionSection.Label), which 1 read from the sub-assembly's name. 3: the VAT is the shop's
    /// (<see cref="VatPercent"/>), where 2 and before kept the product's own in its costing.
    /// </summary>
    public const int CurrentSchema = 3;

    public int Schema { get; init; } = CurrentSchema;

    /// <summary>What the product was called then, which a section's label is read against (SubAssemblyRules.SectionLabel).</summary>
    public string ProductName { get; init; } = string.Empty;

    public bool IsComposite { get; init; }

    /// <summary>How many finished pieces one sellable unit held (Product.QuantityRequired).</summary>
    public int QuantityRequired { get; init; } = 1;

    public decimal? CurrentPrice { get; init; }

    /// <summary>A composite's products brought their own packaging too (Product.IncludesComponentPackaging).</summary>
    public bool IncludesComponentPackaging { get; init; }

    /// <summary>Its costing percentages; null when it had none saved, which prices as all zero.</summary>
    public VersionCosting? Costing { get; init; }

    /// <summary>
    /// The shop-wide Wastage % it was priced with then (CostingDefaults.WastagePercent); null for a version saved before
    /// there was one, which prices with none. Not part of its recipe (ProductVersionRules.IsSameRecipe): it's the shop's,
    /// like a material's price. A version saved before then has a ShrinkagePercent on each section, which is ignored.
    /// </summary>
    public decimal? WastagePercent { get; init; }

    /// <summary>
    /// The shop's VAT it was priced with then (CostingDefaults.VatPercent); null for a version saved while each product had
    /// its own, which its costing kept (VersionCosting.VatPercent). Not part of its recipe, as the wastage isn't.
    /// </summary>
    public decimal? VatPercent { get; init; }

    /// <summary>The VAT it was priced with then: the shop's, or before that its costing's own; 0 when it had none.</summary>
    public decimal VatThen => VatPercent ?? Costing?.VatPercent ?? 0m;

    /// <summary>Its sub-assemblies, in the order they were read.</summary>
    public IReadOnlyList<VersionSection> Sections { get; init; } = [];

    /// <summary>What its packaging linked, or null for its own lines.</summary>
    public VersionPackagingLink? PackagingLink { get; init; }

    /// <summary>The packaging lines it showed: its own, or those its link brought, resolved.</summary>
    public IReadOnlyList<VersionPackagingLine> PackagingLines { get; init; } = [];

    /// <summary>A composite's products, each with what one of it brought then; none for a product of sub-assemblies.</summary>
    public IReadOnlyList<VersionComponent> Components { get; init; } = [];

    /// <summary>Every material its lines, add-ins and packaging used, with its costs then.</summary>
    public IReadOnlyList<VersionMaterial> Materials { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, ProductVersionJsonContext.Default.ProductVersionContent);

    /// <summary>The snapshot <paramref name="json"/> holds. Refused when it can't be read.</summary>
    public static ProductVersionContent FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ProductVersionJsonContext.Default.ProductVersionContent)
                ?? throw new BusinessRuleException("This version's record is empty.");
        }
        catch (JsonException)
        {
            throw new BusinessRuleException("This version's record can't be read.");
        }
    }
}

/// <summary>
/// A version snapshot's JSON, written and read by code generated at compile time rather than by reflection: enums by name
/// and nulls left out, the format every snapshot in the database was written in.
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProductVersionContent))]
internal sealed partial class ProductVersionJsonContext : JsonSerializerContext;

/// <summary>The costing percentages (CostingParameters).</summary>
/// <param name="VatPercent">The product's own VAT, in a snapshot from before VAT was the shop's (ProductVersionContent.VatPercent); null since.</param>
public sealed record VersionCosting(
    decimal LaborPercent, decimal OverheadPercent, decimal AdministrativePercent, decimal MarginPercent, decimal? VatPercent = null);

/// <summary>
/// One sub-assembly in the recipe, as the product used it: its own (<see cref="IsOwn"/>) or linked, at the product's piece
/// size, with the sub-assembly's lines as they were then and the product's own add-ins on top.
/// </summary>
/// <param name="Label">
/// What it went by beside its role in the recipe, "Cheese"; null for one that went by its role alone, and in a snapshot
/// from before schema 2, which read it from <paramref name="SubAssemblyName"/> instead (<see cref="LabelIn"/>).
/// </param>
public sealed record VersionSection(
    int SubAssemblyId,
    string SubAssemblyName,
    bool IsOwn,
    VersionRole Role,
    UsageAuthoredField AuthoredField,
    decimal AuthoredValue,
    IReadOnlyList<VersionLine> Lines,
    IReadOnlyList<VersionAddIn> AddIns,
    string? Label = null)
{
    /// <summary>
    /// What it went by beside its role in <paramref name="productName"/>'s recipe: its <see cref="Label"/>, or in a
    /// snapshot from before schema 2, what its name gives (SubAssemblyRules.SectionLabel).
    /// </summary>
    public string? LabelIn(string productName, int schema) =>
        schema >= 2 ? Label : SubAssemblyRules.SectionLabel(SubAssemblyName, productName, Role.Name);
}

public sealed record VersionRole(int Id, string Name, int SortOrder, bool IsPackaging, bool IsProductOwned);

public sealed record VersionLine(int IngredientId, decimal Grams, bool IsFormulaBase, decimal? AuthoredBakersPercent);

public sealed record VersionAddIn(int IngredientId, decimal Grams);

/// <summary>Another product's packaging (<see cref="ProductId"/>) or a packaging set (<see cref="SubAssemblyId"/>), by the name it had.</summary>
public sealed record VersionPackagingLink(int? ProductId, int? SubAssemblyId, string Name);

/// <summary>A packaging line, its quantity per sellable unit in <see cref="UnitId"/> (one of the material's units) or, when null, its base unit.</summary>
public sealed record VersionPackagingLine(int MaterialId, int? UnitId, decimal QtyPerUnit);

/// <summary>One product in a composite, and what one sellable unit of it brought at each basis (SavedProductReader's ComponentCosts).</summary>
public sealed record VersionComponent(
    int ProductId,
    string ProductName,
    int Quantity,
    decimal RecipeBuffered,
    decimal RecipeUnbuffered,
    decimal PackagingBuffered,
    decimal PackagingUnbuffered);

/// <summary>A raw material as it was costed then: per base unit, buffered and not, with its units.</summary>
public sealed record VersionMaterial(
    int Id,
    string Name,
    RawMaterialType Type,
    VersionUnitOfMeasure? BaseUnit,
    decimal CostPerBaseUnit,
    decimal BufferedCostPerBaseUnit,
    IReadOnlyList<VersionMaterialUnit> Units);

public sealed record VersionUnitOfMeasure(int Id, string Name, string Abbreviation);

/// <summary>One of a material's alternative units (RawMaterialUnit): how many base units one of it holds.</summary>
public sealed record VersionMaterialUnit(int Id, VersionUnitOfMeasure UnitOfMeasure, decimal BaseUnitsPerUnit, string? Description);
