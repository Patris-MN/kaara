using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Global user notification inbox: display snapshots on notification rows,
/// user-level RLS SELECT/UPDATE policies keyed through active memberships,
/// and inbox query index. Tenant-scoped policies remain for existing endpoints.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260831100000_GlobalNotificationInbox")]
public partial class GlobalNotificationInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "task_title",
            table: "notifications",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "project_name",
            table: "notifications",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE notifications n
            SET task_title = t.title
            FROM tasks t
            WHERE n.task_id IS NOT NULL
              AND n.tenant_id = t.tenant_id
              AND n.task_id = t.id
              AND n.task_title IS NULL;

            UPDATE notifications n
            SET project_name = p.name
            FROM projects p
            WHERE n.project_id IS NOT NULL
              AND n.tenant_id = p.tenant_id
              AND n.project_id = p.id
              AND n.project_name IS NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "ix_notifications_recipient_created",
            table: "notifications",
            columns: new[] { "recipient_membership_id", "created_at_utc" });

        migrationBuilder.Sql(
            """
            CREATE POLICY notifications_user_inbox_select ON notifications
                FOR SELECT
                USING (EXISTS (
                    SELECT 1
                    FROM memberships m
                    WHERE m.tenant_id = notifications.tenant_id
                      AND m.id = notifications.recipient_membership_id
                      AND m.user_id = app.uuid_setting('app.current_user_id')
                      AND m.status = 'Active'
                ));

            CREATE POLICY notifications_user_inbox_update ON notifications
                FOR UPDATE
                USING (EXISTS (
                    SELECT 1
                    FROM memberships m
                    WHERE m.tenant_id = notifications.tenant_id
                      AND m.id = notifications.recipient_membership_id
                      AND m.user_id = app.uuid_setting('app.current_user_id')
                      AND m.status = 'Active'
                ))
                WITH CHECK (EXISTS (
                    SELECT 1
                    FROM memberships m
                    WHERE m.tenant_id = notifications.tenant_id
                      AND m.id = notifications.recipient_membership_id
                      AND m.user_id = app.uuid_setting('app.current_user_id')
                      AND m.status = 'Active'
                ));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP POLICY IF EXISTS notifications_user_inbox_update ON notifications;
            DROP POLICY IF EXISTS notifications_user_inbox_select ON notifications;
            """);

        migrationBuilder.DropIndex(
            name: "ix_notifications_recipient_created",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "project_name",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "task_title",
            table: "notifications");
    }
}
