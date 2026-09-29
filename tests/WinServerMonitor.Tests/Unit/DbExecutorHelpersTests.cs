using WinServerMonitor.Core.Execution;
using WinServerMonitor.Infrastructure.Executors;

namespace WinServerMonitor.Tests.Unit;

public class DbExecutorHelpersTests
{
    [Fact]
    public void PostgresProcedure_UsesCallWithNamedArguments() =>
        Assert.Equal("CALL public.daily_close(p_date => @p_date, p_force => @p_force)",
            DbProcedureExecutor.BuildPostgresCommand("public.daily_close", "Procedure", ["p_date", "p_force"]));

    [Fact]
    public void PostgresFunction_UsesSelectFrom() =>
        Assert.Equal("SELECT * FROM stats()", DbProcedureExecutor.BuildPostgresCommand("stats", "Function", []));

    [Theory]
    [InlineData("daily_close", true)]
    [InlineData("public.daily_close", true)]
    [InlineData("dbo.[My Proc]", true)]
    [InlineData("\"Finance\".\"DailyClose\"", true)]
    [InlineData("x; DROP TABLE users", false)]
    [InlineData("proc()", false)]
    [InlineData("a..b", false)]
    [InlineData("\"a\"\"b\"", false)]
    public void ObjectNameValidation(string name, bool valid) =>
        Assert.Equal(valid, DbProcedureExecutor.IsValidObjectName(name));

    [Fact]
    public void JsonParameters_AreConvertedToClrTypes()
    {
        var parameters = DbExecutorBase.ParseJsonParameters(
            """{"@text": "a", "int": 5, "dec": 1.5, "flag": true, ":nothing": null}""").ToDictionary();

        Assert.Equal("a", parameters["text"]);
        Assert.Equal(5L, parameters["int"]);
        Assert.Equal(1.5m, parameters["dec"]);
        Assert.Equal(true, parameters["flag"]);
        Assert.Equal(DBNull.Value, parameters["nothing"]);
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("{not json")]
    [InlineData("""{"bad name": 1}""")]
    public void InvalidJsonParameters_AreConfigurationErrors(string json) =>
        Assert.Throws<TaskConfigurationException>(() => DbExecutorBase.ParseJsonParameters(json));

    [Fact]
    public void SqlServerScript_IsSplitOnGo()
    {
        var batches = DbScriptExecutor.SplitBatches("SELECT 1\nGO\n\nSELECT 2\n  go  \nSELECT 'GOOD'");

        Assert.Equal(["SELECT 1", "SELECT 2", "SELECT 'GOOD'"], batches);
    }
}
