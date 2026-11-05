using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeopleAndAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "identity_secret_created_at",
                table: "widgets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "protected_identity_secret",
                table: "widgets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contact_id",
                table: "widget_visitors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "assigned_at",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assignee_id",
                table: "conversations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assignee_name",
                table: "conversations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contact_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_id",
                table: "conversations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_by_name",
                table: "conversations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "assignee_id",
                table: "conversation_handovers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    conversation_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_reads",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_read_sequence = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_reads", x => new { x.user_id, x.conversation_id });
                    table.ForeignKey(
                        name: "fk_conversation_reads_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_members",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_members", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_widget_visitors_contact_id",
                table: "widget_visitors",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_assignee_id_status",
                table: "conversations",
                columns: new[] { "assignee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_contact_id_updated_at",
                table: "conversations",
                columns: new[] { "contact_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_created_by_id",
                table: "conversations",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_email",
                table: "contacts",
                column: "email")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_external_id",
                table: "contacts",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_last_seen_at_id",
                table: "contacts",
                columns: new[] { "last_seen_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_name",
                table: "contacts",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_reads_conversation_id",
                table: "conversation_reads",
                column: "conversation_id");

            // Each visitor becomes an anonymous person. The contact reuses the visitor's id, so the links below need no lookup.
            migrationBuilder.Sql(
                """
                INSERT INTO contacts (id, external_id, name, email, notes, attributes, first_seen_at, last_seen_at, conversation_count)
                SELECT id, NULL, name, email, NULL, '{}'::jsonb, created_at, last_seen_at, 0 FROM widget_visitors;

                UPDATE widget_visitors SET contact_id = id;

                UPDATE conversations SET contact_id = visitor_id
                WHERE visitor_id IS NOT NULL AND EXISTS (SELECT 1 FROM contacts WHERE contacts.id = conversations.visitor_id);

                UPDATE contacts SET conversation_count = counted.count
                FROM (SELECT contact_id, count(*)::int AS count FROM conversations WHERE contact_id IS NOT NULL GROUP BY contact_id) AS counted
                WHERE contacts.id = counted.contact_id;
                """);

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_contacts_contact_id",
                table: "conversations",
                column: "contact_id",
                principalTable: "contacts",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_widget_visitors_contacts_contact_id",
                table: "widget_visitors",
                column: "contact_id",
                principalTable: "contacts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conversations_contacts_contact_id",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "fk_widget_visitors_contacts_contact_id",
                table: "widget_visitors");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "conversation_reads");

            migrationBuilder.DropTable(
                name: "staff_members");

            migrationBuilder.DropIndex(
                name: "ix_widget_visitors_contact_id",
                table: "widget_visitors");

            migrationBuilder.DropIndex(
                name: "ix_conversations_assignee_id_status",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_contact_id_updated_at",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_created_by_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "identity_secret_created_at",
                table: "widgets");

            migrationBuilder.DropColumn(
                name: "protected_identity_secret",
                table: "widgets");

            migrationBuilder.DropColumn(
                name: "contact_id",
                table: "widget_visitors");

            migrationBuilder.DropColumn(
                name: "assigned_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "assignee_name",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "contact_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "created_by_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "created_by_name",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "conversation_handovers");
        }
    }
}
