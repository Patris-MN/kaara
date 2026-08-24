namespace PTS.Modules.Tenancy;

public sealed class DuplicateSlugException : Exception
{
    public DuplicateSlugException(string slug)
        : base($"Tenant slug '{slug}' is already taken.")
    {
        Slug = slug;
    }

    public string Slug { get; }
}
