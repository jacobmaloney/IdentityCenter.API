using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Dapper;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Ensures communication-related tables exist (TeamsMessageQueueItems, AdminNotifications).
/// These tables support Teams bot messaging and Admin Chat System Feed.
/// </summary>
public class CommunicationTablesSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<CommunicationTablesSeedService> _logger;

    public CommunicationTablesSeedService(
        IConfiguration configuration,
        ILogger<CommunicationTablesSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? "";
        _logger = logger;
    }

    /// <summary>
    /// Creates communication tables if they don't exist
    /// </summary>
    public async Task EnsureCommunicationTablesAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            _logger.LogWarning("No connection string - skipping communication tables seed");
            return;
        }

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Create TeamsMessageQueueItems table
            await connection.ExecuteAsync(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='TeamsMessageQueueItems' AND xtype='U')
                BEGIN
                    CREATE TABLE TeamsMessageQueueItems (
                        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        Recipient NVARCHAR(255) NOT NULL,
                        RecipientType NVARCHAR(50) NOT NULL DEFAULT 'User',
                        MessageContent NVARCHAR(MAX) NOT NULL,
                        IsAdaptiveCard BIT NOT NULL DEFAULT 0,
                        Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                        ErrorMessage NVARCHAR(MAX) NULL,
                        RetryCount INT NOT NULL DEFAULT 0,
                        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        ProcessedAt DATETIME2 NULL,
                        SentAt DATETIME2 NULL
                    );
                    CREATE INDEX IX_TeamsMessageQueueItems_Status ON TeamsMessageQueueItems(Status);
                    CREATE INDEX IX_TeamsMessageQueueItems_CreatedAt ON TeamsMessageQueueItems(CreatedAt);
                END");

            // Create AdminNotifications table for System Feed
            await connection.ExecuteAsync(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AdminNotifications' AND xtype='U')
                BEGIN
                    CREATE TABLE AdminNotifications (
                        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        NotificationType NVARCHAR(50) NOT NULL DEFAULT 'Info',
                        Category NVARCHAR(50) NOT NULL DEFAULT 'System',
                        Severity NVARCHAR(20) NOT NULL DEFAULT 'Info',
                        Title NVARCHAR(255) NOT NULL,
                        Message NVARCHAR(MAX) NOT NULL,
                        ActionUrl NVARCHAR(500) NULL,
                        ActionText NVARCHAR(100) NULL,
                        RelatedEntityId UNIQUEIDENTIFIER NULL,
                        RelatedEntityType NVARCHAR(100) NULL,
                        Source NVARCHAR(100) NOT NULL DEFAULT 'System',
                        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                        IsRead BIT NOT NULL DEFAULT 0,
                        ReadAt DATETIME2 NULL,
                        ReadBy NVARCHAR(255) NULL,
                        IsDismissed BIT NOT NULL DEFAULT 0,
                        DismissedAt DATETIME2 NULL,
                        Metadata NVARCHAR(MAX) NULL
                    );
                    CREATE INDEX IX_AdminNotifications_CreatedAt ON AdminNotifications(CreatedAt DESC);
                    CREATE INDEX IX_AdminNotifications_Category ON AdminNotifications(Category);
                    CREATE INDEX IX_AdminNotifications_IsRead ON AdminNotifications(IsRead);
                END
                ELSE
                BEGIN
                    -- Add missing columns if table exists but columns don't
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('AdminNotifications') AND name = 'DismissedAt')
                        ALTER TABLE AdminNotifications ADD DismissedAt DATETIME2 NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('AdminNotifications') AND name = 'Metadata')
                        ALTER TABLE AdminNotifications ADD Metadata NVARCHAR(MAX) NULL;
                END");

            _logger.LogInformation("📢 Communication tables verified (TeamsMessageQueueItems, AdminNotifications)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify communication tables - may need manual creation");
        }
    }
}
