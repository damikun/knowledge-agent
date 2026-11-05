using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalSourceFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_internal",
                table: "source_folders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // A user folder already called Internal is renamed so it does not clash with the built-in one.
            migrationBuilder.Sql("UPDATE source_folders SET name = 'Internal (renamed)' WHERE parent_id IS NULL AND lower(name) = 'internal';");

            migrationBuilder.InsertData(
                table: "source_folders",
                columns: new[] { "id", "created_at", "is_internal", "name", "parent_id", "updated_at" },
                values: new object[] { new Guid("0193a5c4-0000-7000-8000-00000000f01d"), new DateTimeOffset(new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Internal", null, new DateTimeOffset(new DateTime(2026, 11, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "source_folders",
                keyColumn: "id",
                keyValue: new Guid("0193a5c4-0000-7000-8000-00000000f01d"));

            migrationBuilder.DropColumn(
                name: "is_internal",
                table: "source_folders");
        }
    }
}
