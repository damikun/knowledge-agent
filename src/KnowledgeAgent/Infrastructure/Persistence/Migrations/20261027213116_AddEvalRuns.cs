using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvalRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eval_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dataset = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dataset_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    judge = table.Column<bool>(type: "boolean", nullable: false),
                    total_items = table.Column<int>(type: "integer", nullable: false),
                    completed_items = table.Column<int>(type: "integer", nullable: false),
                    failed_items = table.Column<int>(type: "integer", nullable: false),
                    chat_model = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    embedding_model = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    request_json = table.Column<string>(type: "jsonb", nullable: false),
                    config_json = table.Column<string>(type: "jsonb", nullable: true),
                    metrics_json = table.Column<string>(type: "jsonb", nullable: true),
                    by_tag_json = table.Column<string>(type: "jsonb", nullable: true),
                    gate_json = table.Column<string>(type: "jsonb", nullable: true),
                    items_json = table.Column<string>(type: "jsonb", nullable: true),
                    warnings_json = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    langfuse_experiment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    langfuse_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eval_runs_created_at",
                table: "eval_runs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_eval_runs_dataset_created_at",
                table: "eval_runs",
                columns: new[] { "dataset", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_eval_runs_status",
                table: "eval_runs",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eval_runs");
        }
    }
}
