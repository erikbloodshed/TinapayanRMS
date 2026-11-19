using AwesomeAssertions;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Tests.Fixtures;

namespace TinapayanRMS.Presentation.Tests.Printing;

/// <summary>The print preview's Include ticks, kept for the Windows user: both to start with, and never neither.</summary>
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
    public void A_kept_list_with_no_part_in_it_is_both()
    {
        var viewState = new InMemoryViewStateStore();
        viewState.WriteList(PrintIncludes.Key, ["kitchen"]);

        var includes = new PrintIncludes(viewState);

        (includes.Recipe, includes.Costing).Should().Be((true, true));
    }
}
