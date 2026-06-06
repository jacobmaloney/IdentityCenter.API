using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Services;

/// <summary>
/// Converts user-friendly ScheduleConfiguration to Quartz.NET cron expressions
/// and generates human-readable descriptions.
///
/// Quartz Cron Format: [Seconds] [Minutes] [Hours] [Day-of-Month] [Month] [Day-of-Week] [Year(optional)]
/// Special characters:
///   * = all values
///   ? = no specific value (for day-of-month or day-of-week)
///   L = last (last day of month, or last specific day like 6L = last Saturday)
///   # = nth day of month (e.g., 2#3 = third Monday)
///   / = increment (e.g., 0/15 = every 15 starting at 0)
/// </summary>
public interface ICronConverterService
{
    /// <summary>
    /// Convert a ScheduleConfiguration to a cron expression and description
    /// </summary>
    ScheduleConfiguration Convert(ScheduleConfiguration config);

    /// <summary>
    /// Generate a human-readable description from a cron expression
    /// </summary>
    string GetDescription(string cronExpression);

    /// <summary>
    /// Calculate the next N run times for a schedule
    /// </summary>
    List<DateTime> GetNextRunTimes(ScheduleConfiguration config, int count = 5);

    /// <summary>
    /// Validate a cron expression
    /// </summary>
    bool IsValidCron(string cronExpression, out string? error);

    /// <summary>
    /// Parse a cron expression back into a ScheduleConfiguration (best effort)
    /// </summary>
    ScheduleConfiguration? ParseCron(string cronExpression);
}

public class CronConverterService : ICronConverterService
{
    private static readonly string[] DayOfWeekNames = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
    private static readonly string[] MonthNames = { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    public ScheduleConfiguration Convert(ScheduleConfiguration config)
    {
        if (config.Frequency == ScheduleFrequency.Custom && !string.IsNullOrEmpty(config.CronExpression))
        {
            // Custom mode - just validate and describe
            config.Description = GetDescription(config.CronExpression);
            return config;
        }

        config.CronExpression = GenerateCronExpression(config);
        config.Description = GenerateDescription(config);
        return config;
    }

    private string GenerateCronExpression(ScheduleConfiguration config)
    {
        var second = "0";
        var minute = config.StartTime.Minutes.ToString();
        var hour = config.StartTime.Hours.ToString();
        var dayOfMonth = "?";
        var month = "*";
        var dayOfWeek = "?";

        switch (config.Frequency)
        {
            case ScheduleFrequency.Once:
                // For one-time execution, use specific date
                dayOfMonth = config.StartDate.Day.ToString();
                month = config.StartDate.Month.ToString();
                dayOfWeek = "?";
                break;

            case ScheduleFrequency.Minute:
                // Every N minutes
                second = "0";
                minute = config.IntervalCount == 1 ? "*" : $"0/{config.IntervalCount}";
                hour = "*";
                dayOfMonth = "*";
                dayOfWeek = "?";
                break;

            case ScheduleFrequency.Hourly:
                // Every N hours at specified minute
                minute = config.StartTime.Minutes.ToString();
                hour = config.IntervalCount == 1 ? "*" : $"0/{config.IntervalCount}";
                dayOfMonth = "*";
                dayOfWeek = "?";
                break;

            case ScheduleFrequency.Daily:
                // Every N days at specified time
                if (config.IntervalCount == 1)
                {
                    dayOfMonth = "*";
                }
                else
                {
                    // Quartz doesn't directly support "every N days" - use day-of-month with increment
                    dayOfMonth = $"1/{config.IntervalCount}";
                }
                dayOfWeek = "?";
                break;

            case ScheduleFrequency.Weekly:
                // Specific days of week
                if (config.DaysOfWeek.Any())
                {
                    dayOfWeek = string.Join(",", config.DaysOfWeek.Select(d => DayOfWeekNames[(int)d]));
                }
                else
                {
                    dayOfWeek = "MON"; // Default to Monday if none selected
                }
                dayOfMonth = "?";
                break;

            case ScheduleFrequency.Monthly:
                dayOfMonth = GetMonthlyDayExpression(config);
                if (config.MonthlyPattern != MonthlyDayPattern.SpecificDay &&
                    config.MonthlyPattern != MonthlyDayPattern.LastDayOfMonth)
                {
                    // For patterns like "second Monday", day-of-week is used
                    dayOfWeek = GetMonthlyDayOfWeekExpression(config);
                    dayOfMonth = "?";
                }
                month = config.IntervalCount == 1 ? "*" : $"1/{config.IntervalCount}";
                break;

            case ScheduleFrequency.Quarterly:
                // Every 3 months
                if (config.Months.Any())
                {
                    month = string.Join(",", config.Months);
                }
                else
                {
                    month = "1,4,7,10"; // Default quarters
                }
                dayOfMonth = config.DayOfMonth > 0 ? config.DayOfMonth.ToString() : "1";
                dayOfWeek = "?";
                break;

            case ScheduleFrequency.Yearly:
                if (config.Months.Any())
                {
                    month = config.Months.First().ToString();
                }
                else
                {
                    month = config.StartDate.Month.ToString();
                }
                dayOfMonth = config.DayOfMonth > 0 ? config.DayOfMonth.ToString() : config.StartDate.Day.ToString();
                dayOfWeek = "?";
                break;
        }

        return $"{second} {minute} {hour} {dayOfMonth} {month} {dayOfWeek}";
    }

    private string GetMonthlyDayExpression(ScheduleConfiguration config)
    {
        return config.MonthlyPattern switch
        {
            MonthlyDayPattern.SpecificDay => config.DayOfMonth > 0 ? config.DayOfMonth.ToString() : "1",
            MonthlyDayPattern.LastDayOfMonth => "L",
            _ => "?" // For nth-day patterns, we use day-of-week field instead
        };
    }

    private string GetMonthlyDayOfWeekExpression(ScheduleConfiguration config)
    {
        var dow = config.MonthlyPatternDayOfWeek ?? DayOfWeek.Monday;
        var dowStr = DayOfWeekNames[(int)dow];

        return config.MonthlyPattern switch
        {
            MonthlyDayPattern.First => $"{dowStr}#1",
            MonthlyDayPattern.Second => $"{dowStr}#2",
            MonthlyDayPattern.Third => $"{dowStr}#3",
            MonthlyDayPattern.Fourth => $"{dowStr}#4",
            MonthlyDayPattern.Last => $"{dowStr}L",
            _ => dowStr
        };
    }

    private string GenerateDescription(ScheduleConfiguration config)
    {
        var sb = new StringBuilder();
        var timeStr = config.StartTime.Hours == 0 && config.StartTime.Minutes == 0
            ? "midnight"
            : config.StartTime.Hours == 12 && config.StartTime.Minutes == 0
                ? "noon"
                : DateTime.Today.Add(config.StartTime).ToString("h:mm tt");

        switch (config.Frequency)
        {
            case ScheduleFrequency.Once:
                sb.Append($"Once on {config.StartDate:MMMM d, yyyy} at {timeStr}");
                break;

            case ScheduleFrequency.Minute:
                sb.Append(config.IntervalCount == 1
                    ? "Every minute"
                    : $"Every {config.IntervalCount} minutes");
                break;

            case ScheduleFrequency.Hourly:
                sb.Append(config.IntervalCount == 1
                    ? $"Every hour at :{config.StartTime.Minutes:D2}"
                    : $"Every {config.IntervalCount} hours at :{config.StartTime.Minutes:D2}");
                break;

            case ScheduleFrequency.Daily:
                sb.Append(config.IntervalCount == 1
                    ? $"Every day at {timeStr}"
                    : $"Every {config.IntervalCount} days at {timeStr}");
                break;

            case ScheduleFrequency.Weekly:
                var days = config.DaysOfWeek.Any()
                    ? string.Join(", ", config.DaysOfWeek.Select(d => d.ToString()))
                    : "Monday";

                if (config.DaysOfWeek.Count == 5 &&
                    config.DaysOfWeek.Contains(DayOfWeek.Monday) &&
                    config.DaysOfWeek.Contains(DayOfWeek.Tuesday) &&
                    config.DaysOfWeek.Contains(DayOfWeek.Wednesday) &&
                    config.DaysOfWeek.Contains(DayOfWeek.Thursday) &&
                    config.DaysOfWeek.Contains(DayOfWeek.Friday))
                {
                    days = "weekdays";
                }
                else if (config.DaysOfWeek.Count == 2 &&
                         config.DaysOfWeek.Contains(DayOfWeek.Saturday) &&
                         config.DaysOfWeek.Contains(DayOfWeek.Sunday))
                {
                    days = "weekends";
                }

                sb.Append(config.IntervalCount == 1
                    ? $"Every {days} at {timeStr}"
                    : $"Every {config.IntervalCount} weeks on {days} at {timeStr}");
                break;

            case ScheduleFrequency.Monthly:
                var dayPart = config.MonthlyPattern switch
                {
                    MonthlyDayPattern.SpecificDay => GetOrdinal(config.DayOfMonth),
                    MonthlyDayPattern.LastDayOfMonth => "last day",
                    MonthlyDayPattern.First => $"first {config.MonthlyPatternDayOfWeek}",
                    MonthlyDayPattern.Second => $"second {config.MonthlyPatternDayOfWeek}",
                    MonthlyDayPattern.Third => $"third {config.MonthlyPatternDayOfWeek}",
                    MonthlyDayPattern.Fourth => $"fourth {config.MonthlyPatternDayOfWeek}",
                    MonthlyDayPattern.Last => $"last {config.MonthlyPatternDayOfWeek}",
                    _ => GetOrdinal(config.DayOfMonth)
                };

                sb.Append(config.IntervalCount == 1
                    ? $"Monthly on the {dayPart} at {timeStr}"
                    : $"Every {config.IntervalCount} months on the {dayPart} at {timeStr}");
                break;

            case ScheduleFrequency.Quarterly:
                var months = config.Months.Any()
                    ? string.Join(", ", config.Months.Select(m => MonthNames[m]))
                    : "Jan, Apr, Jul, Oct";
                sb.Append($"Quarterly ({months}) on day {config.DayOfMonth} at {timeStr}");
                break;

            case ScheduleFrequency.Yearly:
                var monthName = config.Months.Any() ? MonthNames[config.Months.First()] : MonthNames[config.StartDate.Month];
                sb.Append($"Yearly on {monthName} {config.DayOfMonth} at {timeStr}");
                break;

            case ScheduleFrequency.Custom:
                sb.Append("Custom schedule");
                break;
        }

        if (config.EndDate.HasValue)
        {
            sb.Append($", until {config.EndDate.Value:MMMM d, yyyy}");
        }

        if (config.MaxOccurrences.HasValue)
        {
            sb.Append($", {config.MaxOccurrences} times");
        }

        return sb.ToString();
    }

    private static string GetOrdinal(int number)
    {
        if (number <= 0) return "1st";

        var suffix = (number % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (number % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            }
        };

        return $"{number}{suffix}";
    }

    public string GetDescription(string cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return "No schedule configured";

        try
        {
            var parts = cronExpression.Split(' ');
            if (parts.Length < 6)
                return $"Custom: {cronExpression}";

            var minute = parts[1];
            var hour = parts[2];
            var dayOfMonth = parts[3];
            var month = parts[4];
            var dayOfWeek = parts[5];

            var sb = new StringBuilder();

            // Try to create a readable description
            if (minute.Contains("/"))
            {
                var interval = minute.Split('/')[1];
                sb.Append($"Every {interval} minutes");
            }
            else if (hour.Contains("/"))
            {
                var interval = hour.Split('/')[1];
                sb.Append($"Every {interval} hours");
            }
            else if (dayOfWeek != "?" && dayOfWeek != "*")
            {
                sb.Append($"Every {dayOfWeek}");
                if (int.TryParse(hour, out var h) && int.TryParse(minute, out var m))
                {
                    var time = new TimeSpan(h, m, 0);
                    sb.Append($" at {DateTime.Today.Add(time):h:mm tt}");
                }
            }
            else if (dayOfMonth == "L")
            {
                sb.Append("Last day of month");
                if (int.TryParse(hour, out var h) && int.TryParse(minute, out var m))
                {
                    var time = new TimeSpan(h, m, 0);
                    sb.Append($" at {DateTime.Today.Add(time):h:mm tt}");
                }
            }
            else if (dayOfMonth != "?" && dayOfMonth != "*")
            {
                if (month != "*")
                {
                    sb.Append($"On {month}/{dayOfMonth}");
                }
                else
                {
                    sb.Append($"Monthly on day {dayOfMonth}");
                }
                if (int.TryParse(hour, out var h) && int.TryParse(minute, out var m))
                {
                    var time = new TimeSpan(h, m, 0);
                    sb.Append($" at {DateTime.Today.Add(time):h:mm tt}");
                }
            }
            else
            {
                sb.Append("Daily");
                if (int.TryParse(hour, out var h) && int.TryParse(minute, out var m))
                {
                    var time = new TimeSpan(h, m, 0);
                    sb.Append($" at {DateTime.Today.Add(time):h:mm tt}");
                }
            }

            return sb.ToString();
        }
        catch
        {
            return $"Custom: {cronExpression}";
        }
    }

    public List<DateTime> GetNextRunTimes(ScheduleConfiguration config, int count = 5)
    {
        var results = new List<DateTime>();
        if (string.IsNullOrEmpty(config.CronExpression))
        {
            config = Convert(config);
        }

        try
        {
            // Use Quartz's CronExpression to calculate next fire times
            var cronExpr = new Quartz.CronExpression(config.CronExpression);
            var next = DateTimeOffset.Now;

            for (var i = 0; i < count; i++)
            {
                var nextFire = cronExpr.GetNextValidTimeAfter(next);
                if (nextFire.HasValue)
                {
                    results.Add(nextFire.Value.LocalDateTime);
                    next = nextFire.Value.AddSeconds(1);

                    // Stop if we've passed the end date
                    if (config.EndDate.HasValue && nextFire.Value.DateTime > config.EndDate.Value)
                        break;
                }
                else
                {
                    break;
                }
            }
        }
        catch
        {
            // If cron parsing fails, return empty list
        }

        return results;
    }

    public bool IsValidCron(string cronExpression, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            error = "Cron expression is required";
            return false;
        }

        try
        {
            var _ = new Quartz.CronExpression(cronExpression);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public ScheduleConfiguration? ParseCron(string cronExpression)
    {
        // Best-effort parsing of cron to ScheduleConfiguration
        // This won't handle all cases but covers common patterns
        if (string.IsNullOrWhiteSpace(cronExpression))
            return null;

        try
        {
            var parts = cronExpression.Split(' ');
            if (parts.Length < 6)
                return null;

            var config = new ScheduleConfiguration
            {
                CronExpression = cronExpression,
                Frequency = ScheduleFrequency.Custom
            };

            var minute = parts[1];
            var hour = parts[2];
            var dayOfMonth = parts[3];
            var month = parts[4];
            var dayOfWeek = parts[5];

            // Try to parse time
            if (int.TryParse(minute, out var m) && int.TryParse(hour, out var h))
            {
                config.StartTime = new TimeSpan(h, m, 0);
            }

            // Detect frequency from pattern
            if (minute.Contains("/"))
            {
                config.Frequency = ScheduleFrequency.Minute;
                if (int.TryParse(minute.Split('/')[1], out var interval))
                    config.IntervalCount = interval;
            }
            else if (hour.Contains("/"))
            {
                config.Frequency = ScheduleFrequency.Hourly;
                if (int.TryParse(hour.Split('/')[1], out var interval))
                    config.IntervalCount = interval;
            }
            else if (dayOfWeek != "?" && dayOfWeek != "*")
            {
                config.Frequency = ScheduleFrequency.Weekly;
                config.DaysOfWeek = ParseDaysOfWeek(dayOfWeek);
            }
            else if (dayOfMonth == "L")
            {
                config.Frequency = ScheduleFrequency.Monthly;
                config.MonthlyPattern = MonthlyDayPattern.LastDayOfMonth;
            }
            else if (month != "*" && month.Contains(","))
            {
                config.Frequency = ScheduleFrequency.Quarterly;
                config.Months = month.Split(',').Select(int.Parse).ToList();
            }
            else if (dayOfMonth != "?" && dayOfMonth != "*")
            {
                if (month != "*")
                {
                    config.Frequency = ScheduleFrequency.Yearly;
                    if (int.TryParse(month, out var mon))
                        config.Months = new List<int> { mon };
                }
                else
                {
                    config.Frequency = ScheduleFrequency.Monthly;
                }
                if (int.TryParse(dayOfMonth, out var dom))
                    config.DayOfMonth = dom;
            }
            else
            {
                config.Frequency = ScheduleFrequency.Daily;
            }

            config.Description = GetDescription(cronExpression);
            return config;
        }
        catch
        {
            return new ScheduleConfiguration
            {
                Frequency = ScheduleFrequency.Custom,
                CronExpression = cronExpression,
                Description = GetDescription(cronExpression)
            };
        }
    }

    private List<DayOfWeek> ParseDaysOfWeek(string dayOfWeek)
    {
        var days = new List<DayOfWeek>();
        var parts = dayOfWeek.Split(',');

        foreach (var part in parts)
        {
            var day = part.Trim().ToUpper().Split('#')[0].TrimEnd('L');
            var index = Array.IndexOf(DayOfWeekNames, day);
            if (index >= 0)
            {
                days.Add((DayOfWeek)index);
            }
        }

        return days;
    }
}
