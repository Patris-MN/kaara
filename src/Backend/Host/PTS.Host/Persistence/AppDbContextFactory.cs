using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PTS.Host.Persistence;

/// <summary>
/// Used exclusively by `dotnet ef migrations add` / `dotnet ef database
/// update` tooling. Deliberately bypasses Program.cs entirely and connects as
/// <c>migrator_role</c> (see LocalPostgresConnectionStrings.BuildMigratorConnectionString) —
/// this is the only place in the whole codebase that role is used. The
/// running application (Program.cs / PersistenceExtensions) always connects
/// as app_role instead; see docs/architecture/architecture-charter.md §5.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(LocalPostgresConnectionStrings.BuildMigratorConnectionString());
        return new AppDbContext(optionsBuilder.Options);
    }
}
