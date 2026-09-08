using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <summary>Phase 8.0 — optional workspace description, start date, and updated timestamp.</summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260830170000_WorkspaceMetadata")]
    public class WorkspaceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "workspaces",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "workspaces",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc",
                table: "workspaces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE workspaces
                SET updated_at_utc = created_at_utc
                WHERE updated_at_utc IS NULL;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at_utc",
                table: "workspaces",
                type: "timestamp with time zone",
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "description", table: "workspaces");
            migrationBuilder.DropColumn(name: "start_date", table: "workspaces");
            migrationBuilder.DropColumn(name: "updated_at_utc", table: "workspaces");
        }
    }
}
