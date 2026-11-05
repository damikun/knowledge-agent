using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillConversationAssignees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Best effort: the latest joined name owns the conversation, without an id.
            migrationBuilder.Sql(
                """
                UPDATE conversations AS c
                SET assignee_name = h.assignee, assigned_at = COALESCE(h.accepted_at, h.requested_at)
                FROM (
                    SELECT DISTINCT ON (conversation_id) conversation_id, assignee, accepted_at, requested_at
                    FROM conversation_handovers
                    WHERE assignee IS NOT NULL AND assignee <> ''
                    ORDER BY conversation_id, requested_at DESC
                ) AS h
                WHERE c.id = h.conversation_id AND c.assignee_name IS NULL AND c.status <> 'Ai';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the names stay, and the previous migration's Down drops the columns.
        }
    }
}
