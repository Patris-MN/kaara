using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PTS.SharedKernel.Identity;

namespace PTS.Modules.Identity;

/// <summary>
/// Composition entry point for the Identity module. The Host calls this during startup;
/// no other module may reference Identity types directly.
/// </summary>
public static class IdentityModuleExtensions
{
    /// <summary>
    /// Registers Identity module services. Persistence adapters
    /// (<see cref="IUserAccountStore"/>) and <see cref="ICurrentUser"/> are
    /// completed by the Host — Identity never references EF Core providers or HTTP.
    /// </summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        return services;
    }
}
