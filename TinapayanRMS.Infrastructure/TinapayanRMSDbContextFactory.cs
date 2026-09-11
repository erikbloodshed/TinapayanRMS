using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TinapayanRMS.Infrastructure;

/// <summary>
/// Lets `dotnet ef` create a DbContext at design time without running the WPF host.
/// The connection string here is only for migration authoring; the app itself
/// gets its connection string from TinapayanRMS.UI's appsettings.json.
/// </summary>
public class TinapayanRMSDbContextFactory : IDesignTimeDbContextFactory<TinapayanRMSDbContext>
{
    public TinapayanRMSDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TinapayanRMSDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=TinapayanRMS;Trusted_Connection=True;TrustServerCertificate=True;");

        return new TinapayanRMSDbContext(optionsBuilder.Options);
    }
}
