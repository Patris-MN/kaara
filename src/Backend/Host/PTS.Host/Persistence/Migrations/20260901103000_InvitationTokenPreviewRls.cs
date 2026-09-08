using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Superseded by InvitationPreviewSnapshots — cross-table preview policies caused
/// RLS recursion; retained for migration history continuity.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260901103000_InvitationTokenPreviewRls")]
public class InvitationTokenPreviewRls : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE POLICY tenants_select_invitation_token ON tenants
                FOR SELECT TO app_role
                USING (EXISTS (
                    SELECT 1 FROM tenant_invitations ti
                    WHERE ti.tenant_id = tenants.id
                      AND ti.token_hash = NULLIF(current_setting('app.invitation_token_hash', true), '')
                      AND ti.used_at_utc IS NULL
                      AND ti.revoked_at_utc IS NULL
                      AND ti.expires_at_utc > now()
                ));

            CREATE POLICY memberships_select_invitation_inviter ON memberships
                FOR SELECT TO app_role
                USING (EXISTS (
                    SELECT 1 FROM tenant_invitations ti
                    WHERE ti.created_by_membership_id = memberships.id
                      AND ti.token_hash = NULLIF(current_setting('app.invitation_token_hash', true), '')
                      AND ti.used_at_utc IS NULL
                      AND ti.revoked_at_utc IS NULL
                      AND ti.expires_at_utc > now()
                ));

            CREATE POLICY users_select_invitation_inviter ON users
                FOR SELECT TO app_role
                USING (EXISTS (
                    SELECT 1 FROM tenant_invitations ti
                    JOIN memberships m ON m.id = ti.created_by_membership_id
                    WHERE m.user_id = users.id
                      AND ti.token_hash = NULLIF(current_setting('app.invitation_token_hash', true), '')
                      AND ti.used_at_utc IS NULL
                      AND ti.revoked_at_utc IS NULL
                      AND ti.expires_at_utc > now()
                ));

            CREATE POLICY workspaces_select_invitation_preview ON workspaces
                FOR SELECT TO app_role
                USING (EXISTS (
                    SELECT 1 FROM invitation_workspace_grants g
                    JOIN tenant_invitations ti ON ti.id = g.invitation_id
                    WHERE g.workspace_id = workspaces.id
                      AND g.tenant_id = workspaces.tenant_id
                      AND ti.token_hash = NULLIF(current_setting('app.invitation_token_hash', true), '')
                      AND ti.used_at_utc IS NULL
                      AND ti.revoked_at_utc IS NULL
                      AND ti.expires_at_utc > now()
                ));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS workspaces_select_invitation_preview ON workspaces;
            DROP POLICY IF EXISTS users_select_invitation_inviter ON users;
            DROP POLICY IF EXISTS memberships_select_invitation_inviter ON memberships;
            DROP POLICY IF EXISTS tenants_select_invitation_token ON tenants;
            """);
    }
}
