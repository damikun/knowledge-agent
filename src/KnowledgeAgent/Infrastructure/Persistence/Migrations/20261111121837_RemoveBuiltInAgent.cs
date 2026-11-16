using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBuiltInAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The former built-in agent stays, as an ordinary agent the team can change or delete.
            migrationBuilder.Sql("""
                UPDATE agents
                SET description = 'Answers from all your sources. Change it or add agents of your own.'
                WHERE is_built_in;

                UPDATE agents
                SET key = 'assistant'
                WHERE is_built_in AND NOT EXISTS (SELECT 1 FROM agents WHERE key = 'assistant');
                """);

            migrationBuilder.DropColumn(
                name: "is_built_in",
                table: "agents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_built_in",
                table: "agents",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
