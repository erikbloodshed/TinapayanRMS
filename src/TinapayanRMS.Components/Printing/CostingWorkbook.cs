using System.Globalization;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Components.Printing;

/// <summary>
/// A product's costing as an Excel workbook (the Costing tab's Export to Excel), from what it would print: a Costing sheet,
/// the breakdown down to the base cost with each part's share of the price, then, after a blank row and a heading row of
/// their own, the suggested price beside the current one with their markup and VAT, laid out as the Costing tab has them
/// (the suggested price under the amounts, the current price under % of Price, a markup reading "(30.0%) 30.00"); a
/// Recipe sheet, each section's lines with their grams, share, cost per gram and cost; and a
/// Packaging sheet when it has packaging. Numbers stay numbers, in the screen's decimals.
/// </summary>
public static class CostingWorkbook
{
    /// <summary>"Asado Roll 60g Costing 2026-11-19.xlsx".</summary>
    public static string FileNameOf(ProductPrintout printout, DateTime today)
    {
        var name = $"{printout.ProductName} Costing {today:yyyy-MM-dd}";
        foreach (var refused in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(refused, '-');
        }

        return $"{name.Trim()}.xlsx";
    }

    public static byte[] Build(ProductPrintout printout)
    {
        List<ExcelSheet> sheets = [ExcelSheet.Of("Costing", CostingRows(printout.Costs, printout.CostUnitCaption),
            new ExcelColumn<CostingRow>("Component", r => r.Component),
            new(printout.CostUnitCaption, r => r.Amount, ExcelWorkbook.Money),
            new("% of Price", r => r.Share, ExcelWorkbook.OneDecimal))];

        var recipe = printout.Sections.SelectMany(section =>
            section.Lines.Select(line => (Section: section.Name, Kind: "", Line: line))
                .Concat(section.AddIns.Select(line => (Section: section.Name, Kind: "Add-in", Line: line)))).ToList();
        if (recipe.Count > 0)
        {
            sheets.Add(ExcelSheet.Of("Recipe", recipe,
                new ExcelColumn<(string Section, string Kind, PrintedLine Line)>("Section", r => r.Section),
                new("Ingredient", r => r.Line.Ingredient),
                new("Add-in", r => r.Kind),
                new("Grams", r => r.Line.Grams, ExcelWorkbook.OneDecimal),
                new("% of Formula", r => r.Line.Percent, ExcelWorkbook.OneDecimal),
                new("Cost/g", r => r.Line.CostPerGram, ExcelWorkbook.Rate),
                new("Cost", r => r.Line.Cost, ExcelWorkbook.Money)));
        }

        if (printout.Packaging is { Lines.Count: > 0 } packaging)
        {
            sheets.Add(ExcelSheet.Of("Packaging", packaging.Lines,
                new ExcelColumn<PrintedPackagingLine>("Material", l => l.Material),
                new("Qty", l => l.Qty, "#,##0.####"),
                new("Unit", l => l.Unit),
                new("Unit Cost", l => l.UnitCost, ExcelWorkbook.Rate),
                new("Cost", l => l.Cost, ExcelWorkbook.Money)));
        }

        return ExcelWorkbook.Build([.. sheets]);
    }

    /// <summary>
    /// One row of the Costing sheet: in the breakdown, a part, what it comes to and its share of the price; in the price
    /// comparison under it, its heading or a row with the suggested price's figure and the current price's, the latter in
    /// the share's column as an <see cref="ExcelCell"/> with the money's format.
    /// </summary>
    public sealed record CostingRow(object? Component, object? Amount, object? Share);

    public static IReadOnlyList<CostingRow> CostingRows(ProductCostSummary costs, string unit)
    {
        List<CostingRow> rows = [new(costs.PartsTitle, costs.SubAssemblies, costs.SubAssembliesShare)];
        rows.AddRange(costs.SubAssemblyLines.Select(line => new CostingRow($"   {line.Name}", line.Cost, line.Share)));
        rows.Add(new($"Wastage ({costs.WastagePercent:0.##}% of {costs.PartsBase})", costs.Wastage, costs.WastageShare));
        rows.Add(new(costs.PackagingTitle, costs.Packaging, costs.PackagingShare));
        rows.Add(new($"Labor ({costs.LaborPercent:0.##}% of {costs.PartsBase})", costs.Labor, costs.LaborShare));
        rows.Add(new($"Overhead ({costs.OverheadPercent:0.##}% of {costs.PartsBase})", costs.Overhead, costs.OverheadShare));
        rows.Add(new($"Administrative ({costs.AdministrativePercent:0.##}% of {costs.PartsBase})", costs.Administrative, costs.AdministrativeShare));
        rows.Add(new("Subtotal (base cost)", costs.BaseCost, costs.BaseCostShare));
        rows.Add(new(null, null, null));
        rows.Add(new(Heading("Component"), Heading("Suggested Price"), Heading("Current Price")));
        rows.Add(new("Markup", Markup(costs.Markup, costs.MarkupPercent),
            costs.CurrentMarkup is { } markup ? Markup(markup, costs.CurrentMarkupPercent) : NoCurrentPrice));
        rows.Add(new($"VAT ({costs.VatPercent:0.##}%)", costs.Vat, Current(costs.CurrentVat)));
        rows.Add(new($"Selling price {unit.ToLowerInvariant()}", costs.SellingPrice, Current(costs.CurrentPrice)));
        if (costs.HasPriceRounding)
        {
            rows.Add(new(costs.PriceRoundingLabel, costs.RoundedSellingPrice, null));
        }

        return rows;
    }

    // A dash where the product has no current price, as the Costing tab shows.
    private const string NoCurrentPrice = "—";

    private static ExcelCell Heading(string text) => new(text, Bold: true);

    // A current price's figure, in the column whose own format is the share's.
    private static object Current(decimal? amount) => amount is { } value ? new ExcelCell(value, ExcelWorkbook.Money) : NoCurrentPrice;

    // A markup with its percentage before it, "(30.0%) 30.00": the percentage is written into the cell's number format,
    // so the cell stays the amount.
    private static ExcelCell Markup(decimal amount, decimal? percent) => new(amount, percent is { } value
        ? $"\"({value.ToString("N1", CultureInfo.CurrentCulture)}%) \"{ExcelWorkbook.Money}"
        : ExcelWorkbook.Money);
}
