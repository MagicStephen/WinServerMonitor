using WinServerMonitor.Core.Domain;

namespace WinServerMonitor.Core.Execution;

public sealed class TaskExecutionContext(
    TaskDefinition definition,
    TaskRun run,
    IReadOnlyDictionary<string, string> parameters,
    ITaskRunLogger logger)
{
    public TaskDefinition Definition { get; } = definition;

    public TaskRun Run { get; } = run;

    public IReadOnlyDictionary<string, string> Parameters { get; } = parameters;

    public ITaskRunLogger Log { get; } = logger;

    public string? GetParameter(string name) =>
        Parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public string GetRequiredParameter(string name) =>
        GetParameter(name) ?? throw new TaskConfigurationException($"Parameter '{name}' is required.");

    public int GetInt(string name, int defaultValue) =>
        int.TryParse(GetParameter(name), out var value) ? value : defaultValue;

    public bool GetBool(string name, bool defaultValue = false) =>
        bool.TryParse(GetParameter(name), out var value) ? value : defaultValue;
}
