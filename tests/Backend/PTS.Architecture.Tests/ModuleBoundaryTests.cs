using System.Reflection;

namespace PTS.Architecture.Tests;

/// <summary>
/// Mechanically enforces the modular-monolith dependency rules described in
/// docs/architecture/architecture-charter.md and .cursor/rules/00-modular-monolith-architecture.mdc:
///   - Modules never reference other modules directly.
///   - PTS.SharedKernel never references a module or the Host.
///   - PTS.Host (the composition root) references every module.
///   - Modules never reference a concrete database provider (e.g. Npgsql) —
///     see docs/architecture/decisions/0004-ports-and-adapters-for-persistence.md.
///     Modules may reference the provider-agnostic Microsoft.EntityFrameworkCore
///     (.Relational) packages for entity-shape configuration
///     (IEntityTypeConfiguration&lt;T&gt;, ToTable, HasColumnName, ...), but the
///     concrete provider and the DbContext itself are wired only in PTS.Host.
///
/// These checks use plain reflection over compiled assemblies rather than a
/// third-party architecture-testing library, per the project rule against adding
/// dependencies without a current, documented need.
/// </summary>
public class ModuleBoundaryTests
{
    private const string ModuleAssemblyPrefix = "PTS.Modules.";
    private const string SharedKernelAssemblyName = "PTS.SharedKernel";
    private const string HostAssemblyName = "PTS.Host";

    /// <summary>
    /// Concrete ADO.NET/EF Core provider assemblies. Any of these showing up
    /// as a *direct* reference of a module means that module has taken a
    /// hard dependency on "we run on PostgreSQL", which is exactly what the
    /// ports/adapters split (module owns entity shape; Host owns the
    /// provider) exists to prevent — see ADR-0004.
    /// </summary>
    private static readonly string[] ConcreteDatabaseProviderAssemblyNames =
    [
        "Npgsql",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "System.Data.SqlClient",
        "Microsoft.Data.SqlClient",
    ];

    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(PTS.Modules.Identity.ModuleAssemblyMarker).Assembly,
        typeof(PTS.Modules.Tenancy.ModuleAssemblyMarker).Assembly,
        typeof(PTS.Modules.WorkManagement.ModuleAssemblyMarker).Assembly,
        typeof(PTS.Modules.Entitlements.ModuleAssemblyMarker).Assembly,
        typeof(PTS.Modules.Billing.ModuleAssemblyMarker).Assembly,
        typeof(PTS.Modules.Storage.ModuleAssemblyMarker).Assembly,
        typeof(PTS.Modules.PlatformAdministration.ModuleAssemblyMarker).Assembly,
        typeof(PTS.Modules.Audit.ModuleAssemblyMarker).Assembly,
    ];

    private static readonly Assembly SharedKernelAssembly =
        typeof(PTS.SharedKernel.ModuleAssemblyMarker).Assembly;

    public static IEnumerable<object[]> AllModuleAssemblies() =>
        ModuleAssemblies.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(AllModuleAssemblies))]
    public void Module_must_not_reference_another_module(Assembly moduleAssembly)
    {
        var otherModuleReferences = moduleAssembly.GetReferencedAssemblies()
            .Where(referenced =>
                referenced.Name is not null &&
                referenced.Name.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal) &&
                referenced.Name != moduleAssembly.GetName().Name)
            .Select(referenced => referenced.Name)
            .ToList();

        Assert.True(
            otherModuleReferences.Count == 0,
            $"{moduleAssembly.GetName().Name} must not reference other modules directly, " +
            $"but references: {string.Join(", ", otherModuleReferences)}");
    }

    [Fact]
    public void SharedKernel_must_not_reference_any_module_or_the_host()
    {
        var forbiddenReferences = SharedKernelAssembly.GetReferencedAssemblies()
            .Where(referenced =>
                referenced.Name is not null &&
                (referenced.Name.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal) ||
                 referenced.Name == HostAssemblyName))
            .Select(referenced => referenced.Name)
            .ToList();

        Assert.True(
            forbiddenReferences.Count == 0,
            $"{SharedKernelAssemblyName} must stay independent of modules and the Host, " +
            $"but references: {string.Join(", ", forbiddenReferences)}");
    }

    [Theory]
    [MemberData(nameof(AllModuleAssemblies))]
    public void Module_must_not_reference_a_concrete_database_provider(Assembly moduleAssembly)
    {
        var providerReferences = moduleAssembly.GetReferencedAssemblies()
            .Where(referenced =>
                referenced.Name is not null &&
                ConcreteDatabaseProviderAssemblyNames.Contains(referenced.Name, StringComparer.Ordinal))
            .Select(referenced => referenced.Name)
            .ToList();

        Assert.True(
            providerReferences.Count == 0,
            $"{moduleAssembly.GetName().Name} must not reference a concrete database provider directly " +
            $"(only PTS.Host may — see ADR-0004), but references: {string.Join(", ", providerReferences)}");
    }

    [Fact]
    public void Identity_and_tenancy_must_not_reference_jwt_bearer()
    {
        var forbidden = "Microsoft.AspNetCore.Authentication.JwtBearer";
        var identity = typeof(PTS.Modules.Identity.ModuleAssemblyMarker).Assembly;
        var tenancy = typeof(PTS.Modules.Tenancy.ModuleAssemblyMarker).Assembly;

        foreach (var assembly in new[] { identity, tenancy, SharedKernelAssembly })
        {
            var names = assembly.GetReferencedAssemblies().Select(a => a.Name).ToHashSet();
            Assert.False(
                names.Contains(forbidden),
                $"{assembly.GetName().Name} must not reference {forbidden}; JWT wiring belongs in PTS.Host.");
        }
    }

    [Fact]
    public void WorkManagement_must_not_reference_tenancy_identity_or_host()
    {
        var workManagement = typeof(PTS.Modules.WorkManagement.ModuleAssemblyMarker).Assembly;
        var names = workManagement.GetReferencedAssemblies().Select(a => a.Name).ToHashSet();

        Assert.DoesNotContain(names, n => n is "PTS.Modules.Tenancy" or "PTS.Modules.Identity" or "PTS.Host" or "Npgsql" or "Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Fact]
    public void Tenancy_must_not_reference_work_management()
    {
        var tenancy = typeof(PTS.Modules.Tenancy.ModuleAssemblyMarker).Assembly;
        var names = tenancy.GetReferencedAssemblies().Select(a => a.Name).ToHashSet();
        Assert.DoesNotContain("PTS.Modules.WorkManagement", names);
    }

    [Fact]
    public void Host_must_compose_every_module()
    {
        var hostAssembly = typeof(PTS.Host.HostAssemblyMarker).Assembly;
        var referencedNames = hostAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToHashSet();

        var missingModules = ModuleAssemblies
            .Select(a => a.GetName().Name)
            .Where(name => name is not null && !referencedNames.Contains(name))
            .ToList();

        Assert.True(
            missingModules.Count == 0,
            $"{HostAssemblyName} must reference every module, but is missing: {string.Join(", ", missingModules)}");
    }

    [Fact]
    public void User_must_not_carry_admin_or_role_properties()
    {
        var names = typeof(PTS.Modules.Identity.User).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("IsAdmin", names);
        Assert.DoesNotContain("IsPlatformAdministrator", names);
        Assert.DoesNotContain("Role", names);
        Assert.DoesNotContain("TenantId", names);
    }
}
