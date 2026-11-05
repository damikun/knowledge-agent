using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "visitor_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "widget_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_widgets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "widget_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    widget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_widget_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_widget_assets_widgets_widget_id",
                        column: x => x.widget_id,
                        principalTable: "widgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "widget_daily_usage",
                columns: table => new
                {
                    widget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    messages = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_widget_daily_usage", x => new { x.widget_id, x.day });
                    table.ForeignKey(
                        name: "fk_widget_daily_usage_widgets_widget_id",
                        column: x => x.widget_id,
                        principalTable: "widgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "widget_visitors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    widget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_widget_visitors", x => x.id);
                    table.ForeignKey(
                        name: "fk_widget_visitors_widgets_widget_id",
                        column: x => x.widget_id,
                        principalTable: "widgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_visitor_id",
                table: "conversations",
                column: "visitor_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_widget_id_updated_at",
                table: "conversations",
                columns: new[] { "widget_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_widget_assets_widget_id",
                table: "widget_assets",
                column: "widget_id");

            migrationBuilder.CreateIndex(
                name: "ix_widget_visitors_widget_id",
                table: "widget_visitors",
                column: "widget_id");

            migrationBuilder.CreateIndex(
                name: "ix_widgets_agent_id",
                table: "widgets",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_widgets_public_key",
                table: "widgets",
                column: "public_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "widget_assets");

            migrationBuilder.DropTable(
                name: "widget_daily_usage");

            migrationBuilder.DropTable(
                name: "widget_visitors");

            migrationBuilder.DropTable(
                name: "widgets");

            migrationBuilder.DropIndex(
                name: "ix_conversations_visitor_id",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_conversations_widget_id_updated_at",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "visitor_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "widget_id",
                table: "conversations");
        }
    }
}
