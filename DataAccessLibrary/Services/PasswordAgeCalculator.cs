namespace DataAccessLibrary.Services;

/// <summary>
/// Pure helpers for converting AD pwdLastSet FILETIME strings into
/// DateTime / age-in-days. Used by the /admin/password-policy admin UI
/// when the Objects.PasswordLastSet column is unavailable but the raw
/// value is in ObjectAttributes (legacy syncs).
/// </summary>
public static class PasswordAgeCalculator
{
    /// <summary>
    /// Converts a Windows FILETIME string (100-ns intervals since 1601-01-01 UTC)
    /// into a DateTime. Returns null for null/empty/zero/non-numeric inputs.
    /// pwdLastSet = "0" in AD means "must change at next logon" — treated as null
    /// here so callers show "Unknown" rather than "Year 1601".
    /// </summary>
    public static DateTime? FromFileTime(string? fileTime)
    {
        if (string.IsNullOrWhiteSpace(fileTime)) return null;
        if (!long.TryParse(fileTime, out var ticks)) return null;
        if (ticks <= 0) return null;
        try
        {
            return DateTime.FromFileTimeUtc(ticks);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Days between <paramref name="passwordSet"/> and <paramref name="now"/>,
    /// or null if the password-set date is unknown. Negative values are clamped
    /// to 0 (clock skew safety).
    /// </summary>
    public static int? AgeInDays(DateTime? passwordSet, DateTime now)
    {
        if (!passwordSet.HasValue) return null;
        var days = (now - passwordSet.Value).TotalDays;
        if (days < 0) return 0;
        return (int)Math.Floor(days);
    }

    /// <summary>
    /// Convenience overload that takes the raw FILETIME string.
    /// </summary>
    public static int? AgeInDaysFromFileTime(string? fileTime, DateTime now)
        => AgeInDays(FromFileTime(fileTime), now);
}
