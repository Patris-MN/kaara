using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityAndTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_isolation_test_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_isolation_test_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_isolation_test_records_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.ForeignKey(
                        name: "fk_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_memberships_tenant_id",
                table: "memberships",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id_tenant_id",
                table: "memberships",
                columns: new[] { "user_id", "tenant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_isolation_test_records_tenant_id",
                table: "tenant_isolation_test_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            // ---------------------------------------------------------------
            // Least-privilege runtime grants (architecture-charter.md §5).
            // migrator_role (running this migration) owns the schema and can
            // do anything; app_role (the running application) gets ONLY the
            // DML it needs on these specific tables — nothing implicit, and
            // no DDL whatsoever. See infra/docker/postgres/init/templates/roles.template.sql,
            // which already REVOKEs default table privileges from app_role so
            // that every grant below has to be explicit and reviewable.
            // ---------------------------------------------------------------
            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE users, tenants, memberships, tenant_isolation_test_records TO app_role;");

            // ---------------------------------------------------------------
            // Row-Level Security proof-of-concept (architecture-charter.md §4.2,
            // decisions/0002-row-level-security-for-tenant-isolation.md).
            //
            // tenant_isolation_test_records is the ONLY table RLS-protected in
            // this phase — see the Phase 2 report's "Intentionally Not
            // Implemented" section for why users/tenants/memberships are not
            // (memberships in particular needs its own, different RLS design,
            // since the membership lookup itself must run BEFORE any tenant
            // context can exist).
            //
            // FORCE ROW LEVEL SECURITY matters as much as ENABLE: without FORCE,
            // the table owner (migrator_role) — and, notably, this migration's
            // own connection — would bypass RLS entirely. Forcing it means
            // even a superuser-owned connection must satisfy the policy.
            //
            // current_setting(..., true) uses missing_ok=true. Empirically
            // verified against a real PostgreSQL 18 instance, this fails
            // CLOSED in BOTH of the two ways a transaction can end up with no
            // tenant context, but via two different mechanisms:
            //   - A connection that has NEVER had this custom GUC referenced
            //     before: current_setting(...) returns SQL NULL, so
            //     "tenant_id = NULL" is never true -> zero rows, silently.
            //   - A pooled connection REUSED from an earlier committed
            //     transaction that DID set it (i.e. the realistic
            //     connection-pooling case): PostgreSQL's reset value for a
            //     custom GUC already created in this backend is '' (empty
            //     string), not NULL, once the SET LOCAL's transaction ends.
            //     current_setting(...) then returns '', and casting '' to
            //     uuid raises a hard Postgres error (22P02) instead of
            //     matching zero rows.
            // Either way, no cross-tenant row is ever returned if the
            // application ever fails to call set_config for a given
            // transaction — see tests/Backend/PTS.IntegrationTests for the
            // test that exercises the second (realistic, pool-reuse) case.
            // ---------------------------------------------------------------
            migrationBuilder.Sql(
                "ALTER TABLE tenant_isolation_test_records ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "ALTER TABLE tenant_isolation_test_records FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                """
                CREATE POLICY tenant_isolation_test_records_tenant_isolation
                ON tenant_isolation_test_records
                USING (tenant_id = current_setting('app.current_tenant_id', true)::uuid)
                WITH CHECK (tenant_id = current_setting('app.current_tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP POLICY IF EXISTS tenant_isolation_test_records_tenant_isolation ON tenant_isolation_test_records;");
            migrationBuilder.Sql(
                "ALTER TABLE tenant_isolation_test_records NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "ALTER TABLE tenant_isolation_test_records DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLE users, tenants, memberships, tenant_isolation_test_records FROM app_role;");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "tenant_isolation_test_records");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
