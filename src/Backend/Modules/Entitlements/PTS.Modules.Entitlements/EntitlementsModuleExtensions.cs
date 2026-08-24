using Microsoft.Extensions.DependencyInjection;

namespace PTS.Modules.Entitlements;

/// <summary>
/// Composition entry point for the Entitlements module (feature flags, plan limits,
/// and "can this tenant do X" checks derived from subscription state).
/// </summary>
public static class EntitlementsModuleExtensions
{
    /// <summary>
    /// Registers Entitlements module services with the DI container.
    /// Phase 1: architectural placeholder only — no plans, feature flags, or limit
    /// checks are implemented yet. See README.md.
    /// </summary>
    public static IServiceCollection AddEntitlementsModule(this IServiceCollection services)
    {
        return services;
    }
}
