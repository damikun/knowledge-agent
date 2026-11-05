using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentsAndConversationTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "agent_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "conversations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Portal");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "conversations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Ai");

            migrationBuilder.AddColumn<string>(
                name: "trace_id",
                table: "conversation_turns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    instructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    language = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    profile_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    folder_ids = table.Column<string>(type: "jsonb", nullable: false),
                    tools = table.Column<string>(type: "jsonb", nullable: false),
                    allow_general_knowledge = table.Column<bool>(type: "boolean", nullable: false),
                    is_built_in = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    author = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    author_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    citations_json = table.Column<string>(type: "jsonb", nullable: false),
                    turn_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agents_key",
                table: "agents",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_conversation_id_sequence",
                table: "conversation_messages",
                columns: new[] { "conversation_id", "sequence" },
                unique: true);

            // Every existing turn becomes its question and its answer on the timeline; a failed turn shows its error.
            migrationBuilder.Sql("""
                INSERT INTO conversation_messages (id, conversation_id, sequence, author, author_name, text, citations_json, turn_id, created_at)
                SELECT gen_random_uuid(), t.conversation_id, row_number() OVER (PARTITION BY t.conversation_id ORDER BY t.sequence) * 2 - 1,
                       'User', NULL, t.user_message, '[]'::jsonb, t.id, t.created_at
                FROM conversation_turns t;
                """);
            migrationBuilder.Sql("""
                INSERT INTO conversation_messages (id, conversation_id, sequence, author, author_name, text, citations_json, turn_id, created_at)
                SELECT gen_random_uuid(), t.conversation_id, numbered.position * 2,
                       CASE WHEN t.assistant_message = '' THEN 'System' ELSE 'Assistant' END, NULL,
                       CASE WHEN t.assistant_message = '' THEN t.error ELSE t.assistant_message END,
                       CASE WHEN t.assistant_message = '' THEN '[]'::jsonb ELSE t.citations_json END, t.id, t.created_at
                FROM conversation_turns t
                JOIN (SELECT id, row_number() OVER (PARTITION BY conversation_id ORDER BY sequence) AS position FROM conversation_turns) numbered
                  ON numbered.id = t.id
                WHERE t.assistant_message <> '' OR t.error IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "status",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "trace_id",
                table: "conversation_turns");
        }
    }
}
