using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Printing;

/// <summary>
/// A product's printout laid out on pages of one printable size: its title, then its recipe (each sub-assembly's table and
/// piece size, then its packaging) and its costing, whichever are included, or its recipe scaled (the Products screen's
/// Scaling tab), with or without its costs. The page is built in code as blocks, each measured at the page's width and
/// paginated by <see cref="PrintPagination"/>, with a footer on every page.
/// <see cref="BuildPage"/> builds new elements on every call: the print pane and the printer each put a page in a tree of
/// their own, and an element can only have one parent.
/// </summary>
public sealed class ProductPrintLayout
{
    // Print sizes, in device-independent pixels (96 to the inch): the body is about 9.5 pt.
    private const double TitleSize = 21;
    private const double PartSize = 16;
    private const double SectionSize = 14;
    private const double BodySize = 12.5;
    private const double SmallSize = 11;

    /// <summary>What the body leaves unused at the foot of a page, so rounding in the page's own layout never pushes a line into the footer.</summary>
    private const double Slack = 1;

    /// <summary>The narrowest page side laid out, for margins wider than the paper.</summary>
    private const double MinimumSide = 120;

    private static readonly FontFamily PrintFont = new("Segoe UI");
    private static readonly Thickness CellPadding = new(4, 2.5, 4, 2.5);

    // A table's columns: its first takes what the page has left, and each figure a fixed width.
    // Without the costing, the recipe's tables leave out their cost columns: nothing on the pages is a cost.
    private static readonly GridLength[] RecipeColumns = [new(1, GridUnitType.Star), new(80), new(72), new(100), new(80), new(44)];
    private static readonly GridLength[] RecipeColumnsWithoutCosts = [new(1, GridUnitType.Star), new(90), new(80), new(44)];
    private static readonly GridLength[] PackagingColumns = [new(1, GridUnitType.Star), new(70), new(64), new(90), new(90)];
    private static readonly GridLength[] PackagingColumnsWithoutCosts = [new(1, GridUnitType.Star), new(70), new(64)];
    private static readonly GridLength[] ComponentColumns = [new(1, GridUnitType.Star), new(70), new(100), new(90)];
    private static readonly GridLength[] ComponentColumnsWithPackaging = [new(1, GridUnitType.Star), new(70), new(100), new(110), new(90)];
    private static readonly GridLength[] ComponentColumnsWithoutCosts = [new(1, GridUnitType.Star), new(70)];
    private static readonly GridLength[] CostingColumns = [new(1, GridUnitType.Star), new(140), new(90)];
    private static readonly GridLength[] ContentsColumns = [new(1, GridUnitType.Star), new(240), new(56)];
    private static readonly GridLength[] ScalingColumns = [new(1, GridUnitType.Star), new(130), new(90)];
    private static readonly GridLength[] ScalingColumnsWithoutCosts = [new(1, GridUnitType.Star), new(130)];
    private static readonly GridLength[] PlanOrderColumns = [new(1, GridUnitType.Star), new(100), new(120)];
    private static readonly GridLength[] MixColumns = [new(1, GridUnitType.Star), new(70), new(110)];
    private static readonly GridLength[] ShoppingColumns = [new(1, GridUnitType.Star), new(110), new(130), new(90)];
    private static readonly GridLength[] ShoppingColumnsWithoutCosts = [new(1, GridUnitType.Star), new(110), new(130)];

    /// <summary>How far a scaled recipe's row sits in from the one it's part of, as the Scaling tab's tree indents it.</summary>
    private const double ScalingIndent = 16;

    // The costing's blocks, kept on one page when they fit on one.
    private const int CostingGroup = 1;

    // A scaled recipe's sections, products and packaging, each kept on one page when it fits on one: the first is this
    // group, the next this plus one, and so on.
    private const int ScalingGroups = 2;

    // What a block belongs to (Owner): a product of the set, by its index, or the contents.
    private const int ContentsOwner = -1;

    private readonly ProductPrintSet _set;
    private readonly bool _includeRecipe;
    private readonly bool _includeCosting;
    private readonly Size _size;
    private readonly List<(Func<bool, FrameworkElement> Create, PrintBlock Layout, int Owner)> _blocks = [];
    private readonly IReadOnlyList<IReadOnlyList<PlacedBlock>> _pages;

    // The product each block added now belongs to, while Compose adds a product's blocks.
    private int _owner = ContentsOwner;

    // The page each product starts on (from 1), once the blocks are paginated; the contents page's figures. Null while
    // they're measured, when a wide stand-in takes their place.
    private readonly int[]? _startPages;

    // The Costing card's colours (App.xaml tokens) on white paper; plain ones only where there's no application, in tests.
    private readonly Brush _text = Token("HeadingForegroundBrush", Brushes.Black);
    private readonly Brush _muted = Token("MutedForegroundBrush", Brushes.DimGray);
    private readonly Brush _rule = Token("PrintRuleBrush", Brushes.Silver);
    private readonly Brush _band = Token("PageBackgroundBrush", Brushes.WhiteSmoke);
    private readonly Brush _emphasisBand = Token("AccentSoftBrush", Brushes.WhiteSmoke);
    private readonly Brush _emphasis = Token("AccentForegroundBrush", Brushes.Black);
    private readonly Brush _warning = Token("WarningForegroundBrush", Brushes.Black);
    private readonly Brush _error = Token("ErrorForegroundBrush", Brushes.Black);

    /// <summary>One product on its own, as the Products screen shows it.</summary>
    /// <param name="printableSize">The page less its margins: what the print pane leaves for the content.</param>
    public ProductPrintLayout(ProductPrintout printout, bool includeRecipe, bool includeCosting, Size printableSize)
        : this(ProductPrintSet.Of(printout), includeRecipe, includeCosting, printableSize)
    {
    }

    /// <summary>
    /// <paramref name="set"/>'s products: one on its own as it always was, or a titled set with a contents page first and
    /// each product starting a page of its own.
    /// </summary>
    /// <param name="printableSize">The page less its margins: what the print pane leaves for the content.</param>
    public ProductPrintLayout(ProductPrintSet set, bool includeRecipe, bool includeCosting, Size printableSize)
    {
        _set = set;
        _includeRecipe = includeRecipe;
        _includeCosting = includeCosting;
        _size = new Size(Math.Max(printableSize.Width, MinimumSide), Math.Max(printableSize.Height, MinimumSide));

        Compose();

        // A footer differs from page to page only in its page number and in the product it names, so the tallest of them,
        // each with a wide number, leaves room for any.
        var footerHeight = _blocks.Select(b => b.Owner).Append(ContentsOwner).Distinct()
            .Max(owner => Measure(Footer(pageNumber: 888, pageCount: 888, owner)));
        var bodyHeight = _size.Height - footerHeight - Slack;
        _pages = PrintPagination.Paginate([.. _blocks.Select(b => b.Layout with { Height = Measure(b.Create(false)) })], bodyHeight);

        _startPages = new int[_set.Products.Count];
        for (var page = _pages.Count - 1; page >= 0; page--)
        {
            foreach (var placed in _pages[page])
            {
                if (_blocks[placed.Index].Owner is var owner and >= 0)
                {
                    _startPages[owner] = page + 1;
                }
            }
        }
    }

    /// <summary>How many pages the printout takes at this size: always at least one, the title's.</summary>
    public int PageCount => _pages.Count;

    /// <summary>The page product <paramref name="index"/> of the set starts on, from 1.</summary>
    public int StartPageOf(int index) => _startPages![index];

    /// <summary>The printable area each page fills.</summary>
    public Size PageSize => _size;

    /// <summary>The paper the pages are on: white, around the printable area as well as behind it.</summary>
    public Brush Paper { get; } = Token("PrintPaperBrush", Brushes.White);

    /// <summary>
    /// What the printout is, naming only the parts included: "Ube Roll — Recipe and Costing" for one product, "All
    /// Products — Recipe" for a set, "Ube Roll — Scaling" for the Scaling tab. The print job's name.
    /// </summary>
    public string DocumentName => $"{_set.Title ?? (_set.Products.Count > 0 ? _set.Products[0].ProductName : null)} — {Parts}";

    // The Scaling tab is printed in place of both parts, whichever are ticked.
    private string Parts => _set.Plan is not null ? "Mixing Sheet and Shopping List" : _set.IsScaling ? "Scaling" : (_includeRecipe, _includeCosting) switch
    {
        (true, true) => "Recipe and Costing",
        (true, false) => "Recipe",
        _ => "Costing",
    };

    /// <summary>Page <paramref name="pageNumber"/> (from 1), newly built: the blocks that fall on it, and the footer.</summary>
    public FrameworkElement BuildPage(int pageNumber)
    {
        var index = Math.Clamp(pageNumber, 1, _pages.Count) - 1;

        var body = new StackPanel();
        foreach (var placed in _pages[index])
        {
            body.Children.Add(_blocks[placed.Index].Create(placed.IsRepeat));
        }

        // The page belongs to the product it holds, a product's always starting a page of its own.
        var owner = _pages[index].Select(p => _blocks[p.Index].Owner).DefaultIfEmpty(ContentsOwner).First();
        var footer = Footer(index + 1, _pages.Count, owner);
        Grid.SetRow(footer, 1);

        // Unrounded, as the blocks were measured, whatever the window around the print pane does.
        var page = new Grid
        {
            Width = _size.Width,
            Height = _size.Height,
            ClipToBounds = true,
            UseLayoutRounding = false,
        };
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.Children.Add(body);
        page.Children.Add(footer);
        return page;
    }

    /// <summary>
    /// The printout's blocks, in reading order, each with what pagination needs to know about it: a set's contents first,
    /// then each product, a set's each starting a page. Table numbers run on across products, so a table continued on a
    /// new page only ever repeats its own heading.
    /// </summary>
    private void Compose()
    {
        var table = 0;
        if (_set.Plan is { } plan)
        {
            AddPlan(plan, ref table);
            return;
        }

        if (_set.Title is { } title)
        {
            AddContents(title, ++table);
        }

        for (var i = 0; i < _set.Products.Count; i++)
        {
            _owner = i;
            AddProduct(_set.Products[i], startsPage: _set.Title is not null, ref table);
        }

        _owner = ContentsOwner;
    }

    /// <summary>A set's contents: its title, then every product with its folder and the page it starts on.</summary>
    private void AddContents(string title, int table)
    {
        var count = _set.Products.Count;
        Add(_ => SetTitle(title, count), keepWithNext: count > 0);
        if (count == 0)
        {
            Add(_ => Caption($"No products in {title}."));
            return;
        }

        Add(_ => HeaderRow(ContentsColumns, ["Product", "Folder", "Page"]), keepWithNext: true, table: table, repeats: true);
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var product = _set.Products[i];
            Add(
                _ => Row(ContentsColumns, [Label(product.ProductName), Label($"{product.StageTitle} · {product.CategoryName}", foreground: _muted), Figure(ContentsPage(index))]),
                table: table);
        }
    }

    // A product's page for the contents; while the blocks are measured, a stand-in as wide as any.
    private string ContentsPage(int index) =>
        _startPages is { } pages ? pages[index].ToString(CultureInfo.CurrentCulture) : "888";

    /// <summary>One product's blocks: its title, then the recipe and the costing, whichever are included, or its recipe scaled.</summary>
    private void AddProduct(ProductPrintout printout, bool startsPage, ref int table)
    {
        Add(_ => Title(printout), startsPage: startsPage);

        if (printout.Scaling is { } scaling)
        {
            AddScaling(scaling, ++table);
            return;
        }

        if (_includeRecipe)
        {
            Add(_ => PartHeading("Recipe"), keepWithNext: true);

            // A composite's recipe is the products it holds, then any sections of its own.
            if (printout.Components is { } components)
            {
                AddComponents(components, ++table);
            }
            else if (printout.Sections.Count == 0)
            {
                Add(_ => Caption("No sub-assemblies yet."));
            }

            foreach (var section in printout.Sections)
            {
                AddSection(section, ++table);
            }

            if (printout.Packaging is { } packaging)
            {
                AddPackaging(packaging, ++table);
            }
        }

        if (_includeCosting)
        {
            AddCosting(printout, ++table);
        }
    }

    /// <summary>A recipe table's cells: its cost per gram and cost only while the costing is printed.</summary>
    private T[] RecipeCells<T>(T ingredient, T grams, T percent, T costPerGram, T cost, T isBase) =>
        _includeCosting ? [ingredient, grams, percent, costPerGram, cost, isBase] : [ingredient, grams, percent, isBase];

    /// <summary>A packaging table's cells: its unit cost and cost only while the costing is printed.</summary>
    private T[] PackagingCells<T>(T material, T qty, T unit, T unitCost, T cost) =>
        _includeCosting ? [material, qty, unit, unitCost, cost] : [material, qty, unit];

    /// <summary>
    /// A composite's Products table's cells: a product's recipe cost and its line's cost only while the costing is printed,
    /// and its packaging cost between them when the composite includes its products' packaging.
    /// </summary>
    private T[] ComponentCells<T>(bool withPackaging, T product, T qty, T recipeCost, T packagingCost, T cost) =>
        !_includeCosting ? [product, qty] : withPackaging ? [product, qty, recipeCost, packagingCost, cost] : [product, qty, recipeCost, cost];

    private GridLength[] RecipeTable => _includeCosting ? RecipeColumns : RecipeColumnsWithoutCosts;

    private GridLength[] PackagingTable => _includeCosting ? PackagingColumns : PackagingColumnsWithoutCosts;

    private GridLength[] ComponentTable(bool withPackaging) =>
        !_includeCosting ? ComponentColumnsWithoutCosts : withPackaging ? ComponentColumnsWithPackaging : ComponentColumns;

    /// <summary>
    /// A composite's products, as its Products tab shows them: each product, how many go in, and with the costing what one
    /// of it brings and what the line comes to, then their total: the costing's Products (ingredients), less any sections,
    /// and with the products' packaging when the composite includes it, which then has a column of its own.
    /// </summary>
    private void AddComponents(IReadOnlyList<PrintedComponent> components, int table)
    {
        if (components.Count == 0)
        {
            Add(_ => Caption("No products yet."));
            return;
        }

        Add(repeat => SectionHeading("Products", null, repeat), keepWithNext: true, table: table, repeats: true);
        // Every line has a packaging cost, or none does: the composite includes its products' packaging or it doesn't.
        var withPackaging = components.Any(c => c.PackagingCost is not null);
        var columns = ComponentTable(withPackaging);
        Add(_ => HeaderRow(columns, ComponentCells(withPackaging, "Product", "Qty", "Recipe Cost", "Packaging Cost", "Cost")), keepWithNext: true, table: table, repeats: true);
        for (var i = 0; i < components.Count; i++)
        {
            var line = components[i];
            Add(
                _ => Row(columns, ComponentCells(
                    withPackaging,
                    Label(line.Product),
                    Figure(line.Quantity is { } qty ? qty.ToString("N0", CultureInfo.CurrentCulture) : "—"),
                    Figure(Money(line.RecipeCost)),
                    Figure(Money(line.PackagingCost ?? 0m)),
                    Figure(Money(line.Cost)))),
                keepWithNext: _includeCosting && i == components.Count - 1,
                table: table);
        }

        // The total is only a cost, so without the costing the table ends at its last line.
        if (_includeCosting)
        {
            var total = Money(components.Sum(c => c.Cost));
            Add(
                _ => withPackaging
                    ? TotalRow(columns, "Total", string.Empty, string.Empty, string.Empty, total)
                    : TotalRow(columns, "Total", string.Empty, string.Empty, total),
                table: table);
        }
    }

    private void AddSection(PrintedSection section, int table)
    {
        Add(repeat => SectionHeading(section.Name, section.Source, repeat), keepWithNext: true, table: table, repeats: true);
        Add(
            _ => HeaderRow(RecipeTable, RecipeCells("Ingredient", "Grams", section.PercentHeader, "Cost/g", "Cost", "Base"), isLastCentred: true),
            keepWithNext: true,
            table: table,
            repeats: true);

        // The last line stays with the total under it, and the total with the piece size, so neither starts a page alone.
        AddLines(section.Lines, table, keepLastWithNext: section.AddIns.Count == 0);
        if (section.AddIns.Count > 0)
        {
            Add(_ => SubHeading("Add-ins"), keepWithNext: true, table: table);
            AddLines(section.AddIns, table, keepLastWithNext: true);
        }

        Add(
            _ => TotalRow(RecipeTable, RecipeCells("Total", Grams(section.TotalWeight), Percent(section.TotalPercent), string.Empty, Money(section.BatchCost), string.Empty)),
            keepWithNext: true,
            table: table);
        Add(_ => Caption(PieceSizeText(section, withCost: _includeCosting)), table: table);
    }

    private void AddLines(IReadOnlyList<PrintedLine> lines, int table, bool keepLastWithNext)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            Add(
                _ => Row(RecipeTable, RecipeCells(Label(line.Ingredient), Figure(Grams(line.Grams)), Figure(Percent(line.Percent)), Figure(UnitMoney(line.CostPerGram)), Figure(Money(line.Cost)), Tick(line.IsFormulaBase))),
                keepWithNext: keepLastWithNext && i == lines.Count - 1,
                table: table);
        }
    }

    private void AddPackaging(PrintedPackaging packaging, int table)
    {
        Add(repeat => SectionHeading("Packaging", packaging.LinkedTo, repeat), keepWithNext: true, table: table, repeats: true);
        Add(_ => HeaderRow(PackagingTable, PackagingCells("Material", "Qty", "Unit", "Unit Cost", "Cost")), keepWithNext: true, table: table, repeats: true);
        for (var i = 0; i < packaging.Lines.Count; i++)
        {
            var line = packaging.Lines[i];
            Add(
                _ => Row(PackagingTable, PackagingCells(
                    Label(line.Material),
                    Figure(line.Qty is { } qty ? qty.ToString("N1", CultureInfo.CurrentCulture) : "—"),
                    Figure(line.Unit),
                    Figure(UnitMoney(line.UnitCost)),
                    Figure(UnitMoney(line.Cost)))),
                keepWithNext: _includeCosting && i == packaging.Lines.Count - 1,
                table: table);
        }

        // The total is only a cost, so without the costing the table ends at its last line.
        if (_includeCosting)
        {
            Add(_ => TotalRow(PackagingTable, "Total", string.Empty, string.Empty, string.Empty, Money(packaging.Total)), table: table);
        }
    }

    private void AddCosting(ProductPrintout printout, int table)
    {
        var costs = printout.Costs;
        var unit = printout.CostUnitCaption;

        Add(_ => PartHeading($"Costing ({unit}{BasisSuffix(printout)})"), keepWithNext: true, keepTogether: CostingGroup);
        Add(_ => Figures(costs), keepWithNext: true, keepTogether: CostingGroup);
        Add(_ => HeaderRow(CostingColumns, ["Component", unit, "% of Price"]), keepWithNext: true, table: table, repeats: true, keepTogether: CostingGroup);

        AddCost(costs.PartsTitle, costs.SubAssemblies, costs.SubAssembliesShare, isFollowedByParts: costs.SubAssemblyLines.Count > 0);

        // What each sub-assembly (a composite's each product) comes to, indented and muted under their total, as the Costing
        // tab shows them.
        for (var i = 0; i < costs.SubAssemblyLines.Count; i++)
        {
            var line = costs.SubAssemblyLines[i];
            var isLast = i == costs.SubAssemblyLines.Count - 1;
            Add(_ => PartRow(line.Name, line.Cost, line.Share, isLast), table: table, keepTogether: CostingGroup);
        }

        AddCost(costs.PackagingTitle, costs.Packaging, costs.PackagingShare);
        AddCost($"Labor ({costs.LaborPercent:0.##}% of {costs.PartsBase})", costs.Labor, costs.LaborShare);
        AddCost($"Overhead ({costs.OverheadPercent:0.##}% of {costs.PartsBase})", costs.Overhead, costs.OverheadShare);
        AddCost($"Administrative ({costs.AdministrativePercent:0.##}% of {costs.PartsBase})", costs.Administrative, costs.AdministrativeShare);
        Add(_ => BandRow("Subtotal (base cost)", Money(costs.BaseCost), Share(costs.BaseCostShare), isTotal: false), table: table, keepTogether: CostingGroup);
        AddCost($"Markup ({costs.MarginPercent:0.##}%)", costs.Markup, costs.MarkupShare);
        AddCost($"VAT ({costs.VatPercent:0.##}%)", costs.Vat, costs.VatShare);
        Add(
            _ => BandRow($"Total Selling Price ({unit})", Money(costs.SellingPrice), costs.SellingPriceShare.ToString("N0", CultureInfo.CurrentCulture) + "%", isTotal: true),
            table: table,
            keepTogether: CostingGroup);

        void AddCost(string label, decimal amount, decimal share, bool isFollowedByParts = false) =>
            Add(
                _ => Row(CostingColumns, [Label(label), Figure(Money(amount)), Figure(Share(share), foreground: _muted)], rule: !isFollowedByParts),
                table: table,
                keepTogether: CostingGroup);
    }

    /// <summary>A scaled recipe's table's cells: its cost only while costs are printed.</summary>
    private T[] ScalingCells<T>(T item, T amount, T cost) => _includeCosting ? [item, amount, cost] : [item, amount];

    private GridLength[] ScalingTable => _includeCosting ? ScalingColumns : ScalingColumnsWithoutCosts;

    /// <summary>
    /// The Scaling tab on paper, in place of the recipe and the costing: the sellable units, the pieces they come to and,
    /// with the costs, the total cost, as the tab's top line has them; then its tree as one table, each row indented under
    /// the one it's part of, with its amount and, with the costs, its cost. The total is only the figure at the top, as on
    /// the tab. Without the costs there's no figure of one anywhere, as for the recipe.
    /// </summary>
    private void AddScaling(PrintedScaling scaling, int table)
    {
        Add(repeat => PartHeading(repeat ? "Scaling (continued)" : "Scaling"), keepWithNext: true, table: table, repeats: true);

        List<(string Label, string Value, Brush? Foreground)> figures =
            [("Sellable Units", scaling.Units.ToString("N0", CultureInfo.CurrentCulture), null)];
        if (scaling.Pieces is { } pieces)
        {
            figures.Add(("Pieces", pieces.ToString("#,0.##", CultureInfo.CurrentCulture), null));
        }

        if (_includeCosting)
        {
            figures.Add(("Total Cost", Money(scaling.TotalCost), null));
        }

        Add(_ => Figures(figures), keepWithNext: true);

        if (scaling.Rows.Count == 0)
        {
            Add(_ => Caption("Nothing to scale yet."));
            return;
        }

        Add(
            _ =>
            {
                var caption = Caption(_includeCosting
                    ? "Each section is made in whole batches, so the amounts and the total cost include the extra pieces. A made ingredient's own ingredients are listed under it."
                    : "Each section is made in whole batches, so the amounts include the extra pieces. A made ingredient's own ingredients are listed under it.");
                caption.Margin = caption.Margin with { Bottom = 6 };
                return caption;
            },
            keepWithNext: true);
        Add(_ => HeaderRow(ScalingTable, ScalingCells("Item", "Amount", "Cost")), keepWithNext: true, table: table, repeats: true);

        // Each section, product or the packaging starts a page of its own when it doesn't fit what's left of one but fits a
        // page, as the costing does, so a page the table runs on to starts with the row that says what it's part of. Within
        // one too long for a page, a row with rows under it stays with the first of them.
        for (var top = 0; top < scaling.Rows.Count; top++)
        {
            foreach (var (row, depth) in Flatten(scaling.Rows[top], 0))
            {
                Add(
                    _ => Row(ScalingTable, ScalingCells(ScalingItem(row, depth), Figure(row.AmountText), Figure(Money(row.Cost)))),
                    keepWithNext: row.Children.Count > 0,
                    table: table,
                    keepTogether: ScalingGroups + top);
            }
        }

        static IEnumerable<(ScalingRow Row, int Depth)> Flatten(ScalingRow row, int depth) =>
            row.Children.SelectMany(child => Flatten(child, depth + 1)).Prepend((row, depth));
    }

    /// <summary>
    /// The Production Plan (2026-10-03): what to make, the mixing sheet and the shopping list, each a table continued under
    /// its heading on a page it runs on to, and what the plan couldn't work out. Without the costs the shopping list has
    /// no Cost column and no total, as the recipe has no costs.
    /// </summary>
    private void AddPlan(PrintedPlan plan, ref int table)
    {
        Add(_ => PlanTitle(plan), keepWithNext: true);

        var orders = ++table;
        Add(repeat => PartHeading(repeat ? "To Make (continued)" : "To Make"), keepWithNext: true, table: orders, repeats: true);
        if (plan.Orders.Count == 0)
        {
            Add(_ => Caption("Nothing in the plan yet."));
            return;
        }

        Add(_ => HeaderRow(PlanOrderColumns, ["Product", "Units", "Pieces"]), keepWithNext: true, table: orders, repeats: true);
        foreach (var line in plan.Orders)
        {
            Add(
                _ => Row(PlanOrderColumns, [Label(line.ProductName), Figure($"{line.Units:#,0.##}"), Figure(line.PiecesText)]),
                table: orders);
        }

        var mixes = ++table;
        Add(repeat => PartHeading(repeat ? "Mixing Sheet (continued)" : "Mixing Sheet"), keepWithNext: true, table: mixes, repeats: true);
        Add(_ => HeaderRow(MixColumns, ["Mix", "Batches", "Weight"]), keepWithNext: true, table: mixes, repeats: true);
        foreach (var mix in plan.Mixes)
        {
            Add(
                _ => Row(MixColumns, [MixItem(mix), Figure(mix.Batches is { } batches ? $"{batches:#,0}" : string.Empty), Figure(Grams(mix.Grams))]),
                table: mixes);
        }

        var shopping = ++table;
        var columns = _includeCosting ? ShoppingColumns : ShoppingColumnsWithoutCosts;
        Add(repeat => PartHeading(repeat ? "Shopping List (continued)" : "Shopping List"), keepWithNext: true, table: shopping, repeats: true);
        Add(
            _ => HeaderRow(columns, _includeCosting ? ["Raw Material", "Needed", "Buy", "Cost"] : ["Raw Material", "Needed", "Buy"]),
            keepWithNext: true,
            table: shopping,
            repeats: true);
        foreach (var row in plan.Shopping)
        {
            Add(
                _ =>
                {
                    List<FrameworkElement> cells =
                    [
                        Label(row.Name),
                        Figure($"{row.Needed:#,0.##} {row.Unit}"),
                        Figure(row.Buy is { } buy ? $"{buy:#,0} {row.BuyUnit}" : string.Empty),
                    ];
                    if (_includeCosting)
                    {
                        cells.Add(Figure(row.Cost is { } cost ? Money(cost) : string.Empty));
                    }

                    return Row(columns, cells);
                },
                table: shopping);
        }

        if (_includeCosting)
        {
            Add(_ => TotalRow(columns, "Total", string.Empty, string.Empty, Money(plan.TotalCost)), table: shopping);
        }

        foreach (var note in plan.Notes)
        {
            Add(_ => Caption(note));
        }
    }

    /// <summary>The plan's title: how many products, the costs it's at, and when it was printed.</summary>
    private StackPanel PlanTitle(PrintedPlan plan)
    {
        var count = plan.Orders.Count;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        heading.Children.Add(Text(_set.Title ?? "Production Plan", TitleSize, weight: FontWeights.SemiBold, wrap: true));
        heading.Children.Add(Text(
            $"{count} {(count == 1 ? "product" : "products")} · Printed {Printed(_set.PrintedAt)}" + (_includeCosting ? " · Buffered costs" : string.Empty),
            BodySize,
            _muted,
            wrap: true));
        heading.Children.Add(Text(
            "Each mix is made in whole batches, shared by the products it's for. A made ingredient is bought as what it's made of.",
            SmallSize,
            _muted,
            wrap: true));
        return heading;
    }

    /// <summary>A mix and, after it in small muted type, the products it's for.</summary>
    private TextBlock MixItem(MixRow mix)
    {
        var item = Label(string.Empty);
        item.Inlines.Add(new Run(mix.Name) { FontWeight = FontWeights.SemiBold });
        item.Inlines.Add(new Run($"   {mix.For}") { FontSize = SmallSize, Foreground = _muted });
        return item;
    }

    private void Add(
        Func<bool, FrameworkElement> create,
        bool keepWithNext = false,
        int? table = null,
        bool repeats = false,
        int? keepTogether = null,
        bool startsPage = false) =>
        _blocks.Add((create, new PrintBlock(0, keepWithNext, table, repeats, keepTogether, startsPage), _owner));

    // The blocks.

    private StackPanel Title(ProductPrintout printout)
    {
        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        title.Children.Add(Text(printout.ProductName, TitleSize, weight: FontWeights.SemiBold, wrap: true));
        title.Children.Add(Text($"{printout.CategoryName} · {printout.StageTitle} · {printout.CostUnitCaption}{BasisSuffix(printout)}", BodySize, _muted, wrap: true));
        title.Children.Add(Text($"Last updated {printout.LastUpdatedText}", SmallSize, _muted));
        return title;
    }

    /// <summary>" · Unbuffered costs" after the cost caption when the figures leave out the cost buffer; nothing when they're buffered, as usual.</summary>
    private static string BasisSuffix(ProductPrintout printout) =>
        printout.CostBasis == CostBasis.Unbuffered ? $" · {ProductPrintout.UnbufferedNote}" : string.Empty;

    /// <summary>A set's title over its contents: "All Products", then how many and when they were printed.</summary>
    private StackPanel SetTitle(string title, int count)
    {
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        heading.Children.Add(Text(title, TitleSize, weight: FontWeights.SemiBold, wrap: true));
        heading.Children.Add(Text(
            $"{count} {(count == 1 ? "product" : "products")} · {Parts} · Printed {Printed(_set.PrintedAt)}", BodySize, _muted, wrap: true));
        heading.Children.Add(Text("As saved: edits not yet saved on the Products screen aren't in these pages.", SmallSize, _muted, wrap: true));
        return heading;
    }

    private static string Printed(DateTime printedAt) => printedAt.ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture);

    /// <summary>A part's heading, Recipe or Costing, over a rule.</summary>
    private Border PartHeading(string text) => new()
    {
        BorderBrush = _rule,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Margin = new Thickness(0, 14, 0, 2),
        Padding = new Thickness(0, 0, 0, 3),
        Child = Text(text, PartSize, weight: FontWeights.SemiBold, wrap: true),
    };

    /// <summary>A sub-assembly's heading, and where a linked one comes from; "(continued)" at the top of a page it runs on to.</summary>
    private TextBlock SectionHeading(string name, string? source, bool isRepeat)
    {
        var heading = Text(string.Empty, SectionSize, wrap: true);
        heading.Margin = new Thickness(0, 10, 0, 4);
        heading.Inlines.Add(new Run(isRepeat ? $"{name} (continued)" : name) { FontWeight = FontWeights.SemiBold });
        if (source is not null)
        {
            heading.Inlines.Add(new Run($"   {source}") { FontSize = SmallSize, Foreground = _muted });
        }

        return heading;
    }

    private TextBlock SubHeading(string text)
    {
        var heading = Text(text, SmallSize, _muted);
        heading.FontStyle = FontStyles.Italic;
        heading.Margin = new Thickness(CellPadding.Left, 6, CellPadding.Right, 2);
        return heading;
    }

    private TextBlock Caption(string text)
    {
        var caption = Text(text, SmallSize, _muted, wrap: true);
        caption.Margin = new Thickness(CellPadding.Left, 4, CellPadding.Right, 2);
        return caption;
    }

    /// <summary>
    /// A table's header row, on a band: its first heading on the left over the names, the others on the right over their
    /// figures, or centred over a column of ticks (<paramref name="isLastCentred"/>).
    /// </summary>
    private Border HeaderRow(GridLength[] columns, string[] headings, bool isLastCentred = false) =>
        Row(
            columns,
            [.. headings.Select((heading, i) => i == 0
                ? Label(heading, SmallSize, _muted, FontWeights.SemiBold)
                : isLastCentred && i == headings.Length - 1
                    ? Mark(heading, SmallSize, _muted, FontWeights.SemiBold)
                    : Figure(heading, SmallSize, _muted, FontWeights.SemiBold, wrap: true))],
            _band,
            rule: false);

    private Border TotalRow(GridLength[] columns, params string[] cells) =>
        Row(
            columns,
            [.. cells.Select((cell, i) => i == 0 ? Label(cell, weight: FontWeights.SemiBold) : Figure(cell, weight: FontWeights.SemiBold))],
            _band,
            rule: false);

    /// <summary>One sub-assembly's part of the costing's Sub-assemblies row: indented, smaller and muted.</summary>
    private Border PartRow(string name, decimal cost, decimal share, bool isLast)
    {
        var label = Label(name, SmallSize, _muted);
        label.Margin = new Thickness(24, 1.5, CellPadding.Right, 1.5);
        return Row(CostingColumns, [label, Figure(Money(cost), SmallSize, _muted), Figure(Share(share), SmallSize, _muted)], rule: isLast);
    }

    /// <summary>The costing's Subtotal or Total Selling Price, on the accent's soft band; the total's figures in the accent.</summary>
    private Border BandRow(string label, string amount, string share, bool isTotal)
    {
        var figures = isTotal ? _emphasis : _text;
        var row = Row(
            CostingColumns,
            [
                Label(label, weight: FontWeights.SemiBold),
                Figure(amount, foreground: figures, weight: FontWeights.SemiBold),
                Figure(share, foreground: isTotal ? figures : _muted, weight: isTotal ? FontWeights.SemiBold : FontWeights.Normal),
            ],
            _emphasisBand,
            rule: false);
        row.Margin = new Thickness(0, isTotal ? 6 : 3, 0, 3);
        return row;
    }

    /// <summary>
    /// The costing's headline figures, labels over values, as the product header has them. The header's Markup % is named
    /// for what it is here, the markup the current price earns, since the breakdown below has a Markup row of its own.
    /// </summary>
    private Grid Figures(ProductCostSummary costs) => Figures(
    [
        ("Material Cost", Money(costs.Material), null),
        ("Base Cost", Money(costs.BaseCost), null),
        ("Current Price", costs.CurrentPrice is { } price ? Money(price) : "—", null),
        (
            "Markup at Current Price",
            costs.CurrentMarkupPercent is { } markup ? markup.ToString("N2", CultureInfo.CurrentCulture) + "%" : "—",
            costs.IsCurrentPriceBelowCost ? _error : null
        ),
    ]);

    /// <summary>Figures, labels over values, in a row of at least four columns, so fewer line up as the costing's four do.</summary>
    private Grid Figures(List<(string Label, string Value, Brush? Foreground)> figures)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 8) };
        for (var i = 0; i < Math.Max(figures.Count, 4); i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var i = 0; i < figures.Count; i++)
        {
            var figure = new StackPanel { Margin = new Thickness(CellPadding.Left, 0, CellPadding.Right, 0) };
            figure.Children.Add(Text(figures[i].Label, SmallSize, _muted));
            figure.Children.Add(Text(figures[i].Value, BodySize + 1, figures[i].Foreground, FontWeights.SemiBold));
            Grid.SetColumn(figure, i);
            grid.Children.Add(figure);
        }

        return grid;
    }

    /// <summary>
    /// Every page's foot, over a rule: what's on the page, the product it belongs to (<paramref name="owner"/>) or a set's
    /// contents, and when it was printed; then whether that holds anything unsaved, and the page number, counted over the
    /// whole document.
    /// </summary>
    private Border Footer(int pageNumber, int pageCount, int owner)
    {
        // It wraps rather than trims: a long name beside the save note would otherwise cut off when it was printed.
        var product = owner >= 0 ? _set.Products[owner] : null;
        var name = product is not null ? $"{product.ProductName} — {Parts}" : _set.Plan is not null ? $"{_set.Title}" : $"{_set.Title} — Contents";
        var about = Text($"{name} · Printed {Printed(_set.PrintedAt)}", SmallSize, _muted, wrap: true);

        var page = Text(string.Empty, SmallSize, _muted, alignment: TextAlignment.Right);
        page.Margin = new Thickness(12, 0, 0, 0);
        if (product?.SaveNote is { } note)
        {
            page.Inlines.Add(new Run(note) { Foreground = _warning, FontWeight = FontWeights.SemiBold });
            page.Inlines.Add(new Run("   "));
        }

        page.Inlines.Add(new Run($"Page {pageNumber} of {pageCount}"));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(page, 1);
        grid.Children.Add(about);
        grid.Children.Add(page);

        // A little room on the right, so the page number's last figure isn't clipped at the page's edge.
        return new Border
        {
            BorderBrush = _rule,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(0, 4, 2, 0),
            Child = grid,
        };
    }

    // Rows and cells.

    /// <summary>A table row: <paramref name="cells"/> in <paramref name="columns"/>, the same widths in every row of the table, so they line up.</summary>
    private Border Row(GridLength[] columns, IReadOnlyList<FrameworkElement> cells, Brush? background = null, bool rule = true)
    {
        var grid = new Grid();
        foreach (var width in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        for (var i = 0; i < cells.Count; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        return new Border
        {
            Background = background,
            BorderBrush = _rule,
            BorderThickness = new Thickness(0, 0, 0, rule ? 0.75 : 0),
            Child = grid,
        };
    }

    /// <summary>A name in a table's first column, wrapping when it's long.</summary>
    private TextBlock Label(string text, double size = BodySize, Brush? foreground = null, FontWeight? weight = null)
    {
        var label = Text(text, size, foreground, weight, wrap: true);
        label.Margin = CellPadding;
        return label;
    }

    /// <summary>
    /// A scaled recipe's row's name, as the Scaling tab's tree has it: indented <paramref name="depth"/> steps under the row
    /// it's part of, a section's, a product's or the packaging's in bold, and what it comes to beside it, muted ("2 batches
    /// · 200 pcs (40 extra)").
    /// </summary>
    private TextBlock ScalingItem(ScalingRow row, int depth)
    {
        var item = Label(string.Empty);
        item.Margin = new Thickness(CellPadding.Left + (depth * ScalingIndent), CellPadding.Top, CellPadding.Right, CellPadding.Bottom);
        item.Inlines.Add(new Run(row.Name) { FontWeight = row.IsGroup ? FontWeights.SemiBold : FontWeights.Normal });
        if (row.Detail.Length > 0)
        {
            item.Inlines.Add(new Run($"   {row.Detail}") { FontSize = SmallSize, Foreground = _muted });
        }

        return item;
    }

    /// <summary>A figure, right-aligned in its column.</summary>
    private TextBlock Figure(string text, double size = BodySize, Brush? foreground = null, FontWeight? weight = null, bool wrap = false)
    {
        var figure = Text(text, size, foreground, weight, TextAlignment.Right, wrap);
        figure.Margin = CellPadding;
        return figure;
    }

    /// <summary>
    /// A formula-base line's tick, as the recipe tab's Base column shows it: drawn, not typed. Segoe UI has no ✓, and WPF
    /// takes the one it falls back to from Segoe UI Emoji, which printing embeds whole (8 MB) and Microsoft Print to PDF
    /// can't print: its PDF came out empty.
    /// </summary>
    private FrameworkElement Tick(bool isFormulaBase) =>
        isFormulaBase
            ? new Path
            {
                Data = TickShape,
                Stroke = _text,
                StrokeThickness = 1.4,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 10,
                Height = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new Border();

    /// <summary>A tick in a 10 × 8 box. Frozen, so pages built on any thread can share it.</summary>
    private static readonly Geometry TickShape = FrozenGeometry("M 1,4.5 L 3.8,7.2 L 9,1.2");

    private static Geometry FrozenGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Text centred in its column: the header over the ticks.</summary>
    private TextBlock Mark(string text, double size = BodySize, Brush? foreground = null, FontWeight? weight = null)
    {
        var mark = Text(text, size, foreground, weight, TextAlignment.Center);
        mark.Margin = CellPadding;
        return mark;
    }

    /// <summary>A line of print. Left empty, it's for runs of its own to be added to.</summary>
    private TextBlock Text(
        string text,
        double size = BodySize,
        Brush? foreground = null,
        FontWeight? weight = null,
        TextAlignment alignment = TextAlignment.Left,
        bool wrap = false)
    {
        var block = new TextBlock
        {
            FontFamily = PrintFont,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = foreground ?? _text,
            TextAlignment = alignment,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };

        if (text.Length > 0)
        {
            block.Text = text;
        }

        return block;
    }

    /// <summary>
    /// A section's piece size in words, as the recipe tab's summary column has it: what a piece weighs, how many a batch
    /// makes, and, <paramref name="withCost"/>, what a piece costs.
    /// </summary>
    private static string PieceSizeText(PrintedSection section, bool withCost)
    {
        if (!section.HasPieceSize)
        {
            return withCost
                ? "No weight per piece or yield yet, so this section costs nothing per piece."
                : "No weight per piece or yield yet.";
        }

        List<string> parts = [];
        if (section.ShrinkagePercent > 0)
        {
            parts.Add($"Shrinkage {section.ShrinkagePercent:0.##}%");
            parts.Add($"Final weight {section.FinalWeight:#,0.##} g");
        }

        parts.Add($"Weight per piece {section.WeightPerPiece:#,0.##} g");
        parts.Add($"Yield {section.Yield:#,0.##} pcs");
        if (withCost)
        {
            parts.Add($"Cost per piece {section.CostPerPiece:N2}");
        }

        return string.Join(" · ", parts);
    }

    private double Measure(FrameworkElement element)
    {
        element.Measure(new Size(_size.Width, double.PositiveInfinity));
        return element.DesiredSize.Height;
    }

    private static string Grams(decimal grams) => grams.ToString("N1", CultureInfo.CurrentCulture);

    private static string Percent(decimal? percent) => percent is { } value ? value.ToString("N1", CultureInfo.CurrentCulture) : "—";

    private static string Share(decimal share) => share.ToString("N1", CultureInfo.CurrentCulture) + "%";

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>A per-unit cost, to the 4 decimals the Packaging tab shows.</summary>
    private static string UnitMoney(decimal amount) => amount.ToString("N4", CultureInfo.CurrentCulture);

    /// <summary>An App.xaml colour token, looked up on the application's own thread; <paramref name="fallback"/> where there's no application.</summary>
    private static Brush Token(string key, Brush fallback) =>
        Application.Current is { } application && application.CheckAccess() && application.TryFindResource(key) is Brush brush
            ? brush
            : fallback;
}
