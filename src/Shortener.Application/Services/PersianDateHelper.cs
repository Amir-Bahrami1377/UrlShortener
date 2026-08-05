using System.Globalization;

namespace Shortener.Application.Services;

/// <summary>§M6.6 — converts UTC (the only thing ever stored, per Appendix C rule 5) to local Tehran
/// time and then to the Persian calendar for display, and parses the reverse for report filters.</summary>
public static class PersianDateHelper
{
    private static readonly PersianCalendar Calendar = new();
    private static readonly TimeZoneInfo TehranTimeZone = ResolveTehranTimeZone();

    public static string ToPersianDate(DateTime utc)
    {
        var local = ToLocal(utc);
        return $"{Calendar.GetYear(local):0000}/{Calendar.GetMonth(local):00}/{Calendar.GetDayOfMonth(local):00}";
    }

    /// <summary>Current Tehran-local time — used by the M7.6 job scheduler to evaluate the nightly window.</summary>
    public static DateTime Now() => ToLocal(DateTime.UtcNow);

    public static string ToPersianDateTime(DateTime utc)
    {
        var local = ToLocal(utc);
        return $"{ToPersianDate(utc)} {local:HH:mm}";
    }

    /// <summary>Accepts "yyyy/MM/dd" or "yyyy-MM-dd" in the Persian calendar; returns UTC midnight of that day, local time.</summary>
    public static DateTime? ParsePersianDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(['/', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month) || !int.TryParse(parts[2], out var day))
        {
            return null;
        }

        try
        {
            var local = Calendar.ToDateTime(year, month, day, 0, 0, 0, 0);
            return ConvertToUtc(local);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TehranTimeZone);

    /// <summary>Treats <paramref name="local"/> as unspecified-kind Tehran wall-clock time and converts it to UTC.</summary>
    public static DateTime ConvertToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TehranTimeZone);

    private static TimeZoneInfo ResolveTehranTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"); // Windows id
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); // IANA id (Linux containers)
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}

public static class PersianDateExtensions
{
    public static string ToPersianDate(this DateTime utc) => PersianDateHelper.ToPersianDate(utc);

    public static string ToPersianDateTime(this DateTime utc) => PersianDateHelper.ToPersianDateTime(utc);

    public static string ToPersianDate(this DateTime? utc) => utc is null ? "—" : PersianDateHelper.ToPersianDate(utc.Value);

    public static string ToPersianDateTime(this DateTime? utc) => utc is null ? "—" : PersianDateHelper.ToPersianDateTime(utc.Value);
}
