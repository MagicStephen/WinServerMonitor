using System.Diagnostics;
using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Infrastructure.Executors;

internal static class ProcessRunner
{
    /// <summary>Starts a process, streams stdout/stderr into the run log and returns its exit code.</summary>
    public static async Task<int> RunAsync(ProcessStartInfo startInfo, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                context.Log.Info(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                context.Log.Warning(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Proces '{startInfo.FileName}' se nepodařilo spustit.");
        }

        context.Log.Debug($"PID {process.Id}: {startInfo.FileName}");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                context.Log.Warning($"Proces {process.Id} byl ukončen.");
            }
            catch (InvalidOperationException)
            {
                // already exited
            }

            throw;
        }

        // Make sure the asynchronous output handlers have drained.
        process.WaitForExit();
        return process.ExitCode;
    }

    public static bool IsSuccess(int exitCode, string? successCodes)
    {
        if (string.IsNullOrWhiteSpace(successCodes))
        {
            return exitCode == 0;
        }

        return successCodes
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(c => int.TryParse(c, out var code) && code == exitCode);
    }
}
