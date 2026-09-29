namespace WinServerMonitor.Core.Execution;

/// <summary>Thrown by executors when a task is misconfigured (missing parameter, unknown connection, ...).</summary>
public sealed class TaskConfigurationException(string message) : Exception(message);
