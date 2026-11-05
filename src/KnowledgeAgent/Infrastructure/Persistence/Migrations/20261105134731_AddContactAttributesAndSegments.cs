using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactAttributesAndSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "widget_visitors",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_page_url",
                table: "widget_visitors",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_referrer",
                table: "widget_visitors",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "session_count",
                table: "widget_visitors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "attribute_sources",
                table: "contacts",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "contacts",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_page_url",
                table: "contacts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_referrer",
                table: "contacts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "session_count",
                table: "contacts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "contact_attributes",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false),
                    expose_to_agent = table.Column<bool>(type: "boolean", nullable: false),
                    trust = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_attributes", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    filter = table.Column<string>(type: "jsonb", nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    icon = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    shared = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_segments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_attributes",
                table: "contacts",
                column: "attributes")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_segments_name",
                table: "segments",
                column: "name");

            // "Plan" -> "plan", "company.size" -> "company_size", built-in names get "_custom";
            // the first key wins a clash and keys that cannot be fixed are dropped.
            migrationBuilder.Sql(
                """
                UPDATE contacts c SET attributes = coalesce((
                    SELECT jsonb_object_agg(k, v) FROM (
                        SELECT DISTINCT ON (k) k, v FROM (
                            SELECT CASE WHEN n IN ('id','name','email','user_id','identified','first_seen','last_seen','sessions','conversations',
                                                   'returning','last_page','referrer','language','widget','country','notes','created_at','updated_at')
                                        THEN n || '_custom' ELSE n END AS k,
                                   e.value AS v, e.key AS original
                            FROM jsonb_each(c.attributes) AS e,
                                 LATERAL (SELECT left(regexp_replace(lower(e.key), '[^a-z0-9_]', '_', 'g'), 40) AS n) AS normalized
                            WHERE n ~ '^[a-z][a-z0-9_]{0,39}$'
                        ) AS renamed
                        ORDER BY k, original
                    ) AS kept), '{}'::jsonb)
                WHERE c.attributes <> '{}'::jsonb;
                """);

            // A key whose every value is a number or true/false takes that type; the rest stay text.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE attribute_types ON COMMIT DROP AS
                SELECT e.key,
                       CASE WHEN bool_and(jsonb_typeof(e.value) = 'string' AND (e.value #>> '{}') ~ '^-?[0-9]{1,15}(\.[0-9]{1,10})?$') THEN 'number'
                            WHEN bool_and(jsonb_typeof(e.value) = 'string' AND (e.value #>> '{}') IN ('true', 'false')) THEN 'boolean'
                            ELSE 'string' END AS type,
                       min(c.first_seen_at) AS first_seen_at,
                       max(c.last_seen_at) AS last_seen_at
                FROM contacts c, jsonb_each(c.attributes) AS e
                GROUP BY e.key;

                UPDATE contacts c SET attributes = (
                    SELECT jsonb_object_agg(e.key, CASE t.type
                        WHEN 'number' THEN to_jsonb((e.value #>> '{}')::numeric)
                        WHEN 'boolean' THEN to_jsonb((e.value #>> '{}')::boolean)
                        ELSE e.value END)
                    FROM jsonb_each(c.attributes) AS e JOIN attribute_types t ON t.key = e.key)
                WHERE c.attributes <> '{}'::jsonb;

                -- They came from identify() with a valid user hash.
                UPDATE contacts c SET attribute_sources = (
                    SELECT jsonb_object_agg(e.key, jsonb_build_object('source', 'verified', 'updatedAt', to_char(c.last_seen_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')))
                    FROM jsonb_each(c.attributes) AS e)
                WHERE c.attributes <> '{}'::jsonb;

                INSERT INTO contact_attributes (key, label, type, first_seen_at, last_seen_at, visible, expose_to_agent, trust)
                SELECT key, left(upper(left(replace(key, '_', ' '), 1)) || substr(replace(key, '_', ' '), 2), 60), type, first_seen_at, last_seen_at, true, false, 'any'
                FROM attribute_types;
                """);

            // Every browser so far had at least one session; a person counts the browsers they used.
            migrationBuilder.Sql(
                """
                UPDATE widget_visitors SET session_count = 1;
                UPDATE contacts c SET session_count = greatest(1, (SELECT count(*) FROM widget_visitors v WHERE v.contact_id = c.id))::int;
                """);

            // Contacts that were only page opens go; their browsers stay as visitors.
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE empty_contacts ON COMMIT DROP AS
                SELECT c.id FROM contacts c
                WHERE c.external_id IS NULL AND c.name IS NULL AND c.email IS NULL AND c.notes IS NULL AND c.attributes = '{}'::jsonb
                  AND NOT EXISTS (SELECT 1 FROM conversations WHERE contact_id = c.id);

                UPDATE widget_visitors SET contact_id = NULL WHERE contact_id IN (SELECT id FROM empty_contacts);
                DELETE FROM contacts WHERE id IN (SELECT id FROM empty_contacts);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_attributes");

            migrationBuilder.DropTable(
                name: "segments");

            migrationBuilder.DropIndex(
                name: "ix_contacts_attributes",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "language",
                table: "widget_visitors");

            migrationBuilder.DropColumn(
                name: "last_page_url",
                table: "widget_visitors");

            migrationBuilder.DropColumn(
                name: "last_referrer",
                table: "widget_visitors");

            migrationBuilder.DropColumn(
                name: "session_count",
                table: "widget_visitors");

            migrationBuilder.DropColumn(
                name: "attribute_sources",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "language",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "last_page_url",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "last_referrer",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "session_count",
                table: "contacts");
        }
    }
}
