using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSpecialists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "active_agent_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "active_agent_name",
                table: "conversations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "staff_only",
                table: "conversation_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "show_specialist_name",
                table: "agents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "specialists",
                table: "agents",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "active_agent_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "active_agent_name",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "staff_only",
                table: "conversation_messages");

            migrationBuilder.DropColumn(
                name: "show_specialist_name",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "specialists",
                table: "agents");
        }
    }
}
