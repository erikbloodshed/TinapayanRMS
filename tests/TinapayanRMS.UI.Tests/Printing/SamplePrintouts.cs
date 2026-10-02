using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.Printing;

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

    /// <summary>
    /// A composite, Pasalubong Box: 6 Ensaymada at 2.50 of recipe each and 4 Cheese Rolls at 5.00, 35.00 between them, and
    /// its gift box.
    /// </summary>
    public static ProductPrintout Composite() => WithLines(0) with
    {
        ProductName = "Pasalubong Box",
        CostUnitCaption = "Per unit",
        Sections = [],
        Components = [new PrintedComponent("Ensaymada", 6, 2.50m, 15m), new PrintedComponent("Cheese Roll", 4, 5m, 20m)],
        Costs = new ProductCostSummary([("Ensaymada × 6", 15m), ("Cheese Roll × 4", 20m)], 1, [30m], 25m, 10m, 5m, 30m, 12m, 120m, isComposite: true),
    };

    /// <summary>
    /// Ube Roll's Scaling tab at 20 boxes of 8, 160 pieces: a Dough of 2 batches, with flour and as many more lines as
    /// given, and syrup, which opens into the sugar it's made of; then a box for each unit.
    /// </summary>
    public static ProductPrintout Scaled(int extraLines = 0)
    {
        ScalingRow[] doughLines =
        [
            ScalingRow.Line("Flour", string.Empty, "1,800 g", 90m),
            new("Syrup", string.Empty, "200 g", 18m, false, [ScalingRow.Line("Sugar", string.Empty, "200 g", 18m)]),
            .. Enumerable.Range(1, extraLines).Select(i => ScalingRow.Line($"Ingredient {i}", string.Empty, "10 g", 1m)),
        ];
        ScalingRow[] rows =
        [
            new("Dough", "2 batches · 200 pcs (40 extra)", "2,000 g", doughLines.Sum(r => r.Cost), true, doughLines),
            new("Packaging", string.Empty, string.Empty, 600m, true, [ScalingRow.Line("Gift Box", string.Empty, "20 pc", 600m)]),
        ];

        return WithLines(1) with
        {
            CostUnitCaption = "Per unit of 8 pieces",
            Scaling = new PrintedScaling(20, 160m, rows, rows.Sum(r => r.Cost)),
        };
    }

    /// <summary>Ube Roll's Scaling tab with the sections given, each one batch of as many 10 g lines as given, named after it.</summary>
    public static ProductPrintout ScaledSections(params (string Name, int Lines)[] sections)
    {
        ScalingRow[] rows =
        [
            .. sections.Select(section =>
            {
                ScalingRow[] lines =
                [
                    .. Enumerable.Range(1, section.Lines).Select(i => ScalingRow.Line($"{section.Name} Ingredient {i}", string.Empty, "10 g", 1m)),
                ];
                return new ScalingRow(section.Name, "1 batch · 20 pcs", $"{10 * section.Lines:#,0} g", lines.Sum(l => l.Cost), true, lines);
            }),
        ];

        return Scaled() with { Scaling = new PrintedScaling(20, 160m, rows, rows.Sum(r => r.Cost)) };
    }

    /// <summary>A titled set of products, each named and with as many Dough lines as given, as a folder or All products prints them.</summary>
    public static ProductPrintSet Set(string title, params (string Name, int Lines)[] products) => new(
        title,
        [.. products.Select(p => WithLines(p.Lines) with { ProductName = p.Name })],
        new DateTime(2026, 9, 27, 15, 40, 0));
}
