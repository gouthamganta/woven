using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace WovenBackend.Data;

public class WovenDbContextFactory : IDesignTimeDbContextFactory<WovenDbContext>
{
    public WovenDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("DefaultConnection connection string is missing.");

        var options = new DbContextOptionsBuilder<WovenDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new WovenDbContext(options);
    }
}
