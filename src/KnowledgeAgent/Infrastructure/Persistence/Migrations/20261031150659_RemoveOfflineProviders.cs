using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOfflineProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The offline and hash providers are gone, so settings that name them would only fail to resolve.
            migrationBuilder.Sql("DELETE FROM model_profile_overrides WHERE lower(provider) IN ('offline', 'hash');");
            migrationBuilder.Sql("DELETE FROM provider_settings_overrides WHERE lower(key) IN ('offline', 'hash');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The deleted rows named providers that no longer exist, so there is nothing to restore.
        }
    }
}
