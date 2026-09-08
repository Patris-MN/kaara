using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <summary>
    /// Organization directory summary: one membership-gated workspace-count
    /// function so GET /tenants can return WorkspaceCount without an N+1
    /// frontend (or per-tenant RLS session) loop. Workspace RLS requires
    /// app.current_tenant_id; a directory listing only has current_user_id.
    ///
    /// Also tightens tenants_update to Owner/Admin so organization metadata
    /// edits cannot be applied by a Member even if application code is wrong.
    /// No Tenant logo/media column — Storage is still scaffolding.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260830140000_OrganizationDirectoryCounts")]
    public class OrganizationDirectoryCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION app.workspace_counts_for_user(p_user_id uuid)
                RETURNS TABLE (tenant_id uuid, workspace_count integer)
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public, app
                AS $$
                    SELECT w.tenant_id, COUNT(*)::integer
                    FROM workspaces w
                    INNER JOIN memberships m
                      ON m.tenant_id = w.tenant_id
                     AND m.user_id = p_user_id
                     AND m.status = 'Active'
                    GROUP BY w.tenant_id;
                $$;

                ALTER FUNCTION app.workspace_counts_for_user(uuid) OWNER TO migrator_role;
                REVOKE ALL ON FUNCTION app.workspace_counts_for_user(uuid) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION app.workspace_counts_for_user(uuid) TO app_role;
                """);

            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenants_update ON tenants;
                CREATE POLICY tenants_update ON tenants
                    FOR UPDATE
                    USING (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = tenants.id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Active'
                          AND m.role IN ('Owner', 'Admin')
                    ))
                    WITH CHECK (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = tenants.id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Active'
                          AND m.role IN ('Owner', 'Admin')
                    ));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS app.workspace_counts_for_user(uuid);");

            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenants_update ON tenants;
                CREATE POLICY tenants_update ON tenants
                    FOR UPDATE
                    USING (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = tenants.id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Active'
                    ))
                    WITH CHECK (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = tenants.id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Active'
                    ));
                """);
        }
    }
}
