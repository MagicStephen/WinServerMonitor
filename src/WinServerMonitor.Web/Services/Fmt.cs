using System.Globalization;
using WinServerMonitor.Core.Domain;

namespace WinServerMonitor.Web.Services;

/// <summary>Formatting helpers for the UI (Czech culture, server local time).</summary>
public static class Fmt
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("cs-CZ");

    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (Math.Abs(bytes) >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return bytes.ToString(unit == 0 ? "0" : bytes < 10 ? "0.0" : "0", Culture) + " " + units[unit];
    }

    public static string Rate(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    public static string Number(double value, string format = "0") => value.ToString(format, Culture);

    public static string Percent(double value) => value.ToString("0", Culture) + " %";

    public static string Time(DateTime? utc) => utc is { } v ? v.ToLocalTime().ToString("HH:mm:ss", Culture) : "–";

    public static string DateTime(DateTime? utc)
    {
        if (utc is not { } v)
        {
            return "–";
        }

        var local = v.ToLocalTime();
        return local.Date == System.DateTime.Today
            ? local.ToString("HH:mm:ss", Culture)
            : local.ToString("d.M. HH:mm", Culture);
    }

    public static string FullDateTime(DateTime? utc) =>
        utc is { } v ? v.ToLocalTime().ToString("d. M. yyyy HH:mm:ss", Culture) : "–";

    public static string Duration(TimeSpan? duration)
    {
        if (duration is not { } d)
        {
            return "–";
        }

        if (d.TotalSeconds < 1)
        {
            return $"{d.TotalMilliseconds:0} ms";
        }

        if (d.TotalMinutes < 1)
        {
            return $"{d.TotalSeconds:0.#} s".Replace('.', ',');
        }

        if (d.TotalHours < 1)
        {
            return $"{(int)d.TotalMinutes} min {d.Seconds} s";
        }

        return d.TotalDays < 1 ? $"{(int)d.TotalHours} h {d.Minutes} min" : $"{(int)d.TotalDays} d {d.Hours} h";
    }

    /// <summary>"za 3 min" / "před 5 s".</summary>
    public static string Relative(DateTime utc)
    {
        var diff = utc - System.DateTime.UtcNow;
        var text = Duration(diff.Duration());
        return diff > TimeSpan.Zero ? "za " + text : "před " + text;
    }

    public static string StatusLabel(TaskRunStatus status) => status switch
    {
        TaskRunStatus.Scheduled => "Naplánováno",
        TaskRunStatus.Queued => "Ve frontě",
        TaskRunStatus.Running => "Běží",
        TaskRunStatus.Succeeded => "Dokončeno",
        TaskRunStatus.Failed => "Selhalo",
        TaskRunStatus.Cancelled => "Zrušeno",
        _ => status.ToString(),
    };

    public static string StatusIcon(TaskRunStatus status) => status switch
    {
        TaskRunStatus.Scheduled => "◷",
        TaskRunStatus.Queued => "⋯",
        TaskRunStatus.Running => "▶",
        TaskRunStatus.Succeeded => "✓",
        TaskRunStatus.Failed => "✕",
        TaskRunStatus.Cancelled => "⊘",
        _ => "?",
    };

    public static string StatusCss(TaskRunStatus status) => "status-" + status.ToString().ToLowerInvariant();

    public static string TriggerLabel(TaskTrigger trigger) => trigger switch
    {
        TaskTrigger.Schedule => "Plán",
        TaskTrigger.Manual => "Ručně",
        TaskTrigger.Rerun => "Znovu spuštěno",
        TaskTrigger.Retry => "Automatické opakování",
        _ => trigger.ToString(),
    };
}
