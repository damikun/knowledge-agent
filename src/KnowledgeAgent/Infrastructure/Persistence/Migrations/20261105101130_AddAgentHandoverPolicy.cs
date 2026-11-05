using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentHandoverPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "handover_policy",
                table: "agents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Never");

            // Agents that had the handover tool get a matching policy; the built-in agent never offers a person.
            migrationBuilder.Sql("""
                UPDATE agents a
                SET handover_policy = CASE
                    WHEN a.is_built_in OR NOT (a.tools ? 'request_human_handover') THEN 'Never'
                    WHEN EXISTS (
                        SELECT 1 FROM widgets w
                        WHERE w.agent_id = a.id
                          AND COALESCE((w.settings -> 'handover' ->> 'offerWhenUnsure')::boolean, true)
                          AND (COALESCE((w.settings -> 'behavior' ->> 'handoverEnabled')::boolean, false)
                               OR NOT EXISTS (
                                   SELECT 1 FROM widgets other
                                   WHERE other.agent_id = a.id
                                     AND COALESCE((other.settings -> 'behavior' ->> 'handoverEnabled')::boolean, false))))
                        THEN 'WhenUnsure'
                    ELSE 'OnRequest'
                END;
                """);
            migrationBuilder.Sql("""
                UPDATE agents SET tools = tools - 'request_human_handover' WHERE tools ? 'request_human_handover';
                """);
            migrationBuilder.Sql("""
                UPDATE widgets SET settings = settings #- '{handover,offerWhenUnsure}' WHERE settings -> 'handover' ? 'offerWhenUnsure';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE agents SET tools = tools || '["request_human_handover"]'::jsonb
                WHERE (handover_policy <> 'Never' OR is_built_in) AND NOT (tools ? 'request_human_handover');
                """);
            migrationBuilder.Sql("""
                UPDATE widgets w
                SET settings = jsonb_set(settings, '{handover,offerWhenUnsure}',
                    to_jsonb(EXISTS (SELECT 1 FROM agents a WHERE a.id = w.agent_id AND a.handover_policy = 'WhenUnsure')))
                WHERE settings ? 'handover';
                """);
            migrationBuilder.DropColumn(
                name: "handover_policy",
                table: "agents");
        }
    }
}
