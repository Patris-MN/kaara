using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Grants app_role DML on invitation tables. RLS policies were added in
/// TenantInvitationTokens but table privileges were omitted.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260901100000_TenantInvitationTableGrants")]
public class TenantInvitationTableGrants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE tenant_invitations, invitation_workspace_grants TO app_role;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLE tenant_invitations, invitation_workspace_grants FROM app_role;
            """);
    }
}
