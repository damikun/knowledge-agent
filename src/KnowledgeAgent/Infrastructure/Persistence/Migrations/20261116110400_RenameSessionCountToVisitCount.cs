using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameSessionCountToVisitCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "session_count",
                table: "widget_visitors",
                newName: "visit_count");

            migrationBuilder.RenameColumn(
                name: "session_count",
                table: "contacts",
                newName: "visit_count");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "visit_count",
                table: "widget_visitors",
                newName: "session_count");

            migrationBuilder.RenameColumn(
                name: "visit_count",
                table: "contacts",
                newName: "session_count");
        }
    }
}
