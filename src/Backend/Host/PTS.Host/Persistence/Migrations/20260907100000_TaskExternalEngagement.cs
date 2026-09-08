using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Phase 8.3: denormalized external engagement flag for creator-only delete eligibility.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260907100000_TaskExternalEngagement")]
public partial class TaskExternalEngagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "has_external_engagement",
            table: "tasks",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql(
            """
            UPDATE tasks AS t
            SET has_external_engagement = true
            WHERE EXISTS (
                SELECT 1
                FROM task_comments AS c
                WHERE c.tenant_id = t.tenant_id
                  AND c.task_id = t.id
                  AND c.author_membership_id <> t.created_by_membership_id)
               OR EXISTS (
                SELECT 1
                FROM task_activities AS a
                WHERE a.tenant_id = t.tenant_id
                  AND a.task_id = t.id
                  AND a.actor_membership_id <> t.created_by_membership_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "has_external_engagement",
            table: "tasks");
    }
}
