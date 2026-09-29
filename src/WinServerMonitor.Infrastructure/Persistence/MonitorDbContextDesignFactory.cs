using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WinServerMonitor.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef migrations add</c>.</summary>
public sealed class MonitorDbContextDesignFactory : IDesignTimeDbContextFactory<MonitorDbContext>
{
    public MonitorDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MonitorDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=winservermonitor;Username=postgres",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MonitorDbContext.Schema))
            .Options);
}
