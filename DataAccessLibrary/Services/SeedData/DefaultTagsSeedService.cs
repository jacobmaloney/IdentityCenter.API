using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default tags for categorizing users, groups, and objects
/// Makes Certification Center instantly useful with practical, real-world tags
/// </summary>
public class DefaultTagsSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<DefaultTagsSeedService> _logger;

    public DefaultTagsSeedService(
        IConfiguration configuration,
        ILogger<DefaultTagsSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Seeds essential tags that make filtering and organization a breeze
    /// </summary>
    public async Task SeedDefaultTagsAsync()
    {
        _logger.LogInformation("Starting default tags seeding - the good stuff!");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var defaultTags = new[]
        {
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "VIP",
                Color = "#dc2626", // Red
                Category = "Security",
                Description = "Very important persons requiring special attention and monitoring",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Privileged",
                Color = "#f59e0b", // Amber/Orange
                Category = "Security",
                Description = "Users with elevated privileges requiring frequent access reviews",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Finance",
                Color = "#10b981", // Green
                Category = "Department",
                Description = "Finance department members subject to SOX compliance",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "IT",
                Color = "#3b82f6", // Blue
                Category = "Department",
                Description = "Information Technology staff with technical access",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Executive",
                Color = "#8b5cf6", // Purple
                Category = "Security",
                Description = "Executive leadership requiring enhanced security and monitoring",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Service Account",
                Color = "#6b7280", // Gray
                Category = "Type",
                Description = "Non-human service accounts for automated processes",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Terminated",
                Color = "#ef4444", // Red (darker)
                Category = "Status",
                Description = "Former employees pending account cleanup",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Employee",
                Color = "#22c55e", // Green
                Category = "Type",
                Description = "Full-time and part-time employees",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Contractor",
                Color = "#f97316", // Orange
                Category = "Type",
                Description = "External contractors with time-limited access",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Vendor",
                Color = "#a855f7", // Purple
                Category = "Type",
                Description = "Third-party vendor accounts with limited access",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Intern",
                Color = "#06b6d4", // Cyan
                Category = "Type",
                Description = "Temporary intern accounts with supervised access",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Consultant",
                Color = "#eab308", // Yellow
                Category = "Type",
                Description = "External consultants with project-based access",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Remote",
                Color = "#06b6d4", // Cyan
                Category = "Location",
                Description = "Remote workers requiring VPN and security monitoring",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "HR",
                Color = "#ec4899", // Pink
                Category = "Department",
                Description = "Human Resources team with access to sensitive employee data",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "Compliance",
                Color = "#14b8a6", // Teal
                Category = "Department",
                Description = "Compliance and audit team members",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "High Risk",
                Color = "#991b1b", // Dark Red
                Category = "Security",
                Description = "High-risk accounts requiring immediate review and monitoring",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            },
            new Tag
            {
                Id = Guid.NewGuid(),
                Name = "SOX",
                Color = "#7c3aed", // Violet
                Category = "Compliance",
                Description = "Subject to Sarbanes-Oxley compliance requirements",
                IsSystem = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        int created = 0;
        int skipped = 0;

        const string checkSql = "SELECT COUNT(*) FROM Tags WHERE Name = @Name";
        const string insertSql = @"
            INSERT INTO Tags (Id, Name, Color, Category, Description, IsSystem, CreatedAt)
            VALUES (@Id, @Name, @Color, @Category, @Description, @IsSystem, @CreatedAt)";

        foreach (var tag in defaultTags)
        {
            // Check if tag already exists by name
            var existingCount = await connection.ExecuteScalarAsync<int>(checkSql, new { tag.Name });
            if (existingCount > 0)
            {
                _logger.LogDebug("Tag '{TagName}' already exists, skipping", tag.Name);
                skipped++;
                continue;
            }

            await connection.ExecuteAsync(insertSql, tag);
            _logger.LogInformation("Created tag '{TagName}' ({Color}) - {Category}",
                tag.Name, tag.Color, tag.Category);
            created++;
        }

        _logger.LogInformation("Default tags seeding complete! Created: {Created}, Skipped: {Skipped}", created, skipped);
        _logger.LogInformation("Users can now start filtering and organizing immediately!");
    }
}
