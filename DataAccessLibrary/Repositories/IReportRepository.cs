using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IReportRepository
{
    // Reports CRUD
    Task<IEnumerable<Report>> GetAllReportsAsync();
    Task<IEnumerable<Report>> GetActiveReportsAsync();
    Task<IEnumerable<Report>> GetReportsByCategoryAsync(string category);
    Task<IEnumerable<Report>> GetBuiltInReportsAsync();
    Task<Report?> GetReportByIdAsync(Guid id);
    Task<Report?> GetReportByNameAsync(string name);
    Task<Guid> CreateReportAsync(Report report);
    Task UpdateReportAsync(Report report);
    Task DeleteReportAsync(Guid id);

    // Report Columns
    Task<IEnumerable<ReportColumn>> GetReportColumnsAsync(Guid reportId);
    Task SaveReportColumnsAsync(Guid reportId, IEnumerable<ReportColumn> columns);

    // Report Parameters
    Task<IEnumerable<ReportParameter>> GetReportParametersAsync(Guid reportId);
    Task SaveReportParametersAsync(Guid reportId, IEnumerable<ReportParameter> parameters);

    // Report Execution
    Task<Guid> LogReportExecutionAsync(ReportExecution execution);
    Task<IEnumerable<ReportExecution>> GetReportExecutionHistoryAsync(Guid reportId, int limit = 50);
    Task<IEnumerable<ReportExecution>> GetRecentExecutionsAsync(int limit = 100);

    // Report Schedules
    Task<IEnumerable<ReportSchedule>> GetReportSchedulesAsync(Guid reportId);
    Task<IEnumerable<ReportSchedule>> GetActiveSchedulesAsync();
    Task<IEnumerable<ReportSchedule>> GetAllSchedulesAsync();
    Task<Guid> CreateScheduleAsync(ReportSchedule schedule);
    Task UpdateScheduleAsync(ReportSchedule schedule);
    Task DeleteScheduleAsync(Guid id);

    // User Favorites
    Task<IEnumerable<Report>> GetUserFavoriteReportsAsync(Guid userId);
    Task AddToFavoritesAsync(Guid userId, Guid reportId);
    Task RemoveFromFavoritesAsync(Guid userId, Guid reportId);

    // Report Templates
    Task<IEnumerable<ReportTemplate>> GetReportTemplatesAsync();
    Task<ReportTemplate?> GetReportTemplateByIdAsync(Guid id);

    // Search
    Task<IEnumerable<Report>> SearchReportsAsync(string searchTerm, string? category = null);

    // Query Execution
    Task<(IEnumerable<string> columns, IEnumerable<Dictionary<string, object?>> rows)> ExecuteReportQueryAsync(
        string query, Dictionary<string, object> parameters, int maxRows = 1000);

    // Statistics
    Task<int> GetTotalReportCountAsync();
    Task<int> GetTotalExecutionCountAsync();
    Task<Dictionary<string, int>> GetExecutionsByCategory(DateTime fromDate);
}
