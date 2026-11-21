using TinapayanRMS.Components.Shared;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Components.Printing;

/// <summary>
/// A product's costing as an Excel workbook (the Costing tab's Export to Excel), from what it would print: a Costing sheet,
/// the breakdown down to the base cost with each part's share of the price, then the suggested and current prices with
/// their markup and VAT; a Recipe sheet, each section's lines with their grams, share, cost per gram and cost; and a
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
            new("% of Price", r => r.Share, ExcelWorkbook.OneDecimal),
            new("Current Price", r => r.Current, ExcelWorkbook.Money))];

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

    /// <summary>One row of the Costing sheet: a part and what it comes to, its share of the price, and the current price's.</summary>
    public sealed record CostingRow(string Component, decimal? Amount, decimal? Share, decimal? Current = null);

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
        rows.Add(new($"Markup ({costs.MarkupPercent:0.##}%)", costs.Markup, costs.MarkupShare, costs.CurrentMarkup));
        rows.Add(new($"VAT ({costs.VatPercent:0.##}%)", costs.Vat, costs.VatShare, costs.CurrentVat));
        rows.Add(new($"Selling price {unit.ToLowerInvariant()}", costs.SellingPrice, costs.SellingPriceShare, costs.CurrentPrice));
        if (costs.HasPriceRounding)
        {
            rows.Add(new(costs.PriceRoundingLabel, costs.RoundedSellingPrice, null));
        }

        if (costs.CurrentMarkupPercent is { } markup)
        {
            rows.Add(new("Markup at current price (%)", null, null, markup));
        }

        return rows;
    }
}
