using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationSearchAndEvalDatasets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eval_datasets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    copied_from = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_datasets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "eval_dataset_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dataset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    reference_answer = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    expected_facts = table.Column<string>(type: "jsonb", nullable: false),
                    expected_sources = table.Column<string>(type: "jsonb", nullable: false),
                    answerable = table.Column<bool>(type: "boolean", nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eval_dataset_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_eval_dataset_items_eval_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "eval_datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_created_at",
                table: "conversations",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_title",
                table: "conversations",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_turns_created_at",
                table: "conversation_turns",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_text",
                table: "conversation_messages",
                column: "text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_eval_dataset_items_dataset_id_created_at",
                table: "eval_dataset_items",
                columns: new[] { "dataset_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_eval_datasets_normalized_name",
                table: "eval_datasets",
                column: "normalized_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eval_dataset_items");

            migrationBuilder.DropTable(
                name: "eval_datasets");

            migrationBuilder.DropIndex(
                name: "ix_conversations_created_at",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_title",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversation_turns_created_at",
                table: "conversation_turns");

            migrationBuilder.DropIndex(
                name: "ix_conversation_messages_text",
                table: "conversation_messages");
        }
    }
}
