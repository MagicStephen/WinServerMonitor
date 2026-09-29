using System.Diagnostics;
using System.Text;
using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Infrastructure.Executors;

/// <summary>Runs an inline PowerShell script through powershell.exe (Windows PowerShell) or pwsh.</summary>
public sealed class PowerShellExecutor : ITaskExecutor
{
    public string TaskType => "PowerShell";

    public string DisplayName => "PowerShell skript";

    public string Description => "Spustí PowerShell skript. Write-Output jde do logu jako Info, chybový výstup jako Warning.";

    public IReadOnlyList<TaskParameterDescriptor> Parameters =>
    [
        new("Script", "Skript", TaskParameterKind.MultilineText, Required: true),
        new("Engine", "Interpret", TaskParameterKind.Choice, DefaultValue: OperatingSystem.IsWindows() ? "powershell" : "pwsh",
            Choices: ["powershell", "pwsh"], Help: "powershell = Windows PowerShell 5.1, pwsh = PowerShell 7+"),
        new("WorkingDirectory", "Pracovní adresář"),
    ];

    public async Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var script = context.GetRequiredParameter("Script");
        var engine = context.GetParameter("Engine") ?? (OperatingSystem.IsWindows() ? "powershell" : "pwsh");

        // Stop on the first error and propagate $LASTEXITCODE so failures fail the task.
        // UTF-8 output keeps diacritics readable (Windows PowerShell defaults to the OEM code page).
        var wrapped = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n$ErrorActionPreference = 'Stop'\n"
                      + script + "\nif ($LASTEXITCODE) { exit $LASTEXITCODE }";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));

        var startInfo = new ProcessStartInfo(engine)
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded },
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (context.GetParameter("WorkingDirectory") is { } workingDirectory)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        context.Log.Info($"Spouštím {engine}");
        var exitCode = await ProcessRunner.RunAsync(startInfo, context, cancellationToken);
        var summary = $"Návratový kód {exitCode}";
        return exitCode == 0 ? TaskExecutionResult.Ok(summary) : TaskExecutionResult.Fail(summary);
    }
}
