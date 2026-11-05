namespace TinapayanRMS.ApplicationCore.Rules;

public static class PackagingLinkRules
{
    /// <summary>What a write that follows a product's packaging links says of a chain that comes round again.</summary>
    public const string LoopText = "Circular packaging link detected.";

    /// <summary>
    /// What the Products screen says of a product whose packaging links come round again: only an edit outside the app
    /// can leave one, since a link that would close one is refused (<see cref="EnsureNoCycle"/>), and it's read all the
    /// same, showing no packaging, rather than fail every product's read (PackagingLineRepository).
    /// </summary>
    public static string LinkedRoundInALoopText(string name) =>
        $"{name}'s packaging links come round in a loop, so it shows no packaging and none is costed. Link it to a product or set outside the loop.";

    public static void EnsureNotSelfLink(int productId, int linkedProductId)
    {
        if (productId == linkedProductId)
        {
            throw new BusinessRuleException("A product's packaging can't be linked to itself.");
        }
    }

    /// <summary>
    /// Walks the chain of packaging links starting at linkedProductId — the product productId is about to
    /// link to — following existingLinkedProductId at each hop. Throws if that chain ever leads back to
    /// productId, which would make its packaging a link to itself once created.
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
                throw new BusinessRuleException("This link would create a circular chain of packaging links.");
            }

            if (!visited.Add(current))
            {
                // A cycle exists among other products' links already, unrelated to
                // productId, but still not safe to link into.
                throw new BusinessRuleException("This product's packaging can't be linked here — it would join an existing circular chain of links.");
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
