using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Pgvector.EntityFrameworkCore;

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

        // UseVector() must be called on NpgsqlDataSourceBuilder so EF Core knows how to
        // map Pgvector.Vector properties at runtime and for model validation.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<WovenDbContext>()
            .UseNpgsql(dataSource, o => o.UseVector())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new WovenDbContext(options);
    }
}
