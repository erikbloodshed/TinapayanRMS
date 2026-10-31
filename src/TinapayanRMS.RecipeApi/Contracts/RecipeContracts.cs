namespace TinapayanRMS.RecipeApi.Contracts;

// What the Recipe API sends and takes, as JSON in camelCase. Other apps keep their own copies of these, so a change here
// is a change to /api/v1: add to a record rather than rename or remove, or start /api/v2. Grams are always grams, as in
// every recipe line; an enum goes by its name.

/// <summary>What /status answers: that this is the Recipe API, and which version of it.</summary>
public sealed record ApiStatus(string App, int ApiVersion);

/// <summary>A refusal: the request's problem, in a sentence to show the user.</summary>
public sealed record ApiError(string Error);

/// <summary>An ingredient (never packaging), with its category and how it's sourced (Purchased, Made or Linked).</summary>
/// <param name="WeightLossPercent">Made only: the weight lost while making it.</param>
/// <param name="LinkedToId">Linked only: the ingredient it's cut from (egg white from the egg).</param>
/// <param name="WeightSharePercent">Linked only: how much of the weight of what it's cut from it is.</param>
/// <param name="SubAssemblyId">Made only: the sub-assembly whose recipe it's made from.</param>
public sealed record IngredientInfo(
    int Id,
    string Name,
    int CategoryId,
    string Category,
    string Source,
    decimal? WeightLossPercent,
    int? LinkedToId,
    decimal? WeightSharePercent,
    int? SubAssemblyId);

/// <summary>
/// One line of a made ingredient's bill of materials (2026-10-31, for TinapayanPreweigh's planning). /boms gives each
/// made ingredient's lines in its BOM's order: the lines of the sub-assembly it's made from, then its own add-ins.
/// </summary>
public sealed record BomLineInfo(int IngredientId, int ComponentIngredientId, decimal Grams, bool IsAddIn);

/// <summary>An ingredient category (Type Ingredient) or a made ingredients' one (Type Made).</summary>
public sealed record CategoryInfo(int Id, string Name, string Type);

/// <summary>A product, not deleted. Stage is Production or ResearchAndDevelopment.</summary>
/// <param name="QuantityRequired">How many pieces make one sellable unit.</param>
public sealed record ProductInfo(int Id, string Name, string? Category, string Stage, bool IsComposite, int QuantityRequired);

/// <summary>
/// A mix: a sub-assembly with ingredients (a dough, a filling, a topping, …), never a packaging set. One a product owns
/// (OwnerProduct set) is that product's own section.
/// </summary>
/// <param name="BatchWeight">What one batch weighs: its lines' grams.</param>
public sealed record MixInfo(int Id, string Name, int RoleId, string Role, int RoleSortOrder, decimal BatchWeight, string? OwnerProduct);

/// <summary>A mix and what goes into one batch of it, in the order the recipe lists them.</summary>
public sealed record MixDetail(MixInfo Mix, IReadOnlyList<MixLine> Lines);

/// <summary>One line of a mix: grams per batch.</summary>
/// <param name="IsFormulaBase">Part of the 100% of the baker's percentage, usually the flour.</param>
public sealed record MixLine(int IngredientId, string Ingredient, decimal Grams, bool IsFormulaBase);

/// <summary>
/// What a product is made of, as the Production Plan reads it (2026-10-31, for TinapayanPreweigh's planning): a
/// composite's products, or its sections in the recipe's order.
/// </summary>
/// <param name="QuantityRequired">How many pieces make one sellable unit.</param>
/// <param name="WastagePercent">
/// The Wastage % it's priced with, its category's or the shop-wide one: what TinapayanPreweigh's Production Results set
/// what fell short against.
/// </param>
/// <param name="PackagingPerUnit">Its packaging for one sellable unit, in each material's base unit (/materials).</param>
/// <param name="IncludesComponentPackaging">A composite that brings its products' packaging too.</param>
/// <param name="Costing">What it sells for and costs, as the Products screen prices it at buffered costs.</param>
public sealed record ProductRecipeInfo(
    int ProductId,
    int QuantityRequired,
    IReadOnlyList<ComponentInfo> Components,
    IReadOnlyList<SectionInfo> Sections,
    decimal WastagePercent,
    IReadOnlyList<PackagingPerUnitInfo> PackagingPerUnit,
    bool IncludesComponentPackaging,
    ProductCostingInfo Costing);

/// <summary>A packaging material a sellable unit takes, and how much of it in the material's base unit.</summary>
public sealed record PackagingPerUnitInfo(int MaterialId, decimal BaseQuantity);

/// <summary>
/// A product's price and cost for one sellable unit (2026-10-31, for TinapayanPreweigh's plan sales and profit).
/// </summary>
/// <param name="CurrentPrice">The price it sells at, with VAT; null when none is set, and its worked-out price stands in.</param>
/// <param name="SellingPrice">The price its costing works out, with VAT.</param>
/// <param name="BaseCost">Its ingredients, packaging, wastage, labor, overhead and administrative, VAT out.</param>
public sealed record ProductCostingInfo(decimal? CurrentPrice, decimal SellingPrice, decimal VatPercent, decimal BaseCost);

/// <summary>
/// A raw material, ingredient or packaging, as a purchase requisition needs it (2026-10-31, for TinapayanPreweigh): its base
/// unit and buffered cost, and what it's bought in at its cost brand's price.
/// </summary>
/// <param name="Type">Ingredient or Packaging.</param>
/// <param name="Unit">Its base unit's abbreviation: "g" for an ingredient, "pc" or "m" for packaging.</param>
/// <param name="CostPerBaseUnit">Its buffered cost per base unit; 0 with none yet.</param>
/// <param name="BuyUnit">What it's bought in, "sack"; null with no price.</param>
/// <param name="BaseUnitsPerBuyUnit">Base units in one: 25,000 for a 25 kg sack.</param>
/// <param name="BuyUnitPrice">What one costs at the brand price its cost comes from; null with no price.</param>
public sealed record MaterialInfo(
    int Id,
    string Name,
    string Type,
    string Unit,
    decimal CostPerBaseUnit,
    string? BuyUnit,
    decimal BaseUnitsPerBuyUnit,
    decimal? BuyUnitPrice);

/// <summary>A product in a composite, and how many of its sellable units go in one of the composite.</summary>
public sealed record ComponentInfo(int ProductId, int Quantity);

/// <summary>
/// One section of a product's recipe: the sub-assembly it's made from, how big a piece is, its lines and the product's own
/// add-ins on top, in grams per batch.
/// </summary>
/// <param name="SectionName">Its tab's name: its role, with its label when the role comes more than once ("Filling: Ube").</param>
/// <param name="LinkedName">The linked sub-assembly's name; null for the product's own section.</param>
/// <param name="AuthoredField">WeightPerPiece or Yield: which of the two <paramref name="AuthoredValue"/> is.</param>
public sealed record SectionInfo(
    int SubAssemblyId,
    string SectionName,
    string? LinkedName,
    string Role,
    string AuthoredField,
    decimal AuthoredValue,
    IReadOnlyList<PlanLine> Lines,
    IReadOnlyList<PlanLine> AddIns);

/// <summary>A production plan saved on the Production Plan screen.</summary>
/// <param name="MakeOn">The day it's for, when one was given.</param>
public sealed record SavedPlanInfo(int Id, string Name, DateTime SavedAt, DateOnly? MakeOn, bool HalfBatches, IReadOnlyList<PlanOrder> Lines);

/// <summary>A product to make and how many of its sellable units. Product is its name, filled in on the way out.</summary>
public sealed record PlanOrder(int ProductId, decimal Units, string? Product = null);

/// <summary>What /plan works out: the products to make, and whether mixes may be rounded up to half batches.</summary>
public sealed record PlanRequest(IReadOnlyList<PlanOrder> Orders, bool HalfBatches);

/// <summary>
/// A production plan worked out as the Production Plan screen works it out: each mix in its batches, the made ingredients
/// to prepare for them, and what it couldn't work out.
/// </summary>
public sealed record PlanResult(IReadOnlyList<PlannedMixInfo> Mixes, IReadOnlyList<PreparedInfo> Prepared, IReadOnlyList<string> Issues);

/// <summary>
/// One mix to make, named as the screen's Mixing Sheet names it. A product's own add-ins make a mix of their own, so a
/// sub-assembly can come more than once, under different names ("Sweet Dough + Ube Flavour").
/// </summary>
/// <param name="For">The products it's for and the pieces each takes ("Ube Roll 40 pcs").</param>
/// <param name="Batches">Whole batches, or half ones when the plan allows them.</param>
/// <param name="LinesPerBatch">The sub-assembly's lines, then the add-ins, grams per batch.</param>
public sealed record PlannedMixInfo(int SubAssemblyId, string Name, string Role, string For, decimal Batches, decimal BatchWeight, IReadOnlyList<PlanLine> LinesPerBatch);

/// <summary>A made ingredient the mixes take, and what to make the grams they need from.</summary>
/// <param name="SubAssemblyId">The sub-assembly it's made from, whose lines (and its add-ins) <paramref name="Lines"/> are.</param>
/// <param name="Lines">Its ingredients, scaled to <paramref name="Grams"/>: grams in all, not per batch.</param>
public sealed record PreparedInfo(int IngredientId, int? SubAssemblyId, string Name, decimal Grams, IReadOnlyList<PlanLine> Lines);

public sealed record PlanLine(int IngredientId, string Ingredient, decimal Grams);
