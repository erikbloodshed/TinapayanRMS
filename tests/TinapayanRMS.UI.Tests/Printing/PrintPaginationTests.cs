using AwesomeAssertions;
using TinapayanRMS.UI.Printing;

namespace TinapayanRMS.UI.Tests.Printing;

/// <summary>
/// PrintPagination: how a printout's blocks fall into pages, by their heights alone. Each page is written as its blocks'
/// indexes, "0 1 2", with an r after a table's heading or header row repeated at the top of a page: "0r 1r 4".
/// </summary>
public class PrintPaginationTests
{
    [Fact]
    public void Blocks_that_fit_go_on_one_page()
    {
        Pages([new(30), new(30), new(30)]).Should().Equal("0 1 2");
    }

    [Fact]
    public void A_block_that_doesnt_fit_starts_the_next_page()
    {
        Pages([new(40), new(40), new(40)]).Should().Equal("0 1", "2");
    }

    [Fact]
    public void A_heading_never_ends_a_page_it_moves_on_with_what_follows_it()
    {
        // The heading and the header row would fit under the first block, but the row they head wouldn't.
        Pages([new(60), new(15, KeepWithNext: true), new(15, KeepWithNext: true), new(20)]).Should().Equal("0", "1 2 3");
    }

    [Fact]
    public void A_table_continued_on_a_new_page_starts_it_with_its_heading_and_header_row_again()
    {
        PrintBlock[] blocks =
        [
            new(20, KeepWithNext: true, Table: 1, RepeatsOnContinuation: true),
            new(10, KeepWithNext: true, Table: 1, RepeatsOnContinuation: true),
            new(30, Table: 1),
            new(30, Table: 1),
            new(30, Table: 1),
            new(30, Table: 1),
        ];

        Pages(blocks).Should().Equal("0 1 2 3", "0r 1r 4 5");
    }

    [Fact]
    public void A_table_that_starts_on_a_new_page_has_nothing_repeated()
    {
        PrintBlock[] blocks =
        [
            new(80),
            new(20, KeepWithNext: true, Table: 1, RepeatsOnContinuation: true),
            new(10, KeepWithNext: true, Table: 1, RepeatsOnContinuation: true),
            new(30, Table: 1),
        ];

        Pages(blocks).Should().Equal("0", "1 2 3");
    }

    [Fact]
    public void A_table_continuing_repeats_only_its_own_heading_and_header_row()
    {
        PrintBlock[] blocks =
        [
            new(10, KeepWithNext: true, Table: 1, RepeatsOnContinuation: true),
            new(30, Table: 1),
            new(10, KeepWithNext: true, Table: 2, RepeatsOnContinuation: true),
            new(30, Table: 2),
            new(30, Table: 2),
        ];

        Pages(blocks).Should().Equal("0 1 2 3", "2r 4");
    }

    [Fact]
    public void A_group_kept_together_starts_a_page_of_its_own_when_it_fits_one()
    {
        // Two of the group's blocks would fit under the first one; the group as a whole fits only a page of its own.
        Pages([new(50), new(20, KeepTogether: 1), new(20, KeepTogether: 1), new(20, KeepTogether: 1)]).Should().Equal("0", "1 2 3");
    }

    [Fact]
    public void A_group_kept_together_that_no_page_can_hold_is_split_where_it_runs_out()
    {
        Pages([new(50), new(40, KeepTogether: 1), new(40, KeepTogether: 1), new(40, KeepTogether: 1)]).Should().Equal("0 1", "2 3");
    }

    [Fact]
    public void A_block_taller_than_a_page_gets_a_page_to_itself()
    {
        Pages([new(30), new(250), new(30)]).Should().Equal("0", "1", "2");
    }

    [Fact]
    public void Blocks_kept_together_that_no_page_can_hold_break_rather_than_overflowing_whole()
    {
        // A chain of headings taller than a page goes a block at a time rather than all on one page.
        Pages([new(60, KeepWithNext: true), new(60, KeepWithNext: true), new(60)]).Should().Equal("0", "1", "2");
    }

    [Fact]
    public void A_block_that_starts_a_page_starts_one_though_it_would_fit_under_the_last()
    {
        Pages([new(20), new(20, StartsPage: true), new(20), new(20, StartsPage: true)]).Should().Equal("0", "1 2", "3");
    }

    [Fact]
    public void A_block_that_starts_a_page_leaves_no_empty_page_before_it()
    {
        // First on the document, and first on a page the block before it has just filled.
        Pages([new(20, StartsPage: true), new(80), new(20, StartsPage: true)]).Should().Equal("0 1", "2");
        Pages([new(100), new(20, StartsPage: true)]).Should().Equal("0", "1");
    }

    [Fact]
    public void There_is_always_a_page()
    {
        Pages([]).Should().Equal(string.Empty);
    }

    private static List<string> Pages(PrintBlock[] blocks) =>
    [
        .. PrintPagination.Paginate(blocks, pageHeight: 100)
            .Select(page => string.Join(' ', page.Select(block => block.IsRepeat ? $"{block.Index}r" : $"{block.Index}"))),
    ];
}
