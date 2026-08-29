using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceResourceAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_memberships_tenant_id_id",
                table: "memberships",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "workspace_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspace_access", x => x.id);
                    table.CheckConstraint("ck_workspace_access_access_level", "access_level IN ('View', 'Edit')");
                    table.ForeignKey(
                        name: "fk_workspace_access_memberships_tenant_id_membership_id",
                        columns: x => new { x.tenant_id, x.membership_id },
                        principalTable: "memberships",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_workspace_access_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_workspace_access_workspaces_tenant_id_workspace_id",
                        columns: x => new { x.tenant_id, x.workspace_id },
                        principalTable: "workspaces",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_workspace_access_tenant_membership_workspace",
                table: "workspace_access",
                columns: new[] { "tenant_id", "membership_id", "workspace_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspace_access_tenant_workspace",
                table: "workspace_access",
                columns: new[] { "tenant_id", "workspace_id" });

            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE workspace_access TO app_role;");

            migrationBuilder.Sql(
                """
                ALTER TABLE workspace_access ENABLE ROW LEVEL SECURITY;
                ALTER TABLE workspace_access FORCE ROW LEVEL SECURITY;
                CREATE POLICY workspace_access_tenant_isolation ON workspace_access
                    USING (tenant_id = app.uuid_setting('app.current_tenant_id'))
                    WITH CHECK (tenant_id = app.uuid_setting('app.current_tenant_id'));

                CREATE POLICY memberships_tenant_manager_select ON memberships
                    FOR SELECT
                    USING (
                        tenant_id = app.uuid_setting('app.current_tenant_id')
                        AND current_setting('app.current_membership_role', true) IN ('Owner', 'Admin')
                    );

                CREATE POLICY users_tenant_manager_select ON users
                    FOR SELECT
                    USING (
                        current_setting('app.current_membership_role', true) IN ('Owner', 'Admin')
                        AND EXISTS (
                            SELECT 1
                            FROM memberships membership
                            WHERE membership.user_id = users.id
                              AND membership.tenant_id = app.uuid_setting('app.current_tenant_id')
                        )
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS users_tenant_manager_select ON users;
                DROP POLICY IF EXISTS memberships_tenant_manager_select ON memberships;
                DROP POLICY IF EXISTS workspace_access_tenant_isolation ON workspace_access;
                ALTER TABLE workspace_access NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE workspace_access DISABLE ROW LEVEL SECURITY;
                REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLE workspace_access FROM app_role;
                """);

            migrationBuilder.DropTable(
                name: "workspace_access");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_memberships_tenant_id_id",
                table: "memberships");
        }
    }
}
