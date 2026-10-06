namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The What-If screen's report on paper (2026-10-06): each product's recipe and costing at the what-if prices, the lines a
/// price change moves carrying their cost now beside it, and the changes that reach it; the set's first page lists every
/// change tried. Nothing in it is WPF: it pairs a product priced now with the same product priced what-if.
/// </summary>
public static class WhatIfReport
{
    public const string Title = "What-If Prices";

    /// <summary>
    /// <paramref name="whatIf"/>'s printout with <paramref name="now"/>'s figures beside those a change moves. Both are the
    /// same product built from the same detail (SavedProductReader.Build), so their sections, lines, packaging and products
    /// come in the same order, and only their costs differ.
    /// </summary>
    /// <param name="reaching">The price changes that reach the product.</param>
    public static ProductPrintout Printout(SavedProduct now, SavedProduct whatIf, IReadOnlyList<PrintedPriceChange> reaching, DateTime printedAt)
    {
        var was = now.ToPrintout(printedAt);
        var printout = whatIf.ToPrintout(printedAt);
        return printout with
        {
            Sections =
            [
                .. printout.Sections.Zip(was.Sections, (section, then) => section with
                {
                    Lines = Lines(section.Lines, then.Lines),
                    AddIns = Lines(section.AddIns, then.AddIns),
                    NowBatchCost = Moved(section.BatchCost, then.BatchCost),
                }),
            ],
            Packaging = printout.Packaging is { } packaging && was.Packaging is { } wasPackaging
                ? packaging with { Lines = [.. packaging.Lines.Zip(wasPackaging.Lines, (line, then) => line with { NowCost = Moved(line.Cost, then.Cost) })] }
                : printout.Packaging,
            Components = printout.Components is { } components && was.Components is { } wasComponents
                ? [.. components.Zip(wasComponents, (line, then) => line with { NowCost = Moved(line.Cost, then.Cost) })]
                : printout.Components,
            WhatIf = new PrintedWhatIf(now.Costs, reaching),
        };
    }

    /// <summary>The report: <paramref name="products"/> in the order given, after a first page of every change tried.</summary>
    public static ProductPrintSet Set(IReadOnlyList<ProductPrintout> products, IReadOnlyList<PrintedPriceChange> changes, DateTime printedAt) =>
        new(Title, products, printedAt) { PriceChanges = changes };

    /// <summary>
    /// The materials <paramref name="productId"/> uses: its sections' ingredients and add-ins, its packaging's materials, and
    /// those of the products a composite holds, however deeply. <paramref name="details"/> holds the products held too, as
    /// SavedProductReader.ReadDetailsAsync reads them.
    /// </summary>
    public static IReadOnlySet<int> MaterialIdsOf(int productId, IReadOnlyDictionary<int, ProductDetail> details)
    {
        HashSet<int> materialIds = [];
        HashSet<int> visited = [];
        Stack<int> products = new([productId]);
        while (products.TryPop(out var id))
        {
            // A composite that holds itself is refused when its details are read; this only keeps it from looping here.
            if (!visited.Add(id) || details.GetValueOrDefault(id) is not { } detail)
            {
                continue;
            }

            materialIds.UnionWith(detail.SubAssemblyLines.Select(l => l.IngredientId));
            materialIds.UnionWith(detail.AddIns.Select(a => a.IngredientId));
            materialIds.UnionWith(detail.Lines.Select(l => l.PackagingMaterialId));
            foreach (var component in detail.Components)
            {
                products.Push(component.Component.ComponentProductId);
            }
        }

        return materialIds;
    }

    private static IReadOnlyList<PrintedLine> Lines(IReadOnlyList<PrintedLine> lines, IReadOnlyList<PrintedLine> then) =>
        [.. lines.Zip(then, (line, was) => line with { NowCost = Moved(line.Cost, was.Cost) })];

    // Exact, as the What-If table's IsAffected is: both come from the same arithmetic, so an untouched figure is equal.
    private static decimal? Moved(decimal whatIf, decimal now) => whatIf != now ? now : null;
}
