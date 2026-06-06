using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DataAccessLibrary.Services.DapperSeedData;

/// <summary>
/// Dapper-based compliance framework seeding.
/// Seeds industry-standard frameworks: SOX, HIPAA, PCI-DSS, GDPR, ISO 27001, NIST 800-53.
/// </summary>
public class DapperFrameworkSeedService : DapperSeedServiceBase
{
    // Fixed GUIDs for frameworks - allows policies to reference them during seeding
    public static readonly Guid SoxFrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    public static readonly Guid HipaaFrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111102");
    public static readonly Guid PciDssFrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111103");
    public static readonly Guid GdprFrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111104");
    public static readonly Guid Iso27001FrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111105");
    public static readonly Guid Nist80053FrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111106");

    public DapperFrameworkSeedService(
        IConfiguration configuration,
        ILogger<DapperFrameworkSeedService> logger)
        : base(configuration, logger)
    {
    }

    public override async Task SeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var sw = Stopwatch.StartNew();

        // Check if frameworks already exist
        var existingCount = await GetCountAsync(connection, transaction, "ComplianceFrameworks", "IsBuiltIn = 1");
        if (existingCount >= 6)
        {
            _logger.LogDebug("Compliance frameworks already seeded ({Count} found), skipping", existingCount);
            return;
        }

        var frameworks = GetDefaultFrameworks();

        const string insertSql = @"
            INSERT INTO ComplianceFrameworks (
                Id, Name, Code, Description, Category, Authority, Jurisdiction,
                Industry, Color, Icon, IsActive, IsBuiltIn, CreatedAt
            )
            SELECT @Id, @Name, @Code, @Description, @Category, @Authority, @Jurisdiction,
                   @Industry, @Color, @Icon, @IsActive, @IsBuiltIn, @CreatedAt
            WHERE NOT EXISTS (SELECT 1 FROM ComplianceFrameworks WHERE Code = @Code)";

        int created = 0;
        foreach (var framework in frameworks)
        {
            var rowsAffected = await InsertAsync(connection, transaction, insertSql, framework);
            if (rowsAffected > 0) created++;
        }

        sw.Stop();
        LogSeedComplete("ComplianceFrameworks", created, frameworks.Count - created, sw.Elapsed);
    }

    private static List<object> GetDefaultFrameworks()
    {
        var now = DateTime.UtcNow;
        return new List<object>
        {
            new
            {
                Id = SoxFrameworkId,
                Name = "SOX (Sarbanes-Oxley)",
                Code = "SOX",
                Description = "Sarbanes-Oxley Act - Financial controls and user access segregation for public companies. Requires quarterly access reviews and strict separation of duties.",
                Category = "Regulatory",
                Authority = "U.S. Securities and Exchange Commission (SEC)",
                Jurisdiction = "United States",
                Industry = "Financial Services",
                Color = "#dc2626",
                Icon = "fa-balance-scale",
                IsActive = false,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = HipaaFrameworkId,
                Name = "HIPAA (Health Insurance Portability and Accountability Act)",
                Code = "HIPAA",
                Description = "Healthcare data privacy and security regulation. Mandates access controls, audit logs, and regular reviews for systems containing Protected Health Information (PHI).",
                Category = "Regulatory",
                Authority = "U.S. Department of Health and Human Services (HHS)",
                Jurisdiction = "United States",
                Industry = "Healthcare",
                Color = "#10b981",
                Icon = "fa-heartbeat",
                IsActive = false,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = PciDssFrameworkId,
                Name = "PCI-DSS (Payment Card Industry Data Security Standard)",
                Code = "PCI-DSS",
                Description = "Payment card industry data security requirements. Mandates quarterly access reviews, strong authentication, and monitoring for systems processing payment card data.",
                Category = "Industry",
                Authority = "PCI Security Standards Council",
                Jurisdiction = "Global",
                Industry = "Financial Services",
                Color = "#3b82f6",
                Icon = "fa-credit-card",
                IsActive = false,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = GdprFrameworkId,
                Name = "GDPR (General Data Protection Regulation)",
                Code = "GDPR",
                Description = "EU data protection and privacy regulation. Requires data access accountability, user consent tracking, and regular reviews of personal data access.",
                Category = "Privacy",
                Authority = "European Data Protection Board (EDPB)",
                Jurisdiction = "European Union",
                Industry = "All",
                Color = "#8b5cf6",
                Icon = "fa-shield-alt",
                IsActive = false,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Iso27001FrameworkId,
                Name = "ISO 27001 (Information Security Management)",
                Code = "ISO27001",
                Description = "International standard for information security management systems. Requires access control policies, regular reviews, and security monitoring.",
                Category = "Security",
                Authority = "International Organization for Standardization (ISO)",
                Jurisdiction = "Global",
                Industry = "All",
                Color = "#f59e0b",
                Icon = "fa-lock",
                IsActive = false,
                IsBuiltIn = true,
                CreatedAt = now
            },
            new
            {
                Id = Nist80053FrameworkId,
                Name = "NIST 800-53 (Federal Information Security)",
                Code = "NIST80053",
                Description = "Security controls for federal information systems and organizations. Mandates access reviews, audit logging, and continuous monitoring.",
                Category = "Regulatory",
                Authority = "National Institute of Standards and Technology (NIST)",
                Jurisdiction = "United States",
                Industry = "Government",
                Color = "#06b6d4",
                Icon = "fa-flag-usa",
                IsActive = false,
                IsBuiltIn = true,
                CreatedAt = now
            }
        };
    }
}
