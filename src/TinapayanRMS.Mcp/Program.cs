using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Mcp.Serialization;
using TinapayanRMS.Pricing;

// The host the app starts from too (DatabaseHost), so every repository is registered as the screens' are, with the MCP
// server over stdio on top. Nothing may write to stdout but the protocol: the host has no console logging. Its database
// connection is read-only (ReadOnlyConnection), so PostgreSQL itself refuses a write.
await DatabaseHost.CreateBuilder()
    .ConfigureAppConfiguration(ReadOnlyConnection.Apply)
    .ConfigureServices(services => services
        .AddSingleton<SavedProductReader>()
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(serializerOptions: ToolJson.Options))
    .Build()
    .RunAsync();
