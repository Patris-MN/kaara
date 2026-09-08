namespace PTS.Modules.Identity;

public sealed class InvalidCurrentPasswordException : Exception
{
    public InvalidCurrentPasswordException()
        : base("The current password is incorrect.")
    {
    }
}
