namespace TinapayanRMS.ApplicationCore.Rules;

public static class ProductTabLinkRules
{
    public static void EnsureNotSelfLink(int productId, int linkedProductId)
    {
        if (productId == linkedProductId)
        {
            throw new InvalidOperationException("A product's tab can't be linked to itself.");
        }
    }

    /// <summary>
    /// Walks the chain of tab links starting at (linkedProductId, tabKind, subAssemblyRoleId)
    /// — the tab productId is about to link to — following existingLinkedProductId for the
    /// same tab identity at each hop. Throws if that chain ever leads back to productId,
    /// which would make the tab a link to itself once created.
    /// </summary>
    public static void EnsureNoCycle(
        int productId,
        int linkedProductId,
        Func<int, int?> existingLinkedProductId)
    {
        var current = linkedProductId;
        var visited = new HashSet<int>();

        while (true)
        {
            if (current == productId)
            {
                throw new InvalidOperationException("This link would create a circular chain of tab links.");
            }

            if (!visited.Add(current))
            {
                // A cycle exists among other products' links already, unrelated to
                // productId, but still not safe to link into.
                throw new InvalidOperationException("This product's tab can't be linked here — it would join an existing circular chain of links.");
            }

            var next = existingLinkedProductId(current);
            if (next is null)
            {
                return;
            }

            current = next.Value;
        }
    }
}
