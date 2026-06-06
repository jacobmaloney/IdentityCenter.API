using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds industry-standard compliance frameworks
/// Makes Certification Center enterprise-ready with SOX, HIPAA, PCI-DSS, GDPR out of the box!
/// </summary>
public class ComplianceFrameworksSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<ComplianceFrameworksSeedService> _logger;

    // Fixed GUIDs for frameworks - allows policies to reference them during seeding
    public static readonly Guid SoxFrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    public static readonly Guid HipaaFrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111102");
    public static readonly Guid PciDssFrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111103");
    public static readonly Guid GdprFrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111104");
    public static readonly Guid Iso27001FrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111105");
    public static readonly Guid Nist80053FrameworkId = Guid.Parse("11111111-1111-1111-1111-111111111106");

    public ComplianceFrameworksSeedService(
        IConfiguration configuration,
        ILogger<ComplianceFrameworksSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    /// <summary>
    /// Seeds the big-name compliance frameworks that enterprises actually use
    /// This is the shit that makes CFOs and auditors happy!
    /// </summary>
    public async Task SeedComplianceFrameworksAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Quick check - if all built-in frameworks exist, skip entirely
        var existingCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ComplianceFrameworks WHERE IsBuiltIn = 1");

        if (existingCount >= 6) // We have 6 built-in frameworks
        {
            _logger.LogDebug("Compliance frameworks already seeded ({Count} found), skipping", existingCount);
            return;
        }

        _logger.LogInformation("Seeding compliance frameworks - enterprise-grade goodness!");

        var frameworks = new[]
        {
            new ComplianceFramework
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
                IsActive = false, // Built-in frameworks disabled by default - user enables via Quick Setup
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ComplianceFramework
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
                IsActive = false, // Built-in frameworks disabled by default - user enables via Quick Setup
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ComplianceFramework
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
                IsActive = false, // Built-in frameworks disabled by default - user enables via Quick Setup
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ComplianceFramework
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
                IsActive = false, // Built-in frameworks disabled by default - user enables via Quick Setup
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ComplianceFramework
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
                IsActive = false, // Built-in frameworks disabled by default - user enables via Quick Setup
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            },
            new ComplianceFramework
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
                IsActive = false, // Built-in frameworks disabled by default - user enables via Quick Setup
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        int created = 0;
        int skipped = 0;

        const string checkSql = "SELECT COUNT(*) FROM ComplianceFrameworks WHERE Code = @Code";
        const string insertSql = @"
            INSERT INTO ComplianceFrameworks
                (Id, Name, Code, Description, Category, Authority, Jurisdiction, Industry, Color, Icon, IsActive, IsBuiltIn, CreatedAt)
            VALUES
                (@Id, @Name, @Code, @Description, @Category, @Authority, @Jurisdiction, @Industry, @Color, @Icon, @IsActive, @IsBuiltIn, @CreatedAt)";

        foreach (var framework in frameworks)
        {
            // Check if framework already exists by code
            var existingByCode = await connection.ExecuteScalarAsync<int>(checkSql, new { framework.Code });
            if (existingByCode > 0)
            {
                _logger.LogDebug("Framework '{FrameworkCode}' already exists, skipping", framework.Code);
                skipped++;
                continue;
            }

            await connection.ExecuteAsync(insertSql, framework);
            _logger.LogInformation("Created compliance framework '{Code}' - {Industry}",
                framework.Code, framework.Industry);
            created++;
        }

        _logger.LogInformation("Compliance frameworks seeding complete! Created: {Created}, Skipped: {Skipped}", created, skipped);
    }
}
