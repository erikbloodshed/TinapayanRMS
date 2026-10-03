using AwesomeAssertions;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Tests.Vim;

/// <summary>VimKeymap: the one table the keys are read from and the cheat sheet is drawn from.</summary>
public class VimKeymapTests
{
    [Fact]
    public void Every_key_says_what_it_does_and_where()
    {
        VimKeymap.Bindings.Should().AllSatisfy(binding =>
        {
            binding.Description.Should().NotBeNullOrWhiteSpace();
            binding.Where.Should().NotBe(VimContexts.None);
        });
    }

    [Fact]
    public void Each_typed_key_means_one_action_wherever_it_works()
    {
        // VimKeySequence reads a key without knowing where it's typed, so its action can't depend on the place; only
        // what the action does there can.
        VimKeymap.Bindings.Where(binding => binding.IsTyped)
            .GroupBy(binding => binding.Keys)
            .Should().AllSatisfy(group => group.Select(binding => binding.Action).Distinct().Should().ContainSingle(group.Key));
    }

    [Fact]
    public void No_key_both_acts_alone_and_starts_a_sequence()
    {
        var prefixes = VimKeymap.Bindings.Where(binding => binding.Keys.Length == 2).Select(binding => binding.Keys[0]).ToHashSet();

        VimKeymap.Bindings.Where(binding => binding.Keys.Length == 1).Select(binding => binding.Keys[0])
            .Should().NotIntersectWith(prefixes);
    }

    [Fact]
    public void No_place_has_one_key_described_twice()
    {
        foreach (var context in new[] { VimContexts.Field, VimContexts.Table, VimContexts.Tree, VimContexts.Other })
        {
            VimKeymap.For(context).Select(binding => binding.Keys).Should().OnlyHaveUniqueItems($"the cheat sheet for {context} lists each key once");
        }
    }

    [Fact]
    public void The_keys_for_a_place_come_before_those_for_anywhere()
    {
        var keys = VimKeymap.For(VimContexts.Field).ToList();

        keys.First().Where.Should().NotBe(VimContexts.Anywhere);
        keys.SkipWhile(binding => binding.Where != VimContexts.Anywhere).Should().OnlyContain(binding => binding.Where == VimContexts.Anywhere);
    }
}
