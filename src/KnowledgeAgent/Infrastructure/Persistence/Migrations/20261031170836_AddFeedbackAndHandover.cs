using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackAndHandover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_handovers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    widget_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    token = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    assignee = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    slack_channel_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    slack_thread_ts = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email_message_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    emailed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_handovers", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_handovers_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reasons = table.Column<string>(type: "jsonb", nullable: false),
                    author = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    author_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    widget_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trace_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    score_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback", x => x.id);
                    table.ForeignKey(
                        name: "fk_feedback_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "handover_inbound",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_handover_inbound", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    secrets_json = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_integration_settings", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_handovers_conversation_id_status",
                table: "conversation_handovers",
                columns: new[] { "conversation_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_handovers_slack_channel_id_slack_thread_ts",
                table: "conversation_handovers",
                columns: new[] { "slack_channel_id", "slack_thread_ts" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_handovers_token",
                table: "conversation_handovers",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feedback_conversation_id_message_id_kind_author_key",
                table: "feedback",
                columns: new[] { "conversation_id", "message_id", "kind", "author_key" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_created_at",
                table: "feedback",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_widget_id_created_at",
                table: "feedback",
                columns: new[] { "widget_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_handover_inbound_received_at",
                table: "handover_inbound",
                column: "received_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_handovers");

            migrationBuilder.DropTable(
                name: "feedback");

            migrationBuilder.DropTable(
                name: "handover_inbound");

            migrationBuilder.DropTable(
                name: "integration_settings");
        }
    }
}
