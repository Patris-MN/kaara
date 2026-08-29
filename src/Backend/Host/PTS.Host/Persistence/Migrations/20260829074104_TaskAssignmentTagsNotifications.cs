using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskAssignmentTagsNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assigned_membership_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_tasks_tenant_id_id",
                table: "tasks",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_type", "type IN ('TaskAssigned')");
                    table.ForeignKey(
                        name: "fk_notifications_memberships_tenant_id_recipient_membership_id",
                        columns: x => new { x.tenant_id, x.recipient_membership_id },
                        principalTable: "memberships",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notifications_tasks_tenant_id_task_id",
                        columns: x => new { x.tenant_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_notifications_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_by_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                    table.UniqueConstraint("ak_tags_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_tags_memberships_tenant_id_created_by_membership_id",
                        columns: x => new { x.tenant_id, x.created_by_membership_id },
                        principalTable: "memberships",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tags_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_tags",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_tags", x => new { x.tenant_id, x.task_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_task_tags_tags_tenant_id_tag_id",
                        columns: x => new { x.tenant_id, x.tag_id },
                        principalTable: "tags",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_tags_tasks_tenant_id_task_id",
                        columns: x => new { x.tenant_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_tenant_assignee",
                table: "tasks",
                columns: new[] { "tenant_id", "assigned_membership_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_tenant_id_task_id",
                table: "notifications",
                columns: new[] { "tenant_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tenant_recipient_unread",
                table: "notifications",
                columns: new[] { "tenant_id", "recipient_membership_id", "is_read" });

            migrationBuilder.CreateIndex(
                name: "IX_tags_tenant_id_created_by_membership_id",
                table: "tags",
                columns: new[] { "tenant_id", "created_by_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tags_tenant_normalized_name",
                table: "tags",
                columns: new[] { "tenant_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_task_tags_tenant_id_tag_id",
                table: "task_tags",
                columns: new[] { "tenant_id", "tag_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_memberships_tenant_id_assigned_membership_id",
                table: "tasks",
                columns: new[] { "tenant_id", "assigned_membership_id" },
                principalTable: "memberships",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE tags, task_tags, notifications TO app_role;");

            migrationBuilder.Sql(
                """
                ALTER TABLE tags ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tags FORCE ROW LEVEL SECURITY;
                CREATE POLICY tags_tenant_isolation ON tags
                    USING (tenant_id = app.uuid_setting('app.current_tenant_id'))
                    WITH CHECK (tenant_id = app.uuid_setting('app.current_tenant_id'));

                ALTER TABLE task_tags ENABLE ROW LEVEL SECURITY;
                ALTER TABLE task_tags FORCE ROW LEVEL SECURITY;
                CREATE POLICY task_tags_tenant_isolation ON task_tags
                    USING (tenant_id = app.uuid_setting('app.current_tenant_id'))
                    WITH CHECK (tenant_id = app.uuid_setting('app.current_tenant_id'));

                ALTER TABLE notifications ENABLE ROW LEVEL SECURITY;
                ALTER TABLE notifications FORCE ROW LEVEL SECURITY;
                CREATE POLICY notifications_recipient_select ON notifications
                    FOR SELECT
                    USING (
                        tenant_id = app.uuid_setting('app.current_tenant_id')
                        AND recipient_membership_id = app.uuid_setting('app.current_membership_id')
                    );
                CREATE POLICY notifications_tenant_insert ON notifications
                    FOR INSERT
                    WITH CHECK (tenant_id = app.uuid_setting('app.current_tenant_id'));
                CREATE POLICY notifications_recipient_update ON notifications
                    FOR UPDATE
                    USING (
                        tenant_id = app.uuid_setting('app.current_tenant_id')
                        AND recipient_membership_id = app.uuid_setting('app.current_membership_id')
                    )
                    WITH CHECK (
                        tenant_id = app.uuid_setting('app.current_tenant_id')
                        AND recipient_membership_id = app.uuid_setting('app.current_membership_id')
                    );
                CREATE POLICY notifications_recipient_delete ON notifications
                    FOR DELETE
                    USING (
                        tenant_id = app.uuid_setting('app.current_tenant_id')
                        AND recipient_membership_id = app.uuid_setting('app.current_membership_id')
                    );

                CREATE POLICY memberships_tenant_peer_select ON memberships
                    FOR SELECT
                    USING (
                        tenant_id = app.uuid_setting('app.current_tenant_id')
                        AND app.uuid_setting('app.current_tenant_id') IS NOT NULL
                    );
                CREATE POLICY users_tenant_peer_select ON users
                    FOR SELECT
                    USING (
                        app.uuid_setting('app.current_tenant_id') IS NOT NULL
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
                DROP POLICY IF EXISTS users_tenant_peer_select ON users;
                DROP POLICY IF EXISTS memberships_tenant_peer_select ON memberships;
                DROP POLICY IF EXISTS notifications_recipient_delete ON notifications;
                DROP POLICY IF EXISTS notifications_recipient_update ON notifications;
                DROP POLICY IF EXISTS notifications_tenant_insert ON notifications;
                DROP POLICY IF EXISTS notifications_recipient_select ON notifications;
                ALTER TABLE notifications NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE notifications DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS task_tags_tenant_isolation ON task_tags;
                ALTER TABLE task_tags NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE task_tags DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tags_tenant_isolation ON tags;
                ALTER TABLE tags NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tags DISABLE ROW LEVEL SECURITY;
                REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLE tags, task_tags, notifications FROM app_role;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_memberships_tenant_id_assigned_membership_id",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "task_tags");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_tasks_tenant_id_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ix_tasks_tenant_assignee",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "assigned_membership_id",
                table: "tasks");
        }
    }
}
