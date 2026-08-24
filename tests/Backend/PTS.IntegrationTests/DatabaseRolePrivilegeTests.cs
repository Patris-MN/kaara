using Npgsql;

namespace PTS.IntegrationTests;

/// <summary>
/// Step 3 — real-database verification that the running application's role
/// (app_role) cannot bypass Row-Level Security and is not a superuser. This
/// connects as app_role itself (the exact credential the application uses)
/// and asks PostgreSQL directly — it does not trust the provisioning script's
/// own say-so. See also
/// tests/Backend/PTS.Architecture.Tests/DatabaseRoleScriptTests.cs for the
/// static, DB-independent counterpart that runs even without PostgreSQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DatabaseRolePrivilegeTests
{
    private readonly PostgresFixture _fixture;

    public DatabaseRolePrivilegeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task App_role_is_not_superuser_and_cannot_bypass_row_level_security()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var connectionString = PTS.Host.Persistence.LocalPostgresConnectionStrings
            .BuildAppConnectionString(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT rolsuper, rolbypassrls, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname = current_user;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "app_role must exist in pg_roles.");

        var isSuperuser = reader.GetBoolean(0);
        var bypassesRls = reader.GetBoolean(1);
        var canCreateDb = reader.GetBoolean(2);
        var canCreateRole = reader.GetBoolean(3);

        Assert.False(isSuperuser, "app_role must never be SUPERUSER.");
        Assert.False(bypassesRls, "app_role must never have BYPASSRLS.");
        Assert.False(canCreateDb, "app_role must never have CREATEDB.");
        Assert.False(canCreateRole, "app_role must never have CREATEROLE.");
    }

    [SkippableFact]
    public async Task App_role_does_not_own_the_database_or_the_tenant_isolation_table()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var connectionString = PTS.Host.Persistence.LocalPostgresConnectionStrings
            .BuildAppConnectionString(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT tablename, tableowner
            FROM pg_tables
            WHERE tablename IN (
                'tenant_isolation_test_records',
                'workspaces',
                'projects')
            ORDER BY tablename;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var owners = new Dictionary<string, string>();
        while (await reader.ReadAsync())
        {
            owners[reader.GetString(0)] = reader.GetString(1);
        }

        Assert.Contains("tenant_isolation_test_records", owners.Keys);
        foreach (var (table, owner) in owners)
        {
            Assert.NotEqual("app_role", owner);
            Assert.Equal("migrator_role", owner);
        }
    }
}
