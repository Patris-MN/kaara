using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskManagementCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_projects_tenant_id_workspace_id",
                table: "projects");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_projects_tenant_id_workspace_id_id",
                table: "projects",
                columns: new[] { "tenant_id", "workspace_id", "id" });

            migrationBuilder.CreateTable(
                name: "tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.CheckConstraint("ck_tasks_priority", "priority IN ('Low', 'Medium', 'High')");
                    table.CheckConstraint("ck_tasks_status", "status IN ('Todo', 'InProgress', 'Done')");
                    table.ForeignKey(
                        name: "fk_tasks_projects_tenant_id_workspace_id_project_id",
                        columns: x => new { x.tenant_id, x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "tenant_id", "workspace_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tasks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_tenant_id_workspace_id_project_id",
                table: "tasks",
                columns: new[] { "tenant_id", "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_project",
                table: "tasks",
                columns: new[] { "tenant_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_workspace",
                table: "tasks",
                columns: new[] { "tenant_id", "workspace_id" });

            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE tasks TO app_role;");

            migrationBuilder.Sql(
                """
                ALTER TABLE tasks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tasks FORCE ROW LEVEL SECURITY;
                CREATE POLICY tasks_tenant_isolation ON tasks
                    USING (tenant_id = app.uuid_setting('app.current_tenant_id'))
                    WITH CHECK (tenant_id = app.uuid_setting('app.current_tenant_id'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS tasks_tenant_isolation ON tasks;
                ALTER TABLE tasks NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tasks DISABLE ROW LEVEL SECURITY;
                REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLE tasks FROM app_role;
                """);

            migrationBuilder.DropTable(
                name: "tasks");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_projects_tenant_id_workspace_id_id",
                table: "projects");

            migrationBuilder.CreateIndex(
                name: "IX_projects_tenant_id_workspace_id",
                table: "projects",
                columns: new[] { "tenant_id", "workspace_id" });
        }
    }
}
