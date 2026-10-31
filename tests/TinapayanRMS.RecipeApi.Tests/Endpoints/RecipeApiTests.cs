using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.RecipeApi.Contracts;
using TinapayanRMS.RecipeApi.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI;

namespace TinapayanRMS.RecipeApi.Tests.Endpoints;

/// <summary>
/// The Recipe API against a real database, through the host Program builds (the app's CompositionRoot with the API on
/// top and a read-only connection), served in memory: the mixes and their lines as saved, the ingredients, BOMs,
/// materials and product recipes TinapayanPreweigh plans from, the key when one is set, and no writes.
/// </summary>
[Collection(DatabaseCollection.Name)]
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

    [Fact]
    public async Task Status_says_it_is_the_Recipe_API()
    {
        using var host = await StartAsync();

        var status = await GetAsync<ApiStatus>(host, "/api/v1/status");

        status.Should().Be(new ApiStatus("TinapayanRMS", 1));
    }

    [Fact]
    public async Task The_mixes_are_the_sub_assemblies_with_ingredients_and_never_a_packaging_set()
    {
        var bakery = await AddBakeryAsync();
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 9, isPackaging: true);
        int setId;
        await using (var dbContext = CreateDbContext())
        {
            var set = new SubAssembly { Name = "Gift Set", RoleId = packagingRoleId, TotalWeight = 5m };
            dbContext.SubAssemblies.Add(set);
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task Every_material_comes_with_its_base_unit_buffered_cost_and_what_its_bought_in()
    {
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.08m, bufferedCostPerGram: 0.09m);
        using var host = await StartAsync();

        var materials = await GetAsync<List<MaterialInfo>>(host, "/api/v1/materials");

        materials.Single(m => m.Id == sugarId).Should().Be(new MaterialInfo(sugarId, "Sugar", "Ingredient", "g", 0.09m, null, 1m, null));
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task Its_connection_refuses_a_write()
    {
        using var host = await StartAsync();
        var factory = host.Services.GetRequiredService<IDbContextFactory<TinapayanRMSDbContext>>();

        await using var dbContext = await factory.CreateDbContextAsync(Cancellation);
        dbContext.SubAssemblyRoles.Add(new SubAssemblyRole { Name = "Glaze" });

        (await FluentActions.Awaiting(() => dbContext.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ReadOnlySqlTransaction);
    }

    /// <summary>The API as Program builds it, on the test database and served in memory, with <paramref name="key"/> as its key.</summary>
    private async Task<IHost> StartAsync(string? key = null)
    {
        var host = CompositionRoot.CreateHostBuilder([])
            .AddRecipeApi(web => web.UseTestServer())
            .ConfigureAppConfiguration(config => config.AddInMemoryCollection(
            [
                new("ConnectionStrings:Default", fixture.ConnectionString),
                new("RecipeApi:Key", key ?? string.Empty),
            ]))
            .ConfigureAppConfiguration(ReadOnlyConnection.Apply)
            .Build();
        await host.StartAsync(Cancellation);
        return host;
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
