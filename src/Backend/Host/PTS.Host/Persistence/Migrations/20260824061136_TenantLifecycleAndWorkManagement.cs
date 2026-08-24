using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantLifecycleAndWorkManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspaces", x => x.id);
                    table.UniqueConstraint("ak_workspaces_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_workspaces_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_projects_workspaces_tenant_id_workspace_id",
                        columns: x => new { x.tenant_id, x.workspace_id },
                        principalTable: "workspaces",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_projects_tenant_id",
                table: "projects",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_projects_tenant_id_workspace_id",
                table: "projects",
                columns: new[] { "tenant_id", "workspace_id" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_workspace_id",
                table: "projects",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_tenant_id",
                table: "workspaces",
                column: "tenant_id");

            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE workspaces, projects TO app_role;");

            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tenants_insert ON tenants;
                CREATE POLICY tenants_insert ON tenants
                    FOR INSERT
                    WITH CHECK (app.uuid_setting('app.current_user_id') IS NOT NULL);

                CREATE POLICY memberships_tenant_insert ON memberships
                    FOR INSERT
                    WITH CHECK (
                        tenant_id = app.uuid_setting('app.current_tenant_id')
                        AND app.uuid_setting('app.current_tenant_id') IS NOT NULL
                    );

                ALTER TABLE workspaces ENABLE ROW LEVEL SECURITY;
                ALTER TABLE workspaces FORCE ROW LEVEL SECURITY;
                CREATE POLICY workspaces_tenant_isolation ON workspaces
                    USING (tenant_id = app.uuid_setting('app.current_tenant_id'))
                    WITH CHECK (tenant_id = app.uuid_setting('app.current_tenant_id'));

                ALTER TABLE projects ENABLE ROW LEVEL SECURITY;
                ALTER TABLE projects FORCE ROW LEVEL SECURITY;
                CREATE POLICY projects_tenant_isolation ON projects
                    USING (tenant_id = app.uuid_setting('app.current_tenant_id'))
                    WITH CHECK (tenant_id = app.uuid_setting('app.current_tenant_id'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS projects_tenant_isolation ON projects;
                ALTER TABLE projects NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE projects DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS workspaces_tenant_isolation ON workspaces;
                ALTER TABLE workspaces NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE workspaces DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS memberships_tenant_insert ON memberships;

                DROP POLICY IF EXISTS tenants_insert ON tenants;
                CREATE POLICY tenants_insert ON tenants
                    FOR INSERT
                    WITH CHECK (true);

                REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLE workspaces, projects FROM app_role;
                """);

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "workspaces");
        }
    }
}
