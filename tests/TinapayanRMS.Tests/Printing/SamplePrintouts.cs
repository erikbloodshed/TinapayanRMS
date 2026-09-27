using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.Tests.Printing;

/// <summary>Printouts to lay out: a product with one linked Dough of as many lines as a test needs, a box, and a costing.</summary>
internal static class SamplePrintouts
{
    /// <summary>
    /// Syncfusion's default page (PrintSettingsBase), A4 less 2.5 cm margins, in device-independent pixels: what the page
    /// leaves for the printout, worked out as ProductPrintManager.PrintableSize works it out.
    /// </summary>
    public static readonly System.Windows.Size A4Printable = new(793.7 - 94.49 - 94.49, 1122.52 - 94.49 - 94.49);

    public static ProductPrintout WithLines(int lineCount, string? saveNote = null) => new(
        "Ube Roll",
        "Breads",
        "Production",
        "Per piece",
        "Sep 20, 2026, 9:15 AM",
        saveNote,
        new DateTime(2026, 9, 27, 15, 40, 0),
        [
            new PrintedSection(
                "Dough",
                "Variety Bread Dough, from Sub-Assemblies",
                "Baker's %",
                [.. Enumerable.Range(1, lineCount).Select(i => new PrintedLine($"Ingredient {i}", 100m, 10m, CostPerGram: 0.05m, 5m, IsFormulaBase: i == 1))],
                [],
                TotalWeight: 100m * lineCount,
                TotalPercent: 10m * lineCount,
                BatchCost: 5m * lineCount,
                ShrinkagePercent: 10m,
                FinalWeight: 90m * lineCount,
                WeightPerPiece: 60m,
                Yield: 1.5m * lineCount,
                CostPerPiece: 2.5m,
                HasPieceSize: true),
        ],
        new PrintedPackaging(null, [new PrintedPackagingLine("Gift Box", 1m, "pc", 30m, 30m)], 30m),
        new ProductCostSummary([("Dough", 2.5m)], 1, [30m], 25m, 10m, 5m, 30m, 12m, 60m));

    /// <summary>A titled set of products, each named and with as many Dough lines as given, as a folder or All products prints them.</summary>
    public static ProductPrintSet Set(string title, params (string Name, int Lines)[] products) => new(
        title,
        [.. products.Select(p => WithLines(p.Lines) with { ProductName = p.Name })],
        new DateTime(2026, 9, 27, 15, 40, 0));
}
