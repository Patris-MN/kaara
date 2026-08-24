using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PTS.Host.Persistence;
using PTS.Modules.Identity;
using PTS.Modules.Tenancy;
using PTS.Modules.WorkManagement;

namespace PTS.IntegrationTests;

/// <summary>
/// Builds the exact same DI composition Program.cs uses (AddTenancyModule +
/// AddPersistence) against a real PostgreSQL database, and probes whether
/// that database is actually reachable as app_role.
///
/// If it isn't reachable, <see cref="DatabaseAvailable"/> is false and every
/// test in this assembly calls <c>Skip.IfNot</c> (via <c>[SkippableFact]</c>)
/// rather than reporting a false PASS — per the Phase 2 rule: never claim a
/// database/RLS test passed unless it actually ran against PostgreSQL.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private ServiceProvider? _serviceProvider;

    public bool DatabaseAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public IServiceProvider Services => _serviceProvider
        ?? throw new InvalidOperationException("PostgresFixture was not initialized or the database was unavailable.");

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder().Build();

        string connectionString;
        try
        {
            connectionString = LocalPostgresConnectionStrings.BuildAppConnectionString(configuration);
        }
        catch (InvalidOperationException ex)
        {
            DatabaseAvailable = false;
            UnavailableReason = ex.Message;
            return;
        }

        try
        {
            await using var probe = new NpgsqlConnection(connectionString);
            await probe.OpenAsync();
            await probe.CloseAsync();
        }
        catch (Exception ex)
        {
            DatabaseAvailable = false;
            UnavailableReason = $"Could not connect to PostgreSQL as app_role: {ex.Message}";
            return;
        }

        var services = new ServiceCollection();
        services.AddIdentityModule();
        services.AddTenancyModule();
        services.AddWorkManagementModule();
        services.AddPersistence(configuration);
        services.AddScoped<TestCurrentUser>();
        services.AddScoped<PTS.SharedKernel.Identity.ICurrentUser>(sp => sp.GetRequiredService<TestCurrentUser>());
        services.AddSingleton<TestDataFactory>();
        _serviceProvider = services.BuildServiceProvider();

        DatabaseAvailable = true;
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
