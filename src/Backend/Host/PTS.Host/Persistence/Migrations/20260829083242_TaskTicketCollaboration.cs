using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskTicketCollaboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_status",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            // FORCE RLS applies to the table owner, so the data rewrite
            // must briefly disable RLS in this migrator-only transaction.
            migrationBuilder.Sql(
                """
                ALTER TABLE tasks NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tasks DISABLE ROW LEVEL SECURITY;
                UPDATE tasks SET status = 'Open' WHERE status = 'Todo';
                UPDATE tasks SET status = 'Closed' WHERE status = 'Done';
                ALTER TABLE tasks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tasks FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_membership_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE tasks NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tasks DISABLE ROW LEVEL SECURITY;
                ALTER TABLE memberships NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE memberships DISABLE ROW LEVEL SECURITY;
                UPDATE tasks AS task
                SET created_by_membership_id = COALESCE(
                    (
                        SELECT membership.id
                        FROM memberships AS membership
                        WHERE membership.tenant_id = task.tenant_id
                          AND membership.role = 'Owner'
                        ORDER BY membership.id
                        LIMIT 1
                    ),
                    (
                        SELECT membership.id
                        FROM memberships AS membership
                        WHERE membership.tenant_id = task.tenant_id
                        ORDER BY membership.id
                        LIMIT 1
                    )
                )
                WHERE created_by_membership_id IS NULL;
                ALTER TABLE memberships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE memberships FORCE ROW LEVEL SECURITY;
                ALTER TABLE tasks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tasks FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "created_by_membership_id",
                table: "tasks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "task_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    old_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_activities", x => x.id);
                    table.CheckConstraint("ck_task_activities_event_type", "event_type IN ('TaskCreated','TitleChanged','DescriptionChanged','PriorityChanged','DeadlineChanged','StatusChanged','AssigneeChanged','TagAdded','TagRemoved','CommentAdded','CommentEdited','CommentDeleted','TaskReopened')");
                    table.ForeignKey(
                        name: "fk_task_activities_memberships_tenant_id_actor_membership_id",
                        columns: x => new { x.tenant_id, x.actor_membership_id },
                        principalTable: "memberships",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_activities_tasks_tenant_id_task_id",
                        columns: x => new { x.tenant_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_comments_memberships_tenant_id_author_membership_id",
                        columns: x => new { x.tenant_id, x.author_membership_id },
                        principalTable: "memberships",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_task_comments_tasks_tenant_id_task_id",
                        columns: x => new { x.tenant_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_read_states",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_viewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_read_states", x => new { x.tenant_id, x.task_id, x.membership_id });
                    table.ForeignKey(
                        name: "fk_task_read_states_memberships_tenant_id_membership_id",
                        columns: x => new { x.tenant_id, x.membership_id },
                        principalTable: "memberships",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_read_states_tasks_tenant_id_task_id",
                        columns: x => new { x.tenant_id, x.task_id },
                        principalTable: "tasks",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tasks_tenant_id_created_by_membership_id",
                table: "tasks",
                columns: new[] { "tenant_id", "created_by_membership_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_status",
                table: "tasks",
                sql: "status IN ('Open', 'InProgress', 'Waiting', 'Resolved', 'Closed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('TaskAssigned','TaskReassigned','TaskCommentAdded','TaskPriorityChanged','TaskDeadlineChanged','TaskStatusChanged','TaskTagChanged','TaskUpdated','TaskClosed','TaskReopened')");

            migrationBuilder.CreateIndex(
                name: "IX_task_activities_tenant_id_actor_membership_id",
                table: "task_activities",
                columns: new[] { "tenant_id", "actor_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_activities_tenant_task_created",
                table: "task_activities",
                columns: new[] { "tenant_id", "task_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_tenant_id_author_membership_id",
                table: "task_comments",
                columns: new[] { "tenant_id", "author_membership_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_comments_tenant_task_created",
                table: "task_comments",
                columns: new[] { "tenant_id", "task_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_task_read_states_tenant_id_membership_id",
                table: "task_read_states",
                columns: new[] { "tenant_id", "membership_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_memberships_tenant_id_created_by_membership_id",
                table: "tasks",
                columns: new[] { "tenant_id", "created_by_membership_id" },
                principalTable: "memberships",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE task_comments, task_read_states TO app_role;");
            migrationBuilder.Sql(
                "GRANT SELECT, INSERT ON TABLE task_activities TO app_role;");

            migrationBuilder.Sql(
                """
                ALTER TABLE task_comments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE task_comments FORCE ROW LEVEL SECURITY;
                CREATE POLICY task_comments_tenant_isolation ON task_comments
                    USING (tenant_id = app.uuid_setting('app.current_tenant_id'))
                    WITH CHECK (tenant_id = app.uuid_setting('app.current_tenant_id'));

                ALTER TABLE task_activities ENABLE ROW LEVEL SECURITY;
                ALTER TABLE task_activities FORCE ROW LEVEL SECURITY;
                CREATE POLICY task_activities_tenant_isolation ON task_activities
                    USING (tenant_id = app.uuid_setting('app.current_tenant_id'))
                    WITH CHECK (tenant_id = app.uuid_setting('app.current_tenant_id'));

                ALTER TABLE task_read_states ENABLE ROW LEVEL SECURITY;
                ALTER TABLE task_read_states FORCE ROW LEVEL SECURITY;
                CREATE POLICY task_read_states_own ON task_read_states
                    USING (
                        tenant_id = app.uuid_setting('app.current_tenant_id')
                        AND membership_id = app.uuid_setting('app.current_membership_id')
                    )
                    WITH CHECK (
                        tenant_id = app.uuid_setting('app.current_tenant_id')
                        AND membership_id = app.uuid_setting('app.current_membership_id')
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP POLICY IF EXISTS task_read_states_own ON task_read_states;
                ALTER TABLE task_read_states NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE task_read_states DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS task_activities_tenant_isolation ON task_activities;
                ALTER TABLE task_activities NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE task_activities DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS task_comments_tenant_isolation ON task_comments;
                ALTER TABLE task_comments NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE task_comments DISABLE ROW LEVEL SECURITY;
                REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLE task_comments, task_read_states FROM app_role;
                REVOKE SELECT, INSERT ON TABLE task_activities FROM app_role;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_tasks_memberships_tenant_id_created_by_membership_id",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "task_activities");

            migrationBuilder.DropTable(
                name: "task_comments");

            migrationBuilder.DropTable(
                name: "task_read_states");

            migrationBuilder.DropIndex(
                name: "IX_tasks_tenant_id_created_by_membership_id",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_status",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "created_by_membership_id",
                table: "tasks");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_status",
                table: "tasks",
                sql: "status IN ('Todo', 'InProgress', 'Done')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('TaskAssigned')");
        }
    }
}
