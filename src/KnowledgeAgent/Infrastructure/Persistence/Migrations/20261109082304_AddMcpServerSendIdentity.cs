using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpServerSendIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "send_identity",
                table: "mcp_servers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "send_identity",
                table: "mcp_servers");
        }
    }
}
