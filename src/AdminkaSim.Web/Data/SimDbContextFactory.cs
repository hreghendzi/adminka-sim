using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdminkaSim.Web.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c> can construct the
/// context without booting the web host. The connection string here is only
/// used by the EF tooling at design time; the running app uses
/// <c>ConnectionStrings:DefaultConnection</c> from configuration.
/// </summary>
public sealed class SimDbContextFactory : IDesignTimeDbContextFactory<SimDbContext>
{
    public SimDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ADMINKA_SIM_DB")
            ?? "Host=localhost;Database=adminka_sim;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<SimDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new SimDbContext(options);
    }
}
