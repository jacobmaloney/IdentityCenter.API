using Dapper;
using DataAccessLibrary.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccessLibrary.Repositories;

public class ReportRepository : IReportRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ReportRepository> _logger;

    public ReportRepository(IConfiguration configuration, ILogger<ReportRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentNullException("Connection string not found");
        _logger = logger;
    }

    // =====================================
    // Reports CRUD
    // =====================================

    public async Task<IEnumerable<Report>> GetAllReportsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<Report>(
            @"SELECT * FROM Reports ORDER BY Category, SortOrder, DisplayName");
    }

    public async Task<IEnumerable<Report>> GetActiveReportsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<Report>(
            @"SELECT * FROM Reports WHERE IsActive = 1 ORDER BY Category, SortOrder, DisplayName");
    }

    public async Task<IEnumerable<Report>> GetReportsByCategoryAsync(string category)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<Report>(
            @"SELECT * FROM Reports WHERE Category = @Category AND IsActive = 1
              ORDER BY SortOrder, DisplayName",
            new { Category = category });
    }

    public async Task<IEnumerable<Report>> GetBuiltInReportsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<Report>(
            @"SELECT * FROM Reports WHERE IsBuiltIn = 1 ORDER BY Category, SortOrder, DisplayName");
    }

    public async Task<Report?> GetReportByIdAsync(Guid id)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Report>(
            @"SELECT * FROM Reports WHERE Id = @Id", new { Id = id });
    }

    public async Task<Report?> GetReportByNameAsync(string name)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<Report>(
            @"SELECT * FROM Reports WHERE Name = @Name", new { Name = name });
    }

    public async Task<Guid> CreateReportAsync(Report report)
    {
        using var connection = new SqlConnection(_connectionString);
        report.Id = report.Id == Guid.Empty ? Guid.NewGuid() : report.Id;

        // Use longer timeout for seed operations (default 30s can timeout on slow/busy servers)
        await connection.ExecuteAsync(
            new CommandDefinition(
                @"INSERT INTO Reports (Id, Name, DisplayName, Description, Category, SubCategory, Icon,
                    QueryDefinition, ConfigurationJson, DefaultFilters, ParametersJson, IsBuiltIn, IsActive,
                    IsPublic, RequiredRole, Tags, SortOrder, CreatedAt, CreatedBy)
                  VALUES (@Id, @Name, @DisplayName, @Description, @Category, @SubCategory, @Icon,
                    @QueryDefinition, @ConfigurationJson, @DefaultFilters, @ParametersJson, @IsBuiltIn, @IsActive,
                    @IsPublic, @RequiredRole, @Tags, @SortOrder, @CreatedAt, @CreatedBy)",
                report,
                commandTimeout: 120)); // 2 minute timeout

        return report.Id;
    }

    public async Task UpdateReportAsync(Report report)
    {
        using var connection = new SqlConnection(_connectionString);
        report.ModifiedAt = DateTime.UtcNow;

        await connection.ExecuteAsync(
            @"UPDATE Reports SET
                Name = @Name, DisplayName = @DisplayName, Description = @Description,
                Category = @Category, SubCategory = @SubCategory, Icon = @Icon,
                QueryDefinition = @QueryDefinition, ConfigurationJson = @ConfigurationJson,
                DefaultFilters = @DefaultFilters, ParametersJson = @ParametersJson,
                IsActive = @IsActive, IsPublic = @IsPublic, RequiredRole = @RequiredRole,
                Tags = @Tags, SortOrder = @SortOrder, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
              WHERE Id = @Id",
            report);
    }

    public async Task DeleteReportAsync(Guid id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            @"DELETE FROM Reports WHERE Id = @Id AND IsBuiltIn = 0", new { Id = id });
    }

    // =====================================
    // Report Columns
    // =====================================

    public async Task<IEnumerable<ReportColumn>> GetReportColumnsAsync(Guid reportId)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ReportColumn>(
            @"SELECT * FROM ReportColumns WHERE ReportId = @ReportId ORDER BY SortOrder",
            new { ReportId = reportId });
    }

    public async Task SaveReportColumnsAsync(Guid reportId, IEnumerable<ReportColumn> columns)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        try
        {
            // Delete existing columns
            await connection.ExecuteAsync(
                "DELETE FROM ReportColumns WHERE ReportId = @ReportId",
                new { ReportId = reportId }, transaction);

            // Insert new columns
            foreach (var column in columns)
            {
                column.Id = Guid.NewGuid();
                column.ReportId = reportId;
                await connection.ExecuteAsync(
                    @"INSERT INTO ReportColumns (Id, ReportId, ColumnName, DisplayName, DataType, FormatString,
                        SortOrder, IsVisible, AllowFilter, AllowSort, IsRequired, DefaultSortDirection, Width, AggregateFunction)
                      VALUES (@Id, @ReportId, @ColumnName, @DisplayName, @DataType, @FormatString,
                        @SortOrder, @IsVisible, @AllowFilter, @AllowSort, @IsRequired, @DefaultSortDirection, @Width, @AggregateFunction)",
                    column, transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =====================================
    // Report Parameters
    // =====================================

    public async Task<IEnumerable<ReportParameter>> GetReportParametersAsync(Guid reportId)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ReportParameter>(
            @"SELECT * FROM ReportParameters WHERE ReportId = @ReportId ORDER BY SortOrder",
            new { ReportId = reportId });
    }

    public async Task SaveReportParametersAsync(Guid reportId, IEnumerable<ReportParameter> parameters)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(
                "DELETE FROM ReportParameters WHERE ReportId = @ReportId",
                new { ReportId = reportId }, transaction);

            foreach (var param in parameters)
            {
                param.Id = Guid.NewGuid();
                param.ReportId = reportId;
                await connection.ExecuteAsync(
                    @"INSERT INTO ReportParameters (Id, ReportId, ParameterName, DisplayName, DataType, ControlType,
                        IsRequired, DefaultValue, OptionsSource, ValidationRules, SortOrder)
                      VALUES (@Id, @ReportId, @ParameterName, @DisplayName, @DataType, @ControlType,
                        @IsRequired, @DefaultValue, @OptionsSource, @ValidationRules, @SortOrder)",
                    param, transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // =====================================
    // Report Execution
    // =====================================

    public async Task<Guid> LogReportExecutionAsync(ReportExecution execution)
    {
        using var connection = new SqlConnection(_connectionString);
        execution.Id = Guid.NewGuid();

        await connection.ExecuteAsync(
            @"INSERT INTO ReportExecutions (Id, ReportId, ScheduleId, ExecutedAt, ExecutedBy, ExecutionContext,
                ExecutionTimeMs, [RowCount], Status, ErrorMessage, ParametersUsed, OutputFormat, OutputFilePath)
              VALUES (@Id, @ReportId, @ScheduleId, @ExecutedAt, @ExecutedBy, @ExecutionContext,
                @ExecutionTimeMs, @ResultRowCount, @Status, @ErrorMessage, @ParametersUsed, @OutputFormat, @OutputFilePath)",
            execution);

        return execution.Id;
    }

    public async Task<IEnumerable<ReportExecution>> GetReportExecutionHistoryAsync(Guid reportId, int limit = 50)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ReportExecution>(
            @"SELECT TOP (@Limit) * FROM ReportExecutions WHERE ReportId = @ReportId
              ORDER BY ExecutedAt DESC",
            new { ReportId = reportId, Limit = limit });
    }

    public async Task<IEnumerable<ReportExecution>> GetRecentExecutionsAsync(int limit = 100)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ReportExecution>(
            @"SELECT TOP (@Limit) e.*, r.DisplayName as ReportDisplayName
              FROM ReportExecutions e
              INNER JOIN Reports r ON e.ReportId = r.Id
              ORDER BY e.ExecutedAt DESC",
            new { Limit = limit });
    }

    // =====================================
    // Report Schedules
    // =====================================

    public async Task<IEnumerable<ReportSchedule>> GetReportSchedulesAsync(Guid reportId)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ReportSchedule>(
            @"SELECT * FROM ReportSchedules WHERE ReportId = @ReportId",
            new { ReportId = reportId });
    }

    public async Task<IEnumerable<ReportSchedule>> GetActiveSchedulesAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ReportSchedule>(
            @"SELECT s.*, r.Name as ReportName, r.DisplayName as ReportDisplayName
              FROM ReportSchedules s
              INNER JOIN Reports r ON s.ReportId = r.Id
              WHERE s.IsActive = 1 AND r.IsActive = 1
              ORDER BY s.NextExecutionAt");
    }

    public async Task<IEnumerable<ReportSchedule>> GetAllSchedulesAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ReportSchedule>(
            @"SELECT s.*, r.Name as ReportName, r.DisplayName as ReportDisplayName
              FROM ReportSchedules s
              INNER JOIN Reports r ON s.ReportId = r.Id
              WHERE r.IsActive = 1
              ORDER BY s.IsActive DESC, s.NextExecutionAt");
    }

    public async Task<Guid> CreateScheduleAsync(ReportSchedule schedule)
    {
        using var connection = new SqlConnection(_connectionString);
        schedule.Id = Guid.NewGuid();

        await connection.ExecuteAsync(
            @"INSERT INTO ReportSchedules (Id, ReportId, Name, Frequency, CronExpression, ExecutionTime,
                DayOfWeek, DayOfMonth, IsActive, OutputFormat, EmailRecipients, EmailSubject, EmailBody,
                AttachReport, EmbedInEmail, ParameterValuesJson, NextExecutionAt, CreatedAt, CreatedBy)
              VALUES (@Id, @ReportId, @Name, @Frequency, @CronExpression, @ExecutionTime,
                @DayOfWeek, @DayOfMonth, @IsActive, @OutputFormat, @EmailRecipients, @EmailSubject, @EmailBody,
                @AttachReport, @EmbedInEmail, @ParameterValuesJson, @NextExecutionAt, @CreatedAt, @CreatedBy)",
            schedule);

        return schedule.Id;
    }

    public async Task UpdateScheduleAsync(ReportSchedule schedule)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            @"UPDATE ReportSchedules SET
                Name = @Name, Frequency = @Frequency, CronExpression = @CronExpression,
                ExecutionTime = @ExecutionTime, DayOfWeek = @DayOfWeek, DayOfMonth = @DayOfMonth,
                IsActive = @IsActive, OutputFormat = @OutputFormat, EmailRecipients = @EmailRecipients,
                EmailSubject = @EmailSubject, EmailBody = @EmailBody, AttachReport = @AttachReport,
                EmbedInEmail = @EmbedInEmail, ParameterValuesJson = @ParameterValuesJson,
                LastExecutedAt = @LastExecutedAt, NextExecutionAt = @NextExecutionAt,
                LastExecutionStatus = @LastExecutionStatus, LastExecutionError = @LastExecutionError
              WHERE Id = @Id",
            schedule);
    }

    public async Task DeleteScheduleAsync(Guid id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync("DELETE FROM ReportSchedules WHERE Id = @Id", new { Id = id });
    }

    // =====================================
    // User Favorites
    // =====================================

    public async Task<IEnumerable<Report>> GetUserFavoriteReportsAsync(Guid userId)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<Report>(
            @"SELECT r.* FROM Reports r
              INNER JOIN UserReportFavorites f ON r.Id = f.ReportId
              WHERE f.UserId = @UserId AND r.IsActive = 1
              ORDER BY f.SortOrder, r.DisplayName",
            new { UserId = userId });
    }

    public async Task AddToFavoritesAsync(Guid userId, Guid reportId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            @"IF NOT EXISTS (SELECT 1 FROM UserReportFavorites WHERE UserId = @UserId AND ReportId = @ReportId)
              INSERT INTO UserReportFavorites (Id, UserId, ReportId, AddedAt, SortOrder)
              VALUES (NEWID(), @UserId, @ReportId, GETUTCDATE(), 0)",
            new { UserId = userId, ReportId = reportId });
    }

    public async Task RemoveFromFavoritesAsync(Guid userId, Guid reportId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "DELETE FROM UserReportFavorites WHERE UserId = @UserId AND ReportId = @ReportId",
            new { UserId = userId, ReportId = reportId });
    }

    // =====================================
    // Report Templates
    // =====================================

    public async Task<IEnumerable<ReportTemplate>> GetReportTemplatesAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryAsync<ReportTemplate>(
            @"SELECT * FROM ReportTemplates WHERE IsActive = 1 ORDER BY Category, SortOrder, Name");
    }

    public async Task<ReportTemplate?> GetReportTemplateByIdAsync(Guid id)
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<ReportTemplate>(
            "SELECT * FROM ReportTemplates WHERE Id = @Id", new { Id = id });
    }

    // =====================================
    // Search
    // =====================================

    public async Task<IEnumerable<Report>> SearchReportsAsync(string searchTerm, string? category = null)
    {
        using var connection = new SqlConnection(_connectionString);
        var sql = @"SELECT * FROM Reports WHERE IsActive = 1 AND
                    (Name LIKE @SearchTerm OR DisplayName LIKE @SearchTerm OR Description LIKE @SearchTerm OR Tags LIKE @SearchTerm)";

        if (!string.IsNullOrEmpty(category))
            sql += " AND Category = @Category";

        sql += " ORDER BY Category, SortOrder, DisplayName";

        return await connection.QueryAsync<Report>(sql, new { SearchTerm = $"%{searchTerm}%", Category = category });
    }

    // =====================================
    // Statistics
    // =====================================

    public async Task<int> GetTotalReportCountAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Reports WHERE IsActive = 1");
    }

    public async Task<int> GetTotalExecutionCountAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ReportExecutions");
    }

    public async Task<Dictionary<string, int>> GetExecutionsByCategory(DateTime fromDate)
    {
        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync<(string Category, int Count)>(
            @"SELECT r.Category, COUNT(*) as Count
              FROM ReportExecutions e
              INNER JOIN Reports r ON e.ReportId = r.Id
              WHERE e.ExecutedAt >= @FromDate
              GROUP BY r.Category",
            new { FromDate = fromDate });

        return results.ToDictionary(r => r.Category, r => r.Count);
    }

    // =====================================
    // Query Execution
    // =====================================

    public async Task<(IEnumerable<string> columns, IEnumerable<Dictionary<string, object?>> rows)> ExecuteReportQueryAsync(
        string query, Dictionary<string, object> parameters, int maxRows = 1000)
    {
        var columns = new List<string>();
        var rows = new List<Dictionary<string, object?>>();

        // Security check - only allow SELECT statements
        var queryTrimmed = query.Trim();
        if (!queryTrimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !queryTrimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only SELECT queries are allowed");
        }

        // Check for dangerous SQL keywords
        var dangerousPatterns = new[] { "DROP", "DELETE", "UPDATE", "INSERT", "TRUNCATE", "EXEC", "EXECUTE", "ALTER" };
        foreach (var pattern in dangerousPatterns)
        {
            if (query.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Query contains disallowed keyword: {pattern}");
            }
        }

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        using var command = new SqlCommand(query, connection);
        command.CommandTimeout = 120; // 2 minute timeout

        // Add parameters
        foreach (var param in parameters)
        {
            command.Parameters.AddWithValue($"@{param.Key}", param.Value ?? DBNull.Value);
        }

        using var reader = await command.ExecuteReaderAsync();

        // Get column names
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        // Read rows up to maxRows
        while (await reader.ReadAsync() && rows.Count < maxRows)
        {
            var row = new Dictionary<string, object?>();
            foreach (var col in columns)
            {
                var value = reader[col];
                row[col] = value == DBNull.Value ? null : value;
            }
            rows.Add(row);
        }

        return (columns, rows);
    }
}
