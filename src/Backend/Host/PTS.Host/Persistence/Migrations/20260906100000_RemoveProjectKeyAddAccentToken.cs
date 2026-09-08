using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Phase 8.2A: retire Project Key / task sequence references; add optional accent token.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260906100000_RemoveProjectKeyAddAccentToken")]
public partial class RemoveProjectKeyAddAccentToken : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_tasks_tenant_project_sequence",
            table: "tasks");

        migrationBuilder.DropIndex(
            name: "ux_projects_tenant_key_normalized",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "sequence_number",
            table: "tasks");

        migrationBuilder.DropColumn(
            name: "next_task_sequence",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "key_normalized",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "key",
            table: "projects");

        migrationBuilder.AddColumn<string>(
            name: "accent_token",
            table: "projects",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "accent_token",
            table: "projects");

        migrationBuilder.AddColumn<string>(
            name: "key",
            table: "projects",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            defaultValue: "PR");

        migrationBuilder.AddColumn<string>(
            name: "key_normalized",
            table: "projects",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            defaultValue: "PR");

        migrationBuilder.AddColumn<int>(
            name: "next_task_sequence",
            table: "projects",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "sequence_number",
            table: "tasks",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "ux_projects_tenant_key_normalized",
            table: "projects",
            columns: new[] { "tenant_id", "key_normalized" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_tasks_tenant_project_sequence",
            table: "tasks",
            columns: new[] { "tenant_id", "project_id", "sequence_number" },
            unique: true);
    }
}
