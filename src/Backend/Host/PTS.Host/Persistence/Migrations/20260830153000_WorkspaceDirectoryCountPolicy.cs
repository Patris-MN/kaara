using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <summary>
    /// Workspace RLS requires app.current_tenant_id. A directory listing only
    /// has current_user_id, and FORCE RLS also applies to SECURITY DEFINER
    /// functions owned by migrator_role. This membership-gated SELECT policy
    /// lets GET /tenants count workspaces the caller can already access,
    /// without N+1 sessions, BYPASSRLS, or cross-tenant leakage.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260830153000_WorkspaceDirectoryCountPolicy")]
    public class WorkspaceDirectoryCountPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS app.workspace_counts_for_current_user();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS app.workspace_counts_for_user(uuid);");
            migrationBuilder.Sql(
                """
                CREATE POLICY workspaces_select_active_member ON workspaces
                    FOR SELECT
                    USING (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = workspaces.tenant_id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Active'
                    ));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS workspaces_select_active_member ON workspaces;");
        }
    }
}
