using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskPriorityDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_priority",
                table: "tasks");

            // FORCE RLS applies to the table owner, so the data rewrite
            // must briefly disable RLS in this migrator-only transaction.
            migrationBuilder.Sql(
                """
                ALTER TABLE tasks NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tasks DISABLE ROW LEVEL SECURITY;
                UPDATE tasks SET priority = 'Normal' WHERE priority = 'Medium';
                ALTER TABLE tasks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tasks FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_priority",
                table: "tasks",
                sql: "priority IN ('Low', 'Normal', 'High', 'Urgent')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_priority",
                table: "tasks");

            migrationBuilder.Sql(
                """
                ALTER TABLE tasks NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE tasks DISABLE ROW LEVEL SECURITY;
                UPDATE tasks SET priority = 'Medium' WHERE priority IN ('Normal', 'Urgent');
                ALTER TABLE tasks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tasks FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_priority",
                table: "tasks",
                sql: "priority IN ('Low', 'Medium', 'High')");
        }
    }
}
