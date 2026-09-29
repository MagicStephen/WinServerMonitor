using WinServerMonitor.Core.Domain;
using WinServerMonitor.Core.Execution;
using WinServerMonitor.Infrastructure.Executors;
using WinServerMonitor.Tests.Support;

namespace WinServerMonitor.Tests.Unit;

public class ProcessExecutorTests
{
    private static (TaskExecutionContext, CollectingLogger) Context(Dictionary<string, string> parameters)
    {
        var log = new CollectingLogger();
        return (new TaskExecutionContext(new TaskDefinition(), new TaskRun(), parameters, log), log);
    }

    private static Dictionary<string, string> Shell(string script, string? successCodes = null)
    {
        var parameters = OperatingSystem.IsWindows()
            ? new Dictionary<string, string> { ["FileName"] = "cmd.exe", ["Arguments"] = "/c " + script.Replace(";", " &") }
            : new Dictionary<string, string> { ["FileName"] = "/bin/sh", ["Arguments"] = $"-c \"{script}\"" };
        if (successCodes is not null)
        {
            parameters["SuccessExitCodes"] = successCodes;
        }

        return parameters;
    }

    [Fact]
    public async Task Output_IsLogged_AndExitCodeDecidesResult()
    {
        var (context, log) = Context(Shell("echo hello; exit 3", successCodes: "0,3"));

        var result = await new ProcessExecutor().ExecuteAsync(context, default);

        Assert.True(result.Success);
        Assert.Equal("Návratový kód 3", result.Summary);
        Assert.Contains(log.Entries, e => e.Message.Trim() == "hello");
    }

    [Fact]
    public async Task NonZeroExitCode_Fails()
    {
        var (context, _) = Context(Shell("exit 2"));

        var result = await new ProcessExecutor().ExecuteAsync(context, default);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task MissingFileName_IsConfigurationError()
    {
        var (context, _) = Context([]);

        await Assert.ThrowsAsync<TaskConfigurationException>(() => new ProcessExecutor().ExecuteAsync(context, default));
    }
}
