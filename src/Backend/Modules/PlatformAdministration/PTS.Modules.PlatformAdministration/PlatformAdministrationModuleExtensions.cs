using Microsoft.Extensions.DependencyInjection;

namespace PTS.Modules.PlatformAdministration;

/// <summary>
/// Composition entry point for the Platform Administration module (internal
/// operator/staff capabilities, distinct from any tenant-level role).
/// </summary>
public static class PlatformAdministrationModuleExtensions
{
    /// <summary>
    /// Registers Platform Administration module services. The persistence
    /// adapter for <see cref="IPlatformAdministratorStore"/> is completed by
    /// the Host.
    /// </summary>
    public static IServiceCollection AddPlatformAdministrationModule(this IServiceCollection services)
    {
        return services;
    }
}
