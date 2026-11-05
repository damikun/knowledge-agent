using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropJobTablesAddIngestionProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "background_jobs");

            migrationBuilder.DropTable(
                name: "job_schedules");

            migrationBuilder.AddColumn<int>(
                name: "embedded_chunks",
                table: "sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "job_run_id",
                table: "sources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stage",
                table: "sources",
                type: "text",
                nullable: true);

            // Sources ingested before progress was tracked had all their chunks embedded.
            migrationBuilder.Sql("UPDATE sources SET embedded_chunks = chunk_count WHERE status = 'Ready';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embedded_chunks",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "job_run_id",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "stage",
                table: "sources");

            migrationBuilder.CreateTable(
                name: "background_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    rerun_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_background_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "job_schedules",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cron = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cron_override = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    enabled_override = table.Column<bool>(type: "boolean", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_schedules", x => x.name);
                });

            migrationBuilder.CreateIndex(
                name: "ix_background_jobs_completed_at",
                table: "background_jobs",
                column: "completed_at");

            migrationBuilder.CreateIndex(
                name: "ix_background_jobs_kind_key",
                table: "background_jobs",
                columns: new[] { "kind", "key" },
                unique: true,
                filter: "status IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "ix_background_jobs_kind_status_run_at",
                table: "background_jobs",
                columns: new[] { "kind", "status", "run_at" });
        }
    }
}
