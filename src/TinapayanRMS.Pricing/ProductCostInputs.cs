namespace TinapayanRMS.Pricing;

/// <summary>
/// What a <see cref="ProductCostSummary"/> is worked out from, each figure by name. The Products screen fills it from the
/// form and <see cref="SavedProductReader"/> from what's saved, so both price a product the same way.
/// </summary>
public sealed record ProductCostInputs
{
    /// <summary>
    /// The recipe's parts, in recipe order, each with its cost per piece: the sections, or for a composite its products
    /// ("Ensaymada × 6" and what the six bring in recipe cost), then its own sections.
    /// </summary>
    public IReadOnlyList<(string Name, decimal CostPerPiece)> Parts { get; init; } = [];

    /// <summary>How many pieces one sellable unit holds. A composite is always one unit.</summary>
    public int QuantityRequired { get; init; } = 1;

    /// <summary>The product's own packaging lines, each for one whole sellable unit.</summary>
    public IReadOnlyList<decimal> PackagingCosts { get; init; } = [];

    /// <summary>What a composite's products bring in packaging when it includes theirs; null when it doesn't.</summary>
    public decimal? ComponentPackaging { get; init; }

    public decimal LaborPercent { get; init; }

    public decimal OverheadPercent { get; init; }

    public decimal AdministrativePercent { get; init; }

    /// <summary>The markup on the base cost.</summary>
    public decimal MarginPercent { get; init; }

    public decimal VatPercent { get; init; }

    /// <summary>The Wastage % it's priced with: its category's when that sets one, else the shop's.</summary>
    public decimal WastagePercent { get; init; }

    /// <summary>What the selling price is rounded up to a multiple of; 0 for no rounding.</summary>
    public decimal PriceRoundingStep { get; init; }

    /// <summary>What the shop charges now, VAT included; null when none is entered.</summary>
    public decimal? CurrentPrice { get; init; }

    /// <summary>The product is a composite, whose recipe is the products it holds.</summary>
    public bool IsComposite { get; init; }

    /// <summary>A composite that also has sections of its own (a topping, a filling).</summary>
    public bool HasOwnSections { get; init; }
}
