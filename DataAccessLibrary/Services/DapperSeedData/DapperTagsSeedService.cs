using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based tag seeding for categorizing users, groups, and objects.
/// Seeds default organizational tags like VIP, Privileged, Finance, IT, etc.
/// </summary>
public class DapperTagsSeedService : DapperSeedServiceBase
{
    public DapperTagsSeedService(
        IConfiguration configuration,
        ILogger<DapperTagsSeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if tags already exist
        var existingCount = await GetCountAsync(connection, transaction, "Tags", "IsSystem = 1");
        if (existingCount >= 16)
        {
            _logger.LogDebug("Tags already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var tags = GetDefaultTags();

        const string insertSql = @"
            INSERT INTO Tags (Id, Name, Color, Category, Description, IsSystem, CreatedAt)
            SELECT @Id, @Name, @Color, @Category, @Description, @IsSystem, @CreatedAt
            WHERE NOT EXISTS (SELECT 1 FROM Tags WHERE Name = @Name)";

        int created = 0;
        foreach (var tag in tags)
        {
            var rowsAffected = await InsertAsync(connection, transaction, insertSql, tag);
            if (rowsAffected > 0) created++;
        }

        sw.Stop();
        LogSeedComplete("Tags", created, tags.Count - created, sw.Elapsed);
    }

    private static List<object> GetDefaultTags()
    {
        var now = DateTime.UtcNow;
        return new List<object>
        {
            new { Id = Guid.NewGuid(), Name = "VIP", Color = "#dc2626", Category = "Security", Description = "Very important persons requiring special attention and monitoring", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Privileged", Color = "#f59e0b", Category = "Security", Description = "Users with elevated privileges requiring frequent access reviews", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Finance", Color = "#10b981", Category = "Department", Description = "Finance department members subject to SOX compliance", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "IT", Color = "#3b82f6", Category = "Department", Description = "Information Technology staff with technical access", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Executive", Color = "#8b5cf6", Category = "Security", Description = "Executive leadership requiring enhanced security and monitoring", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Service Account", Color = "#6b7280", Category = "Type", Description = "Non-human service accounts for automated processes", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Terminated", Color = "#ef4444", Category = "Status", Description = "Former employees pending account cleanup", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Employee", Color = "#22c55e", Category = "Type", Description = "Full-time and part-time employees", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Contractor", Color = "#f97316", Category = "Type", Description = "External contractors with time-limited access", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Vendor", Color = "#a855f7", Category = "Type", Description = "Third-party vendor accounts with limited access", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Intern", Color = "#06b6d4", Category = "Type", Description = "Temporary intern accounts with supervised access", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Consultant", Color = "#eab308", Category = "Type", Description = "External consultants with project-based access", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Remote", Color = "#06b6d4", Category = "Location", Description = "Remote workers requiring VPN and security monitoring", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "HR", Color = "#ec4899", Category = "Department", Description = "Human Resources team with access to sensitive employee data", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "Compliance", Color = "#14b8a6", Category = "Department", Description = "Compliance and audit team members", IsSystem = true, CreatedAt = now },
            new { Id = Guid.NewGuid(), Name = "High Risk", Color = "#991b1b", Category = "Security", Description = "High-risk accounts requiring immediate review and monitoring", IsSystem = true, CreatedAt = now }
        };
    }
}
