using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.Mcp;
using TinapayanRMS.Mcp.Serialization;
using TinapayanRMS.UI;

// The app's own host (CompositionRoot), so every repository and reader is registered as the screens' are, with the MCP
// server over stdio on top. Nothing may write to stdout but the protocol: the host has no console logging. Its database
// connection is read-only (ReadOnlyConnection), so PostgreSQL itself refuses a write.
await CompositionRoot.CreateHostBuilder(args)
    .ConfigureAppConfiguration(ReadOnlyConnection.Apply)
    .ConfigureServices(services => services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(serializerOptions: ToolJson.Options))
    .Build()
    .RunAsync();
