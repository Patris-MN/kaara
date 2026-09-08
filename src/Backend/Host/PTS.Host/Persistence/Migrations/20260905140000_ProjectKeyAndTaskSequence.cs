using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Phase 8.2: stable Project Key (tenant-unique), optional description,
/// concurrency-safe NextTaskSequence, and Task SequenceNumber for KEY-n references.
/// Backfills existing projects/tasks deterministically.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260905140000_ProjectKeyAndTaskSequence")]
public partial class ProjectKeyAndTaskSequence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "description",
            table: "projects",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "key",
            table: "projects",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "key_normalized",
            table: "projects",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "next_task_sequence",
            table: "projects",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "sequence_number",
            table: "tasks",
            type: "integer",
            nullable: true);

        migrationBuilder.Sql(
            """
            ALTER TABLE projects DISABLE ROW LEVEL SECURITY;
            ALTER TABLE tasks DISABLE ROW LEVEL SECURITY;

            CREATE OR REPLACE FUNCTION pg_temp.pts_suggest_project_key(p_name text)
            RETURNS text
            LANGUAGE plpgsql
            AS $$
            DECLARE
              words text[];
              w text;
              result text := '';
              cleaned text;
              alnum text;
            BEGIN
              p_name := btrim(p_name);
              IF p_name = '' THEN
                RETURN 'PR';
              END IF;

              words := regexp_split_to_array(p_name, '\s+');
              IF array_length(words, 1) IS NULL THEN
                RETURN 'PR';
              END IF;

              IF array_length(words, 1) = 1 THEN
                alnum := upper(regexp_replace(words[1], '[^a-zA-Z0-9]', '', 'g'));
                IF length(alnum) >= 2 THEN
                  RETURN substring(alnum FROM 1 FOR LEAST(10, length(alnum)));
                ELSIF length(alnum) = 1 THEN
                  RETURN alnum || 'X';
                ELSE
                  RETURN 'PR';
                END IF;
              END IF;

              FOREACH w IN ARRAY words LOOP
                cleaned := regexp_replace(w, '[^a-zA-Z0-9]', '', 'g');
                IF length(cleaned) > 0 THEN
                  result := result || upper(substring(cleaned FROM 1 FOR 1));
                END IF;
              END LOOP;

              IF length(result) < 2 THEN
                result := rpad(result, 2, 'X');
              END IF;

              IF substring(result FROM 1 FOR 1) !~ '[A-Z]' THEN
                result := 'P' || result;
              END IF;

              RETURN substring(result FROM 1 FOR 10);
            END;
            $$;

            DO $$
            DECLARE
              rec RECORD;
              base_key text;
              candidate text;
              suffix int;
              taken boolean;
            BEGIN
              FOR rec IN
                SELECT id, tenant_id, name
                FROM projects
                ORDER BY tenant_id, created_at_utc, id
              LOOP
                base_key := pg_temp.pts_suggest_project_key(rec.name);
                candidate := base_key;
                suffix := 2;

                LOOP
                  SELECT EXISTS(
                    SELECT 1 FROM projects p2
                    WHERE p2.tenant_id = rec.tenant_id
                      AND p2.key_normalized = upper(candidate)
                      AND p2.id <> rec.id
                  ) INTO taken;

                  EXIT WHEN NOT taken;
                  candidate := substring(base_key FROM 1 FOR GREATEST(1, 10 - length(suffix::text))) || suffix::text;
                  suffix := suffix + 1;
                  EXIT WHEN suffix > 99;
                END LOOP;

                UPDATE projects
                SET key = candidate,
                    key_normalized = upper(candidate)
                WHERE id = rec.id;
              END LOOP;
            END $$;

            WITH numbered AS (
              SELECT
                id,
                row_number() OVER (
                  PARTITION BY tenant_id, project_id
                  ORDER BY created_at_utc, id
                ) AS seq
              FROM tasks
            )
            UPDATE tasks t
            SET sequence_number = n.seq
            FROM numbered n
            WHERE t.id = n.id;

            UPDATE projects p
            SET next_task_sequence = COALESCE(
              (
                SELECT MAX(t.sequence_number)
                FROM tasks t
                WHERE t.tenant_id = p.tenant_id
                  AND t.project_id = p.id
              ),
              0);

            ALTER TABLE projects
            ALTER COLUMN key SET NOT NULL;

            ALTER TABLE projects
            ALTER COLUMN key_normalized SET NOT NULL;

            ALTER TABLE tasks
            ALTER COLUMN sequence_number SET NOT NULL;

            ALTER TABLE projects ENABLE ROW LEVEL SECURITY;
            ALTER TABLE projects FORCE ROW LEVEL SECURITY;
            ALTER TABLE tasks ENABLE ROW LEVEL SECURITY;
            ALTER TABLE tasks FORCE ROW LEVEL SECURITY;
            """);

        migrationBuilder.CreateIndex(
            name: "ux_projects_tenant_key_normalized",
            table: "projects",
            columns: new[] { "tenant_id", "key_normalized" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_tasks_tenant_project_sequence",
            table: "tasks",
            columns: new[] { "tenant_id", "project_id", "sequence_number" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_tasks_tenant_project_sequence",
            table: "tasks");

        migrationBuilder.DropIndex(
            name: "ux_projects_tenant_key_normalized",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "sequence_number",
            table: "tasks");

        migrationBuilder.DropColumn(
            name: "next_task_sequence",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "key_normalized",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "key",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "description",
            table: "projects");
    }
}
