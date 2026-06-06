using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based business role seeding for workflow routing and approval chains.
/// Seeds organizational roles like CEO, CISO, IT Admin, Helpdesk, etc.
/// </summary>
public class DapperBusinessRolesSeedService : DapperSeedServiceBase
{
    public DapperBusinessRolesSeedService(
        IConfiguration configuration,
        ILogger<DapperBusinessRolesSeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if business roles already exist
        var existingCount = await GetCountAsync(connection, transaction, "BusinessRoles");
        if (existingCount > 0)
        {
            _logger.LogDebug("Business roles already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var roles = GetDefaultBusinessRoles();

        const string insertSql = @"
            INSERT INTO BusinessRoles (
                Id, Name, DisplayName, Description, Category, Icon, Color,
                SortOrder, IsSystem, IsActive, CanApprove, CanEscalate,
                CreatedAt, CreatedBy
            )
            VALUES (
                @Id, @Name, @DisplayName, @Description, @Category, @Icon, @Color,
                @SortOrder, @IsSystem, @IsActive, @CanApprove, @CanEscalate,
                @CreatedAt, @CreatedBy
            )";

        var created = await BatchInsertAsync(connection, transaction, insertSql, roles);

        sw.Stop();
        LogSeedComplete("BusinessRoles", created, 0, sw.Elapsed);
    }

    private static List<object> GetDefaultBusinessRoles()
    {
        var now = DateTime.UtcNow;
        return new List<object>
        {
            // Executive Roles
            new { Id = Guid.NewGuid(), Name = "CEO", DisplayName = "Chief Executive Officer", Description = "Organization leader with final approval authority", Category = "Executive", Icon = "bi-award-fill", Color = "#dc2626", SortOrder = 1, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "CTO", DisplayName = "Chief Technology Officer", Description = "Technology strategy and architecture decisions", Category = "Executive", Icon = "bi-cpu-fill", Color = "#7c3aed", SortOrder = 2, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "CIO", DisplayName = "Chief Information Officer", Description = "Information systems and IT operations oversight", Category = "Executive", Icon = "bi-diagram-3-fill", Color = "#2563eb", SortOrder = 3, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "CFO", DisplayName = "Chief Financial Officer", Description = "Financial decisions and budget approvals", Category = "Executive", Icon = "bi-currency-dollar", Color = "#059669", SortOrder = 4, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },

            // Security Roles
            new { Id = Guid.NewGuid(), Name = "CISO", DisplayName = "Chief Information Security Officer", Description = "Security policy enforcement and high-risk access approvals", Category = "Security", Icon = "bi-shield-lock-fill", Color = "#dc2626", SortOrder = 10, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "Security Analyst", DisplayName = "Security Analyst", Description = "Security monitoring and incident response", Category = "Security", Icon = "bi-shield-check", Color = "#ea580c", SortOrder = 11, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = false, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "Security Admin", DisplayName = "Security Administrator", Description = "Security infrastructure and access control management", Category = "Security", Icon = "bi-shield-fill-exclamation", Color = "#b91c1c", SortOrder = 12, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },

            // IT Roles
            new { Id = Guid.NewGuid(), Name = "IT Administrator", DisplayName = "IT Administrator", Description = "System administration and infrastructure management", Category = "IT", Icon = "bi-gear-fill", Color = "#0284c7", SortOrder = 20, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "Helpdesk", DisplayName = "Helpdesk Support", Description = "First-line user support and basic access requests", Category = "IT", Icon = "bi-headset", Color = "#0891b2", SortOrder = 21, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = false, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "Network Admin", DisplayName = "Network Administrator", Description = "Network infrastructure and connectivity management", Category = "IT", Icon = "bi-router-fill", Color = "#4f46e5", SortOrder = 22, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "DBA", DisplayName = "Database Administrator", Description = "Database systems management and data access", Category = "IT", Icon = "bi-database-fill", Color = "#7c3aed", SortOrder = 23, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },

            // Compliance Roles
            new { Id = Guid.NewGuid(), Name = "Compliance Officer", DisplayName = "Compliance Officer", Description = "Regulatory compliance and audit coordination", Category = "Compliance", Icon = "bi-clipboard-check-fill", Color = "#059669", SortOrder = 30, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "Auditor", DisplayName = "Internal Auditor", Description = "Internal audit and control assessment", Category = "Compliance", Icon = "bi-search", Color = "#ca8a04", SortOrder = 31, IsSystem = true, IsActive = true, CanApprove = false, CanEscalate = false, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "Risk Manager", DisplayName = "Risk Manager", Description = "Risk assessment and mitigation oversight", Category = "Compliance", Icon = "bi-exclamation-triangle-fill", Color = "#dc2626", SortOrder = 32, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },

            // Operations Roles
            new { Id = Guid.NewGuid(), Name = "HR Manager", DisplayName = "HR Manager", Description = "Human resources management and employee lifecycle", Category = "Operations", Icon = "bi-people-fill", Color = "#ec4899", SortOrder = 40, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = true, CreatedAt = now, CreatedBy = "System" },
            new { Id = Guid.NewGuid(), Name = "Facilities Manager", DisplayName = "Facilities Manager", Description = "Physical access and building management", Category = "Operations", Icon = "bi-building", Color = "#64748b", SortOrder = 41, IsSystem = true, IsActive = true, CanApprove = true, CanEscalate = false, CreatedAt = now, CreatedBy = "System" }
        };
    }
}
