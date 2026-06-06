using DataAccessLibrary.Models;

namespace DataAccessLibrary.Repositories;

public interface IAnomalyDataRepository
{
    Task<List<AnomalyUserRecord>> GetDormantAccountsAwakenedAsync(int dormantDays);
    Task<List<PrivilegeEscalationRecord>> GetRecentPrivilegeEscalationsAsync();
    Task<List<SuddenGroupChangeRecord>> GetSuddenGroupChangesAsync(int threshold);
    Task<List<DisabledAccountActivityRecord>> GetDisabledAccountsWithActivityAsync();
}

public class AnomalyUserRecord
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? Department { get; set; }
    public string? LastSignIn { get; set; }
    public string? PreviousSignIn { get; set; }
}

public class PrivilegeEscalationRecord
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? Department { get; set; }
    public string? GroupName { get; set; }
    public DateTime? AddedAt { get; set; }
}

public class SuddenGroupChangeRecord
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? Department { get; set; }
    public int NewGroupCount { get; set; }
}

public class DisabledAccountActivityRecord
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? Department { get; set; }
    public string? LastActivity { get; set; }
}
