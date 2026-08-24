namespace PTS.Modules.Identity;

public sealed class DuplicateEmailException : Exception
{
    public string Email { get; }

    public DuplicateEmailException(string email)
        : base($"A user with email '{email}' already exists.")
    {
        Email = email;
    }
}
