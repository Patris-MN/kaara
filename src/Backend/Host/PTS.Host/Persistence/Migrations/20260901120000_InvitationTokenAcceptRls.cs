using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTS.Host.Persistence;

#nullable disable

namespace PTS.Host.Persistence.Migrations;

/// <summary>
/// Allows a pending invitation row to be consumed (mark used) when the caller
/// holds the matching token hash GUC during acceptance.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260901120000_InvitationTokenAcceptRls")]
public class InvitationTokenAcceptRls : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE POLICY tenant_invitations_token_accept ON tenant_invitations
                FOR UPDATE TO app_role
                USING (
                    token_hash = NULLIF(current_setting('app.invitation_token_hash', true), '')
                    AND used_at_utc IS NULL
                    AND revoked_at_utc IS NULL
                    AND expires_at_utc > now()
                )
                WITH CHECK (
                    token_hash = NULLIF(current_setting('app.invitation_token_hash', true), '')
                );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_invitations_token_accept ON tenant_invitations;");
    }
}
