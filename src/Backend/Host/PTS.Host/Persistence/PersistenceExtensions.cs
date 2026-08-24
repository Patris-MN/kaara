using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTS.Host.TenantAccess;
using PTS.Modules.Identity;
using PTS.Modules.Tenancy;

namespace PTS.Host.Persistence;

/// <summary>
/// Composition-root wiring for persistence. Called from Program.cs after the
/// module extension methods (AddIdentityModule/AddTenancyModule/...) — this
/// is where the Host completes the "port" each module declared (e.g.
/// Tenancy's <see cref="IMembershipLookup"/>) with a concrete EF Core
/// "adapter", per
/// docs/architecture/decisions/0004-tenancy-ports-and-adapters-for-persistence.md.
///
/// Connects the RUNNING APPLICATION exclusively as app_role — never
/// migrator_role, never a superuser (architecture-charter.md §5). Migrations
/// use a completely separate connection path: <see cref="AppDbContextFactory"/>.
/// </summary>
public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(LocalPostgresConnectionStrings.BuildAppConnectionString(configuration)));

        services.AddScoped<IMembershipLookup, EfMembershipLookup>();
        services.AddScoped<IUserAccountStore, EfUserAccountStore>();
        services.AddScoped<ITenantLifecycleStore, EfTenantLifecycleStore>();
        services.AddScoped<ITenantRlsSessionFactory, TenantRlsSessionFactory>();

        return services;
    }
}
