using Microsoft.Extensions.Configuration;

namespace PTS.Host.Persistence;

/// <summary>
/// Builds PostgreSQL connection strings for local development, matching
/// infra/docker/.env.example. No secret ever lives in source or appsettings —
/// passwords are only ever read from environment variables that a developer
/// (or CI job) sets from their own `.env`.
///
/// The running application uses <see cref="BuildAppConnectionString"/>
/// (app_role) exclusively. <see cref="BuildMigratorConnectionString"/>
/// (migrator_role) is used only by <see cref="AppDbContextFactory"/> —
/// design-time/CLI migration tooling — and must never be wired into
/// <c>Program.cs</c>'s runtime DI container.
/// </summary>
public static class LocalPostgresConnectionStrings
{
    public static string BuildAppConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("AppDb");
        return string.IsNullOrWhiteSpace(configured)
            ? BuildFromEnvironment(username: "app_role", passwordEnvVar: "PTS_APP_PASSWORD")
            : configured;
    }

    public static string BuildMigratorConnectionString()
        => BuildFromEnvironment(username: "migrator_role", passwordEnvVar: "PTS_MIGRATOR_PASSWORD");

    private static string BuildFromEnvironment(string username, string passwordEnvVar)
    {
        var password = Environment.GetEnvironmentVariable(passwordEnvVar);
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"Environment variable '{passwordEnvVar}' is not set. Copy " +
                "infra/docker/.env.example to infra/docker/.env, set real local " +
                "passwords, export them into your shell, and retry. The running " +
                "application must never fall back to a hardcoded or superuser credential.");
        }

        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("POSTGRES_HOST_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "pts";

        return $"Host={host};Port={port};Database={database};Username={username};Password={password};";
    }
}
