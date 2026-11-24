using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.RecipeApi.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.RecipeApi.Tests.Endpoints;

/// <summary>
/// The JSON each endpoint sends, by its fields' names, read as the other apps read it rather than through this repo's own
/// records, which would follow a rename. TinapayanBMS keeps its own copy of the records, and a field it no longer finds
/// reads as null or 0 with no error. So a field is added here when it's added to /api/v1, and one that's renamed or
/// removed belongs in /api/v2.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class RecipeApiContractTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static readonly (string Route, string[] Fields)[] Contract =
    [
        ("/api/v1/status", ["app", "apiVersion"]),
        ("/api/v1/ingredients",
        [
            "[].id", "[].name", "[].categoryId", "[].category", "[].source", "[].weightLossPercent", "[].linkedToId",
            "[].weightSharePercent", "[].subAssemblyId",
        ]),
        ("/api/v1/boms", ["[].ingredientId", "[].componentIngredientId", "[].grams", "[].isAddIn"]),
        ("/api/v1/categories", ["[].id", "[].name", "[].type"]),
        ("/api/v1/products", ["[].id", "[].name", "[].category", "[].stage", "[].isComposite", "[].quantityRequired"]),
        ("/api/v1/mixes", ["[].id", "[].name", "[].roleId", "[].role", "[].roleSortOrder", "[].batchWeight", "[].ownerProduct"]),
        ("/api/v1/mixes/details",
        [
            "[].mix", "[].mix.id", "[].mix.name", "[].mix.roleId", "[].mix.role", "[].mix.roleSortOrder", "[].mix.batchWeight",
            "[].mix.ownerProduct", "[].lines", "[].lines[].ingredientId", "[].lines[].ingredient", "[].lines[].grams",
            "[].lines[].isFormulaBase",
        ]),
        ("/api/v1/mixes/{id}",
        [
            "mix", "mix.id", "mix.name", "mix.roleId", "mix.role", "mix.roleSortOrder", "mix.batchWeight", "mix.ownerProduct",
            "lines", "lines[].ingredientId", "lines[].ingredient", "lines[].grams", "lines[].isFormulaBase",
        ]),
        ("/api/v1/products/recipes",
        [
            "[].productId", "[].quantityRequired", "[].components", "[].components[].productId", "[].components[].quantity",
            "[].sections", "[].sections[].subAssemblyId", "[].sections[].sectionName", "[].sections[].linkedName",
            "[].sections[].role", "[].sections[].authoredField", "[].sections[].authoredValue", "[].sections[].lines",
            "[].sections[].lines[].ingredientId", "[].sections[].lines[].ingredient", "[].sections[].lines[].grams",
            "[].sections[].addIns", "[].sections[].addIns[].ingredientId", "[].sections[].addIns[].ingredient",
            "[].sections[].addIns[].grams", "[].sections[].goesIntoSubAssemblyId", "[].sections[].sectionLines",
            "[].sections[].sectionLines[].subAssemblyId", "[].sections[].sectionLines[].sectionName", "[].sections[].sectionLines[].grams", "[].wastagePercent", "[].packagingPerUnit", "[].packagingPerUnit[].materialId",
            "[].packagingPerUnit[].baseQuantity", "[].includesComponentPackaging", "[].costing", "[].costing.currentPrice",
            "[].costing.sellingPrice", "[].costing.vatPercent", "[].costing.baseCost",
        ]),
        ("/api/v1/materials",
        [
            "[].id", "[].name", "[].type", "[].unit", "[].costPerBaseUnit", "[].buyUnit", "[].baseUnitsPerBuyUnit", "[].buyUnitPrice",
        ]),
    ];

    [Test]
    public async Task Every_endpoint_sends_the_fields_the_other_apps_read()
    {
        var mixId = await AddBakeryAsync();
        using var host = DatabaseHost.CreateBuilder()
            .AddRecipeApi(web => web.UseTestServer())
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(
            [
                new("ConnectionStrings:Default", fixture.ConnectionString),
                new("RecipeApi:Key", string.Empty),
            ]))
            .ConfigureAppConfiguration(ReadOnlyConnection.Apply)
            .Build();
        await host.StartAsync(Cancellation);
        using var client = host.GetTestClient();

        foreach (var (route, fields) in Contract)
        {
            var body = await client.GetStringAsync(route.Replace("{id}", mixId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), Cancellation);
            using var json = JsonDocument.Parse(body);

            FieldsOf(json.RootElement).Should().BeEquivalentTo(fields, $"{route} is what the other apps were written against");
        }
    }

    /// <summary>
    /// One of each thing the JSON can hold, so no list comes back empty: a made ingredient, a linked recipe with an add-in
    /// and a line that is the product's other section, packaging, and a composite. Returns the linked recipe's id, a mix.
    /// </summary>
    private async Task<int> AddBakeryAsync()
    {
        var flourId = await AddIngredientAsync("Flour");
        var waterId = await AddIngredientAsync("Water");
        var ubeId = await AddIngredientAsync("Ube");
        await AddMadeIngredientAsync("Syrup", [(waterId, 100m), (flourId, 10m)]);
        var doughId = await AddRecipeAsync("Sweet Dough", [(flourId, 1000m), (waterId, 500m)]);
        var spongeId = await AddRecipeAsync("Ube Sponge", [(flourId, 200m), (waterId, 200m)]);

        var boxId = await AddPackagingAsync("Box");
        var products = new ProductRepository(CreateDbContextFactory());
        var ubeBunId = await products.SaveAsync(
            new Product { Name = "Ube Bun" },
            new RecipeDraft(
                [],
                [
                    new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 50m)
                    {
                        AddIns = [(ubeId, 20m)],
                        SectionLines = [new SectionLineDraft(spongeId, 100m, null, 1)],
                    },
                    new LinkedUsageDraft(spongeId, UsageAuthoredField.WeightPerPiece, 0m),
                ]),
            packagingLink: null,
            [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)],
            costing: null,
            Cancellation);
        var pandesalId = await AddProductAsync("Pandesal");
        await products.SaveAsync(
            new Product { Name = "Party Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ubeBunId, 2), (pandesalId, 4)] },
            packagingLink: null,
            packagingLines: null,
            costing: null,
            Cancellation);
        return doughId;
    }

    private async Task<int> AddPackagingAsync(string name)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var categoryId = await AddPackagingCategoryAsync();
        await using var dbContext = CreateDbContext();
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = name,
            CategoryId = categoryId,
            BaseUnitId = pieceId,
            CostPerBaseUnit = 1m,
            BufferedCostPerBaseUnit = 1m,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    /// <summary>Every field in <paramref name="root"/> by where it is: "lines[].grams" for the grams of each of its lines.</summary>
    private static SortedSet<string> FieldsOf(JsonElement root)
    {
        SortedSet<string> fields = new(StringComparer.Ordinal);
        AddFields(string.Empty, root, fields);
        return fields;
    }

    private static void AddFields(string path, JsonElement element, SortedSet<string> fields)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AddFields(path + "[]", item, fields);
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var field = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                fields.Add(field);
                AddFields(field, property.Value, fields);
            }
        }
    }
}
