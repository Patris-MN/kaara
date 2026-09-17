namespace PTS.Host.Persistence;

/// <summary>
/// Loads git-ignored <c>infra/docker/.env</c> into the process environment for
/// local Development. Existing variables are left alone so a shell or CI
/// export still wins. Secrets never go in appsettings.
/// </summary>
internal static class LocalDevelopmentEnvironment
{
    public static void ApplyDotEnvIfPresent()
    {
        var envFile = FindDotEnv();
        if (envFile is null)
        {
            return;
        }

        foreach (var raw in File.ReadLines(envFile))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 1)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (name.Length == 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static string? FindDotEnv()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "PTS.slnx")))
                {
                    var envFile = Path.Combine(dir.FullName, "infra", "docker", ".env");
                    return File.Exists(envFile) ? envFile : null;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }
}
