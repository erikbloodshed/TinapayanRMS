using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class PackagingLinkRulesTests
{
    [Test]
    public void EnsureNotSelfLink_rejects_linking_a_product_to_itself()
    {
        var act = () => PackagingLinkRules.EnsureNotSelfLink(productId: 1, linkedProductId: 1);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void EnsureNotSelfLink_accepts_linking_to_a_different_product()
    {
        var act = () => PackagingLinkRules.EnsureNotSelfLink(productId: 1, linkedProductId: 2);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureNoCycle_accepts_linking_to_a_product_with_no_further_links()
    {
        var act = () => PackagingLinkRules.EnsureNoCycle(productId: 1, linkedProductId: 2, existingLinkedProductId: _ => null);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureNoCycle_accepts_a_chain_that_never_loops_back()
    {
        // 1 wants to link to 2; 2 is linked to 3; 3 has no further link.
        var links = new Dictionary<int, int> { [2] = 3 };

        var act = () => PackagingLinkRules.EnsureNoCycle(productId: 1, linkedProductId: 2, existingLinkedProductId: id => links.TryGetValue(id, out var v) ? v : null);

        act.Should().NotThrow();
    }

    [Test]
    public void EnsureNoCycle_rejects_a_direct_two_product_cycle()
    {
        // 1 wants to link to 2, but 2 is already linked to 1 — 1 -> 2 -> 1.
        var links = new Dictionary<int, int> { [2] = 1 };

        var act = () => PackagingLinkRules.EnsureNoCycle(productId: 1, linkedProductId: 2, existingLinkedProductId: id => links.TryGetValue(id, out var v) ? v : null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void EnsureNoCycle_rejects_a_cycle_hidden_several_hops_deep()
    {
        // 1 wants to link to 2; 2 -> 3 -> 4 -> 1 closes the loop several hops down.
        var links = new Dictionary<int, int> { [2] = 3, [3] = 4, [4] = 1 };

        var act = () => PackagingLinkRules.EnsureNoCycle(productId: 1, linkedProductId: 2, existingLinkedProductId: id => links.TryGetValue(id, out var v) ? v : null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void EnsureNoCycle_rejects_joining_a_cycle_that_does_not_involve_productId()
    {
        // 1 wants to link to 2. 2 -> 3 -> 2 is already a cycle between other products,
        // unrelated to 1 — still not safe to link into.
        var links = new Dictionary<int, int> { [2] = 3, [3] = 2 };

        var act = () => PackagingLinkRules.EnsureNoCycle(productId: 1, linkedProductId: 2, existingLinkedProductId: id => links.TryGetValue(id, out var v) ? v : null);

        act.Should().Throw<InvalidOperationException>();
    }
}
