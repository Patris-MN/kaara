using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Allows Owner/Admin membership mutations (role/status) and invited-membership
/// cleanup under tenant manager RLS context. Without these policies, member
/// management actions fail with PostgreSQL RLS violations (HTTP 500).
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260901130000_MembershipManagerMutationRls")]
public partial class MembershipManagerMutationRls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE POLICY memberships_tenant_manager_update ON memberships
                FOR UPDATE
                USING (
                    tenant_id = app.uuid_setting('app.current_tenant_id')
                    AND current_setting('app.current_membership_role', true) IN ('Owner', 'Admin')
                    AND user_id <> app.uuid_setting('app.current_user_id')
                )
                WITH CHECK (
                    tenant_id = app.uuid_setting('app.current_tenant_id')
                    AND current_setting('app.current_membership_role', true) IN ('Owner', 'Admin')
                    AND user_id <> app.uuid_setting('app.current_user_id')
                );

            CREATE POLICY memberships_tenant_manager_delete ON memberships
                FOR DELETE
                USING (
                    tenant_id = app.uuid_setting('app.current_tenant_id')
                    AND current_setting('app.current_membership_role', true) IN ('Owner', 'Admin')
                    AND status = 'Invited'
                );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS memberships_tenant_manager_delete ON memberships;
            DROP POLICY IF EXISTS memberships_tenant_manager_update ON memberships;
            """);
    }
}
