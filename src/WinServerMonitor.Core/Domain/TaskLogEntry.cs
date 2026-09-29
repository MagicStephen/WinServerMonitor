namespace WinServerMonitor.Core.Domain;

public class TaskLogEntry
{
    public long Id { get; set; }

    public long TaskRunId { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public TaskLogLevel Level { get; set; } = TaskLogLevel.Info;

    public string Message { get; set; } = string.Empty;
}
