using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTS.Host.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthenticationAndIdentityRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_credentials",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_credentials", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_credentials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_credentials_email",
                table: "user_credentials",
                column: "email",
                unique: true);

            migrationBuilder.Sql(
                "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE user_credentials TO app_role;");

            migrationBuilder.Sql(
                """
                CREATE SCHEMA IF NOT EXISTS app;
                GRANT USAGE ON SCHEMA app TO app_role;

                CREATE OR REPLACE FUNCTION app.uuid_setting(p_name text)
                RETURNS uuid
                LANGUAGE plpgsql
                STABLE
                AS $$
                DECLARE
                    raw text := current_setting(p_name, true);
                BEGIN
                    IF raw IS NULL OR btrim(raw) = '' THEN
                        RETURN NULL;
                    END IF;
                    RETURN raw::uuid;
                EXCEPTION WHEN invalid_text_representation THEN
                    RETURN NULL;
                END;
                $$;

                ALTER FUNCTION app.uuid_setting(text) OWNER TO migrator_role;
                GRANT EXECUTE ON FUNCTION app.uuid_setting(text) TO app_role;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE users ENABLE ROW LEVEL SECURITY;
                ALTER TABLE users FORCE ROW LEVEL SECURITY;
                CREATE POLICY users_self ON users
                    USING (id = app.uuid_setting('app.current_user_id'))
                    WITH CHECK (id = app.uuid_setting('app.current_user_id'));
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE memberships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE memberships FORCE ROW LEVEL SECURITY;
                CREATE POLICY memberships_self ON memberships
                    USING (user_id = app.uuid_setting('app.current_user_id'))
                    WITH CHECK (user_id = app.uuid_setting('app.current_user_id'));
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE tenants ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tenants FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenants_select ON tenants
                    FOR SELECT
                    USING (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = tenants.id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Active'
                    ));
                CREATE POLICY tenants_insert ON tenants
                    FOR INSERT
                    WITH CHECK (true);
                CREATE POLICY tenants_update ON tenants
                    FOR UPDATE
                    USING (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = tenants.id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Active'
                    ))
                    WITH CHECK (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = tenants.id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Active'
                    ));
                CREATE POLICY tenants_delete ON tenants
                    FOR DELETE
                    USING (EXISTS (
                        SELECT 1
                        FROM memberships m
                        WHERE m.tenant_id = tenants.id
                          AND m.user_id = app.uuid_setting('app.current_user_id')
                          AND m.status = 'Active'
                    ));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenants_delete ON tenants;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenants_update ON tenants;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenants_insert ON tenants;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenants_select ON tenants;");
            migrationBuilder.Sql("ALTER TABLE tenants NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE tenants DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP POLICY IF EXISTS memberships_self ON memberships;");
            migrationBuilder.Sql("ALTER TABLE memberships NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE memberships DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP POLICY IF EXISTS users_self ON users;");
            migrationBuilder.Sql("ALTER TABLE users NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE users DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS app.uuid_setting(text);");
            migrationBuilder.Sql("REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLE user_credentials FROM app_role;");

            migrationBuilder.DropTable(
                name: "user_credentials");
        }
    }
}
