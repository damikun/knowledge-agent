using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                table: "sources",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "source_folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_folders", x => x.id);
                    table.ForeignKey(
                        name: "fk_source_folders_source_folders_parent_id",
                        column: x => x.parent_id,
                        principalTable: "source_folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sources_folder_id",
                table: "sources",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_source_folders_parent_id",
                table: "source_folders",
                column: "parent_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sources_source_folders_folder_id",
                table: "sources",
                column: "folder_id",
                principalTable: "source_folders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sources_source_folders_folder_id",
                table: "sources");

            migrationBuilder.DropTable(
                name: "source_folders");

            migrationBuilder.DropIndex(
                name: "ix_sources_folder_id",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "folder_id",
                table: "sources");
        }
    }
}
