namespace WinServerMonitor.Core.Execution;

public sealed record TaskExecutionResult(bool Success, string? Summary = null)
{
    public static TaskExecutionResult Ok(string? summary = null) => new(true, summary);

    public static TaskExecutionResult Fail(string summary) => new(false, summary);
}
