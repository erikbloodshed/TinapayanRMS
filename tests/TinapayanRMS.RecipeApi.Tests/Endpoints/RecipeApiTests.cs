using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.RecipeApi.Contracts;
using TinapayanRMS.RecipeApi.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.RecipeApi.Tests.Endpoints;

/// <summary>
/// The Recipe API against a real database, through the host Program builds (DatabaseHost with the API on
/// top and a read-only connection), served in memory: the mixes and their lines as saved, the ingredients, BOMs,
/// materials and product recipes TinapayanBMS plans from, who it answers, what it says when something goes wrong, and no
/// writes.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class RecipeApiTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>
    /// Flour and water, a 1,500 g Pandesal Dough at 50 g a piece (30 pieces a batch) that Pandesal links, and a Sweet Dough
    /// on the Sub-Assemblies screen that nothing uses.
    /// </summary>
    private async Task<Bakery> AddBakeryAsync()
    {
        var doughId = await AddRoleAsync("Dough", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour");
        var waterId = await AddIngredientAsync("Water");
        var (pandesalId, pandesalDoughId) = await AddProductWithSubAssemblyAsync(
            "Pandesal", doughId, [new RecipeLine(flourId, 1000m, IsFormulaBase: true), (waterId, 500m)], weightPerPiece: 50m);
        var sweetDoughId = await AddRecipeAsync("Sweet Dough", [(waterId, 300m), (flourId, 600m)]);
        return new Bakery(flourId, waterId, pandesalId, pandesalDoughId, sweetDoughId);
    }

    private sealed record Bakery(int FlourId, int WaterId, int PandesalId, int PandesalDoughId, int SweetDoughId);

    [Test]
    public async Task Status_says_it_is_the_Recipe_API()
    {
        using var host = await StartAsync();

        var status = await GetAsync<ApiStatus>(host, "/api/v1/status");

        status.Should().Be(new ApiStatus("TinapayanRMS", 1));
    }

    [Test]
    public async Task The_mixes_are_the_sub_assemblies_with_ingredients_and_never_a_packaging_set()
    {
        var bakery = await AddBakeryAsync();
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 9, isPackaging: true);
        int setId;
        await using (var dbContext = CreateDbContext())
        {
            // With an ingredient line, as the app never gives a set: the role alone keeps it out.
            var set = new SubAssembly { Name = "Gift Set", RoleId = packagingRoleId };
            dbContext.SubAssemblies.Add(set);
            await dbContext.SaveChangesAsync(Cancellation);
            dbContext.SubAssemblyLines.Add(new SubAssemblyLine { SubAssemblyId = set.Id, IngredientId = bakery.FlourId, Grams = 5m });
            await dbContext.SaveChangesAsync(Cancellation);
            setId = set.Id;
        }

        using var host = await StartAsync();
        var mixes = await GetAsync<List<MixInfo>>(host, "/api/v1/mixes");

        mixes.Select(m => (m.Id, m.Name, m.Role, m.BatchWeight)).Should().Equal(
            (bakery.PandesalDoughId, "Pandesal Dough", "Dough", 1500m),
            (bakery.SweetDoughId, "Sweet Dough", "Base", 900m));
        (await host.GetTestClient().GetAsync($"/api/v1/mixes/{setId}", Cancellation)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_mixs_lines_come_in_grams_per_batch_in_the_recipes_order()
    {
        var bakery = await AddBakeryAsync();
        using var host = await StartAsync();

        var mix = await GetAsync<MixDetail>(host, $"/api/v1/mixes/{bakery.SweetDoughId}");
        var pandesalDough = await GetAsync<MixDetail>(host, $"/api/v1/mixes/{bakery.PandesalDoughId}");

        mix.Mix.Name.Should().Be("Sweet Dough");
        mix.Lines.Should().Equal(new MixLine(bakery.WaterId, "Water", 300m, false), new MixLine(bakery.FlourId, "Flour", 600m, false));
        pandesalDough.Lines.Should().Equal(new MixLine(bakery.FlourId, "Flour", 1000m, true), new MixLine(bakery.WaterId, "Water", 500m, false));
        (await host.GetTestClient().GetAsync("/api/v1/mixes/999999", Cancellation)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_made_ingredient_comes_with_what_it_is_made_from_and_its_BOM_in_order()
    {
        var eggId = await AddIngredientAsync("Egg");
        var milkId = await AddIngredientAsync("Milk");
        var custardId = await AddMadeIngredientAsync("Custard", [(milkId, 300m), (eggId, 100m)], weightLossPercent: 10m);
        using var host = await StartAsync();

        var ingredients = await GetAsync<List<IngredientInfo>>(host, "/api/v1/ingredients");
        var boms = await GetAsync<List<BomLineInfo>>(host, "/api/v1/boms");

        var custard = ingredients.Single(i => i.Id == custardId);
        custard.Source.Should().Be("Made");
        custard.WeightLossPercent.Should().Be(10m);
        custard.SubAssemblyId.Should().Be(await RecipeIdOfAsync(custardId));
        custard.LinkedToId.Should().BeNull();
        ingredients.Single(i => i.Id == eggId).Should().Match<IngredientInfo>(i => i.Source == "Purchased" && i.SubAssemblyId == null);
        boms.Should().Equal(new BomLineInfo(custardId, milkId, 300m, false), new BomLineInfo(custardId, eggId, 100m, false));
    }

    [Test]
    public async Task A_products_recipe_comes_with_its_sections_lines_and_piece_size()
    {
        var bakery = await AddBakeryAsync();
        using var host = await StartAsync();

        var recipes = await GetAsync<List<ProductRecipeInfo>>(host, "/api/v1/products/recipes");

        var pandesal = recipes.Should().ContainSingle().Subject;
        pandesal.ProductId.Should().Be(bakery.PandesalId);
        pandesal.QuantityRequired.Should().Be(1);
        pandesal.Components.Should().BeEmpty();
        var section = pandesal.Sections.Should().ContainSingle().Subject;
        section.SubAssemblyId.Should().Be(bakery.PandesalDoughId);
        section.SectionName.Should().Be("Dough");
        section.Role.Should().Be("Dough");
        section.AuthoredField.Should().Be("WeightPerPiece");
        section.AuthoredValue.Should().Be(50m);
        section.Lines.Should().Equal(new PlanLine(bakery.FlourId, "Flour", 1000m), new PlanLine(bakery.WaterId, "Water", 500m));
        section.AddIns.Should().BeEmpty();
        pandesal.WastagePercent.Should().BeGreaterThanOrEqualTo(0m);
        pandesal.PackagingPerUnit.Should().BeEmpty();
        pandesal.IncludesComponentPackaging.Should().BeFalse();
        pandesal.Costing.CurrentPrice.Should().BeNull("none was set");
        pandesal.Costing.SellingPrice.Should().BeGreaterThanOrEqualTo(pandesal.Costing.BaseCost);
    }

    [Test]
    public async Task A_products_packaging_per_unit_takes_its_share_of_the_bulk_pack_one_entry_a_material()
    {
        var bakery = await AddBakeryAsync();
        var tapeId = await AddPieceOfPackagingAsync("Tape");
        var boxId = await AddPieceOfPackagingAsync("Box");
        var products = new ProductRepository(CreateDbContextFactory());
        await products.SaveAsync(
            (await products.GetByIdAsync(bakery.PandesalId, Cancellation))!,
            recipe: null,
            packagingLink: null,
            [(tapeId, null, Domain.Enums.PackagingLineAuthoredField.QtyPerUnit, 2m)],
            new BulkPackagingDraft([(boxId, null, Domain.Enums.PackagingLineAuthoredField.QtyPerUnit, 1m), (tapeId, null, Domain.Enums.PackagingLineAuthoredField.QtyPerUnit, 6m)], 6),
            costing: null,
            Cancellation);
        using var host = await StartAsync();

        var recipes = await GetAsync<List<ProductRecipeInfo>>(host, "/api/v1/products/recipes");

        recipes.Single().PackagingPerUnit.Should().BeEquivalentTo(
            [new PackagingPerUnitInfo(tapeId, 3m), new PackagingPerUnitInfo(boxId, 1m / 6m)],
            "2 pieces of tape on each, and a sixth of the box's pack of a box and 6 pieces of tape");
    }

    /// <summary>A packaging material bought by the piece, at ₱1 a piece.</summary>
    private async Task<int> AddPieceOfPackagingAsync(string name)
    {
        var unitId = await GetOrCreateUnitOfMeasureAsync("pc", "pc");
        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(c => c.Name == "Boxes", Cancellation)
            ?? new RawMaterialCategory { Name = "Boxes", Type = Domain.Enums.RawMaterialCategoryType.Packaging };
        var material = new RawMaterial
        {
            Type = Domain.Enums.RawMaterialType.Packaging,
            Name = name,
            Category = category,
            BaseUnitId = unitId,
            CostPerBaseUnit = 1m,
            BufferedCostPerBaseUnit = 1m,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    [Test]
    public async Task Every_material_comes_with_its_base_unit_buffered_cost_and_what_its_bought_in()
    {
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.08m, bufferedCostPerGram: 0.09m);
        using var host = await StartAsync();

        var materials = await GetAsync<List<MaterialInfo>>(host, "/api/v1/materials");

        materials.Single(m => m.Id == sugarId).Should().Be(new MaterialInfo(sugarId, "Sugar", "Ingredient", "g", 0.09m, null, 1m, null));
    }

    [Test]
    public async Task Every_mix_comes_with_its_lines_at_once()
    {
        var bakery = await AddBakeryAsync();
        using var host = await StartAsync();

        var mixes = await GetAsync<List<MixDetail>>(host, "/api/v1/mixes/details");

        mixes.Select(m => m.Mix.Id).Should().Equal(bakery.PandesalDoughId, bakery.SweetDoughId);
        mixes.Single(m => m.Mix.Id == bakery.SweetDoughId).Lines.Should().Equal(
            new MixLine(bakery.WaterId, "Water", 300m, false), new MixLine(bakery.FlourId, "Flour", 600m, false));
        var one = await GetAsync<MixDetail>(host, $"/api/v1/mixes/{bakery.PandesalDoughId}");
        var pandesalDough = mixes.Single(m => m.Mix.Id == bakery.PandesalDoughId);
        pandesalDough.Mix.Should().Be(one.Mix);
        pandesalDough.Lines.Should().Equal(one.Lines);
    }

    [Test]
    public async Task With_a_key_set_a_request_without_it_is_refused()
    {
        using var host = await StartAsync(key: "s3cret");
        var client = host.GetTestClient();

        var without = await client.GetAsync("/api/v1/status", Cancellation);
        var wrong = await SendAsync(client, "/api/v1/status", "guess");
        var with = await SendAsync(client, "/api/v1/status", "s3cret");

        without.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ErrorOf(without)).Should().Be("Send the Recipe API's key in the X-Api-Key header.");
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        with.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task With_a_key_set_this_PC_needs_it_too_and_another_PC_gets_in_with_it()
    {
        using var host = await StartAsync(key: "s3cret");

        (await SendFromAsync(host, "127.0.0.1")).Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        (await SendFromAsync(host, "192.168.1.20")).Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        (await SendFromAsync(host, "127.0.0.1", "s3cret")).Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await SendFromAsync(host, "192.168.1.20", "s3cret")).Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    // ::ffff: is how Kestrel, listening on every address, reports whoever came over IPv4.
    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("::1")]
    [Arguments("::ffff:127.0.0.1")]
    public async Task With_no_key_set_this_PC_is_answered(string address)
    {
        using var host = await StartAsync();

        (await SendFromAsync(host, address)).Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Test]
    [Arguments("192.168.1.20")]
    [Arguments("::ffff:192.168.1.20")]
    [Arguments("fe80::1")]
    public async Task With_no_key_set_another_PC_is_refused_and_told_why(string address)
    {
        using var host = await StartAsync();

        var context = await SendFromAsync(host, address);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await JsonSerializer.DeserializeAsync<ApiError>(context.Response.Body, JsonSerializerOptions.Web, Cancellation))!.Error.Should().Be(RecipeApiHost.NoKeyText);
    }

    [Test]
    [Arguments("/api/v1/status")]
    [Arguments("/api/v1/ingredients")]
    [Arguments("/api/v1/boms")]
    [Arguments("/api/v1/materials")]
    [Arguments("/api/v1/categories")]
    [Arguments("/api/v1/products")]
    [Arguments("/api/v1/products/recipes")]
    [Arguments("/api/v1/mixes")]
    [Arguments("/api/v1/mixes/details")]
    [Arguments("/api/v1/mixes/1")]
    public async Task A_database_that_cant_be_reached_comes_back_as_a_503_saying_so(string path)
    {
        using var host = await StartAsync(connectionString: "Host=localhost;Port=1;Database=tinapayanrms;Username=tinapayanrms;Timeout=10");

        var response = await host.GetTestClient().GetAsync(path, Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorOf(response)).Should().Be(Failure.UnreachableText);
    }

    [Test]
    public async Task A_timeout_comes_back_as_a_503_saying_so()
    {
        using var host = await StartAsync(costingDefaults: new FailingCostingDefaults(new TimeoutException("Execution Timeout Expired.")));

        var response = await host.GetTestClient().GetAsync("/api/v1/status", Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorOf(response)).Should().Be(Failure.TimedOutText);
    }

    [Test]
    public async Task A_bug_comes_back_as_a_500_without_its_details()
    {
        using var host = await StartAsync(costingDefaults: new FailingCostingDefaults(new InvalidOperationException("column secret_column does not exist")));

        var response = await host.GetTestClient().GetAsync("/api/v1/status", Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ErrorOf(response)).Should().Be(RecipeApiHost.UnexpectedText);
    }

    // 42703 a column this version expects, 57014 a statement that ran out of time, XX000 PostgreSQL's own internal error,
    // whose words Failure would hand on and a caller shouldn't see.
    [Test]
    [Arguments("42703", HttpStatusCode.ServiceUnavailable, Failure.OutOfDateText)]
    [Arguments("57014", HttpStatusCode.ServiceUnavailable, Failure.TimedOutText)]
    [Arguments("XX000", HttpStatusCode.InternalServerError, RecipeApiHost.UnexpectedText)]
    public async Task What_the_server_says_comes_back_in_a_fixed_sentence(string sqlState, HttpStatusCode status, string text)
    {
        var refusal = new PostgresException("secret_column went wrong", "ERROR", "ERROR", sqlState);
        using var host = await StartAsync(costingDefaults: new FailingCostingDefaults(refusal));

        var response = await host.GetTestClient().GetAsync("/api/v1/status", Cancellation);

        response.StatusCode.Should().Be(status);
        (await ErrorOf(response)).Should().Be(text);
    }

    [Test]
    public async Task A_rules_refusal_comes_back_as_a_400_in_its_own_words()
    {
        using var host = await StartAsync(costingDefaults: new FailingCostingDefaults(new BusinessRuleException("That won't do.")));

        var response = await host.GetTestClient().GetAsync("/api/v1/status", Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorOf(response)).Should().Be("That won't do.");
    }

    [Test]
    public async Task Its_connection_refuses_a_write()
    {
        using var host = await StartAsync();
        var factory = host.Services.GetRequiredService<IDbContextFactory<TinapayanRMSDbContext>>();

        await using var dbContext = await factory.CreateDbContextAsync(Cancellation);
        dbContext.SubAssemblyRoles.Add(new SubAssemblyRole { Name = "Glaze" });

        (await FluentActions.Awaiting(() => dbContext.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ReadOnlySqlTransaction);
    }

    /// <summary>
    /// The API as Program builds it, on the test database (or <paramref name="connectionString"/>) and served in memory, with
    /// <paramref name="key"/> as its key, and <paramref name="costingDefaults"/> in place of what /status reads.
    /// </summary>
    private async Task<IHost> StartAsync(string? key = null, string? connectionString = null, ICostingDefaultsRepository? costingDefaults = null)
    {
        var host = DatabaseHost.CreateBuilder()
            .AddRecipeApi(web => web.UseTestServer())
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(
            [
                new("ConnectionStrings:Default", connectionString ?? fixture.ConnectionString),
                new("RecipeApi:Key", key ?? string.Empty),
            ]))
            .ConfigureAppConfiguration(ReadOnlyConnection.Apply)
            .ConfigureServices(services =>
            {
                if (costingDefaults is not null)
                {
                    services.AddSingleton(costingDefaults);
                }
            })
            .Build();
        await host.StartAsync(Cancellation);
        return host;
    }

    /// <summary>/status as if from <paramref name="address"/>, which only the in-memory server's own context can say.</summary>
    private static Task<HttpContext> SendFromAsync(IHost host, string address, string? key = null) =>
        host.GetTestServer().SendAsync(
            context =>
            {
                context.Request.Method = HttpMethods.Get;
                context.Request.Path = "/api/v1/status";
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                if (key is not null)
                {
                    context.Request.Headers[RecipeApiHost.ApiKeyHeader] = key;
                }
            },
            Cancellation);

    /// <summary>What /status reads, failing with <paramref name="failure"/>.</summary>
    private sealed class FailingCostingDefaults(Exception failure) : ICostingDefaultsRepository
    {
        public Task<CostingDefaults> GetAsync(CancellationToken cancellationToken) => Task.FromException<CostingDefaults>(failure);

        public Task SetAsync(CostingDefaults defaults, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static async Task<T> GetAsync<T>(IHost host, string path) =>
        (await host.GetTestClient().GetFromJsonAsync<T>(path, JsonSerializerOptions.Web, Cancellation))!;

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(RecipeApiHost.ApiKeyHeader, key);
        return client.SendAsync(request, Cancellation);
    }

    private static async Task<string> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiError>(JsonSerializerOptions.Web, Cancellation))!.Error;
}
