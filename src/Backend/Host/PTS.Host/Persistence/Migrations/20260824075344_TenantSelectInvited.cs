using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantSelectInvited : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Invited users must be able to read tenant name/slug to accept an
            // invitation. This does not grant workspace/project access: those
            // tables still require app.current_tenant_id, which is set only
            // after an Active membership is proven.
            migrationBuilder.Sql(
                """
                CREATE POLICY tenants_select_invited ON tenants
                    FOR SELECT
                    USING (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = tenants.id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Invited'
                    ));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenants_select_invited ON tenants;");
        }
    }
}
