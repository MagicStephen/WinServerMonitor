using System.Diagnostics;
using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Infrastructure.Executors;

/// <summary>Runs an executable or batch file.</summary>
public sealed class ProcessExecutor : ITaskExecutor
{
    public string TaskType => "Process";

    public string DisplayName => "Program / příkaz";

    public string Description => "Spustí program (exe, bat, cmd). Výstup programu se zapisuje do logu.";

    public IReadOnlyList<TaskParameterDescriptor> Parameters =>
    [
        new("FileName", "Program", Required: true, Help: @"Např. C:\Tools\export.exe nebo cmd.exe"),
        new("Arguments", "Argumenty"),
        new("WorkingDirectory", "Pracovní adresář"),
        new("SuccessExitCodes", "Úspěšné návratové kódy", DefaultValue: "0", Help: "Seznam oddělený čárkou."),
    ];

    public async Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(context.GetRequiredParameter("FileName"), context.GetParameter("Arguments") ?? string.Empty);
        if (context.GetParameter("WorkingDirectory") is { } workingDirectory)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        context.Log.Info($"Spouštím: {startInfo.FileName} {startInfo.Arguments}");
        var exitCode = await ProcessRunner.RunAsync(startInfo, context, cancellationToken);
        var summary = $"Návratový kód {exitCode}";
        return ProcessRunner.IsSuccess(exitCode, context.GetParameter("SuccessExitCodes"))
            ? TaskExecutionResult.Ok(summary)
            : TaskExecutionResult.Fail(summary);
    }
}
