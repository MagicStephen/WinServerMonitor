using WinServerMonitor.Core.Domain;

namespace WinServerMonitor.Core.Execution;

/// <summary>Writes entries into the log of the current run (persisted and streamed to the UI).</summary>
public interface ITaskRunLogger
{
    void Write(TaskLogLevel level, string message);

    void Debug(string message) => Write(TaskLogLevel.Debug, message);

    void Info(string message) => Write(TaskLogLevel.Info, message);

    void Warning(string message) => Write(TaskLogLevel.Warning, message);

    void Error(string message) => Write(TaskLogLevel.Error, message);
}
