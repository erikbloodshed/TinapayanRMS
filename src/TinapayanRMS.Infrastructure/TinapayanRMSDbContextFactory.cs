using Microsoft.EntityFrameworkCore.Design;

namespace TinapayanRMS.Infrastructure;

/// <summary>
/// Lets `dotnet ef` create a DbContext at design time without running the WPF host.
/// The connection string here is only for migration authoring; the app itself
/// gets its connection string from TinapayanRMS.UI's appsettings.json.
/// </summary>
public sealed class TinapayanRMSDbContextFactory : IDesignTimeDbContextFactory<TinapayanRMSDbContext>
{
    /// <summary>The dev database. No password: Npgsql reads it from the password file (%APPDATA%\postgresql\pgpass.conf).</summary>
    public const string DevelopmentConnectionString = "Host=localhost;Database=tinapayanrms;Username=tinapayanrms";

    public TinapayanRMSDbContext CreateDbContext(string[] args) =>
        new(TinapayanRMSDbContext.CreateOptions(DevelopmentConnectionString));
}
