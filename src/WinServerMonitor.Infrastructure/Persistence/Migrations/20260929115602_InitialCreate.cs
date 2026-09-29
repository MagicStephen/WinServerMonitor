using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WinServerMonitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "monitor");

            migrationBuilder.CreateTable(
                name: "TaskDefinitions",
                schema: "monitor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Domain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TaskType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParametersJson = table.Column<string>(type: "text", nullable: false),
                    CronExpression = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxRetries = table.Column<int>(type: "integer", nullable: false),
                    RetryDelaySeconds = table.Column<int>(type: "integer", nullable: false),
                    AllowConcurrentRuns = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskRuns",
                schema: "monitor",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ParentRunId = table.Column<long>(type: "bigint", nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultSummary = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    MachineName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskRuns_TaskDefinitions_TaskDefinitionId",
                        column: x => x.TaskDefinitionId,
                        principalSchema: "monitor",
                        principalTable: "TaskDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskRuns_TaskRuns_ParentRunId",
                        column: x => x.ParentRunId,
                        principalSchema: "monitor",
                        principalTable: "TaskRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TaskLogs",
                schema: "monitor",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskRunId = table.Column<long>(type: "bigint", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskLogs_TaskRuns_TaskRunId",
                        column: x => x.TaskRunId,
                        principalSchema: "monitor",
                        principalTable: "TaskRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskDefinitions_Domain",
                schema: "monitor",
                table: "TaskDefinitions",
                column: "Domain");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDefinitions_Name",
                schema: "monitor",
                table: "TaskDefinitions",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLogs_TaskRunId_Id",
                schema: "monitor",
                table: "TaskLogs",
                columns: new[] { "TaskRunId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRuns_FinishedAtUtc",
                schema: "monitor",
                table: "TaskRuns",
                column: "FinishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TaskRuns_ParentRunId",
                schema: "monitor",
                table: "TaskRuns",
                column: "ParentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskRuns_Status_ScheduledForUtc",
                schema: "monitor",
                table: "TaskRuns",
                columns: new[] { "Status", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRuns_TaskDefinitionId_Status",
                schema: "monitor",
                table: "TaskRuns",
                columns: new[] { "TaskDefinitionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskLogs",
                schema: "monitor");

            migrationBuilder.DropTable(
                name: "TaskRuns",
                schema: "monitor");

            migrationBuilder.DropTable(
                name: "TaskDefinitions",
                schema: "monitor");
        }
    }
}
