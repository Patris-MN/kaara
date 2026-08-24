using System.Text.RegularExpressions;

namespace PTS.Architecture.Tests;

/// <summary>
/// Step 3 — static (no database required) invariant checks on
/// infra/docker/postgres/init/templates/roles.template.sql. These run in any
/// environment, including CI without PostgreSQL, and catch a security-relevant
/// regression in the *authored* script (e.g. someone adding BYPASSRLS or
/// SUPERUSER to app_role) even before it is ever executed.
///
/// This is a text-level complement to, not a replacement for,
/// tests/Backend/PTS.IntegrationTests/DatabaseRolePrivilegeTests.cs, which
/// verifies the *actually applied* privileges against a real, running
/// PostgreSQL instance — a script asserting the right things is not proof it
/// was ever run correctly (see the Phase 2 report for a concrete case where
/// this script had never actually been executed against real PostgreSQL and
/// contained a syntax error only the real database caught).
/// </summary>
public class DatabaseRoleScriptTests
{
    private static string RoleScriptContent
    {
        get
        {
            var repoRoot = FindRepoRoot();
            var path = Path.Combine(repoRoot, "infra", "docker", "postgres", "init", "templates", "roles.template.sql");
            Assert.True(File.Exists(path), $"Expected to find role provisioning script at '{path}'.");
            return File.ReadAllText(path);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PTS.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, $"Could not locate repository root (PTS.slnx) above '{AppContext.BaseDirectory}'.");
        return dir!.FullName;
    }

    /// <summary>
    /// True if <paramref name="keyword"/> (e.g. "BYPASSRLS") appears in
    /// <paramref name="text"/> other than as part of its negated form
    /// "NO" + keyword (e.g. "NOBYPASSRLS") — i.e. a real, affirmative grant
    /// of that privilege, not just the negated keyword containing it as a
    /// substring.
    /// </summary>
    private static bool ContainsBarePrivilegeKeyword(string text, string keyword) =>
        Regex.IsMatch(text, $"(?<!NO){Regex.Escape(keyword)}", RegexOptions.None);

    [Fact]
    public void App_role_creation_never_grants_BYPASSRLS()
    {
        var appRoleStatement = ExtractCreateRoleStatement(RoleScriptContent, "app_role");

        Assert.False(
            ContainsBarePrivilegeKeyword(appRoleStatement, "BYPASSRLS"),
            "app_role's CREATE ROLE statement must never grant bare BYPASSRLS.");
        // Explicitly stated as NOBYPASSRLS for documentation/defense-in-depth,
        // even though it is also PostgreSQL's default for new roles.
        Assert.Contains("NOBYPASSRLS", appRoleStatement, StringComparison.Ordinal);
    }

    [Fact]
    public void App_role_creation_never_requests_SUPERUSER_CREATEDB_or_CREATEROLE()
    {
        var appRoleStatement = ExtractCreateRoleStatement(RoleScriptContent, "app_role");

        Assert.False(ContainsBarePrivilegeKeyword(appRoleStatement, "SUPERUSER"), "app_role must never be SUPERUSER.");
        Assert.False(ContainsBarePrivilegeKeyword(appRoleStatement, "CREATEDB"), "app_role must never have CREATEDB.");
        Assert.False(ContainsBarePrivilegeKeyword(appRoleStatement, "CREATEROLE"), "app_role must never have CREATEROLE.");

        Assert.Contains("NOSUPERUSER", appRoleStatement, StringComparison.Ordinal);
        Assert.Contains("NOCREATEDB", appRoleStatement, StringComparison.Ordinal);
        Assert.Contains("NOCREATEROLE", appRoleStatement, StringComparison.Ordinal);
    }

    private static string ExtractCreateRoleStatement(string sql, string roleName)
    {
        var start = sql.IndexOf($"CREATE ROLE {roleName}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected a 'CREATE ROLE {roleName}' statement.");
        var end = sql.IndexOf(';', start);
        Assert.True(end > start, $"Expected 'CREATE ROLE {roleName}' statement to be terminated by ';'.");
        return sql[start..end];
    }

    [Fact]
    public void Script_contains_a_runtime_sanity_check_that_fails_loudly_if_app_role_regresses()
    {
        var sql = RoleScriptContent;

        Assert.Contains("RAISE EXCEPTION", sql, StringComparison.Ordinal);
        Assert.Contains("rolbypassrls", sql, StringComparison.Ordinal);
        Assert.Contains("rolsuper", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_grants_app_role_only_CONNECT_and_USAGE_at_the_database_and_schema_level()
    {
        var sql = RoleScriptContent;

        // Table-level DML grants belong in migrations (per-table, reviewable),
        // not in this script — this script only grants connect/usage.
        Assert.Contains("GRANT CONNECT ON DATABASE", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT USAGE ON SCHEMA public TO app_role", sql, StringComparison.Ordinal);

        var linesGrantingAppRole = sql
            .Split('\n')
            .Where(line => line.Contains("app_role", StringComparison.Ordinal) && line.Contains("GRANT", StringComparison.Ordinal));

        Assert.All(linesGrantingAppRole, line =>
            Assert.DoesNotContain("GRANT ALL", line, StringComparison.Ordinal));
    }

    [Fact]
    public void Script_revokes_default_table_privileges_so_future_grants_must_be_explicit()
    {
        var sql = RoleScriptContent;

        Assert.Contains("ALTER DEFAULT PRIVILEGES", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON TABLES FROM PUBLIC", sql, StringComparison.Ordinal);
    }
}
