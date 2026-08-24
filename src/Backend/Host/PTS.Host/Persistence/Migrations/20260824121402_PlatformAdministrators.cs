using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformAdministrators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_administrators",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_administrators", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_platform_administrators_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Not tenant-owned: no RLS. app_role may look up and grant rows
            // (Development bootstrap / future operator tooling). Table owner
            // remains migrator_role.
            migrationBuilder.Sql(
                "GRANT SELECT, INSERT ON TABLE platform_administrators TO app_role;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE SELECT, INSERT ON TABLE platform_administrators FROM app_role;");
            migrationBuilder.DropTable(
                name: "platform_administrators");
        }
    }
}
