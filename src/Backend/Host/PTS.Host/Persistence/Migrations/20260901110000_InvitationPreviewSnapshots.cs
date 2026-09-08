using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Replaces cross-table invitation preview RLS (which caused policy recursion on
/// login) with denormalized snapshot columns populated at invite time.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260901110000_InvitationPreviewSnapshots")]
public class InvitationPreviewSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS workspaces_select_invitation_preview ON workspaces;
            DROP POLICY IF EXISTS users_select_invitation_inviter ON users;
            DROP POLICY IF EXISTS memberships_select_invitation_inviter ON memberships;
            DROP POLICY IF EXISTS tenants_select_invitation_token ON tenants;
            """);

        migrationBuilder.AddColumn<string>(
            name: "organization_name",
            table: "tenant_invitations",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "inviter_display_name",
            table: "tenant_invitations",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "workspace_name",
            table: "invitation_workspace_grants",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");

        migrationBuilder.Sql(
            """
            UPDATE tenant_invitations ti
            SET organization_name = t.name
            FROM tenants t
            WHERE t.id = ti.tenant_id
              AND ti.organization_name = '';

            UPDATE invitation_workspace_grants g
            SET workspace_name = w.name
            FROM workspaces w
            WHERE w.id = g.workspace_id
              AND w.tenant_id = g.tenant_id
              AND g.workspace_name = '';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "workspace_name", table: "invitation_workspace_grants");
        migrationBuilder.DropColumn(name: "inviter_display_name", table: "tenant_invitations");
        migrationBuilder.DropColumn(name: "organization_name", table: "tenant_invitations");
    }
}
