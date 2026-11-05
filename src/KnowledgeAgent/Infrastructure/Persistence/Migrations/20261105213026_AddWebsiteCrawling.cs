using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsiteCrawling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "crawl",
                table: "sources",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_fetched_at",
                table: "sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pages_done",
                table: "sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "pages_failed",
                table: "sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "pages_found",
                table: "sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_id",
                table: "sources",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sources_parent_id",
                table: "sources",
                column: "parent_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sources_sources_parent_id",
                table: "sources",
                column: "parent_id",
                principalTable: "sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sources_sources_parent_id",
                table: "sources");

            migrationBuilder.DropIndex(
                name: "ix_sources_parent_id",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "crawl",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "last_fetched_at",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "pages_done",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "pages_failed",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "pages_found",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "sources");
        }
    }
}
