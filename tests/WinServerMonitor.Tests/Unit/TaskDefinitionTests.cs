using WinServerMonitor.Core.Domain;

namespace WinServerMonitor.Tests.Unit;

public class TaskDefinitionTests
{
    [Fact]
    public void Parameters_RoundTrip_AndEmptyValuesAreDropped()
    {
        var definition = new TaskDefinition();
        definition.SetParameters(new Dictionary<string, string> { ["Procedure"] = "public.x", ["Empty"] = " " });

        var parameters = definition.GetParameters();

        Assert.Equal("public.x", Assert.Single(parameters).Value);
    }

    [Theory]
    [InlineData(TaskRunStatus.Scheduled, true, false)]
    [InlineData(TaskRunStatus.Running, true, false)]
    [InlineData(TaskRunStatus.Failed, false, true)]
    [InlineData(TaskRunStatus.Cancelled, false, true)]
    public void StatusClassification(TaskRunStatus status, bool active, bool finished)
    {
        Assert.Equal(active, status.IsActive());
        Assert.Equal(finished, status.IsFinished());
    }
}
