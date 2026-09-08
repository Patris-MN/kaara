using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <summary>
    /// Phase 8.0.1 — backfill legacy workspace rows whose <c>updated_at_utc</c> was
    /// never populated when metadata columns were introduced.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260830171000_WorkspaceUpdatedAtUtcBackfill")]
    public class WorkspaceUpdatedAtUtcBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE workspaces DISABLE ROW LEVEL SECURITY;

                UPDATE workspaces
                SET updated_at_utc = COALESCE(created_at_utc, NOW() AT TIME ZONE 'UTC')
                WHERE updated_at_utc IS NULL
                   OR updated_at_utc = '-infinity'::timestamptz;

                ALTER TABLE workspaces
                    ALTER COLUMN updated_at_utc SET NOT NULL;

                ALTER TABLE workspaces ENABLE ROW LEVEL SECURITY;
                ALTER TABLE workspaces FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE workspaces DISABLE ROW LEVEL SECURITY;

                ALTER TABLE workspaces
                    ALTER COLUMN updated_at_utc DROP NOT NULL;

                ALTER TABLE workspaces ENABLE ROW LEVEL SECURITY;
                ALTER TABLE workspaces FORCE ROW LEVEL SECURITY;
                """);
        }
    }
}
