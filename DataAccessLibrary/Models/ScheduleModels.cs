using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DataAccessLibrary.Models;

// ============================================================================
// USER-FRIENDLY SCHEDULE CONFIGURATION MODELS
// Replaces raw cron expressions with intuitive date/time/frequency pickers
// ============================================================================

/// <summary>
/// User-friendly schedule configuration that converts to cron expressions
/// </summary>
public class ScheduleConfiguration
{
    /// <summary>When the schedule starts</summary>
    [Required]
    public DateTime StartDate { get; set; } = DateTime.Today;

    /// <summary>Time of day for execution (hour and minute)</summary>
    [Required]
    public TimeSpan StartTime { get; set; } = new TimeSpan(9, 0, 0); // 9:00 AM default

    /// <summary>Optional end date for the schedule</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>How often the schedule runs</summary>
    [Required]
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;

    /// <summary>Interval count (every N days/weeks/months)</summary>
    [Range(1, 365)]
    public int IntervalCount { get; set; } = 1;

    /// <summary>Days of week for weekly frequency</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    /// <summary>Day of month for monthly frequency (1-31, or -1 for last day)</summary>
    [Range(-1, 31)]
    public int DayOfMonth { get; set; } = 1;

    /// <summary>Pattern for monthly schedules (first Monday, last Friday, etc.)</summary>
    public MonthlyDayPattern MonthlyPattern { get; set; } = MonthlyDayPattern.SpecificDay;

    /// <summary>Day of week for monthly pattern (e.g., "second Monday")</summary>
    public DayOfWeek? MonthlyPatternDayOfWeek { get; set; }

    /// <summary>Months for yearly/quarterly frequency</summary>
    public List<int> Months { get; set; } = new() { 1 }; // January default

    /// <summary>Maximum number of occurrences (null = unlimited)</summary>
    public int? MaxOccurrences { get; set; }

    /// <summary>Timezone for schedule interpretation</summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>Generated cron expression (set by converter)</summary>
    public string? CronExpression { get; set; }

    /// <summary>Human-readable description (set by converter)</summary>
    public string? Description { get; set; }

    /// <summary>Combines StartDate and StartTime into a single DateTime</summary>
    public DateTime StartDateTime => StartDate.Date.Add(StartTime);
}

/// <summary>
/// Schedule frequency options
/// </summary>
public enum ScheduleFrequency
{
    /// <summary>Run once at specified date/time</summary>
    Once,

    /// <summary>Run every N minutes</summary>
    Minute,

    /// <summary>Run every N hours</summary>
    Hourly,

    /// <summary>Run every N days</summary>
    Daily,

    /// <summary>Run on specific days of the week</summary>
    Weekly,

    /// <summary>Run monthly on specific day</summary>
    Monthly,

    /// <summary>Run quarterly (every 3 months)</summary>
    Quarterly,

    /// <summary>Run yearly on specific date</summary>
    Yearly,

    /// <summary>Custom cron expression</summary>
    Custom
}

/// <summary>
/// Pattern options for monthly schedules
/// </summary>
public enum MonthlyDayPattern
{
    /// <summary>Specific day of month (e.g., 15th)</summary>
    SpecificDay,

    /// <summary>First occurrence of day (e.g., first Monday)</summary>
    First,

    /// <summary>Second occurrence of day (e.g., second Monday)</summary>
    Second,

    /// <summary>Third occurrence of day</summary>
    Third,

    /// <summary>Fourth occurrence of day</summary>
    Fourth,

    /// <summary>Last occurrence of day (e.g., last Friday)</summary>
    Last,

    /// <summary>Last day of the month</summary>
    LastDayOfMonth
}

/// <summary>
/// Common schedule presets for quick selection
/// </summary>
public static class SchedulePresets
{
    public static ScheduleConfiguration Daily9AM => new()
    {
        Frequency = ScheduleFrequency.Daily,
        StartTime = new TimeSpan(9, 0, 0),
        IntervalCount = 1,
        Description = "Every day at 9:00 AM"
    };

    public static ScheduleConfiguration Weekdays9AM => new()
    {
        Frequency = ScheduleFrequency.Weekly,
        StartTime = new TimeSpan(9, 0, 0),
        DaysOfWeek = new List<DayOfWeek>
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday
        },
        Description = "Every weekday at 9:00 AM"
    };

    public static ScheduleConfiguration WeeklyMonday9AM => new()
    {
        Frequency = ScheduleFrequency.Weekly,
        StartTime = new TimeSpan(9, 0, 0),
        DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday },
        Description = "Every Monday at 9:00 AM"
    };

    public static ScheduleConfiguration MonthlyFirst => new()
    {
        Frequency = ScheduleFrequency.Monthly,
        StartTime = new TimeSpan(9, 0, 0),
        DayOfMonth = 1,
        MonthlyPattern = MonthlyDayPattern.SpecificDay,
        Description = "First day of every month at 9:00 AM"
    };

    public static ScheduleConfiguration MonthlyLast => new()
    {
        Frequency = ScheduleFrequency.Monthly,
        StartTime = new TimeSpan(9, 0, 0),
        MonthlyPattern = MonthlyDayPattern.LastDayOfMonth,
        Description = "Last day of every month at 9:00 AM"
    };

    public static ScheduleConfiguration Quarterly => new()
    {
        Frequency = ScheduleFrequency.Quarterly,
        StartTime = new TimeSpan(9, 0, 0),
        DayOfMonth = 1,
        Months = new List<int> { 1, 4, 7, 10 },
        Description = "First day of each quarter at 9:00 AM"
    };

    public static ScheduleConfiguration Every6Hours => new()
    {
        Frequency = ScheduleFrequency.Hourly,
        IntervalCount = 6,
        StartTime = new TimeSpan(0, 0, 0),
        Description = "Every 6 hours"
    };

    public static ScheduleConfiguration Every30Minutes => new()
    {
        Frequency = ScheduleFrequency.Minute,
        IntervalCount = 30,
        StartTime = new TimeSpan(0, 0, 0),
        Description = "Every 30 minutes"
    };

    /// <summary>
    /// Get all presets as a dictionary for UI selection
    /// </summary>
    public static Dictionary<string, ScheduleConfiguration> GetAll() => new()
    {
        { "Daily at 9 AM", Daily9AM },
        { "Weekdays at 9 AM", Weekdays9AM },
        { "Every Monday at 9 AM", WeeklyMonday9AM },
        { "First of Month", MonthlyFirst },
        { "Last of Month", MonthlyLast },
        { "Quarterly", Quarterly },
        { "Every 6 Hours", Every6Hours },
        { "Every 30 Minutes", Every30Minutes }
    };
}
