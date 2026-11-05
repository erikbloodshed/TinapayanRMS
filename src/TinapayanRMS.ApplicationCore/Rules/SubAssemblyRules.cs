using System.Globalization;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class SubAssemblyRules
{
    /// <summary>A sub-assembly needs at least one line, positive grams, and no ingredient used twice.</summary>
    public static void EnsureValidLines(IReadOnlyList<RecipeLine> lines)
    {
        if (lines.Count == 0)
        {
            throw new BusinessRuleException("A sub-assembly needs at least one line.");
        }

        if (lines.Any(l => l.Grams <= 0))
        {
            throw new BusinessRuleException("A sub-assembly line's grams must be positive.");
        }

        if (lines.Select(l => l.IngredientId).Distinct().Count() != lines.Count)
        {
            throw new BusinessRuleException("A sub-assembly can't use the same ingredient more than once — combine it into a single line instead.");
        }

        // The base is 100% between its lines by definition, so a base line is always given in grams.
        if (lines.Any(l => l.IsFormulaBase && l.AuthoredBakersPercent is not null))
        {
            throw new BusinessRuleException("A base line is given in grams, not as a percentage.");
        }

        if (lines.Any(l => l.AuthoredBakersPercent is <= 0))
        {
            throw new BusinessRuleException("A sub-assembly line's baker's percentage must be positive.");
        }
    }

    /// <summary>
    /// A packaging set needs at least one line, a positive quantity per set, and no material used twice. The
    /// materials' type is checked where they're loaded (RawMaterialRules.EnsureUsableAsPackaging), and each
    /// line's unit is checked there too: it must be one of that material's own units, or null for its base unit.
    /// </summary>
    public static void EnsureValidPackagingLines(IReadOnlyList<(int PackagingMaterialId, int? UnitId, decimal QtyPerSet)> lines)
    {
        if (lines.Count == 0)
        {
            throw new BusinessRuleException("A packaging set needs at least one line.");
        }

        if (lines.Any(l => l.QtyPerSet <= 0))
        {
            throw new BusinessRuleException("A packaging set line's quantity must be positive.");
        }

        if (lines.Select(l => l.PackagingMaterialId).Distinct().Count() != lines.Count)
        {
            throw new BusinessRuleException("A packaging set can't use the same material more than once — put its quantity on a single line instead.");
        }
    }

    /// <summary>
    /// A saved sub-assembly keeps its kind: a recipe (ingredients in grams) can't move to the Packaging role, nor a
    /// packaging set to a recipe role, since its lines are of the other kind and products use the two differently.
    /// </summary>
    public static void EnsureKindUnchanged(bool wasPackaging, bool isPackaging)
    {
        if (wasPackaging != isPackaging)
        {
            throw new BusinessRuleException(wasPackaging
                ? "A packaging set stays in the Packaging role. Make a new sub-assembly for a recipe instead."
                : "A recipe can't move to the Packaging role. Make a new sub-assembly for a packaging set instead.");
        }
    }

    /// <summary>
    /// A recipe's sub-assemblies are ingredients in grams, so none of them takes the Packaging role: a product's
    /// packaging is its Packaging tab, which can link a packaging set instead.
    /// </summary>
    public static void EnsureRecipeRole(SubAssemblyRole role)
    {
        if (role.IsPackaging)
        {
            throw new BusinessRuleException(
                $"The {role.Name} role is for packaging sets on the Sub-Assemblies screen, not a recipe section. A product takes one as its packaging: Add Sub-Assembly in the Packaging role.");
        }
    }

    /// <summary>
    /// A product's own sub-assembly is copied by other products, never linked: it's edited in its product, which can
    /// change or remove it at any time. Create Variant alone links it, and those links aren't checked here.
    /// <paramref name="ownerName"/> is its product's, or null for one on the Sub-Assemblies screen, which any product can link.
    /// </summary>
    public static void EnsureLinkable(string subAssemblyName, string? ownerName)
    {
        if (ownerName is not null)
        {
            throw new BusinessRuleException(
                $"{subAssemblyName} is {ownerName}'s own, so it can't be linked. Copy it instead: Add Sub-Assembly, then Copy an existing one.");
        }
    }

    /// <summary>
    /// What a made ingredient can be made from: a recipe kept on the Sub-Assemblies screen, the one place it's
    /// written. Not a packaging set, which has no ingredients, and not a product's own, which its product changes or removes
    /// at any time. <paramref name="ownerName"/> is its product's, or null for one on the Sub-Assemblies screen.
    /// </summary>
    public static void EnsureLinkableByMadeIngredient(string subAssemblyName, string? ownerName, bool isPackaging)
    {
        if (isPackaging)
        {
            throw new BusinessRuleException($"{subAssemblyName} is a packaging set. A made ingredient is made from a recipe of ingredients.");
        }

        if (ownerName is not null)
        {
            throw new BusinessRuleException(
                $"{subAssemblyName} is {ownerName}'s own, so a made ingredient can't be made from it. Move it to Sub-Assemblies from that product's recipe first.");
        }
    }

    /// <summary>
    /// A sub-assembly can't be deleted while a made ingredient is made from it: it's that ingredient's recipe.
    /// <paramref name="madeIngredientNames"/> are theirs, by name.
    /// </summary>
    public static void EnsureNoMadeIngredientMadeFrom(string subAssemblyName, IReadOnlyList<string> madeIngredientNames)
    {
        if (madeIngredientNames.Count > 0)
        {
            var one = madeIngredientNames.Count == 1;
            throw new BusinessRuleException(
                $"{string.Join(", ", madeIngredientNames)} {(one ? "is" : "are")} made from {subAssemblyName}, so it can't be deleted. "
                + $"On the Raw Materials screen, make {(one ? "it" : "them")} from another sub-assembly, or delete {(one ? "it" : "them")}, first.");
        }
    }

    /// <summary>
    /// The section name a product's own sub-assembly is saved with in <paramref name="roleName"/>
    /// (SubAssembly.SectionName): <paramref name="sectionName"/> trimmed, or null when that's nothing or just the role's
    /// name, which its name leaves out too (<see cref="StandaloneNameFor"/>).
    /// </summary>
    public static string? StoredSectionName(string? sectionName, string roleName) =>
        NormalizeName(sectionName) is { } section && !string.Equals(section, roleName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? section
            : null;

    /// <summary>
    /// Whether <paramref name="name"/> is the name a product's own section is saved under (<see cref="StandaloneNameFor"/>,
    /// numbered by <see cref="UniqueStandaloneName"/> when that was taken), so it's named after its product and role again
    /// when either is renamed. One named otherwise, such as a sub-assembly from the Sub-Assemblies screen a product came
    /// to own, keeps its name.
    /// </summary>
    public static bool IsNamedAfter(string name, string productName, string roleName, string? sectionName)
    {
        var expected = StandaloneNameFor(productName, roleName, sectionName);
        var trimmed = name.Trim();
        if (string.Equals(trimmed, Numbered(expected, 1), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Numbered, "Cheese Roll Filling 2", and cut short when the whole wouldn't have fitted, as UniqueStandaloneName makes it.
        var space = trimmed.LastIndexOf(' ');
        return space > 0
            && int.TryParse(trimmed.AsSpan(space + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= 2
            && string.Equals(trimmed, Numbered(expected, number), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What <paramref name="subAssembly"/> goes by beside its role in product <paramref name="productId"/>'s recipe: the
    /// section name it's saved with when it's that product's own, else what its name gives there (<see cref="SectionLabel"/>).
    /// </summary>
    public static string? LabelIn(SubAssembly subAssembly, int productId, string? productName, string roleName) =>
        subAssembly.OwnerProductId == productId
            ? NormalizeName(subAssembly.SectionName)
            : SectionLabel(subAssembly.Name, productName, roleName);

    /// <summary>
    /// The section name a product's own sub-assembly saved before section names were stored goes by: its
    /// label as it was read from its name then (<see cref="SectionLabel"/>), with "Cheese Roll Filling 2", numbered when
    /// "Cheese Roll Filling" was taken, read as no name rather than as all of it. For that one-time upgrade only.
    /// </summary>
    public static string? SectionNameFromSavedName(string name, string productName, string roleName) =>
        IsNamedAfter(name, productName, roleName, null) ? null : StoredSectionName(SectionLabel(name, productName, roleName), roleName);

    /// <summary>The Packaging role holds packaging sets from the Sub-Assemblies screen, so it's never product-owned.</summary>
    public static void EnsureProductOwnedAllowed(SubAssemblyRole role)
    {
        if (role.IsPackaging && role.IsProductOwned)
        {
            throw new BusinessRuleException(
                $"The {role.Name} role holds packaging sets from the Sub-Assemblies screen, so it can't be product-owned.");
        }
    }

    /// <summary>
    /// A batch from its lines: TotalWeight = Σ grams; TotalCost = Σ (grams × the line's ingredient cost per gram as given,
    /// its buffered cost or, for the unbuffered view, its true cost). Nothing stores it: a sub-assembly is costed from its
    /// lines whenever it's read.
    /// </summary>
    public static (decimal TotalWeight, decimal TotalCost) CalculateTotals(IReadOnlyList<(decimal Grams, decimal IngredientCostPerGram)> lines)
    {
        var totalWeight = lines.Sum(l => l.Grams);
        var totalCost = lines.Sum(l => l.Grams * l.IngredientCostPerGram);
        return (totalWeight, totalCost);
    }

    /// <summary>
    /// The formula's 100% base: the weight of the lines ticked as base, which is what every line's baker's
    /// percentage is read against. Usually the flour, and more than one flour ticks together to make 100%
    /// between them. Nothing ticked gives 0, which means the formula has no base.
    /// </summary>
    public static decimal FormulaBaseWeight(IReadOnlyList<(decimal Grams, bool IsFormulaBase)> lines) =>
        lines.Where(l => l.IsFormulaBase).Sum(l => l.Grams);

    /// <summary>
    /// A line in baker's percentage: its weight against the formula's base (<see cref="FormulaBaseWeight"/>),
    /// so the base itself comes to 100 and 650 g of water on 1,000 g of flour comes to 65. Null when nothing
    /// is ticked — a glaze or a filling needn't have a base, and no figure is the honest answer, not zero.
    /// </summary>
    public static decimal? CalculateBakersPercent(decimal grams, decimal baseWeight) =>
        baseWeight <= 0 ? null : grams / baseWeight * 100m;

    /// <summary>
    /// A line in absolute percentage: its weight against the whole batch, so every line together comes to 100 and 150 g
    /// of butter in a 1,000 g glaze comes to 15. What a formula with nothing ticked as its base shows instead of baker's
    /// percentage. Null for an empty batch.
    /// </summary>
    public static decimal? CalculateAbsolutePercent(decimal grams, decimal batchWeight) =>
        batchWeight <= 0 ? null : grams / batchWeight * 100m;

    /// <summary>
    /// The other way: a line's grams from its baker's percentage, so 65% on 1,000 g of flour is 650 g. What a line typed as
    /// a percentage is worked out from, again, whenever the base weight changes. Rounded to the 4 decimals grams are stored
    /// in, so a line saved this way reads back exactly as it was worked out — unrounded, reopening it could move its grams
    /// by a ten-thousandth and make a recipe nobody touched look changed.
    /// </summary>
    public static decimal GramsFromBakersPercent(decimal percent, decimal baseWeight) =>
        Math.Round(baseWeight * percent / 100m, 4);

    /// <summary>
    /// A line's grams in a batch scaled from weighing <paramref name="from"/> to <paramref name="to"/> (the
    /// Sub-Assemblies screen's Scale): the batch or its base, every line by the same factor, so each keeps its percentage.
    /// Rounded to the 4 decimals grams are stored in, as <see cref="GramsFromBakersPercent"/> is. Nothing to scale from
    /// leaves the grams as they are.
    /// </summary>
    public static decimal ScaledGrams(decimal grams, decimal from, decimal to) =>
        from <= 0m ? grams : Math.Round(grams * to / from, 4);

    /// <summary>The longest name a sub-assembly can have (the Name column's length).</summary>
    public const int MaxNameLength = 100;

    /// <summary>A name as it's saved: trimmed, and null when nothing's left.</summary>
    public static string? NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    /// <summary>
    /// What a sub-assembly first written in a product's Recipe Structure is saved as on the Sub-Assemblies screen: the
    /// product and its role, "Cheese Roll Dough", and the section's own name after them when it has one that isn't just
    /// the role's, "Cheese Roll Filling: Cheese". Not yet unique (<see cref="UniqueStandaloneName"/>) nor cut to length.
    /// </summary>
    public static string StandaloneNameFor(string productName, string roleName, string? sectionName)
    {
        var baseName = $"{productName.Trim()} {roleName.Trim()}";
        return NormalizeName(sectionName) is { } section && !string.Equals(section, roleName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? $"{baseName}: {section}"
            : baseName;
    }

    /// <summary>
    /// <paramref name="name"/>, or the first of "name 2", "name 3", … that no sub-assembly in <paramref name="taken"/> has
    /// (case- and space-insensitively), cut so the whole still fits <see cref="MaxNameLength"/>.
    /// </summary>
    public static string UniqueStandaloneName(string name, IReadOnlyCollection<string> taken)
    {
        var names = taken.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var candidate = Numbered(name, n);
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// <paramref name="name"/> as <see cref="UniqueStandaloneName"/> tries it the <paramref name="number"/>th time: as it is
    /// first, then "name 2", "name 3", …, cut so the whole fits <see cref="MaxNameLength"/>.
    /// </summary>
    private static string Numbered(string name, int number)
    {
        var trimmed = name.Trim();
        var suffix = number == 1 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" {number}");
        var stem = trimmed.Length + suffix.Length > MaxNameLength ? trimmed[..(MaxNameLength - suffix.Length)].TrimEnd() : trimmed;
        return stem + suffix;
    }

    /// <summary>
    /// What a linked sub-assembly goes by beside its role in a product's recipe, once the recipe has more than one in
    /// that role: its name, less the "Cheese Roll Filling: " it was saved under by that product
    /// (<see cref="StandaloneNameFor"/>), so its tab reads "Filling: Cheese"; and nothing when it's just "Cheese Roll
    /// Filling", which was saved from that product without a name of its own, so it goes by its role alone there, as it
    /// did before it was saved. A product's own goes by its stored section name instead (<see cref="LabelIn"/>).
    /// </summary>
    public static string? SectionLabel(string subAssemblyName, string? productName, string roleName)
    {
        if (NormalizeName(productName) is { } product)
        {
            var saved = StandaloneNameFor(product, roleName, null);
            if (string.Equals(subAssemblyName.Trim(), saved, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var prefix = saved + ": ";
            if (subAssemblyName.Length > prefix.Length && subAssemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return subAssemblyName[prefix.Length..].Trim();
            }
        }

        return subAssemblyName;
    }

    /// <summary>
    /// A product can have several sub-assemblies in one role (an ube filling and a cheese filling), its own and the ones
    /// it links together, as long as they can be told apart: within a role no two go by the same name
    /// (<see cref="SectionLabel"/>, case- and space-insensitively), and at most one goes without one. Its tab reads
    /// "Filling: Ube" beside "Filling: Cheese", or "Filling" beside "Filling: Cheese".
    /// </summary>
    /// <param name="recipe">Whose recipe the message is about: "This recipe", or a product's.</param>
    public static void EnsureSectionsDistinct(IReadOnlyList<(int RoleId, string RoleName, string? Label)> sections, string recipe = "This recipe")
    {
        foreach (var role in sections.GroupBy(s => s.RoleId))
        {
            var clash = role.GroupBy(s => NormalizeName(s.Label), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (clash is null)
            {
                continue;
            }

            var roleName = role.First().RoleName;
            throw new BusinessRuleException(clash.Key is null
                ? $"{recipe} has more than one {roleName} without a name. Name them, such as Ube and Cheese, so their tabs can be told apart."
                : $"{recipe} has two {roleName} sub-assemblies called {clash.Key}. Give one of them another name.");
        }
    }

    /// <summary>The name typed for a new recipe section is optional, and at most <see cref="MaxNameLength"/> characters.</summary>
    public static void EnsureValidSectionName(string? name)
    {
        if (NormalizeName(name) is { Length: > MaxNameLength } tooLong)
        {
            throw new BusinessRuleException($"'{tooLong[..20]}…' is too long for a sub-assembly's name: keep it to {MaxNameLength} characters.");
        }
    }

    /// <summary>
    /// A sub-assembly needs a name no other has (case- and space-insensitively): products pick them by name.
    /// <paramref name="otherStandaloneNames"/> leaves out the one being saved.
    /// </summary>
    public static void EnsureStandaloneNameAvailable(string name, IReadOnlyCollection<string> otherStandaloneNames)
    {
        var trimmed = name.Trim();
        if (otherStandaloneNames.Any(n => string.Equals(n.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessRuleException($"There's already a sub-assembly named '{trimmed}'. Give this one its own name.");
        }
    }

    /// <summary>The most a raw material's name can be (RawMaterialConfiguration).</summary>
    public const int MaxIngredientNameLength = 100;

    /// <summary>
    /// The made ingredient made from a sub-assembly by Move to In-House (on the Products screen) or Make In-House Ingredient
    /// (on the Sub-Assemblies screen) needs a name of its
    /// own: at most <see cref="MaxIngredientNameLength"/> characters, and no other raw material's (<paramref name="isNameTaken"/>).
    /// </summary>
    public static void EnsureValidMadeIngredientName(string ingredientName, bool isNameTaken)
    {
        var name = ingredientName.Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("Give the made ingredient a name.");
        }

        if (name.Length > MaxIngredientNameLength)
        {
            throw new BusinessRuleException($"'{name[..20]}…' is too long for a raw material's name: keep it to {MaxIngredientNameLength} characters.");
        }

        if (isNameTaken)
        {
            throw new BusinessRuleException($"There's already a raw material named '{name}'. Give the made ingredient its own name.");
        }
    }

    /// <summary>
    /// A sub-assembly another product links can't be deleted from under it: that product has to copy it
    /// or remove it first. <paramref name="linkingProductNames"/> are the products whose recipes link it.
    /// </summary>
    public static void EnsureNotLinkedElsewhere(string subAssemblyName, IReadOnlyCollection<string> linkingProductNames)
    {
        if (linkingProductNames.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", linkingProductNames.Order(StringComparer.CurrentCultureIgnoreCase));
        var verb = linkingProductNames.Count == 1 ? "links" : "link";
        throw new BusinessRuleException(
            $"{names} {verb} {subAssemblyName}. Make a copy of it there, or remove it there, first.");
    }
}
