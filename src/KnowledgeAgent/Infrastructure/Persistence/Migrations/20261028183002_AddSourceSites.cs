using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "site",
                table: "sources",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            // Web sources added earlier get their site from the stored URL, the same way SourceDefaults.SiteOf derives it.
            migrationBuilder.Sql(
                """
                UPDATE sources
                SET site = regexp_replace(
                    rtrim(lower(substring(url from '^[A-Za-z][A-Za-z0-9+.-]*://(?:[^/@]*@)?([^/:?#]+)')), '.'),
                    '^www\.(?=.)', '')
                WHERE type = 'Url' AND url IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_sources_site",
                table: "sources",
                column: "site");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sources_site",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "site",
                table: "sources");
        }
    }
}
