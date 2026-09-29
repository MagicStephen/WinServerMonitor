using Cronos;

namespace WinServerMonitor.Infrastructure.Scheduling;

public static class CronSchedule
{
    /// <summary>Parses a 5-field (minute precision) or 6-field (with seconds) cron expression.</summary>
    public static CronExpression Parse(string expression)
    {
        var trimmed = expression.Trim();
        var fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var format = fields == 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        return CronExpression.Parse(trimmed, format);
    }

    public static bool TryValidate(string? expression, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        try
        {
            Parse(expression);
            return true;
        }
        catch (CronFormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        // .NET resolves both Windows ("Central Europe Standard Time") and IANA ("Europe/Prague") ids.
        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) ? zone : TimeZoneInfo.Local;
    }

    /// <summary>Next occurrence strictly after <paramref name="fromUtc"/>, or null when there is none.</summary>
    public static DateTime? GetNextOccurrence(string expression, string? timeZoneId, DateTime fromUtc) =>
        Parse(expression).GetNextOccurrence(
            DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
            ResolveTimeZone(timeZoneId));

    public static IReadOnlyList<DateTime> GetNextOccurrences(string expression, string? timeZoneId, DateTime fromUtc, int count)
    {
        var result = new List<DateTime>(count);
        var cron = Parse(expression);
        var zone = ResolveTimeZone(timeZoneId);
        var cursor = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        while (result.Count < count && cron.GetNextOccurrence(cursor, zone) is { } next)
        {
            result.Add(next);
            cursor = next;
        }

        return result;
    }
}
