using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DrivingLicenceWorkflow.Shared.Data;

// Used by `dotnet ef` only; avoids needing a running host or real credentials to scaffold migrations.
public class DrivingLicenceDbContextFactory : IDesignTimeDbContextFactory<DrivingLicenceDbContext>
{
    public DrivingLicenceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AppDb")
            ?? "Host=localhost;Port=5432;Database=dl_system;Username=postgres;Password=design-time-only";

        var options = new DbContextOptionsBuilder<DrivingLicenceDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DrivingLicenceDbContext(options);
    }
}
