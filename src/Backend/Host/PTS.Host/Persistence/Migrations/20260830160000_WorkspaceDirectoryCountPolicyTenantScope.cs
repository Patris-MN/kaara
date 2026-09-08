using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <summary>
    /// <see cref="WorkspaceDirectoryCountPolicy"/> added a permissive SELECT
    /// policy that OR-ed with tenant isolation and leaked workspaces from every
    /// organization the caller belongs to during tenant-scoped sessions.
    /// Scope the directory policy to sessions with no current tenant GUC only.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260830160000_WorkspaceDirectoryCountPolicyTenantScope")]
    public class WorkspaceDirectoryCountPolicyTenantScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS workspaces_select_active_member ON workspaces;");
            migrationBuilder.Sql(
                """
                CREATE POLICY workspaces_select_active_member ON workspaces
                    FOR SELECT
                    USING (
                        app.uuid_setting('app.current_tenant_id') IS NULL
                        AND EXISTS (
                            SELECT 1
                            FROM memberships m
                            WHERE m.tenant_id = workspaces.tenant_id
                              AND m.user_id = app.uuid_setting('app.current_user_id')
                              AND m.status = 'Active'
                        )
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS workspaces_select_active_member ON workspaces;");
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
    }
}
