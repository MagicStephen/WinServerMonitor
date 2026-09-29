using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WinServerMonitor.Core.Domain;

namespace WinServerMonitor.Infrastructure.Persistence;

public sealed class DatabaseInitializer(
    IDbContextFactory<MonitorDbContext> dbFactory,
    IOptions<MonitorOptions> options,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);

        if (options.Value.SeedDemoTasks && !await db.TaskDefinitions.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Seeding demo task definitions");
            db.TaskDefinitions.AddRange(CreateDemoTasks());
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static IEnumerable<TaskDefinition> CreateDemoTasks()
    {
        TaskDefinition Demo(string name, string domain, string? cron, int duration, int failureRate, string description)
        {
            var definition = new TaskDefinition
            {
                Name = name,
                Domain = domain,
                Description = description,
                TaskType = "Demo",
                CronExpression = cron,
                TimeoutSeconds = 120,
                MaxRetries = failureRate > 0 ? 1 : 0,
                RetryDelaySeconds = 20,
            };
            definition.SetParameters(new Dictionary<string, string>
            {
                ["DurationSeconds"] = duration.ToString(),
                ["FailureRate"] = failureRate.ToString(),
            });
            return definition;
        }

        yield return Demo("Import objednávek", "Sklad", "*/2 * * * *", 20, 10, "Ukázkový task - simuluje import dat.");
        yield return Demo("Přepočet zásob", "Sklad", "*/5 * * * *", 45, 0, "Ukázkový task - dlouhý výpočet.");
        yield return Demo("Uzávěrka dne", "Finance", "0 22 * * *", 30, 0, "Ukázkový task - spouští se jednou denně.");
        yield return Demo("Synchronizace kurzů", "Finance", "*/3 * * * *", 10, 30, "Ukázkový task - občas selže, má 1 retry.");
        yield return Demo("Ruční údržba", "IT", null, 15, 0, "Ukázkový task bez plánu - pouze ruční spuštění.");
    }
}
