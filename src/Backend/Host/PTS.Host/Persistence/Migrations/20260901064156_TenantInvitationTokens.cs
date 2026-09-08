using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantInvitationTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitation_workspace_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitation_workspace_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_invitations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_workspace_grants_invitation_id",
                table: "invitation_workspace_grants",
                column: "invitation_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_invitations_tenant_email",
                table: "tenant_invitations",
                columns: new[] { "tenant_id", "invited_email" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_invitations_token_hash",
                table: "tenant_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE tenant_invitations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenant_invitations FORCE ROW LEVEL SECURITY;
                ALTER TABLE invitation_workspace_grants ENABLE ROW LEVEL SECURITY;
                ALTER TABLE invitation_workspace_grants FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_invitations_manager ON tenant_invitations
                    FOR ALL TO app_role
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                        AND EXISTS (
                            SELECT 1 FROM memberships m
                            WHERE m.tenant_id = tenant_invitations.tenant_id
                              AND m.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                              AND m.status = 'Active'
                              AND m.role IN ('Owner', 'Admin')
                        )
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                        AND EXISTS (
                            SELECT 1 FROM memberships m
                            WHERE m.tenant_id = tenant_invitations.tenant_id
                              AND m.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                              AND m.status = 'Active'
                              AND m.role IN ('Owner', 'Admin')
                        )
                    );

                CREATE POLICY tenant_invitations_token_lookup ON tenant_invitations
                    FOR SELECT TO app_role
                    USING (
                        token_hash = NULLIF(current_setting('app.invitation_token_hash', true), '')
                    );

                CREATE POLICY invitation_workspace_grants_manager ON invitation_workspace_grants
                    FOR ALL TO app_role
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                        AND EXISTS (
                            SELECT 1 FROM memberships m
                            WHERE m.tenant_id = invitation_workspace_grants.tenant_id
                              AND m.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                              AND m.status = 'Active'
                              AND m.role IN ('Owner', 'Admin')
                        )
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
                        AND EXISTS (
                            SELECT 1 FROM memberships m
                            WHERE m.tenant_id = invitation_workspace_grants.tenant_id
                              AND m.user_id = NULLIF(current_setting('app.current_user_id', true), '')::uuid
                              AND m.status = 'Active'
                              AND m.role IN ('Owner', 'Admin')
                        )
                    );

                CREATE POLICY invitation_workspace_grants_token_lookup ON invitation_workspace_grants
                    FOR SELECT TO app_role
                    USING (
                        EXISTS (
                            SELECT 1 FROM tenant_invitations ti
                            WHERE ti.id = invitation_workspace_grants.invitation_id
                              AND ti.token_hash = NULLIF(current_setting('app.invitation_token_hash', true), '')
                        )
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS invitation_workspace_grants_token_lookup ON invitation_workspace_grants;
                DROP POLICY IF EXISTS invitation_workspace_grants_manager ON invitation_workspace_grants;
                DROP POLICY IF EXISTS tenant_invitations_token_lookup ON tenant_invitations;
                DROP POLICY IF EXISTS tenant_invitations_manager ON tenant_invitations;
                """);

            migrationBuilder.DropTable(
                name: "invitation_workspace_grants");

            migrationBuilder.DropTable(
                name: "tenant_invitations");
        }
    }
}
