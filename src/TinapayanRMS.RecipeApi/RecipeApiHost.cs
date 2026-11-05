using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Pricing;
using TinapayanRMS.RecipeApi.Contracts;
using TinapayanRMS.RecipeApi.Endpoints;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.RecipeApi;

/// <summary>
/// Puts the Recipe API's web server on a host with the app's database (DatabaseHost). Kestrel listens where recipeapi.json's Kestrel section says
/// (every address, port 5290, so another PC on the LAN can reach it). With RecipeApi:Key set, as the installer sets it, every
/// request must carry it in an X-Api-Key header. With none set, only this PC is answered, so a copy run without a key isn't
/// open to the LAN, where it would give every cost and price to anyone who could reach the port. What goes wrong comes back
/// as { error }: a business rule's refusal as a 400 with its message, a database that can't be used as a 503 saying so, and
/// anything else as a 500 with its details left in the log.
/// </summary>
public static class RecipeApiHost
{
    public const string ApiKeyHeader = "X-Api-Key";

    public const string NoKeyText = "The Recipe API has no key set, so it only answers on its own PC. Set RecipeApi:Key in its recipeapi.json.";

    public const string UnexpectedText = "The Recipe API hit a problem it didn't expect. Its log has the details.";

    /// <param name="configureWeb">More web host setup after the API's own, such as the tests' in-memory server.</param>
    public static IHostBuilder AddRecipeApi(this IHostBuilder builder, Action<IWebHostBuilder>? configureWeb = null) =>
        builder
            .ConfigureAppConfiguration(config => config.AddJsonFile("recipeapi.json", optional: true, reloadOnChange: false))
            .ConfigureWebHost(web =>
            {
                web.UseKestrel((context, kestrel) => kestrel.Configure(context.Configuration.GetSection("Kestrel")));
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<SavedProductReader>();
                    services.AddSingleton(Log.Logger);
                });
                web.Configure((context, app) =>
                {
                    var key = context.Configuration["RecipeApi:Key"];
                    var logger = app.ApplicationServices.GetRequiredService<ILogger>().ForContext(typeof(RecipeApiHost));
                    if (string.IsNullOrEmpty(key))
                    {
                        logger.Warning("RecipeApi:Key isn't set, so only this PC can use the Recipe API");
                    }

                    app.Use(async (http, next) =>
                    {
                        if (RefusalOf(http, key) is { } refusal)
                        {
                            logger.Warning("Refused a request from {Address}: {Reason}", http.Connection.RemoteIpAddress, refusal.Error);
                            await Results.Json(new ApiError(refusal.Error), statusCode: refusal.StatusCode).ExecuteAsync(http);
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
                        catch (Exception ex) when (!http.Response.HasStarted && !http.RequestAborted.IsCancellationRequested)
                        {
                            logger.Error(ex, "A request for {Path} failed", http.Request.Path);
                            var failure = FailureOf(ex);
                            await Results.Json(new ApiError(failure.Error), statusCode: failure.StatusCode).ExecuteAsync(http);
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

    /// <summary>Why <paramref name="http"/> isn't answered, or null when it is: a key set has to be sent, from this PC too.</summary>
    private static (int StatusCode, string Error)? RefusalOf(HttpContext http, string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return IsFromThisPc(http) ? null : (StatusCodes.Status403Forbidden, NoKeyText);
        }

        return KeyMatches(http.Request.Headers[ApiKeyHeader].ToString(), key)
            ? null
            : (StatusCodes.Status401Unauthorized, $"Send the Recipe API's key in the {ApiKeyHeader} header.");
    }

    /// <summary>A loopback address, or none at all, as a request has that came through neither a network nor a socket (the tests' in-memory server).</summary>
    private static bool IsFromThisPc(HttpContext http) =>
        http.Connection.RemoteIpAddress is not { } address || IPAddress.IsLoopback(address);

    /// <summary>Compared in fixed time, so how long a refusal takes says nothing of how much of the key was right.</summary>
    private static bool KeyMatches(string sent, string key) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sent), Encoding.UTF8.GetBytes(key));

    /// <summary>
    /// A database that can't be used, in the fixed sentence a screen says it in, since that's the caller's to know and to try
    /// again after. Anything else is a bug, and what it or the server says may name columns and the like, so that stays in
    /// the log. That's why this goes by Failure's sentences rather than by NpgsqlException, whose every other code
    /// Failure hands on in the server's own words.
    /// </summary>
    private static (int StatusCode, string Error) FailureOf(Exception exception)
    {
        var text = Failure.Of(exception).Text;
        return text is Failure.UnreachableText or Failure.TimedOutText or Failure.OutOfDateText
            ? (StatusCodes.Status503ServiceUnavailable, text)
            : (StatusCodes.Status500InternalServerError, UnexpectedText);
    }
}
