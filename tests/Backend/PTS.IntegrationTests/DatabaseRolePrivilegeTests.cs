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
                'projects',
                'workspace_access',
                'tasks',
                'tags',
                'task_tags',
                'notifications',
                'task_comments',
                'task_activities',
                'task_read_states',
                'platform_administrators')
            ORDER BY tablename;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var owners = new Dictionary<string, string>();
        while (await reader.ReadAsync())
        {
            owners[reader.GetString(0)] = reader.GetString(1);
        }

        Assert.Contains("tenant_isolation_test_records", owners.Keys);
        Assert.Contains("platform_administrators", owners.Keys);
        Assert.Contains("workspace_access", owners.Keys);
        Assert.Contains("tasks", owners.Keys);
        Assert.Contains("tags", owners.Keys);
        Assert.Contains("task_tags", owners.Keys);
        Assert.Contains("notifications", owners.Keys);
        Assert.Contains("task_comments", owners.Keys);
        Assert.Contains("task_activities", owners.Keys);
        Assert.Contains("task_read_states", owners.Keys);
        foreach (var (table, owner) in owners)
        {
            Assert.NotEqual("app_role", owner);
            Assert.Equal("migrator_role", owner);
        }
    }

    [SkippableFact]
    public async Task Workspace_access_has_force_rls_and_app_role_dml_without_ownership()
    {
        Skip.IfNot(_fixture.DatabaseAvailable, _fixture.UnavailableReason);

        var connectionString = PTS.Host.Persistence.LocalPostgresConnectionStrings
            .BuildAppConnectionString(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var catalog = connection.CreateCommand())
        {
            catalog.CommandText =
                """
                SELECT c.relrowsecurity, c.relforcerowsecurity
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relname IN ('workspace_access', 'tasks', 'tags', 'task_tags', 'notifications', 'task_comments', 'task_activities', 'task_read_states')
                ORDER BY c.relname;
                """;
            await using var reader = await catalog.ExecuteReaderAsync();
            var found = 0;
            while (await reader.ReadAsync())
            {
                found++;
                Assert.True(reader.GetBoolean(0), "tenant-owned tables must have RLS enabled.");
                Assert.True(reader.GetBoolean(1), "tenant-owned tables must have FORCE RLS.");
            }
            Assert.Equal(8, found);
        }

        await using var grants = connection.CreateCommand();
        grants.CommandText =
            """
            SELECT
                has_table_privilege('workspace_access', 'SELECT'),
                has_table_privilege('workspace_access', 'INSERT'),
                has_table_privilege('workspace_access', 'UPDATE'),
                has_table_privilege('workspace_access', 'DELETE'),
                has_table_privilege('tasks', 'SELECT'),
                has_table_privilege('tasks', 'INSERT'),
                has_table_privilege('tasks', 'UPDATE'),
                has_table_privilege('tasks', 'DELETE'),
                has_table_privilege('tags', 'SELECT'),
                has_table_privilege('tags', 'INSERT'),
                has_table_privilege('tags', 'UPDATE'),
                has_table_privilege('tags', 'DELETE'),
                has_table_privilege('task_tags', 'SELECT'),
                has_table_privilege('task_tags', 'INSERT'),
                has_table_privilege('task_tags', 'UPDATE'),
                has_table_privilege('task_tags', 'DELETE'),
                has_table_privilege('notifications', 'SELECT'),
                has_table_privilege('notifications', 'INSERT'),
                has_table_privilege('notifications', 'UPDATE'),
                has_table_privilege('notifications', 'DELETE'),
                has_table_privilege('task_comments', 'SELECT'),
                has_table_privilege('task_comments', 'INSERT'),
                has_table_privilege('task_comments', 'UPDATE'),
                has_table_privilege('task_comments', 'DELETE'),
                has_table_privilege('task_activities', 'SELECT'),
                has_table_privilege('task_activities', 'INSERT'),
                has_table_privilege('task_read_states', 'SELECT'),
                has_table_privilege('task_read_states', 'INSERT'),
                has_table_privilege('task_read_states', 'UPDATE'),
                has_table_privilege('task_read_states', 'DELETE'),
                has_table_privilege('task_activities', 'UPDATE'),
                has_table_privilege('task_activities', 'DELETE');
            """;
        await using var grantReader = await grants.ExecuteReaderAsync();
        Assert.True(await grantReader.ReadAsync());
        for (var i = 0; i < 30; i++)
        {
            Assert.True(grantReader.GetBoolean(i));
        }

        Assert.False(grantReader.GetBoolean(30));
        Assert.False(grantReader.GetBoolean(31));
    }
}
