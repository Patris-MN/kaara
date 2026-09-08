using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Case-insensitive resource name uniqueness within tenant/workspace scopes.
/// Backfills name_normalized from lower(btrim(name)). Migration fails if legacy
/// duplicates exist under that rule — resolve manually before re-applying.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260831153000_ResourceNameUniqueness")]
public partial class ResourceNameUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "name_normalized",
            table: "workspaces",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "name_normalized",
            table: "projects",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        // Backfill and NOT NULL must run in one SQL batch before any separate
        // AlterColumn — EF otherwise applies SET NOT NULL before the UPDATE.
        // RLS must be disabled during backfill (see WorkspaceUpdatedAtUtcBackfill).
        migrationBuilder.Sql(
            """
            ALTER TABLE workspaces DISABLE ROW LEVEL SECURITY;
            ALTER TABLE projects DISABLE ROW LEVEL SECURITY;

            UPDATE workspaces
            SET name_normalized = lower(btrim(name))
            WHERE name_normalized IS NULL;

            UPDATE projects
            SET name_normalized = lower(btrim(name))
            WHERE name_normalized IS NULL;

            ALTER TABLE workspaces
            ALTER COLUMN name_normalized SET NOT NULL;

            ALTER TABLE projects
            ALTER COLUMN name_normalized SET NOT NULL;

            ALTER TABLE workspaces ENABLE ROW LEVEL SECURITY;
            ALTER TABLE workspaces FORCE ROW LEVEL SECURITY;
            ALTER TABLE projects ENABLE ROW LEVEL SECURITY;
            ALTER TABLE projects FORCE ROW LEVEL SECURITY;
            """);

        migrationBuilder.CreateIndex(
            name: "ux_workspaces_tenant_name_normalized",
            table: "workspaces",
            columns: new[] { "tenant_id", "name_normalized" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_projects_tenant_workspace_name_normalized",
            table: "projects",
            columns: new[] { "tenant_id", "workspace_id", "name_normalized" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_workspaces_tenant_name_normalized",
            table: "workspaces");

        migrationBuilder.DropIndex(
            name: "ux_projects_tenant_workspace_name_normalized",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "name_normalized",
            table: "workspaces");

        migrationBuilder.DropColumn(
            name: "name_normalized",
            table: "projects");
    }
}
