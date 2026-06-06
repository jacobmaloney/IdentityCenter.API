using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DataAccessLibrary.Data
{
    /// <summary>
    /// Design-time factory for ApplicationDbContext to support EF Core migrations.
    /// Reads connection string from WebPortal/appsettings.json.
    /// </summary>
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            // Build configuration from WebPortal's appsettings.json
            var basePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "WebPortal"));

            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' not found in appsettings.json. " +
                    $"Searched in: {basePath}");
            }

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.CommandTimeout(600); // 10 minutes for large migrations
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: new[] { -1, -2, 19, 64, 233, 10053, 10054, 10060, 40197, 40501, 40613 });
            });

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
