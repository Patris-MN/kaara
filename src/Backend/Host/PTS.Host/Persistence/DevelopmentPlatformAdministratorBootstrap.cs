using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PTS.Modules.Identity;
using PTS.Modules.PlatformAdministration;

namespace PTS.Host.Persistence;

/// <summary>
/// Development-only: creates the configured user (if missing) and grants
/// platform-administrator. Credentials come from environment variables so
/// they never live in source. No-op outside Development or when the
/// variables are unset.
/// </summary>
internal sealed class DevelopmentPlatformAdministratorBootstrap : IHostedService
{
    public const string EmailVariable = "PTS_BOOTSTRAP_PLATFORM_ADMIN_EMAIL";
    public const string PasswordVariable = "PTS_BOOTSTRAP_PLATFORM_ADMIN_PASSWORD";
    public const string DisplayNameVariable = "PTS_BOOTSTRAP_PLATFORM_ADMIN_DISPLAY_NAME";

    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DevelopmentPlatformAdministratorBootstrap> _logger;

    public DevelopmentPlatformAdministratorBootstrap(
        IHostEnvironment environment,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<DevelopmentPlatformAdministratorBootstrap> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return;
        }

        var email = _configuration[EmailVariable];
        var password = _configuration[PasswordVariable];
        var displayName = _configuration[DisplayNameVariable];
        if (string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountStore>();
        var platformAdministrators = scope.ServiceProvider.GetRequiredService<IPlatformAdministratorStore>();

        Guid userId;
        try
        {
            var created = await authentication.RegisterAsync(email, password, displayName, cancellationToken);
            userId = created.Id;
        }
        catch (DuplicateEmailException)
        {
            var credential = await accounts.FindCredentialByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
            if (credential is null)
            {
                _logger.LogWarning("Platform-admin bootstrap email is registered but the credential row was not found.");
                return;
            }

            userId = credential.UserId;
        }

        await platformAdministrators.EnsureAsync(userId, cancellationToken);
        _logger.LogInformation("Ensured platform administrator grant for {Email}.", email);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
