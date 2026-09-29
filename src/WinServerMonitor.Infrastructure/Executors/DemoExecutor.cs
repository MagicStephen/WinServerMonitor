using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Infrastructure.Executors;

/// <summary>Simulated work - useful to try out the board without touching real systems.</summary>
public sealed class DemoExecutor : ITaskExecutor
{
    public string TaskType => "Demo";

    public string DisplayName => "Demo (simulace)";

    public string Description => "Simuluje práci: čeká zadaný čas, loguje průběh a s danou pravděpodobností selže.";

    public IReadOnlyList<TaskParameterDescriptor> Parameters =>
    [
        new("DurationSeconds", "Délka (s)", TaskParameterKind.Number, DefaultValue: "10"),
        new("FailureRate", "Pravděpodobnost chyby (%)", TaskParameterKind.Number, DefaultValue: "0"),
    ];

    public async Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var duration = Math.Max(0, context.GetInt("DurationSeconds", 10));
        var failureRate = Math.Clamp(context.GetInt("FailureRate", 0), 0, 100);
        const int steps = 5;

        for (var step = 1; step <= steps; step++)
        {
            await Task.Delay(TimeSpan.FromSeconds(duration / (double)steps), cancellationToken);
            context.Log.Info($"Krok {step}/{steps} dokončen");
        }

        if (Random.Shared.Next(100) < failureRate)
        {
            throw new InvalidOperationException("Simulovaná chyba (FailureRate).");
        }

        return TaskExecutionResult.Ok($"Zpracováno {Random.Shared.Next(100, 5000)} záznamů");
    }
}
