using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpConnectionsAndAgentApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "http_api_enabled",
                table: "agents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "mcp_server_enabled",
                table: "agents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "mcp_tools",
                table: "agents",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.CreateTable(
                name: "agent_api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requests_per_minute = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_api_keys_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mcp_servers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    auth_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    header_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    protected_secret = table.Column<string>(type: "text", nullable: true),
                    secret_hint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_check_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_check_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    server_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    server_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tools = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_servers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_api_keys_agent_id",
                table: "agent_api_keys",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_api_keys_hash",
                table: "agent_api_keys",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mcp_servers_key",
                table: "mcp_servers",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_api_keys");

            migrationBuilder.DropTable(
                name: "mcp_servers");

            migrationBuilder.DropColumn(
                name: "http_api_enabled",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "mcp_server_enabled",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "mcp_tools",
                table: "agents");
        }
    }
}
