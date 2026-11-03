using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.RecipeApi.Contracts;
using TinapayanRMS.RecipeApi.Endpoints;

namespace TinapayanRMS.RecipeApi;

/// <summary>
/// Puts the Recipe API's web server on the app's host. Kestrel listens where recipeapi.json's Kestrel section says
/// (every address, port 5290, so another PC on the LAN can reach it). When RecipeApi:Key is set, every request must carry it
/// in an X-Api-Key header. A business rule's refusal comes back as a 400 with its message.
/// </summary>
public static class RecipeApiHost
{
    public const string ApiKeyHeader = "X-Api-Key";

    /// <param name="configureWeb">More web host setup after the API's own, such as the tests' in-memory server.</param>
    public static IHostBuilder AddRecipeApi(this IHostBuilder builder, Action<IWebHostBuilder>? configureWeb = null) =>
        builder
            .ConfigureAppConfiguration(config => config.AddJsonFile("recipeapi.json", optional: true, reloadOnChange: false))
            .ConfigureWebHost(web =>
            {
                web.UseKestrel((context, kestrel) => kestrel.Configure(context.Configuration.GetSection("Kestrel")));
                web.ConfigureServices(services => services.AddRouting());
                web.Configure((context, app) =>
                {
                    var key = context.Configuration["RecipeApi:Key"];
                    app.Use(async (http, next) =>
                    {
                        if (!string.IsNullOrEmpty(key) && !KeyMatches(http.Request.Headers[ApiKeyHeader].ToString(), key))
                        {
                            await Results.Json(new ApiError($"Send the Recipe API's key in the {ApiKeyHeader} header."), statusCode: StatusCodes.Status401Unauthorized)
                                .ExecuteAsync(http);
                            return;
                        }

                        try
                        {
                            await next(http);
                        }
                        catch (BusinessRuleException ex) when (!http.Response.HasStarted)
                        {
                            await Results.BadRequest(new ApiError(ex.Message)).ExecuteAsync(http);
                        }
                    });
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        var api = endpoints.MapGroup("/api/v1");
                        CatalogEndpoints.Map(api);
                        MixEndpoints.Map(api);
                        ProductRecipeEndpoints.Map(api);
                    });
                });
                configureWeb?.Invoke(web);
            });

    /// <summary>Compared in fixed time, so how long a refusal takes says nothing of how much of the key was right.</summary>
    private static bool KeyMatches(string sent, string key) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(key));
}
