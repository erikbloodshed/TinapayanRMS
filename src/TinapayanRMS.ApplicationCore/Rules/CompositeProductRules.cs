namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A composite product: one made of other products, its own sections in product-owned roles (a topping, a filling) and its
/// own packaging (Product.IsComposite), each product in it at a whole number of its sellable units (ProductComponent).
/// </summary>
public static class CompositeProductRules
{
    /// <summary>A composite is "two or more products" (the user's definition): six of one product is a Box of 6, which Quantity Required covers.</summary>
    public const int MinimumProducts = 2;

    /// <summary>
    /// A composite's products: at least <see cref="MinimumProducts"/> different ones, each at a quantity of 1 or more,
    /// none of them the composite itself, and none on two lines (put its quantity on one).
    /// </summary>
    /// <param name="productId">The composite's id; 0 for one not saved yet, which nothing can contain.</param>
    /// <param name="components">Each line's product, its quantity and its product's name, for the messages.</param>
    public static void EnsureValidComponents(int productId, IReadOnlyList<(int ComponentProductId, int Quantity, string Name)> components)
    {
        if (productId > 0 && components.Any(c => c.ComponentProductId == productId))
        {
            throw new BusinessRuleException("A composite product can't contain itself.");
        }

        var notCounted = components.Where(c => c.Quantity < 1).Select(c => c.Name).FirstOrDefault();
        if (notCounted is not null)
        {
            throw new BusinessRuleException($"Enter how many {notCounted} go in: a whole number, 1 or more.");
        }

        var duplicate = components.GroupBy(c => c.ComponentProductId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessRuleException(
                $"{duplicate.First().Name} is on two lines. Put its quantity on one line and delete the other.");
        }

        if (components.Count < MinimumProducts)
        {
            throw new BusinessRuleException("A composite product needs at least two different products.");
        }
    }

    /// <summary>
    /// No composite may end up containing itself, directly or through another composite: a Party Package holding a
    /// Pasalubong Box can't then go into that Pasalubong Box. <paramref name="componentIdsByProductId"/> is what every
    /// OTHER composite holds now (<paramref name="productId"/>'s own lines, about to be replaced, are left out by the
    /// caller), and <paramref name="newComponentIds"/> what <paramref name="productId"/> is to hold.
    /// </summary>
    /// <param name="nameOf">A product's name, for the message.</param>
    public static void EnsureNoCycle(
        int productId,
        IEnumerable<int> newComponentIds,
        IReadOnlyDictionary<int, IReadOnlyList<int>> componentIdsByProductId,
        Func<int, string> nameOf)
    {
        foreach (var componentId in newComponentIds)
        {
            if (componentId == productId)
            {
                throw new BusinessRuleException("A composite product can't contain itself.");
            }

            if (Contains(componentId, productId, componentIdsByProductId))
            {
                throw new BusinessRuleException(
                    $"{nameOf(componentId)} can't go into {nameOf(productId)}: it already contains {nameOf(productId)}, so {nameOf(productId)} would end up containing itself.");
            }
        }
    }

    /// <summary>Whether <paramref name="containerId"/> holds <paramref name="targetId"/>, directly or through the composites in it.</summary>
    public static bool Contains(int containerId, int targetId, IReadOnlyDictionary<int, IReadOnlyList<int>> componentIdsByProductId)
    {
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(containerId);

        while (stack.TryPop(out var current))
        {
            if (!visited.Add(current) || !componentIdsByProductId.TryGetValue(current, out var components))
            {
                continue;
            }

            foreach (var component in components)
            {
                if (component == targetId)
                {
                    return true;
                }

                stack.Push(component);
            }
        }

        return false;
    }

    /// <summary>
    /// A composite's own sections are only in product-owned roles (SubAssemblyRole.IsProductOwned), Settings' checkbox for
    /// what's finished on the product itself: a topping, a filling. What it's made of otherwise is its products.
    /// </summary>
    /// <param name="compositeName">The composite's name, for the message.</param>
    /// <param name="roles">Each section's role: its name, and whether it's product-owned.</param>
    public static void EnsureSectionRoles(string compositeName, IEnumerable<(string Name, bool IsProductOwned)> roles)
    {
        var notOwned = roles.Where(r => !r.IsProductOwned).Select(r => r.Name).FirstOrDefault();
        if (notOwned is not null)
        {
            throw new BusinessRuleException(
                $"{compositeName} can only have sections in product-owned roles, such as a topping or filling. {notOwned} isn't one: tick Product-owned for it in Settings ▸ Roles, or take that section out.");
        }
    }

    /// <summary>Whether a product is a composite is picked when it's made, and never changes afterward.</summary>
    public static void EnsureKindUnchanged(bool storedIsComposite, bool isComposite)
    {
        if (storedIsComposite != isComposite)
        {
            throw new BusinessRuleException(
                storedIsComposite
                    ? "A composite product can't become a single product once it's saved."
                    : "A product can't become a composite product once it's saved. Make a new composite product instead.");
        }
    }

    /// <summary>A composite is one sellable unit whatever it holds: its products say how many of each go in.</summary>
    public static void EnsureOneUnit(bool isComposite, int quantityRequired)
    {
        if (isComposite && quantityRequired != 1)
        {
            throw new BusinessRuleException(
                "A composite product is one sellable unit. Say how many of each product go in on its Products table instead.");
        }
    }

    /// <summary>Only a composite's products can bring their packaging: a product of sub-assemblies holds no products.</summary>
    public static void EnsureComponentPackagingOnComposite(bool isComposite, bool includesComponentPackaging)
    {
        if (!isComposite && includesComponentPackaging)
        {
            throw new BusinessRuleException(
                "Only a composite product can include the packaging of the products in it.");
        }
    }

    /// <summary>
    /// What one line brings into its composite's cost: <paramref name="quantity"/> of the product's sellable units at its
    /// recipe (sub-assemblies, ingredients) cost for one, its packaging left out.
    /// </summary>
    public static decimal CalculateComponentCost(decimal recipeCostPerUnit, int quantity) => recipeCostPerUnit * quantity;

    /// <summary>
    /// What one line brings into its composite's packaging when the composite includes its products' packaging
    /// (Product.IncludesComponentPackaging): <paramref name="quantity"/> of the product's sellable units at its packaging
    /// cost for one.
    /// </summary>
    public static decimal CalculateComponentPackagingCost(decimal packagingCostPerUnit, int quantity) => packagingCostPerUnit * quantity;
}
