using Microsoft.Extensions.DependencyInjection;
using PTS.SharedKernel.Tenancy;

namespace PTS.Modules.Tenancy;

/// <summary>
/// Composition entry point for the Tenancy &amp; Membership module.
/// </summary>
public static class TenancyModuleExtensions
{
    /// <summary>
    /// Registers Tenancy module services with the DI container.
    ///
    /// Deliberately does NOT register <see cref="IMembershipLookup"/> — that
    /// port is implemented against EF Core by the Host (see
    /// PTS.Host.Persistence.EfMembershipLookup / PersistenceExtensions), which
    /// must register it before anything resolves <see cref="ITenantContextResolver"/>.
    /// </summary>
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextEstablisher>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextResolver, TenantContextResolver>();
        services.AddScoped<ITenantLifecycleService, TenantLifecycleService>();

        return services;
    }
}
