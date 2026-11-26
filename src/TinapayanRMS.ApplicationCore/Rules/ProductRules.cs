using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class ProductRules
{
    /// <summary>
    /// What Innovation was called, and stored as, before 2026-11-09: a product's stage as an older copy of the app writes it
    /// (ProductStageConverter reads it as Innovation), and a folder key the view state kept then (ProductTree.CurrentFolderKey).
    /// </summary>
    public const string FormerInnovationName = "ResearchAndDevelopment";

    /// <summary>What a product with no category is filed under: its folder in the list, and its category on a report or a printout.</summary>
    public const string UncategorizedTitle = "Uncategorized";

    /// <summary>What a stage is called: "Production" or "Innovation".</summary>
    public static string StageTitle(ProductStage stage) => stage == ProductStage.Production ? "Production" : "Innovation";

    /// <summary>
    /// How a product is named where it's in the way of something (a refusal, a Dependents tile): "Cheese Roll (deleted)" for
    /// one in the Deleted folder, which still holds what it uses until it's deleted permanently.
    /// </summary>
    public static string ListedName(string name, bool isDeleted) => isDeleted ? $"{name} (deleted)" : name;

    /// <summary>
    /// A product's name as it's saved: without spaces at either end, and refused when there's nothing else. PostgreSQL's
    /// collation counts a space at the end, so "Pandesal " would get past the unique index beside "Pandesal".
    /// </summary>
    public static string StoredName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length > 0 ? trimmed : throw new BusinessRuleException("Every product needs a name.");
    }

    /// <summary>
    /// A product's name is unique among deleted products too, as the one waiting in the Deleted folder keeps it for its
    /// restore: say so, rather than that the name is taken by nothing in sight.
    /// </summary>
    public static void EnsureNameNotHeldByDeleted(string name, bool isHeldByDeleted)
    {
        if (isHeldByDeleted)
        {
            throw new BusinessRuleException(
                $"A deleted product is named {name}. Restore it from the Deleted folder, or delete it permanently there first.");
        }
    }

    /// <summary>
    /// A product's whole set of usages: an empty set is valid (PLAN.md doesn't require a product to
    /// have a usage at all times), and so is a piece size of 0 — a section can go into the recipe before
    /// anyone knows how much of it a piece takes, and until it's
    /// known the section costs the product nothing per piece (ResolvePieceSize, CalculateUsageCostPerPiece).
    /// A negative piece size is still nonsense, and a product uses each sub-assembly once.
    /// </summary>
    public static void EnsureValidUsages(IReadOnlyList<(int SubAssemblyId, decimal AuthoredValue)> usages)
    {
        if (usages.Any(u => u.AuthoredValue < 0))
        {
            throw new BusinessRuleException("A sub-assembly's weight per piece or yield can't be negative.");
        }

        if (usages.Select(u => u.SubAssemblyId).Distinct().Count() != usages.Count)
        {
            throw new BusinessRuleException("A product can't use the same sub-assembly more than once.");
        }
    }

    /// <summary>
    /// The lines of a product's sections that are others of its sections (UsageSectionLine): a sponge on the dough's lines.
    /// Each section is named by <paramref name="sections"/>' SectionId, and each line by the SectionId of the one it uses.
    /// A line uses one of the recipe's sections, never the section it's on, in a role that can go into another
    /// (SubAssemblyRole.CanGoIntoOtherSections), once in that section, with grams; a section can be on several others' lines,
    /// and a chain (a sponge in a dough in a bigger dough) is fine, but a loop is not, as no batch could cost what it does.
    /// </summary>
    public static void EnsureValidSectionLines(
        IReadOnlyList<(int SectionId, string Name, string RoleName, bool CanGoIntoOtherSections, IReadOnlyList<(int UsedId, decimal Grams)> Lines)> sections)
    {
        var byId = sections.ToDictionary(s => s.SectionId);
        IEnumerable<int> UsesOf(int id) => byId.TryGetValue(id, out var section) ? section.Lines.Select(l => l.UsedId) : [];
        foreach (var section in sections)
        {
            HashSet<int> used = [];
            foreach (var (usedId, grams) in section.Lines)
            {
                if (!byId.TryGetValue(usedId, out var usedSection))
                {
                    throw new BusinessRuleException($"{section.Name} uses a section that isn't in this recipe any more. Delete its line.");
                }

                if (usedId == section.SectionId)
                {
                    throw new BusinessRuleException($"{section.Name} can't be on its own lines.");
                }

                if (!usedSection.CanGoIntoOtherSections)
                {
                    throw new BusinessRuleException(
                        $"{usedSection.Name} can't be on {section.Name}'s lines: Settings → Roles doesn't let a {usedSection.RoleName} section go into "
                        + $"another. Tick Into other sections on the {usedSection.RoleName} role, or delete its line.");
                }

                if (!used.Add(usedId))
                {
                    throw new BusinessRuleException($"{usedSection.Name} is on two lines of {section.Name}. Put its total on one line and delete the other.");
                }

                if (grams <= 0)
                {
                    throw new BusinessRuleException($"Enter how many grams of {usedSection.Name} go into {section.Name}, or delete its line.");
                }

                if (Reaches(usedId, section.SectionId, UsesOf))
                {
                    throw new BusinessRuleException(
                        $"{section.Name} uses {usedSection.Name}, which uses {section.Name}. A section can't be on its own lines, even through others.");
                }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="fromId"/> uses <paramref name="toId"/>, on its lines or on theirs however deep, each section's
    /// lines given by <paramref name="usesOf"/>: putting <paramref name="fromId"/> on <paramref name="toId"/>'s lines would
    /// make a loop. Public for the Products form's picker, which offers no section that would.
    /// </summary>
    public static bool Reaches<T>(T fromId, T toId, Func<T, IEnumerable<T>> usesOf)
        where T : notnull
    {
        HashSet<T> seen = [];
        Stack<T> next = new([fromId]);
        while (next.TryPop(out var id))
        {
            if (EqualityComparer<T>.Default.Equals(id, toId))
            {
                return true;
            }

            if (seen.Add(id))
            {
                foreach (var used in usesOf(id))
                {
                    next.Push(used);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A usage's add-ins: this product's own extra ingredients on top of a recipe it links — a colouring,
    /// a flavouring, an inclusion. Unlike a sub-assembly's own lines, an empty set is valid: most linked
    /// usages take the recipe as it is. What isn't valid is an ingredient the recipe already has, since
    /// add-ins are only for what it doesn't. <paramref name="baseIngredientIds"/> are the linked
    /// sub-assembly's own lines' ingredients.
    /// </summary>
    public static void EnsureValidAddIns(
        IReadOnlyList<(int IngredientId, decimal Grams, string IngredientName)> addIns,
        string subAssemblyName,
        IReadOnlyCollection<int> baseIngredientIds) =>
        AddInRules.EnsureValid(
            addIns,
            baseIngredientIds,
            alreadyThere => $"{alreadyThere} is already in {subAssemblyName}. Add-ins are only for what its recipe doesn't have — "
                + $"change how much it uses where {subAssemblyName} is kept, or make this product a copy of it.");

    /// <summary>
    /// A usage's effective batch: the sub-assembly's own, plus this product's add-ins. Add-ins go into the
    /// batch, so 1,700 g of dough with 20 g of flavouring is a 1,720 g batch and a piece takes its
    /// weight-per-piece share of the whole thing. Pass the sub-assembly's own batch, worked out from its lines
    /// (SubAssemblyRules.CalculateTotals). A sub-assembly of the product's own never has add-ins, so its
    /// effective batch is its own.
    /// </summary>
    public static (decimal TotalWeight, decimal TotalCost) CalculateEffectiveBatch(
        decimal subAssemblyTotalWeight,
        decimal subAssemblyTotalCost,
        IReadOnlyList<(decimal Grams, decimal IngredientCostPerGram)> addIns)
    {
        var (addInWeight, addInCost) = SubAssemblyRules.CalculateTotals(addIns);
        return (subAssemblyTotalWeight + addInWeight, subAssemblyTotalCost + addInCost);
    }

    /// <summary>
    /// A usage's weight per piece and yield, from the one the user typed and what the batch weighs
    /// (<paramref name="totalWeight"/>, its effective batch: CalculateEffectiveBatch): the typed one as it is, the
    /// other worked out (Yield = TotalWeight / WeightPerPiece). Both are worked out on every read, so the typed
    /// one stays put when the batch changes. A batch that weighs nothing yet (a new sub-assembly with no lines)
    /// has no other figure: it comes out as 0. What's lost making a product is a cost (CostingDefaults.WastagePercent),
    /// not a smaller batch: the pieces share out the whole of it.
    /// </summary>
    public static (decimal WeightPerPiece, decimal Yield) ResolvePieceSize(UsageAuthoredField authoredField, decimal authoredValue, decimal totalWeight)
    {
        var other = totalWeight > 0 && authoredValue > 0 ? totalWeight / authoredValue : 0m;
        return authoredField switch
        {
            UsageAuthoredField.WeightPerPiece => (authoredValue, other),
            UsageAuthoredField.Yield => (other, authoredValue),
            _ => throw new ArgumentOutOfRangeException(nameof(authoredField)),
        };
    }

    /// <summary>
    /// Whether a batch of <paramref name="totalWeight"/> grams is cut into pieces: not while it weighs nothing, nor while no
    /// piece size is typed (ResolvePieceSize gives a weight per piece of 0 then).
    /// </summary>
    public static bool HasPieces(decimal totalWeight, decimal weightPerPiece) => totalWeight > 0 && weightPerPiece > 0;

    /// <summary>
    /// Whether a section has a piece size: a weight per piece or a yield typed, whichever (<paramref name="authoredValue"/>,
    /// which EnsureValidUsages keeps from going below 0). One with neither costs the product nothing per piece, however much
    /// its batch costs, so the product's cost and price leave it out until one is typed: the Products header, the Products
    /// list, Reports and Print say so. A saved section always has lines, so for one that's saved this is <see cref="HasPieces"/>.
    /// </summary>
    public static bool HasPieceSize(decimal authoredValue) => authoredValue > 0;

    /// <summary>
    /// This usage's share of the sub-assembly's cost, per piece. The batch's whole cost is spread over its
    /// weight (<paramref name="totalWeight"/>), so this comes to the same thing as TotalCost / Yield. Nothing while the
    /// batch has no pieces (<see cref="HasPieces"/>): a section with no piece size yet costs the product nothing per piece.
    /// </summary>
    public static decimal CalculateUsageCostPerPiece(decimal subAssemblyTotalCost, decimal weightPerPiece, decimal totalWeight) =>
        HasPieces(totalWeight, weightPerPiece) ? subAssemblyTotalCost * weightPerPiece / totalWeight : 0m;

    /// <summary>
    /// A product's packaging lines. Unlike EnsureValidUsages and the ingredient/sub-assembly line rules,
    /// an empty list is deliberately allowed here — a product may legitimately have no packaging lines
    /// entered yet, whereas a sub-assembly with no ingredient lines or a product with no sub-assembly
    /// usage isn't a real recipe.
    /// </summary>
    public static void EnsureValidPackagingLines(IReadOnlyList<(int PackagingMaterialId, decimal QtyPerUnit)> lines)
    {
        if (lines.Any(l => l.QtyPerUnit <= 0))
        {
            throw new BusinessRuleException("A packaging line's quantity must be positive.");
        }

        if (lines.GroupBy(l => l.PackagingMaterialId).Any(g => g.Count() > 1))
        {
            throw new BusinessRuleException(
                "A product can't use the same packaging material more than once — combine it into a single line instead.");
        }
    }

    /// <summary>
    /// A product's bulk packaging (PackagingLine.IsBulk): <paramref name="lineCount"/> lines for one bulk pack need to know
    /// how many sellable units the pack holds, a whole number of 1 or more, which their cost is shared out among. Its
    /// lines are packaging lines too, so EnsureValidPackagingLines applies to them.
    /// </summary>
    public static void EnsureValidBulkPackaging(int lineCount, int? quantity)
    {
        if (lineCount > 0 && quantity is not >= 1)
        {
            throw new BusinessRuleException("Enter how many products one bulk pack holds: 1 or more.");
        }
    }

    /// <summary>
    /// What a product's bulk packaging costs one sellable unit: what one bulk pack's lines cost together,
    /// <paramref name="packCost"/>, shared out among the <paramref name="quantity"/> units it holds. A ₱12 box and ₱0.60 of
    /// tape holding 6 is ₱2.10 a unit. Nothing without a quantity, as with no bulk packaging.
    /// </summary>
    public static decimal CalculateBulkPackagingCost(decimal packCost, int? quantity) =>
        quantity is >= 1 ? packCost / quantity.Value : 0m;

    /// <summary>
    /// Derives QtyPerUnit and UnitsPerPackagingUnit — exact reciprocals of each other — from whichever one the user actually
    /// typed. 0 has no reciprocal, so it comes out as 0 rather than throwing: <see cref="EnsureValidPackagingLines"/> refuses
    /// it by name on the way in, and on the way out (a row only an edit outside the app could leave, since
    /// CK_PackagingLines_AuthoredQuantity) the line costs nothing and can be seen and put right. It used to throw on every
    /// read, which took down Reports, Print and the Recipe API for every product, not just the one with the bad row.
    /// </summary>
    public static (decimal QtyPerUnit, decimal UnitsPerPackagingUnit) ReconcileReciprocal(
        PackagingLineAuthoredField authoredField, decimal authoredValue) =>
        authoredField switch
        {
            PackagingLineAuthoredField.QtyPerUnit => (authoredValue, Reciprocal(authoredValue)),
            PackagingLineAuthoredField.UnitsPerPackagingUnit => (Reciprocal(authoredValue), authoredValue),
            _ => throw new ArgumentOutOfRangeException(nameof(authoredField)),
        };

    private static decimal Reciprocal(decimal value) => value == 0m ? 0m : 1m / value;

    /// <summary>
    /// Cost of one packaging line per sellable unit of the product: QtyPerUnit, in the line's unit,
    /// converted to the material's base units (<paramref name="baseUnitsPerEntryUnit"/>, null when the
    /// line is in the base unit), at the material's cost per base unit.
    /// </summary>
    public static decimal CalculatePackagingLineCost(decimal costPerBaseUnit, decimal qtyPerUnit, decimal? baseUnitsPerEntryUnit) =>
        costPerBaseUnit * UnitConversionRules.ToBaseQuantity(qtyPerUnit, baseUnitsPerEntryUnit);

    /// <summary>
    /// A product's total material cost, per sellable unit: the pieces that unit holds
    /// (<paramref name="quantityRequired"/>) at every sub-assembly usage's cost-per-piece, plus the
    /// packaging that wraps the whole unit. The recipe is authored per piece whatever the quantity is,
    /// so only the usages are multiplied; the packaging lines are already for one whole unit.
    /// </summary>
    public static decimal CalculateMaterialCost(
        IReadOnlyList<decimal> usageCostsPerPiece, int quantityRequired, IReadOnlyList<decimal> packagingCosts) =>
        usageCostsPerPiece.Sum() * quantityRequired + packagingCosts.Sum();
}
