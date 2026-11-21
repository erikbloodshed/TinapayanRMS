using AwesomeAssertions;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Tests.Fixtures;

namespace TinapayanRMS.Presentation.Tests.Printing;

/// <summary>
/// The print preview's Include ticks, kept for the Windows user: both parts to start with, and never neither; the
/// sub-assembly total in place of the costing.
/// </summary>
public class PrintIncludesTests
{
    [Test]
    public void Both_parts_are_included_until_one_is_cleared()
    {
        var includes = new PrintIncludes(new InMemoryViewStateStore());

        (includes.Recipe, includes.Costing).Should().Be((true, true));
    }

    [Test]
    public void A_part_cleared_stays_cleared_for_the_next_preview()
    {
        var viewState = new InMemoryViewStateStore();
        new PrintIncludes(viewState).Costing = false;

        var next = new PrintIncludes(viewState);

        (next.Recipe, next.Costing).Should().Be((true, false));
        viewState.ReadList(PrintIncludes.Key).Should().Equal("recipe");
    }

    [Test]
    public void The_last_part_included_cannot_be_cleared()
    {
        var includes = new PrintIncludes(new InMemoryViewStateStore()) { Recipe = false };

        includes.Costing = false;

        (includes.Recipe, includes.Costing).Should().Be((false, true));
    }

    [Test]
    public void Production_copy_is_off_until_ticked_and_stays_as_the_parts_change()
    {
        var viewState = new InMemoryViewStateStore();
        var includes = new PrintIncludes(viewState);
        includes.ProductionCopy.Should().BeFalse();

        includes.ProductionCopy = true;
        includes.Costing = false;

        var next = new PrintIncludes(viewState);
        (next.Recipe, next.Costing, next.ProductionCopy).Should().Be((true, false, true));
        viewState.ReadList(PrintIncludes.Key).Should().Equal("recipe", "production");
    }

    [Test]
    public void Sub_assembly_total_takes_the_costings_place_and_is_kept()
    {
        var viewState = new InMemoryViewStateStore();
        var includes = new PrintIncludes(viewState) { Recipe = false };
        includes.SubAssemblyTotal.Should().BeFalse();

        includes.SubAssemblyTotal = true;

        var next = new PrintIncludes(viewState);
        (next.Recipe, next.Costing, next.SubAssemblyTotal).Should().Be((true, false, true), "it's the recipe's, in place of the costing");
        viewState.ReadList(PrintIncludes.Key).Should().Equal("recipe", "sub-assembly-total");
    }

    [Test]
    public void Ticking_costing_clears_the_sub_assembly_total_and_clearing_it_leaves_the_recipe()
    {
        var includes = new PrintIncludes(new InMemoryViewStateStore()) { SubAssemblyTotal = true };

        includes.Costing = true;
        (includes.Recipe, includes.Costing, includes.SubAssemblyTotal).Should().Be((true, true, false));

        includes.SubAssemblyTotal = true;
        includes.SubAssemblyTotal = false;
        (includes.Recipe, includes.Costing, includes.SubAssemblyTotal).Should().Be((true, false, false));
    }

    [Test]
    public void A_kept_list_with_no_part_in_it_is_both()
    {
        var viewState = new InMemoryViewStateStore();
        viewState.WriteList(PrintIncludes.Key, ["kitchen"]);

        var includes = new PrintIncludes(viewState);

        (includes.Recipe, includes.Costing).Should().Be((true, true));
    }
}
