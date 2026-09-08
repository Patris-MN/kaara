using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <summary>
    /// Replaces the GUC-reading count function with an explicit user-id
    /// argument. SET LOCAL (transaction-scoped) current_user_id is not
    /// reliably visible to a SECURITY DEFINER function invoked through a
    /// separate DbCommand on the same connection.
    /// The Host only passes the authenticated user id.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260830152000_OrganizationDirectoryCountsByUser")]
    public class OrganizationDirectoryCountsByUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS app.workspace_counts_for_current_user();");
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS app.workspace_counts_for_user(uuid);");
        }
    }
}
