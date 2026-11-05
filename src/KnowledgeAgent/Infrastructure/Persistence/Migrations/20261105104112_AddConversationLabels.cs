using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "auto_labeled_at",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pinned",
                table: "conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "labels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    auto_apply = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_labels",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    removed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_labels", x => new { x.conversation_id, x.label_id });
                    table.ForeignKey(
                        name: "fk_conversation_labels_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_labels_labels_label_id",
                        column: x => x.label_id,
                        principalTable: "labels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_pinned_updated_at_id",
                table: "conversations",
                columns: new[] { "pinned", "updated_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_labels_label_id_conversation_id",
                table: "conversation_labels",
                columns: new[] { "label_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_labels_normalized_name",
                table: "labels",
                column: "normalized_name",
                unique: true);

            // The default labels, inserted once here so labels an admin deleted do not come back on the next start.
            migrationBuilder.Sql("""
                INSERT INTO labels (id, name, normalized_name, color, description, auto_apply, created_at, updated_at) VALUES
                ('019a5a00-0000-7000-8000-000000000001', 'Question', 'QUESTION', 'blue',
                 'The person asks how something works, where to find something or whether something is possible. Nothing is broken.',
                 true, now(), now()),
                ('019a5a00-0000-7000-8000-000000000002', 'Problem', 'PROBLEM', 'red',
                 'Something does not work as expected and the person needs it fixed: an error, a bug, a failed payment, or trouble with an order or an account.',
                 true, now(), now()),
                ('019a5a00-0000-7000-8000-000000000003', 'Feedback', 'FEEDBACK', 'green',
                 'The person shares an opinion about the product or the service: praise, a complaint or a suggestion, without needing an answer to a question.',
                 true, now(), now())
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_labels");

            migrationBuilder.DropTable(
                name: "labels");

            migrationBuilder.DropIndex(
                name: "ix_conversations_pinned_updated_at_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "auto_labeled_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "pinned",
                table: "conversations");
        }
    }
}
