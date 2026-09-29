using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WinServerMonitor.Core.Events;
using WinServerMonitor.Core.Execution;
using WinServerMonitor.Core.Metrics;
using WinServerMonitor.Infrastructure.Events;
using WinServerMonitor.Infrastructure.Execution;
using WinServerMonitor.Infrastructure.Executors;
using WinServerMonitor.Infrastructure.Metrics;
using WinServerMonitor.Infrastructure.Persistence;
using WinServerMonitor.Infrastructure.Scheduling;
using WinServerMonitor.Infrastructure.Services;

namespace WinServerMonitor.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "MonitorDb";

    /// <summary>Registers persistence, scheduler, workers, executors and metrics collection.</summary>
    public static IServiceCollection AddWinServerMonitor(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MonitorOptions>(configuration.GetSection(MonitorOptions.SectionName));

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        services.AddDbContextFactory<MonitorDbContext>(o => o.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MonitorDbContext.Schema)));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMonitorEvents, MonitorEvents>();
        services.AddSingleton<DatabaseInitializer>();

        // Execution
        services.AddSingleton<TaskQueue>();
        services.AddSingleton<RunningTaskRegistry>();
        services.AddSingleton<TaskExecutorRegistry>();
        services.AddSingleton<TaskRunner>();
        services.AddSingleton<TaskSchedulingEngine>();
        services.AddHostedService<TaskSchedulerService>();
        services.AddHostedService<TaskWorkerService>();

        // Task types - add new executors here.
        services.AddHttpClient(HttpRequestExecutor.HttpClientName);
        services.AddSingleton<ITaskExecutor, DbProcedureExecutor>();
        services.AddSingleton<ITaskExecutor, DbScriptExecutor>();
        services.AddSingleton<ITaskExecutor, PowerShellExecutor>();
        services.AddSingleton<ITaskExecutor, ProcessExecutor>();
        services.AddSingleton<ITaskExecutor, HttpRequestExecutor>();
        services.AddSingleton<ITaskExecutor, DemoExecutor>();

        // Application services used by the UI
        services.AddSingleton<TaskDefinitionService>();
        services.AddSingleton<TaskRunService>();

        // Server metrics
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IServerMetricsProvider, WindowsMetricsProvider>();
        }
        else
        {
            services.AddSingleton<IServerMetricsProvider, LinuxMetricsProvider>();
        }

        services.AddSingleton<IMetricsHistory, MetricsHistory>();
        services.AddHostedService<MetricsCollectorService>();

        return services;
    }
}
