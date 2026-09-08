using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Repairs malformed notification task_title snapshots and backfills missing
/// titles from live tasks where still available.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260901131000_NotificationTaskTitleRepair")]
public partial class NotificationTaskTitleRepair : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE notifications
            SET task_title = NULL
            WHERE task_title IS NOT NULL
              AND lower(btrim(task_title)) IN (
                  'notification',
                  'task',
                  'unknown',
                  'a task',
                  'taskassigned',
                  'taskreassigned',
                  'taskcommentadded',
                  'taskprioritychanged',
                  'taskdeadlinechanged',
                  'taskstatuschanged',
                  'tasktagchanged',
                  'taskupdated',
                  'taskclosed',
                  'taskreopened'
              );

            UPDATE notifications n
            SET task_title = t.title
            FROM tasks t
            WHERE n.task_id IS NOT NULL
              AND n.tenant_id = t.tenant_id
              AND n.task_id = t.id
              AND (n.task_title IS NULL OR btrim(n.task_title) = '');

            UPDATE notifications
            SET task_title = NULL
            WHERE task_title IS NOT NULL
              AND btrim(task_title) = '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data repair is not reversed.
    }
}
