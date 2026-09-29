using Microsoft.EntityFrameworkCore;
using WinServerMonitor.Core.Domain;

namespace WinServerMonitor.Infrastructure.Persistence;

public class MonitorDbContext(DbContextOptions<MonitorDbContext> options) : DbContext(options)
{
    /// <summary>All tables live in their own schema so the application can share an existing database.</summary>
    public const string Schema = "monitor";

    public DbSet<TaskDefinition> TaskDefinitions => Set<TaskDefinition>();

    public DbSet<TaskRun> TaskRuns => Set<TaskRun>();

    public DbSet<TaskLogEntry> TaskLogs => Set<TaskLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<TaskDefinition>(e =>
        {
            e.ToTable("TaskDefinitions");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Domain).HasMaxLength(100).IsRequired();
            e.Property(x => x.TaskType).HasMaxLength(100).IsRequired();
            e.Property(x => x.CronExpression).HasMaxLength(100);
            e.Property(x => x.TimeZoneId).HasMaxLength(100);
            e.HasIndex(x => x.Domain);
            e.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<TaskRun>(e =>
        {
            e.ToTable("TaskRuns");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RequestedBy).HasMaxLength(200);
            e.Property(x => x.MachineName).HasMaxLength(100);
            e.HasOne(x => x.TaskDefinition)
                .WithMany(x => x.Runs)
                .HasForeignKey(x => x.TaskDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TaskRun>()
                .WithMany()
                .HasForeignKey(x => x.ParentRunId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Logs)
                .WithOne()
                .HasForeignKey(x => x.TaskRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Status, x.ScheduledForUtc });
            e.HasIndex(x => new { x.TaskDefinitionId, x.Status });
            e.HasIndex(x => x.FinishedAtUtc);
        });

        modelBuilder.Entity<TaskLogEntry>(e =>
        {
            e.ToTable("TaskLogs");
            e.Property(x => x.Level).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TaskRunId, x.Id });
        });
    }
}
