namespace TinapayanRMS.Presentation.Printing;

/// <summary>
/// A printout of tables rather than products: a Reports table as it's shown, or a sub-assembly's recipe on its own. Its
/// cells are already text, as the screen shows them. It prints as a product's printout does, on Settings → Printing's
/// page, with the shop's heading, the pages worked out and "Continued on page" under a table that goes on.
/// </summary>
/// <param name="Title">What it is, the page's title: "Product Cost Summary", "Asado Roll Dough".</param>
/// <param name="Lines">Lines under the title: what it shows, how many rows, a note.</param>
/// <param name="Tables">The tables, one under another.</param>
/// <param name="PrintedAt">When it was prepared, which the page says.</param>
public sealed record TablePrintout(string Title, IReadOnlyList<string> Lines, IReadOnlyList<PrintedTable> Tables, DateTime PrintedAt)
{
    /// <summary>A note that its figures are as the screen shows them, unsaved edits included; null when they're as saved.</summary>
    public string? SaveNote { get; init; }
}

/// <summary>One table of a <see cref="TablePrintout"/>: a heading over it (null for none), its columns, its rows and a total row.</summary>
public sealed record PrintedTable(string? Heading, IReadOnlyList<PrintedColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>A last row in bold, printed once after the others; null for none.</summary>
    public IReadOnlyList<string>? Total { get; init; }

    /// <summary>A line under the table, as a recipe's piece size is; null for none.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// A column of a <see cref="PrintedTable"/>: its heading, whether it's a figure, set at the right, and whether its cells
/// keep to one line, as a date's do.
/// </summary>
public sealed record PrintedColumn(string Header, bool IsNumber = false, bool KeepsToOneLine = false);
