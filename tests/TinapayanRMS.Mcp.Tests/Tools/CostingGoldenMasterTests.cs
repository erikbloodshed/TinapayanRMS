using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Mcp.Serialization;
using TinapayanRMS.Mcp.Tests.Fixtures;
using TinapayanRMS.Mcp.Tools;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Mcp.Tests.Tools;

/// <summary>
/// The golden master of the costing: every product of a copy of the shop's data, its names replaced by ids, priced by the
/// app's own code and compared figure by figure with what was approved (Golden/costing.approved.txt). The copy comes in as
/// a backup does, through Copy In and Restore, so an older copy is brought up to date by every migration since it was
/// taken. A costing change that's meant to move a figure approves the new ones: see <see cref="ApproveVariable"/>.
/// In the database collection, as a restore clears every pooled connection.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public sealed class CostingGoldenMasterTests(DatabaseFixture fixture)
{
    /// <summary>Set to 1 on a developer's PC to write the figures this run works out over the approved ones.</summary>
    private const string ApproveVariable = "TINAPAYANRMS_APPROVE_COSTING";

    private const string ApprovedFile = "costing.approved.txt";

    private const int DifferencesShown = 30;

    /// <summary>Flour on most products, the egg two linked ingredients are cut from, a milk made ingredients use, and packaging.</summary>
    private static readonly PriceChangeInput[] Changes =
    [
        new("Material 11", ChangePercent: 10m),
        new("Material 45", ChangePercent: 15m),
        new("Material 42", ChangePercent: -5m),
        new("Material 75", ChangePercent: 20m),
    ];

    private static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;

    private static string GoldenFolder => Path.Combine(AppContext.BaseDirectory, "Golden");

    [Test]
    public async Task Every_product_prices_as_approved()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "Test Backups", Guid.NewGuid().ToString("N"));
        await using var database = await ThrowawayDatabase.CreateAsync(fixture.ConnectionString, Cancellation);
        try
        {
            using (var backups = new DatabaseBackups(database.CreateDbContextFactory(), folder))
            {
                var copied = await backups.CopyInAsync(Path.Combine(GoldenFolder, "costing-fixture.backup"), Cancellation);
                await backups.RestoreAsync(copied, Cancellation);
            }

            var figures = await FiguresOfAsync(CreateTools(database.ConnectionString));
            if (Environment.GetEnvironmentVariable(ApproveVariable) == "1")
            {
                Environment.GetEnvironmentVariable("CI").Should().BeNullOrEmpty("figures are approved on a developer's PC, where the change can be read");
                await File.WriteAllTextAsync(SourceFile(), string.Join('\n', figures) + "\n", new UTF8Encoding(false), Cancellation);
                return;
            }

            var approved = await File.ReadAllLinesAsync(Path.Combine(GoldenFolder, ApprovedFile), Cancellation);
            DifferencesBetween(approved, figures).Should().BeEmpty(
                $"every costing figure should be as approved. If this change is meant to move them, set {ApproveVariable}=1, run this test, and read the diff of {ApprovedFile} before keeping it");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>The tools as the server makes them, on a read-only connection (ReadOnlyConnection).</summary>
    private static CostingTools CreateTools(string connectionString)
    {
        var factory = new SharedTransactionDbContextFactory(ReadOnlyConnection.Of(connectionString));
        var productRepository = new ProductRepository(factory);
        return new CostingTools(
            productRepository,
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                new PackagingLineRepository(factory),
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                productRepository,
                new CostingDefaultsRepository(factory),
                factory));
    }

    /// <summary>Every figure the tools give, at both cost bases, one to a line: where it is, then its value.</summary>
    private static async Task<List<string>> FiguresOfAsync(CostingTools tools)
    {
        List<string> figures = [];
        foreach (var (basis, unbuffered) in new[] { ("Buffered", false), ("Unbuffered", true) })
        {
            var products = await tools.ListProductsAsync(unbuffered: unbuffered, cancellationToken: Cancellation);
            foreach (var product in products.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                AddFigures($"{basis} | {product.Name} | list", product, figures);
                var sheet = await tools.GetProductCostingAsync(product.Name, unbuffered, Cancellation);
                AddFigures($"{basis} | {product.Name} | sheet", sheet, figures);
            }

            var simulation = await tools.SimulatePriceChangesAsync(Changes, unbuffered, includeUnaffected: true, Cancellation);
            AddFigures($"{basis} | simulation", simulation, figures);
        }

        return figures;
    }

    private static void AddFigures<T>(string path, T result, List<string> figures) =>
        AddFigures(path, JsonSerializer.SerializeToElement(result, ToolJson.Options), figures);

    /// <summary>
    /// The numbers and yes-or-no answers in <paramref name="element"/>, as the tools write them (to 6 places). A number
    /// reads the same however many trailing zeros its working left it with. Text is left out: a label reworded moves no figure.
    /// </summary>
    private static void AddFigures(string path, JsonElement element, List<string> figures)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddFigures($"{path}.{property.Name}", property.Value, figures);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AddFigures($"{path}[{index++}]", item, figures);
                }

                break;
            case JsonValueKind.Number:
                figures.Add($"{path} = {element.GetDecimal().ToString("G29", CultureInfo.InvariantCulture)}");
                break;
            case JsonValueKind.True or JsonValueKind.False:
                figures.Add($"{path} = {element.GetBoolean()}");
                break;
        }
    }

    /// <summary>What moved, the first <see cref="DifferencesShown"/> of it, with how many there are in all.</summary>
    private static List<string> DifferencesBetween(IReadOnlyList<string> approved, IReadOnlyList<string> figures)
    {
        var approvedByPath = approved.Where(line => line.Length > 0).ToDictionary(PathOf, ValueOf);
        var figuresByPath = figures.ToDictionary(PathOf, ValueOf);
        List<string> differences =
        [
            .. approvedByPath.Where(a => !figuresByPath.ContainsKey(a.Key)).Select(a => $"{a.Key}: approved {a.Value}, now gone"),
            .. figuresByPath.Where(f => !approvedByPath.ContainsKey(f.Key)).Select(f => $"{f.Key}: new, {f.Value}"),
            .. figuresByPath
                .Where(f => approvedByPath.TryGetValue(f.Key, out var value) && value != f.Value)
                .Select(f => $"{f.Key}: approved {approvedByPath[f.Key]}, now {f.Value}"),
        ];

        return differences.Count <= DifferencesShown
            ? differences
            : [.. differences.Take(DifferencesShown), $"… {differences.Count} differences in all"];
    }

    private static string PathOf(string line) => line[..line.LastIndexOf(" = ", StringComparison.Ordinal)];

    private static string ValueOf(string line) => line[(line.LastIndexOf(" = ", StringComparison.Ordinal) + 3)..];

    /// <summary>The approved file in the source folder, as the build copies it from there into the tests' own.</summary>
    private static string SourceFile([CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(thisFile)!)!, "Golden", ApprovedFile);
}
