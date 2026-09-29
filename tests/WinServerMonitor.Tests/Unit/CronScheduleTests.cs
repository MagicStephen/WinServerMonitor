using WinServerMonitor.Infrastructure.Scheduling;

namespace WinServerMonitor.Tests.Unit;

public class CronScheduleTests
{
    private static readonly DateTime From = new(2026, 1, 15, 10, 2, 0, DateTimeKind.Utc);

    [Fact]
    public void FiveFieldExpression_HasMinutePrecision() =>
        Assert.Equal(new DateTime(2026, 1, 15, 10, 5, 0, DateTimeKind.Utc), CronSchedule.GetNextOccurrence("*/5 * * * *", "UTC", From));

    [Fact]
    public void SixFieldExpression_IncludesSeconds() =>
        Assert.Equal(new DateTime(2026, 1, 15, 10, 2, 30, DateTimeKind.Utc), CronSchedule.GetNextOccurrence("30 * * * * *", "UTC", From));

    [Theory]
    [InlineData(1, 5)] // winter: CET = UTC+1
    [InlineData(7, 4)] // summer: CEST = UTC+2
    public void TimeZone_IsRespected(int month, int expectedUtcHour)
    {
        var next = CronSchedule.GetNextOccurrence("0 6 * * *", "Europe/Prague", new DateTime(2026, month, 15, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, month, 15, expectedUtcHour, 0, 0, DateTimeKind.Utc), next);
    }

    [Fact]
    public void NextOccurrences_AreConsecutive()
    {
        var next = CronSchedule.GetNextOccurrences("0 * * * *", "UTC", From, 3);

        Assert.Equal([11, 12, 13], next.Select(d => d.Hour));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("30 7 * * 1-5", true)]
    [InlineData("not a cron", false)]
    [InlineData("61 * * * *", false)]
    public void Validation(string? expression, bool valid)
    {
        Assert.Equal(valid, CronSchedule.TryValidate(expression, out var error));
        Assert.Equal(valid, error is null);
    }
}
