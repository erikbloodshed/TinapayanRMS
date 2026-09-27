namespace TinapayanRMS.UI.Printing;

/// <summary>
/// One piece of a printout that is never split across pages (a title, a heading, a table's header row, one table row, a
/// caption) as pagination sees it: how tall it is at the page's width, and what it has to stay with.
/// </summary>
/// <param name="Height">Its height at the page's printable width.</param>
/// <param name="KeepWithNext">It never ends a page: it moves to the next page with the block after it, and so on down a chain of them.</param>
/// <param name="Table">The table it belongs to; null outside a table.</param>
/// <param name="RepeatsOnContinuation">
/// One of its <paramref name="Table"/>'s blocks that goes again at the top of every page the table continues on: its heading
/// and its header row.
/// </param>
/// <param name="KeepTogether">
/// A group whose blocks start a new page together when they don't fit the space left but do fit a page of their own.
/// </param>
/// <param name="StartsPage">It begins a page, unless the page is still empty: each product of a set starts on its own.</param>
public readonly record struct PrintBlock(
    double Height,
    bool KeepWithNext = false,
    int? Table = null,
    bool RepeatsOnContinuation = false,
    int? KeepTogether = null,
    bool StartsPage = false);

/// <summary>A block on a page: which one it is, and whether it's its table's heading or header row repeated there.</summary>
public readonly record struct PlacedBlock(int Index, bool IsRepeat = false);

/// <summary>
/// Breaks a printout's blocks into pages. Pure arithmetic over heights, so the rules can be checked without laying out
/// anything (Printing/ProductPrintLayout measures the blocks and builds the pages).
/// </summary>
public static class PrintPagination
{
    /// <summary>
    /// The blocks of each page, in order. Blocks go on a page while they fit; a block kept with the next one moves on with
    /// it; a table continued on a new page starts it with its heading and header row again; a keep-together group that
    /// fits a page of its own starts one rather than being split; a block that starts a page starts one unless the page
    /// is still empty; and a block taller than a whole page gets a page to itself. There is always at least one page.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PlacedBlock>> Paginate(IReadOnlyList<PrintBlock> blocks, double pageHeight)
    {
        List<IReadOnlyList<PlacedBlock>> pages = [];
        List<PlacedBlock> page = [];
        var used = 0.0;

        var index = 0;
        while (index < blocks.Count)
        {
            var block = blocks[index];

            if (block.StartsPage && page.Count > 0)
            {
                StartPage(index);
            }

            // A group kept together moves to a page of its own when it fits one, rather than starting at the bottom of this.
            if (block.KeepTogether is { } group
                && (index == 0 || blocks[index - 1].KeepTogether != group)
                && page.Count > 0)
            {
                var groupHeight = blocks.Skip(index).TakeWhile(b => b.KeepTogether == group).Sum(b => b.Height);
                if (used + groupHeight > pageHeight && groupHeight <= pageHeight)
                {
                    StartPage(index);
                }
            }

            // The run that must travel together: this block and every block it's kept with.
            var runEnd = index;
            while (runEnd < blocks.Count - 1 && blocks[runEnd].KeepWithNext)
            {
                runEnd++;
            }

            var runHeight = Height(index, runEnd);
            if (page.Count > 0 && used + runHeight > pageHeight)
            {
                StartPage(index);
            }

            // A run that can't fit even an empty page goes a block at a time, so it breaks rather than overflowing whole.
            // A block taller than a page still goes on one, alone, and the next block starts another.
            if (used + runHeight > pageHeight)
            {
                runEnd = index;
            }

            for (var i = index; i <= runEnd; i++)
            {
                page.Add(new PlacedBlock(i));
                used += blocks[i].Height;
            }

            index = runEnd + 1;
        }

        pages.Add(page);
        return pages;

        double Height(int from, int to)
        {
            var height = 0.0;
            for (var i = from; i <= to; i++)
            {
                height += blocks[i].Height;
            }

            return height;
        }

        // A new page. When it continues a table begun on an earlier page, it starts with that table's heading and header row.
        void StartPage(int next)
        {
            pages.Add(page);
            page = [];
            used = 0.0;

            if (blocks[next] is { Table: { } table, RepeatsOnContinuation: false } && IsBegun(table, next))
            {
                for (var i = 0; i < next; i++)
                {
                    if (blocks[i] is { RepeatsOnContinuation: true } repeat && repeat.Table == table)
                    {
                        page.Add(new PlacedBlock(i, IsRepeat: true));
                        used += repeat.Height;
                    }
                }
            }
        }

        bool IsBegun(int table, int before)
        {
            for (var i = 0; i < before; i++)
            {
                if (blocks[i].Table == table)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
